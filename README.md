```
  ███╗   ██╗███████╗██████╗ ██╗   ██╗      ██████╗████████╗██╗  ██╗
  ████╗  ██║██╔════╝██╔══██╗██║   ██║     ██╔════╝╚══██╔══╝╚██╗██╔╝
  ██╔██╗ ██║█████╗  ██████╔╝██║   ██║     ██║        ██║    ╚███╔╝
  ██║╚██╗██║██╔══╝  ██╔══██╗██║   ██║     ██║        ██║    ██╔██╗
  ██║ ╚████║███████╗██████╔╝╚██████╔╝     ╚██████╗   ██║   ██╔╝ ██╗
  ╚═╝  ╚═══╝╚══════╝╚═════╝  ╚═════╝       ╚═════╝   ╚═╝   ╚═╝  ╚═╝
          Context Runtime for AI Agents
```

<h3 align="center">Rust client + self-hosted .NET host for MCP, dashboard, memory, telemetry, and agent workflows</h3>

<p align="center">
  <a href="https://github.com/MarkBovee/nebu-ctx/actions"><img src="https://github.com/MarkBovee/nebu-ctx/actions/workflows/ci.yml/badge.svg" alt="CI"></a>
  <a href="https://crates.io/crates/nebu-ctx"><img src="https://img.shields.io/crates/v/nebu-ctx?color=%23e6522c" alt="crates.io"></a>
  <a href="https://crates.io/crates/nebu-ctx"><img src="https://img.shields.io/crates/d/nebu-ctx?color=%23e6522c" alt="Downloads"></a>
  <a href="https://github.com/MarkBovee/nebu-ctx/pkgs/container/nebu-ctx"><img src="https://img.shields.io/badge/Container-GHCR-2496ED?logo=docker&logoColor=white" alt="Container"></a>
  <a href="LICENSE"><img src="https://img.shields.io/badge/License-Apache%202.0-blue.svg" alt="License"></a>
</p>

<p align="center">
  <a href="#install">Install</a> ·
  <a href="#what-it-is">What It Is</a> ·
  <a href="#how-it-works">How It Works</a> ·
  <a href="#architecture">Architecture</a> ·
  <a href="#tool-routing">Tool Routing</a> ·
  <a href="docs/MEMORY.md">Memory</a> ·
  <a href="docs/TOOLS.md">Tools</a> ·
  <a href="#dashboard">Dashboard</a> ·
  <a href="#development">Development</a>
</p>

## Install

Choose one path:

- Rust client CLI on your machine
- Home Assistant or self-hosted server with the dashboard and HTTP MCP host

### Rust client CLI

Install `cargo-binstall` first if you do not already have it:

```bash
curl -L --proto '=https' --tlsv1.2 -sSf https://raw.githubusercontent.com/cargo-bins/cargo-binstall/main/install-from-binstall-release.sh | bash
```

Install the thin client:

```bash
cargo binstall nebu-ctx
```

Set up your editor and agent integrations:

```bash
nebu-ctx setup
nebu-ctx doctor
```

If you want to build from source instead:

```bash
cargo install --path client --bin nebu-ctx --force
```

### Home Assistant or self-hosted server

Run the published container:

```bash
podman run -d --name nebu-ctx \
  -p 127.0.0.1:3333:3333 \
  -p 127.0.0.1:4242:4242 \
  --env-file .env \
  ghcr.io/markbovee/nebu-ctx
```

Your `.env` should include at least:

- `NEBULA_CTX_HTTP_TOKEN`
- `NEBULA_CTX_HOST=0.0.0.0`
- the PostgreSQL variables required by the host

Then connect the client:

```bash
nebu-ctx connect --endpoint http://127.0.0.1:4242 --token <token>
nebu-ctx doctor
```

Home Assistant users can use the packaged add-on under `homeassistant/`. The add-on runs both host surfaces in one container:

- dashboard on `3333`
- MCP HTTP on `4242`

## What It Is

`nebu-ctx` is split into two real runtime surfaces:

- a thin Rust client that lives on the developer machine and plugs into editors, MCP stdio, shell hooks, and agent workflows
- a self-hosted .NET host that serves MCP over HTTP, exposes the dashboard, persists project state, and handles the server-backed tool surfaces

In production, the host uses PostgreSQL-backed stores for project, knowledge, session, brain, and code-index data. The client stays responsible for local tool execution, local compression behavior, shell integration, and hook orchestration.

The current user-facing workflow is `connect`: connect the client to a host and let normal server-backed calls resolve project identity implicitly from repository and checkout context.

## How It Works

1. Your editor or agent talks to the local `nebu-ctx` Rust client.
2. The client handles local tools, shell execution, rules injection, and hook workflows directly.
3. Server-backed tools are routed from the client to the .NET host over HTTP.
4. The host serves the dashboard, persists project state, ingests telemetry, and returns MCP responses.

In practice, that means `nebu-ctx` is not a SaaS dependency. The host is yours to run locally, in a container, or on your own infrastructure.

## Project Bootstrap

Use the explicit project bootstrap flow when you want an agent to map a repository and propose durable memory facts without silently writing memory on first contact.

```bash
nebu-ctx project-bootstrap preview [--path <repo>]
nebu-ctx project-bootstrap apply [--path <repo>]
```

- `preview` summarizes stack, entrypoints, tests, infra, modules, and workflow signals.
- `preview` only shows candidate facts and evidence.
- `apply` persists those reviewed facts through canonical knowledge/memory paths.

This flow is intentionally preview-first and explicit.

## Architecture

```text
AI editor / agent
  -> MCP stdio / hooks
Rust client (`client/`)
  -> local tools
  -> shell integration
  -> hook handlers
  -> host connection + implicit project resolution
  -> HTTP calls for server-backed tools
.NET host (`server/src/`)
  -> MCP HTTP endpoint
  -> dashboard UI + APIs
  -> server tool handlers
  -> telemetry + project registry
  -> PostgreSQL-backed stores
```

Main surfaces:

- `client/src/main.rs`: thin CLI entrypoint
- `client/src/cli/dispatch.rs`: CLI surface and command routing
- `client/src/cli/memory.rs`: `nebu-ctx memory list | lifecycle | export | import` subcommands
- `client/src/core/contextual_surface.rs`: hook-driven memory suggestion engine
- `client/src/hook_handlers.rs`: hook logic for Claude Code / Copilot CLI flows
- `client/src/mcp_server/mod.rs`: MCP routing and server-backed tool decisions
- `client/src/mcp_server/dispatch.rs`: local MCP tool dispatch
- `client/src/server_client.rs`: current HTTP client for host communication
- `server/src/NebuCtx.Server.Host/`: .NET host and dashboard endpoints
- `server/src/NebuCtx.Server.Core/`: tool registry, telemetry, validation, shared host logic
- `server/src/NebuCtx.Server.Core/Services/MemoryLifecycleService.cs`: lifecycle stats, promotions, stale, scoring
- `server/src/NebuCtx.Tools/`: one server tool handler per directory
- `server/src/NebuCtx.Contracts/`: shared DTOs
- `server/src/NebuCtx.Contracts/Mcp/MemoryListContracts.cs`: browsing filter and result DTOs
- `server/src/NebuCtx.Contracts/Mcp/PromotionTrace.cs`: brain-to-knowledge traceability DTO

Runtime ports:

- dashboard: `3333`
- MCP HTTP host: `4242`

Production storage model:

- PostgreSQL is the only supported production store
- `TelemetryStore` is in-memory inside the host, with persistence/hydration around it
- `IBrainStore`, `IKnowledgeStore`, `ISessionStore`, `IProjectStore`, `ICodeIndexStore`, and `ICheckoutBindingStore` are Postgres-backed in production

## Tool Routing

The client now exposes exactly 4 public MCP tools.

| Public tool | Purpose |
|:---|:---|
| `ctx_read` | Read files, multiple files, symbols, outlines, and archived output |
| `ctx_search` | Regex and semantic code search |
| `ctx_tree` | Directory structure and file counts |
| `ctx` | Higher-level memory, context, graph, analytics, agent, and inspect workflows |

Internally, the Rust client still routes these public calls to local handlers or server-backed tool handlers as needed. That internal routing is deliberately hidden from the MCP contract so agents only learn one small surface.

For shell execution, use the native `Shell` / `Bash` tool. The nebu-ctx shell hook (installed via `nebu-ctx setup`) wraps that command and compresses its output automatically.

## Current MCP Tool Surface

The public MCP contract is intentionally small and fixed.

### `ctx_read`

Use `ctx_read` for:

- single-file reads
- multi-file reads
- symbol reads
- outline reads
- archived output retrieval

Representative `ctx_read` modes:

- `auto`
- `full`
- `map`
- `signatures`
- `diff`
- `task`
- `reference`
- `aggressive`
- `entropy`
- `lines:N-M`

Representative targets:

- `file`
- `files`
- `symbol`
- `outline`
- `archive`

### `ctx_search`

Use `ctx_search` for:

- regex code search
- semantic code search

Modes:

- `regex`
- `semantic`

### `ctx_tree`

Use `ctx_tree` for:

- directory listings
- shallow project orientation
- compact file-count views

### `ctx`

Use `ctx` for high-level workflows via `domain` + `action`.

For hosted memory management over the MCP HTTP server, this is the public entrypoint. Do not call internal host handlers like `ctx_knowledge` or `ctx_session` directly from external clients.

Domains:

- `memory`
- `context`
- `graph`
- `analytics`
- `agents`
- `inspect`

Examples:

```json
{ "name": "ctx", "arguments": { "domain": "memory", "action": "recall", "query": "session state decisions" } }
```

```json
{ "name": "ctx", "arguments": { "domain": "graph", "action": "impact", "path": "client/src/mcp_server/mod.rs" } }
```

### Hosted Memory Management

Public memory calls go to `POST /v1/tools/call` with `name="ctx"` and `arguments.domain="memory"`.

Internally the host routes memory actions like this:

- `task`, `finding`, `decision`, `save`, `load`, `status`, `reset`, `list`, `cleanup` -> hosted session state
- `remember`, `recall`, `search`, `list`, `lifecycle`, `status`, `remove`, `categories`, `timeline`, `consolidate`, `promote`, `candidates`, `review_candidate`, `upkeep`, `wakeup`, `triage`, `import` -> hosted canonical knowledge
- `store`, `ingest`, `recall`, `list`, `lifecycle`, `forget`, `import` -> hosted brain (server-only, no public client fallback)

Example: store a durable fact

```bash
curl -fsS \
  -H "Authorization: Bearer <token>" \
  -H 'Content-Type: application/json' \
  -d '{
    "name": "ctx",
    "arguments": {
      "domain": "memory",
      "action": "remember",
      "category": "decision",
      "key": "memory-owner",
      "value": "server owns canonical memory",
      "confidence": 0.95
    }
  }' \
  http://127.0.0.1:4242/v1/tools/call
```

Example: recall stored memory

```bash
curl -fsS \
  -H "Authorization: Bearer <token>" \
  -H 'Content-Type: application/json' \
  -d '{
    "name": "ctx",
    "arguments": {
      "domain": "memory",
      "action": "recall",
      "query": "memory-owner"
    }
  }' \
  http://127.0.0.1:4242/v1/tools/call
```

Example: run hosted upkeep or triage

```bash
curl -fsS \
  -H "Authorization: Bearer <token>" \
  -H 'Content-Type: application/json' \
  -d '{"name":"ctx","arguments":{"domain":"memory","action":"upkeep"}}' \
  http://127.0.0.1:4242/v1/tools/call

curl -fsS \
  -H "Authorization: Bearer <token>" \
  -H 'Content-Type: application/json' \
  -d '{"name":"ctx","arguments":{"domain":"memory","action":"triage","mode":"preview"}}' \
  http://127.0.0.1:4242/v1/tools/call
```

Example: list or inspect lifecycle (0.9.0+)

```bash
curl -fsS \
  -H "Authorization: Bearer <token>" \
  -H 'Content-Type: application/json' \
  -d '{"name":"ctx","arguments":{"domain":"memory","action":"list","memory_type":"knowledge","category":"decision","limit":25}}' \
  http://127.0.0.1:4242/v1/tools/call

curl -fsS \
  -H "Authorization: Bearer <token>" \
  -H 'Content-Type: application/json' \
  -d '{"name":"ctx","arguments":{"domain":"memory","action":"lifecycle","lifecycle_subaction":"stats","memory_type":"knowledge"}}' \
  http://127.0.0.1:4242/v1/tools/call
```

For operator inspection, the dashboard exposes per-project memory on `GET /api/dashboard/projects/{projectId}/memory` and triage apply on `POST /api/dashboard/projects/{projectId}/memory/triage?mode=apply`.

Full memory flow docs, including hooks, storage layers, recall paths, and dashboard cleanup proposal: `docs/MEMORY.md`.

## Memory System

The memory system is hosted on the .NET side and exposed through `ctx(domain="memory", action=...)` over MCP. As of 0.9.0 the memory surface covers recall, browsing, lifecycle inspection, contextual surfacing via hooks, cross-domain correlation between brain and knowledge, and portable export/import.

### Browsing

List memories with category, time range, source type, lifecycle status, sort, limit, and offset filters. Backward compatible with existing `recall` and `remember` paths.

```bash
nebu-ctx memory list --type knowledge \
  --category decision --since 30d --limit 25 --json

nebu-ctx memory list --type brain --status stale --limit 50
```

### Lifecycle

Inspect health stats, promotion candidates, stale rows, and per-memory scoring breakdown for both brain and knowledge.

```bash
nebu-ctx memory lifecycle stats --type knowledge
nebu-ctx memory lifecycle promotions --type brain --limit 10
nebu-ctx memory lifecycle stale --type knowledge --days 30
nebu-ctx memory lifecycle scoring --type brain --category decision --key memory-owner
```

### Contextual surfacing via hooks

The client hook system surfaces memory suggestions inline during `PostToolUse`, `PreCompact`, and `UserPromptSubmit` for Claude Code / Copilot CLI style flows. Suggestions prefer contextual facts over static high-confidence memory, with a 1024-byte byte budget and a per-tool cooldown to avoid spam. The system is non-intrusive: nothing changes if the host is unreachable.

### Cross-domain correlation

Knowledge entries carry a `promotion_trace` that links back to the brain session event, brain key, category, value, and timestamp that produced them. New `knowledge_entries` columns track `promotion_action`, `promotion_session_id`, `promotion_timestamp`, and the originating brain key/category/value/timestamp. Knowledge listings can be filtered by source session or source brain key for traceability.

```bash
nebu-ctx memory list --type knowledge --source-session <session-id>
nebu-ctx memory list --type knowledge --source-brain-key memory-owner
```

### Portability

Export and import full memory sets as versioned JSON, with overwrite control and roundtrip fidelity for transfer between environments.

```bash
nebu-ctx memory export --type knowledge --category decision --since 30d \
  --output knowledge-export.json

nebu-ctx memory import knowledge-export.json --type knowledge --overwrite
```

Export schema (`schema_version: "1"`) includes `export_info` (timestamp, version, memory_type, filters_applied, memory_counts) and a `memories` array. Import reports `added`, `updated`, and `skipped` counts and honours `overwrite` to skip-or-replace existing entries.

### Architecture and constraints

- Public MCP surface stays at 5 tools; all new capabilities flow through the existing `ctx` meta-tool with `domain="memory"` and new actions, or through the `nebu-ctx memory ...` CLI subcommands.
- All additions are backward compatible with existing `ctx memory recall` and `remember` paths.
- Storage model unchanged: PostgreSQL in production, in-memory stubs in tests.
- Server-only and server-preferred routing preserved: `ctx_brain` stays server-only with no public client fallback.
- Telemetry and analytics tools remain untouched.

Full requirements, design decisions, and per-capability specs live under `openspec/specs/memory-{brain,core,knowledge,lifecycle}-enhanced/`, `memory-browsing/`, `memory-correlation/`, and `memory-portability/`. The archived change proposal sits under `openspec/changes/archive/2026-06-04-memory-system-enhancements/`.

## Hook System

The client currently wires 7 hook types for Claude Code / Copilot CLI style flows:

| Hook | Command |
|:---|:---|
| PostToolUse.* | `nebu-ctx hook post-tool-use` |
| PreCompact | `nebu-ctx hook pre-compact` |
| PreToolUse:bash | `nebu-ctx hook rewrite` |
| PreToolUse:read... | `nebu-ctx hook redirect` |
| SessionStart | `nebu-ctx hook session-start` |
| Stop | `nebu-ctx hook stop` |
| UserPromptSubmit | `nebu-ctx hook user-prompt-submit` |

Those hooks are responsible for things like:

- command rewriting
- session-state snapshots
- local journal capture
- post-tool telemetry and fact-backed memory promotion

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

The dashboard is not a separate frontend deployment. It is part of the host process that also serves the MCP HTTP surface.

## Main Capabilities

### Local client capabilities

- MCP stdio entrypoint for editor/agent integration
- shell command execution and compression-aware workflows
- local file/context tools
- hook orchestration and rules injection
- contextual memory surfacing during PostToolUse, PreCompact, and UserPromptSubmit
- project-local state and fallback behavior for selected tools
- `nebu-ctx memory ...` operator subcommands that wrap hosted memory actions

### Host-backed capabilities

- persistent session, knowledge, and brain stores
- memory browsing with category, time, source, and lifecycle filters
- memory lifecycle stats, promotion candidates, stale rows, and scoring breakdown
- cross-domain correlation with promotion trace from brain session events to knowledge
- portable export/import of memory with schema versioning and overwrite control
- route extraction and analytics surfaces
- telemetry ingestion and observability
- dashboard APIs and UI
- project registry and code-index related host workflows

### Multi-agent direction

The repo already includes agent/session-oriented surfaces such as `ctx_agent`, `ctx_share`, `ctx_task`, and `ctx_handoff`. The roadmap continues further in that direction: better collaboration between agent sessions, richer handoff flows, and stronger shared context across runs.

## Install And Run

### 1. Install the Rust client

```bash
cargo binstall nebu-ctx
```

This downloads the prebuilt lightweight client binary with `cargo-binstall` when available. If you do not have cargo-binstall yet, install it using the instructions at <https://github.com/cargo-bins/cargo-binstall>. If `cargo binstall nebu-ctx` is unavailable or no matching artifact exists, use the published release asset instead. If you prefer a source-build fallback, use `cargo install nebu-ctx`.

If you want to build from the repo instead, use `cargo install --path client --bin nebu-ctx --force`.

On Windows, the published crate keeps the `rust-lld` override so the packaged install path does not depend on Visual Studio Build Tools.

### 2. Start the host

```bash
podman run -d --name nebu-ctx \
  -p 127.0.0.1:3333:3333 \
  -p 127.0.0.1:4242:4242 \
  --env-file .env \
  ghcr.io/markbovee/nebu-ctx
```

Your `.env` should include at least:

- `NEBULA_CTX_HTTP_TOKEN`
- `NEBULA_CTX_HOST=0.0.0.0`
- the PostgreSQL variables required by the host

### 3. Connect the client to the host

```bash
nebu-ctx connect --endpoint http://127.0.0.1:4242 --token <token>
```

### 4. Configure editors and agent integrations

```bash
nebu-ctx setup
```

Shell startup banner modes:

- `nebu-ctx config set startup_banner compact` keeps the default one-line startup status
- `nebu-ctx config set startup_banner ascii` shows the README-style ASCII banner on shell init
- `nebu-ctx config set startup_banner off` disables the startup banner entirely

Temporary override for the current shell session:

```bash
NEBU_CTX_STARTUP_BANNER=ascii
```

### 5. Verify

```bash
nebu-ctx doctor
```

### 6. Visit the dashboard directly on the host

Open `http://127.0.0.1:3333` or the mapped host URL from your deployment.

## Agent And Editor Integrations

`nebu-ctx setup` and `nebu-ctx setup --agent ...` configure the supported surfaces that exist today.

Common integrations:

- Claude Code
- GitHub Copilot
- Cursor
- VS Code
- Windsurf
- Zed
- Codex CLI
- Gemini CLI
- OpenCode

Additional `init --agent` support also exists for:

- Cline / Roo
- Pi
- Qwen
- Trae
- Amazon Q
- JetBrains
- Kiro
- Verdent
- Aider
- Amp
- Crush
- Antigravity
- Hermes

Depending on the target, the setup path may include:

- MCP registration
- shell or pre-tool hook installation
- project or global rules/instruction files
- editor-specific config file updates

Shell hook notes:

- `nebu-ctx setup --global` now refreshes older PowerShell installs to the current sourced `~/.nebu-ctx/shell-hook.ps1` model
- PowerShell now gets the same `nebu-ctx-on`, `nebu-ctx-off`, `nebu-ctx-mode`, and `nebu-ctx-status` helpers as the POSIX shells

## CLI Surfaces Worth Knowing

The current user-facing CLI surface includes, among others:

- `nebu-ctx connect`
- `nebu-ctx disconnect`
- `nebu-ctx setup`
- `nebu-ctx gain`
- `nebu-ctx serve`
- `nebu-ctx doctor`
- `nebu-ctx uninstall`
- `nebu-ctx mcp`
- `nebu-ctx memory list`
- `nebu-ctx memory lifecycle`
- `nebu-ctx memory export`
- `nebu-ctx memory import`
- `nebu-ctx project-bootstrap`
- `nebu-ctx -c "..."`

The `nebu-ctx memory ...` subcommands wrap the corresponding `ctx(domain="memory", action=...)` MCP actions for operator use. The 0.9.0 surface covers `list`, `lifecycle` (`stats` / `promotions` / `stale` / `scoring`), `export`, and `import` for both brain and knowledge. The new commands are server-backed; the client refuses to run them without a connected host.

If you still see older server-routing terminology in low-level internals, treat that as cleanup debt rather than the current product model.

## Home Assistant Add-on

The repo also ships a Home Assistant add-on package under `homeassistant/`.

That add-on runs both host surfaces in one container:

- dashboard on `3333`
- MCP HTTP on `4242`

## Project Origin

The client side of `nebu-ctx` started from an earlier internal client foundation and was reshaped into a broader system that fits the rest of Mark's projects.

The result is no longer just a token-compression client. It is a combined Rust client + .NET host + dashboard stack designed around persistent project context, observability, and agent-oriented workflows.

## Roadmap

Current direction includes:

- better collaboration between agent sessions
- richer shared session state and handoff flows
- continued cleanup of old server-routing terminology across remaining internals
- further simplification and consolidation of the client/server boundary where it helps

## Development

Main validation commands:

```bash
cargo test --manifest-path client/Cargo.toml
dotnet test server/NebuCtx.slnx -p:AllowMissingPrunePackageData=true
bash tests/local-server-cli-test.sh
```

Local container workflow:

```bash
podman build -t nebu-ctx-server -f Dockerfile .
podman run -d --name nebu-ctx-local \
  -p 127.0.0.1:3333:3333 \
  -p 127.0.0.1:4242:4242 \
  --env-file .env \
  nebu-ctx-server
```

For deeper implementation details and repo conventions, see `AGENTS.md`.

## License

Apache License 2.0. See `LICENSE`.
