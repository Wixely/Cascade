# Reuse VM instances for repeated kernel execution

- Status: Accepted
- Date: 2026-10-21
- Decision owner: User

## Context

The Game of Life workload invokes an 18-instruction vector kernel once for each
16-cell batch. Constructing a VM instance for every batch repeatedly allocated
register, predicate, memory, and call-stack state. A loaded `CascadeProgram` was
also revalidating the same immutable module during each instantiation.

The host needs a low-overhead way to run the same kernel repeatedly while still
receiving fresh control state, fuel accounting, and predictable predicate state.

## Decision

`CascadeProgram` validates its module once and uses that fact when constructing
instances. Add `CascadeInstance.Restart` to reset the instruction pointer to the
module entry point, restore the configured fuel, clear predicate masks, and
clear the VM return stack.

Restart deliberately preserves vector registers and linear memory. This makes
the operation suitable for repeated kernels and stateful computations without
copying data. Hosts that require zero-initialized data create a new instance.
Instances remain non-thread-safe, and host code must overwrite every input that
must not carry over from the preceding invocation.

## Consequences

- Repeated kernels avoid per-batch VM allocation and module validation.
- Fuel, predicates, instruction position, and call state start fresh each run.
- Register and memory persistence is an explicit embedding contract rather than
  an incidental interpreter behavior.
- The Game of Life demo reuses one instance for each backend and continues to
  verify its output against the scalar oracle.
- Short kernels still pay interpreter dispatch per instruction; batching more
  independent work is the next optimization to evaluate.

## Follow-up

- [x] Add conformance coverage for reference and hardware-SIMD restart behavior — Owner: Codex — Completed: 2026-10-21
- [x] Use instance restart in the Game of Life workload — Owner: Codex — Completed: 2026-10-21
- [ ] Design and benchmark batched kernel execution — Owner: TBD — Due: TBD
