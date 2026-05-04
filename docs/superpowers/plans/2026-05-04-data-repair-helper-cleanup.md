# Data Repair Helper Cleanup Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the local `NebuCtx.DataRepair` helper easier to understand and safer to run without changing its behavior.

**Architecture:** Keep the helper as a single admin-oriented console entrypoint, but remove repetitive connection boilerplate and document the two supported modes clearly in both source and a local README. This keeps the tool useful for local repair work without adding a full CLI layer.

**Tech Stack:** .NET 10, Npgsql, Markdown

---

### Task 1: Clean Up Helper Structure

**Files:**
- Modify: `server/tools/NebuCtx.DataRepair/Program.cs`

- [ ] Extract repeated connection-string / connection-opening code into one small helper.
- [ ] Add a short top-of-file usage comment covering inspect mode and `NEBU_REPAIR_DELETE_UNRESOLVED=1`.
- [ ] Keep the main flow readable: inspect -> optional delete/migrate -> report.

### Task 2: Add Local Usage Documentation

**Files:**
- Create: `server/tools/NebuCtx.DataRepair/README.md`

- [ ] Document what the helper is for.
- [ ] Document the safe inspect-first workflow.
- [ ] Add example commands for inspect mode and unresolved-delete mode.

### Task 3: Verify

**Files:**
- Modify: none

- [ ] Run: `dotnet build server/tools/NebuCtx.DataRepair/NebuCtx.DataRepair.csproj`
- [ ] Confirm build succeeds with 0 errors.
