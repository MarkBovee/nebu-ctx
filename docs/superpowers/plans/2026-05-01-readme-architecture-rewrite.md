# README Architecture Rewrite Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Rewrite `README.md` so it documents the real `nebu-ctx` architecture, current tool routing, host/client split, and project direction using `connect` / `host` / `server` as the primary product vocabulary.

**Architecture:** This is a documentation-first change centered on `README.md`. The work should replace stale marketing copy with a fact-based system overview derived from current source files, preserving a useful landing-page structure while making the Rust client, .NET host, dashboard, storage, routing, origin story, and roadmap explicit.

**Tech Stack:** Markdown, Rust source as reference, .NET source as reference, GitHub README conventions

---

## File Map

- Modify: `README.md`
  - Replace outdated opening narrative and architecture copy
  - Rebuild setup, routing, dashboard, tool inventory, origin, roadmap, and development sections
- Reference: `AGENTS.md`
  - Source of truth for architecture, routing, storage model, dashboard panels, and validation commands
- Reference: `client/src/mcp_server/mod.rs`
  - Source of truth for server-only and server-preferred tool routing
- Reference: `client/src/cli/dispatch.rs`
  - Source of truth for currently exposed CLI surfaces such as `dashboard`, `gain`, `serve`, and legacy `cloud` wording still present in commands
- Reference: `server/src/NebuCtx.Server.Core/ToolRegistry.cs`
  - Source of truth for current server version string and registered server-facing behavior references

### Task 1: Rebuild the README opening around the actual system

**Files:**
- Modify: `README.md:1-220`
- Reference: `AGENTS.md:3-18`
- Reference: `AGENTS.md:33-49`
- Reference: `AGENTS.md:88-92`

- [ ] **Step 1: Write the failing content expectation list**

Add this checklist to your scratchpad before editing so the rewrite has explicit pass/fail criteria:

```markdown
- README opening no longer says "Cloud Edition"
- README opening no longer leads with "60-99%" savings language
- README opening explicitly states: Rust client + self-hosted .NET host + dashboard + Postgres-backed server state
- README opening uses `connect`, `host`, or `server` as the main product vocabulary
```

- [ ] **Step 2: Verify the current README fails that checklist**

Run: `grep -nE "Cloud Edition|60-99%|Cloud Dashboard|cloud-backed|cloud server" README.md`

Expected: matches are returned from the current README header/opening, proving the rewrite is still needed.

- [ ] **Step 3: Replace the opening block with fact-based intro copy**

Update the top of `README.md` so the opening uses content in this shape:

```md
```
  ███╗   ██╗███████╗██████╗ ██╗   ██╗      ██████╗████████╗██╗  ██╗
  ████╗  ██║██╔════╝██╔══██╗██║   ██║     ██╔════╝╚══██╔══╝╚██╗██╔╝
  ██╔██╗ ██║█████╗  ██████╔╝██║   ██║     ██║        ██║    ╚███╔╝
  ██║╚██╗██║██╔══╝  ██╔══██╗██║   ██║     ██║        ██║    ██╔██╗
  ██║ ╚████║███████╗██████╔╝╚██████╔╝     ╚██████╗   ██║   ██╔╝ ██╗
  ╚═╝  ╚═══╝╚══════╝╚═════╝  ╚═════╝       ╚═════╝   ╚═╝   ╚═╝  ╚═╝
          Context Runtime for AI Agents
```

`nebu-ctx` is built around two halves that work together: a thin Rust client that plugs into agent/editor workflows, and a self-hosted .NET host that serves MCP over HTTP, stores project state, and exposes the dashboard. The host uses PostgreSQL-backed stores in production, while the client handles local tools, shell integration, hooks, and routing.

This project started from a fork/inspiration of `lean-ctx`, then evolved into `nebu-ctx` so the client, host, dashboard, and project-memory model fit the rest of Mark's stack.
```

Also add a short section immediately after the intro with a real flow summary in this shape:

```md
## How `nebu-ctx` actually works

1. Your editor or agent talks to the local Rust client.
2. The client handles local tools, shell execution, and hook workflows directly.
3. Server-backed tools are routed to the .NET host over HTTP.
4. The host serves the dashboard, persists project state, and returns MCP responses.
```

- [ ] **Step 4: Verify the opening now reflects the new positioning**

Run: `grep -nE "Cloud Edition|60-99%|Cloud Dashboard" README.md`

Expected: no matches in the rewritten opening sections.

Run: `grep -nE "Rust client|self-hosted \.NET host|PostgreSQL|How `nebu-ctx` actually works" README.md`

Expected: matches confirm the new opening language is present.

- [ ] **Step 5: Commit**

```bash
git add README.md
git commit -m "docs: rewrite README opening around actual architecture"
```

### Task 2: Replace stale architecture, routing, and dashboard claims with verified system details

**Files:**
- Modify: `README.md:221-520`
- Reference: `AGENTS.md:40-92`
- Reference: `client/src/mcp_server/mod.rs:15-27`

- [ ] **Step 1: Write the failing architecture facts to verify**

Use this checklist before editing:

```markdown
- README must describe local vs server-only vs server-preferred routing
- README must list dashboard port 3333 and MCP HTTP port 4242
- README must describe the 15 dashboard panels using current names
- README must stop using `cloud dashboard` and `cloud-backed` as primary labels
```

- [ ] **Step 2: Verify current architecture wording is stale**

Run: `grep -nE "cloud dashboard|cloud-backed|Cloud Context Server|Cloud Dashboard" README.md`

Expected: matches prove the current wording is still anchored to outdated terminology.

- [ ] **Step 3: Rewrite the architecture and dashboard sections using current facts**

Replace the architecture section with content in this shape:

```md
## Architecture

`nebu-ctx` is split into a local client and a self-hosted host:

- `client/`: Rust CLI, MCP stdio entrypoint, shell integration, hook handlers, and local tool dispatch
- `server/src/`: .NET host, dashboard endpoints, contracts, telemetry, project registry, and server tool handlers

Runtime ports:

- Dashboard: `3333`
- MCP HTTP host: `4242`

Production storage model:

- PostgreSQL-backed stores for project, session, knowledge, brain, code index, and checkout bindings
- In-memory telemetry store with persistence/hydration on the host side

Tool routing:

- Server-only: `ctx_brain`, `ctx_routes`, `ctx_gain`, `ctx_cost`, `ctx_heatmap`, `ctx_stats`
- Server-preferred: `ctx_knowledge`, `ctx_session`
- All other tools stay local in the Rust client
```

Replace the dashboard section with a fact-based list in this shape:

```md
## Dashboard

The dashboard is served by the same .NET host on port `3333`.

Current panels:

1. Overview
2. Live Observatory
3. Knowledge Graph
4. Dependency Map
5. Compression Lab
6. Agent World
7. Brain Memory
8. Search Explorer
9. Learning Curves
10. Symbol Explorer
11. Call Graph
12. Route Map
13. Context Layer
14. MCP Token
```

- [ ] **Step 4: Verify the rewritten routing and dashboard facts**

Run: `grep -nE "Server-only:|Server-preferred:|3333|4242|Live Observatory|MCP Token" README.md`

Expected: matches confirm the new architecture and dashboard sections are present.

Run: `grep -nE "cloud dashboard|cloud-backed|Cloud Context Server" README.md`

Expected: no matches remain.

- [ ] **Step 5: Commit**

```bash
git add README.md
git commit -m "docs: align README architecture and dashboard details"
```

### Task 3: Rebuild setup, tool inventory, and integration sections around current reality

**Files:**
- Modify: `README.md:80-560`
- Reference: `client/src/cli/dispatch.rs:28-190`
- Reference: `AGENTS.md:51-68`
- Reference: `AGENTS.md:100-151`

- [ ] **Step 1: Write the failing setup/inventory requirements**

Use this checklist:

```markdown
- README setup must describe install client -> run host -> connect project -> setup integrations -> verify
- README must not present `cloud` as the canonical product workflow
- README tool inventory must be framed as current surfaces and routing, not stale headline counts unless revalidated
- README integrations must describe what `setup` or `init` actually configures
```

- [ ] **Step 2: Verify the current setup still centers `cloud`**

Run: `grep -nE "cloud bind|Start the cloud server|Cloud & Project Management|49 Intelligent Tools" README.md`

Expected: current setup and inventory sections still show the stale framing.

- [ ] **Step 3: Rewrite setup and tool inventory sections**

Update setup to use content in this shape:

```md
## Install and run

```bash
# 1. Install the Rust client
cargo install nebu-ctx

# 2. Start the host
podman run -d --name nebu-ctx \
  -p 3333:3333 -p 4242:4242 \
  --env-file .env \
  ghcr.io/markbovee/nebu-ctx

# 3. Connect your project to the host
nebu-ctx cloud bind

# 4. Configure editor and agent integrations
nebu-ctx setup

# 5. Verify
nebu-ctx doctor
```

Today the CLI still exposes some `cloud`-named commands. In the product language of this README, those commands are part of the project-to-host connect flow.
```

Rewrite the tool section into a routing-first inventory in this shape:

```md
## Tool surfaces

`nebu-ctx` exposes a mixed tool surface:

- local client tools for file/context work, shell execution, search, tree, compression, and project analysis
- server-only tools for brain, routes, gain, cost, heatmap, and stats
- server-preferred tools for knowledge and session state

Representative local tools:

- `ctx_read`
- `ctx_multi_read`
- `ctx_tree`
- `ctx_shell`
- `ctx_search`
- `ctx_edit`
- `ctx_overview`
- `ctx_preload`
- `ctx_semantic_search`
- `ctx_architecture`

Representative server-backed tools:

- `ctx_brain`
- `ctx_routes`
- `ctx_gain`
- `ctx_cost`
- `ctx_heatmap`
- `ctx_stats`
- `ctx_knowledge`
- `ctx_session`
```

Rewrite integrations into a concise table in this shape:

```md
## Agent and editor integrations

`nebu-ctx setup` and `nebu-ctx init --agent ...` configure the supported surfaces that exist today, including Claude Code, GitHub Copilot, Cursor, VS Code, Windsurf, Zed, Codex CLI, Gemini CLI, and OpenCode.

The exact configuration varies by tool, but the integration paths are built around MCP registration, hook installation, and project/global instruction files where supported.
```

- [ ] **Step 4: Verify the new setup and inventory framing**

Run: `grep -nE "Install and run|connect your project|Tool surfaces|Agent and editor integrations" README.md`

Expected: matches confirm the new sections exist.

Run: `grep -nE "49 Intelligent Tools|Cloud & Project Management|Start the cloud server" README.md`

Expected: no matches remain.

- [ ] **Step 5: Commit**

```bash
git add README.md
git commit -m "docs: rebuild README setup and tool inventory"
```

### Task 4: Add origin, roadmap, and accurate development guidance; then verify the full README

**Files:**
- Modify: `README.md:520-EOF`
- Reference: `AGENTS.md:108-151`
- Reference: `docs/superpowers/specs/2026-05-01-readme-architecture-design.md`

- [ ] **Step 1: Write the final acceptance checklist**

Use this as the final pass criteria:

```markdown
- README contains a concise origin section mentioning `lean-ctx` fork/inspiration and transition into `nebu-ctx`
- README contains a roadmap section that mentions richer agent-session collaboration as future work
- README includes the real build/test commands from AGENTS.md
- README no longer uses `cloud` as the main product term
- README still reads cleanly as a landing page
```

- [ ] **Step 2: Verify those sections are currently missing or incomplete**

Run: `grep -nE "What's the difference between nebu-ctx and lean-ctx|Roadmap|Build And Validation|agent-session collaboration" README.md`

Expected: origin exists only as a narrow FAQ item, roadmap is absent, and development guidance is not yet shaped the new way.

- [ ] **Step 3: Add origin, roadmap, and development sections**

Add or rewrite sections using content in this shape:

```md
## Project origin

The client side of `nebu-ctx` started from a fork and practical inspiration of `lean-ctx`, then was reshaped into a broader system that fits the rest of Mark's projects: a Rust client locally, a .NET host for MCP and dashboard capabilities, and shared project state on the server side.

## Roadmap

Current direction includes:

- better collaboration between agent sessions
- richer shared session state and handoff flows
- continued removal of old `cloud` terminology from the product surface
- further simplification of the client/server split where possible

## Development

```bash
cargo test --manifest-path client/Cargo.toml
dotnet test server/NebuCtx.slnx -p:AllowMissingPrunePackageData=true
bash tests/local-server-cli-test.sh
```

For deeper implementation details, see `AGENTS.md`.
```

- [ ] **Step 4: Verify the full README against the design spec**

Run: `grep -n "cloud" README.md`

Expected: only limited compatibility references remain, not primary product framing.

Run: `grep -nE "Project origin|Roadmap|Development|lean-ctx|agent sessions|AGENTS.md" README.md`

Expected: matches confirm the new closing sections are present.

Read the full README once and confirm:

```markdown
- opening matches the actual architecture
- ports and routing are correct
- dashboard panel list is correct
- setup flow uses connect/server vocabulary
- no unsupported hard counts remain unless revalidated from code
```

- [ ] **Step 5: Commit**

```bash
git add README.md
git commit -m "docs: complete README architecture rewrite"
```
