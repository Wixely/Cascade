# Decision records

Store accepted architectural and project decisions here as `YYYY-MM-DD-short-title.md`.

Each record should contain:

- status and date;
- context and alternatives;
- the decision;
- consequences and compatibility impact;
- related plan sections and follow-up actions.

Supersede an older decision with a new record and cross-link both; do not erase decision history.

The initial decision queue is listed under [Open questions](../plan.md#open-questions).

## Accepted decisions

- [Publish Cascade on GitHub under Wixely](2026-10-20-public-github-under-wixely.md)
- [Freeze Cascade v0.1 executable semantics](2026-10-20-executable-semantics.md)
- [Freeze the CVM1 module format](2026-10-20-cvm1-module-format.md)
- [Add a portable hardware-SIMD execution backend](2026-10-21-portable-hardware-simd-backend.md)
- [Reuse VM instances for repeated kernel execution](2026-10-21-reusable-instance-execution.md)
- [Add a typed kernel source language and compiler](2026-10-21-typed-kernel-source-language.md)
- [Expand the source language to version 0.2](2026-10-21-source-language-0.2.md)
- [Expand the source language to version 0.3](2026-10-22-source-language-0.3.md)
- [Optimize source lowering without changing CVM1](2026-10-22-local-compiler-optimization.md)
- [Add typed value-numbered compiler IR](2026-10-22-typed-compiler-ir.md)
- [Verify compiler transformations and preserve value lifetimes](2026-10-22-compiler-verification-and-lifetimes.md)
- [Add sequential packed kernel batches](2026-10-22-packed-kernel-batches.md)
- [Evaluate opt-in compare/select dispatch fusion](2026-10-22-dispatch-fusion-prototype.md)
- [Add private spill storage in CVM1 1.1](2026-10-22-private-spill-storage.md)
