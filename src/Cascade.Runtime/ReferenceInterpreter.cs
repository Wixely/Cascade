using Cascade.Core;

namespace Cascade.Runtime;

public sealed class ReferenceInterpreter
{
    private readonly CascadeModule _module;
    private readonly VmOptions _options;
    private readonly Stack<int> _returnAddresses = new();
    private readonly HostImportHandler?[] _importBindings;
    private readonly VmTrap? _bindingTrap;

    public ReferenceInterpreter(CascadeModule module, VmOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(module);
        var errors = ModuleValidator.Validate(module);
        if (errors.Count != 0)
            throw new ArgumentException($"Module validation failed: {string.Join("; ", errors.Select(e => $"[{e.InstructionIndex}] {e.Message}"))}", nameof(module));

        _options = options ?? new VmOptions();
        _options.Validate();
        _module = module;
        State = new VmState(_options.MemorySize, _options.Fuel);
        State.InstructionPointer = module.EntryPoint;
        _importBindings = new HostImportHandler?[module.Imports.Count];
        for (var index = 0; index < module.Imports.Count; index++)
        {
            var import = module.Imports[index];
            var key = new ImportKey(import.Namespace, import.Name, import.AbiVersion);
            if (_options.ImportBindings.TryGetValue(key, out var binding)) _importBindings[index] = binding;
            else _bindingTrap ??= new(TrapCode.InvalidHostImport, module.EntryPoint, $"No binding was supplied for import {import.Namespace}.{import.Name} ABI {import.AbiVersion}.");
        }
    }

    public VmState State { get; }

    public ExecutionResult Execute()
    {
        if (_bindingTrap is not null) return new(false, 0, _bindingTrap);
        while (true)
        {
            var ip = State.InstructionPointer;
            if ((uint)ip >= (uint)_module.Instructions.Count)
                return Trapped(TrapCode.InvalidBranchTarget, "Instruction pointer left the code range.");
            if (State.FuelRemaining == 0)
                return Trapped(TrapCode.FuelExhausted, "Execution fuel was exhausted.");

            State.FuelRemaining--;
            var instruction = _module.Instructions[ip];
            var next = ip + 1;
            VmTrap? trap = instruction.Opcode switch
            {
                Opcode.Zero => Zero(instruction),
                Opcode.Move => Move(instruction),
                Opcode.SplatI32 => SplatI32(instruction),
                Opcode.SplatI64 => SplatI64(instruction),
                Opcode.LoadConstant => LoadConstant(instruction),
                Opcode.AddI32 => BinaryI32(instruction, static (a, b) => unchecked(a + b)),
                Opcode.SubI32 => BinaryI32(instruction, static (a, b) => unchecked(a - b)),
                Opcode.MulI32 => BinaryI32(instruction, static (a, b) => unchecked(a * b)),
                Opcode.AddI64 => BinaryI64(instruction, static (a, b) => unchecked(a + b)),
                Opcode.SubI64 => BinaryI64(instruction, static (a, b) => unchecked(a - b)),
                Opcode.And => Bitwise(instruction, static (a, b) => (byte)(a & b)),
                Opcode.Or => Bitwise(instruction, static (a, b) => (byte)(a | b)),
                Opcode.Xor => Bitwise(instruction, static (a, b) => (byte)(a ^ b)),
                Opcode.Not => Not(instruction),
                Opcode.CompareEqualI32 => CompareI32(instruction, static (a, b) => a == b),
                Opcode.CompareLessThanI32 => CompareI32(instruction, static (a, b) => a < b),
                Opcode.CompareEqualI64 => CompareI64(instruction, static (a, b) => a == b),
                Opcode.CompareLessThanI64 => CompareI64(instruction, static (a, b) => a < b),
                Opcode.ConvertMask8 => ConvertMask(instruction, 8),
                Opcode.ConvertMask16 => ConvertMask(instruction, 16),
                Opcode.ConvertMask32 => ConvertMask(instruction, 32),
                Opcode.ConvertMask64 => ConvertMask(instruction, 64),
                Opcode.SelectI32 => Select(instruction, 16, 4),
                Opcode.SelectI64 => Select(instruction, 8, 8),
                Opcode.Load => Load(instruction),
                Opcode.Store => Store(instruction),
                Opcode.GatherI32 => Gather(instruction, 16, 4, true),
                Opcode.ScatterI32 => Scatter(instruction, 16, 4, true),
                Opcode.GatherI64 => Gather(instruction, 8, 8, false),
                Opcode.ScatterI64 => Scatter(instruction, 8, 8, false),
                Opcode.Jump => Branch(instruction, true, ref next),
                Opcode.BranchAny => Branch(instruction, State.Mask(instruction.Mask).Any, ref next),
                Opcode.BranchAll => Branch(instruction, State.Mask(instruction.Mask).All, ref next),
                Opcode.Call => Call(instruction, ref next),
                Opcode.Return => Return(ref next),
                Opcode.HostCall => HostCall(instruction),
                Opcode.Halt => null,
                _ => new(TrapCode.InvalidInstruction, ip, "Unknown opcode reached execution."),
            };

            if (trap is not null) return new(false, State.InitialFuel - State.FuelRemaining, trap);
            if (instruction.Opcode == Opcode.Halt) return new(true, State.InitialFuel - State.FuelRemaining, null);
            State.InstructionPointer = next;
        }
    }

    private ExecutionResult Trapped(TrapCode code, string message) =>
        new(false, State.InitialFuel - State.FuelRemaining, new(code, State.InstructionPointer, message));

    private VmTrap? Zero(Instruction i) { State.Register(i.Destination).Clear(); return null; }
    private VmTrap? Move(Instruction i) { State.Register(i.Destination).CopyFrom(State.Register(i.SourceA)); return null; }

    private VmTrap? SplatI32(Instruction i)
    {
        var destination = State.Register(i.Destination);
        for (var lane = 0; lane < 16; lane++) destination.WriteUInt32(lane, unchecked((uint)i.Immediate));
        return null;
    }

    private VmTrap? SplatI64(Instruction i)
    {
        var destination = State.Register(i.Destination);
        for (var lane = 0; lane < 8; lane++) destination.WriteUInt64(lane, i.Immediate);
        return null;
    }

    private VmTrap? LoadConstant(Instruction i)
    {
        State.Register(i.Destination).ReadFrom(_module.Constants[checked((int)i.Immediate)].Bytes);
        return null;
    }

    private VmTrap? BinaryI32(Instruction i, Func<uint, uint, uint> operation)
    {
        var destination = State.Register(i.Destination);
        var a = State.Register(i.SourceA);
        var b = State.Register(i.SourceB);
        Span<uint> values = stackalloc uint[16];
        for (var lane = 0; lane < values.Length; lane++) values[lane] = operation(a.ReadUInt32(lane), b.ReadUInt32(lane));
        for (var lane = 0; lane < values.Length; lane++) destination.WriteUInt32(lane, values[lane]);
        return null;
    }

    private VmTrap? BinaryI64(Instruction i, Func<ulong, ulong, ulong> operation)
    {
        var destination = State.Register(i.Destination);
        var a = State.Register(i.SourceA);
        var b = State.Register(i.SourceB);
        Span<ulong> values = stackalloc ulong[8];
        for (var lane = 0; lane < values.Length; lane++) values[lane] = operation(a.ReadUInt64(lane), b.ReadUInt64(lane));
        for (var lane = 0; lane < values.Length; lane++) destination.WriteUInt64(lane, values[lane]);
        return null;
    }

    private VmTrap? Bitwise(Instruction i, Func<byte, byte, byte> operation)
    {
        var destination = State.Register(i.Destination);
        var a = State.Register(i.SourceA);
        var b = State.Register(i.SourceB);
        Span<byte> values = stackalloc byte[VectorRegister.ByteCount];
        for (var index = 0; index < values.Length; index++) values[index] = operation(a.ReadByte(index), b.ReadByte(index));
        destination.ReadFrom(values);
        return null;
    }

    private VmTrap? Not(Instruction i)
    {
        var destination = State.Register(i.Destination);
        var source = State.Register(i.SourceA);
        Span<byte> values = stackalloc byte[VectorRegister.ByteCount];
        for (var index = 0; index < values.Length; index++) values[index] = (byte)~source.ReadByte(index);
        destination.ReadFrom(values);
        return null;
    }

    private VmTrap? CompareI32(Instruction i, Func<int, int, bool> comparison)
    {
        ulong bits = 0;
        for (var lane = 0; lane < 16; lane++)
            if (comparison(State.Register(i.SourceA).ReadInt32(lane), State.Register(i.SourceB).ReadInt32(lane))) bits |= 1UL << lane;
        State.SetMask(i.Mask, new(bits, 16));
        return null;
    }

    private VmTrap? CompareI64(Instruction i, Func<long, long, bool> comparison)
    {
        ulong bits = 0;
        for (var lane = 0; lane < 8; lane++)
            if (comparison(State.Register(i.SourceA).ReadInt64(lane), State.Register(i.SourceB).ReadInt64(lane))) bits |= 1UL << lane;
        State.SetMask(i.Mask, new(bits, 8));
        return null;
    }

    private VmTrap? ConvertMask(Instruction i, byte width)
    {
        var source = State.Mask(i.Mask);
        var widthBits = width == 64 ? ulong.MaxValue : (1UL << width) - 1;
        State.SetMask(i.Destination, new(source.ActiveBits & widthBits, width));
        return null;
    }

    private VmTrap? Select(Instruction i, int lanes, int laneBytes)
    {
        var mask = State.Mask(i.Mask);
        if (mask.Width != lanes) return InvalidMaskWidth(lanes, mask.Width);
        var destination = State.Register(i.Destination);
        var a = State.Register(i.SourceA);
        var b = State.Register(i.SourceB);
        Span<byte> values = stackalloc byte[VectorRegister.ByteCount];
        for (var lane = 0; lane < lanes; lane++)
        {
            var source = (mask.ActiveBits & (1UL << lane)) != 0 ? a : b;
            for (var offset = 0; offset < laneBytes; offset++) values[(lane * laneBytes) + offset] = source.ReadByte((lane * laneBytes) + offset);
        }
        destination.ReadFrom(values);
        return null;
    }

    private VmTrap? Load(Instruction i)
    {
        var address = State.Register(i.SourceA).ReadUInt64(0);
        if (!TryMemoryRange(address, VectorRegister.ByteCount, out var offset)) return InvalidMemory(address, VectorRegister.ByteCount);
        State.Register(i.Destination).ReadFrom(State.Memory.AsSpan(offset, VectorRegister.ByteCount));
        return null;
    }

    private VmTrap? Store(Instruction i)
    {
        var address = State.Register(i.SourceA).ReadUInt64(0);
        if (!TryMemoryRange(address, VectorRegister.ByteCount, out var offset)) return InvalidMemory(address, VectorRegister.ByteCount);
        State.Register(i.SourceB).WriteTo(State.Memory.AsSpan(offset, VectorRegister.ByteCount));
        return null;
    }

    private VmTrap? Gather(Instruction i, int lanes, int laneBytes, bool pairedAddresses)
    {
        var mask = State.Mask(i.Mask);
        if (mask.Width != lanes) return InvalidMaskWidth(lanes, mask.Width);
        Span<int> offsets = stackalloc int[lanes];
        offsets.Fill(-1);
        for (var lane = 0; lane < lanes; lane++)
        {
            if ((mask.ActiveBits & (1UL << lane)) == 0) continue;
            var address = Address(i.SourceA, lane, pairedAddresses);
            if (!TryMemoryRange(address, laneBytes, out offsets[lane])) return InvalidMemory(address, laneBytes);
        }
        var destination = State.Register(i.Destination);
        Span<byte> values = stackalloc byte[VectorRegister.ByteCount];
        destination.WriteTo(values);
        for (var lane = 0; lane < lanes; lane++)
        {
            if (offsets[lane] < 0) continue;
            State.Memory.AsSpan(offsets[lane], laneBytes).CopyTo(values.Slice(lane * laneBytes, laneBytes));
        }
        destination.ReadFrom(values);
        return null;
    }

    private VmTrap? Scatter(Instruction i, int lanes, int laneBytes, bool pairedAddresses)
    {
        var mask = State.Mask(i.Mask);
        if (mask.Width != lanes) return InvalidMaskWidth(lanes, mask.Width);
        Span<int> offsets = stackalloc int[lanes];
        offsets.Fill(-1);
        for (var lane = 0; lane < lanes; lane++)
        {
            if ((mask.ActiveBits & (1UL << lane)) == 0) continue;
            var address = Address(i.SourceA, lane, pairedAddresses);
            if (!TryMemoryRange(address, laneBytes, out offsets[lane])) return InvalidMemory(address, laneBytes);
        }
        var source = State.Register(i.SourceB);
        for (var lane = 0; lane < lanes; lane++)
        {
            if (offsets[lane] < 0) continue;
            SourceLane(source, lane, laneBytes).CopyTo(State.Memory.AsSpan(offsets[lane], laneBytes));
        }
        return null;
    }

    private static ReadOnlySpan<byte> SourceLane(VectorRegister source, int lane, int laneBytes) =>
        source.Bytes.Slice(lane * laneBytes, laneBytes);

    private ulong Address(byte firstRegister, int lane, bool paired)
    {
        var register = paired ? firstRegister + (lane / 8) : firstRegister;
        return State.Register(register).ReadUInt64(lane % 8);
    }

    private VmTrap? Branch(Instruction i, bool take, ref int next)
    {
        if (take) next = checked((int)i.Immediate);
        return null;
    }

    private VmTrap? Call(Instruction i, ref int next)
    {
        if (_returnAddresses.Count >= _options.MaximumCallDepth)
            return new(TrapCode.StackOverflow, State.InstructionPointer, "Maximum call depth was exceeded.");
        _returnAddresses.Push(next);
        next = checked((int)i.Immediate);
        return null;
    }

    private VmTrap? Return(ref int next)
    {
        if (!_returnAddresses.TryPop(out next))
            return new(TrapCode.StackUnderflow, State.InstructionPointer, "Return executed with an empty call stack.");
        return null;
    }

    private VmTrap? HostCall(Instruction i)
    {
        var importIndex = checked((int)i.Immediate);
        var import = _module.Imports[importIndex];
        if (State.FuelRemaining < import.FuelCost)
            return new(TrapCode.FuelExhausted, State.InstructionPointer, "Execution fuel was exhausted before a host call.");
        State.FuelRemaining -= import.FuelCost;
        try
        {
            var status = _importBindings[importIndex]!(new HostCallContext(State));
            return status == HostCallStatus.Success
                ? null
                : new(TrapCode.HostCallFailed, State.InstructionPointer, "A host call reported failure.");
        }
        catch (Exception)
        {
            return new(TrapCode.HostCallFailed, State.InstructionPointer, "A host call failed.");
        }
    }

    private bool TryMemoryRange(ulong address, int length, out int offset)
    {
        if (address > int.MaxValue || address > (ulong)State.Memory.Length || (ulong)length > (ulong)State.Memory.Length - address)
        {
            offset = 0;
            return false;
        }
        offset = (int)address;
        return true;
    }

    private VmTrap InvalidMemory(ulong address, int length) =>
        new(TrapCode.InvalidMemoryAccess, State.InstructionPointer, $"Memory access at {address} with length {length} is out of bounds.");

    private VmTrap InvalidMaskWidth(int expected, int actual) =>
        new(TrapCode.InvalidInstruction, State.InstructionPointer, $"Expected a {expected}-bit predicate mask but found {actual} bits.");
}
