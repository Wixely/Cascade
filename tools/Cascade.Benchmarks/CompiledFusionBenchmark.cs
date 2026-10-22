using System.Buffers.Binary;
using System.Diagnostics;
using Cascade.Compiler;
using Cascade.Core;
using Cascade.Runtime;

internal static class CompiledFusionBenchmark
{
    public static int Run(bool quick)
    {
        var samples = quick ? 3 : 9;
        var calls = quick ? 128 : 4096;
        Console.WriteLine("Compiled fusion: compilation excluded; instance setup, argument copies and execution included");
        foreach (var name in new[] { "life-rule", "gather-transform", "clamp" })
        {
            var source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Kernels", name + ".cascade"));
            var compiled = CascadeCompiler.Compile(source);
            if (!compiled.Succeeded) throw new InvalidOperationException(string.Join("; ", compiled.Diagnostics));
            var module = compiled.Module!;
            var program = CascadeProgram.FromModule(module);
            var pairs = 0;
            for (var ip = 0; ip + 1 < module.Instructions.Count; ip++)
            {
                var a = module.Instructions[ip];
                var b = module.Instructions[ip + 1];
                if (a.Mask == b.Mask &&
                    ((a.Opcode is Opcode.CompareEqualI32 or Opcode.CompareLessThanI32 && b.Opcode == Opcode.SelectI32) ||
                     (a.Opcode is Opcode.CompareEqualI64 or Opcode.CompareLessThanI64 && b.Opcode == Opcode.SelectI64))) pairs++;
            }
            var (inputs, expected, memory) = Fixture(name);
            Console.WriteLine($"{name}: {module.Instructions.Count} instructions, {pairs} eligible pairs");
            foreach (var backend in SimdInterpreter.IsSupported
                ? new[] { ExecutionBackend.Reference, ExecutionBackend.HardwareSimd } : [ExecutionBackend.Reference])
                foreach (var reuse in new[] { 1, 64 })
                {
                    var times = new[] { new double[samples], new double[samples] };
                    var allocations = new[] { new long[samples], new long[samples] };
                    for (var mode = 0; mode < 2; mode++) Execute(mode != 0, 128, verify: true);
                    for (var sample = 0; sample < samples; sample++)
                        for (var order = 0; order < 2; order++)
                        {
                            var mode = (sample + order) % 2;
                            var before = GC.GetAllocatedBytesForCurrentThread();
                            var started = Stopwatch.GetTimestamp();
                            Execute(mode != 0, calls, verify: false);
                            times[mode][sample] = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                            allocations[mode][sample] = GC.GetAllocatedBytesForCurrentThread() - before;
                            Execute(mode != 0, reuse, verify: true);
                        }
                    foreach (var values in times) Array.Sort(values);
                    foreach (var values in allocations) Array.Sort(values);
                    Console.WriteLine($"  {backend,-12} reuse {reuse,2}: off {times[0][samples / 2]:F3} ms, on {times[1][samples / 2]:F3} ms, " +
                        $"{times[0][samples / 2] / times[1][samples / 2]:F2}x; bytes {allocations[0][samples / 2]} / {allocations[1][samples / 2]}");

                    void Execute(bool enabled, int count, bool verify)
                    {
                        var options = new VmOptions { MemorySize = memory, Fuel = (ulong)module.Instructions.Count, EnableInstructionFusion = enabled };
                        CascadeInstance? instance = null;
                        for (var iteration = 0; iteration < count; iteration++)
                        {
                            if (iteration % reuse == 0) instance = program.Instantiate(options, backend);
                            else instance!.Restart();
                            for (var register = 0; register < inputs.Length; register++) instance!.State.Register(register).ReadFrom(inputs[register]);
                            var result = instance!.Execute();
                            if (!result.Succeeded || result.FuelConsumed != (ulong)module.Instructions.Count)
                                throw new InvalidOperationException($"{name} execution mismatch");
                            if (verify && !instance.State.Register(0).Bytes.SequenceEqual(expected))
                                throw new InvalidOperationException($"{name} scalar oracle mismatch");
                            if (verify && name == "gather-transform")
                                for (var lane = 0; lane < 16; lane++)
                                    if (BinaryPrimitives.ReadUInt32LittleEndian(instance.State.LinearMemory.Span.Slice((15 - lane) * 4)) != (uint)(100 + lane))
                                        throw new InvalidOperationException("Scatter memory oracle mismatch");
                        }
                    }
                }
        }
        Console.WriteLine("PASS compiled-kernel scalar and memory checks; timings are local observations.");
        return 0;
    }

    private static (byte[][] Inputs, byte[] Expected, int Memory) Fixture(string name)
    {
        var inputs = Enumerable.Range(0, name == "life-rule" ? 9 : 3).Select(_ => new byte[64]).ToArray();
        var expected = new byte[64];
        for (var lane = 0; lane < 16; lane++)
        {
            int result;
            if (name == "life-rule")
            {
                var neighbors = lane % 9;
                for (var register = 0; register < 9; register++)
                    Write(inputs[register], lane, register == 0 ? lane % 2 : register <= neighbors ? 1 : 0);
                result = neighbors == 3 || (neighbors == 2 && lane % 2 != 0) ? 1 : 0;
            }
            else if (name == "gather-transform")
            {
                Write(inputs[0], lane, 100 + lane);
                BinaryPrimitives.WriteUInt64LittleEndian(inputs[1 + lane / 8].AsSpan((lane % 8) * 8), (ulong)((15 - lane) * 4));
                result = 100 + lane;
            }
            else
            {
                Write(inputs[0], lane, lane - 8);
                Write(inputs[1], lane, -3);
                Write(inputs[2], lane, 4);
                result = Math.Clamp(lane - 8, -3, 4);
            }
            Write(expected, lane, result);
        }
        return (inputs, expected, name == "gather-transform" ? 64 : 0);
    }

    private static void Write(byte[] bytes, int lane, int value) => BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(lane * 4), value);
}
