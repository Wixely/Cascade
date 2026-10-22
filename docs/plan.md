# Cascade VM v0.1 implementation plan

- Status: v0.1 MVP complete
- Owner: TBD
- Created: 2026-10-20
- Last reviewed: 2026-10-22
- Target date: None
- Intended repository: Public GitHub repository under Wixely; remote not yet created
- License: MIT
- Source: User-supplied "Cascade VM v0.1 — Technical Specification"

## Outcome

Build the first implementable version of Cascade: a small, deterministic, portable vector virtual machine that executes validated programs correctly through a reference interpreter before a compiler or AVX-512 backend is introduced.

The first useful result is a tested C#/.NET 10 runtime that executes decoded in-memory instructions. The portable `.cvm` module format follows once instruction semantics are stable.

## Success criteria

- [x] The reference interpreter implements the frozen v0.1 instruction set deterministically.
- [x] MVP modules and memory operations are validated before invalid input can produce partial effects.
- [x] Runtime failures produce typed traps without terminating the host process.
- [x] Fuel and call-stack limits reliably bound execution.
- [x] Tests cover lane semantics, wrapping arithmetic, masks, memory bounds, scatter order, control flow, calls, traps, malformed modules, imports, and deterministic replay.
- [x] A CLI loads, validates, and runs sectioned `CVM1` `.cvm` modules and reports results or traps.
- [x] Windows and Linux x64 behavior are verified with managed self-contained and NativeAOT builds.
- [x] NativeAOT and single-executable publishing are evaluated against the working runtime, with the Windows toolchain note documented.
- [x] Repository-local VS Code build and debug configuration is aligned with normal development commands.
- [x] A typed kernel source language compiles deterministically to validated `CVM1` modules.

## Architecture baseline

| Component | v0.1 baseline |
| --- | --- |
| General-purpose registers | 32 × 512-bit registers, `v0`–`v31` |
| Predicate registers | 8 × 64-bit registers, `k0`–`k7` |
| Memory | One byte-addressable, zero-initialized linear memory per VM instance |
| Addresses | Unsigned 64-bit byte addresses held in vector lanes |
| Instruction encoding | Fixed 16-byte records initially |
| Byte order | Little-endian |
| Integer arithmetic | Explicitly typed and wrapping |
| Branches | Direct instruction indices, including mask-based conditions |
| Functions | VM-managed `call` and `ret` stack |
| External operations | Explicit host imports |
| Backends | Reference interpreter as authority; portable hardware SIMD available post-MVP |
| Source language | Typed `.cascade` kernels compiled ahead of time to `CVM1` |
| Module format | Versioned `.cvm` binary |
| Implementation | C# on .NET 10 |

All program-visible general-purpose data lives in vector registers or linear memory. The instruction pointer, call stack, masks, fuel, and execution bookkeeping are VM control state rather than scalar variables exposed to programs.

Modules are immutable and may be shared by instances. Registers, masks, mask widths, memory, call stack, instruction pointer, fuel, and import bindings belong to one instance and are not shared unless the host explicitly arranges it.

## Register and lane semantics

Each vector register contains exactly 64 bytes. An instruction interprets those bits as 64 × 8-bit, 32 × 16-bit, 16 × 32-bit, or 8 × 64-bit lanes. Registers have no permanent type.

- Signed and unsigned addition, subtraction, and multiplication wrap modulo `2^N`.
- Integer division is outside v0.1.
- Logical shifts with a count greater than or equal to the lane width produce zero.
- Signed comparisons use two's-complement interpretation.
- Comparisons write predicate masks rather than vectors.
- Inactive predicate bits are zero.
- A vector operation cannot access bytes outside its defined lanes.

## Memory semantics

The initial VM uses fixed-size memory supplied at instantiation. Suggested host defaults are 64 KiB initially and a 16 MiB maximum, with no growth in v0.1. These are configurable host limits, not architectural maxima.

- `load` and `store` transfer 64 bytes at the address in lane 0 of the address register.
- `gather.i32` and `scatter.i32` use an even-numbered address-register pair. The first register supplies addresses for lanes 0–7 and the next supplies lanes 8–15.
- `gather.i64` and `scatter.i64` use the eight addresses in one vector register.
- Every active address is checked before any access, preventing partial gather/scatter effects when a later lane is invalid.
- Overlapping scatter destinations execute in ascending lane order, so the highest-numbered overlapping lane wins. Optimized backends must reproduce this result.

## Predicates and control flow

The meaningful mask width follows the lane type: 64 bits for `i8`, 32 for `i16`, 16 for `i32`, and 8 for `i64`. Each mask stores both its value and active width. Comparisons establish the width; changing widths requires an explicit conversion.

- `br.any` branches when any active bit is set.
- `br.all` branches when all active bits are set.
- `jmp` branches unconditionally.

The stored width prevents stale high bits or an unrelated earlier instruction from changing branch behavior.

## Proposed v0.1 instruction set

| Group | Instructions |
| --- | --- |
| Data | `zero`, `mov`, `splat.i32`, `splat.i64` |
| Arithmetic | `add.i32`, `sub.i32`, `mul.i32`, `add.i64`, `sub.i64` |
| Bitwise | `and`, `or`, `xor`, `not` |
| Comparisons | `cmpeq.i32`, `cmplt.i32`, `cmpeq.i64`, `cmplt.i64` |
| Mask conversion | `mask.to8`, `mask.to16`, `mask.to32`, `mask.to64` |
| Selection | `select.i32`, `select.i64` |
| Memory | `load`, `store`, `gather.i32`, `scatter.i32`, `gather.i64`, `scatter.i64` |
| Control flow | `jmp`, `br.any`, `br.all`, `call`, `ret`, `halt` |

`splat.i64` uses an instruction plus a 64-bit immediate payload. The proposed calling convention uses `v0` as the return register, `v0`–`v7` for arguments, `v8`–`v15` as callee-saved registers, and the rest as caller-saved. Test this convention before freezing it.

## Portable module format

The versioned `.cvm` file begins with a 32-byte header containing `CVM1` magic, format version `1.0`, flags, section count, and reserved metadata. A section directory locates code, constants, functions, imports, and future sections.

| Offset | Size | Instruction field |
| --- | --- | --- |
| 0 | 2 bytes | Opcode |
| 2 | 1 byte | Destination |
| 3 | 1 byte | Source A |
| 4 | 1 byte | Source B |
| 5 | 1 byte | Mask register |
| 6 | 2 bytes | Flags/type metadata |
| 8 | 8 bytes | Immediate or section index |

Unused fields are zero. An immediate may reference the constant section so full 512-bit constants do not enlarge instruction records.

Before instantiation, reject unknown opcodes, malformed register references, unsupported flags, nonzero unused fields, invalid or overlapping section boundaries, invalid constant references, and out-of-range branch or function targets. Use checked arithmetic before allocation, slicing, or decoding.

## Runtime lifecycle and traps

1. Load the module.
2. Validate structure and instructions.
3. Instantiate isolated VM state.
4. Resolve declared host imports.
5. Execute under configured limits.
6. Return a result or controlled trap.

Initial trap codes:

- `InvalidInstruction`
- `InvalidRegister`
- `InvalidMemoryAccess`
- `InvalidBranchTarget`
- `StackOverflow`
- `StackUnderflow`
- `FuelExhausted`
- `InvalidHostImport`
- `HostCallFailed`

Every executed instruction, branch, and host call consumes fuel. Decide host-call charging before imports are implemented because external work may cost substantially more than an ordinary instruction.

## Solution shape

```text
Cascade.sln
src/
  Cascade.Core/          Architectural types, opcodes, decoded instructions, modules
  Cascade.Runtime/       Validation, VM state, reference interpreter, traps, imports
  Cascade.Cli/           Load, validate, execute, and report
tests/
  Cascade.Runtime.Tests/ Semantic, validation, boundary, and determinism tests
```

Keep the architectural vector representation independent of hardware intrinsics. The source specification suggests an explicitly laid-out 64-byte value with an encapsulated unsafe fixed buffer and typed lane helpers. Before adopting it, compare it with a safe representation and verify lifetime safety, copying, zero initialization, endianness, NativeAOT behavior, and performance. Unsafe storage is an implementation option, not an architectural semantic.

The decoded `Instruction` type is distinct from the 16-byte disk record. Execute decoded in-memory instructions first so semantics and diagnostics can stabilize before binary serialization is added.

## Scope

### In scope for v0.1

- Architectural types and deterministic instruction semantics.
- Decoded in-memory instructions and module validation.
- Reference interpreter, fixed linear memory, and bounds checks.
- Predicate masks with explicit active widths.
- Direct branches, calls, bounded stack, halt, and execution fuel.
- Explicit import declarations and guarded resolution.
- `.cvm` reader/writer after semantics stabilize.
- CLI execution, diagnostics, examples, and conformance tests.

### Out of scope for v0.1

- Source-language compiler or JIT.
- Text assembler, unless later justified as a narrow test aid.
- AVX-512 backend.
- Integer division.
- Dynamic memory growth.
- Threads or shared mutable execution state.
- Stable public embedding API beyond the first runtime's needs.

## Constraints and dependencies

- Determinism includes exceptional cases and overlapping writes.
- The reference backend defines behavior optimized backends reproduce.
- Validate untrusted lengths and offsets before allocating or executing.
- Imports are explicit capabilities; modules receive no ambient filesystem, network, process, environment, clock, or randomness access.
- Follow [project preferences](preferences.md), including the Python/Node.js prohibition.
- Creating the public GitHub repository and pushing remain separate external actions requiring a complete pre-push review.

## Assumptions

- Cascade is a new standalone project.
- `.cvm` is intended to be portable between Windows and Linux.
- The 16-byte encoding and opcode list remain revisable until deliberately frozen.
- The supplied specification is the canonical initial input. It ends partway through a draft `VmState.Memory` declaration, so missing implementation text has not been inferred.

## Risks

| Risk | Likelihood | Impact | Mitigation |
| --- | --- | --- | --- |
| Edge cases remain ambiguous when the format freezes | Medium | High | Resolve questions through executable conformance cases first |
| Unsafe register storage permits invalid spans or unintended copies | Medium | High | Compare safe/unsafe layouts and test lifetime and aliasing behavior |
| Validator arithmetic overflows on hostile modules | Medium | High | Use checked arithmetic and validate before allocation or slicing |
| Gather/scatter creates partial effects on a trap | Medium | High | Validate every active lane before any access |
| Optimized backends diverge from reference behavior | Medium | High | Differential-test shared conformance vectors |
| Imports bypass determinism, fuel, or capability limits | Medium | High | Bound, meter, audit, and convert failures to traps |
| Encoding is costly to evolve | Medium | Medium | Reserve deliberately, reject unknown flags, and version compatibility rules |
| NativeAOT conflicts with implementation choices | Low | Medium | Publish and execute an early probe before adding dynamic dependencies |

## Milestones

Targets remain unset until the project is scheduled.

| Milestone | Target | Exit condition |
| --- | --- | --- |
| 0. Establish public repository | TBD | Public Wixely GitHub repository exists under MIT, the reviewed `main` branch is pushed, and branch settings are recorded |
| 1. Freeze executable semantics | Completed 2026-10-20 | Instructions, traps, calls, masks, fuel, and imports have conformance cases |
| 2. Reference core | Completed 2026-10-20 | In-memory programs pass core semantic and trap tests |
| 3. Memory and control flow | Completed 2026-10-20 | Bounds, atomic gather/scatter, overlap, branches, calls, stack, and fuel pass |
| 4. Portable modules | Completed 2026-10-20 | `.cvm` round-trips valid fixtures and rejects malformed/adversarial fixtures |
| 5. Usable v0.1 | Completed 2026-10-20 | CLI, examples, diagnostics, Windows verification, and publish checks pass |
| 6. Optimization readiness | Completed 2026-10-20 | Benchmark corpus and differential harness are ready for optimized-backend work |
| 7. Portable hardware SIMD | Completed 2026-10-21 | Accelerated vector operations match complete reference state under differential tests |
| 8. Typed kernel compiler | Completed 2026-10-21 | Game of Life compiles from typed source with semantic, diagnostic, and determinism tests |
| 9. Source language 0.2 | Completed 2026-10-21 | Typed functions, bounded repetition, mutation, and memory operations compile and execute |
| 10. Source language 0.3 | Completed 2026-10-22 | Typed gather/scatter and mask combinators compile and execute without leaking register-pair constraints |

## Verification strategy

- Table-driven tests for every opcode and supported lane width.
- Boundary values for wrapping arithmetic, signed comparisons, shifts, masks, and addresses.
- State-before/state-after assertions proving traps have no partial writes.
- Malformed modules covering truncation, overflow, overlap, unknown values, and invalid references.
- Fuel tests for straight-line code, loops, branches, calls, and imports.
- Recursive and malformed call-flow tests for stack limits and invalid returns.
- Golden `.cvm` byte fixtures once byte order and encoding freeze.
- Replay tests comparing complete final state and trap information.
- Differential tests between reference and future optimized backends.
- Published NativeAOT smoke tests on each platform before support claims.

## Next actions

- [ ] Create the public Wixely GitHub repository, review the complete outgoing range, and push `main` — Owner: User — Due: TBD
- [x] Accept or revise the proposed memory defaults, calling convention, fixed encoding, and instruction freeze — Owner: User — Completed: 2026-10-20
- [x] Resolve semantic questions and turn each answer into a conformance test or decision record — Owner: Codex — Completed: 2026-10-20
- [x] Create the .NET 10 solution, projects, VS Code configuration, and first NativeAOT feasibility probe — Owner: Codex — Completed: 2026-10-20
- [x] Implement architectural types and the decoded interpreter before `.cvm` serialization — Owner: Codex — Completed: 2026-10-20
- [x] Add golden `CVM1` fixtures, a stable embedding facade, packages, benchmark corpora, and a differential harness — Owner: Codex — Completed: 2026-10-20
- [x] Verify NativeAOT publish and execution on Linux — Owner: Codex — Completed: 2026-10-20
- [x] Implement and differentially verify a portable hardware-SIMD backend — Owner: Codex — Completed: 2026-10-21
- [x] Profile dispatch and instance-creation overhead and add reusable instance execution — Owner: Codex — Completed: 2026-10-21
- [x] Define a typed kernel language and compile Game of Life source to `CVM1` — Owner: Codex — Completed: 2026-10-21
- [x] Design typed memory access, functions, and bounded control flow for source language 0.2 — Owner: Codex — Completed: 2026-10-21
- [x] Design typed gather/scatter and mask combinators for source language 0.3 — Owner: Codex — Completed: 2026-10-22
- [ ] Add a typed compiler IR with liveness and dead-code elimination — Owner: TBD — Due: TBD
- [ ] Design and benchmark batched kernel execution before choosing an AVX2- or AVX-512-specific backend — Owner: TBD — Due: TBD

## Open questions

- [x] Freeze `CVM1` only after executable conformance tests stabilize; its layout is recorded in the accepted format decision.
- [x] Accept 64 KiB initial memory, 16 MiB default maximum, and no v0.1 growth.
- [x] Accept `v0` return, `v0`–`v7` arguments, and `v8`–`v15` callee-saved.
- [x] Provide explicit 8/16/32/64-bit mask conversions using low-bit copy, truncation, and zero-filled widening.
- [x] Forbid zero-width masks; `br.all` covers the stored active width.
- [x] Omit shifts from v0.1 because the proposed opcode list excluded them.
- [x] Omit `mul.i64` from v0.1 because the proposed opcode list excluded it.
- [x] Freeze alignment, duplicate/unknown-section, ordering, count, reserved-byte, and forward-compatibility rules in the `CVM1` decision.
- [x] Freeze the import ABI and capability model; its binary record representation remains part of the `CVM1` design.
- [x] Charge imports one instruction plus a declared fixed surcharge, precharged before host work.
- [x] Stabilize trap code and instruction index; redact host exception details.
- [x] Use a 1,024-frame default maximum and `StackUnderflow` for empty `ret`.
- [x] Make `halt` expose complete final VM state; entry-point contracts may interpret `v0`.
- [x] Make self-modifying code impossible by separating immutable code from linear memory.

Resolved execution questions are specified in [the executable-semantics decision](decisions/2026-10-20-executable-semantics.md).
