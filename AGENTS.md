# Instructions for agents

Cascade is currently a plan-only repository for a deterministic vector virtual machine.

## Before starting work

1. Read `README.md`, `docs/plan.md`, `docs/preferences.md`, and recent records under `docs/decisions/`.
2. Inspect the working tree and preserve unrelated or user-authored changes.
3. Keep facts, assumptions, decisions, risks, and open questions distinct.
4. Do not treat a proposal in the plan as frozen unless the plan or a decision record says it is accepted.

## Development rules

- Use C# on .NET 10. Use top-level statements for the CLI where practical.
- Keep the architectural representation independent of hardware intrinsics.
- Implement and test the reference interpreter before optimized backends.
- Treat deterministic behavior, validation, traps, fuel, and capability boundaries as architectural requirements.
- Prefer NativeAOT and a single self-contained executable when the working implementation supports them; verify with published builds before claiming support.
- Do not use Python, Node.js, or tooling that transitively requires either without explicit user permission.
- Use Windows PowerShell 5.1 for repository scripting.
- Treat Windows as the primary platform and Linux as secondary. Do not claim platform support until tested.
- Include repository-local VS Code build and debug configuration when runnable projects are added.
- Run relevant builds, tests, formatting, static checks, and published-executable smoke tests in proportion to each change.

## Project boundaries

- Do not add a remote, choose GitHub or GitLab, change visibility, or add a license without an explicit decision.
- Do not add compiler, assembler, JIT, AVX-512, or dynamic-memory-growth work to v0.1 unless the plan is updated deliberately.
- Host imports are explicit capabilities. Do not grant modules ambient filesystem, network, process, environment, clock, or randomness access.
- Preserve decision history by adding a dated record under `docs/decisions/` rather than silently rewriting an accepted decision.
- Use ISO dates and give every next action an owner or mark it `TBD`.

## Source control and privacy

- Never commit secrets or credentials.
- Before any push, inspect the complete outgoing range, metadata, filenames, text, binaries, and generated artifacts for sensitive or identifying information.
- If this becomes a Wixely GitHub repository, configure the repository-local commit identity to `Wixely <5593644+Wixely@users.noreply.github.com>` before creating outgoing commits.
- If this becomes a private local GitLab project, grant the `agents` group the Developer role and verify effective access.

## Completion handoff

State what was verified, list what remains, and identify the recommended next action. If nothing remains, say so explicitly and recommend the appropriate release or operational follow-up.
