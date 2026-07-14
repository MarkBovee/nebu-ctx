## Why

This repository has zero CI verification for correctness. `.github/workflows/` contains only `auto-release.yml` (verifies version-sync locations, tags a release on every push to `main`) and `release.yml` (builds/publishes once a tag exists) — neither runs `cargo test`, `cargo clippy`, `dotnet build`, or `dotnet test`. Every push to `main` can trigger an automatic tag-and-release with no automated check that the code even builds. This is the highest-leverage DX gap found in the audit: every other correctness fix lands with more confidence once a CI gate exists to catch regressions automatically.

## What Changes

- Add a new `.github/workflows/ci.yml` with two independent jobs: `client` (`cargo test`, non-blocking `cargo clippy`) and `server` (`dotnet build`, `dotnet test`), triggered on every pull request and on push to `main`.
- Bump the `crossbeam-epoch` transitive dependency (`0.9.18` → `0.9.20`) to resolve `RUSTSEC-2026-0204`, a lockfile-only change with zero manifest edits (confirmed via dry-run).
- Does not modify `auto-release.yml`/`release.yml` triggers or behavior — this is a separate, additive verification workflow.

## Capabilities

### New Capabilities
- `ci-verification`: defines the requirement that the repository's client and server code is automatically built and tested on every pull request and push to `main`, independent of the release pipeline.

### Modified Capabilities

## Impact

- **Code**: new `.github/workflows/ci.yml`; `client/Cargo.lock` regenerated (dependency bump only, no `Cargo.toml` change).
- **Out of scope**: fixing the 13 pre-existing `cargo clippy` style warnings (clippy step is non-blocking); adding `cargo audit`/`dotnet list --vulnerable` as a hard gate (optional, non-blocking if added); enabling GitHub branch protection to require this check (a repository-settings change, not a code change — flagged as a follow-up for the operator).
- Full technical detail (exact workflow YAML, toolchain versions, dry-run output) already captured in `plans/003-add-ci-verification-gate.md` — this proposal is the OpenSpec-tracked counterpart of that plan.
