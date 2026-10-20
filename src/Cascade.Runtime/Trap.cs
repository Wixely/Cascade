namespace Cascade.Runtime;

public enum TrapCode
{
    InvalidInstruction,
    InvalidRegister,
    InvalidMemoryAccess,
    InvalidBranchTarget,
    StackOverflow,
    StackUnderflow,
    FuelExhausted,
    InvalidHostImport,
    HostCallFailed,
}

public sealed record VmTrap(TrapCode Code, int InstructionPointer, string Message);

public sealed record ExecutionResult(bool Halted, ulong FuelConsumed, VmTrap? Trap)
{
    public bool Succeeded => Halted && Trap is null;
}
