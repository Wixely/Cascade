# Optimize source lowering without changing CVM1

- Status: Accepted
- Date: 2026-10-22
- Decision owner: Codex

## Context

The initial source compiler retained every immutable local until its lexical
scope ended. Straight-line expression chains could therefore exhaust the 32
architectural vector registers even when each intermediate had only one use.
Version 0.3 mask combinators also used their general 0/1-vector lowering for
simple identities, making the sparse-memory demo 54 instructions long.

The recommended long-term design is a typed compiler intermediate
representation. Before committing to that larger architecture, local lowering
changes can test which optimizations produce useful, semantics-preserving gains.

## Decision

Count source-name uses across bounded repeats and transfer immutable storage to
the expression consuming its final use. This lets the existing deterministic
allocator reuse registers without changing source or bytecode semantics.

Remove an unused `let` only when its expression is locally proven pure. Loads,
gathers, and helper calls remain conservative because removing them could erase
a runtime trap or effect. Fold neutral mask constants, double negation, and
identical-mask Boolean operations before the general CVM1 lowering.

Do not introduce new VM opcodes or claim that these local analyses are a general
compiler IR.

## Consequences

- A tested 40-step expression chain compiles within the fixed register file.
- Unused pure arithmetic bindings generate no bytecode.
- The 0.3 gather demo falls from 54 to 36 instructions with identical output.
- Use counting saturates safely for deeply nested bounded repeats; the existing
  one-million-instruction limit remains authoritative.
- Global value propagation, generalized dead-code elimination, spilling, and a
  measured allocator remain future work.

## Follow-up

- [x] Add regression coverage for register reuse and local simplification — Owner: Codex — Completed: 2026-10-22
- [x] Introduce a typed compiler IR and measure it against the demos — Owner: Codex — Completed: 2026-10-22
