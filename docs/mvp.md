# Cascade v0.1 MVP

- Status: Complete
- Owner: Codex
- Implemented: 2026-10-20
- Last verified: 2026-10-20

## Included

The MVP provides architectural vector and instruction types, pre-execution
validation, isolated VM state, a deterministic reference interpreter, fixed
linear memory, typed traps, fuel accounting, a bounded VM call stack, a
sectioned `CVM1` reader/writer, explicit host-import binding, a CLI, and a dependency-free
conformance executable.

All proposed v0.1 opcodes listed in the plan are implemented, together with the
four accepted explicit mask-width conversions. Vector storage is
a safe 64-byte representation using explicit little-endian lane access and has
no dependency on hardware intrinsics. Arithmetic uses explicit unchecked
wrapping behavior. Signed comparisons use two's-complement interpretation.

Memory operations validate their complete range before writing. Gather and
scatter validate every active lane before any effect, and scatter commits lanes
in ascending order so the highest overlapping lane wins. Inactive gather lanes
preserve the destination lane, as frozen by the executable-semantics decision.

The accepted default host limits are 64 KiB of fixed memory, a 16 MiB
configurable maximum, 1,000,000 fuel units, and 1,024 call frames. Every decoded
instruction consumes one fuel unit, including taken or untaken branches and
calls.

## Portable module container

The CLI reads and writes the accepted `CVM1` version 1.0 format. Files contain a
32-byte header, a 24-byte-per-entry section directory, and canonical aligned
code, constant, function, and import sections.

| Header offset | Size | Meaning |
| --- | --- | --- |
| 0 | 4 | ASCII `CVM1` |
| 4 | 1 | Major version `1` |
| 5 | 1 | Minor version `0` |
| 6 | 2 | Header size, little-endian (`32`) |
| 8 | 4 | Reserved flags; zero |
| 12 | 4 | Section count |
| 16 | 4 | Directory offset (`32`) |
| 20 | 4 | Directory byte length |
| 24 | 4 | Entry instruction index |
| 28 | 4 | Reserved; zero |

Instruction records use the field layout in the plan. The loader rejects
truncation, gaps, overlaps, trailing data, unknown required sections, unknown
opcodes, unsupported flags, nonzero unused fields, invalid references, malformed
UTF-8, and arithmetic overflow before VM instantiation. Full layout details are
in [the CVM1 decision](decisions/2026-10-20-cvm1-module-format.md).

## CLI

`cascade sample <path>` writes a minimal module that computes `40 + 2` in all
eight i64 lanes of `v0`. `cascade run <path>` validates and executes a module,
prints `v0.i64` on success, or prints a typed trap and returns exit code `2`.
Load and validation errors return exit code `1`.

## Not yet included

- Shift instructions or `mul.i64`, which are described ambiguously or omitted
  from the proposed opcode list.
- A compiler, assembler, JIT, AVX-512 backend, or dynamic memory growth.
- Linux build, runtime, and NativeAOT verification.
- A stable public embedding API or package.

## Verification

On Windows x64 with .NET SDK `10.0.300`, the solution builds without warnings,
the conformance executable passes, the CLI writes and runs a module, and a
self-contained NativeAOT `cascade.exe` publishes and runs successfully. The
publish directory also contains an optional PDB that is not a runtime dependency
and should be omitted from release archives. The
Visual Studio Installer directory had to be added temporarily to `PATH` so the
NativeAOT toolchain could discover `vswhere.exe`.

## Recommended next action

Perform a release privacy review, create the public Wixely GitHub repository,
and publish the reviewed MVP — Owner: User — Due: TBD.
