# Project preferences

- Status: Confirmed defaults unless explicitly marked otherwise
- Source: User-maintained PLAN repository and Cascade specification
- Captured: 2026-10-20
- Last reviewed: 2026-10-20

These are the relevant working preferences copied into Cascade so the repository is usable on its own. A documented Cascade-specific requirement may override a global default.

## Application and implementation

### C# and .NET 10

- Status: Confirmed
- Preference: Use C# on .NET 10 for the initial implementation.
- Notes: Use top-level statements for the CLI where practical. Class libraries use conventional declarations.

### Correctness-first backend

- Status: Confirmed for Cascade
- Preference: Implement a hardware-independent reference interpreter before the compiler, JIT, or AVX-512 backend.
- Notes: The reference backend defines observable behavior for all optimized implementations.

### Native publishing

- Status: Confirmed default
- Preference: Prefer NativeAOT and a single self-contained executable when dependencies and runtime behavior support them.
- Notes: Run an early feasibility probe and published-executable tests. Document concrete blockers rather than assuming compatibility.

### Testing

- Status: Confirmed
- Preference: Add and run relevant tests as soon as functionality exists.
- Notes: Cascade specifically requires conformance, malformed-input, determinism, atomicity, fuel, stack, and differential-backend tests.

### Tooling restrictions

- Status: Confirmed
- Preference: Do not use Python or Node.js for development, testing, builds, or transitive tooling without explicit user permission.
- Notes: Use Windows PowerShell 5.1 for scripting and keep scripts compatible with it.

### Debugging

- Status: Confirmed
- Preference: Provide working repository-local VS Code build and debug configuration for runnable projects.
- Notes: Keep it aligned with normal commands. It is intentionally deferred while this repository is plan-only.

## Platforms and packaging

### Platform priority

- Status: Confirmed
- Preference: Treat Windows as primary and Linux as secondary; macOS support is not required unless explicitly requested and verified.

### Support claims

- Status: Confirmed
- Preference: Do not claim platform, NativeAOT, packaging, or execution-backend support that has not been tested.

## Source control

### Hosting boundary

- Status: Confirmed for Cascade
- Preference: Publish Cascade as an open-source GitHub repository under Wixely.
- Notes: Confirmed by the user on 2026-10-20. Public repository and v0.1.0 release published on 2026-10-22 at https://github.com/Wixely/Cascade.

### GitHub identity, license, and release defaults

- Status: Confirmed for Cascade
- Preference: Use the repository-local identity `Wixely <5593644+Wixely@users.noreply.github.com>` and publish under the MIT license.
- Notes: Review the exact outgoing range before every push. GitHub Actions uploads must set purpose-specific short `retention-days`; durable files belong in GitHub Releases. Release archives should omit PDBs and other files users do not need, and releases must include meaningful notes.

## Privacy and completion

### Pre-push review

- Status: Confirmed
- Preference: Before pushing, review every outgoing commit and object for credentials, personal or machine-identifying data, internal endpoints, local paths, logs, generated output, and binary metadata.
- Notes: Scrub unnecessary information and repeat the review. Stop for user direction if safe removal would damage required information or rewrite shared history.

### Handoffs

- Status: Confirmed
- Preference: Every implementation handoff must say what remains and identify a concrete recommended next action with an owner or `TBD`.
