namespace Cascade.Core;

public sealed class CascadeModule
{
    public const int MaximumSpillSlots = 65_536;
    // Preserve the original binary constructor signature for existing embedders.
    public CascadeModule(IEnumerable<Instruction> instructions, IEnumerable<VectorConstant>? constants,
        IEnumerable<FunctionDefinition>? functions, IEnumerable<ImportDefinition>? imports, int entryPoint)
        : this(instructions, constants, functions, imports, entryPoint, 0, 0) { }

    public CascadeModule(
        IEnumerable<Instruction> instructions,
        IEnumerable<VectorConstant>? constants = null,
        IEnumerable<FunctionDefinition>? functions = null,
        IEnumerable<ImportDefinition>? imports = null,
        int entryPoint = 0,
        int vectorSpillSlots = 0,
        int maskSpillSlots = 0)
    {
        ArgumentNullException.ThrowIfNull(instructions);
        Instructions = Array.AsReadOnly(instructions.ToArray());
        var constantArray = constants?.ToArray() ?? [];
        var importArray = imports?.ToArray() ?? [];
        if (constantArray.Any(constant => constant is null))
            throw new ArgumentException("Constants cannot contain null entries.", nameof(constants));
        if (importArray.Any(import => import is null))
            throw new ArgumentException("Imports cannot contain null entries.", nameof(imports));
        Constants = Array.AsReadOnly(constantArray);
        Functions = Array.AsReadOnly(functions?.ToArray() ?? []);
        Imports = Array.AsReadOnly(importArray);
        EntryPoint = entryPoint;
        if (vectorSpillSlots is < 0 or > MaximumSpillSlots) throw new ArgumentOutOfRangeException(nameof(vectorSpillSlots));
        if (maskSpillSlots is < 0 or > MaximumSpillSlots) throw new ArgumentOutOfRangeException(nameof(maskSpillSlots));
        VectorSpillSlots = vectorSpillSlots;
        MaskSpillSlots = maskSpillSlots;
    }

    public IReadOnlyList<Instruction> Instructions { get; }
    public IReadOnlyList<VectorConstant> Constants { get; }
    public IReadOnlyList<FunctionDefinition> Functions { get; }
    public IReadOnlyList<ImportDefinition> Imports { get; }
    public int EntryPoint { get; }
    public int VectorSpillSlots { get; }
    public int MaskSpillSlots { get; }
    public int SpillStorageBytes => VectorSpillSlots * 64 + MaskSpillSlots * 16;
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
