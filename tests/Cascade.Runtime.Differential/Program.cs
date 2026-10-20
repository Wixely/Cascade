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
};

IDifferentialBackend baseline = new ReferenceBackend("reference-a");
IDifferentialBackend candidate = new ReferenceBackend("reference-b");
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
