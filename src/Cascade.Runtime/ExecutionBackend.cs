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
    ExecutionOutcome ExecuteCore();
    void Restart();
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

    internal SimdInterpreter(CascadeModule module, VmOptions? options, bool moduleIsValidated)
    {
        if (!IsSupported)
            throw new PlatformNotSupportedException("The hardware SIMD backend requires little-endian hardware acceleration through System.Numerics.Vector.");
        _interpreter = new(module, options, HardwareSimdVectorOperations.Instance, moduleIsValidated);
    }

    public static bool IsSupported => BitConverter.IsLittleEndian && Vector.IsHardwareAccelerated;
    public static int VectorWidthBits => IsSupported ? Vector<byte>.Count * 8 : 0;
    public VmState State => _interpreter.State;
    public ExecutionResult Execute() => _interpreter.Execute();
    ExecutionOutcome IVmInterpreter.ExecuteCore() => _interpreter.ExecuteCore();
    public void Restart() => _interpreter.Restart();
}

// Keeps the batch hot path allocation-free for successful invocations while
// preserving the public reference-type ExecutionResult API.
internal readonly record struct ExecutionOutcome(bool Halted, ulong FuelConsumed, VmTrap? Trap)
{
    public bool Succeeded => Halted && Trap is null;
    public ExecutionResult ToResult() => new(Halted, FuelConsumed, Trap);
}
