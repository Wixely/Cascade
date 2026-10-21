# Add a typed kernel source language and compiler

- Status: Accepted
- Date: 2026-10-21
- Decision owner: User

## Context

The Game of Life demo originally constructed VM instructions in C# and
serialized them to `life-rule.cvm`. That proved the runtime and module format,
but it did not provide a source file whose language semantics a compiler could
validate and lower. The user expected a conventional source-to-bytecode flow.

The v0.1 VM plan excluded compiler work until reference semantics and optimized
execution were stable. Those prerequisites are complete, and the user
explicitly expanded the scope to include the compiler.

## Decision

Add `Cascade.Compiler` and version 0.1 of a typed `.cascade` kernel language.
Source contains one kernel with typed vector parameters, inferred immutable
locals, wrapping arithmetic, bitwise expressions, signed comparisons, masked
selection, and a typed return. Compilation produces a `CascadeModule`; the CLI
writes that module as deterministic `CVM1` bytecode.

Kernel parameters map to `v0` onward and the returned vector maps to `v0`.
Language rules are specified in `docs/language.md`. The compiler has no runtime
dependency, and compiler details do not enter the portable bytecode architecture.

Replace the Game of Life instruction builder with `life-rule.cascade` and
require its compiled bytes to match the committed `life-rule.cvm` artifact.

## Consequences

- Cascade now has a real source-to-bytecode workflow.
- Diagnostics identify syntax and type errors by line and column.
- The initial language cannot yet expose all CVM1 control-flow, memory,
  function, or import instructions.
- The compiler uses no dynamic code generation or third-party parser.
- Language evolution is versioned separately from the frozen CVM1 encoding.

## Follow-up

- [x] Compile and execute the Game of Life kernel from source — Owner: Codex — Completed: 2026-10-21
- [x] Add compiler semantic, diagnostic, determinism, and execution tests — Owner: Codex — Completed: 2026-10-21
- [x] Design typed memory access, functions, and bounded control flow — Owner: Codex — Completed: 2026-10-21
- [ ] Add optimization and register spilling after representative kernels require them — Owner: TBD — Due: TBD
