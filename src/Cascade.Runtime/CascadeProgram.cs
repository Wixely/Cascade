using Cascade.Core;

namespace Cascade.Runtime;

/// <summary>A validated, immutable Cascade program suitable for repeated instantiation.</summary>
public sealed class CascadeProgram
{
    private readonly CascadeModule _module;

    private CascadeProgram(CascadeModule module) => _module = module;

    public int InstructionCount => _module.Instructions.Count;
    public int ConstantCount => _module.Constants.Count;
    public int FunctionCount => _module.Functions.Count;
    public IReadOnlyList<ImportDefinition> Imports => _module.Imports;

    public static CascadeProgram Load(Stream stream) => FromModule(CvmModuleFormat.Read(stream));

    public static CascadeProgram FromModule(CascadeModule module)
    {
        ArgumentNullException.ThrowIfNull(module);
        var errors = ModuleValidator.Validate(module);
        if (errors.Count != 0)
            throw new ArgumentException($"Module validation failed: {string.Join("; ", errors.Select(error => $"[{error.InstructionIndex}] {error.Message}"))}", nameof(module));
        return new(module);
    }

    public void Save(Stream stream) => CvmModuleFormat.Write(stream, _module);

    public CascadeInstance Instantiate(VmOptions? options = null, ExecutionBackend backend = ExecutionBackend.Reference)
    {
        var selected = backend == ExecutionBackend.Automatic
            ? SimdInterpreter.IsSupported ? ExecutionBackend.HardwareSimd : ExecutionBackend.Reference
            : backend;
        return selected switch
        {
            ExecutionBackend.Reference => new(new ReferenceInterpreter(_module, options, ScalarVectorOperations.Instance, moduleIsValidated: true), selected),
            ExecutionBackend.HardwareSimd => new(new SimdInterpreter(_module, options, moduleIsValidated: true), selected),
            _ => throw new ArgumentOutOfRangeException(nameof(backend)),
        };
    }
}

/// <summary>An isolated Cascade VM instance. Instances are not thread-safe.</summary>
public sealed class CascadeInstance
{
    private readonly IVmInterpreter _interpreter;

    internal CascadeInstance(IVmInterpreter interpreter, ExecutionBackend backend)
    {
        _interpreter = interpreter;
        Backend = backend;
    }

    public ExecutionBackend Backend { get; }
    public VmState State => _interpreter.State;
    public ExecutionResult Execute() => _interpreter.Execute();

    /// <summary>Restarts execution with fresh control state while preserving registers and linear memory.</summary>
    public void Restart() => _interpreter.Restart();

    /// <summary>
    /// Runs sequential kernels from packed 64-byte input registers and writes v0
    /// after each successful invocation. Each invocation receives fresh control
    /// state and the instance's original fuel allowance; memory and other vector
    /// registers persist. Stops at the first trap without writing that output.
    /// </summary>
    public BatchExecutionResult ExecuteBatch(ReadOnlySpan<byte> inputs, Span<byte> outputs, int inputRegisterCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(inputRegisterCount);
        if (inputRegisterCount > 32) throw new ArgumentOutOfRangeException(nameof(inputRegisterCount));
        if (outputs.Length % VectorRegister.ByteCount != 0)
            throw new ArgumentException("Outputs must contain whole 64-byte vectors.", nameof(outputs));
        var count = outputs.Length / VectorRegister.ByteCount;
        var stride = inputRegisterCount * VectorRegister.ByteCount;
        if ((long)count * stride != inputs.Length)
            throw new ArgumentException("Inputs must contain one packed register set per output vector.", nameof(inputs));
        if (count != 0 && State.InitialFuel > ulong.MaxValue / (ulong)count)
            throw new ArgumentException("The batch's maximum aggregate fuel exceeds UInt64 capacity.", nameof(outputs));
        if (inputs.Overlaps(outputs))
            throw new ArgumentException("Input and output buffers must not overlap.", nameof(outputs));
        if (inputs.Overlaps(State.LinearMemory.Span) || ((ReadOnlySpan<byte>)outputs).Overlaps(State.LinearMemory.Span))
            throw new ArgumentException("Batch buffers must not alias instance memory.");
        for (var register = 0; register < 32; register++)
        {
            var bytes = State.Register(register).Bytes;
            if (inputs.Overlaps(bytes) || ((ReadOnlySpan<byte>)outputs).Overlaps(bytes))
                throw new ArgumentException("Batch buffers must not alias instance registers.");
        }

        ulong consumed = 0;
        for (var item = 0; item < count; item++)
        {
            Restart();
            var arguments = inputs.Slice(item * stride, stride);
            for (var register = 0; register < inputRegisterCount; register++)
                State.Register(register).ReadFrom(arguments.Slice(register * VectorRegister.ByteCount, VectorRegister.ByteCount));
            var result = _interpreter.ExecuteCore();
            consumed += result.FuelConsumed;
            if (!result.Succeeded) return new(item, consumed, result.Trap);
            State.Register(0).WriteTo(outputs.Slice(item * VectorRegister.ByteCount, VectorRegister.ByteCount));
        }
        return new(count, consumed, null);
    }
}

/// <summary>Completed outputs and aggregate fuel, including the failing invocation if any.</summary>
public sealed record BatchExecutionResult(int CompletedCount, ulong FuelConsumed, VmTrap? Trap)
{
    public bool Succeeded => Trap is null;
}
