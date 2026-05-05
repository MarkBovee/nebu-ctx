# 0.8.0 Release Bump And Changelog Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Bump the repo to `0.8.0` and add full release notes that explain the breaking public MCP contract change.

**Architecture:** Keep the change narrowly scoped to release metadata and changelog text. Reuse the repo's existing three-version sync rule, then verify the specific client contract tests that justify the release notes.

**Tech Stack:** Cargo, .NET, Home Assistant addon config, Markdown changelog

---

## File Map

- Modify: `client/Cargo.toml`
- Modify: `homeassistant/config.yaml`
- Modify: `server/src/NebuCtx.Server.Core/ToolRegistry.cs`
- Modify: `CHANGELOG.md`
- Update generated lockfile: `Cargo.lock`

### Task 1: Bump the canonical version markers

**Files:**
- Modify: `client/Cargo.toml`
- Modify: `homeassistant/config.yaml`
- Modify: `server/src/NebuCtx.Server.Core/ToolRegistry.cs`

- [ ] Change all three canonical version markers from `0.7.10` to `0.8.0`.
- [ ] Re-read the three files and confirm they match exactly.

### Task 2: Write the 0.8.0 release notes

**Files:**
- Modify: `CHANGELOG.md`

- [ ] Add a new top `## 0.8.0` section.
- [ ] Describe the release as the public MCP surface simplification release.
- [ ] Include subsections for breaking changes, client and routing, docs and guidance, and upgrade notes.
- [ ] Keep the content technical and explicit about the public 5-tool contract.

### Task 3: Sync the lockfile

**Files:**
- Update: `Cargo.lock`

- [ ] Run `cargo update --manifest-path client/Cargo.toml`.
- [ ] Confirm `Cargo.lock` changed.

### Task 4: Verify the release metadata and supporting tests

**Files:**
- Verify only

- [ ] Run the focused client contract tests that support the release note claims.
- [ ] Confirm the three version locations all read `0.8.0`.
- [ ] Confirm the new changelog entry is at the top of `CHANGELOG.md`.
