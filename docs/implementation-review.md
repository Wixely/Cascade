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

## Follow-up review: dispatch fusion and bounded diagnostics

The checkpoint above was committed as `8caa385`. Subsequent work added an
explicitly opt-in compare/select dispatch experiment, then evaluated compiled
Game of Life, gather and clamp kernels with instance setup included. The
experiment preserves architectural mask writes and two fuel charges, takes the
shortcut only with sufficient fuel, and does not fuse potentially trapping or
effectful operations. Full-state comparisons cover both widths, aliases,
mismatched masks, direct branch entry, restart and every nearby fuel boundary.

Compiled measurements do not justify default enablement: Game of Life and
gather have no eligible pairs, and clamp benefits only when setup is amortized
under the measured steady-state/native configurations. The option remains off.

This review also found excessive diagnostic accumulation for invalid modules.
Validation now stops with at most 128 diagnostics, including an explicit final
truncation notice. Regression cases cover instruction, function and import
errors and verify bounded embedding exception messages. Valid-module semantics
are unchanged. This is a diagnostic reporting contract refinement, not a
change to CVM1 validity or fuel rules.

Before the approved spill extension, follow-up verification passed `eng/verify.ps1` and
`eng/verify-linux.ps1 -RequireNativeAot`. That checkpoint contained 20 compiler
cases (including 640 generated kernels), 31 runtime cases (including 1,440
fusion comparisons), and five backend differential corpora. The updated runtime
suite also passed a freshly published Windows NativeAOT executable. The compiled
benchmark and its source assets published and ran on Windows and Linux NativeAOT.

## Completion audit

The user subsequently approved the private-spill extension. Implementation adds
virtual-index lowering, bounded private slot operations, CVM1 1.1 metadata and
a runnable source stress demo. Review preserved the original binary constructor
signature and corrected a compatibility edge: optional type-5 sections in 1.0
must still be skipped, not reinterpreted as 1.1 spill metadata. Unspilled
instances use shared empty arrays rather than allocating empty spill banks.

Managed spill acceptance tests now cover 300 live vectors, both vector lane
types, mask-only and mixed pressure, saved loads across helper writes, sparse
gather/scatter, all mask widths, slot allocation limits, restart/batch clearing,
instance isolation and caller-clobbered low-level calls. An older published CLI
was also exercised against the stress module and rejected version 1.1 explicitly.
Final spill verification passed on Windows and Linux, managed and NativeAOT:
24/24 compiler cases and 33/33 runtime conformance cases. Linux's verification
script also passed the differential corpus, native Game of Life scalar oracle,
and managed/native CLI compilation and execution of the spill sample. Windows
published compiler/runtime suites and native spill CLI execution passed. Debug
and Release builds, formatting, diff whitespace checks, and debug output-path
checks passed. The package smoke consumer also round-trips and executes a 1.1
module using the packaged embedding API.

| Requirement | Current evidence / disposition |
| --- | --- |
| Build the missing compiler verification and reuse work | Generated scalar oracles, scoped CSE, borrow regression tests and per-block liveness are implemented and passing |
| Reduce repeated invocation overhead | Packed API, state/fuel/failure tests and allocation benchmark implemented |
| Evaluate the next dispatch optimization | Opt-in fusion, full-state boundary tests, synthetic and compiled setup-inclusive benchmarks implemented; default remains off |
| Final code review and fixes | Findings above reproduced and fixed; added bounded validator reporting and metadata regression cases |
| Windows/Linux and native verification | Current managed scripts and published native suites passed as listed above |
| Preserve format and capabilities | Approved CVM1 1.1 extension; 1.0 golden bytes and unknown-optional-section behavior preserved, no ambient capabilities |
| Approved spill implementation | Private storage, explicit operations and virtual-register lowering implemented; acceptance coverage listed above |
| Publication | User-owned external action; no push performed |

## Remaining work

- Further performance changes need an application workload that demonstrates
  value beyond the measured prototypes — Owner: TBD — Due: TBD.
- Review the complete outgoing history before public repository creation and
  publication — Owner: User — Due: TBD.

Recommended next action: review and commit the verified follow-up, then complete
the separate publication/privacy review when ready — Owner: User for commit and
publication authorization.
