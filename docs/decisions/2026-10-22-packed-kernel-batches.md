# Add sequential packed kernel batches

- Status: Accepted
- Date: 2026-10-22
- Decision owner: Codex

## Decision

Add `CascadeInstance.ExecuteBatch` for packed 64-byte argument vectors and one
returned v0 vector per invocation. Preserve the existing `Restart` contract:
fresh control state and fuel per invocation, persistent vector data and memory,
and unchanged import bindings. Validate sizes, aliasing, and aggregate fuel
capacity before starting. Stop at the first trap, retain completed outputs, and
leave the failing output and subsequent slots untouched.

This is an additive embedding API. It introduces neither bytecode changes nor
cross-invocation transactions, parallelism, or ambient capabilities.

## Measurements

A Windows managed run on 2026-10-22 used 4,096 three-instruction kernels and the
median of nine samples, including argument/output copies. SIMD measured 4.118 ms
with new instances, 0.965 ms with manual restarts, and 0.908 ms with packed
batching. Corresponding allocated bytes were 18,284,544, 163,840, and 163,880.
These figures are local observations, not performance guarantees. The dominant
gain is instance reuse. A subsequent internal value-type result path reduced
successful packed-batch allocation from 163,880 bytes to 40 bytes per sample.
With that path, SIMD measured 4.314 ms for new instances, 1.036 ms for manual
restart and 0.936 ms for batches. Ordinary interpreter dispatch remains.

The benchmark checks each output against scalar arithmetic outside the timed
region. Conformance compares manual and batch results, verifies partial failure,
fuel exhaustion, buffer preflight, persistent memory, and host-import charging.

## Follow-up

- [x] Measure and implement a non-allocating internal success-result path — Owner: Codex — Completed: 2026-10-22
- [ ] Measure instruction fusion before selecting architecture-specific backend work — Owner: TBD — Due: TBD
