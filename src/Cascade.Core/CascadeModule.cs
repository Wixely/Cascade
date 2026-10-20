namespace Cascade.Core;

public sealed class CascadeModule
{
    public CascadeModule(
        IEnumerable<Instruction> instructions,
        IEnumerable<VectorConstant>? constants = null,
        IEnumerable<FunctionDefinition>? functions = null,
        IEnumerable<ImportDefinition>? imports = null,
        int entryPoint = 0)
    {
        ArgumentNullException.ThrowIfNull(instructions);
        Instructions = instructions.ToArray();
        Constants = constants?.ToArray() ?? [];
        Functions = functions?.ToArray() ?? [];
        Imports = imports?.ToArray() ?? [];
        EntryPoint = entryPoint;
    }

    public IReadOnlyList<Instruction> Instructions { get; }
    public IReadOnlyList<VectorConstant> Constants { get; }
    public IReadOnlyList<FunctionDefinition> Functions { get; }
    public IReadOnlyList<ImportDefinition> Imports { get; }
    public int EntryPoint { get; }
}

public sealed class VectorConstant
{
    private readonly byte[] _bytes;

    public VectorConstant(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length != VectorRegister.ByteCount)
            throw new ArgumentException("A vector constant is exactly 64 bytes.", nameof(bytes));
        _bytes = bytes.ToArray();
    }

    public ReadOnlySpan<byte> Bytes => _bytes;
}

public readonly record struct FunctionDefinition(uint Start, uint EndExclusive);

public sealed record ImportDefinition(string Namespace, string Name, ushort AbiVersion, ulong FuelCost);
