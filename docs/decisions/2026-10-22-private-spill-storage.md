# Add private spill storage in CVM1 1.1

- Date: 2026-10-22
- Status: Accepted following user approval of the spill-storage proposal
- Decision owner: User (extension approval); Codex (implementation details)

Unextended modules remain CVM1 1.0 with identical bytes. Modules declaring private
spill slots use minor version 1 and required section type 5. Its 16-byte payload
is count=1, reserved=0, vector-slot count, mask-slot count (four little-endian
uint32 fields). At least one count is nonzero; each is bounded to 65,536.

Add vector spill store/load and mask spill store/load at opcodes 0x0080–0x0083.
The immediate is the validated slot index. Vector store reads SourceA, vector
load writes Destination; mask store reads Mask, mask load writes Destination.
Unused fields are zero. Each costs one ordinary instruction of fuel.

Slots belong privately to one instance. A vector slot holds 64 bytes; a mask
slot holds bits and width (accounted as 16 bytes). Host options limit aggregate
spill allocation separately from linear memory, before allocation. Slots start
zeroed; mask width starts at 64. Restart clears slots but preserves ordinary
linear memory and vector registers. Imports receive no slot access.

Low-level calls share the instance's slots: every slot is caller-clobbered,
with no implicit save/restore or per-call allocation. Low-level authors must
coordinate slot usage; recursive callees do not gain private spill frames.
The source compiler inlines helpers and therefore uses one static slot plan.

Lower into integer-indexed virtual instructions. Keep the legacy direct mapping
when register limits are not exceeded. For high-pressure kernels, reserve a
small fixed scratch bank and assign overflow virtual values deterministic private
slots; insert explicit reloads/stores, preserving address-pair alignment and
evaluated snapshots. Report virtual pressure, slot counts and spill traffic.
This conservative policy favors correctness over optimal spill cost; future
allocation policies must preserve the same source and storage contract.

Owner: Codex for implementation and acceptance tests. Review date: 2026-10-22.

## Acceptance evidence

- 24 compiler cases and 33 conformance cases pass on Windows/Linux managed and
  published NativeAOT executables, including high-pressure mixed-width kernels.
- Tests cover 300 live vectors (virtual indices beyond byte range), mask-only
  pressure, mixed pressure with helper writes and saved loads, inactive gather
  lanes, address pairs, scalar expectations and deterministic serialization.
- Runtime tests cover every mask width, exact fuel boundaries, host payload
  limits, malformed counts/flags/versioning, shared caller-clobbered slots,
  instance isolation and restart/batch clearing.
- Existing 1.0 golden fixtures remain unchanged. Optional type-5 payloads in
  1.0 retain the historical skip behavior. An older native CLI explicitly
  rejects the new 1.1 stress module.
- The source demo uses 14 vector and six mask slots (992 payload bytes), with
  zero user linear memory. The packaged API round-trip smoke test includes 1.1.

Implementation is complete. Further spill-cost optimization is optional and
should use measured application workloads — Owner: TBD — Due: TBD.
