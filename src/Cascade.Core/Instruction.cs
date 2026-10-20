namespace Cascade.Core;

/// <summary>A decoded instruction. Registers are validated before execution.</summary>
public readonly record struct Instruction(
    Opcode Opcode,
    byte Destination = 0,
    byte SourceA = 0,
    byte SourceB = 0,
    byte Mask = 0,
    ushort Flags = 0,
    ulong Immediate = 0);
