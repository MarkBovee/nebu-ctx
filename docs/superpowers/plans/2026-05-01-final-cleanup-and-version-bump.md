# Final Cleanup And Version Bump Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Remove the last relevant internal `cloud` terminology from the Rust client, bump the project version to `0.7.1`, and commit the completed cleanup.

**Architecture:** Keep the final pass minimal and compatibility-aware except where the user explicitly requested a rename. Rename the persistent client config section from `cloud` to `server`, update the remaining internal wording leaks, then sync all three required version locations and `Cargo.lock` before verification and commit.

**Tech Stack:** Rust, .NET, Cargo, Markdown, git

---

### Task 1: Remove remaining relevant internal `cloud` terminology

**Files:**
- Modify: `client/src/core/config.rs`
- Modify: `client/src/core/pop_pruning.rs`
- Modify: `client/src/core/mode_predictor.rs`
- Modify: `client/src/bin/seed_observatory.rs`

- [ ] **Step 1: Rename the config section from `cloud` to `server`**

Update the config model in `client/src/core/config.rs` so the persisted config field becomes `server` and the type becomes `ServerConfig`.

- [ ] **Step 2: Update the remaining internal wording leaks**

Replace:

- `cloud-infra` with `server-infra`
- `cloud models` with `server models`
- `Zero data sent to cloud` with host/server wording that matches the current architecture

- [ ] **Step 3: Run the focused wording search**

Run: `rg -n "\bcloud\b" client/src --glob '*.rs'`

Expected: no remaining relevant matches in the touched Rust client sources.

### Task 2: Bump version to `0.7.1`

**Files:**
- Modify: `client/Cargo.toml`
- Modify: `homeassistant/config.yaml`
- Modify: `server/src/NebuCtx.Server.Core/ToolRegistry.cs`
- Modify: `client/Cargo.lock`

- [ ] **Step 1: Update the three required version locations**

Set all required version locations to `0.7.1`.

- [ ] **Step 2: Update the Rust lockfile**

Run: `cargo update --manifest-path client/Cargo.toml`

Expected: `client/Cargo.lock` reflects the new package version metadata.

### Task 3: Verify and commit

**Files:**
- Verify: client and server trees

- [ ] **Step 1: Run Rust client tests**

Run: `cargo test --manifest-path client/Cargo.toml`

Expected: PASS.

- [ ] **Step 2: Run .NET server tests**

Run: `dotnet test server/NebuCtx.slnx -p:AllowMissingPrunePackageData=true`

Expected: PASS.

- [ ] **Step 3: Create the commit**

Stage the touched files and create a non-amended commit that covers the final cleanup and version bump.
