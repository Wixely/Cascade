# Freeze the CVM1 module format

- Status: Accepted
- Date: 2026-10-20
- Decision owner: User

## Context

The flat `CVM0` prototype could encode instructions but not constants, function
boundaries, entry points, or declared host capabilities. Cascade needs a bounded,
canonical, forward-extensible format before the MVP is complete.

## Decision

`CVM1` version 1.0 is little-endian. It consists of a 32-byte header, a directory
of 24-byte records, and contiguous 8-byte-aligned sections in ascending section
type order. Duplicate types, gaps, overlaps, trailing bytes, unsupported flags,
and nonzero reserved fields are invalid.

### Header

| Offset | Size | Field |
| --- | --- | --- |
| 0 | 4 | ASCII `CVM1` |
| 4 | 1 | Major version `1` |
| 5 | 1 | Minor version `0` |
| 6 | 2 | Header size (`32`) |
| 8 | 4 | Flags; zero in v1.0 |
| 12 | 4 | Section count |
| 16 | 4 | Directory offset (`32`) |
| 20 | 4 | Directory byte length |
| 24 | 4 | Entry instruction index |
| 28 | 4 | Reserved; zero |

Each directory record contains a 32-bit type, 32-bit flags, 64-bit offset, and
64-bit length. Flag bit zero means the section is required; all other flag bits
are reserved. Unknown required sections are rejected. Unknown optional sections
are skipped, allowing additive extensions that do not change execution.

### Sections

| Type | Name | Required | Payload |
| --- | --- | --- | --- |
| 1 | Code | Yes | Count header and 16-byte instruction records |
| 2 | Constants | No | Count header and 64-byte vector constants |
| 3 | Functions | No | Count header and 16-byte range records |
| 4 | Imports | No | Count header and padded variable-length import records |

Every known section begins with a 32-bit item count and 32 reserved zero bits.
The code record layout is the instruction layout already specified in the plan.
A function record stores a start index and exclusive end index followed by eight
reserved zero bytes. Functions are ordered, nonempty, and nonoverlapping. When
function metadata exists, every `call` target must be a declared function start.

An import record contains byte lengths for namespace and name, ABI version,
fixed fuel surcharge, total padded record length, reserved fields, then UTF-8
text and zero padding. v1.0 accepts ABI version 1 and portable ASCII identifiers.
Duplicate namespace/name/ABI keys are invalid.

`load.constant` copies one constant into a destination vector register.
`host.call` references an import by zero-based index. Both indices are validated
before instantiation.

### Bounds

The reference implementation rejects files larger than 128 MiB before reading
their contents. It limits a module to 1,000,000 instructions, 65,536 constants,
65,536 functions, 1,024 imports, and 64 sections. Checked arithmetic precedes
allocation and slicing. These are implementation safety limits, not expanded
architectural address spaces.

## Consequences

- `CVM0` is replaced rather than supported as a compatibility format because it
  was explicitly provisional and never released.
- Canonical ordering makes byte-for-byte module output reproducible.
- Optional unknown sections can carry future nonsemantic metadata.
- Changes to known records or execution behavior require a new format version.

## Follow-up

- [x] Implement the CVM1 reader and writer — Owner: Codex — Completed: 2026-10-20
- [x] Implement constant loading and host-call execution — Owner: Codex — Completed: 2026-10-20
- [x] Add round-trip, malformed-directory, import, and fuel conformance cases — Owner: Codex — Completed: 2026-10-20
