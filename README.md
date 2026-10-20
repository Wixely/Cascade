# Cascade

Cascade is a planned deterministic, portable vector virtual machine with 32 512-bit general-purpose registers, predicate masks, byte-addressable linear memory, and a versioned `.cvm` module format.

The first implementation will use C# and .NET 10. It will establish correctness through a reference interpreter before adding a compiler, JIT, or AVX-512 backend.

## Status

- Phase: Planning
- Implementation: Not started
- Repository host: Local only; GitHub versus private GitLab is undecided
- License: Undecided
- Plan captured: 2026-10-20
- Last reviewed: 2026-10-20

## Start here

- [Implementation plan](docs/plan.md)
- [Project preferences](docs/preferences.md)
- [Decision records](docs/decisions/README.md)
- [Agent instructions](AGENTS.md)

The supplied source specification ended partway through a draft `VmState.Memory` declaration. The plan captures all complete architectural material and does not infer the missing implementation text.

## Current next action

Decide whether Cascade is intended to be public on GitHub or private on the local GitLab, then record the host, ownership, and license before implementation begins.

No VS Code debug configuration exists yet because the repository contains no runnable project. Creating and verifying it is part of the solution-bootstrap milestone.
