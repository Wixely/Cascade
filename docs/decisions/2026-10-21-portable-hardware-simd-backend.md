# Add a portable hardware-SIMD execution backend

- Status: Accepted
- Date: 2026-10-21
- Decision owner: User

## Context

The reference interpreter executes Cascade's 512-bit architectural operations
with scalar C# loops. That implementation is the semantic authority, but it
cannot demonstrate the throughput benefit expected from vector workloads such
as the Game of Life sample. The user directed implementation of a SIMD backend
after the reference runtime, conformance suite, and differential harness were
complete.

The architectural register representation must remain independent of CPU
intrinsics, and optimized execution must preserve traps, fuel, imports, memory
atomicity, mask widths, and complete final state.

## Decision

Add `ExecutionBackend.Reference`, `HardwareSimd`, and `Automatic` choices to the
embedding API. Reference remains the default and semantic authority. Automatic
selects hardware SIMD only when `System.Numerics.Vector` is accelerated on a
little-endian platform; explicitly requesting unavailable SIMD throws
`PlatformNotSupportedException`.

The hardware-SIMD backend accelerates splats, `i32` arithmetic, `i64`
addition/subtraction, bitwise operations, signed comparisons, and masked
selection. It uses the hardware vector width exposed by .NET and divides each
architectural 512-bit register into as many native vectors as required. The
common interpreter retains instruction dispatch, validation, control flow,
fuel, calls, imports, loads, stores, gathers, scatters, and traps.

No CPU-specific type is exposed by `Cascade.Core`, stored in `VmState`, or
encoded in `CVM1`. The same module can execute through either backend.

## Consequences

- Optimized and reference instances have identical observable VM semantics.
- Differential tests compare every accelerated opcode and complete VM state.
- The Game of Life benchmark executes both backends and requires the same final
  state hash before reporting speedup.
- Hardware width depends on the machine. A 512-bit architectural register may
  execute as two 256-bit vectors or four 128-bit vectors.
- A future AVX2- or AVX-512-specific backend remains a separate optimization
  decision; this change does not embed hardware intrinsics in the architecture.

## Follow-up

- [x] Implement portable hardware-SIMD vector operations — Owner: Codex — Completed: 2026-10-21
- [x] Add embedding backend selection and automatic fallback — Owner: Codex — Completed: 2026-10-21
- [x] Differential-test the SIMD backend against the reference interpreter — Owner: Codex — Completed: 2026-10-21
- [x] Measure the Game of Life workload through both backends — Owner: Codex — Completed: 2026-10-21
- [ ] Profile dispatch and instance-creation overhead before selecting another optimization — Owner: TBD — Due: TBD
