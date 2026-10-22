# Cascade

Cascade is a deterministic, portable vector virtual machine with 32 512-bit
general-purpose registers, eight predicate masks, fixed byte-addressable linear
memory, and a correctness-first reference interpreter.

## Status

- Phase: Complete v0.1 MVP
- Implementation: C# on .NET 10
- Backends: Hardware-independent reference interpreter and portable hardware SIMD
- Source compiler: Typed `.cascade` language version 0.3 to portable `CVM1` bytecode
- Module container: Sectioned `CVM1` version `1.0`
- Verified platforms: Windows x64 and Linux x64, managed self-contained and NativeAOT
- License: [MIT](LICENSE)
- Last reviewed: 2026-10-21

The MVP implements data, arithmetic, bitwise, comparison, mask conversion,
selection, memory, branch, call, return, constant, host-call, and halt instructions. Execution is
bounded by fuel and call depth; invalid memory operations produce typed traps;
gather/scatter addresses are checked before effects occur.

The reference backend defines behavior. On supported little-endian platforms,
the optional hardware-SIMD backend accelerates vector arithmetic, bitwise,
comparison, and selection instructions using the native width exposed by .NET.

## Build and run

```powershell
dotnet build Cascade.sln
dotnet run --project tests\Cascade.Compiler.Tests
dotnet run --project tests\Cascade.Runtime.Conformance
dotnet run --project src\Cascade.Cli -- sample sample.cvm
dotnet run --project src\Cascade.Cli -- compile examples\Cascade.GameOfLife\life-rule.cascade
dotnet run --project src\Cascade.Cli -- run sample.cvm
dotnet run --project src\Cascade.Cli -- run sample.cvm --backend auto
dotnet run --project examples\Cascade.Demos
dotnet run --project examples\Cascade.GameOfLife
```

The sample halts with every `v0.i64` lane set to `42`. The repository-local VS
Code build task, conformance task, and CLI debug configuration use the same
commands.

NativeAOT publishing is verified on Windows x64:

```powershell
$env:Path = 'C:\Program Files (x86)\Microsoft Visual Studio\Installer;' + $env:Path
dotnet publish src\Cascade.Cli -c Release -r win-x64 --self-contained true -o artifacts\publish\win-x64
```

The temporary `PATH` addition is needed only when `vswhere.exe` is installed in
the standard Visual Studio Installer directory but is not already discoverable.

Linux x64 managed and NativeAOT verification is available through WSL. On
Ubuntu 24.04, install `dotnet-sdk-10.0`, `clang`, `gcc`, and `zlib1g-dev`, then
run:

```powershell
eng\verify-linux.ps1 -RequireNativeAot
```

## Documentation

- [MVP behavior and limitations](docs/mvp.md)
- [Runnable demos](examples/README.md)
- [Embedding API](docs/embedding.md)
- [Kernel language and compiler](docs/language.md)
- [Post-MVP engineering status](docs/post-mvp.md)
- [Implementation plan](docs/plan.md)
- [Project preferences](docs/preferences.md)
- [Decision records](docs/decisions/README.md)
- [Agent instructions](AGENTS.md)

The source specification supplied before implementation ended partway through a
draft `VmState.Memory` declaration. The implementation does not infer that
missing text; completed choices are recorded in the decision history.
