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

    public CascadeInstance Instantiate(VmOptions? options = null) => new(new ReferenceInterpreter(_module, options));
}

/// <summary>An isolated Cascade VM instance. Instances are not thread-safe.</summary>
public sealed class CascadeInstance
{
    private readonly ReferenceInterpreter _interpreter;

    internal CascadeInstance(ReferenceInterpreter interpreter) => _interpreter = interpreter;

    public VmState State => _interpreter.State;
    public ExecutionResult Execute() => _interpreter.Execute();
}
