using Cascade.Core;

namespace Cascade.Runtime;

public sealed record ValidationError(int InstructionIndex, string Message);

public static class ModuleValidator
{
    public const int MaximumDiagnostics = 128;
    public static IReadOnlyList<ValidationError> Validate(CascadeModule module)
    {
        ArgumentNullException.ThrowIfNull(module);
        var errors = new List<ValidationError>();
        if (module.Instructions.Count == 0)
            errors.Add(new(-1, "A module must contain at least one instruction."));
        if ((uint)module.EntryPoint >= (uint)module.Instructions.Count)
            errors.Add(new(-1, $"Entry point {module.EntryPoint} is out of range."));

        uint previousFunctionEnd = 0;
        var functionStarts = new HashSet<ulong>();
        for (var index = 0; index < module.Functions.Count; index++)
        {
            var function = module.Functions[index];
            functionStarts.Add(function.Start);
            if (function.Start >= function.EndExclusive || function.EndExclusive > module.Instructions.Count)
                errors.Add(new(-1, $"Function {index} has invalid range [{function.Start}, {function.EndExclusive})."));
            if (index != 0 && function.Start < previousFunctionEnd)
                errors.Add(new(-1, $"Function {index} is out of order or overlaps its predecessor."));
            previousFunctionEnd = function.EndExclusive;
            if (errors.Count >= MaximumDiagnostics) return Truncate(errors);
        }

        var importKeys = new HashSet<ImportKey>();
        for (var index = 0; index < module.Imports.Count; index++)
        {
            var import = module.Imports[index];
            if (!ValidImportPart(import.Namespace) || !ValidImportPart(import.Name))
                errors.Add(new(-1, $"Import {index} namespace and name must be nonempty portable identifiers."));
            if (import.AbiVersion != 1)
                errors.Add(new(-1, $"Import {index} uses unsupported ABI version {import.AbiVersion}."));
            if (import.FuelCost is 0 or > 1_000_000)
                errors.Add(new(-1, $"Import {index} fuel cost must be between 1 and 1000000."));
            if (!importKeys.Add(new(import.Namespace, import.Name, import.AbiVersion)))
                errors.Add(new(-1, $"Import {index} duplicates an earlier capability."));
            if (errors.Count >= MaximumDiagnostics) return Truncate(errors);
        }

        for (var index = 0; index < module.Instructions.Count; index++)
        {
            ValidateInstruction(module.Instructions[index], index, module, functionStarts, errors);
            if (errors.Count >= MaximumDiagnostics) return Truncate(errors);
        }

        return errors;
    }

    private static IReadOnlyList<ValidationError> Truncate(List<ValidationError> errors)
    {
        errors.RemoveRange(MaximumDiagnostics - 1, errors.Count - (MaximumDiagnostics - 1));
        errors.Add(new(-1, "Validation stopped at the diagnostic limit; further diagnostics may be omitted."));
        return errors;
    }

    private static bool ValidImportPart(string? value) =>
        !string.IsNullOrEmpty(value) && value.Length <= 255 && value.All(character =>
            char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or '-');

    private static void ValidateInstruction(Instruction instruction, int index, CascadeModule module, HashSet<ulong> functionStarts, List<ValidationError> errors)
    {
        if (!Enum.IsDefined(instruction.Opcode))
        {
            errors.Add(new(index, $"Unknown opcode 0x{(ushort)instruction.Opcode:X4}."));
            return;
        }
        if (instruction.Flags != 0)
            errors.Add(new(index, "Flags are reserved and must be zero."));

        void Register(byte value, string name)
        {
            if (value >= 32) errors.Add(new(index, $"{name} register v{value} is out of range."));
        }
        void Mask(byte value)
        {
            if (value >= 8) errors.Add(new(index, $"Predicate register k{value} is out of range."));
        }
        void Target()
        {
            if (instruction.Immediate >= (ulong)module.Instructions.Count)
                errors.Add(new(index, $"Branch target {instruction.Immediate} is out of range."));
        }
        void Unused(bool condition, string fields)
        {
            if (!condition) errors.Add(new(index, $"Unused {fields} fields must be zero."));
        }

        switch (instruction.Opcode)
        {
            case Opcode.SpillStoreVector:
                Register(instruction.SourceA, "Source A");
                if (instruction.Immediate >= (ulong)module.VectorSpillSlots) errors.Add(new(index, "Vector spill slot is out of range."));
                Unused(instruction.Destination == 0 && instruction.SourceB == 0 && instruction.Mask == 0, "destination, source B, and mask");
                break;
            case Opcode.SpillLoadVector:
                Register(instruction.Destination, "Destination");
                if (instruction.Immediate >= (ulong)module.VectorSpillSlots) errors.Add(new(index, "Vector spill slot is out of range."));
                Unused(instruction.SourceA == 0 && instruction.SourceB == 0 && instruction.Mask == 0, "source and mask");
                break;
            case Opcode.SpillStoreMask:
                Mask(instruction.Mask);
                if (instruction.Immediate >= (ulong)module.MaskSpillSlots) errors.Add(new(index, "Mask spill slot is out of range."));
                Unused(instruction.Destination == 0 && instruction.SourceA == 0 && instruction.SourceB == 0, "vector register");
                break;
            case Opcode.SpillLoadMask:
                Mask(instruction.Destination);
                if (instruction.Immediate >= (ulong)module.MaskSpillSlots) errors.Add(new(index, "Mask spill slot is out of range."));
                Unused(instruction.SourceA == 0 && instruction.SourceB == 0 && instruction.Mask == 0, "source and mask");
                break;
            case Opcode.Zero:
                Register(instruction.Destination, "Destination");
                Unused(instruction.SourceA == 0 && instruction.SourceB == 0 && instruction.Mask == 0 && instruction.Immediate == 0, "source, mask, and immediate");
                break;
            case Opcode.SplatI32:
            case Opcode.SplatI64:
                Register(instruction.Destination, "Destination");
                Unused(instruction.SourceA == 0 && instruction.SourceB == 0 && instruction.Mask == 0, "source and mask");
                break;
            case Opcode.LoadConstant:
                Register(instruction.Destination, "Destination");
                if (instruction.Immediate >= (ulong)module.Constants.Count)
                    errors.Add(new(index, $"Constant index {instruction.Immediate} is out of range."));
                Unused(instruction.SourceA == 0 && instruction.SourceB == 0 && instruction.Mask == 0, "source and mask");
                break;
            case Opcode.Move:
            case Opcode.Not:
                Register(instruction.Destination, "Destination");
                Register(instruction.SourceA, "Source A");
                Unused(instruction.SourceB == 0 && instruction.Mask == 0 && instruction.Immediate == 0, "source B, mask, and immediate");
                break;
            case Opcode.AddI32:
            case Opcode.SubI32:
            case Opcode.MulI32:
            case Opcode.AddI64:
            case Opcode.SubI64:
            case Opcode.And:
            case Opcode.Or:
            case Opcode.Xor:
                Register(instruction.Destination, "Destination");
                Register(instruction.SourceA, "Source A");
                Register(instruction.SourceB, "Source B");
                Unused(instruction.Mask == 0 && instruction.Immediate == 0, "mask and immediate");
                break;
            case Opcode.CompareEqualI32:
            case Opcode.CompareLessThanI32:
            case Opcode.CompareEqualI64:
            case Opcode.CompareLessThanI64:
                Register(instruction.SourceA, "Source A");
                Register(instruction.SourceB, "Source B");
                Mask(instruction.Mask);
                Unused(instruction.Destination == 0 && instruction.Immediate == 0, "destination and immediate");
                break;
            case Opcode.ConvertMask8:
            case Opcode.ConvertMask16:
            case Opcode.ConvertMask32:
            case Opcode.ConvertMask64:
                if (instruction.Destination >= 8) errors.Add(new(index, $"Destination predicate register k{instruction.Destination} is out of range."));
                Mask(instruction.Mask);
                Unused(instruction.SourceA == 0 && instruction.SourceB == 0 && instruction.Immediate == 0, "vector source and immediate");
                break;
            case Opcode.SelectI32:
            case Opcode.SelectI64:
                Register(instruction.Destination, "Destination");
                Register(instruction.SourceA, "Source A");
                Register(instruction.SourceB, "Source B");
                Mask(instruction.Mask);
                Unused(instruction.Immediate == 0, "immediate");
                break;
            case Opcode.Load:
                Register(instruction.Destination, "Destination");
                Register(instruction.SourceA, "Address");
                Unused(instruction.SourceB == 0 && instruction.Mask == 0 && instruction.Immediate == 0, "source B, mask, and immediate");
                break;
            case Opcode.Store:
                Register(instruction.SourceA, "Address");
                Register(instruction.SourceB, "Value");
                Unused(instruction.Destination == 0 && instruction.Mask == 0 && instruction.Immediate == 0, "destination, mask, and immediate");
                break;
            case Opcode.GatherI32:
            case Opcode.ScatterI32:
                Register(instruction.SourceA, "Address");
                if ((instruction.SourceA & 1) != 0 || instruction.SourceA == 31)
                    errors.Add(new(index, "An i32 gather/scatter address pair must start at an even register from v0 through v30."));
                if (instruction.Opcode == Opcode.GatherI32) Register(instruction.Destination, "Destination");
                else Register(instruction.SourceB, "Value");
                Mask(instruction.Mask);
                Unused((instruction.Opcode == Opcode.GatherI32 ? instruction.SourceB == 0 : instruction.Destination == 0) && instruction.Immediate == 0, "value/destination and immediate");
                break;
            case Opcode.GatherI64:
                Register(instruction.Destination, "Destination");
                Register(instruction.SourceA, "Address");
                Mask(instruction.Mask);
                Unused(instruction.SourceB == 0 && instruction.Immediate == 0, "source B and immediate");
                break;
            case Opcode.ScatterI64:
                Register(instruction.SourceA, "Address");
                Register(instruction.SourceB, "Value");
                Mask(instruction.Mask);
                Unused(instruction.Destination == 0 && instruction.Immediate == 0, "destination and immediate");
                break;
            case Opcode.Jump:
            case Opcode.Call:
                Target();
                if (instruction.Opcode == Opcode.Call && module.Functions.Count != 0 &&
                    !functionStarts.Contains(instruction.Immediate))
                    errors.Add(new(index, $"Call target {instruction.Immediate} is not a declared function entry."));
                Unused(instruction.Destination == 0 && instruction.SourceA == 0 && instruction.SourceB == 0 && instruction.Mask == 0, "register and mask");
                break;
            case Opcode.BranchAny:
            case Opcode.BranchAll:
                Mask(instruction.Mask);
                Target();
                Unused(instruction.Destination == 0 && instruction.SourceA == 0 && instruction.SourceB == 0, "vector register");
                break;
            case Opcode.Return:
            case Opcode.Halt:
                Unused(instruction.Destination == 0 && instruction.SourceA == 0 && instruction.SourceB == 0 && instruction.Mask == 0 && instruction.Immediate == 0, "register, mask, and immediate");
                break;
            case Opcode.HostCall:
                if (instruction.Immediate >= (ulong)module.Imports.Count)
                    errors.Add(new(index, $"Import index {instruction.Immediate} is out of range."));
                Unused(instruction.Destination == 0 && instruction.SourceA == 0 && instruction.SourceB == 0 && instruction.Mask == 0, "register and mask");
                break;
        }
    }
}
