# Compiler optimization and batch execution review

- Review date: 2026-10-22
- Owner: Codex
- Scope: typed compiler IR, lowering/lifetimes, packed execution, module immutability, CLI output handling, and verification tooling

## Findings fixed

1. Complement propagation could replay an earlier memory read after a store.
   Restoration now reuses a saved immutable value or removes adjacent complements.
2. Immutable bindings could alias mutable storage. Lowering now takes a snapshot.
3. Last-use register reuse could overwrite an operand still needed by its parent
   expression. Outstanding borrows now prevent ownership transfer and early reuse.
4. Helper names sharing a built-in prefix could take the wrong lowering path.
   Built-ins now use exact names.
5. Collapsing a constant selection could lose a literal's inferred lane type.
   The typed IR preserves that context when rewriting syntax.
6. Deep source expressions and repeated empty blocks lacked practical expansion
   limits. Parsing and lowering now bound depth, source size, and expanded work.
7. Read-only module interfaces exposed mutable arrays through casts. Defensive
   copies now have read-only wrappers; null constant/import entries are rejected.
8. CLI compilation could overwrite its source or truncate an existing output.
   Same-path output is rejected and successful serialization replaces output
   through a temporary sibling file.

Each correctness finding has regression coverage. Common-expression reuse is
limited to pure expressions with available immutable bindings; mutable values
are invalidated on assignment and across repeat boundaries. Loads and helper
calls are not treated as pure. Function-entry validation uses a precomputed set.

## Verification

`eng/verify.ps1` passes the Release build, formatting, compiler tests, runtime
conformance, backend differential corpus, demos, scalar Game of Life oracle,
packed-batch benchmark checks, CLI preservation checks, and isolated package
consumer smoke test.

The compiler suite includes 640 fixed-seed generated kernels and 32 memory,
effect, and trap configurations, comparing optimized/unoptimized compilation
with independent expected results on available execution backends. Batch tests
cover restart equivalence, preflight rejection without effects, partial failure,
per-invocation fuel, persistent memory, and import charging.

Linux x64 managed verification passed all three suites (20 compiler cases,
29 conformance cases, and five backend differential corpora). Linux NativeAOT
CLI and Game of Life checks passed; both full native compiler and conformance
suites passed after fixing the test project's explicit Core reference and
canonicalizing linked repository paths in the WSL verification script.

Windows x64 NativeAOT publishing and execution also passed the full 20-case
compiler and 29-case runtime suites, plus CLI compilation and execution of the
gather sample. Game of Life retained its committed bytecode and scalar-checked
state hash. Native timings are machine-dependent; Linux's baseline native SIMD
run was not faster than reference execution, so no universal speedup is claimed.

This is a focused correctness review, not a claim of exhaustive proof or a
repository-wide security audit.

## Remaining work

- Evaluate instruction fusion with measured workloads and an explicit fuel/trap
  contract before changing dispatch — Owner: TBD — Due: TBD.
- Design isolated spill storage before allowing programs to exceed physical
  register capacity — Owner: TBD — Due: TBD.
- Review the complete outgoing history before public repository creation and
  publication — Owner: User — Due: TBD.

Recommended next action: review and commit this implementation checkpoint,
then measure a narrowly scoped instruction-fusion prototype — Owner: User for
commit authorization; TBD for the prototype.
