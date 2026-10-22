using System.Buffers.Binary;
using System.Diagnostics;
using Cascade.Core;
using Cascade.Runtime;

internal static class BatchBenchmark
{
    public static int Run(bool quick)
    {
        var count = quick ? 256 : 4096;
        var samples = quick ? 3 : 9;
        var program = CascadeProgram.FromModule(new CascadeModule([
            new(Opcode.SplatI32, Destination: 1, Immediate: 7),
            new(Opcode.AddI32, Destination: 0, SourceA: 0, SourceB: 1),
            new(Opcode.Halt),
        ]));
        var inputs = new byte[count * 64];
        var outputs = new byte[inputs.Length];
        var expected = new byte[inputs.Length];
        for (var lane = 0; lane < count * 16; lane++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(inputs.AsSpan(lane * 4), (uint)lane);
            BinaryPrimitives.WriteUInt32LittleEndian(expected.AsSpan(lane * 4), (uint)(lane + 7));
        }
        Console.WriteLine($"Packed kernel benchmark: {count} invocations per sample, median of {samples}; buffers prepared outside timing");
        foreach (var backend in SimdInterpreter.IsSupported
            ? new[] { ExecutionBackend.Reference, ExecutionBackend.HardwareSimd } : [ExecutionBackend.Reference])
        {
            var options = new VmOptions { MemorySize = 0, Fuel = 3 };
            var reusable = program.Instantiate(options, backend);
            foreach (var mode in new[] { "new instance", "manual restart", "packed batch" })
            {
                for (var warmup = 0; warmup < 4; warmup++) Execute(mode);
                var times = new double[samples];
                var allocations = new long[samples];
                for (var sample = 0; sample < samples; sample++)
                {
                    var before = GC.GetAllocatedBytesForCurrentThread();
                    var started = Stopwatch.GetTimestamp();
                    Execute(mode);
                    times[sample] = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                    allocations[sample] = GC.GetAllocatedBytesForCurrentThread() - before;
                    if (!outputs.AsSpan().SequenceEqual(expected)) throw new InvalidOperationException($"{backend}/{mode}: scalar oracle mismatch");
                }
                Array.Sort(times);
                Array.Sort(allocations);
                Console.WriteLine($"{backend,-12} {mode,-15} {times[samples / 2],9:F3} ms; {allocations[samples / 2],12:N0} allocated bytes/sample");
            }

            void Execute(string mode)
            {
                if (mode == "packed batch")
                {
                    var result = reusable.ExecuteBatch(inputs, outputs, 1);
                    if (!result.Succeeded || result.CompletedCount != count || result.FuelConsumed != (ulong)count * 3)
                        throw new InvalidOperationException($"Batch result mismatch: {result}");
                    return;
                }
                for (var item = 0; item < count; item++)
                {
                    var instance = mode == "new instance" ? program.Instantiate(options, backend) : reusable;
                    if (mode != "new instance") instance.Restart();
                    instance.State.Register(0).ReadFrom(inputs.AsSpan(item * 64, 64));
                    var result = instance.Execute();
                    if (!result.Succeeded || result.FuelConsumed != 3) throw new InvalidOperationException("Manual invocation failed.");
                    instance.State.Register(0).WriteTo(outputs.AsSpan(item * 64, 64));
                }
            }
        }
        Console.WriteLine("PASS independent scalar output checks; timings are machine-specific. Packed batching uses value-type internal results.");
        return 0;
    }
}
