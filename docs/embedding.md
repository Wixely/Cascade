# Embedding Cascade

- API version: 0.1
- NuGet packages: `Wixely.Cascade.Runtime` and `Wixely.Cascade.Core`
- Threading: A `CascadeProgram` is immutable; a `CascadeInstance` is isolated and
  must not be used concurrently.

## Lifecycle

```csharp
using Cascade.Runtime;

using var moduleStream = File.OpenRead("program.cvm");
var program = CascadeProgram.Load(moduleStream);
var instance = program.Instantiate(new VmOptions { Fuel = 100_000 });
var result = instance.Execute();

if (!result.Succeeded)
    Console.Error.WriteLine($"{result.Trap!.Code} at {result.Trap.InstructionPointer}");
```

`CascadeProgram.Load` performs structural and instruction validation before it
returns. `Instantiate` creates fresh registers, predicates, memory, fuel, and
call-stack state. Complete final state is available through `CascadeInstance.State`.

## Host imports

Bind only capabilities the host intends to grant:

```csharp
var key = new ImportKey("example", "answer", 1);
var options = new VmOptions
{
    ImportBindings = new Dictionary<ImportKey, HostImportHandler>
    {
        [key] = context =>
        {
            context.Register(0).WriteUInt64(0, 42);
            return HostCallStatus.Success;
        },
    },
};
```

Every declared import must have an exact namespace, name, and ABI-version match.
The host-call context exposes caller-saved registers and linear memory but not
callee-saved registers. Host exceptions become redacted `HostCallFailed` traps.

## Compatibility

The stable v0.1 embedding entry points are `CascadeProgram`, `CascadeInstance`,
`VmOptions`, `ImportKey`, `HostImportHandler`, `HostCallContext`,
`HostCallStatus`, `ExecutionResult`, `VmTrap`, `TrapCode`, and read access to
`VmState`. Breaking these contracts requires a documented package-version
change. Low-level interpreter and decoded-module types remain available for
tooling but are not the preferred embedding entry points.

Build packages with:

```powershell
dotnet pack src\Cascade.Core -c Release -o artifacts\packages
dotnet pack src\Cascade.Runtime -c Release -o artifacts\packages
```
