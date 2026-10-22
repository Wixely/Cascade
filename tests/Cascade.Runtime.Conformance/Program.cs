using System.Buffers.Binary;
using Cascade.Core;
using Cascade.Runtime;

if (args is ["--write-golden", var fixtureDirectory]) return WriteGoldenFixtures(fixtureDirectory);

var tests = new (string Name, Action Body)[]
{
    ("wrapping arithmetic and register aliasing", Arithmetic),
    ("data, subtraction, and bitwise operations", DataAndBitwise),
    ("signed comparisons, masks, and selection", MasksAndSelection),
    ("mask branches and i64 selection", MaskBranches),
    ("mask conversions truncate and zero-extend", MaskConversions),
    ("load and store", LoadStore),
    ("i32 gather and i64 scatter", GatherAndScatterVariants),
    ("inactive gather lanes preserve destination", GatherMerge),
    ("scatter is atomic on invalid address", ScatterAtomicity),
    ("overlapping scatter uses ascending lane order", ScatterOrder),
    ("branches and calls", ControlFlow),
    ("fuel bounds execution", Fuel),
    ("call stack bounds execution", CallStack),
    ("empty return traps with stack underflow", EmptyReturn),
    ("mask widths are enforced", MaskWidth),
    ("boundary accesses and invalid modules fail safely", BoundaryAndValidation),
    ("module format round trips and rejects malformed data", ModuleFormat),
    ("golden CVM1 fixtures remain byte-stable", GoldenFixtures),
    ("constants and entry points execute", ConstantsAndEntryPoint),
    ("embedding facade loads, saves, and isolates instances", EmbeddingFacade),
    ("validated modules cannot be mutated through collection casts", ImmutableModules),
    ("embedding facade selects execution backends", BackendSelection),
    ("instances restart with reusable data state", InstanceRestart),
    ("batch execution matches manual restart and preserves failure boundaries", BatchExecution),
    ("batch validation rejects malformed buffers before execution", BatchValidation),
    ("batch invocations retain memory and charge host imports", BatchStateAndImports),
    ("host imports bind and precharge fuel", HostImports),
    ("host import failures are bounded and redacted", HostImportFailures),
    ("deterministic replay", Replay),
};

var failed = 0;
foreach (var (name, body) in tests)
{
    try
    {
        body();
        Console.WriteLine($"PASS {name}");
    }
    catch (Exception exception)
    {
        failed++;
        Console.Error.WriteLine($"FAIL {name}: {exception.Message}");
    }
}
Console.WriteLine($"{tests.Length - failed}/{tests.Length} conformance cases passed");
return failed == 0 ? 0 : 1;

static void Arithmetic()
{
    var vm = Vm(
        new(Opcode.SplatI32, Destination: 0, Immediate: uint.MaxValue),
        new(Opcode.SplatI32, Destination: 1, Immediate: 2),
        new(Opcode.AddI32, Destination: 0, SourceA: 0, SourceB: 1),
        new(Opcode.MulI32, Destination: 0, SourceA: 0, SourceB: 1),
        new(Opcode.Halt));
    Success(vm.Execute());
    Equal(2U, vm.State.Register(0).ReadUInt32(0));
}

static void DataAndBitwise()
{
    var vm = Vm(
        new(Opcode.SplatI32, Destination: 0, Immediate: 0x0F0F),
        new(Opcode.SplatI32, Destination: 1, Immediate: 0x00FF),
        new(Opcode.And, Destination: 2, SourceA: 0, SourceB: 1),
        new(Opcode.Or, Destination: 3, SourceA: 0, SourceB: 1),
        new(Opcode.Xor, Destination: 4, SourceA: 0, SourceB: 1),
        new(Opcode.Not, Destination: 5, SourceA: 0),
        new(Opcode.Move, Destination: 6, SourceA: 2),
        new(Opcode.Zero, Destination: 7),
        new(Opcode.SubI32, Destination: 8, SourceA: 0, SourceB: 1),
        new(Opcode.SplatI64, Destination: 9, Immediate: 1),
        new(Opcode.SplatI64, Destination: 10, Immediate: 2),
        new(Opcode.SubI64, Destination: 9, SourceA: 9, SourceB: 10),
        new(Opcode.Halt));
    Success(vm.Execute());
    Equal(0x000FU, vm.State.Register(2).ReadUInt32(0));
    Equal(0x0FFFU, vm.State.Register(3).ReadUInt32(0));
    Equal(0x0FF0U, vm.State.Register(4).ReadUInt32(0));
    Equal(unchecked((uint)~0x0F0F), vm.State.Register(5).ReadUInt32(0));
    Equal(vm.State.Register(2).ReadUInt32(0), vm.State.Register(6).ReadUInt32(0));
    Equal(0U, vm.State.Register(7).ReadUInt32(0));
    Equal(0x0E10U, vm.State.Register(8).ReadUInt32(0));
    Equal(ulong.MaxValue, vm.State.Register(9).ReadUInt64(0));
}

static void MasksAndSelection()
{
    var vm = Vm(
        new(Opcode.SplatI32, Destination: 0, Immediate: unchecked((uint)-1)),
        new(Opcode.SplatI32, Destination: 1, Immediate: 1),
        new(Opcode.CompareLessThanI32, SourceA: 0, SourceB: 1, Mask: 2),
        new(Opcode.SelectI32, Destination: 2, SourceA: 0, SourceB: 1, Mask: 2),
        new(Opcode.Halt));
    Success(vm.Execute());
    Equal((byte)16, vm.State.Mask(2).Width);
    Equal(0xFFFFUL, vm.State.Mask(2).ActiveBits);
    Equal(uint.MaxValue, vm.State.Register(2).ReadUInt32(15));
}

static void MaskBranches()
{
    var vm = Vm(
        new(Opcode.SplatI64, Destination: 0, Immediate: 1),
        new(Opcode.SplatI64, Destination: 1, Immediate: 2),
        new(Opcode.CompareLessThanI64, SourceA: 0, SourceB: 1, Mask: 0),
        new(Opcode.SelectI64, Destination: 2, SourceA: 1, SourceB: 0, Mask: 0),
        new(Opcode.BranchAny, Mask: 0, Immediate: 6),
        new(Opcode.SplatI64, Destination: 2, Immediate: 99),
        new(Opcode.BranchAll, Mask: 0, Immediate: 8),
        new(Opcode.SplatI64, Destination: 2, Immediate: 99),
        new(Opcode.Halt));
    Success(vm.Execute());
    Equal(2UL, vm.State.Register(2).ReadUInt64(0));
}

static void MaskConversions()
{
    var vm = Vm(
        new(Opcode.CompareEqualI32, SourceA: 0, SourceB: 1, Mask: 0),
        new(Opcode.ConvertMask8, Destination: 1, Mask: 0),
        new(Opcode.ConvertMask64, Destination: 2, Mask: 1),
        new(Opcode.ConvertMask16, Destination: 3, Mask: 2),
        new(Opcode.ConvertMask32, Destination: 4, Mask: 3),
        new(Opcode.Halt));
    Success(vm.Execute());
    Equal(new PredicateMask(0xFF, 8), vm.State.Mask(1));
    Equal(new PredicateMask(0xFF, 64), vm.State.Mask(2));
    True(!vm.State.Mask(2).All, "Widening must zero-fill new active bits.");
    Equal(new PredicateMask(0xFF, 16), vm.State.Mask(3));
    Equal(new PredicateMask(0xFF, 32), vm.State.Mask(4));
}

static void LoadStore()
{
    var vm = Vm(
        new(Opcode.SplatI64, Destination: 0, Immediate: 4),
        new(Opcode.SplatI64, Destination: 1, Immediate: 0xAABBCCDDEEFF0011),
        new(Opcode.Store, SourceA: 0, SourceB: 1),
        new(Opcode.Load, Destination: 2, SourceA: 0),
        new(Opcode.Halt));
    Success(vm.Execute());
    Equal(0xAABBCCDDEEFF0011UL, vm.State.Register(2).ReadUInt64(7));
}

static void GatherAndScatterVariants()
{
    var vm = VmWithOptions(new() { MemorySize = 256 },
        new(Opcode.CompareEqualI32, SourceA: 2, SourceB: 3, Mask: 0),
        new(Opcode.Store, SourceA: 0, SourceB: 4),
        new(Opcode.GatherI32, Destination: 5, SourceA: 0, Mask: 0),
        new(Opcode.CompareEqualI64, SourceA: 2, SourceB: 3, Mask: 1),
        new(Opcode.ScatterI64, SourceA: 6, SourceB: 7, Mask: 1),
        new(Opcode.Load, Destination: 8, SourceA: 6),
        new(Opcode.Halt));
    vm.State.Register(4).WriteUInt32(0, 123);
    for (var lane = 0; lane < 8; lane++)
    {
        vm.State.Register(6).WriteUInt64(lane, 64);
        vm.State.Register(7).WriteUInt64(lane, (ulong)(10 + lane));
    }
    Success(vm.Execute());
    for (var lane = 0; lane < 16; lane++) Equal(123U, vm.State.Register(5).ReadUInt32(lane));
    Equal(17UL, vm.State.Register(8).ReadUInt64(0));
}

static void GatherMerge()
{
    var vm = Vm(
        new(Opcode.Store, SourceA: 0, SourceB: 5),
        new(Opcode.CompareEqualI64, SourceA: 2, SourceB: 3, Mask: 0),
        new(Opcode.GatherI64, Destination: 4, SourceA: 0, Mask: 0),
        new(Opcode.Halt));
    for (var lane = 0; lane < 8; lane++)
    {
        vm.State.Register(4).WriteUInt64(lane, 99);
        vm.State.Register(5).WriteUInt64(lane, 42);
        if (lane != 0) vm.State.Register(3).WriteUInt64(lane, 1);
    }
    Success(vm.Execute());
    Equal(42UL, vm.State.Register(4).ReadUInt64(0));
    for (var lane = 1; lane < 8; lane++) Equal(99UL, vm.State.Register(4).ReadUInt64(lane));
}

static void ScatterAtomicity()
{
    var vm = VmWithOptions(new() { MemorySize = 128 },
        new(Opcode.CompareEqualI32, SourceA: 2, SourceB: 3, Mask: 0),
        new(Opcode.ScatterI32, SourceA: 0, SourceB: 4, Mask: 0),
        new(Opcode.Halt));
    for (var lane = 0; lane < 8; lane++)
    {
        vm.State.Register(0).WriteUInt64(lane, (ulong)(lane * 4));
        vm.State.Register(1).WriteUInt64(lane, (ulong)((lane + 8) * 4));
    }
    vm.State.Register(1).WriteUInt64(7, 126);
    for (var lane = 0; lane < 16; lane++) vm.State.Register(4).WriteUInt32(lane, (uint)(lane + 1));
    var result = vm.Execute();
    Equal(TrapCode.InvalidMemoryAccess, result.Trap?.Code);
    True(vm.State.LinearMemory.Span.SequenceEqual(new byte[128]), "Scatter wrote before validating all addresses.");
}

static void ScatterOrder()
{
    var vm = VmWithOptions(new() { MemorySize = 64 },
        new(Opcode.CompareEqualI32, SourceA: 2, SourceB: 3, Mask: 0),
        new(Opcode.ScatterI32, SourceA: 0, SourceB: 4, Mask: 0),
        new(Opcode.Halt));
    for (var lane = 0; lane < 16; lane++) vm.State.Register(4).WriteUInt32(lane, (uint)(100 + lane));
    Success(vm.Execute());
    Equal((byte)115, vm.State.LinearMemory.Span[0]);
}

static void ControlFlow()
{
    var vm = Vm(
        new(Opcode.Call, Immediate: 3),
        new(Opcode.Jump, Immediate: 5),
        new(Opcode.Halt),
        new(Opcode.SplatI64, Destination: 0, Immediate: 42),
        new(Opcode.Return),
        new(Opcode.Halt));
    Success(vm.Execute());
    Equal(42UL, vm.State.Register(0).ReadUInt64(0));
}

static void Fuel()
{
    var vm = VmWithOptions(new VmOptions { Fuel = 3 }, new Instruction(Opcode.Jump, Immediate: 0));
    var result = vm.Execute();
    Equal(TrapCode.FuelExhausted, result.Trap?.Code);
    Equal(3UL, result.FuelConsumed);
}

static void CallStack()
{
    var vm = VmWithOptions(new VmOptions { MaximumCallDepth = 2 }, new Instruction(Opcode.Call, Immediate: 0));
    var result = vm.Execute();
    Equal(TrapCode.StackOverflow, result.Trap?.Code);
    Equal(3UL, result.FuelConsumed);
}

static void EmptyReturn()
{
    var vm = Vm(new Instruction(Opcode.Return));
    Equal(TrapCode.StackUnderflow, vm.Execute().Trap?.Code);
}

static void MaskWidth()
{
    var vm = Vm(
        new(Opcode.CompareEqualI32, SourceA: 0, SourceB: 1, Mask: 0),
        new(Opcode.SelectI64, Destination: 2, SourceA: 0, SourceB: 1, Mask: 0),
        new(Opcode.Halt));
    Equal(TrapCode.InvalidInstruction, vm.Execute().Trap?.Code);
}

static void BoundaryAndValidation()
{
    var exact = VmWithOptions(new() { MemorySize = VectorRegister.ByteCount },
        new(Opcode.Store, SourceA: 0, SourceB: 1),
        new(Opcode.Load, Destination: 2, SourceA: 0),
        new(Opcode.Halt));
    exact.State.Register(1).WriteUInt64(0, 0xAABBCCDD);
    Success(exact.Execute());
    Equal(0xAABBCCDDUL, exact.State.Register(2).ReadUInt64(0));

    foreach (var invalidAddress in new[] { 1UL, ulong.MaxValue })
    {
        var invalid = VmWithOptions(new() { MemorySize = VectorRegister.ByteCount },
            new(Opcode.Store, SourceA: 0, SourceB: 1), new(Opcode.Halt));
        invalid.State.Register(0).WriteUInt64(0, invalidAddress);
        invalid.State.Register(1).WriteUInt64(0, 42);
        Equal(TrapCode.InvalidMemoryAccess, invalid.Execute().Trap?.Code);
        True(invalid.State.LinearMemory.Span.SequenceEqual(new byte[VectorRegister.ByteCount]), "Invalid store had a partial effect.");
    }

    HasValidationError(new CascadeModule([new(Opcode.Jump, Immediate: 2), new(Opcode.Halt)]), "Branch target");
    HasValidationError(new CascadeModule([new(Opcode.Halt)], functions: [new(0, 1), new(0, 1)]), "overlaps");
    var duplicate = new ImportDefinition("test", "same", 1, 1);
    HasValidationError(new CascadeModule([new(Opcode.Halt)], imports: [duplicate, duplicate]), "duplicates");
    HasValidationError(new CascadeModule([new(Opcode.LoadConstant, Immediate: 0), new(Opcode.Halt)]), "Constant index");
}

static void ModuleFormat()
{
    var constantBytes = new byte[VectorRegister.ByteCount];
    BinaryPrimitives.WriteUInt64LittleEndian(constantBytes, 42);
    var module = new CascadeModule(
        [new(Opcode.LoadConstant, Destination: 0, Immediate: 0), new(Opcode.HostCall, Immediate: 0), new(Opcode.Halt)],
        [new VectorConstant(constantBytes)],
        [new FunctionDefinition(0, 3)],
        [new ImportDefinition("test", "noop", 1, 5)]);
    using var stream = new MemoryStream();
    CvmModuleFormat.Write(stream, module);
    stream.Position = 0;
    var loaded = CvmModuleFormat.Read(stream);
    SequenceEqual(module.Instructions.ToArray(), loaded.Instructions.ToArray());
    Equal(1, loaded.Constants.Count);
    True(module.Constants[0].Bytes.SequenceEqual(loaded.Constants[0].Bytes), "Constant bytes differ.");
    SequenceEqual(module.Functions.ToArray(), loaded.Functions.ToArray());
    SequenceEqual(module.Imports.ToArray(), loaded.Imports.ToArray());
    using var rewritten = new MemoryStream();
    CvmModuleFormat.Write(rewritten, loaded);
    True(stream.ToArray().SequenceEqual(rewritten.ToArray()), "Canonical module bytes were not deterministic.");

    var bytes = stream.ToArray();
    bytes[28] = 1;
    Throws<InvalidDataException>(() => CvmModuleFormat.Read(new MemoryStream(bytes)));
    Throws<InvalidDataException>(() => CvmModuleFormat.Read(new MemoryStream(bytes[..10])));

    bytes = stream.ToArray();
    BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(40), 136); // Move the first section away from its canonical offset.
    Throws<InvalidDataException>(() => CvmModuleFormat.Read(new MemoryStream(bytes)));

    bytes = stream.ToArray();
    BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(104), 99);
    BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(108), 1); // Unknown required section.
    Throws<InvalidDataException>(() => CvmModuleFormat.Read(new MemoryStream(bytes)));

    bytes = stream.ToArray();
    BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(120), ulong.MaxValue);
    Throws<InvalidDataException>(() => CvmModuleFormat.Read(new MemoryStream(bytes)));

    var minimal = new CascadeModule([new(Opcode.Halt)]);
    using var minimalStream = new MemoryStream();
    CvmModuleFormat.Write(minimalStream, minimal);
    var original = minimalStream.ToArray();
    var extended = new byte[original.Length + 32];
    original.AsSpan(0, 32).CopyTo(extended);
    BinaryPrimitives.WriteUInt32LittleEndian(extended.AsSpan(12), 2);
    BinaryPrimitives.WriteUInt32LittleEndian(extended.AsSpan(20), 48);
    original.AsSpan(32, 24).CopyTo(extended.AsSpan(32));
    BinaryPrimitives.WriteUInt64LittleEndian(extended.AsSpan(40), 80);
    BinaryPrimitives.WriteUInt32LittleEndian(extended.AsSpan(56), 99);
    BinaryPrimitives.WriteUInt64LittleEndian(extended.AsSpan(64), (ulong)(original.Length + 24));
    BinaryPrimitives.WriteUInt64LittleEndian(extended.AsSpan(72), 8);
    original.AsSpan(56).CopyTo(extended.AsSpan(80));
    var extendedModule = CvmModuleFormat.Read(new MemoryStream(extended));
    Equal(Opcode.Halt, extendedModule.Instructions[0].Opcode);
}

static void GoldenFixtures()
{
    var directory = Path.Combine(AppContext.BaseDirectory, "Fixtures");
    foreach (var name in new[] { "minimal.cvm", "complete.cvm" })
    {
        var bytes = File.ReadAllBytes(Path.Combine(directory, name));
        using var input = new MemoryStream(bytes, writable: false);
        var module = CvmModuleFormat.Read(input);
        using var output = new MemoryStream();
        CvmModuleFormat.Write(output, module);
        True(bytes.SequenceEqual(output.ToArray()), $"Golden fixture {name} changed.");
        for (var length = 0; length < bytes.Length; length++)
            Throws<InvalidDataException>(() => CvmModuleFormat.Read(new MemoryStream(bytes[..length])));
    }
}

static void ConstantsAndEntryPoint()
{
    var bytes = Enumerable.Repeat((byte)0xA5, VectorRegister.ByteCount).ToArray();
    var module = new CascadeModule(
        [new(Opcode.SplatI64, Destination: 0, Immediate: 1), new(Opcode.LoadConstant, Destination: 0, Immediate: 0), new(Opcode.Halt)],
        [new VectorConstant(bytes)], entryPoint: 1);
    var vm = new ReferenceInterpreter(module);
    Success(vm.Execute());
    True(vm.State.Register(0).Bytes.SequenceEqual(bytes), "Entry point or constant load was incorrect.");
    Equal(2UL, vm.State.InitialFuel - vm.State.FuelRemaining);
}

static void EmbeddingFacade()
{
    var module = new CascadeModule([new(Opcode.SplatI64, Destination: 0, Immediate: 42), new(Opcode.Halt)]);
    var program = CascadeProgram.FromModule(module);
    using var encoded = new MemoryStream();
    program.Save(encoded);
    encoded.Position = 0;
    var loaded = CascadeProgram.Load(encoded);
    Equal(2, loaded.InstructionCount);
    var first = loaded.Instantiate();
    var second = loaded.Instantiate();
    Success(first.Execute());
    Success(second.Execute());
    first.State.Register(0).WriteUInt64(0, 99);
    Equal(42UL, second.State.Register(0).ReadUInt64(0));
}

static void BackendSelection()
{
    var program = CascadeProgram.FromModule(new CascadeModule([
        new(Opcode.SplatI32, Destination: 0, Immediate: 21),
        new(Opcode.AddI32, Destination: 0, SourceA: 0, SourceB: 0),
        new(Opcode.Halt),
    ]));
    var reference = program.Instantiate(backend: ExecutionBackend.Reference);
    var automatic = program.Instantiate(backend: ExecutionBackend.Automatic);
    Success(reference.Execute());
    Success(automatic.Execute());
    Equal(ExecutionBackend.Reference, reference.Backend);
    Equal(SimdInterpreter.IsSupported ? ExecutionBackend.HardwareSimd : ExecutionBackend.Reference, automatic.Backend);
    True(reference.State.Register(0).Bytes.SequenceEqual(automatic.State.Register(0).Bytes), "Automatic backend differs from reference output.");

    if (SimdInterpreter.IsSupported)
    {
        var simd = program.Instantiate(backend: ExecutionBackend.HardwareSimd);
        Success(simd.Execute());
        Equal(42U, simd.State.Register(0).ReadUInt32(15));
    }
    else
    {
        Throws<PlatformNotSupportedException>(() => program.Instantiate(backend: ExecutionBackend.HardwareSimd));
    }
}

static void InstanceRestart()
{
    var program = CascadeProgram.FromModule(new CascadeModule([
        new(Opcode.CompareEqualI32, SourceA: 0, SourceB: 1, Mask: 0),
        new(Opcode.AddI32, Destination: 0, SourceA: 0, SourceB: 1),
        new(Opcode.Halt),
    ]));
    var backends = SimdInterpreter.IsSupported
        ? new[] { ExecutionBackend.Reference, ExecutionBackend.HardwareSimd }
        : new[] { ExecutionBackend.Reference };

    foreach (var backend in backends)
    {
        var instance = program.Instantiate(new VmOptions { Fuel = 10 }, backend);
        instance.State.Register(0).WriteUInt32(0, 20);
        instance.State.Register(1).WriteUInt32(0, 22);
        Success(instance.Execute());
        Equal(42U, instance.State.Register(0).ReadUInt32(0));
        Equal((byte)16, instance.State.Mask(0).Width);

        instance.State.Register(2).WriteUInt32(0, 99);
        instance.Restart();
        Equal(42U, instance.State.Register(0).ReadUInt32(0));
        Equal(99U, instance.State.Register(2).ReadUInt32(0));
        Equal(new PredicateMask(0, 64), instance.State.Mask(0));
        Equal(10UL, instance.State.FuelRemaining);
        Equal(0, instance.State.InstructionPointer);

        instance.State.Register(0).WriteUInt32(0, 10);
        instance.State.Register(1).WriteUInt32(0, 5);
        Success(instance.Execute());
        Equal(15U, instance.State.Register(0).ReadUInt32(0));
    }
}

static void HostImports()
{
    var import = new ImportDefinition("test", "answer", 1, 5);
    var key = new ImportKey(import.Namespace, import.Name, import.AbiVersion);
    var options = new VmOptions
    {
        Fuel = 10,
        ImportBindings = new Dictionary<ImportKey, HostImportHandler>
        {
            [key] = context =>
            {
                context.Register(0).WriteUInt64(0, 42);
                context.LinearMemory.Span[0] = 7;
                return HostCallStatus.Success;
            },
        },
    };
    var vm = new ReferenceInterpreter(new CascadeModule([new(Opcode.HostCall, Immediate: 0), new(Opcode.Halt)], imports: [import]), options);
    var result = vm.Execute();
    Success(result);
    Equal(7UL, result.FuelConsumed);
    Equal(42UL, vm.State.Register(0).ReadUInt64(0));
    Equal((byte)7, vm.State.LinearMemory.Span[0]);
}

static void HostImportFailures()
{
    var import = new ImportDefinition("test", "failure", 1, 5);
    var module = new CascadeModule([new(Opcode.HostCall, Immediate: 0), new(Opcode.Halt)], imports: [import]);
    var missing = new ReferenceInterpreter(module).Execute();
    Equal(TrapCode.InvalidHostImport, missing.Trap?.Code);
    Equal(0UL, missing.FuelConsumed);

    var invoked = false;
    var key = new ImportKey(import.Namespace, import.Name, import.AbiVersion);
    HostImportHandler handler = _ => { invoked = true; return HostCallStatus.Success; };
    var underfunded = new ReferenceInterpreter(module, new VmOptions
    {
        Fuel = 5,
        ImportBindings = new Dictionary<ImportKey, HostImportHandler> { [key] = handler },
    }).Execute();
    Equal(TrapCode.FuelExhausted, underfunded.Trap?.Code);
    True(!invoked, "Underfunded import was invoked.");

    HostImportHandler throwing = _ => throw new InvalidOperationException("sensitive host detail");
    var failed = new ReferenceInterpreter(module, new VmOptions
    {
        ImportBindings = new Dictionary<ImportKey, HostImportHandler> { [key] = throwing },
    }).Execute();
    Equal(TrapCode.HostCallFailed, failed.Trap?.Code);
    True(!failed.Trap!.Message.Contains("sensitive", StringComparison.Ordinal), "Host exception details leaked.");
}

static void Replay()
{
    var instructions = new[]
    {
        new Instruction(Opcode.SplatI64, Destination: 0, Immediate: 7),
        new Instruction(Opcode.SplatI64, Destination: 1, Immediate: 6),
        new Instruction(Opcode.AddI64, Destination: 0, SourceA: 0, SourceB: 1),
        new Instruction(Opcode.Halt),
    };
    var first = Vm(instructions);
    var second = Vm(instructions);
    Equal(first.Execute(), second.Execute());
    for (var register = 0; register < 32; register++)
        True(first.State.Register(register).Bytes.SequenceEqual(second.State.Register(register).Bytes), $"Final register v{register} differs.");
    for (var mask = 0; mask < 8; mask++) Equal(first.State.Mask(mask), second.State.Mask(mask));
    True(first.State.LinearMemory.Span.SequenceEqual(second.State.LinearMemory.Span), "Final memories differ.");
    Equal(first.State.InstructionPointer, second.State.InstructionPointer);
    Equal(first.State.FuelRemaining, second.State.FuelRemaining);
}

static void ImmutableModules()
{
    var instructions = new[] { new Instruction(Opcode.SplatI64, Immediate: 42), new Instruction(Opcode.Halt) };
    var constant = new VectorConstant(new byte[64]);
    var import = new ImportDefinition("test", "immutable", 1, 1);
    var module = new CascadeModule(instructions, [constant], [new(0, 2)], [import]);
    var program = CascadeProgram.FromModule(module);
    instructions[0] = new(Opcode.SplatI64, Immediate: 99);
    Throws<NotSupportedException>(() => ((IList<Instruction>)module.Instructions)[0] = instructions[0]);
    Throws<NotSupportedException>(() => ((IList<VectorConstant>)module.Constants)[0] = new(new byte[64]));
    Throws<NotSupportedException>(() => ((IList<FunctionDefinition>)module.Functions)[0] = new(1, 2));
    Throws<NotSupportedException>(() => ((IList<ImportDefinition>)program.Imports)[0] = import with { FuelCost = 100 });
    var instance = program.Instantiate(new VmOptions
    {
        MemorySize = 0,
        ImportBindings = new Dictionary<ImportKey, HostImportHandler> { [new("test", "immutable", 1)] = _ => HostCallStatus.Success },
    });
    Success(instance.Execute());
    Equal(42UL, instance.State.Register(0).ReadUInt64(0));
    Throws<ArgumentException>(() => new CascadeModule(instructions, constants: [null!]));
    Throws<ArgumentException>(() => new CascadeModule(instructions, imports: [null!]));
}

static void BatchExecution()
{
    var program = CascadeProgram.FromModule(new CascadeModule([
        new(Opcode.AddI64, Destination: 0, SourceA: 0, SourceB: 1), new(Opcode.Halt)]));
    var inputs = new byte[3 * 128];
    var outputs = new byte[3 * 64];
    for (var item = 0; item < 3; item++)
        for (var lane = 0; lane < 8; lane++)
        {
            BinaryPrimitives.WriteUInt64LittleEndian(inputs.AsSpan(item * 128 + lane * 8), (ulong)(item + lane));
            BinaryPrimitives.WriteUInt64LittleEndian(inputs.AsSpan(item * 128 + 64 + lane * 8), 11);
        }
    foreach (var backend in SimdInterpreter.IsSupported
        ? new[] { ExecutionBackend.Reference, ExecutionBackend.HardwareSimd } : [ExecutionBackend.Reference])
    {
        var batch = program.Instantiate(new VmOptions { MemorySize = 0, Fuel = 2 }, backend);
        var result = batch.ExecuteBatch(inputs, outputs, 2);
        True(result.Succeeded, "Batch failed.");
        Equal(3, result.CompletedCount);
        Equal(6UL, result.FuelConsumed);
        var manual = program.Instantiate(new VmOptions { MemorySize = 0, Fuel = 2 }, backend);
        for (var item = 0; item < 3; item++)
        {
            manual.Restart();
            manual.State.Register(0).ReadFrom(inputs.AsSpan(item * 128, 64));
            manual.State.Register(1).ReadFrom(inputs.AsSpan(item * 128 + 64, 64));
            Success(manual.Execute());
            True(outputs.AsSpan(item * 64, 64).SequenceEqual(manual.State.Register(0).Bytes), "Manual output differs.");
        }
        for (var register = 0; register < 32; register++)
            True(batch.State.Register(register).Bytes.SequenceEqual(manual.State.Register(register).Bytes), "Final registers differ.");
        Equal(manual.State.InstructionPointer, batch.State.InstructionPointer);
        Equal(manual.State.FuelRemaining, batch.State.FuelRemaining);
    }

    var load = CascadeProgram.FromModule(new CascadeModule([new(Opcode.Load, Destination: 0, SourceA: 0), new(Opcode.Halt)]));
    inputs = new byte[3 * 64];
    BinaryPrimitives.WriteUInt64LittleEndian(inputs.AsSpan(64), 1);
    Array.Fill(outputs, (byte)0xAA);
    var stopped = load.Instantiate(new VmOptions { MemorySize = 64 }).ExecuteBatch(inputs, outputs, 1);
    Equal(1, stopped.CompletedCount);
    Equal(TrapCode.InvalidMemoryAccess, stopped.Trap?.Code);
    Equal(3UL, stopped.FuelConsumed);
    True(outputs.AsSpan(0, 64).SequenceEqual(new byte[64]), "Successful output missing.");
    True(outputs.AsSpan(64).ToArray().All(value => value == 0xAA), "Failed or unattempted outputs changed.");

    var exhausted = program.Instantiate(new VmOptions { MemorySize = 0, Fuel = 1 }).ExecuteBatch(new byte[128], new byte[64], 2);
    Equal(0, exhausted.CompletedCount);
    Equal(TrapCode.FuelExhausted, exhausted.Trap?.Code);
    Equal(1UL, exhausted.FuelConsumed);
}

static void BatchValidation()
{
    var program = CascadeProgram.FromModule(new CascadeModule([new(Opcode.Halt)]));
    var instance = program.Instantiate(new VmOptions { MemorySize = 0 });
    instance.State.Register(0).WriteUInt64(0, 42);
    Throws<ArgumentException>(() => instance.ExecuteBatch(new byte[64], new byte[63], 1));
    Throws<ArgumentException>(() => instance.ExecuteBatch(new byte[63], new byte[64], 1));
    Throws<ArgumentOutOfRangeException>(() => instance.ExecuteBatch([], [], 33));
    Throws<ArgumentOutOfRangeException>(() => instance.ExecuteBatch([], [], -1));
    var overlap = new byte[64];
    Throws<ArgumentException>(() => instance.ExecuteBatch(overlap, overlap, 1));
    Throws<ArgumentException>(() => instance.ExecuteBatch(instance.State.Register(0).Bytes, new byte[64], 1));
    var huge = program.Instantiate(new VmOptions { MemorySize = 0, Fuel = ulong.MaxValue });
    Throws<ArgumentException>(() => huge.ExecuteBatch([], new byte[128], 0));
    Equal(42UL, instance.State.Register(0).ReadUInt64(0));
    Equal(instance.State.InitialFuel, instance.State.FuelRemaining);
    var empty = instance.ExecuteBatch([], [], 0);
    Equal(0, empty.CompletedCount);
    Equal(0UL, empty.FuelConsumed);
    var outputs = new byte[128];
    var constants = instance.ExecuteBatch([], outputs, 0);
    True(constants.Succeeded, "Zero-argument batch failed.");
    Equal(42UL, BinaryPrimitives.ReadUInt64LittleEndian(outputs.AsSpan(64)));
}

static void BatchStateAndImports()
{
    var import = new ImportDefinition("test", "batch", 1, 4);
    var calls = 0;
    HostImportHandler handler = _ => { calls++; return HostCallStatus.Success; };
    var options = new VmOptions
    {
        MemorySize = 64,
        Fuel = 11,
        ImportBindings = new Dictionary<ImportKey, HostImportHandler> { [new("test", "batch", 1)] = handler },
    };
    var program = CascadeProgram.FromModule(new CascadeModule([
        new(Opcode.Zero, Destination: 1),
        new(Opcode.Load, Destination: 0, SourceA: 1),
        new(Opcode.SplatI64, Destination: 2, Immediate: 1),
        new(Opcode.AddI64, Destination: 0, SourceA: 0, SourceB: 2),
        new(Opcode.Store, SourceA: 1, SourceB: 0),
        new(Opcode.HostCall, Immediate: 0),
        new(Opcode.Halt),
    ], imports: [import]));
    var outputs = new byte[3 * 64];
    var instance = program.Instantiate(options);
    var result = instance.ExecuteBatch([], outputs, 0);
    True(result.Succeeded, "Stateful batch failed.");
    Equal(3, calls);
    Equal(33UL, result.FuelConsumed);
    for (var item = 0; item < 3; item++)
        Equal((ulong)(item + 1), BinaryPrimitives.ReadUInt64LittleEndian(outputs.AsSpan(item * 64)));
    Equal(3UL, BinaryPrimitives.ReadUInt64LittleEndian(instance.State.LinearMemory.Span));
    Throws<ArgumentException>(() => instance.ExecuteBatch(instance.State.LinearMemory.Span, new byte[64], 1));
}

static int WriteGoldenFixtures(string directory)
{
    Directory.CreateDirectory(directory);
    WriteFixture(Path.Combine(directory, "minimal.cvm"), new([new(Opcode.Halt)]));
    var constant = Enumerable.Range(0, VectorRegister.ByteCount).Select(index => (byte)index).ToArray();
    WriteFixture(Path.Combine(directory, "complete.cvm"), new(
        [new(Opcode.LoadConstant, Destination: 0, Immediate: 0), new(Opcode.HostCall, Immediate: 0), new(Opcode.Halt)],
        [new VectorConstant(constant)],
        [new FunctionDefinition(0, 3)],
        [new ImportDefinition("fixture", "noop", 1, 7)]));
    Console.WriteLine($"Wrote golden fixtures to {directory}");
    return 0;
}

static void WriteFixture(string path, CascadeModule module)
{
    using var output = File.Create(path);
    CvmModuleFormat.Write(output, module);
}

static ReferenceInterpreter Vm(params Instruction[] instructions) => VmWithOptions(new(), instructions);
static ReferenceInterpreter VmWithOptions(VmOptions options, params Instruction[] instructions) => new(new CascadeModule(instructions), options);
static void Success(ExecutionResult result) => True(result.Succeeded, result.Trap?.Message ?? "VM did not halt successfully.");
static void True(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
static void Equal<T>(T expected, T actual) { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new InvalidOperationException($"Expected {expected}, got {actual}."); }
static void SequenceEqual<T>(T[] expected, T[] actual) { if (!expected.SequenceEqual(actual)) throw new InvalidOperationException("Sequences differ."); }
static void Throws<T>(Action action) where T : Exception
{
    try { action(); }
    catch (T) { return; }
    throw new InvalidOperationException($"Expected {typeof(T).Name}.");
}

static void HasValidationError(CascadeModule module, string text)
{
    var errors = ModuleValidator.Validate(module);
    True(errors.Any(error => error.Message.Contains(text, StringComparison.Ordinal)), $"Expected validation error containing '{text}'.");
}
