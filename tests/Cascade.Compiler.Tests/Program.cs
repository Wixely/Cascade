using Cascade.Compiler;
using Cascade.Runtime;

var tests = new (string Name, Action Body)[]
{
    ("typed expressions compile and execute", TypedExpressions),
    ("i64 expressions compile and execute", I64Expressions),
    ("compilation is deterministic", DeterministicCompilation),
    ("invalid programs produce source diagnostics", InvalidPrograms),
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
