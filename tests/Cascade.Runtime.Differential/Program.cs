using System.Buffers.Binary;
using Cascade.Core;
using Cascade.Runtime;

var corpus = new (string Name, CascadeModule Module, VmOptions Options)[]
{
    ("arithmetic", new([
        new(Opcode.SplatI64, Destination: 0, Immediate: ulong.MaxValue),
        new(Opcode.SplatI64, Destination: 1, Immediate: 2),
        new(Opcode.AddI64, Destination: 0, SourceA: 0, SourceB: 1),
        new(Opcode.Halt),
    ]), new()),
    ("memory", new([
        new(Opcode.SplatI64, Destination: 0, Immediate: 32),
        new(Opcode.SplatI64, Destination: 1, Immediate: 0x1122334455667788),
        new(Opcode.Store, SourceA: 0, SourceB: 1),
        new(Opcode.Load, Destination: 2, SourceA: 0),
        new(Opcode.Halt),
    ]), new()),
    ("control", new([
        new(Opcode.Call, Immediate: 3),
        new(Opcode.Jump, Immediate: 5),
        new(Opcode.Halt),
        new(Opcode.SplatI32, Destination: 0, Immediate: 42),
        new(Opcode.Return),
        new(Opcode.Halt),
    ], functions: [new(3, 5)]), new()),
    ("fuel trap", new([new(Opcode.Jump, Immediate: 0)]), new() { Fuel = 17 }),
    ("vector operations", VectorOperations(), new()),
};

IDifferentialBackend baseline = new ReferenceBackend("reference-a");
IDifferentialBackend candidate = SimdInterpreter.IsSupported
    ? new SimdBackend()
    : new ReferenceBackend("reference-b");
Console.WriteLine(SimdInterpreter.IsSupported
    ? $"Hardware SIMD differential candidate: {SimdInterpreter.VectorWidthBits}-bit System.Numerics vectors"
    : "Hardware SIMD is unavailable; differential candidate uses a second reference interpreter.");
foreach (var item in corpus)
{
    var difference = DifferentialVerifier.Compare(item.Module, item.Options, baseline, candidate);
    if (difference is not null)
    {
        Console.Error.WriteLine($"FAIL {item.Name}: {difference}");
        return 1;
    }
    Console.WriteLine($"PASS {item.Name}");
}

var selfTest = DifferentialVerifier.Compare(corpus[0].Module, corpus[0].Options, baseline, new DivergentBackend());
if (selfTest is null)
{
    Console.Error.WriteLine("FAIL harness self-test: deliberate divergence was not detected");
    return 1;
}
Console.WriteLine("PASS harness self-test");
Console.WriteLine($"{corpus.Length} corpus cases matched; divergence detector passed");
return 0;

static CascadeModule VectorOperations()
{
    int[] left = [int.MinValue, -100, -1, 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, int.MaxValue, 42];
    int[] right = [int.MinValue, -50, 1, 0, -1, 3, 2, 4, 6, 5, 8, 7, 10, 9, 1, 42];
    return new(
    [
        new(Opcode.LoadConstant, Destination: 0, Immediate: 0),
        new(Opcode.LoadConstant, Destination: 1, Immediate: 1),
        new(Opcode.AddI32, Destination: 2, SourceA: 0, SourceB: 1),
        new(Opcode.SubI32, Destination: 3, SourceA: 0, SourceB: 1),
        new(Opcode.MulI32, Destination: 4, SourceA: 0, SourceB: 1),
        new(Opcode.AddI64, Destination: 5, SourceA: 0, SourceB: 1),
        new(Opcode.SubI64, Destination: 6, SourceA: 0, SourceB: 1),
        new(Opcode.And, Destination: 7, SourceA: 0, SourceB: 1),
        new(Opcode.Or, Destination: 8, SourceA: 0, SourceB: 1),
        new(Opcode.Xor, Destination: 9, SourceA: 0, SourceB: 1),
        new(Opcode.Not, Destination: 10, SourceA: 0),
        new(Opcode.CompareEqualI32, SourceA: 0, SourceB: 1, Mask: 0),
        new(Opcode.SelectI32, Destination: 11, SourceA: 0, SourceB: 1, Mask: 0),
        new(Opcode.CompareLessThanI32, SourceA: 0, SourceB: 1, Mask: 1),
        new(Opcode.SelectI32, Destination: 12, SourceA: 1, SourceB: 0, Mask: 1),
        new(Opcode.CompareEqualI64, SourceA: 0, SourceB: 1, Mask: 2),
        new(Opcode.SelectI64, Destination: 13, SourceA: 0, SourceB: 1, Mask: 2),
        new(Opcode.CompareLessThanI64, SourceA: 0, SourceB: 1, Mask: 3),
        new(Opcode.SelectI64, Destination: 14, SourceA: 1, SourceB: 0, Mask: 3),
        new(Opcode.AddI32, Destination: 0, SourceA: 0, SourceB: 1),
        new(Opcode.Halt),
    ],
    [I32Constant(left), I32Constant(right)]);
}

static VectorConstant I32Constant(IReadOnlyList<int> values)
{
    var bytes = new byte[VectorRegister.ByteCount];
    for (var lane = 0; lane < values.Count; lane++)
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(lane * sizeof(int), sizeof(int)), values[lane]);
    return new(bytes);
}

public interface IDifferentialBackend
{
    string Name { get; }
    BackendRun Execute(CascadeModule module, VmOptions options);
}

public sealed record BackendRun(ExecutionResult Result, VmSnapshot State);

public sealed record VmSnapshot(
    byte[][] Registers,
    PredicateMask[] Masks,
    byte[] Memory,
    int InstructionPointer,
    ulong FuelRemaining);

public sealed class ReferenceBackend(string name) : IDifferentialBackend
{
    public string Name { get; } = name;

    public BackendRun Execute(CascadeModule module, VmOptions options)
    {
        var interpreter = new ReferenceInterpreter(module, options);
        var result = interpreter.Execute();
        return new(result, Snapshot(interpreter.State));
    }

    internal static VmSnapshot Snapshot(VmState state) => new(
        Enumerable.Range(0, 32).Select(index => state.Register(index).Bytes.ToArray()).ToArray(),
        Enumerable.Range(0, 8).Select(state.Mask).ToArray(),
        state.LinearMemory.ToArray(),
        state.InstructionPointer,
        state.FuelRemaining);
}

public sealed class SimdBackend : IDifferentialBackend
{
    public string Name => "hardware-simd";

    public BackendRun Execute(CascadeModule module, VmOptions options)
    {
        var interpreter = new SimdInterpreter(module, options);
        var result = interpreter.Execute();
        return new(result, ReferenceBackend.Snapshot(interpreter.State));
    }
}

public sealed class DivergentBackend : IDifferentialBackend
{
    public string Name => "deliberately-divergent";

    public BackendRun Execute(CascadeModule module, VmOptions options)
    {
        var interpreter = new ReferenceInterpreter(module, options);
        var result = interpreter.Execute();
        var snapshot = ReferenceBackend.Snapshot(interpreter.State);
        snapshot.Registers[0][0] ^= 1;
        return new(result, snapshot);
    }
}

public static class DifferentialVerifier
{
    public static string? Compare(CascadeModule module, VmOptions options, IDifferentialBackend baseline, IDifferentialBackend candidate)
    {
        var expected = baseline.Execute(module, options);
        var actual = candidate.Execute(module, options);
        if (expected.Result != actual.Result) return $"result differs between {baseline.Name} and {candidate.Name}";
        for (var index = 0; index < 32; index++)
            if (!expected.State.Registers[index].SequenceEqual(actual.State.Registers[index])) return $"v{index} differs";
        for (var index = 0; index < 8; index++)
            if (expected.State.Masks[index] != actual.State.Masks[index]) return $"k{index} differs";
        if (!expected.State.Memory.SequenceEqual(actual.State.Memory)) return "linear memory differs";
        if (expected.State.InstructionPointer != actual.State.InstructionPointer) return "instruction pointer differs";
        if (expected.State.FuelRemaining != actual.State.FuelRemaining) return "remaining fuel differs";
        return null;
    }
}
