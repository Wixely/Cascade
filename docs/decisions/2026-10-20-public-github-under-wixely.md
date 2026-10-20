# Publish Cascade on GitHub under Wixely

- Status: Accepted
- Date: 2026-10-20
- Decision owner: User

## Context

Cascade was initialized locally while its disclosure model remained undecided. Repository hosting determines whether the source is private or intentionally released as open source, which commit identity applies, and which licensing default is relevant.

## Decision

Cascade will be published as a public GitHub repository under Wixely. Commits use the repository-local identity `Wixely <5593644+Wixely@users.noreply.github.com>`. The planned license is MIT, following the confirmed default for Wixely open-source projects.

## Consequences

- The full outgoing history and all files must pass the pre-push privacy review before publication.
- The GitHub repository, remote, and first push still need to be created deliberately.
- Moving Cascade to private GitLab, another owner, or another license requires a superseding decision.
- Public documentation must not contain private infrastructure, local filesystem, credential, or identifying context that is unnecessary for the project.

## Follow-up

- [ ] Create the public GitHub repository under Wixely — Owner: User — Due: TBD
- [x] Add the MIT license — Owner: Codex — Completed: 2026-10-20
- [ ] Review the exact outgoing commit range — Owner: TBD — Due: TBD
- [ ] Push the reviewed `main` branch and record the canonical repository URL — Owner: TBD — Due: TBD
