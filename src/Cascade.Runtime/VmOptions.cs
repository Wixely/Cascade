namespace Cascade.Runtime;

public sealed record VmOptions
{
    public const int DefaultMemorySize = 64 * 1024;
    public const int MaximumMemorySize = 16 * 1024 * 1024;

    public int MemorySize { get; init; } = DefaultMemorySize;
    public ulong Fuel { get; init; } = 1_000_000;
    public int MaximumCallDepth { get; init; } = 1_024;
    public int MaximumSpillBytes { get; init; } = MaximumMemorySize;
    /// <summary>Experimental compare/select dispatch shortcut; preserves instruction-level state and fuel.</summary>
    public bool EnableInstructionFusion { get; init; }
    public IReadOnlyDictionary<ImportKey, HostImportHandler> ImportBindings { get; init; } =
        new Dictionary<ImportKey, HostImportHandler>();

    internal void Validate()
    {
        if (MemorySize < 0 || MemorySize > MaximumMemorySize)
            throw new ArgumentOutOfRangeException(nameof(MemorySize), $"Memory must be between 0 and {MaximumMemorySize} bytes.");
        if (MaximumCallDepth < 0)
            throw new ArgumentOutOfRangeException(nameof(MaximumCallDepth));
        if (MaximumSpillBytes is < 0 or > MaximumMemorySize)
            throw new ArgumentOutOfRangeException(nameof(MaximumSpillBytes));
        ArgumentNullException.ThrowIfNull(ImportBindings);
    }
}
