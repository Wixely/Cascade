# Cascade

Cascade is an embeddable, deterministic vector virtual machine and a small typed
kernel language. Write data-parallel integer logic in a readable `.cascade`
source file, compile it to portable `.cvm` bytecode, and execute it inside a .NET
application with explicit memory, fuel, and capability limits.

Its purpose is to separate **what a kernel computes** from **how a host runs it**.
The same bytecode can run through a correctness-first reference interpreter or
a hardware-SIMD backend without changing its instruction semantics. Cascade is
useful when portable, inspectable computation and controlled execution matter
alongside performance; it is not currently a replacement for a native optimizing
compiler or a general-purpose scripting language.

## Status

- Phase: Complete v0.1 runtime MVP with post-MVP compiler and SIMD extensions; early-stage project
- Implementation: C# on .NET 10
- Backends: Hardware-independent reference interpreter and portable hardware SIMD
- Source compiler: Typed `.cascade` language version 0.3 with value-numbered IR optimization to portable `CVM1` bytecode
- Module container: Sectioned `CVM1` versions `1.0` and `1.1` (private register spills)
- Verified platforms: Windows x64 and Linux x64, managed self-contained and NativeAOT
- License: [MIT](LICENSE)
- Last reviewed: 2026-10-22

The MVP implements data, arithmetic, bitwise, comparison, mask conversion,
selection, memory, branch, call, return, constant, host-call, and halt instructions. Execution is
bounded by fuel and call depth; invalid memory operations produce typed traps;
gather/scatter addresses are checked before effects occur.

The reference backend defines behavior. On supported little-endian platforms,
the optional hardware-SIMD backend accelerates vector arithmetic, bitwise,
comparison, and selection instructions using the native width exposed by .NET.

## Why Cascade?

- **Readable, replaceable compute rules.** The source file contains the kernel's
  types, expressions, masks, memory operations, and bounded repetition. The host
  handles application state and I/O. Compiled rules can be loaded as data rather
  than compiled into the host executable.
- **Explicit data parallelism without processor-specific source.** One
  `vector<i32>` contains 16 lanes; one `vector<i64>` contains eight. Arithmetic
  and selection work lane by lane. The compiler handles physical register
  assignment, gather/scatter address pairs, and private spilling.
- **Reproducible integer behavior.** Wrapping arithmetic, signed comparisons,
  mask widths, memory checks, and overlapping scatter order have defined
  semantics. The reference backend provides an executable baseline against which
  optimized execution is differentially tested.
- **Host-controlled execution.** Modules are validated before execution. Fuel,
  call-depth limits, fixed linear memory, and a separate spill-storage budget
  constrain VM work and storage. External operations require explicitly bound
  host imports; modules receive no ambient filesystem or network access.
- **A reusable embedding model.** Immutable programs can be shared by isolated
  instances. Instance reuse and packed sequential batches reduce repeated setup
  and allocation, with per-invocation fuel and stop-on-trap behavior.
- **An approachable compiler/runtime laboratory.** The repository includes the
  typed compiler, portable format, two execution backends, scalar oracles,
  malformed-input tests, and benchmarks in C# on .NET 10. This makes it useful
  for studying optimization without leaving correctness implicit.

Deterministic replay requires the same bytecode, initial state, limits, and
deterministic host-import behavior. A host callback can introduce time, randomness,
or other effects if the embedding application grants them. Fuel counts VM work;
it is not a wall-clock deadline and cannot interrupt a blocking host callback.
Validation and capability controls are not a claim of an independently audited
security sandbox or a substitute for process isolation when running hostile code.

## What is it useful for?

The following are intended fits, not claims of production adoption:

| Use case | Why Cascade fits | Boundary to keep in mind |
| --- | --- | --- |
| Cellular automata and integer simulation rules | Apply the same rule to many cells with explicit masks and reproducible results | The host still manages the grid, boundaries, scheduling, and display |
| Application-defined integer transforms | Load replaceable threshold, clamp, classification, or bitwise kernels | This is a small numeric language, not a full plugin application environment |
| Sparse integer updates | Typed masked gather/scatter with checked addresses and defined overlap order | Irregular memory access and interpreter overhead may dominate execution time |
| Replay and backend regression testing | Compare results, traps, and full VM state for identical bytecode | Compiler optimizations may change instruction counts and fuel requirements |
| Compiler and VM experimentation | Explore value numbering, liveness, spilling, dispatch, and batching against a reference | It is an early implementation, not a mature optimizing toolchain |

The [Game of Life demo](examples/Cascade.GameOfLife/Program.cs) is the most
complete application example: a C# host supplies neighborhoods, the kernel
updates 16 cells per call, and an independent scalar implementation checks the
result. [Other demos](examples/README.md) cover arithmetic, masks, capabilities,
memory transforms, sparse access, and register pressure.

## Source code to execution

```text
Typed .cascade source
    -> parser and semantic checks
    -> typed IR optimization and register/spill lowering
    -> portable .cvm bytecode
    -> validation and isolated instance
    -> reference or hardware-SIMD execution
```

For example, this kernel adjusts 16 integer values and caps each result at 10:

```c
kernel choose(
    vector<i32> value,
    vector<i32> increment
) -> vector<i32> {
    let adjusted = value + increment * 2;
    return select(adjusted < 10, adjusted, 10);
}
```

`select` chooses independently for each lane; it is not a scalar `if` statement.
Integer literals are broadcast to all lanes. Arithmetic wraps at the lane width.
Kernel parameters occupy input registers in declaration order, and the returned
vector is placed in `v0`. The host supplies inputs and decides how often to run
the kernel.

The human-authored source for Game of Life is
[`life-rule.cascade`](examples/Cascade.GameOfLife/life-rule.cascade).
[`life-rule.cvm`](examples/Cascade.GameOfLife/life-rule.cvm) is its compiled binary
artifact, not the source file to edit. The [language guide](docs/language.md)
documents helper functions, mutation, bounded `repeat`, typed memory, masks,
and compiler diagnostics.

Architecturally, Cascade has 32 512-bit vector registers, eight predicate masks,
and fixed byte-addressable linear memory. **512 bits is the VM register width,
not an AVX-512 requirement.** The SIMD backend operates using the native vector
width exposed by .NET; the reference backend requires no hardware SIMD.
Kernels that exceed the physical register banks use private spill slots rather
than taking space from application linear memory.

## Comparable projects and alternatives

These are architectural comparisons, not head-to-head benchmark results.
External descriptions were checked against the linked primary documentation on
2026-10-22. The selection guidance is this project's assessment.

| Project or approach | Shared idea | Important difference and when to choose it |
| --- | --- | --- |
| [WebAssembly / Wasmtime](https://docs.wasmtime.dev/) | Portable executable modules embedded in a host; Wasmtime also offers [fuel and SIMD configuration](https://docs.wasmtime.dev/api/wasmtime/struct.Config.html) | A broader application runtime and toolchain ecosystem. Prefer it for general-purpose portable plugins or existing Wasm software; Cascade focuses on a small integer vector ISA and its own kernel language. Cascade does not load Wasm. |
| [ISPC](https://ispc.github.io/ispc.html) | Express data-parallel computation without hand-writing processor intrinsics | ISPC compiles a C-based SPMD language to target code and integrates with C/C++. Prefer it when native data-parallel performance is the priority; Cascade executes portable bytecode with explicit VM limits. |
| [Numba](https://numba.readthedocs.io/en/stable/user/5minguide.html) | Compile numeric source into an executable computation | Numba JIT-compiles supported Python/NumPy code. Prefer it for accelerating an existing Python numerical workflow; Cascade uses its own typed language and a .NET embedding API, without a guest-code JIT. |
| [Direct .NET SIMD](https://learn.microsoft.com/en-us/dotnet/standard/simd) | Process multiple values per operation using vectors or hardware intrinsics | Direct C# vector code avoids Cascade's bytecode dispatch and is part of the host application. Prefer it when kernels are trusted, built in, and raw throughput matters more than independently loadable VM modules. |

Cascade's niche is the combination of a small vector-first source language,
portable bytecode, explicit execution accounting, and a reference implementation
that makes optimized behavior testable. The individual ideas are not unique;
the useful question is whether that combination fits the embedding application.

## Performance: what SIMD does and does not promise

SIMD can make an individual VM arithmetic operation faster than its reference
implementation. It does **not** remove bytecode dispatch, input packing, memory
checks, fuel accounting, or output copying. A short native scalar loop can beat
a vector VM when those costs exceed the arithmetic saved.

The existing Game of Life measurements have shown the scalar C# host faster than
Cascade. That means the complete native baseline finishes the same workload
sooner; it does not mean SIMD instructions are unavailable or incorrect.
Compare both **SIMD versus reference VM** and **Cascade versus native host**,
including setup and data movement, before deciding whether Cascade is suitable.

Instance reuse and packed batches address overhead, but batching is sequential,
not multicore execution. Experimental compare/select dispatch fusion is disabled
by default: measured gains depend on the kernel and reuse, and fresh instances
can regress. See the [fusion evaluation](docs/decisions/2026-10-22-dispatch-fusion-prototype.md)
and [implementation review](docs/implementation-review.md).

Run correctness-checked workloads on the intended deployment machine:

```powershell
dotnet run --project examples\Cascade.GameOfLife -c Release -- --benchmark
dotnet run --project tools\Cascade.Benchmarks -c Release -- --batch
dotnet run --project tools\Cascade.Benchmarks -c Release -- --fusion-compiled
```

## Current limitations

- The source language supports `i32` and `i64` vectors, not floating point,
  strings, objects, or arbitrary application code. Integer division and i64
  multiplication are not implemented.
- Source helpers are inlined; `repeat` counts are compile-time constants.
  Source-level recursion, dynamic loops, data-dependent branches, and host-import
  declarations are not supported, even though the low-level VM has control-flow
  and import instructions.
- There is no guest-code JIT, GPU backend, dynamic memory growth, or VM threading.
  A single instance must not be used concurrently. NativeAOT compiles the .NET
  host/runtime executable; it does not turn each loaded kernel into native code.
- Spill allocation is deliberately conservative. Larger kernels can compile,
  but extra stores/reloads consume memory, instructions, and fuel.
- CVM1 1.0 binaries remain supported. Kernels requiring private spills emit 1.1
  and require a 1.1-capable runtime; old runtimes reject them explicitly.
- Verification covers Windows x64 and Linux x64. Other architectures, macOS,
  browser execution, production security assurance, and performance parity with
  other projects are not claimed.

## Build and run

Install the .NET 10 SDK for development. Run these commands from the repository
root; the ordinary managed build does not require a native C/C++ toolchain.
Start with the demos for checked outputs rather than an empty-input CLI run:

```powershell
dotnet build Cascade.sln
eng\verify.ps1
dotnet run --project tests\Cascade.Compiler.Tests
dotnet run --project tests\Cascade.Runtime.Conformance
dotnet run --project src\Cascade.Cli -- sample sample.cvm
dotnet run --project src\Cascade.Cli -- compile examples\Cascade.GameOfLife\life-rule.cascade
dotnet run --project src\Cascade.Cli -- run sample.cvm
dotnet run --project src\Cascade.Cli -- run sample.cvm --backend auto
dotnet run --project examples\Cascade.Demos
dotnet run --project examples\Cascade.Demos -- source-spill
dotnet run --project examples\Cascade.GameOfLife
dotnet run --project tools\Cascade.Benchmarks -c Release -- --batch
```

The sample halts with every `v0.i64` lane set to `42`. The repository-local VS
Code build task, conformance task, and CLI debug configuration use the same
commands.

`compile` writes a `.cvm` beside the source unless `-o <path>` is supplied.
`run` validates and executes a module with initially zeroed registers and memory;
it is a diagnostic runner, not a general input-data interface. Use the demo host
or embedding API to provide application inputs.

## Embed in a .NET application

Reference `src/Cascade.Runtime/Cascade.Runtime.csproj` from a local application,
or build the `Wixely.Cascade.Runtime` and `Wixely.Cascade.Core` packages as
described in the [embedding guide](docs/embedding.md). Package creation and
consumer tests are verified locally; this README does not assume a public NuGet
release exists.

```csharp
using Cascade.Runtime;

using var stream = File.OpenRead("life-rule.cvm");
var program = CascadeProgram.Load(stream);
var instance = program.Instantiate(
    new VmOptions { Fuel = 100_000, MemorySize = 0 },
    backend: ExecutionBackend.Automatic);

// Supply the kernel's input vectors here; for Life these are v0-v8.
var result = instance.Execute();
if (!result.Succeeded)
    Console.Error.WriteLine($"{result.Trap!.Code} at {result.Trap.InstructionPointer}");
else
    Console.WriteLine(instance.State.Register(0).ReadInt32(0));
```

The snippet is a loading/lifecycle example; without filling the inputs, Life
receives an empty neighborhood. See the [complete host](examples/Cascade.GameOfLife/Program.cs)
for packing actual data. `Automatic` selects SIMD when supported and otherwise
falls back to reference execution; it does not benchmark which is faster.
Call `Restart()` before another invocation: it resets control state, masks, fuel,
and private spills while preserving ordinary vector registers and linear memory.
Overwrite the required inputs, or create a new instance for fresh data state.

## Native publishing and platform verification

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

Windows NativeAOT also requires the native toolchain described in the
[MVP platform notes](docs/mvp.md). A published self-contained executable does not
require the .NET SDK to run.

## Verification and repository layout

As of 2026-10-22, the compiler suite has 24 top-level cases and runtime conformance
has 33, including generated kernels, spill limits, malformed modules, fuel
boundaries, and backend equivalence. These suites have passed managed and
NativeAOT execution on Windows x64 and Linux x64. Tests are executable console
projects; use the verification scripts or `dotnet run`, not just `dotnet test`.

`eng\verify.ps1` runs the managed build, formatting check, compiler/runtime suites,
differential corpora, demos, benchmark smoke checks, CLI checks, and local package
consumer checks. Smoke timings are not performance conclusions. The
[implementation review](docs/implementation-review.md) records the verification
scope and caveats.

| Location | Contents |
| --- | --- |
| `src/Cascade.Core` | Architectural types, instructions, and immutable modules |
| `src/Cascade.Compiler` | Source parsing, typing, optimization, and bytecode lowering |
| `src/Cascade.Runtime` | Validation, serialization, interpreters, and embedding API |
| `src/Cascade.Cli` | Compile, sample, and run commands |
| `examples` | Readable kernel sources and checked host applications |
| `tests` | Compiler, runtime, and differential verification |
| `tools/Cascade.Benchmarks` | Correctness-checked performance experiments |
| `eng` | Windows/Linux verification and packaging scripts |

## Documentation

- [MVP behavior and limitations](docs/mvp.md)
- [Runnable demos](examples/README.md)
- [Embedding API](docs/embedding.md)
- [Kernel language and compiler](docs/language.md)
- [Post-MVP engineering status](docs/post-mvp.md)
- [Compiler and batch implementation review](docs/implementation-review.md)
- [Implementation plan](docs/plan.md)
- [Project preferences](docs/preferences.md)
- [Decision records](docs/decisions/README.md)
- [Agent instructions](AGENTS.md)

The source specification supplied before implementation ended partway through a
draft `VmState.Memory` declaration. The implementation does not infer that
missing text; completed choices are recorded in the decision history.
