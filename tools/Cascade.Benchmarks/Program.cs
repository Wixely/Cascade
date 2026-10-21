using System.Diagnostics;
using Cascade.Core;
using Cascade.Runtime;

var iterations = args is ["--quick"] ? 25 : 100;
var corpus = new (string Name, CascadeModule Module)[]
{
    ("arithmetic-i64", Arithmetic(1_024)),
    ("linear-memory", Memory(512)),
    ("mask-select-i32", Masks(512)),
    ("calls", Calls(256)),
};

Console.WriteLine($"Cascade backend benchmark; {iterations} iterations per case");
Console.WriteLine(SimdInterpreter.IsSupported
    ? $"Hardware SIMD width: {SimdInterpreter.VectorWidthBits} bits"
    : "Hardware SIMD unavailable");
foreach (var item in corpus)
{
    var reference = Measure(item.Name, item.Module, iterations, ExecutionBackend.Reference);
    Console.WriteLine($"{item.Name,-18} reference {reference.Instructions,10} instructions  {reference.Elapsed.TotalMilliseconds,10:F3} ms  {reference.Rate,14:F0} instr/s");
    if (SimdInterpreter.IsSupported)
    {
        var simd = Measure(item.Name, item.Module, iterations, ExecutionBackend.HardwareSimd);
        Console.WriteLine($"{"",-18} simd      {simd.Instructions,10} instructions  {simd.Elapsed.TotalMilliseconds,10:F3} ms  {simd.Rate,14:F0} instr/s  {reference.Elapsed.TotalSeconds / simd.Elapsed.TotalSeconds,6:F2}x");
    }
}
return 0;

static BenchmarkResult Measure(string name, CascadeModule module, int iterations, ExecutionBackend backend)
{
    var program = CascadeProgram.FromModule(module);
    for (var iteration = 0; iteration < 10; iteration++)
    {
        var warmup = program.Instantiate(backend: backend).Execute();
        if (!warmup.Succeeded) throw new InvalidOperationException($"{name} warmup trapped: {warmup.Trap}");
    }
    var executed = 0UL;
    var stopwatch = Stopwatch.StartNew();
    for (var iteration = 0; iteration < iterations; iteration++)
    {
        var instance = program.Instantiate(backend: backend);
        var result = instance.Execute();
        if (!result.Succeeded) throw new InvalidOperationException($"{name} trapped: {result.Trap}");
        executed += result.FuelConsumed;
    }
    stopwatch.Stop();
    return new(executed, stopwatch.Elapsed, executed / Math.Max(stopwatch.Elapsed.TotalSeconds, double.Epsilon));
}

static CascadeModule Arithmetic(int count)
{
    var code = new List<Instruction>
    {
        new(Opcode.SplatI64, Destination: 0, Immediate: 1),
        new(Opcode.SplatI64, Destination: 1, Immediate: 3),
    };
    for (var index = 0; index < count; index++) code.Add(new(Opcode.AddI64, Destination: 0, SourceA: 0, SourceB: 1));
    code.Add(new(Opcode.Halt));
    return new(code);
}

static CascadeModule Memory(int count)
{
    var code = new List<Instruction>
    {
        new(Opcode.SplatI64, Destination: 0, Immediate: 64),
        new(Opcode.SplatI64, Destination: 1, Immediate: 0xA5A5A5A5A5A5A5A5),
    };
    for (var index = 0; index < count; index++)
    {
        code.Add(new(Opcode.Store, SourceA: 0, SourceB: 1));
        code.Add(new(Opcode.Load, Destination: 2, SourceA: 0));
    }
    code.Add(new(Opcode.Halt));
    return new(code);
}

static CascadeModule Masks(int count)
{
    var code = new List<Instruction>
    {
        new(Opcode.SplatI32, Destination: 0, Immediate: 1),
        new(Opcode.SplatI32, Destination: 1, Immediate: 2),
    };
    for (var index = 0; index < count; index++)
    {
        code.Add(new(Opcode.CompareLessThanI32, SourceA: 0, SourceB: 1, Mask: 0));
        code.Add(new(Opcode.SelectI32, Destination: 2, SourceA: 1, SourceB: 0, Mask: 0));
    }
    code.Add(new(Opcode.Halt));
    return new(code);
}

static CascadeModule Calls(int count)
{
    var functionStart = checked((uint)(count + 1));
    var code = new List<Instruction>(count + 3);
    for (var index = 0; index < count; index++) code.Add(new(Opcode.Call, Immediate: functionStart));
    code.Add(new(Opcode.Halt));
    code.Add(new(Opcode.AddI64, Destination: 0, SourceA: 0, SourceB: 1));
    code.Add(new(Opcode.Return));
    return new(code, functions: [new(functionStart, functionStart + 2)]);
}

internal sealed record BenchmarkResult(ulong Instructions, TimeSpan Elapsed, double Rate);
