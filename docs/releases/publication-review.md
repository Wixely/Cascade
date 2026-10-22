# Initial public release review

- Review date: 2026-10-22
- Owner: Codex
- Authorization: User requested public GitHub publication, commit, tag, and useful
  libraries; selected GitHub release packages rather than NuGet.org publication.
- Destination: https://github.com/Wixely/Cascade
- Scope: Full `main` ancestry from the root commit, plus the v0.1.0 release commit
  and annotated tag. No other local refs or ignored artifacts are intended for upload.

## History review

Reviewed commit subjects, author/committer identities, filenames, file modes,
historical text content, endpoint/address patterns, and credential patterns.
All 13 pre-release commits use the approved Wixely public noreply identity.
The pre-release history contains 235 unique blobs. Historical binaries consist
of five CVM1 blobs: three Life revisions and two test fixtures. Their complete
bytes were inspected: instruction records, numeric constants, and the synthetic
fixture import; no document/image metadata or embedded personal data.

No credentials, private endpoints, personal filesystem paths, logs, screenshots,
or user-specific machine identifiers were found in this review. The documented
standard Visual Studio installation path and public project identity are
intentional. No history repair was required. This disclosure review is not a
security audit or proof that the runtime is safe against hostile inputs.

## Release process

Only the three versioned NuGet packages and their SHA-256 checksum manifest are
release assets. Package entries and embedded assembly strings are inspected
before upload; symbols, build logs, caches, and local verification artifacts
are excluded. Source remains available through GitHub's tagged source archives.

Managed verification and isolated package-consumer tests passed. Runtime changes
were already verified on Windows/Linux x64 managed and NativeAOT; this release
adds compiler packaging, package metadata, and consumer documentation, not new
VM semantics.

## Publication verification

Public visibility, default branch `main`, annotated `v0.1.0` tag, and the published
release were verified on 2026-10-22. The tag resolves to release commit `30b4a7d`.
All three packages were downloaded back from GitHub and match the reviewed
SHA-256 hashes. The exact release packages also restored into an isolated
consumer and ran the quickstart, producing the odd integers 1 through 31.
The follow-up publication-status commit changes only documentation and does not
move the release tag or change its artifacts.

No publication work remains. Recommended next action: use the quickstart in a
consumer application — Owner: User. NuGet.org publication is out of scope.
