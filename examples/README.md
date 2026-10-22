# Cascade demos

`Cascade.Demos` is a dependency-free console tour of the public embedding API.
Every demo builds a module, validates and instantiates it through
`CascadeProgram`, executes it, checks the final VM state, and prints the result.

```powershell
dotnet run --project examples\Cascade.Demos
dotnet run --project examples\Cascade.Demos -- list
dotnet run --project examples\Cascade.Demos -- arithmetic masks memory capability
```

| Demo | What it shows |
| --- | --- |
| `arithmetic` | Sixteen `i32` lanes, multiplication, and defined wrapping overflow |
| `masks` | Signed comparison, a 16-bit predicate mask, and lane-wise selection |
| `memory` | Eight noncontiguous `i64` scatter/gather addresses in fixed linear memory |
| `capability` | An explicitly declared, fuel-charged host import with no ambient authority |
| `source` | Language 0.2 functions, bounded repetition, and typed full-register memory |
| `source-gather` | Language 0.3 typed gather/scatter and mask combinators |
| `source-spill` | More live vectors/masks than the register banks, using private CVM1 1.1 slots |

Running without arguments, or with `all`, executes all demos. A failed result
or unexpected VM state returns a nonzero exit code, so the same command is a
useful smoke test.

The `source` demo compiles
[`memory-transform.cascade`](Cascade.Demos/memory-transform.cascade), whose 0.2
features include a typed helper function, mutable vector state, a bounded
`repeat` block, and typed memory storage and loading:

```powershell
dotnet run --project examples\Cascade.Demos -- source
```

The `source-gather` demo compiles
[`gather-transform.cascade`](Cascade.Demos/gather-transform.cascade), which uses
arbitrarily placed i32 address vectors, typed sparse memory operations, and all
mask Boolean combinators:

```powershell
dotnet run --project examples\Cascade.Demos -- source-gather
```

## Private-spill source sample

[`spill-stress.cascade`](Cascade.Demos/spill-stress.cascade) deliberately retains
36 vector values and 12 masks. It is a register-pressure test, not a claim that
this expression needs that many temporaries in an optimal implementation. The
current allocator emits 257 instructions, 14 vector slots and six mask slots
(992 private bytes), with 44 stores and 44 reloads. The runner verifies scalar
results on both available backends with **zero linear-memory bytes**.

```powershell
dotnet run --project examples\Cascade.Demos -- source-spill
dotnet run --project src\Cascade.Cli -- compile examples\Cascade.Demos\spill-stress.cascade -o artifacts\spill-stress.cvm
dotnet run --project src\Cascade.Cli -- run artifacts\spill-stress.cvm
```

Old runtimes reject the 1.1 binary explicitly. Kernels not needing private slots
still compile to the original 1.0 format.

## Game of Life bytecode sample

`Cascade.GameOfLife` compiles the typed kernel source in
[`life-rule.cascade`](Cascade.GameOfLife/life-rule.cascade) and verifies that it
exactly reproduces the committed portable
[`life-rule.cvm`](Cascade.GameOfLife/life-rule.cvm). The kernel contains the
vectorized Life rule and updates 16 cells at a time. The C# host and benchmark
live in [`Program.cs`](Cascade.GameOfLife/Program.cs).

```powershell
dotnet run --project examples\Cascade.GameOfLife
```

The runner checks that the committed bytecode exactly matches the compiled
source, simulates a glider and an R-pentomino on a wrapping
32-by-16 board, and verifies an identical replay. Regenerate the bytecode
deliberately with either entry point:

```powershell
dotnet run --project src\Cascade.Cli -- compile examples\Cascade.GameOfLife\life-rule.cascade
dotnet run --project examples\Cascade.GameOfLife -- --write-module examples\Cascade.GameOfLife\life-rule.cvm
```

The expanded workload also cross-checks every result against a scalar Life
implementation and reports cell throughput. Its benchmark preset uses a
deterministic 256-by-256 seed for 50 generations without printing the board:

```powershell
dotnet run --project examples\Cascade.GameOfLife -c Release -- --benchmark
```

Override `--width`, `--height`, and `--generations` to scale the workload. The
total cell count must be divisible by 16. Add `--verify-replay` to benchmark a
second identical Cascade execution. The reference interpreter provides the
semantic baseline. When hardware SIMD is available, the benchmark executes both
backends, requires identical output, and reports the measured SIMD speedup and
active hardware-vector width. The runner reuses each backend's VM instance and
calls `Restart` between 16-cell batches, preserving data storage while resetting
control state and fuel.
