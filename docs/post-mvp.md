# Post-MVP engineering status

- Status: Complete
- Owner: Codex
- Last verified: 2026-10-21

## Linux verification

The CLI, conformance suite, and differential harness were cross-published as
self-contained `linux-x64` applications and executed under Ubuntu 24.04.4 on
WSL2. All 25 runtime conformance cases and four compiler cases passed, all five
differential corpus cases matched, the harness detected a deliberate divergence,
and the CLI compiled `.cascade` source and ran the resulting `CVM1` module.

Linux NativeAOT publishing and execution are verified in the same environment
with .NET SDK `10.0.113`, Clang `18.1.3`, GCC `13.3.0`, and `zlib1g-dev`. The
strict `eng\verify-linux.ps1 -RequireNativeAot` workflow publishes the CLI on
Linux and executes its native binary against a generated `CVM1` module.

## Embedding packages

`Wixely.Cascade.Core` and `Wixely.Cascade.Runtime` pack successfully at version
0.1.0 with MIT metadata and package readmes. The runtime package declares an
exact dependency on `Wixely.Cascade.Core` 0.1.0. See [Embedding
Cascade](embedding.md) for the supported lifecycle and API surface.

`eng\verify-packages.ps1` builds both packages into an isolated local feed,
restores a clean temporary consumer using only `Wixely.Cascade.Runtime`, and
executes a save/load/run cycle through the public embedding facade.

## Golden modules

The conformance suite copies, loads, and byte-for-byte rewrites two committed
fixtures:

| Fixture | SHA-256 |
| --- | --- |
| `minimal.cvm` | `6DF8C72093A551ED39086854B76987CFF0C3628D791DD9655BAAAA8F8E07DCBC` |
| `complete.cvm` | `C5A0AD2EFA847FA2F47D25B5F900BACE2494EE5452F2CB5FE479FB12B0C2C1F6` |

Every truncated prefix of each fixture is also required to fail as malformed.

## Optimization readiness

`Cascade.Runtime.Differential` compares results, all vector registers, all
predicate registers, complete memory, instruction pointer, and remaining fuel
between backends. Its self-test deliberately changes one register byte and
proves that the harness reports divergence.

`Cascade.Benchmarks` supplies arithmetic, linear-memory, mask-selection, and
call-heavy corpora. It uses only `Stopwatch`, supports a quick smoke mode, and
reports executed instructions and throughput without making machine-independent
performance assertions.

## Hardware SIMD backend

`SimdInterpreter` accelerates vector splats, arithmetic, bitwise operations,
signed comparisons, and masked selection through `System.Numerics.Vector` while
sharing the reference interpreter's validation, control, memory, fuel, import,
and trap paths. The embedding API supports explicit reference, explicit SIMD,
and automatic selection. The differential harness compares complete state after
all accelerated opcode families.

On the verified Windows x64 development machine, .NET selected 256-bit hardware
vectors. The 256-by-256 Game of Life workload produced the same state hash on
the reference, SIMD, and scalar implementations. Reusing one validated VM
instance per backend reduced per-batch setup overhead; a 2026-10-21 managed run
measured 307.4 ms for the reference VM, 246.1 ms for SIMD, and 64.5 ms for the
direct scalar oracle. Timing is machine-dependent and is not a portable
performance guarantee.

`CascadeInstance.Restart` supports this kernel pattern by resetting control
state and fuel while preserving registers and linear memory. The instance and
dispatch overhead is now lower, but the remaining scalar gap shows that
per-instruction dispatch dominates short kernel invocations.

```powershell
eng\verify-packages.ps1
eng\verify-linux.ps1
dotnet run --project tests\Cascade.Runtime.Differential -c Release
dotnet run --project tools\Cascade.Benchmarks -c Release -- --quick
```

## Typed kernel compiler

`Cascade.Compiler` implements version 0.1 of the `.cascade` source language and
the CLI compiles source directly to `CVM1`. The compiler performs parsing,
static lane-type checking, deterministic register allocation, and lowering
without dynamic code generation or external parser dependencies. See [Cascade
kernel language](language.md).

The Game of Life demo now treats `life-rule.cascade` as its source of truth. At
startup it recompiles the source and requires byte-for-byte equality with the
committed `life-rule.cvm` before running either backend.

## Recommended next action

Design typed memory operations, functions, and bounded control flow for source
language version 0.2, using executable semantic tests before expanding the
parser — Owner: TBD — Due: TBD.

The independent runtime-performance action remains designing and measuring a
batched kernel API before considering CPU-specific AVX2 or AVX-512 code — Owner:
TBD — Due: TBD.

Release remains a separate user-owned action: perform the privacy review, create
the public Wixely GitHub repository, and publish the reviewed v0.1 work — Owner:
User — Due: TBD.
