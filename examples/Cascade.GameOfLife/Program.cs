using System.Diagnostics;
using System.Security.Cryptography;
using Cascade.Core;
using Cascade.Runtime;

if (args is ["--write-module", var outputPath])
{
    using var output = File.Create(outputPath);
    CascadeProgram.FromModule(BuildLifeRuleModule()).Save(output);
    Console.WriteLine($"Wrote Cascade Game of Life kernel to {outputPath}");
    return 0;
}

if (args.Contains("--help", StringComparer.OrdinalIgnoreCase))
{
    PrintUsage();
    return 0;
}

DemoOptions options;
try
{
    options = ParseOptions(args);
}
catch (ArgumentException exception)
{
    Console.Error.WriteLine(exception.Message);
    Console.Error.WriteLine();
    PrintUsage();
    return 2;
}

var modulePath = Path.Combine(AppContext.BaseDirectory, "life-rule.cvm");
var expectedModule = Encode(BuildLifeRuleModule());
var storedModule = File.ReadAllBytes(modulePath);
if (!storedModule.SequenceEqual(expectedModule))
{
    Console.Error.WriteLine("life-rule.cvm does not match the readable instruction definition in Program.cs.");
    return 1;
}

using var input = new MemoryStream(storedModule, writable: false);
var program = CascadeProgram.Load(input);
var seed = options.Benchmark ? CreateBenchmarkSeed(options.Width, options.Height) : CreateShowcaseSeed(options.Width, options.Height);

_ = Simulate(program, seed, options.Width, options.Height, 1, ExecutionBackend.Reference);
if (SimdInterpreter.IsSupported)
    _ = Simulate(program, seed, options.Width, options.Height, 1, ExecutionBackend.HardwareSimd);
_ = SimulateScalar(seed, options.Width, options.Height, 1);

var referenceTimer = Stopwatch.StartNew();
var reference = Simulate(program, seed, options.Width, options.Height, options.Generations, ExecutionBackend.Reference);
referenceTimer.Stop();

var result = reference;
TimeSpan? simdElapsed = null;
if (SimdInterpreter.IsSupported)
{
    var simdTimer = Stopwatch.StartNew();
    result = Simulate(program, seed, options.Width, options.Height, options.Generations, ExecutionBackend.HardwareSimd);
    simdTimer.Stop();
    simdElapsed = simdTimer.Elapsed;
    if (!result.SequenceEqual(reference))
    {
        Console.Error.WriteLine("Hardware SIMD result differs from the reference VM.");
        return 1;
    }
}

var scalarTimer = Stopwatch.StartNew();
var scalar = SimulateScalar(seed, options.Width, options.Height, options.Generations);
scalarTimer.Stop();
if (!result.SequenceEqual(scalar))
{
    Console.Error.WriteLine("Cascade result differs from the scalar Life rules.");
    return 1;
}

if (options.VerifyReplay)
{
    var replayBackend = SimdInterpreter.IsSupported ? ExecutionBackend.HardwareSimd : ExecutionBackend.Reference;
    var replay = Simulate(program, seed, options.Width, options.Height, options.Generations, replayBackend);
    if (!result.SequenceEqual(replay))
    {
        Console.Error.WriteLine("Deterministic replay failed.");
        return 1;
    }
}

var cellUpdates = checked((long)options.Width * options.Height * options.Generations);
Console.WriteLine($"Cascade Game of Life: {options.Width}x{options.Height}, {options.Generations} generations");
Console.WriteLine("Kernel: portable life-rule.cvm, 18 instructions, 16 i32 lanes per VM batch");
if (options.Display)
{
    Console.WriteLine("Initial state:");
    Print(seed, options.Width, options.Height);
    Console.WriteLine($"After {options.Generations} generations:");
    Print(result, options.Width, options.Height);
}
Console.WriteLine($"Cell updates: {cellUpdates:N0} in {cellUpdates / 16:N0} VM batches");
Console.WriteLine($"Reference VM: {referenceTimer.Elapsed.TotalMilliseconds:N1} ms, {Rate(cellUpdates, referenceTimer.Elapsed):N0} cells/s");
if (simdElapsed is { } elapsed)
{
    Console.WriteLine($"Hardware SIMD ({SimdInterpreter.VectorWidthBits}-bit): {elapsed.TotalMilliseconds:N1} ms, {Rate(cellUpdates, elapsed):N0} cells/s");
    Console.WriteLine($"SIMD speedup over reference: {referenceTimer.Elapsed.TotalSeconds / elapsed.TotalSeconds:N2}x");
}
else
{
    Console.WriteLine("Hardware SIMD: unavailable on this platform");
}
Console.WriteLine($"Scalar check: {scalarTimer.Elapsed.TotalMilliseconds:N1} ms, {Rate(cellUpdates, scalarTimer.Elapsed):N0} cells/s");
Console.WriteLine($"Live cells: {result.Count(static cell => cell):N0}");
Console.WriteLine($"State SHA-256: {Hash(result)}");
Console.WriteLine($"PASS scalar cross-check{(options.VerifyReplay ? " and deterministic replay" : string.Empty)}");
if (options.Benchmark) Console.WriteLine("The reference VM defines semantics; the SIMD backend must produce the same state hash.");
return 0;

static CascadeModule BuildLifeRuleModule() => new(
[
    // v0 is the current cell state; v1-v8 contain its eight neighbours.
    new(Opcode.Move, Destination: 9, SourceA: 1),
    new(Opcode.AddI32, Destination: 9, SourceA: 9, SourceB: 2),
    new(Opcode.AddI32, Destination: 9, SourceA: 9, SourceB: 3),
    new(Opcode.AddI32, Destination: 9, SourceA: 9, SourceB: 4),
    new(Opcode.AddI32, Destination: 9, SourceA: 9, SourceB: 5),
    new(Opcode.AddI32, Destination: 9, SourceA: 9, SourceB: 6),
    new(Opcode.AddI32, Destination: 9, SourceA: 9, SourceB: 7),
    new(Opcode.AddI32, Destination: 9, SourceA: 9, SourceB: 8),

    // A cell is born with exactly three neighbours.
    new(Opcode.SplatI32, Destination: 10, Immediate: 3),
    new(Opcode.CompareEqualI32, SourceA: 9, SourceB: 10, Mask: 0),
    new(Opcode.SplatI32, Destination: 11, Immediate: 1),
    new(Opcode.Zero, Destination: 12),
    new(Opcode.SelectI32, Destination: 13, SourceA: 11, SourceB: 12, Mask: 0),

    // A live cell survives with exactly two neighbours.
    new(Opcode.SplatI32, Destination: 10, Immediate: 2),
    new(Opcode.CompareEqualI32, SourceA: 9, SourceB: 10, Mask: 1),
    new(Opcode.SelectI32, Destination: 14, SourceA: 0, SourceB: 12, Mask: 1),

    // Birth and survival are disjoint, so bitwise OR produces the next state.
    new(Opcode.Or, Destination: 0, SourceA: 13, SourceB: 14),
    new(Opcode.Halt),
]);

static bool[] Simulate(CascadeProgram program, bool[] seed, int width, int height, int generations, ExecutionBackend backend)
{
    var current = (bool[])seed.Clone();
    var instance = program.Instantiate(new VmOptions { MemorySize = 0, Fuel = 64 }, backend);
    var firstBatch = true;
    for (var generation = 0; generation < generations; generation++)
    {
        var next = new bool[current.Length];
        for (var start = 0; start < current.Length; start += 16)
        {
            if (firstBatch)
                firstBatch = false;
            else
                instance.Restart();
            for (var lane = 0; lane < 16; lane++)
            {
                var index = start + lane;
                var x = index % width;
                var y = index / width;
                instance.State.Register(0).WriteUInt32(lane, current[index] ? 1U : 0U);
                var neighbour = 1;
                for (var dy = -1; dy <= 1; dy++)
                {
                    for (var dx = -1; dx <= 1; dx++)
                    {
                        if (dx == 0 && dy == 0) continue;
                        var neighbourX = (x + dx + width) % width;
                        var neighbourY = (y + dy + height) % height;
                        var alive = current[(neighbourY * width) + neighbourX];
                        instance.State.Register(neighbour++).WriteUInt32(lane, alive ? 1U : 0U);
                    }
                }
            }

            var result = instance.Execute();
            if (!result.Succeeded)
                throw new InvalidOperationException($"Life kernel trapped: {result.Trap!.Code} at instruction {result.Trap.InstructionPointer}.");
            for (var lane = 0; lane < 16; lane++) next[start + lane] = instance.State.Register(0).ReadUInt32(lane) != 0;
        }
        current = next;
    }
    return current;
}

static bool[] SimulateScalar(bool[] seed, int width, int height, int generations)
{
    var current = (bool[])seed.Clone();
    for (var generation = 0; generation < generations; generation++)
    {
        var next = new bool[current.Length];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var neighbours = 0;
                for (var dy = -1; dy <= 1; dy++)
                {
                    for (var dx = -1; dx <= 1; dx++)
                    {
                        if (dx == 0 && dy == 0) continue;
                        var neighbourX = (x + dx + width) % width;
                        var neighbourY = (y + dy + height) % height;
                        if (current[(neighbourY * width) + neighbourX]) neighbours++;
                    }
                }
                var index = (y * width) + x;
                next[index] = neighbours == 3 || current[index] && neighbours == 2;
            }
        }
        current = next;
    }
    return current;
}

static bool[] CreateShowcaseSeed(int width, int height)
{
    var cells = new bool[width * height];
    Set(2, 1);
    Set(3, 2);
    Set(1, 3);
    Set(2, 3);
    Set(3, 3);

    var centerX = width / 2;
    var centerY = height / 2;
    Set(centerX + 1, centerY);
    Set(centerX + 2, centerY);
    Set(centerX, centerY + 1);
    Set(centerX + 1, centerY + 1);
    Set(centerX + 1, centerY + 2);
    return cells;

    void Set(int x, int y) => cells[(y * width) + x] = true;
}

static bool[] CreateBenchmarkSeed(int width, int height)
{
    var cells = new bool[width * height];
    for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
            cells[(y * width) + x] = ((x * 17) + (y * 31) + (x * y)) % 23 is 0 or 1 or 5;
    return cells;
}

static byte[] Encode(CascadeModule module)
{
    using var output = new MemoryStream();
    CascadeProgram.FromModule(module).Save(output);
    return output.ToArray();
}

static string Hash(IEnumerable<bool> cells) => Convert.ToHexString(SHA256.HashData(cells.Select(static cell => cell ? (byte)1 : (byte)0).ToArray()));

static double Rate(long cellUpdates, TimeSpan elapsed) => cellUpdates / Math.Max(elapsed.TotalSeconds, double.Epsilon);

static void Print(IReadOnlyList<bool> cells, int width, int height)
{
    for (var y = 0; y < height; y++)
    {
        for (var x = 0; x < width; x++) Console.Write(cells[(y * width) + x] ? '#' : '.');
        Console.WriteLine();
    }
    Console.WriteLine();
}

static DemoOptions ParseOptions(string[] args)
{
    var benchmark = args.Contains("--benchmark", StringComparer.OrdinalIgnoreCase);
    var width = benchmark ? 256 : 32;
    var height = benchmark ? 256 : 16;
    var generations = benchmark ? 50 : 24;
    var display = !benchmark;
    var verifyReplay = !benchmark;

    for (var index = 0; index < args.Length; index++)
    {
        switch (args[index].ToLowerInvariant())
        {
            case "--benchmark":
                break;
            case "--width":
                width = ReadPositiveInt(args, ref index, "--width");
                break;
            case "--height":
                height = ReadPositiveInt(args, ref index, "--height");
                break;
            case "--generations":
                generations = ReadPositiveInt(args, ref index, "--generations");
                break;
            case "--display":
                display = true;
                break;
            case "--no-display":
                display = false;
                break;
            case "--verify-replay":
                verifyReplay = true;
                break;
            default:
                throw new ArgumentException($"Unknown option '{args[index]}'.");
        }
    }

    int cellCount;
    try
    {
        cellCount = checked(width * height);
    }
    catch (OverflowException)
    {
        throw new ArgumentException("Grid dimensions are too large.");
    }
    if (width < 8 || height < 8) throw new ArgumentException("Width and height must each be at least 8.");
    if (cellCount % 16 != 0) throw new ArgumentException("The total cell count must be divisible by 16.");
    if (cellCount > 16 * 1024 * 1024) throw new ArgumentException("The grid may contain at most 16,777,216 cells.");
    return new(width, height, generations, display, verifyReplay, benchmark);
}

static int ReadPositiveInt(string[] args, ref int index, string option)
{
    if (++index >= args.Length || !int.TryParse(args[index], out var value) || value <= 0)
        throw new ArgumentException($"{option} requires a positive integer.");
    return value;
}

static void PrintUsage()
{
    Console.WriteLine("Usage:");
    Console.WriteLine("  dotnet run --project examples/Cascade.GameOfLife");
    Console.WriteLine("  dotnet run --project examples/Cascade.GameOfLife -- --benchmark");
    Console.WriteLine("  dotnet run --project examples/Cascade.GameOfLife -- --width 64 --height 32 --generations 100 --no-display");
    Console.WriteLine();
    Console.WriteLine("Options:");
    Console.WriteLine("  --benchmark        Run a deterministic 256x256, 50-generation workload");
    Console.WriteLine("  --width N          Grid width; width times height must be divisible by 16");
    Console.WriteLine("  --height N         Grid height");
    Console.WriteLine("  --generations N    Number of generations");
    Console.WriteLine("  --display          Print initial and final boards");
    Console.WriteLine("  --no-display       Suppress board rendering");
    Console.WriteLine("  --verify-replay    Execute the Cascade workload twice and compare results");
    Console.WriteLine("  --help             Show this help");
}

internal sealed record DemoOptions(
    int Width,
    int Height,
    int Generations,
    bool Display,
    bool VerifyReplay,
    bool Benchmark);
