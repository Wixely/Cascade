using System.Buffers.Binary;
using Cascade.Compiler;
using Cascade.Core;
using Cascade.Runtime;

var demos = new Dictionary<string, (string Description, Action Run)>(StringComparer.OrdinalIgnoreCase)
{
    ["arithmetic"] = ("Lane-wise vector arithmetic, including wrapping overflow", RunArithmetic),
    ["masks"] = ("Predicate comparison and masked selection", RunMasks),
    ["memory"] = ("Deterministic scatter and gather through linear memory", RunMemory),
    ["capability"] = ("An explicitly declared and bound host capability", RunCapability),
    ["source"] = ("Compile functions, bounded repetition, and typed memory from source", RunSourceKernel),
};

if (args is ["list"])
{
    PrintAvailableDemos(demos);
    return 0;
}

var selectedNames = args.Length == 0 || args is ["all"] ? demos.Keys.ToArray() : args;
foreach (var name in selectedNames)
{
    if (!demos.TryGetValue(name, out var demo))
    {
        Console.Error.WriteLine($"Unknown demo '{name}'.");
        PrintAvailableDemos(demos);
        return 2;
    }

    Console.WriteLine($"=== {name}: {demo.Description} ===");
    try
    {
        demo.Run();
    }
    catch (Exception exception)
    {
        Console.Error.WriteLine($"FAIL {name}: {exception.Message}");
        return 1;
    }
    Console.WriteLine();
}

Console.WriteLine($"PASS {selectedNames.Length} demo(s)");
return 0;

static void RunArithmetic()
{
    int[] left = [int.MaxValue, -20, 7, 9, 12, -4, 100, 3, 1, 2, 3, 4, 5, 6, 7, 8];
    int[] right = [1, 5, 6, -2, 3, 10, -2, 11, 8, 7, 6, 5, 4, 3, 2, 1];
    var module = new CascadeModule(
    [
        new(Opcode.LoadConstant, Destination: 0, Immediate: 0),
        new(Opcode.LoadConstant, Destination: 1, Immediate: 1),
        new(Opcode.AddI32, Destination: 2, SourceA: 0, SourceB: 1),
        new(Opcode.MulI32, Destination: 3, SourceA: 0, SourceB: 1),
        new(Opcode.Halt),
    ],
    [I32Constant(left), I32Constant(right)]);

    var instance = Execute(module);
    var sums = ReadI32(instance.State.Register(2));
    var products = ReadI32(instance.State.Register(3));
    for (var lane = 0; lane < 16; lane++)
    {
        Equal(unchecked(left[lane] + right[lane]), sums[lane], $"sum lane {lane}");
        Equal(unchecked(left[lane] * right[lane]), products[lane], $"product lane {lane}");
    }

    Console.WriteLine($"left     {Format(left)}");
    Console.WriteLine($"right    {Format(right)}");
    Console.WriteLine($"add.i32  {Format(sums)}");
    Console.WriteLine($"mul.i32  {Format(products)}");
    Console.WriteLine("Lane 0 demonstrates defined wrapping: int.MaxValue + 1 = int.MinValue.");
}

static void RunMasks()
{
    int[] readings = [-8, 14, -1, 20, 0, 3, -40, 9, 12, -2, 7, 1, -6, 18, 4, -3];
    var module = new CascadeModule(
    [
        new(Opcode.LoadConstant, Destination: 0, Immediate: 0),
        new(Opcode.SplatI32, Destination: 1, Immediate: 0),
        new(Opcode.CompareLessThanI32, SourceA: 0, SourceB: 1, Mask: 0),
        new(Opcode.SelectI32, Destination: 2, SourceA: 1, SourceB: 0, Mask: 0),
        new(Opcode.Halt),
    ],
    [I32Constant(readings)]);

    var instance = Execute(module);
    var clamped = ReadI32(instance.State.Register(2));
    for (var lane = 0; lane < 16; lane++) Equal(Math.Max(0, readings[lane]), clamped[lane], $"clamped lane {lane}");

    Console.WriteLine($"input     {Format(readings)}");
    Console.WriteLine($"negative  0x{instance.State.Mask(0).ActiveBits:X4}");
    Console.WriteLine($"selected  {Format(clamped)}");
}

static void RunMemory()
{
    ulong[] addresses = [56, 0, 40, 16, 48, 8, 32, 24];
    ulong[] values = [107, 100, 105, 102, 106, 101, 104, 103];
    var module = new CascadeModule(
    [
        new(Opcode.LoadConstant, Destination: 0, Immediate: 0),
        new(Opcode.LoadConstant, Destination: 1, Immediate: 1),
        new(Opcode.CompareEqualI64, SourceA: 2, SourceB: 3, Mask: 0),
        new(Opcode.ScatterI64, SourceA: 0, SourceB: 1, Mask: 0),
        new(Opcode.GatherI64, Destination: 4, SourceA: 0, Mask: 0),
        new(Opcode.Halt),
    ],
    [I64Constant(addresses), I64Constant(values)]);

    var instance = Execute(module, new VmOptions { MemorySize = 64 });
    var gathered = ReadI64(instance.State.Register(4));
    for (var lane = 0; lane < 8; lane++) Equal(values[lane], gathered[lane], $"gathered lane {lane}");

    var memoryOrder = Enumerable.Range(0, 8)
        .Select(lane => BinaryPrimitives.ReadUInt64LittleEndian(instance.State.LinearMemory.Span.Slice(lane * 8, 8)))
        .ToArray();
    Console.WriteLine($"addresses  {Format(addresses)}");
    Console.WriteLine($"values     {Format(values)}");
    Console.WriteLine($"gathered   {Format(gathered)}");
    Console.WriteLine($"memory     {Format(memoryOrder)}");
}

static void RunCapability()
{
    var import = new ImportDefinition("demo", "sum.i64", 1, 8);
    ulong[] values = [3, 5, 8, 13, 21, 34, 55, 89];
    var module = new CascadeModule(
    [
        new(Opcode.LoadConstant, Destination: 0, Immediate: 0),
        new(Opcode.HostCall, Immediate: 0),
        new(Opcode.Halt),
    ],
    [I64Constant(values)],
    imports: [import]);

    var key = new ImportKey(import.Namespace, import.Name, import.AbiVersion);
    var options = new VmOptions
    {
        Fuel = 32,
        ImportBindings = new Dictionary<ImportKey, HostImportHandler>
        {
            [key] = SumI64,
        },
    };
    var instance = Execute(module, options);
    var expected = values.Aggregate(0UL, static (sum, value) => unchecked(sum + value));
    Equal(expected, instance.State.Register(0).ReadUInt64(0), "host capability result");

    Console.WriteLine($"input     {Format(values)}");
    Console.WriteLine($"sum       {expected}");
    Console.WriteLine($"fuel used {instance.State.InitialFuel - instance.State.FuelRemaining} (includes declared import surcharge)");
}

static void RunSourceKernel()
{
    var sourcePath = Path.Combine(AppContext.BaseDirectory, "memory-transform.cascade");
    var compilation = CascadeCompiler.Compile(File.ReadAllText(sourcePath));
    if (!compilation.Succeeded)
        throw new InvalidOperationException(string.Join(Environment.NewLine, compilation.Diagnostics));

    var program = CascadeProgram.FromModule(compilation.Module!);
    var instance = program.Instantiate(new VmOptions { MemorySize = 64 });
    instance.State.Register(0).WriteUInt64(0, 0);
    for (var lane = 0; lane < 16; lane++) instance.State.Register(1).WriteUInt32(lane, (uint)lane);
    var result = instance.Execute();
    if (!result.Succeeded) throw new InvalidOperationException($"VM trapped: {result.Trap!.Code} at instruction {result.Trap.InstructionPointer}");

    var output = ReadI32(instance.State.Register(0));
    for (var lane = 0; lane < 16; lane++) Equal(lane + 15, output[lane], $"compiled lane {lane}");
    Console.WriteLine($"compiled  {Path.GetFileName(sourcePath)} ({program.InstructionCount} instructions)");
    Console.WriteLine($"output    {Format(output)}");
    Console.WriteLine("The kernel called a helper three times, stored the vector, and loaded it back.");
}

static HostCallStatus SumI64(HostCallContext context)
{
    var register = context.Register(0);
    var sum = 0UL;
    for (var lane = 0; lane < 8; lane++) sum = unchecked(sum + register.ReadUInt64(lane));
    register.Clear();
    register.WriteUInt64(0, sum);
    return HostCallStatus.Success;
}

static CascadeInstance Execute(CascadeModule module, VmOptions? options = null)
{
    var instance = CascadeProgram.FromModule(module).Instantiate(options);
    var result = instance.Execute();
    if (!result.Succeeded) throw new InvalidOperationException($"VM trapped: {result.Trap!.Code} at instruction {result.Trap.InstructionPointer}");
    return instance;
}

static VectorConstant I32Constant(IReadOnlyList<int> values)
{
    if (values.Count != 16) throw new ArgumentException("An i32 vector requires 16 lanes.", nameof(values));
    var bytes = new byte[VectorRegister.ByteCount];
    for (var lane = 0; lane < values.Count; lane++) BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(lane * 4, 4), values[lane]);
    return new(bytes);
}

static VectorConstant I64Constant(IReadOnlyList<ulong> values)
{
    if (values.Count != 8) throw new ArgumentException("An i64 vector requires 8 lanes.", nameof(values));
    var bytes = new byte[VectorRegister.ByteCount];
    for (var lane = 0; lane < values.Count; lane++) BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(lane * 8, 8), values[lane]);
    return new(bytes);
}

static int[] ReadI32(VectorRegister register) => Enumerable.Range(0, 16).Select(register.ReadInt32).ToArray();
static ulong[] ReadI64(VectorRegister register) => Enumerable.Range(0, 8).Select(register.ReadUInt64).ToArray();
static string Format<T>(IEnumerable<T> values) => $"[{string.Join(", ", values)}]";

static void Equal<T>(T expected, T actual, string label) where T : IEquatable<T>
{
    if (!expected.Equals(actual)) throw new InvalidOperationException($"{label}: expected {expected}, got {actual}.");
}

static void PrintAvailableDemos(IReadOnlyDictionary<string, (string Description, Action Run)> demos)
{
    Console.WriteLine("Usage: dotnet run --project examples/Cascade.Demos -- [all|list|demo ...]");
    Console.WriteLine("Available demos:");
    foreach (var (name, demo) in demos) Console.WriteLine($"  {name,-12} {demo.Description}");
}
