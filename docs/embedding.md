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

For repeated kernel invocation, reuse an instance instead of allocating one per
batch:

```csharp
var instance = program.Instantiate(backend: ExecutionBackend.Automatic);

for (var batch = 0; batch < batchCount; batch++)
{
    if (batch != 0)
        instance.Restart();

    // Overwrite this invocation's input registers, then execute.
    var result = instance.Execute();
}
```

`Restart` resets the instruction pointer, fuel, predicate masks, and VM call
stack. It deliberately preserves vector registers and linear memory so a host
can retain kernel data or overwrite only its inputs. Import bindings and limits
also remain attached to the instance. Create a new instance when completely
zero-initialized data state is required.

## Packed kernel batches

`ExecuteBatch` accepts consecutive input register sets and a 64-byte output
slot per invocation. Inputs and outputs use the same little-endian lane encoding
as architectural registers:

```csharp
// For each invocation, inputs contain v0, v1, ... in declaration order.
var inputs = new byte[invocations * argumentCount * 64];
var outputs = new byte[invocations * 64];
// Fill inputs before calling.
var result = instance.ExecuteBatch(inputs, outputs, argumentCount);
Console.WriteLine($"Completed {result.CompletedCount}; fuel {result.FuelConsumed}");
```

The count comes from the output length. Zero arguments and an empty batch are
supported. Each invocation restarts control state and receives the original
per-invocation fuel allowance. Memory and non-input vector registers persist,
and imports retain their ordinary capability bindings and fuel surcharges.
The instance must not be used concurrently or reentrantly by host callbacks.

On a trap, completed outputs remain available; the failing output and all later
slots remain untouched. `CompletedCount` is then the failing invocation's
zero-based index. `FuelConsumed` includes the failing invocation. Memory effects
from instructions before a trap remain visible, as with individual execution.
This is sequential execution, not an atomic transaction across the whole batch.

All buffer sizes and aliasing are checked before execution. Buffers must not
overlap each other or instance registers/memory. Batches whose maximum aggregate
fuel (`count * instance.State.InitialFuel`) cannot fit in `ulong` are rejected.
An empty batch preserves existing instance state. Callers can split large work
into batches to control overall host workload and cancellation granularity.

Measure creation, manual restart, and batching with:

```powershell
dotnet run --project tools/Cascade.Benchmarks -c Release -- --batch
```

The benchmark validates every output against scalar arithmetic, reports median
time and allocations. Packed batches use an internal value-type execution result
and allocate one public batch result on successful completion. Individual
`Execute` calls retain their existing `ExecutionResult` contract. Batching does
not fuse instructions or eliminate interpreter dispatch.

## Execution backends

Reference execution remains the default and defines Cascade semantics. Select
portable hardware SIMD explicitly, or request automatic selection:

```csharp
var simd = program.Instantiate(backend: ExecutionBackend.HardwareSimd);
var automatic = program.Instantiate(backend: ExecutionBackend.Automatic);

Console.WriteLine($"Selected backend: {automatic.Backend}");
Console.WriteLine($"SIMD available: {SimdInterpreter.IsSupported}");
Console.WriteLine($"Native vector width: {SimdInterpreter.VectorWidthBits} bits");
```

`HardwareSimd` throws `PlatformNotSupportedException` when accelerated
`System.Numerics.Vector` operations or little-endian storage are unavailable.
`Automatic` falls back to the reference backend. Both paths share validation,
fuel, traps, memory, control flow, and host-import handling.

## Experimental dispatch fusion

`new VmOptions { EnableInstructionFusion = true }` opts into precomputed
same-width compare/select dispatch pairs on either backend. It is disabled by
default. Bytecode, intermediate mask writes, fuel charges, and trap locations
remain unchanged; this is not a new instruction set or hardware-specific JIT.
Opted-in instances allocate one eligibility byte per instruction and scan the
code during construction. Reuse instances to amortize this setup cost.

Measure your workload before enabling it. The synthetic benchmark is:

```powershell
dotnet run --project tools/Cascade.Benchmarks -c Release -- --fusion
dotnet run --project tools/Cascade.Benchmarks -c Release -- --fusion-compiled
```

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

### Private spill storage (CVM1 1.1)

Modules can declare `vectorSpillSlots` and `maskSpillSlots` through
`CascadeModule`; `CascadeProgram` exposes these counts and `SpillStorageBytes`.
The runtime allocates private instance storage, inaccessible through
`VmState.LinearMemory` and `HostCallContext`. `VmOptions.MaximumSpillBytes`
defaults to 16 MiB and can be set to zero to reject spilled modules. This is a
logical payload budget (64 bytes/vector, 16 bytes/mask), excluding array headers,
and is checked before allocating slots. Each bank is limited to 65,536 entries.

The four spill operations each cost one fuel unit and preserve vector bits or
mask bits plus width. Slots start at zero; mask slots start at width 64.
`Restart` and every batch invocation clear private slots while retaining the
ordinary vector-register and linear-memory persistence contract. Low-level
`call`/`return` share caller-clobbered slots, with no hidden per-frame allocation;
callers must coordinate usage. Source helpers are inlined by the compiler.

Serialization emits CVM1 1.0 without slots and 1.1 with a required spill section.
Existing 1.0 golden binaries remain byte-stable; old readers reject 1.1 explicitly.
Version 1.0 unknown optional section handling remains unchanged, including type 5.
See the [format decision](decisions/2026-10-22-private-spill-storage.md).

Module validation returns at most `ModuleValidator.MaximumDiagnostics` (128)
entries. At the limit, the last entry states that validation stopped and further
diagnostics may be omitted. Invalid modules are still rejected; the cap bounds
diagnostic allocation and exception messages, not the valid instruction count.

The stable v0.1 embedding entry points are `CascadeProgram`, `CascadeInstance`,
`VmOptions`, `ExecutionBackend`, `SimdInterpreter`, `ImportKey`,
`HostImportHandler`, `HostCallContext`,
`HostCallStatus`, `ExecutionResult`, `VmTrap`, `TrapCode`, and read access to
`VmState`. Breaking these contracts requires a documented package-version
change. Low-level interpreter and decoded-module types remain available for
tooling but are not the preferred embedding entry points.

Build packages with:

```powershell
dotnet pack src\Cascade.Core -c Release -o artifacts\packages
dotnet pack src\Cascade.Runtime -c Release -o artifacts\packages
```
