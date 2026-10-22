using System.Buffers.Binary;
using System.Text;
using Cascade.Compiler;
using Cascade.Core;
using Cascade.Runtime;

internal static class CompilerSpillChecks
{
    public static void VectorPressure()
    {
        foreach (var bits in new[] { 32, 64 })
        {
            const int count = 300;
            var source = new StringBuilder($"kernel pressure(vector<i{bits}> input) -> vector<i{bits}> {{\n");
            for (var index = 0; index < count; index++) source.AppendLine($"let v{index} = input + {index};");
            source.AppendLine("var sum = input - input;");
            for (var index = 0; index < count; index++) source.AppendLine($"sum = sum + v{index};");
            source.AppendLine("return sum; }");
            foreach (var optimize in new[] { false, true })
            {
                var compilation = Compile(source.ToString(), optimize);
                Require(compilation.Metrics!.PeakVirtualVectorRegisters > 256, "Virtual indices did not exceed byte range");
                Require(compilation.Module!.VectorSpillSlots > 0 && compilation.Module.MaskSpillSlots == 0, "Vector-only spill metadata");
                foreach (var backend in Backends())
                {
                    var instance = RoundTrip(compilation.Module).Instantiate(new VmOptions { MemorySize = 0 }, backend);
                    for (var lane = 0; lane < 512 / bits; lane++)
                        if (bits == 32) instance.State.Register(0).WriteUInt32(lane, unchecked((uint)(lane - 9)));
                        else instance.State.Register(0).WriteUInt64(lane, unchecked((ulong)(lane - 9)));
                    Require(instance.Execute().Succeeded, "High pressure vector execution trapped");
                    for (var lane = 0; lane < 512 / bits; lane++)
                    {
                        var expected = (lane - 9) * count + count * (count - 1) / 2;
                        Require(bits == 32 ? instance.State.Register(0).ReadUInt32(lane) == unchecked((uint)expected)
                            : instance.State.Register(0).ReadUInt64(lane) == unchecked((ulong)expected), "Vector spill scalar mismatch");
                    }
                }
            }
        }
    }

    public static void MixedPressure()
    {
        var source = new StringBuilder("""
            fn bump(vector<i32> value) -> vector<i32> { return value + 3; }
            fn replace(vector<i64> address, vector<i32> value) -> vector<i32> { store(address, value + 100); return value; }
            kernel mixed(vector<i32> input, vector<i64> low, vector<i64> high, vector<i64> address) -> vector<i32> {
            """);
        for (var index = 0; index < 40; index++) source.AppendLine($"let v{index} = input + {index};");
        for (var index = 0; index < 16; index++) source.AppendLine($"let m{index} = input < {index};");
        source.AppendLine("""
            scatter_i32(low, high, input, mask_all_i32());
            let gathered = gather_i32(low, high, mask_all_i32());
            let snapshot = load_i32(address);
            let afterWrite = replace(address, input);
            var sum = input - input;
            """);
        for (var index = 0; index < 40; index++) source.AppendLine($"sum = sum + v{index};");
        for (var index = 0; index < 16; index++) source.AppendLine($"sum = sum + select(m{index}, input, 0);");
        source.AppendLine("repeat 2 { sum = sum + 1; } return bump(sum + gathered + snapshot + afterWrite); }");
        foreach (var optimize in new[] { false, true })
        {
            var compilation = Compile(source.ToString(), optimize);
            Require(compilation.Module!.VectorSpillSlots > 0 && compilation.Module.MaskSpillSlots > 0, "Both spill banks required");
            foreach (var backend in Backends())
            {
                var program = RoundTrip(compilation.Module);
                var instance = program.Instantiate(new VmOptions { MemorySize = 64 }, backend);
                for (var pass = 0; pass < 2; pass++)
                {
                    instance.Restart();
                    for (var lane = 0; lane < 16; lane++)
                    {
                        instance.State.Register(0).WriteUInt32(lane, unchecked((uint)(lane - 8)));
                        instance.State.Register(1 + lane / 8).WriteUInt64(lane % 8, (ulong)((15 - lane) * 4));
                    }
                    instance.State.Register(3).Clear();
                    Require(instance.Execute().Succeeded, "Mixed pressure kernel trapped");
                    for (var lane = 0; lane < 16; lane++)
                    {
                        var input = lane - 8;
                        var count = Enumerable.Range(0, 16).Count(threshold => input < threshold);
                        var expected = 40 * input + 780 + count * input + 2 * input + (7 - lane) + 5;
                        Require(instance.State.Register(0).ReadUInt32(lane) == unchecked((uint)expected), "Mixed spill scalar mismatch");
                        Require(BinaryPrimitives.ReadInt32LittleEndian(instance.State.LinearMemory.Span.Slice(lane * 4)) == input + 100,
                            "Spilling changed linear-memory effects");
                    }
                }
                // Spill allocation must not change a real memory-failure boundary.
                var trapped = program.Instantiate(new VmOptions { MemorySize = 0 }, backend).Execute();
                Require(trapped.Trap?.Code == TrapCode.InvalidMemoryAccess, "Expected scatter memory trap");
            }
        }
    }

    public static void MaskPressure()
    {
        foreach (var bits in new[] { 32, 64 })
        {
            var source = new StringBuilder($"kernel masks(vector<i{bits}> input) -> vector<i{bits}> {{\n");
            for (var index = 0; index < 24; index++) source.AppendLine($"let m{index} = input < {index};");
            source.AppendLine("let saved = m10; var sum = input - input;");
            for (var index = 0; index < 24; index++) source.AppendLine($"sum = sum + select(m{index}, input, 0);");
            source.AppendLine("return sum + select(saved, input, 0); }");
            foreach (var optimize in new[] { false, true })
            {
                var compilation = Compile(source.ToString(), optimize);
                Require(compilation.Module!.VectorSpillSlots == 0 && compilation.Module.MaskSpillSlots > 0, "Expected mask-only spill path");
                foreach (var backend in Backends())
                {
                    var instance = RoundTrip(compilation.Module).Instantiate(new VmOptions { MemorySize = 0 }, backend);
                    for (var lane = 0; lane < 512 / bits; lane++)
                        if (bits == 32) instance.State.Register(0).WriteUInt32(lane, unchecked((uint)(lane - 2)));
                        else instance.State.Register(0).WriteUInt64(lane, unchecked((ulong)(lane * 5 - 2)));
                    Require(instance.Execute().Succeeded, "Mask-only spill trapped");
                    for (var lane = 0; lane < 512 / bits; lane++)
                    {
                        var input = bits == 32 ? lane - 2 : lane * 5 - 2;
                        var expected = input * (Enumerable.Range(0, 24).Count(threshold => input < threshold) + (input < 10 ? 1 : 0));
                        Require(bits == 32 ? instance.State.Register(0).ReadUInt32(lane) == unchecked((uint)expected)
                            : instance.State.Register(0).ReadUInt64(lane) == unchecked((ulong)expected), "Mask spill scalar mismatch");
                    }
                }
            }
        }
    }

    public static void I64MemoryPressure()
    {
        var source = new StringBuilder("kernel sparse(vector<i64> input, vector<i64> addresses, vector<i64> address) -> vector<i64> {\n");
        for (var index = 0; index < 36; index++) source.AppendLine($"let v{index} = input + {index};");
        for (var index = 0; index < 12; index++) source.AppendLine($"let m{index} = input < {index};");
        source.AppendLine("""
            let active = input < 47;
            scatter_i64(addresses, input, active);
            let gathered = gather_i64(addresses, active);
            let snapshot = load_i64(address);
            store(address, input + 100);
            var sum = input - input;
            """);
        for (var index = 0; index < 36; index++) source.AppendLine($"sum = sum + v{index};");
        for (var index = 0; index < 12; index++) source.AppendLine($"sum = sum + select(m{index}, input, 0);");
        source.AppendLine("return sum + gathered + snapshot; }");
        foreach (var optimize in new[] { false, true })
        {
            var compilation = Compile(source.ToString(), optimize);
            Require(compilation.Module!.VectorSpillSlots > 0 && compilation.Module.MaskSpillSlots > 0, "Expected mixed i64 spills");
            foreach (var backend in Backends())
            {
                var instance = RoundTrip(compilation.Module).Instantiate(new VmOptions { MemorySize = 64 }, backend);
                for (var lane = 0; lane < 8; lane++)
                {
                    instance.State.Register(0).WriteUInt64(lane, unchecked((ulong)(lane * 13 - 10)));
                    instance.State.Register(1).WriteUInt64(lane, (ulong)((7 - lane) * 8));
                }
                Require(instance.Execute().Succeeded, "i64 pressure kernel trapped");
                for (var lane = 0; lane < 8; lane++)
                {
                    var input = lane * 13 - 10;
                    var reversed = (7 - lane) * 13 - 10;
                    var count = Enumerable.Range(0, 12).Count(threshold => input < threshold);
                    var expected = 36 * input + 630 + count * input + (input < 47 ? input : 0) + (reversed < 47 ? reversed : 0);
                    Require(instance.State.Register(0).ReadUInt64(lane) == unchecked((ulong)expected), "i64 sparse spill scalar mismatch");
                    Require(BinaryPrimitives.ReadInt64LittleEndian(instance.State.LinearMemory.Span.Slice(lane * 8)) == input + 100,
                        "i64 spilling changed memory");
                }
            }
        }
    }

    private static CompilationResult Compile(string source, bool optimize)
    {
        var options = new CompilationOptions { Optimize = optimize };
        var result = CascadeCompiler.Compile(source, options);
        Require(result.Succeeded, string.Join("; ", result.Diagnostics));
        var repeat = CascadeCompiler.Compile(source, options);
        Require(repeat.Succeeded && repeat.Module!.Instructions.SequenceEqual(result.Module!.Instructions), "Spill lowering is nondeterministic");
        Require(result.Metrics!.SpillStores == result.Module!.Instructions.Count(i => i.Opcode is Opcode.SpillStoreVector or Opcode.SpillStoreMask), "Spill store metric mismatch");
        Require(result.Metrics.SpillReloads == result.Module.Instructions.Count(i => i.Opcode is Opcode.SpillLoadVector or Opcode.SpillLoadMask), "Spill reload metric mismatch");
        return result;
    }

    private static CascadeProgram RoundTrip(CascadeModule module)
    {
        using var stream = new MemoryStream();
        CvmModuleFormat.Write(stream, module);
        Require(stream.ToArray()[5] == 1, "Spilled compiler output must use CVM1 1.1");
        stream.Position = 0;
        return CascadeProgram.Load(stream);
    }

    private static ExecutionBackend[] Backends() => SimdInterpreter.IsSupported
        ? [ExecutionBackend.Reference, ExecutionBackend.HardwareSimd] : [ExecutionBackend.Reference];

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
