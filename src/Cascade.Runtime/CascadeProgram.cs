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
            ExecutionBackend.Reference => new(new ReferenceInterpreter(_module, options), selected),
            ExecutionBackend.HardwareSimd => new(new SimdInterpreter(_module, options), selected),
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
}
