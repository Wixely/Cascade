# Isolated register spilling proposal

- Status: Approved by user; implemented details are in the [CVM1 1.1 decision](decisions/2026-10-22-private-spill-storage.md)
- Review date: 2026-10-22
- Owner: User for approval; Codex for implementation

## Problem and constraints

The following rationale describes the pre-extension implementation; it is kept
to explain why the approved change uses private storage and a format revision.

Source lowering still rejects programs exceeding 32 vector or eight mask
registers. Fixed linear memory is entirely module-visible, including through
host imports. Reserving hidden bytes there would alter addresses, memory limits,
observable contents, and trapping behavior. Recomputing spilled loads or helper
calls would also violate snapshots and ordered effects.

Consequently, spilling cannot be implemented transparently using current CVM1
instructions. Do not silently grow memory or grant a host capability to obtain
scratch space. Existing binaries and their fuel/trap semantics must remain valid.

## Recommended design for a future format revision

1. Declare bounded vector and mask slot counts in module metadata. Slots are
   private instance storage, not addresses in linear memory. Hosts can impose a
   separate allocation limit before instantiation; account for both storage
   kinds when reporting resource requirements.
2. Add explicit vector save/restore and mask save/restore operations. Vector
   slots contain 64 bytes; mask slots preserve both active bits and width. Each
   operation costs one unit of fuel, checked before modifying state.
3. Validate every slot index and metadata arithmetic before execution. Reject
   undeclared slots; no dynamic slot allocation or growth during execution.
4. Initialize slots to zero on instantiation and restart. Define the initial
   mask width explicitly in the revision. No host import receives slot access.
5. Specify the call convention before exposing these operations to low-level
   callers. The current source compiler inlines helpers, so it can use a
   module-wide static slot plan; that does not define a general call-frame ABI.
6. Introduce a virtual-register IR and deterministic allocation with explicit
   reloads. Spill only evaluated values, never recipes that replay memory or
   effects. Allocate even-aligned address pairs with the same policy as other
   constrained temporaries. Report slot count and reload/store count in metrics.
7. Version the format deliberately so old readers reject new modules clearly.
   Retain the existing serializer and execution rules for unextended CVM1 1.0.

## Alternatives rejected for the present implementation

- Hidden linear-memory reservation: aliases user memory and changes traps.
- Host callbacks for spills: introduce new capabilities and host-dependent cost.
- Unlimited extra physical registers: changes instruction encoding and architecture.
- Unrestricted expression rematerialization: can repeat loads and helper effects.

Pure constant rematerialization may eventually reduce pressure without new
storage, but it cannot solve all register-pressure cases and is not spilling.

## Required acceptance evidence

- Source kernels exceeding both register limits match scalar output and memory.
- Reloads preserve immutable snapshots across writes and helper effects.
- Exact slot-limit boundaries and overflow cases fail before instance effects.
- Masks retain width; all fuel boundaries expose deterministic partial state.
- Restart clears private storage without changing linear-memory persistence.
- Old CVM1 golden fixtures remain byte-stable; old readers reject the new version.
- Managed and NativeAOT execution agree on Windows and Linux.

The user approved proceeding on 2026-10-22. The accepted decision specifies
CVM1 1.1, width-64 zero initial mask slots, and shared caller-clobbered slots
across low-level calls. The source compiler inlines helpers. The original
alternatives and acceptance criteria above remain the design rationale.
