# Verify compiler transformations and preserve value lifetimes

- Status: Accepted
- Date: 2026-10-22
- Decision owner: Codex

## Evidence

Review found reproducible miscompilations in saved mask restoration, immutable
snapshots of mutable variables, and nested arithmetic sharing a last-use
register. Helper names beginning with a mask constructor prefix were also
misclassified, and folding a constant selection could lose a literal's lane
type. Dedicated tests reproduced each failure before its corresponding fix.

## Decision

Track outstanding expression borrows before transferring or freeing binding
storage. Snapshot borrowed values when creating a new immutable binding; mask
snapshots use the existing same-width mask conversion. Restore a complement only
from an available immutable mask or adjacent syntactic negations, never by
replaying a previously evaluated expression. Match builtin names exactly and
retain resolved literal types in the internal representation.

Analyze reads once per lexical block, including repeated free-variable reads.
Bound source size, nesting and expanded visits so finite source cannot request
unbounded compiler work through empty repeated blocks.

Expose an optimizer bypass in the API and CLI. Test optimized and bypassed
compilation against independent scalar expectations, on both available VM
backends. Compare source outputs, memory and trap behavior; do not require
compiler variants to have identical scratch registers, bytecode locations or
fuel consumption. Backends still require full-state equivalence for identical
bytecode.

After this coverage, reuse pure common expressions only through available
immutable bindings. Invalidate mutable value identities at assignments and
repeat boundaries. Loads and helper calls remain effectful and are not commoned.

## Verification and follow-up

- 640 deterministic generated kernels cover i32/i64 arithmetic, masks, aliases,
  common expressions, functions, mutation, and repetition with boundary inputs.
- 32 configurations cover memory, eager helper effects, snapshots, and traps.
- Failures include the fixed random seed and generated source for reproduction.
- [ ] Evaluate register spilling only with a separate semantics and memory-isolation design — Owner: TBD — Due: TBD
