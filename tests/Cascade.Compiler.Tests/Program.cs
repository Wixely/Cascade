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
    ("typed IR propagates values and reports pressure", TypedIrOptimization),
    ("saved masks do not replay memory reads", SavedMaskSnapshot),
    ("immutable bindings snapshot mutable variables", ImmutableSnapshot),
    ("register reuse preserves earlier operands", OperandLifetime),
    ("helper names do not collide with builtin prefixes", BuiltinPrefixes),
    ("generated kernels match scalar and unoptimized oracles", CompilerDifferential.Run),
    ("constant selection preserves literal type context", LiteralContext),
    ("compiler bounds source and expansion work", CompilerLimits),
    ("common expressions respect mutation and loop boundaries", CommonExpressions),
    ("virtual registers spill beyond byte indices deterministically", CompilerSpillChecks.VectorPressure),
    ("spill snapshots preserve masks, effects and gather pairs", CompilerSpillChecks.MixedPressure),
    ("mask-only spills retain i32 and i64 widths and aliases", CompilerSpillChecks.MaskPressure),
    ("i64 spills preserve inactive gather lanes and sparse writes", CompilerSpillChecks.I64MemoryPressure),
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
    Equal(3, Compile(identicalMasks).InstructionCount);

    const string doubleNegation = "kernel clean(vector<i32> input) -> vector<i32> { return select(mask_not(mask_not(input < 8)), input, 0); }";
    Equal(6, Compile(doubleNegation).InstructionCount);

    const string trappingBinding = "kernel keep(vector<i64> address) -> vector<i64> { let unused = load_i64(address); return address; }";
    var trapped = Compile(trappingBinding).Instantiate(new VmOptions { MemorySize = 0 }).Execute();
    if (trapped.Succeeded) throw new InvalidOperationException("Unused trapping expression was removed.");

    const string eagerSelect = "kernel keep(vector<i64> address) -> vector<i64> { return select(mask_all_i64(), address, load_i64(address)); }";
    trapped = Compile(eagerSelect).Instantiate(new VmOptions { MemorySize = 0 }).Execute();
    if (trapped.Succeeded) throw new InvalidOperationException("An unselected trapping expression was removed.");
}

static void TypedIrOptimization()
{
    const string source = """
        kernel simplify(vector<i32> input) -> vector<i32> {
            let low = input < 8;
            let high = mask_not(low);
            let every = mask_or(low, high);
            let stillEvery = mask_and(every, mask_all_i32());
            let none = mask_xor(stillEvery, every);
            return select(mask_not(none), input, 0);
        }
        """;
    var result = CascadeCompiler.Compile(source);
    if (!result.Succeeded) throw new InvalidOperationException(string.Join(Environment.NewLine, result.Diagnostics));
    var metrics = result.Metrics ?? throw new InvalidOperationException("Compilation metrics were not reported.");
    Equal(1, metrics.InstructionCount);
    Equal(1, metrics.PeakVectorRegisters);
    Equal(0, metrics.PeakMaskRegisters);
}

static CascadeProgram Compile(string source)
{
    var result = CascadeCompiler.Compile(source);
    if (!result.Succeeded) throw new InvalidOperationException(string.Join(Environment.NewLine, result.Diagnostics));
    return CascadeProgram.FromModule(result.Module!);
}

static void SavedMaskSnapshot()
{
    const string source = """
        kernel saved(vector<i64> address, vector<i32> input) -> vector<i32> {
            let inverted = mask_not(load_i32(address) < 8);
            store(address, input);
            return select(mask_not(inverted), input, 0);
        }
        """;
    var instance = Compile(source).Instantiate(new VmOptions { MemorySize = 64 });
    for (var lane = 0; lane < 16; lane++) instance.State.Register(1).WriteUInt32(lane, 100);
    Success(instance.Execute());
    for (var lane = 0; lane < 16; lane++) Equal(100U, instance.State.Register(0).ReadUInt32(lane));
}

static void ImmutableSnapshot()
{
    const string source = """
        kernel saved(vector<i32> input) -> vector<i32> {
            var current = input;
            let saved = current;
            current = current + 10;
            return saved;
        }
        """;
    var instance = Compile(source).Instantiate(new VmOptions { MemorySize = 0 });
    Success(instance.Execute());
    Equal(0U, instance.State.Register(0).ReadUInt32(0));
}

static void OperandLifetime()
{
    foreach (var expression in new[] { "x + (x + 1)", "x + (x + 1) * 2" })
    {
        var instance = Compile($"kernel nested(vector<i32> input) -> vector<i32> {{ let x = input + 3; return {expression}; }}")
            .Instantiate(new VmOptions { MemorySize = 0 });
        Success(instance.Execute());
        Equal(expression.Contains('*') ? 11U : 7U, instance.State.Register(0).ReadUInt32(0));
    }
}

static void BuiltinPrefixes()
{
    const string source = """
        fn mask_all_custom(vector<i32> input) -> vector<i32> { return input + 9; }
        kernel names(vector<i32> input) -> vector<i32> { return mask_all_custom(input); }
        """;
    var instance = Compile(source).Instantiate(new VmOptions { MemorySize = 0 });
    Success(instance.Execute());
    Equal(9U, instance.State.Register(0).ReadUInt32(0));
}

static void LiteralContext()
{
    foreach (var literal in new[] { "7", "(7 + 8)", "(7 + 8) * 2" })
    {
        var source = $"kernel literal(vector<i32> input) -> vector<i32> {{ let value = select(mask_none_i32(), input, {literal}); return value; }}";
        var instance = Compile(source).Instantiate(new VmOptions { MemorySize = 0 });
        Success(instance.Execute());
        Equal(literal == "7" ? 7U : literal.Contains('*') ? 30U : 15U, instance.State.Register(0).ReadUInt32(0));
    }
}

static void CompilerLimits()
{
    Diagnostic(new string(' ', 1_000_001), "CAS007");
    Diagnostic("kernel deep(vector<i32> x) -> vector<i32> { return " + new string('(', 256) + "x" + new string(')', 256) + "; }", "CAS006");
    Diagnostic("kernel deep(vector<i32> x) -> vector<i32> { return x" + string.Concat(Enumerable.Repeat(" + 1", 256)) + "; }", "CAS006");
    Diagnostic("kernel large(vector<i32> x) -> vector<i32> { repeat 1024 { repeat 1024 { repeat 1024 { } } } return x; }", "CAS204");
    var additions = string.Concat(Enumerable.Repeat(" + 1", 40));
    var functions = string.Join(Environment.NewLine, Enumerable.Range(0, 4).Select(index =>
        $"fn depth{index}(vector<i32> x) -> vector<i32> {{ return {(index == 3 ? "x" : $"depth{index + 1}(x)")}{additions}; }}"));
    Diagnostic(functions + " kernel deep(vector<i32> x) -> vector<i32> { return depth0(x); }", "CAS204");
}

static void CommonExpressions()
{
    const string shared = """
        kernel shared(vector<i32> input, vector<i32> other) -> vector<i32> {
            let first = (input + other) * 3;
            let second = (input + other) * 3;
            return first + second;
        }
        """;
    var optimized = CascadeCompiler.Compile(shared);
    var baseline = CascadeCompiler.Compile(shared, new CompilationOptions { Optimize = false });
    if (!optimized.Succeeded || !baseline.Succeeded || optimized.Metrics!.InstructionCount >= baseline.Metrics!.InstructionCount)
        throw new InvalidOperationException("Common expression reuse did not reduce emitted instructions.");
    var instance = CascadeProgram.FromModule(optimized.Module!).Instantiate(new VmOptions { MemorySize = 0 });
    instance.State.Register(0).WriteUInt32(0, 2);
    instance.State.Register(1).WriteUInt32(0, 4);
    Success(instance.Execute());
    Equal(36U, instance.State.Register(0).ReadUInt32(0));

    const string loop = """
        kernel mutation(vector<i32> input) -> vector<i32> {
            var current = input;
            let before = current + 1;
            repeat 3 {
                repeat 2 {
                    let during = current + 1;
                    current = during;
                }
            }
            let after = current + 1;
            return before + after;
        }
        """;
    foreach (var optimize in new[] { false, true })
    {
        var compilation = CascadeCompiler.Compile(loop, new CompilationOptions { Optimize = optimize });
        if (!compilation.Succeeded) throw new InvalidOperationException(string.Join(", ", compilation.Diagnostics));
        instance = CascadeProgram.FromModule(compilation.Module!).Instantiate(new VmOptions { MemorySize = 0 });
        Success(instance.Execute());
        Equal(8U, instance.State.Register(0).ReadUInt32(0));
    }
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
