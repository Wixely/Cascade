using System.Numerics;
using Cascade.Core;

namespace Cascade.Runtime;

/// <summary>Selects the execution engine used for a Cascade instance.</summary>
public enum ExecutionBackend
{
    Reference,
    HardwareSimd,
    Automatic,
}

internal interface IVmInterpreter
{
    VmState State { get; }
    ExecutionResult Execute();
}

/// <summary>Executes Cascade with hardware-accelerated System.Numerics vectors.</summary>
public sealed class SimdInterpreter : IVmInterpreter
{
    private readonly ReferenceInterpreter _interpreter;

    public SimdInterpreter(CascadeModule module, VmOptions? options = null)
    {
        if (!IsSupported)
            throw new PlatformNotSupportedException("The hardware SIMD backend requires little-endian hardware acceleration through System.Numerics.Vector.");
        _interpreter = new(module, options, HardwareSimdVectorOperations.Instance);
    }

    public static bool IsSupported => BitConverter.IsLittleEndian && Vector.IsHardwareAccelerated;
    public static int VectorWidthBits => IsSupported ? Vector<byte>.Count * 8 : 0;
    public VmState State => _interpreter.State;
    public ExecutionResult Execute() => _interpreter.Execute();
}
