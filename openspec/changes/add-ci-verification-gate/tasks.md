## 1. Dependency bump

- [x] 1.1 `cargo update --manifest-path client/Cargo.toml -p crossbeam-epoch`
- [x] 1.2 Verify: `grep -A2 'name = "crossbeam-epoch"' client/Cargo.lock` shows `version = "0.9.20"`
- [x] 1.3 Verify: `cargo test --manifest-path client/Cargo.toml` → all pass

## 2. CI workflow

- [x] 2.1 Create `.github/workflows/ci.yml` with `client` job (checkout, `dtolnay/rust-toolchain@stable`, `cargo test --manifest-path client/Cargo.toml`, non-blocking `cargo clippy --manifest-path client/Cargo.toml --all-targets`)
- [x] 2.2 Add `server` job (checkout, `actions/setup-dotnet@v4` with `10.0.x`, `dotnet build server/NebuCtx.slnx -p:AllowMissingPrunePackageData=true`, `dotnet test server/NebuCtx.slnx -p:AllowMissingPrunePackageData=true`)
- [x] 2.3 Set triggers: `pull_request` (no branch filter) and `push: branches: [main]`
- [x] 2.4 Verify: YAML parses (`python3 -c "import yaml; yaml.safe_load(open('.github/workflows/ci.yml'))"`)

## 3. Optional (not required for done)

- [x] 3.1 (Optional, skipped) Add non-blocking `cargo audit` step to `client` job and/or `dotnet list package --vulnerable --include-transitive` to `server` job; note explicitly in the final summary if added

## 4. Full verification

- [x] 4.1 `dotnet build server/NebuCtx.slnx -p:AllowMissingPrunePackageData=true` → 0 errors, 0 warnings
- [x] 4.2 `dotnet test server/NebuCtx.slnx -p:AllowMissingPrunePackageData=true` → all pass
- [x] 4.3 `git diff --stat` confirms `.github/workflows/auto-release.yml` and `release.yml` are unmodified
- [x] 4.4 `git status` shows only `.github/workflows/ci.yml` (new) and `client/Cargo.lock` changed
