using Cascade.Core;
using Cascade.Runtime;

internal static class FusionChecks
{
    public static void Run()
    {
        var count = 0;
        foreach (var compare in new[] { Opcode.CompareEqualI32, Opcode.CompareLessThanI32, Opcode.CompareEqualI64, Opcode.CompareLessThanI64 })
            foreach (var destination in new byte[] { 0, 1, 2 })
                foreach (var variant in Enumerable.Range(0, 6))
                    foreach (var fuel in Enumerable.Range(0, 5))
                    {
                        var is32 = compare is Opcode.CompareEqualI32 or Opcode.CompareLessThanI32;
                        var select = is32 ? Opcode.SelectI32 : Opcode.SelectI64;
                        if (variant == 1) select = is32 ? Opcode.SelectI64 : Opcode.SelectI32;
                        var code = new List<Instruction>();
                        if (variant == 3) code.Add(new(Opcode.Jump, Immediate: 2));
                        code.Add(new(compare, SourceA: 0, SourceB: 1, Mask: 2));
                        if (variant != 5) code.Add(new(select, Destination: destination, SourceA: 0, SourceB: 1, Mask: variant == 2 ? (byte)3 : (byte)2));
                        if (variant != 4 && variant != 5) code.Add(new(Opcode.Halt));
                        var program = CascadeProgram.FromModule(new CascadeModule(code));
                        foreach (var backend in SimdInterpreter.IsSupported
                            ? new[] { ExecutionBackend.Reference, ExecutionBackend.HardwareSimd } : [ExecutionBackend.Reference])
                        {
                            var baseline = program.Instantiate(new VmOptions { MemorySize = 64, Fuel = (ulong)fuel });
                            var candidate = program.Instantiate(new VmOptions { MemorySize = 64, Fuel = (ulong)fuel, EnableInstructionFusion = true }, backend);
                            for (var pass = 0; pass < 2; pass++)
                            {
                                baseline.Restart();
                                candidate.Restart();
                                for (var register = 0; register < 32; register++)
                                    for (var lane = 0; lane < 8; lane++)
                                    {
                                        ulong[] values = [0, ulong.MaxValue, 1UL << 63, long.MaxValue, 0x80000000, uint.MaxValue, 1, 17];
                                        var bits = values[(lane + register + pass) % values.Length];
                                        baseline.State.Register(register).WriteUInt64(lane, bits);
                                        candidate.State.Register(register).WriteUInt64(lane, bits);
                                    }
                                var expected = baseline.Execute();
                                var actual = candidate.Execute();
                                if (actual != expected || candidate.State.InstructionPointer != baseline.State.InstructionPointer ||
                                    candidate.State.FuelRemaining != baseline.State.FuelRemaining)
                                    throw new InvalidOperationException($"Fusion result mismatch: {compare}/{destination}/{variant}/{fuel}/{backend}");
                                for (var register = 0; register < 32; register++)
                                    if (!candidate.State.Register(register).Bytes.SequenceEqual(baseline.State.Register(register).Bytes))
                                        throw new InvalidOperationException("Fusion register mismatch.");
                                for (var mask = 0; mask < 8; mask++)
                                    if (candidate.State.Mask(mask) != baseline.State.Mask(mask)) throw new InvalidOperationException("Fusion mask mismatch.");
                                if (!candidate.State.LinearMemory.Span.SequenceEqual(baseline.State.LinearMemory.Span))
                                    throw new InvalidOperationException("Fusion memory mismatch.");
                                count++;
                            }
                        }
                    }
        Console.WriteLine($"  {count} fusion full-state comparisons passed");
    }
}
