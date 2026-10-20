using Cascade.Core;

namespace Cascade.Runtime;

public readonly record struct ImportKey(string Namespace, string Name, ushort AbiVersion);

public enum HostCallStatus
{
    Success,
    Failed,
}

public delegate HostCallStatus HostImportHandler(HostCallContext context);

/// <summary>Restricted VM access supplied only to an explicitly bound import.</summary>
public sealed class HostCallContext
{
    private readonly VmState _state;

    internal HostCallContext(VmState state) => _state = state;

    public Memory<byte> LinearMemory => _state.Memory;

    public VectorRegister Register(int index)
    {
        if (index is < 0 or > 31 || index is >= 8 and <= 15)
            throw new ArgumentOutOfRangeException(nameof(index), "Imports may access v0-v7 and v16-v31 only.");
        return _state.Register(index);
    }
}
