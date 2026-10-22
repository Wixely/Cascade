# Expand the source language to version 0.2

- Status: Accepted
- Date: 2026-10-21
- Decision owner: User

## Context

Source-language 0.1 proved the typed source-to-CVM1 path with expression-only
kernels. Useful kernels also need reusable logic, bounded state transitions,
and controlled access to the VM's fixed linear memory. These features must not
weaken deterministic execution, fuel bounds, or memory traps.

## Decision

Version the documented source language as 0.2 and add:

- typed helper functions, deterministically inlined with recursion rejected;
- mutable vector locals declared with `var` and explicitly assigned;
- `repeat N` blocks where `N` is a compile-time integer from 1 through 1024;
- `load_i32`, `load_i64`, and `store` for full 64-byte memory transfers using
  lane 0 of a `vector<i64>` address;
- a 1,000,000-instruction ceiling on compiler output.

Repeat blocks are unrolled. They do not add a hidden loop construct or bypass
VM fuel accounting. Memory operations lower directly to existing CVM1
instructions and retain the runtime's bounds and trap-before-effect semantics.
Helper functions inline rather than using the VM call ABI, keeping source
functions independent from the v0.1 bytecode calling convention.

## Consequences

- Source can express fixed-round transforms and stateful memory kernels.
- Every source loop has a statically visible finite upper bound.
- Large repeat counts increase bytecode size; the instruction ceiling bounds it.
- Inlining can duplicate function bodies and does not support recursion.
- Gather/scatter, data-dependent branches, imports, and register spilling remain
  future language work.

## Follow-up

- [x] Add execution and diagnostic tests for all 0.2 constructs — Owner: Codex — Completed: 2026-10-21
- [x] Add a runnable 0.2 source demo — Owner: Codex — Completed: 2026-10-21
- [x] Design typed gather/scatter and mask combinators — Owner: Codex — Completed: 2026-10-22
- [ ] Add liveness-based register reuse and spilling — Owner: TBD — Due: TBD
