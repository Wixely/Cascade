using Cascade.Core;

namespace Cascade.Compiler;

internal static class SpillAllocator
{
    // The low bank retains its original mapping. Scratch registers are never
    // homes for virtual values; every scratch write is consumed in one instruction.
    private const int VectorHomes = 24;
    private const int MaskHomes = 6;

    public static (CascadeModule Module, int Stores, int Reloads) Lower(
        IReadOnlyList<VirtualInstruction> code, int vectors, int masks, int parameters, Token token)
    {
        var vectorSlots = vectors > 32 ? vectors - VectorHomes : 0;
        var maskSlots = masks > 8 ? masks - MaskHomes : 0;
        if (vectorSlots == 0 && maskSlots == 0) return (new(code.Select(instruction => instruction.Encode())), 0, 0);
        var output = new List<Instruction>();
        var stores = 0;
        var reloads = 0;
        // Preserve any input arguments whose architectural homes become scratch.
        if (vectorSlots != 0)
            for (var argument = VectorHomes; argument < parameters; argument++)
                Emit(new(Opcode.SpillStoreVector, SourceA: (byte)argument, Immediate: (ulong)(argument - VectorHomes)));

        foreach (var instruction in code)
        {
            var access = AccessFor(instruction.Opcode);
            var mapped = instruction;
            if ((access & Access.A) != 0)
            {
                if ((access & Access.Pair) != 0 && IsVectorSpilled(instruction.SourceA))
                {
                    LoadVector(instruction.SourceA, 24);
                    LoadVector(instruction.SourceA + 1, 25);
                    mapped = mapped with { SourceA = 24 };
                }
                else mapped = mapped with { SourceA = ReadVector(instruction.SourceA, 26) };
            }
            if ((access & Access.B) != 0) mapped = mapped with { SourceB = ReadVector(instruction.SourceB, 27) };
            if ((access & Access.MaskRead) != 0 && IsMaskSpilled(instruction.Mask))
            {
                Emit(new(Opcode.SpillLoadMask, Destination: 6, Immediate: (ulong)(instruction.Mask - MaskHomes)));
                mapped = mapped with { Mask = 6 };
            }
            if ((access & Access.VectorWrite) != 0 && IsVectorSpilled(instruction.Destination))
            {
                if ((access & Access.DestinationRead) != 0) LoadVector(instruction.Destination, 28);
                mapped = mapped with { Destination = 28 };
            }
            if ((access & Access.MaskWrite) != 0 && IsMaskSpilled(instruction.Mask)) mapped = mapped with { Mask = 7 };
            if ((access & Access.MaskDestinationWrite) != 0 && IsMaskSpilled(instruction.Destination)) mapped = mapped with { Destination = 7 };
            Emit(mapped.Encode());
            if ((access & Access.VectorWrite) != 0 && IsVectorSpilled(instruction.Destination))
                Emit(new(Opcode.SpillStoreVector, SourceA: 28, Immediate: (ulong)(instruction.Destination - VectorHomes)));
            if ((access & Access.MaskWrite) != 0 && IsMaskSpilled(instruction.Mask))
                Emit(new(Opcode.SpillStoreMask, Mask: 7, Immediate: (ulong)(instruction.Mask - MaskHomes)));
            if ((access & Access.MaskDestinationWrite) != 0 && IsMaskSpilled(instruction.Destination))
                Emit(new(Opcode.SpillStoreMask, Mask: 7, Immediate: (ulong)(instruction.Destination - MaskHomes)));
        }
        return (new(output, vectorSpillSlots: vectorSlots, maskSpillSlots: maskSlots), stores, reloads);

        bool IsVectorSpilled(int index) => vectorSlots != 0 && index >= VectorHomes;
        bool IsMaskSpilled(int index) => maskSlots != 0 && index >= MaskHomes;
        int ReadVector(int index, byte scratch)
        {
            if (!IsVectorSpilled(index)) return index;
            LoadVector(index, scratch);
            return scratch;
        }
        void LoadVector(int index, byte scratch) => Emit(new(Opcode.SpillLoadVector, Destination: scratch, Immediate: (ulong)(index - VectorHomes)));
        void Emit(Instruction instruction)
        {
            if (output.Count >= 1_000_000)
                throw new CompilationFailure(new("CAS202", "Spill-expanded kernels may contain at most 1,000,000 instructions.", token.Line, token.Column));
            output.Add(instruction);
            if (instruction.Opcode is Opcode.SpillStoreVector or Opcode.SpillStoreMask) stores++;
            if (instruction.Opcode is Opcode.SpillLoadVector or Opcode.SpillLoadMask) reloads++;
        }
    }

    [Flags]
    private enum Access { None = 0, A = 1, B = 2, VectorWrite = 4, MaskRead = 8, MaskWrite = 16, MaskDestinationWrite = 32, DestinationRead = 64, Pair = 128 }

    private static Access AccessFor(Opcode opcode) => opcode switch
    {
        Opcode.Halt => Access.None,
        Opcode.Zero or Opcode.SplatI32 or Opcode.SplatI64 or Opcode.LoadConstant => Access.VectorWrite,
        Opcode.Move or Opcode.Not or Opcode.Load => Access.A | Access.VectorWrite,
        Opcode.AddI32 or Opcode.SubI32 or Opcode.MulI32 or Opcode.AddI64 or Opcode.SubI64 or Opcode.And or Opcode.Or or Opcode.Xor
            => Access.A | Access.B | Access.VectorWrite,
        Opcode.CompareEqualI32 or Opcode.CompareLessThanI32 or Opcode.CompareEqualI64 or Opcode.CompareLessThanI64
            => Access.A | Access.B | Access.MaskWrite,
        Opcode.ConvertMask8 or Opcode.ConvertMask16 or Opcode.ConvertMask32 or Opcode.ConvertMask64 => Access.MaskRead | Access.MaskDestinationWrite,
        Opcode.SelectI32 or Opcode.SelectI64 => Access.A | Access.B | Access.MaskRead | Access.VectorWrite,
        Opcode.Store => Access.A | Access.B,
        Opcode.GatherI32 => Access.A | Access.Pair | Access.MaskRead | Access.VectorWrite | Access.DestinationRead,
        Opcode.GatherI64 => Access.A | Access.MaskRead | Access.VectorWrite | Access.DestinationRead,
        Opcode.ScatterI32 => Access.A | Access.Pair | Access.B | Access.MaskRead,
        Opcode.ScatterI64 => Access.A | Access.B | Access.MaskRead,
        _ => throw new InvalidOperationException($"Unsupported virtual instruction {opcode}."),
    };
}
