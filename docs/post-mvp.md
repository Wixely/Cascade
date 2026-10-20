# Post-MVP engineering status

- Status: Complete
- Owner: Codex
- Last verified: 2026-10-20

## Linux verification

The CLI, conformance suite, and differential harness were cross-published as
self-contained `linux-x64` applications and executed under Ubuntu 24.04.4 on
WSL2. All 23 conformance cases passed, all four differential corpus cases
matched, the harness detected a deliberate divergence, and the CLI wrote and ran
a `CVM1` module.

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

```powershell
eng\verify-packages.ps1
eng\verify-linux.ps1
dotnet run --project tests\Cascade.Runtime.Differential -c Release
dotnet run --project tools\Cascade.Benchmarks -c Release -- --quick
```

## Recommended next action

Perform the release privacy review, create the public Wixely GitHub repository,
and publish the reviewed v0.1 work — Owner: User — Due: TBD.
