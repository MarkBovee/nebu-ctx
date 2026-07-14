## Context

`.github/workflows/` today only contains `auto-release.yml` (tags releases on push to `main`, verifying version-sync locations only) and `release.yml` (builds/publishes after a tag exists). Neither runs tests or a build check. There is no `pull_request` trigger anywhere, so PRs get no automated feedback before merge.

## Goals / Non-Goals

**Goals:**
- Give every PR and push-to-main fast, automated build/test feedback for both stacks.
- Bundle the trivial, zero-risk `crossbeam-epoch` security bump alongside, since a CI-gate PR is a natural place for it.

**Non-Goals:**
- Fixing the 13 pre-existing `cargo clippy` warnings — out of scope, tracked separately; clippy runs non-blocking (`continue-on-error: true`) so it doesn't fail CI on landing.
- Modifying `auto-release.yml`/`release.yml` triggers or behavior — this is a separate, additive workflow.
- Enabling GitHub branch protection to require this check — a repository-settings change, not a code change; flagged to the operator as a follow-up.

## Decisions

- **Two independent jobs (`client`, `server`), not one combined job.** A Rust-only or server-only PR still gets fast, isolated, clearly-attributable feedback instead of waiting on an unrelated stack's build.
- **`dtolnay/rust-toolchain@stable`, `actions/setup-dotnet@v4` with `10.0.x`.** Matches `release.yml`'s existing Rust toolchain convention and the `Dockerfile`'s `dotnet/sdk:10.0` base image version — no new toolchain-pinning convention introduced.
- **Clippy is non-blocking.** Making it blocking today would fail CI immediately on 13 pre-existing style warnings unrelated to this change; a separate follow-up should triage those first, then flip clippy to blocking.
- **Lockfile-only dependency bump.** `cargo update -p crossbeam-epoch` resolves `RUSTSEC-2026-0204` with zero `Cargo.toml` changes (confirmed via `--dry-run`: exactly one package affected, `0.9.18` → `0.9.20`).

## Risks / Trade-offs

- [Risk] Without branch protection, this workflow only reports status — it doesn't block a bad merge. → Mitigation: explicitly flagged to the operator as a required follow-up (repository settings, not code).
- [Risk] A future advisory with no available fix could make a hard `cargo audit` gate block all merges indefinitely. → Mitigation: any such check is added non-blocking (`continue-on-error: true`), per scope.
