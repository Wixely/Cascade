# Evaluate opt-in compare/select dispatch fusion

- Date: 2026-10-22
- Status: Accepted experiment; disabled by default
- Owner: Codex

## Contract

Recognize adjacent same-width compare/select instructions using the compare's
destination mask. Execute both existing operations without returning through
the dispatch switch between them. Keep the compare's mask write, all vector
writes, and two units of fuel. Do not change CVM1 or compiler output.

Only take the shortcut when at least two fuel units remain. Otherwise execute
the original path so fuel exhaustion exposes the exact intermediate state and
instruction index. Entering the select directly still uses normal dispatch.
Never fuse memory, imports, calls, branches, or potentially failing selections
whose mask width is not established by the immediately preceding comparison.

Expose an opt-in VM option for measurement on either execution backend. The
reference backend with the option disabled remains the oracle. This experiment
reduces dispatch only; it does not eliminate mask materialization or combine
hardware operations. Default enablement requires broader performance evidence.

Recognize pairs once at instance construction and store one eligibility byte
per instruction for opted-in instances. The initial per-dispatch recognition
prototype regressed longer workloads and was replaced. This setup cost is not
included in reused-instance timings and matters for short-lived instances.

## Evaluation

- Compare full state and results across fuel boundaries, aliases, lane widths,
  branch entry, mismatched masks, and restart — Owner: Codex.
- Benchmark reused instances with fusion on/off and independent output checks;
  include a workload with no eligible pairs — Owner: Codex.
- Register spilling remains deferred pending isolated-storage semantics — Owner: TBD.

## Initial managed measurements

Windows x64, 2026-10-22, median of nine samples of 2,000 reused invocations:
precomputed fusion measured 1.10x on a short SIMD pair and 1.03x on 256 SIMD
pairs. Reference measured 1.04x and 1.03x respectively. These small synthetic
gains are not evidence to enable the option by default. All results were checked
against scalar expected outputs; timings vary by runtime and machine.

## Compiled workloads and setup amortization

`--fusion-compiled` compiles repository Game of Life and gather sources plus a
checked-in clamp source, then measures 4,096 calls with instance reuse of one
and 64. Compilation is excluded; construction, argument copies and execution
are included. Output is checked against independent scalar expectations, and
the gather case checks written memory. Alternating order and median-of-nine
samples reduce noise but do not remove runtime warm-up effects.

Game of Life (19 instructions) and gather (11) have no eligible adjacent pairs;
clamp has two pairs in six instructions. A Windows managed run with
`DOTNET_TieredCompilation=0` measured clamp SIMD at 1.08x for reuse 64 but 0.93x
for fresh instances. Windows NativeAOT measured 1.10x and 0.96x respectively.
Both zero-pair workloads paid setup overhead without a useful execution gain.
The default tiered run was less stable and showed no clamp gain; these results
reinforce the need to test the actual deployment runtime.

Decision: retain the explicit experimental option, disabled by default. Do not
extend fusion or schedule instructions merely to make synthetic numbers better.
Further optimization needs an application workload that justifies it — Owner:
TBD — Due: TBD. Setup amortization evaluation is complete for these three kernels.

## Verification results

Windows managed verification passed the compiler suite (20/20), runtime suite
(31/31, including the subsequent diagnostic-limit regression), five differential corpora, demos, CLI preservation checks and package
smoke test. The runtime suite includes 1,440 fusion comparisons covering full
register/mask/memory state, result/trap, instruction pointer and remaining fuel.
The same 31-case runtime suite passed published Windows x64 and Linux x64
NativeAOT executables. Formatting and diff whitespace checks passed. The compiled
benchmark was also published and executed on both platforms; Linux's quick run
is a correctness smoke check, not performance evidence.
