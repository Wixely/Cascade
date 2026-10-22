# Expand the source language to version 0.3

- Status: Accepted
- Date: 2026-10-22
- Decision owner: User

## Context

Language 0.2 supports full-register memory transfers but cannot express CVM1's
lane-addressed gather/scatter operations or combine predicate masks. Hosts would
still need to hand-build bytecode for sparse vector memory workloads.

The CVM1 i32 encoding adds a compiler constraint: sixteen i32 lane addresses
occupy an even-aligned consecutive pair of vector registers. Source expressions
must remain independent of that physical restriction.

## Decision

Add typed `gather_i32`, `gather_i64`, `scatter_i32`, and `scatter_i64` built-ins.
i32 operations accept two address vectors in lane order; the compiler inserts
copies to a temporary even-aligned register pair when necessary. i64 operations
accept one address vector. Values and masks must match the operation's lane type.

Add all/none mask constructors and `mask_not`, `mask_and`, `mask_or`, and
`mask_xor`. CVM1 has no predicate Boolean instructions, so combinators lower
through 0/1 vectors and comparisons without changing the bytecode architecture.

All memory validation, inactive-lane behavior, atomic failure, and overlapping
write order remain runtime semantics defined by the reference interpreter.

## Consequences

- Sparse memory kernels can now be written entirely in `.cascade` source.
- Physical i32 address-pair constraints do not leak into source register layout.
- Mask expressions remain strongly typed by lane width.
- Mask combinators add several instructions and temporary registers until the
  compiler gains an optimizing intermediate representation.

## Follow-up

- [x] Test i32 and i64 gather/scatter lowering and execution — Owner: Codex — Completed: 2026-10-22
- [x] Test every mask constructor and Boolean combinator — Owner: Codex — Completed: 2026-10-22
- [x] Add a runnable 0.3 sparse-memory source demo — Owner: Codex — Completed: 2026-10-22
- [x] Add local liveness-aware release, conservative dead-binding removal, and mask identities — Owner: Codex — Completed: 2026-10-22
- [x] Add a typed compiler IR for global value propagation and broader dead-code elimination — Owner: Codex — Completed: 2026-10-22
