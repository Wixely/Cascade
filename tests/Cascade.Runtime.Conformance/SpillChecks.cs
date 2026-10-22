using System.Buffers.Binary;
using Cascade.Core;
using Cascade.Runtime;

internal static class SpillChecks
{
    private static CascadeModule Module() => new([
        new(Opcode.SpillLoadVector, Destination: 4),
        new(Opcode.SpillLoadMask, Destination: 4),
        new(Opcode.SplatI32, Destination: 0, Immediate: 42),
        new(Opcode.SpillStoreVector, SourceA: 0),
        new(Opcode.CompareEqualI32, SourceA: 0, SourceB: 0, Mask: 0),
        new(Opcode.SpillStoreMask, Mask: 0),
        new(Opcode.Zero, Destination: 0),
        new(Opcode.ConvertMask64, Destination: 0, Mask: 0),
        new(Opcode.SpillLoadVector, Destination: 1),
        new(Opcode.SpillLoadMask, Destination: 1),
        new(Opcode.SelectI32, Destination: 2, SourceA: 1, SourceB: 0, Mask: 1),
        new(Opcode.Store, SourceA: 3, SourceB: 2),
        new(Opcode.Halt),
    ], vectorSpillSlots: 1, maskSpillSlots: 1);

    public static void Execution()
    {
        foreach (var (convert, width, bits) in new[]
        {
            (Opcode.ConvertMask8, (byte)8, 0xFFUL), (Opcode.ConvertMask16, (byte)16, 0xFFFFUL),
            (Opcode.ConvertMask32, (byte)32, 0xFFFFUL), (Opcode.ConvertMask64, (byte)64, 0xFFFFUL),
        })
        {
            var maskProgram = CascadeProgram.FromModule(new CascadeModule([
                new(Opcode.CompareEqualI32, SourceA: 0, SourceB: 0, Mask: 0),
                new(convert, Destination: 1, Mask: 0),
                new(Opcode.SpillStoreMask, Mask: 1),
                new(Opcode.SpillLoadMask, Destination: 2),
                new(Opcode.Halt),
            ], maskSpillSlots: 1));
            var maskInstance = maskProgram.Instantiate(new VmOptions { MemorySize = 0 });
            Require(maskInstance.Execute().Succeeded && maskInstance.State.Mask(2) == new PredicateMask(bits, width), "Spill lost converted mask width");
        }
        var program = CascadeProgram.FromModule(Module());
        for (ulong fuel = 0; fuel <= 14; fuel++)
            foreach (var backend in SimdInterpreter.IsSupported
                ? new[] { ExecutionBackend.Reference, ExecutionBackend.HardwareSimd } : [ExecutionBackend.Reference])
            {
                var reference = program.Instantiate(new VmOptions { MemorySize = 64, Fuel = fuel, MaximumSpillBytes = 80 });
                var candidate = program.Instantiate(new VmOptions { MemorySize = 64, Fuel = fuel, MaximumSpillBytes = 80 }, backend);
                for (var pass = 0; pass < 2; pass++)
                {
                    reference.Restart();
                    candidate.Restart();
                    var expected = reference.Execute();
                    var result = candidate.Execute();
                    Require(result == expected, "Backend result mismatch");
                    Require(result.Succeeded == (fuel >= 13), "Fuel success boundary");
                    Require(result.FuelConsumed == Math.Min(fuel, 13UL), "Exact fuel charge");
                    Require(candidate.State.InstructionPointer == (int)Math.Min(fuel, 12UL), "Instruction pointer boundary");
                    for (var register = 0; register < 32; register++)
                        Require(candidate.State.Register(register).Bytes.SequenceEqual(reference.State.Register(register).Bytes), "Register mismatch");
                    for (var mask = 0; mask < 8; mask++) Require(candidate.State.Mask(mask) == reference.State.Mask(mask), "Mask mismatch");
                    Require(candidate.State.LinearMemory.Span.SequenceEqual(reference.State.LinearMemory.Span), "Memory mismatch");
                    if (fuel >= 1) Require(candidate.State.Register(4).ReadUInt64(0) == 0, "Private vector slots not cleared on restart");
                    if (fuel >= 2) Require(candidate.State.Mask(4) == new PredicateMask(0, 64), "Private masks not initialized on restart");
                    if (fuel >= 9) Require(candidate.State.Register(1).ReadUInt32(0) == 42, "Vector snapshot lost");
                    if (fuel >= 10) Require(candidate.State.Mask(1) == new PredicateMask(0xFFFF, 16), "Mask width snapshot lost");
                    if (fuel >= 12) Require(BinaryPrimitives.ReadUInt32LittleEndian(candidate.State.LinearMemory.Span) == 42, "Spill output store");
                    if (result.Succeeded)
                    {
                        candidate.Restart();
                        Require(BinaryPrimitives.ReadUInt32LittleEndian(candidate.State.LinearMemory.Span) == 42, "Restart changed linear memory");
                    }
                }
            }
        var isolated = program.Instantiate(new VmOptions { MemorySize = 64, Fuel = 2 });
        isolated.Execute();
        Require(isolated.State.Register(4).ReadUInt64(0) == 0 && isolated.State.Mask(4) == new PredicateMask(0, 64), "Instance isolation");
        var batchProgram = CascadeProgram.FromModule(new CascadeModule([
            new(Opcode.SpillLoadVector, Destination: 0),
            new(Opcode.SplatI64, Destination: 1, Immediate: 9),
            new(Opcode.SpillStoreVector, SourceA: 1),
            new(Opcode.Halt),
        ], vectorSpillSlots: 1));
        var outputs = new byte[128];
        var batch = batchProgram.Instantiate(new VmOptions { MemorySize = 0 }).ExecuteBatch([], outputs, 0);
        Require(batch.Succeeded && batch.CompletedCount == 2 && batch.FuelConsumed == 8 && outputs.AsSpan().IndexOfAnyExcept((byte)0) == -1,
            "Batch restart did not clear private slots");
        var callModule = new CascadeModule([
            new(Opcode.SplatI64, Destination: 0, Immediate: 7),
            new(Opcode.SpillStoreVector, SourceA: 0),
            new(Opcode.Call, Immediate: 5),
            new(Opcode.SpillLoadVector, Destination: 1),
            new(Opcode.Halt),
            new(Opcode.SplatI64, Destination: 0, Immediate: 9),
            new(Opcode.SpillStoreVector, SourceA: 0),
            new(Opcode.Return),
        ], functions: [new(5, 8)], vectorSpillSlots: 1);
        var caller = CascadeProgram.FromModule(callModule).Instantiate();
        Require(caller.Execute().Succeeded && caller.State.Register(1).ReadUInt64(0) == 9, "Caller-clobbered slot convention");
    }

    public static void FormatAndLimits()
    {
        var module = Module();
        using var stream = new MemoryStream();
        CvmModuleFormat.Write(stream, module);
        var bytes = stream.ToArray();
        Require(bytes[4] == 1 && bytes[5] == 1, "Expected CVM1 1.1");
        stream.Position = 0;
        var loaded = CvmModuleFormat.Read(stream);
        Require(loaded.VectorSpillSlots == 1 && loaded.MaskSpillSlots == 1 && loaded.SpillStorageBytes == 80, "Metadata round trip");
        using var again = new MemoryStream();
        CvmModuleFormat.Write(again, loaded);
        Require(bytes.AsSpan().SequenceEqual(again.ToArray()), "Noncanonical spill serialization");
        var spillOffset = (int)BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan(64));
        RejectMutation(data => data[5] = 0);
        RejectMutation(data => data[5] = 2);
        RejectMutation(data => data[60] = 0);
        RejectMutation(data => data[spillOffset] = 0);
        RejectMutation(data => data[spillOffset + 4] = 1);
        RejectMutation(data => BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(spillOffset + 8), uint.MaxValue));
        RejectMutation(data => BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(spillOffset + 12), uint.MaxValue));
        RejectMutation(data => data.AsSpan(spillOffset + 8, 8).Clear());
        RejectMutation(data => { data[56] = 6; data[60] = 0; });
        using var optionalStream = new MemoryStream();
        CvmModuleFormat.Write(optionalStream, new CascadeModule([new(Opcode.Halt)], vectorSpillSlots: 1));
        var oldOptional = optionalStream.ToArray();
        oldOptional[5] = 0;
        oldOptional[60] = 0;
        Require(CvmModuleFormat.Read(new MemoryStream(oldOptional)).SpillStorageBytes == 0, "1.0 unknown optional section compatibility");
        Require(ModuleValidator.Validate(new CascadeModule([new(Opcode.SpillLoadVector), new(Opcode.Halt)])).Count != 0, "Undeclared vector slot accepted");
        Require(ModuleValidator.Validate(new CascadeModule([new(Opcode.SpillLoadMask, Immediate: 1), new(Opcode.Halt)], maskSpillSlots: 1)).Count != 0, "Out of range mask slot accepted");
        RejectArgument(() => CascadeProgram.FromModule(module).Instantiate(new VmOptions { MaximumSpillBytes = 79 }));
        RejectArgument(() => new CascadeModule([new(Opcode.Halt)], vectorSpillSlots: -1));
        RejectArgument(() => new CascadeModule([new(Opcode.Halt)], maskSpillSlots: CascadeModule.MaximumSpillSlots + 1));
        var limitModule = new CascadeModule([new(Opcode.SpillLoadVector, Immediate: 65_535), new(Opcode.SpillLoadMask, Immediate: 65_535), new(Opcode.Halt)],
            vectorSpillSlots: 65_536, maskSpillSlots: 65_536);
        Require(CascadeProgram.FromModule(limitModule).Instantiate(new VmOptions { MemorySize = 0, MaximumSpillBytes = limitModule.SpillStorageBytes }).Execute().Succeeded,
            "Exact implementation limit rejected");

        void RejectMutation(Action<byte[]> mutate)
        {
            var corrupted = bytes.ToArray();
            mutate(corrupted);
            try { CvmModuleFormat.Read(new MemoryStream(corrupted)); }
            catch (InvalidDataException) { return; }
            throw new InvalidOperationException("Malformed spill module accepted");
        }
    }

    private static void RejectArgument(Action action)
    {
        try { action(); }
        catch (ArgumentException) { return; }
        throw new InvalidOperationException("Invalid spill argument accepted");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
