# Add typed value-numbered compiler IR

- Status: Accepted
- Date: 2026-10-22
- Decision owner: User

## Context

Local last-use release and mask identities reduced register pressure and cut the
0.3 gather demo from 54 to 36 instructions. They could not see that separately
named masks represented the same value or were complements, so entire chains of
provably redundant mask work remained.

Compiler optimization must preserve Cascade's eager expression evaluation.
Removing an unused load, gather, helper call, or unselected expression could
otherwise erase an observable trap or effect.

## Decision

Build a typed, value-numbered expression graph after source validation. Track
immutable aliases, all/none masks, and complement relationships by value number.
Use those facts for global mask simplification and known-condition selection.
Run backwards liveness over the optimized statements to remove transitively dead
bindings only when their complete expression trees are proven pure.

Validate the optimized program again, then pass it to the existing deterministic
CVM1 lowerer. Report instruction count plus peak vector-register and mask-register
allocation on every successful compilation. Do not change the source-language
version or CVM1 format because the optimization is not observable program state.

## Consequences

- The gather demo is 11 instructions, down from 36 after local optimization and
  54 before optimization, with a measured peak of six vector and one mask
  register.
- A redundant mask chain becomes one `halt`, with one input register and no mask
  registers allocated.
- Potentially trapping unused bindings and unselected operands remain eagerly
  evaluated, with regression coverage.
- The IR does not yet provide common-subexpression elimination, instruction
  scheduling, or spilling.

## Follow-up

- [x] Publish allocation metrics through the compiler API, CLI, and demos — Owner: Codex — Completed: 2026-10-22
- [x] Verify global mask propagation, transitive DCE, and trap preservation — Owner: Codex — Completed: 2026-10-22
- [x] Add optimizer differential and generated-program coverage — Owner: Codex — Completed: 2026-10-22

The subsequent [verification and lifetime decision](2026-10-22-compiler-verification-and-lifetimes.md)
records review fixes and scoped common-expression reuse added after this initial milestone.
