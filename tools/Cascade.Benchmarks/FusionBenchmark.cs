using System.Diagnostics;
using Cascade.Core;
using Cascade.Runtime;

internal static class FusionBenchmark
{
    public static int Run(bool quick)
    {
        var repetitions = quick ? 100 : 2000;
        var samples = quick ? 3 : 9;
        Console.WriteLine("Compare/select dispatch fusion: reused instances, alternating sample order, median times");
        foreach (var (name, pairs) in new[] { ("short-pair", 1), ("long-pairs", 256), ("no-pairs", 0) })
            foreach (var backend in SimdInterpreter.IsSupported
                ? new[] { ExecutionBackend.Reference, ExecutionBackend.HardwareSimd } : [ExecutionBackend.Reference])
            {
                var code = new List<Instruction>();
                var operationCount = pairs == 0 ? 256 : pairs;
                for (var pair = 0; pair < operationCount; pair++)
                {
                    if (pairs == 0) code.Add(new(Opcode.AddI32, Destination: 2, SourceA: 0, SourceB: 1));
                    else
                    {
                        code.Add(new(Opcode.CompareLessThanI32, SourceA: 0, SourceB: 1, Mask: 0));
                        code.Add(new(Opcode.SelectI32, Destination: 2, SourceA: 0, SourceB: 1, Mask: 0));
                    }
                }
                code.Add(new(Opcode.Halt));
                var program = CascadeProgram.FromModule(new CascadeModule(code));
                var instances = new[] { false, true }.Select(enabled => program.Instantiate(
                    new VmOptions { MemorySize = 0, Fuel = (ulong)code.Count, EnableInstructionFusion = enabled }, backend)).ToArray();
                foreach (var instance in instances)
                    for (var lane = 0; lane < 16; lane++)
                    {
                        instance.State.Register(0).WriteUInt32(lane, unchecked((uint)(lane - 8)));
                        instance.State.Register(1).WriteUInt32(lane, (uint)(15 - lane));
                    }
                foreach (var instance in instances) Run(instance, 200);
                var times = new[] { new double[samples], new double[samples] };
                for (var sample = 0; sample < samples; sample++)
                    for (var order = 0; order < 2; order++)
                    {
                        var mode = (sample + order) % 2;
                        var started = Stopwatch.GetTimestamp();
                        Run(instances[mode], repetitions);
                        times[mode][sample] = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                        for (var lane = 0; lane < 16; lane++)
                        {
                            var expected = pairs == 0 ? 7 : Math.Min(lane - 8, 15 - lane);
                            if (instances[mode].State.Register(2).ReadUInt32(lane) != unchecked((uint)expected))
                                throw new InvalidOperationException("Fusion scalar oracle mismatch.");
                        }
                    }
                foreach (var measurements in times) Array.Sort(measurements);
                Console.WriteLine($"{name,-12} {backend,-12} off {times[0][samples / 2]:F3} ms; on {times[1][samples / 2]:F3} ms; {times[0][samples / 2] / times[1][samples / 2]:F2}x");

                void Run(CascadeInstance instance, int count)
                {
                    for (var iteration = 0; iteration < count; iteration++)
                    {
                        instance.Restart();
                        var result = instance.Execute();
                        if (!result.Succeeded || result.FuelConsumed != (ulong)code.Count)
                            throw new InvalidOperationException("Fusion benchmark execution mismatch.");
                    }
                }
            }
        Console.WriteLine("PASS scalar checks; synthetic workloads only, not a universal speedup claim.");
        return 0;
    }
}
