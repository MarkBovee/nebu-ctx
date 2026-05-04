# Binstall Client Install Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make `cargo binstall nebu-ctx` the primary Rust install path for the client, backed by published release binaries so Windows users do not need local build tools.

**Architecture:** Keep the crate published on crates.io, but shift the end-user install flow to prebuilt artifacts. Update the release workflow to publish `cargo-binstall`-compatible client assets, then update docs and smoke coverage so the documented install path is validated against the published binary route rather than local compilation.

**Tech Stack:** GitHub Actions, Rust/Cargo release assets, cargo-binstall, Markdown docs, Rust smoke tests, shell smoke scripts.

---

## File Map

- Modify: `.github/workflows/release.yml`
  Controls release artifacts and GitHub release asset naming. This is the main place where `cargo-binstall` compatibility must be established.
- Modify: `README.md`
  Main product install docs. Needs to recommend `cargo binstall nebu-ctx` first and demote `cargo install` to fallback/dev usage.
- Modify: `client/README.md`
  Crate-level install docs shown to Rust users. Needs the same install-path shift.
- Modify: `client/tests/setup_ci_smoke.rs`
  Current smoke assertions are tied to the `rust-lld` story. Replace those assertions with coverage for the new documented install story.
- Modify: `tests/local-server-cli-test.sh`
  Current smoke path installs from local source. Decide whether to keep that as a dev smoke only or add a separate release-facing smoke check for prebuilt install behavior.
- Create or modify if needed: `tests/` release validation script(s)
  If the existing shell smoke script should stay source-based, add a separate narrow script for validating release asset expectations.

### Task 1: Update release assets for `cargo-binstall`

**Files:**
- Modify: `.github/workflows/release.yml`

- [ ] **Step 1: Write the failing release-asset expectation down in the workflow diff**

Add target metadata planning directly in the matrix so the workflow can emit platform-aware asset names instead of only `amd64` / `arm64` labels.

Use this structure as the intended end state in `.github/workflows/release.yml`:

```yaml
strategy:
  matrix:
    include:
      - target: x86_64-unknown-linux-gnu
        runner: ubuntu-latest
        artifact_suffix: x86_64-unknown-linux-gnu
        archive_name: nebu-ctx-x86_64-unknown-linux-gnu.tar.gz
      - target: aarch64-unknown-linux-gnu
        runner: ubuntu-latest
        artifact_suffix: aarch64-unknown-linux-gnu
        archive_name: nebu-ctx-aarch64-unknown-linux-gnu.tar.gz
      - target: x86_64-pc-windows-msvc
        runner: windows-latest
        artifact_suffix: x86_64-pc-windows-msvc
        archive_name: nebu-ctx-x86_64-pc-windows-msvc.zip
```

- [ ] **Step 2: Run a narrow YAML sanity check locally**

Run: `git diff -- .github/workflows/release.yml`
Expected: only the release workflow shows pending edits and the matrix includes a Windows target plus target-based artifact naming.

- [ ] **Step 3: Implement the workflow changes**

Adjust `.github/workflows/release.yml` so it:

```yaml
- builds a Windows client binary on `windows-latest`
- stages output into a target-specific folder
- archives Linux binaries as `.tar.gz`
- archives Windows binaries as `.zip`
- uploads release assets using target-based names
```

Concrete implementation shape:

```yaml
- name: Package artifact
  shell: bash
  run: |
    mkdir -p package
    case "${{ matrix.target }}" in
      x86_64-pc-windows-msvc)
        cp "client/target/${{ matrix.target }}/release/${{ env.CLIENT_BINARY_NAME }}.exe" package/
        (cd package && 7z a "../${{ matrix.archive_name }}" "${{ env.CLIENT_BINARY_NAME }}.exe")
        ;;
      *)
        cp "client/target/${{ matrix.target }}/release/${{ env.CLIENT_BINARY_NAME }}" package/
        tar -C package -czf "${{ matrix.archive_name }}" "${{ env.CLIENT_BINARY_NAME }}"
        ;;
    esac

- name: Upload artifact
  uses: actions/upload-artifact@v7
  with:
    name: ${{ matrix.artifact_suffix }}
    path: ${{ matrix.archive_name }}
```

And release upload shape:

```yaml
files: |
  artifacts/x86_64-unknown-linux-gnu/nebu-ctx-x86_64-unknown-linux-gnu.tar.gz
  artifacts/aarch64-unknown-linux-gnu/nebu-ctx-aarch64-unknown-linux-gnu.tar.gz
  artifacts/x86_64-pc-windows-msvc/nebu-ctx-x86_64-pc-windows-msvc.zip
```

- [ ] **Step 4: Validate the workflow syntax by inspection**

Run: `git diff -- .github/workflows/release.yml`
Expected: the workflow still has one build job and one release job, but now publishes target-specific archives including Windows.

- [ ] **Step 5: Commit**

```bash
git add .github/workflows/release.yml
git commit -m "build: publish binstall-compatible client artifacts"
```

### Task 2: Update user-facing install docs

**Files:**
- Modify: `README.md:260-268`
- Modify: `client/README.md:7-20`
- Test: `client/tests/setup_ci_smoke.rs`

- [ ] **Step 1: Write the failing doc assertions first**

Update `client/tests/setup_ci_smoke.rs` to assert the new install messaging instead of the old `rust-lld` wording.

Replace the current docs-focused test intent with assertions like:

```rust
#[test]
fn client_readme_promotes_binstall_as_primary_install_path() {
    let readme = std::fs::read_to_string(std::path::Path::new(env!("CARGO_MANIFEST_DIR")).join("README.md"))
        .expect("read README");

    assert!(
        readme.contains("cargo binstall nebu-ctx") && readme.contains("cargo install nebu-ctx"),
        "client README should promote cargo-binstall first and keep cargo install as fallback"
    );
}
```

If needed, add a second test that reads the repo `README.md` via `Path::new(env!("CARGO_MANIFEST_DIR")).join("../README.md")` and checks the same install-story shift.

- [ ] **Step 2: Run the narrow test to verify it fails**

Run: `cargo test --manifest-path client/Cargo.toml setup_ci_smoke -- --nocapture`
Expected: FAIL because the current docs still say `cargo install nebu-ctx` first and mention Windows linker workarounds.

- [ ] **Step 3: Update the docs minimally**

In `README.md`, change the install section to this shape:

```md
### 1. Install the Rust client

```bash
cargo binstall nebu-ctx
```

This installs the prebuilt lightweight client binary. If you want or need a local source build instead, use `cargo install nebu-ctx`.
```

In `client/README.md`, change the install section to this shape:

```md
## Install

```bash
cargo binstall nebu-ctx
```

This is the recommended install path for Rust users because it downloads a published binary instead of compiling locally. For a local source build, use `cargo install nebu-ctx`.
```

Remove wording that claims the normal Windows install path is solved by `rust-lld`.

- [ ] **Step 4: Run the narrow docs smoke test again**

Run: `cargo test --manifest-path client/Cargo.toml setup_ci_smoke -- --nocapture`
Expected: PASS for the updated docs assertions.

- [ ] **Step 5: Commit**

```bash
git add README.md client/README.md client/tests/setup_ci_smoke.rs
git commit -m "docs: promote cargo-binstall for client installs"
```

### Task 3: Add release-facing install validation

**Files:**
- Modify: `tests/local-server-cli-test.sh`
- Create if needed: `tests/local-binstall-smoke.sh`

- [ ] **Step 1: Decide the boundary explicitly in the scripts**

Keep `tests/local-server-cli-test.sh` focused on source-based dev smoke if that script's purpose is local repo validation, and add a separate script for the published-binary install story.

The new script should target the release install contract, not the local source path.

- [ ] **Step 2: Add the failing script skeleton**

Create `tests/local-binstall-smoke.sh` with this shape:

```bash
#!/usr/bin/env bash
set -euo pipefail

command -v cargo-binstall >/dev/null 2>&1 || command -v cargo >/dev/null 2>&1

VERSION="${1:-0.7.7}"
ROOT="$(mktemp -d)"

cargo binstall nebu-ctx --version "$VERSION" --root "$ROOT" --no-confirm
test -x "$ROOT/bin/nebu-ctx" || test -x "$ROOT/bin/nebu-ctx.exe"
```

- [ ] **Step 3: Run shell syntax validation**

Run: `bash -n tests/local-binstall-smoke.sh`
Expected: no output.

- [ ] **Step 4: Integrate or document invocation**

If this script should participate in release validation, add a short comment near the install section of `tests/local-server-cli-test.sh` or in the release docs describing the split:

```bash
# local-server-cli-test.sh validates local source builds and runtime wiring.
# local-binstall-smoke.sh validates the published binary install path.
```

Do not repurpose the existing source-build smoke to pretend it validates `cargo-binstall`.

- [ ] **Step 5: Commit**

```bash
git add tests/local-server-cli-test.sh tests/local-binstall-smoke.sh
git commit -m "test: add binstall install smoke coverage"
```

### Task 4: Verify the full plan intent before merge

**Files:**
- Modify: any files from Tasks 1-3 if verification reveals mismatches

- [ ] **Step 1: Run the client smoke tests**

Run: `cargo test --manifest-path client/Cargo.toml setup_ci_smoke -- --nocapture`
Expected: PASS.

- [ ] **Step 2: Run shell syntax checks for smoke scripts**

Run: `bash -n tests/local-server-cli-test.sh && bash -n tests/local-binstall-smoke.sh`
Expected: no output.

- [ ] **Step 3: Review docs and workflow together**

Run: `git diff -- README.md client/README.md client/tests/setup_ci_smoke.rs .github/workflows/release.yml tests/local-server-cli-test.sh tests/local-binstall-smoke.sh`
Expected: the diff shows one consistent install story: docs recommend `cargo binstall`, release publishes matching assets, and tests validate the new contract.

- [ ] **Step 4: Final commit if Task 4 required follow-up fixes**

```bash
git add README.md client/README.md client/tests/setup_ci_smoke.rs .github/workflows/release.yml tests/local-server-cli-test.sh tests/local-binstall-smoke.sh
git commit -m "chore: finalize binstall client install flow"
```

- [ ] **Step 5: Record verification notes in the PR description or handoff**

Include these points verbatim in the handoff:

```md
- `cargo binstall nebu-ctx` is now the primary documented Rust install path.
- Release assets are target-named archives intended for cargo-binstall discovery.
- `cargo install nebu-ctx` remains a fallback/source-build path only.
- Smoke coverage now distinguishes source-build validation from published-binary install validation.
```

## Self-Review

- Spec coverage check: covered release assets, docs shift, fallback behavior, and verification.
- Placeholder scan: no `TODO` / `TBD` placeholders left in tasks.
- Type consistency: file names, commands, and asset names are consistent with the chosen `cargo-binstall` direction.
