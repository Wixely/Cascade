# Cascade VM v0.1 implementation plan

- Status: Planned
- Owner: TBD
- Created: 2026-10-20
- Last reviewed: 2026-10-20
- Target date: None
- Intended repository: Public GitHub repository under Wixely; remote not yet created
- License: MIT
- Source: User-supplied "Cascade VM v0.1 — Technical Specification"

## Outcome

Build the first implementable version of Cascade: a small, deterministic, portable vector virtual machine that executes validated programs correctly through a reference interpreter before a compiler or AVX-512 backend is introduced.

The first useful result is a tested C#/.NET 10 runtime that executes decoded in-memory instructions. The portable `.cvm` module format follows once instruction semantics are stable.

## Success criteria

- [ ] The reference interpreter implements the frozen v0.1 instruction set deterministically.
- [ ] Modules and memory operations are validated before invalid input can produce partial effects.
- [ ] Invalid programs produce typed traps without terminating the host process.
- [ ] Fuel and call-stack limits reliably bound execution.
- [ ] Tests cover lane semantics, wrapping arithmetic, masks, memory bounds, scatter order, control flow, calls, traps, malformed modules, and deterministic replay.
- [ ] A CLI loads, validates, and runs `.cvm` modules and reports results or traps.
- [ ] Windows behavior is verified; Linux behavior is tested before support is claimed.
- [ ] NativeAOT and single-file publishing are evaluated against the working runtime, with any exception documented.
- [ ] Repository-local VS Code build and debug configuration works with normal development commands.

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
| Backends | Reference interpreter first, AVX-512 later |
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
| 1. Freeze executable semantics | TBD | Instructions, traps, calls, masks, fuel, and imports have conformance cases |
| 2. Reference core | TBD | In-memory programs pass core semantic and trap tests |
| 3. Memory and control flow | TBD | Bounds, atomic gather/scatter, overlap, branches, calls, stack, and fuel pass |
| 4. Portable modules | TBD | `.cvm` round-trips valid fixtures and rejects malformed/adversarial fixtures |
| 5. Usable v0.1 | TBD | CLI, examples, diagnostics, platform verification, and publish checks pass |
| 6. Optimization readiness | TBD | Benchmark corpus and differential harness are ready for AVX-512 work |

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
- [ ] Accept or revise the proposed memory defaults, calling convention, fixed encoding, and instruction freeze — Owner: User — Due: TBD
- [ ] Resolve semantic questions and turn each answer into a conformance test or decision record — Owner: TBD — Due: TBD
- [ ] Create the .NET 10 solution, projects, VS Code configuration, and first NativeAOT feasibility probe — Owner: TBD — Due: TBD
- [ ] Implement architectural types and the decoded interpreter before `.cvm` serialization — Owner: TBD — Due: TBD

## Open questions

- Does `CVM1`/format `1.0` freeze immediately or after conformance tests stabilize?
- Are 64 KiB initial memory, 16 MiB default maximum, and no growth accepted defaults?
- Is the proposed `v0` return, `v0`–`v7` argument, and `v8`–`v15` callee-saved convention accepted?
- Which mask-conversion instructions exist, and how do they populate or truncate bits?
- Can zero-width masks exist, and if so what does `br.all` mean for one?
- Which shift opcodes belong in v0.1 given that shift behavior is specified but shifts are absent from the opcode list?
- Is `mul.i64` intentionally omitted despite the general wrapping-multiplication rule?
- What are the alignment, duplicate/unknown-section, ordering, count, reserved-byte, and forward-compatibility rules?
- How are function and import signatures, return status, and host-call payloads represented?
- How is import fuel charged, and may hosts reject or pre-charge expected work?
- Which details accompany a trap, and how are host exceptions redacted?
- What is the maximum call depth, and how does an empty-stack `ret` trap?
- Does `halt` return `v0`, a structured status, or expose complete VM state to the embedding API?
- Is self-modifying code impossible by construction because code is immutable and separate from linear memory?
