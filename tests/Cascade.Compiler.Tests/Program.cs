using Cascade.Compiler;
using Cascade.Runtime;

var tests = new (string Name, Action Body)[]
{
    ("typed expressions compile and execute", TypedExpressions),
    ("i64 expressions compile and execute", I64Expressions),
    ("compilation is deterministic", DeterministicCompilation),
    ("invalid programs produce source diagnostics", InvalidPrograms),
    ("functions and bounded repeats execute", FunctionsAndRepeats),
    ("typed memory operations execute", TypedMemory),
    ("version 0.2 safety rules reject invalid source", VersionTwoDiagnostics),
    ("typed gather and scatter execute", GatherAndScatter),
    ("mask combinators preserve lane semantics", MaskCombinators),
    ("last-use release handles long expression chains", LastUseRelease),
    ("dead bindings and mask identities are optimized", CompilerOptimizations),
};

var failures = 0;
foreach (var (name, body) in tests)
{
    try
    {
        body();
        Console.WriteLine($"PASS {name}");
    }
    catch (Exception exception)
    {
        failures++;
        Console.Error.WriteLine($"FAIL {name}: {exception.Message}");
    }
}

Console.WriteLine($"{tests.Length - failures}/{tests.Length} compiler cases passed");
return failures == 0 ? 0 : 1;

static void TypedExpressions()
{
    const string source = """
        kernel choose(vector<i32> value, vector<i32> increment) -> vector<i32> {
            let adjusted = value + increment * 2;
            return select(adjusted < 10, adjusted, 10);
        }
        """;
    var instance = Compile(source).Instantiate(new VmOptions { MemorySize = 0 });
    for (var lane = 0; lane < 16; lane++)
    {
        instance.State.Register(0).WriteUInt32(lane, (uint)lane);
        instance.State.Register(1).WriteUInt32(lane, 2);
    }

    Success(instance.Execute());
    for (var lane = 0; lane < 16; lane++)
        Equal((uint)Math.Min(lane + 4, 10), instance.State.Register(0).ReadUInt32(lane));
}

static void I64Expressions()
{
    const string source = """
        kernel offset(vector<i64> value) -> vector<i64> {
            return (value - 2) ^ 1;
        }
        """;
    var instance = Compile(source).Instantiate(new VmOptions { MemorySize = 0 });
    for (var lane = 0; lane < 8; lane++) instance.State.Register(0).WriteUInt64(lane, (ulong)(lane + 10));
    Success(instance.Execute());
    for (var lane = 0; lane < 8; lane++) Equal(((ulong)(lane + 10) - 2) ^ 1UL, instance.State.Register(0).ReadUInt64(lane));
}

static void DeterministicCompilation()
{
    const string source = "kernel add(vector<i32> a, vector<i32> b) -> vector<i32> { return a + b; }";
    using var first = new MemoryStream();
    using var second = new MemoryStream();
    Compile(source).Save(first);
    Compile(source).Save(second);
    if (!first.ToArray().AsSpan().SequenceEqual(second.ToArray())) throw new InvalidOperationException("Compiler output differs between identical compilations.");
}

static void InvalidPrograms()
{
    Diagnostic("kernel bad(vector<i32> value) -> vector<i64> { return value; }", "CAS103");
    Diagnostic("kernel bad(vector<i32> value) -> vector<i32> { return missing; }", "CAS105");
    Diagnostic("kernel bad(vector<i64> value) -> vector<i64> { return value * 2; }", "CAS112");
    Diagnostic("kernel bad(vector<i32> value) -> vector<i32> { let x = value + 1 return x; }", "CAS002");
}

static void FunctionsAndRepeats()
{
    const string source = """
        fn increment(vector<i32> value, vector<i32> amount) -> vector<i32> {
            return value + amount;
        }

        kernel accumulate(vector<i32> input) -> vector<i32> {
            var result = input;
            repeat 4 {
                result = increment(result, 3);
            }
            return result;
        }
        """;
    var instance = Compile(source).Instantiate(new VmOptions { MemorySize = 0 });
    for (var lane = 0; lane < 16; lane++) instance.State.Register(0).WriteUInt32(lane, (uint)lane);
    Success(instance.Execute());
    for (var lane = 0; lane < 16; lane++) Equal((uint)(lane + 12), instance.State.Register(0).ReadUInt32(lane));
}

static void TypedMemory()
{
    const string i32Source = """
        kernel roundtrip(vector<i64> address, vector<i32> input) -> vector<i32> {
            store(address, input);
            return load_i32(address);
        }
        """;
    var instance = Compile(i32Source).Instantiate(new VmOptions { MemorySize = 64 });
    instance.State.Register(0).WriteUInt64(0, 0);
    for (var lane = 0; lane < 16; lane++) instance.State.Register(1).WriteUInt32(lane, (uint)(lane * 7));
    Success(instance.Execute());
    for (var lane = 0; lane < 16; lane++) Equal((uint)(lane * 7), instance.State.Register(0).ReadUInt32(lane));

    const string i64Source = """
        kernel roundtrip(vector<i64> address, vector<i64> input) -> vector<i64> {
            store(address, input);
            return load_i64(address);
        }
        """;
    instance = Compile(i64Source).Instantiate(new VmOptions { MemorySize = 64 });
    instance.State.Register(0).WriteUInt64(0, 0);
    for (var lane = 0; lane < 8; lane++) instance.State.Register(1).WriteUInt64(lane, (ulong)(lane * 11));
    Success(instance.Execute());
    for (var lane = 0; lane < 8; lane++) Equal((ulong)(lane * 11), instance.State.Register(0).ReadUInt64(lane));
}

static void VersionTwoDiagnostics()
{
    Diagnostic("kernel bad(vector<i32> value) -> vector<i32> { repeat 0 { } return value; }", "CAS005");
    Diagnostic("kernel bad(vector<i32> value) -> vector<i32> { value = value + 1; return value; }", "CAS116");
    Diagnostic("kernel bad(vector<i32> address) -> vector<i32> { return load_i32(address); }", "CAS118");
    Diagnostic("""
        fn recurse(vector<i32> value) -> vector<i32> { return recurse(value); }
        kernel bad(vector<i32> value) -> vector<i32> { return recurse(value); }
        """, "CAS125");
}

static void GatherAndScatter()
{
    const string i32Source = """
        kernel roundtrip(
            vector<i32> values,
            vector<i64> addressLow,
            vector<i64> addressHigh
        ) -> vector<i32> {
            let active = values < 108;
            scatter_i32(addressLow, addressHigh, values, active);
            return gather_i32(addressLow, addressHigh, active);
        }
        """;
    var instance = Compile(i32Source).Instantiate(new VmOptions { MemorySize = 64 });
    for (var lane = 0; lane < 16; lane++) instance.State.Register(0).WriteUInt32(lane, (uint)(100 + lane));
    for (var lane = 0; lane < 8; lane++)
    {
        instance.State.Register(1).WriteUInt64(lane, (ulong)(lane * 4));
        instance.State.Register(2).WriteUInt64(lane, (ulong)((lane + 8) * 4));
    }
    Success(instance.Execute());
    for (var lane = 0; lane < 16; lane++)
        Equal(lane < 8 ? (uint)(100 + lane) : 0U, instance.State.Register(0).ReadUInt32(lane));

    const string i64Source = """
        kernel roundtrip(vector<i64> addresses, vector<i64> values) -> vector<i64> {
            let active = mask_or(mask_all_i64(), mask_none_i64());
            scatter_i64(addresses, values, active);
            return gather_i64(addresses, active);
        }
        """;
    instance = Compile(i64Source).Instantiate(new VmOptions { MemorySize = 64 });
    for (var lane = 0; lane < 8; lane++)
    {
        instance.State.Register(0).WriteUInt64(lane, (ulong)(lane * 8));
        instance.State.Register(1).WriteUInt64(lane, (ulong)(200 + lane));
    }
    Success(instance.Execute());
    for (var lane = 0; lane < 8; lane++) Equal((ulong)(200 + lane), instance.State.Register(0).ReadUInt64(lane));
}

static void MaskCombinators()
{
    const string source = """
        kernel masks(vector<i32> input) -> vector<i32> {
            let low = input < 8;
            let high = mask_not(low);
            let all = mask_or(low, high);
            let stillAll = mask_and(all, mask_all_i32());
            let none = mask_xor(stillAll, all);
            let keep = mask_or(mask_not(none), mask_none_i32());
            return select(keep, input, 0);
        }
        """;
    var instance = Compile(source).Instantiate(new VmOptions { MemorySize = 0 });
    for (var lane = 0; lane < 16; lane++) instance.State.Register(0).WriteUInt32(lane, (uint)lane);
    Success(instance.Execute());
    for (var lane = 0; lane < 16; lane++) Equal((uint)lane, instance.State.Register(0).ReadUInt32(lane));

    Diagnostic("kernel bad(vector<i64> a) -> vector<i64> { return select(mask_all_i32(), a, 0); }", "CAS110");
    Diagnostic("kernel bad(vector<i64> a) -> vector<i64> { scatter_i64(a, a, mask_all_i32()); return a; }", "CAS128");
}

static void LastUseRelease()
{
    var statements = string.Join(Environment.NewLine, Enumerable.Range(1, 40)
        .Select(index => $"let value{index} = value{index - 1} + 1;"));
    var source = $$"""
        kernel chain(vector<i32> value0) -> vector<i32> {
            {{statements}}
            return value40;
        }
        """;
    var instance = Compile(source).Instantiate(new VmOptions { MemorySize = 0 });
    Success(instance.Execute());
    for (var lane = 0; lane < 16; lane++) Equal(40U, instance.State.Register(0).ReadUInt32(lane));
}

static void CompilerOptimizations()
{
    const string deadBinding = "kernel clean(vector<i32> input) -> vector<i32> { let unused = input + 123; return input; }";
    Equal(1, Compile(deadBinding).InstructionCount);

    const string neutralMask = "kernel clean(vector<i32> input) -> vector<i32> { return select(mask_and(input < 8, mask_all_i32()), input, 0); }";
    Equal(6, Compile(neutralMask).InstructionCount);

    const string identicalMasks = "kernel clean(vector<i32> input) -> vector<i32> { let active = input < 8; return select(mask_xor(active, active), input, 0); }";
    Equal(8, Compile(identicalMasks).InstructionCount);

    const string doubleNegation = "kernel clean(vector<i32> input) -> vector<i32> { return select(mask_not(mask_not(input < 8)), input, 0); }";
    Equal(6, Compile(doubleNegation).InstructionCount);

    const string trappingBinding = "kernel keep(vector<i64> address) -> vector<i64> { let unused = load_i64(address); return address; }";
    var trapped = Compile(trappingBinding).Instantiate(new VmOptions { MemorySize = 0 }).Execute();
    if (trapped.Succeeded) throw new InvalidOperationException("Unused trapping expression was removed.");
}

static CascadeProgram Compile(string source)
{
    var result = CascadeCompiler.Compile(source);
    if (!result.Succeeded) throw new InvalidOperationException(string.Join(Environment.NewLine, result.Diagnostics));
    return CascadeProgram.FromModule(result.Module!);
}

static void Diagnostic(string source, string expectedCode)
{
    var result = CascadeCompiler.Compile(source);
    if (result.Succeeded) throw new InvalidOperationException("Invalid source compiled successfully.");
    Equal(expectedCode, result.Diagnostics.Single().Code);
    if (result.Diagnostics[0].Line < 1 || result.Diagnostics[0].Column < 1)
        throw new InvalidOperationException("Diagnostic has no source position.");
}

static void Success(ExecutionResult result)
{
    if (!result.Succeeded) throw new InvalidOperationException(result.Trap?.Message ?? "Execution failed.");
}

static void Equal<T>(T expected, T actual) where T : IEquatable<T>
{
    if (!expected.Equals(actual)) throw new InvalidOperationException($"Expected {expected}; actual {actual}.");
}
