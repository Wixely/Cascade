using Cascade.Core;

namespace Cascade.Runtime;

public readonly record struct PredicateMask(ulong Value, byte Width)
{
    public ulong ActiveBits => Width == 64 ? Value : Value & ((1UL << Width) - 1);
    public bool Any => ActiveBits != 0;
    public bool All => ActiveBits == (Width == 64 ? ulong.MaxValue : (1UL << Width) - 1);
}

public sealed class VmState
{
    private readonly VectorRegister[] _registers = Enumerable.Range(0, 32).Select(_ => new VectorRegister()).ToArray();
    private readonly PredicateMask[] _masks = Enumerable.Repeat(new PredicateMask(0, 64), 8).ToArray();

    internal VmState(int memorySize, ulong fuel)
    {
        Memory = new byte[memorySize];
        FuelRemaining = fuel;
        InitialFuel = fuel;
    }

    public int InstructionPointer { get; internal set; }
    public ulong FuelRemaining { get; internal set; }
    public ulong InitialFuel { get; }
    public ReadOnlyMemory<byte> LinearMemory => Memory;
    internal byte[] Memory { get; }

    public VectorRegister Register(int index) => _registers[index];
    public PredicateMask Mask(int index) => _masks[index];
    internal void SetMask(int index, PredicateMask value) => _masks[index] = value;

    internal void Restart(int entryPoint)
    {
        InstructionPointer = entryPoint;
        FuelRemaining = InitialFuel;
        Array.Fill(_masks, new PredicateMask(0, 64));
    }
}
