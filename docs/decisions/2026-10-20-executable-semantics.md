# Freeze Cascade v0.1 executable semantics

- Status: Accepted
- Date: 2026-10-20
- Decision owner: User

## Context

The runnable MVP exposed unresolved behavior around predicate conversion,
inactive memory lanes, calls, imports, traps, completion, and resource limits.
Those choices must be stable before the portable `CVM1` container can be
designed. The user directed Codex to complete the recommended semantic-freeze
work on 2026-10-20.

## Decision

### Predicates

Predicate widths are exactly 8, 16, 32, or 64 bits; zero-width predicates do not
exist. Comparisons establish width. Four explicit instructions convert masks:
`mask.to8`, `mask.to16`, `mask.to32`, and `mask.to64` (represented in code as
`ConvertMask8` through `ConvertMask64`). Conversion copies low bits, truncates
bits above the destination width, and fills newly active high bits with zero.
Consequently, widening an all-true narrow mask does not produce an all-true wide
mask. `br.all` requires every bit in the stored active width to be one.

### Memory

Memory is fixed after instantiation. The accepted defaults are 64 KiB, a 16 MiB
host maximum, and no v0.1 growth instruction. Inactive gather lanes preserve
their destination lanes. Gather and scatter validate every active address before
any effect. Scatter commits active lanes in ascending order, so the
highest-numbered overlapping lane wins.

Code and linear memory are separate. Modules cannot modify code, so
self-modifying code is impossible by construction.

### Calls and completion

`call` pushes only the next instruction index and jumps directly to its target.
`ret` pops that index. The default maximum call depth is 1,024. Overflow traps
with `StackOverflow`; `ret` on an empty stack traps with `StackUnderflow`.

The v0.1 ABI uses `v0` for a return value, `v0`-`v7` for arguments, `v8`-`v15`
as callee-saved registers, and `v16`-`v31` as caller-saved registers. This is a
program ABI: the runtime does not copy or restore registers around ordinary VM
calls.

`halt` completes normally. The embedding API retains the complete final VM
state; a caller may interpret `v0` according to its own entry-point contract.

### Fuel and imports

Every instruction costs one fuel unit, whether or not a branch is taken. An
import call additionally has a nonzero fixed surcharge declared in the module.
The runtime validates and precharges that complete cost before invoking host
code, so insufficient fuel causes `FuelExhausted` with no host effect. A host may
reject work that cannot fit its declared bound; it cannot charge an
input-dependent amount after performing work.

Imports are explicit declarations identified by namespace, name, and ABI
version. Instantiation requires an exact host binding; no ambient filesystem,
network, process, environment, clock, or randomness authority exists. The v0.1
import ABI exchanges vector arguments in `v0`-`v7`, returns in `v0`, may mutate
caller-saved registers and linear memory, and must preserve `v8`-`v15`.

Binding or signature failure is `InvalidHostImport`. A host-reported failure or
exception is `HostCallFailed`. Host exception types, messages, stacks, and other
host details are never copied into a program-visible trap.

Import declarations and calls are not added to provisional `CVM0`; they require
the sectioned `CVM1` representation.

That follow-up was completed by [the CVM1 module-format decision](2026-10-20-cvm1-module-format.md).

### Traps and instruction scope

Stable trap data consists of a trap code and instruction index. Human-readable
diagnostic text is for hosts and tools and is not a compatibility contract.
Trap handling does not terminate the host process.

Shift instructions and `mul.i64` are omitted from v0.1 because they were not in
the proposed opcode list. Adding them requires a later versioned decision.

`CVM1` freezes only after these semantics have conformance coverage. Its section
layout and forward-compatibility rules remain a separate design decision.

## Consequences

- Optimized backends must preserve mask conversion, merge, atomicity, scatter
  ordering, fuel, call-stack, and trap behavior exactly.
- Widening masks is deliberately not a replication operation.
- Import implementations must expose a bounded deterministic cost and preserve
  callee-saved registers.
- Existing MVP behavior changes only for empty `ret`, which now reports the more
  precise `StackUnderflow` code.
- At the time of this decision, `CVM0` remained provisional and could not
  represent imports. It has since been replaced by `CVM1`.

## Follow-up

- [x] Implement explicit mask-width conversions — Owner: Codex — Completed: 2026-10-20
- [x] Add conformance cases for conversion, gather merge, and stack underflow — Owner: Codex — Completed: 2026-10-20
- [x] Specify the `CVM1` section directory and import records — Owner: Codex — Completed: 2026-10-20
- [x] Implement import binding and precharge after `CVM1` declarations exist — Owner: Codex — Completed: 2026-10-20
