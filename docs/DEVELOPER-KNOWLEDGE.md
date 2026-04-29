# nebu-ctx Developer Knowledge Base

> Auto-generated from session history. Read this first at every session start instead of re-researching.
> Last updated: 2026-04-29 · Version baseline: 0.5.6

---

## 1. Architecture

```
┌─────────────────────────────────────────────────────────┐
│ Rust CLI client  (client/)                               │
│  nebu-ctx binary → crates.io                            │
│  - Thin proxy: installs hooks, routes MCP calls to host │
│  - Shell hook: captures every command (-c path)         │
│  - Hook events: Stop, PostToolUse → consolidation       │
└─────────────────────────┬───────────────────────────────┘
                          │ HTTP (MCP + REST)
┌─────────────────────────▼───────────────────────────────┐
│ .NET 10 server  (server/)                               │
│  Port 4242 → MCP endpoint (tool calls)                  │
│  Port 3333 → Dashboard (REST + HTML)                    │
│  PostgreSQL-backed: brain, knowledge, session, telemetry│
│  → ghcr.io/markbovee/nebu-ctx                          │
└─────────────────────────────────────────────────────────┘
```

**Layer locations:**
| Layer | Tech | Path | Published |
|-------|------|------|-----------|
| CLI | Rust | `client/` | crates.io |
| MCP host + dashboard | .NET 10 ASP.NET | `server/` | GHCR `ghcr.io/markbovee/nebu-ctx` |
| Container | Dockerfile (multi-stage SDK→Alpine) | `Dockerfile` (root) | GHCR |
| HA add-on | Pulls GHCR image, no local build | `homeassistant/config.yaml` | HA store |

---

## 2. Version Sync — THREE places, ONE commit

**Every version bump MUST update all three simultaneously:**

```
client/Cargo.toml                                  version = "X.Y.Z"
homeassistant/config.yaml                          version: "X.Y.Z"
server/src/NebuCtx.Application/ToolRegistry.cs     Current = "X.Y.Z"
```

`auto-release.yml` fails if any of the three differ. Current: `0.5.6`.

---

## 3. Build & Validation Commands

### Rust client
```bash
cargo test --manifest-path client/Cargo.toml
cargo build --manifest-path client/Cargo.toml
cargo install --path client --locked    # install locally
```

### .NET server
```bash
# Build (must add AllowMissingPrunePackageData on this machine)
dotnet build server/NebuCtx.slnx -p:AllowMissingPrunePackageData=true

# Tests — IMPORTANT: use vstest, NOT dotnet test (output is silently swallowed)
dotnet vstest server/tests/NebuCtx.IntegrationTests/bin/Debug/net10.0/NebuCtx.IntegrationTests.dll \
  --logger:"console;verbosity=detailed"

# Filter to specific tests
dotnet vstest ... --testcasefilter:"FullyQualifiedName~TelemetryStoreTests"
```

### Container (local dev)
```bash
# Build multi-stage image (compiles .NET from source)
podman build -t nebu-ctx-server -f Dockerfile .

# Run pointing at local Postgres (same DB as HA server)
podman run -d --name nebu-ctx-eval \
  -p 127.0.0.1:3333:3333 -p 127.0.0.1:4242:4242 \
  --env-file .env \
  nebu-ctx-server

# .env must contain:
# NEBULA_CTX_HTTP_TOKEN=<token>   (NOT AUTH_TOKEN)
# NEBULA_CTX_HOST=0.0.0.0
# DATABASE_URL=postgres://...
```

### Health checks
```bash
TOKEN=$(grep '^NEBULA_CTX_HTTP_TOKEN=' .env | cut -d= -f2)
curl -s http://127.0.0.1:4242/health                          # MCP (open)
curl -s -H "Authorization: Bearer $TOKEN" http://127.0.0.1:3333/api/gain  # dashboard (auth required)
```

### HA add-on smoke test
```bash
ADDON_DOCKERFILE=Dockerfile bash tests/local-addon-test.sh
# or (pull from GHCR):
NEBU_CTX_VERSION=0.5.4 bash tests/local-addon-test.sh
```

---

## 4. MCP Routing Architecture (Rust client)

File: `client/src/mcp_server/mod.rs`

```rust
// Tools that ALWAYS route to cloud server (never local fallback)
pub const CLOUD_ONLY_TOOLS: &[&str] = &[
    "ctx_brain", "ctx_gain", "ctx_cost", "ctx_heatmap", "ctx_stats",
];

// Tools that try cloud first, fall back to local on failure
const CLOUD_PREFERRED_TOOLS: &[&str] = &["ctx_knowledge", "ctx_session", ...];
```

**KEY ISSUE (Task A — pending):** `ctx_knowledge` is in `CLOUD_PREFERRED_TOOLS` and silently falls back to local JSON if cloud call fails. In a setup where cloud is always on, this creates silent divergence. Fix: if `ServerClient::load()` succeeds, treat `ctx_knowledge` like a cloud-only tool.

**Routing flow:**
1. Tool call arrives via MCP
2. If in `CLOUD_ONLY_TOOLS` → `route_to_cloud()` → hard fail if server unreachable
3. If in `CLOUD_PREFERRED_TOOLS` → `route_to_cloud()` with fallback to local
4. Otherwise → `dispatch_local()`

---

## 5. Server — IToolHandler Pattern

All MCP tools are `IToolHandler` implementations registered via DI.

### Interface
```csharp
// server/src/NebuCtx.Tools/IToolHandler.cs
public interface IToolHandler
{
    string Name { get; }
    string Description { get; }
    Dictionary<string, object?> InputSchema { get; }
    Task<object> ExecuteAsync(
        Dictionary<string, object?> arguments,
        ToolExecutionContext context,
        CancellationToken ct);
}
```

### ToolExecutionContext
```csharp
// Has: ProjectId, Cwd, ProjectRoot, ActorLabel
// No storage access — inject TelemetryStore/IKnowledgeStore via constructor
```

### Registration
```csharp
// server/src/NebuCtx.Tools/ToolRegistration.cs
services.AddSingleton<IToolHandler, GainToolHandler>();
services.AddSingleton<IToolHandler, CostToolHandler>();
services.AddSingleton<IToolHandler, HeatmapToolHandler>();
services.AddSingleton<IToolHandler, StatsToolHandler>();
```

### Folder convention
Each handler lives in its own subfolder: `server/src/NebuCtx.Tools/Gain/GainToolHandler.cs`

---

## 6. TelemetryStore (server)

File: `server/src/NebuCtx.Application/TelemetryStore.cs`

**Core pattern:**
```csharp
public sealed class TelemetryStore
{
    private readonly object _gate = new();
    private readonly Dictionary<string, CommandTelemetrySnapshot> _commands = ...;

    // After Task 1 — NEW fields:
    private readonly Dictionary<string, Dictionary<string, CommandTelemetrySnapshot>> _projectCommands = ...;
    private readonly Dictionary<(string ProjectId, string Path), int> _fileAccessCounts = ...;
    private static readonly FrozenSet<string> FileAccessTools = FrozenSet.Create(...);

    public void RecordToolCall(string toolName, Dictionary<string,object?> arguments,
        object result, ToolExecutionContext context) { ... }

    public Snapshot GetSnapshot() { ... }
}
```

**Snapshot structure after Task 1:**
```csharp
public sealed class Snapshot
{
    public IReadOnlyDictionary<string, CommandTelemetrySnapshot> Commands { ... }
    public IReadOnlyDictionary<string, ProjectTelemetrySnapshot> PerProject { ... }  // NEW
    public IReadOnlyDictionary<string, int> GetFileAccess(string projectId) { ... }   // NEW
    // ... other existing properties
}

public sealed class ProjectTelemetrySnapshot
{
    public required string ProjectId { get; init; }
    public int TotalToolCalls { get; set; }
    public long TotalInputTokens { get; set; }
    public long TotalOutputTokens { get; set; }
    public IReadOnlyDictionary<string, CommandTelemetrySnapshot> Commands { ... }
    public IReadOnlyDictionary<string, int> FileAccess { ... }
}
```

**File-access tools set:**
```
ctx_read, ctx_edit, ctx_search, ctx_outline, ctx_symbol,
ctx_callees, ctx_callers, ctx_delta, ctx_benchmark, ctx_analyze,
ctx_smart_read, ctx_multi_read
```

**Important:** `TelemetryStore` has no constructor dependencies — `new TelemetryStore()` works in unit tests.

---

## 7. Dashboard Endpoints

File: `server/src/NebuCtx.Dashboard/DashboardEndpoints.cs`

All endpoints require `Authorization: Bearer <token>` (port 3333).

Existing:
- `GET /api/gain` — token usage report
- `GET /api/projects` — project list
- `GET /api/heatmap` — file-access heatmap (currently returns empty arrays — fixed in Task 4)

After Task 5 — new:
- `GET /api/projects/{projectId}/stats` → per-project stats JSON

---

## 8. Analytics Tools — Cloud Plan (Tasks 2–7)

Plan file: `docs/superpowers/plans/2026-04-29-cloud-analytics-tools.md`

### What each tool does

| Tool | Actions | Data source |
|------|---------|-------------|
| `ctx_gain` | report / score / tasks / agents / wrapped / json | TelemetryStore.GetSnapshot() |
| `ctx_cost` | report / tools / status / json | TelemetryStore, $2.50/1M tokens |
| `ctx_heatmap` | status / directory / dirs / cold / json | TelemetryStore.GetSnapshot().GetFileAccess() |
| `ctx_stats` | report / json | TelemetryStore, per-project filter via `project_id` arg |

### Pricing constant
```csharp
private const decimal PricePerMillionTokens = 2.50m;  // matches DashboardPayloadFactory.EstimateSavedCost
```

### CLI stubs disposition (main.rs)

| CLI command | Action |
|-------------|--------|
| `gain` | Rewrite → `call_tool("ctx_gain", ...)` via ServerClient |
| `cep` | DELETE (dead code — exits at line 28 guard AND has unreachable arm) |
| `heatmap` | DELETE (exits at line 28 guard) |
| `stats` | DELETE (exits at line 28 guard) |
| `dashboard` | Rewrite → print dashboard URL |
| `watch` | Rewrite → print dashboard URL |

Remove from `dispatch.rs`: stub arms for `ctx_cost` (~line 1270), `ctx_gain` (~line 1282), `ctx_heatmap` (~line 1573).

---

## 9. Hook System

Files: `client/src/hooks/agents.rs`, `client/src/hook_handlers.rs`, `client/src/main.rs`

### Installed hooks (as of v0.5.5)

| Agent | Hook event | Action |
|-------|-----------|--------|
| Claude Code | `PreToolUse: Bash` | `hook rewrite` — rewrites shell commands |
| Claude Code | `PreToolUse: Read/Grep/View` | `hook redirect` — redirects to MCP |
| Claude Code | `Stop` | `hook stop` — consolidate + cloud sync |
| Claude Code | `PostToolUse` | `hook post-tool-use` — telemetry |
| Copilot CLI | `preToolUse` | `hook rewrite` + `hook redirect` |
| Copilot CLI | `postSession` | `hook stop` |
| Copilot CLI | `postToolUse` | `hook post-tool-use` |

### CRITICAL: Telemetry must fire BEFORE passthrough()
In `main.rs`, the `-c` and `-t` shell-hook paths call `passthrough()` which calls `process::exit()`.
Fire telemetry/cloud sync BEFORE calling `passthrough()` or it will never execute.

### hook_handlers.rs key functions
```rust
pub fn handle_stop() { /* consolidate_latest() then post_promoted_facts_to_cloud() */ }
pub fn handle_post_tool_use() { /* reads stdin JSON, extracts tool_name + output, fires telemetry */ }
pub fn post_promoted_facts_to_cloud(facts, project_root) { /* ctx_knowledge(action=remember) for each */ }
```

---

## 10. ServerClient (Rust → .NET calls)

File: `client/src/server_client.rs` (or similar)

```rust
// Load from ~/.nebu-ctx/ config
let client = ServerClient::load()?;

// Call a tool
let result = client.call_tool(
    "ctx_gain",
    arguments,           // Map<String, Value>
    &project_context,    // created via discover_project_context(path)
)?;
```

**ureq 3.x gotcha:** `.send(&[u8])` sends no `Content-Type`. Must use:
```rust
.header("Content-Type", "application/json")
.send(&body_bytes)
```

---

## 11. Integration Test Pattern

File: `server/tests/NebuCtx.IntegrationTests/McpEndpointTests.cs`

```csharp
// WebApplicationFactory<Program> pattern
using var factory = new WebApplicationFactory<Program>()
    .WithWebHostBuilder(b => b.ConfigureAppConfiguration(...));
var client = factory.CreateClient();

// MCP tool call
var response = await client.PostAsJsonAsync("/mcp", new { method = "tools/call",
    @params = new { name = "ctx_gain", arguments = new { action = "report" } } });
var result = await response.Content.ReadFromJsonAsync<ToolCallResponse>();

// RULE: Never use ApiJsonRequestAsync<object> or <JsonElement> when a real model exists
// Always use strongly-typed DTOs for response validation
```

**TelemetryStore tests:** Direct instantiation works — no DI setup needed.
```csharp
var store = new TelemetryStore();
store.RecordToolCall("ctx_read", args, result, ctx);
var snap = store.GetSnapshot();
```

---

## 12. Hook Automation — Complete as of v0.5.6

All hooks are wired and active in `.claude/settings.local.json`:

| Hook | Matcher | Command | Handler | Does |
|------|---------|---------|---------|------|
| `PreToolUse` | `Bash` | `hook rewrite` | `handle_rewrite()` | Rewrites shell cmds to compression-aware form |
| `PreToolUse` | `Read/View/Grep/...` | `hook redirect` | `handle_redirect()` | Redirects native reads to MCP |
| `PostToolUse` | `.*` | `hook post-tool-use` | `handle_post_tool_use()` | Fires telemetry to `/v1/telemetry/ingest` |
| `Stop` | — | `hook stop` | `handle_stop()` | Consolidates knowledge + snapshots session to brain |

### Stop hook flow (hook_handlers.rs)

```rust
pub fn handle_stop() {
    // 1. Consolidate local session facts → knowledge.json
    let outcome = consolidate_latest(&project_root, ...);
    // 2. If facts promoted, push to cloud ctx_knowledge
    if outcome.promoted > 0 {
        post_promoted_facts_to_cloud(&project_root);
    }
    // 3. Always snapshot session summary → ctx_brain (PostgreSQL)
    let session = SessionState::load_latest_for_project_root(&project_root);
    cloud_client::post_session_to_brain(&session);
}
```

**Critical bug fixed (7b1997c):** Early `return` on `promoted == 0` was silently skipping brain snapshot for every session with no promoted facts. Brain snapshot now always fires.

### PostToolUse — known token tracking gap

`handle_post_tool_use()` uses `tool_input`/`tool_output` field byte length as token proxy. In practice, many Claude Code / Copilot hook events send minimal metadata rather than full content, so most calls show 0 tokens. Needs field-name investigation to fix.

---

## 13. Analytics Tools — v0.5.6

Four new cloud-only MCP tools + REST endpoint:

| Tool | Actions | Key behaviour |
|------|---------|---------------|
| `ctx_gain` | report/score/tasks/agents/wrapped/json | Activity score = `calls*2 + tokens/1000`. Per-project when `project_id` given, global when empty. |
| `ctx_cost` | report/tools/status/json | $2.50/1M token pricing. Reports cost estimate (not savings). |
| `ctx_heatmap` | status/directory/dirs/cold/json | File-access counts from `TelemetryStore._fileAccessCounts`. Median-based cold detection. |
| `ctx_stats` | report/json | Per-project unified snapshot. |

REST: `GET /api/projects/{projectId}/stats` → always `200` with zeros for unknown projects (not 404).

**InputSchema envelope (mandatory):**
```csharp
public Dictionary<string, object?> InputSchema => new()
{
    ["type"] = "object",
    ["properties"] = new Dictionary<string, object?>
    {
        ["action"] = new Dictionary<string, object?> { ["type"] = "string", ["description"] = "..." },
        ["project_id"] = new Dictionary<string, object?> { ["type"] = "string", ["description"] = "..." },
    },
    ["required"] = new[] { "action" },
};
```

**IngestEvent feeds per-project counters:** `TelemetryStore.IngestEvent` (called by Rust client PostToolUse hook) now updates `_projectCommands` so Rust MCP sessions show up in per-project analytics.

---

## 14. WIP Stash — Tasks A/B (not yet implemented)

```
stash@{0}: On main: wip: Task A/B cloud sync
```

Task C (session-end brain snapshot) is now done via Stop hook. The stash still contains partially-implemented A/B work with compile errors — do not pop until ready to finish those tasks.

- **Task A** — `ctx_knowledge` cloud-only when server configured (`mcp_server/mod.rs` ~line 233)
- **Task B** — autopilot consolidation loop → PostgreSQL bridge (`mcp_server/mod.rs` ~line 499)

---

## 15. Pre-existing Test Failures (not caused by recent changes)

In `client/tests/integration_tests.rs`:
- `help_shows_environment_section`
- `pipe_guard_rust_side_defense_in_depth`

These were failing before this session began. Do not investigate or fix as part of analytics work.

---

## 16. CI/CD

### Release flow
1. Bump all 3 version locations in one commit → push to `main`
2. `auto-release.yml` fires: verifies versions match → creates git tag
3. Tag push triggers `release.yml`:
   - `build`: amd64 + arm64 Rust client binaries
   - `release`: GitHub release + binary assets
   - `publish-crate`: crates.io publish (token: `CARGO_REGISTRY_TOKEN` secret ✅)
   - `publish-server-image`: multi-platform Docker → GHCR

### Required secrets
- `CARGO_REGISTRY_TOKEN` ✅ already set
  - Rotate: https://crates.io/settings/tokens → "Publish new crates" + "Publish updates"

---

## 17. Installed State (This Linux Machine)

| Item | Location | Status |
|------|----------|--------|
| Rust client | `~/.cargo/bin/nebu-ctx` | v0.5.6 |
| Fish shell hook | `~/.nebu-ctx/shell-hook.fish` | active |
| Copilot CLI MCP config | `~/.copilot/mcp-config.json` | wired, all tools auto-approved |
| VS Code MCP config | `~/.config/Code/User/mcp.json` | wired |
| Server container | `nebu-ctx-local` (podman) | running on 3333/4242 (PostgreSQL always on) |
| Token env var | `NEBULA_CTX_HTTP_TOKEN` in `.env` | present |
| Claude Code hooks | Stop + PostToolUse + PreToolUse (Bash + Read) | `.claude/settings.local.json` |

### MCP API call pattern (Rust client uses /v1/tools/call, NOT /mcp)

```bash
TOKEN=$(grep '^NEBULA_CTX_HTTP_TOKEN=' .env | cut -d= -f2)
curl -s -X POST \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  http://127.0.0.1:4242/v1/tools/call \
  -d '{"name":"ctx_brain","arguments":{"action":"recall","query":"test"}}'
```

### Reinstall client after changes
```bash
cargo install --path client --locked --force
```

### Restart server after .NET changes
```bash
podman stop nebu-ctx-local && podman rm nebu-ctx-local
podman build -t nebu-ctx-server -f Dockerfile .
podman run -d --name nebu-ctx-local \
  -p 127.0.0.1:3333:3333 -p 127.0.0.1:4242:4242 \
  --env-file .env nebu-ctx-server
```

---

## 18. C# Coding Standards (this repo)

- No fully-qualified type names — always add `using` directives
- No `dynamic`
- XML docs on ALL methods/classes/records including private and static helpers
- No long parameter lists — 3+ params → DTO/request object  
- Keep parameter lists on one line when they fit
- `switch`/pattern matching over `if/else if` chains for dispatch
- Guard clauses + early returns to keep logic flat
- `[JsonInclude]` on required properties with non-public setters (STJ/OpenAPI compat)
- EF Core: timestamps in `DbContext.SaveChanges()` override, never in business logic
- `static readonly JsonSerializerOptions` field, never `new JsonSerializerOptions()` per call

---

## 19. Rust Coding Standards (this repo)

- Dead code behind `#[cfg(feature = "...")]` or deleted
- Telemetry fires BEFORE `passthrough()` / `process::exit()`
- ureq 3.x: always set `Content-Type: application/json` explicitly for JSON POSTs
- `pub const` for constants accessed from tests (e.g. `CLOUD_ONLY_TOOLS`)
- `2024` edition
- `Value::String(s)` match before printing `serde_json::Value` — `println!("{}", value)` wraps strings in JSON quotes

---

## 20. Known Architecture Debt

1. **ctx_knowledge local fallback** — silently writes to JSON when cloud call fails (Task A pending)
2. **Brain automation gap** — autopilot consolidation writes to local JSON, no bridge to PostgreSQL mid-session (Task B pending)
3. **Token tracking shows 0** — PostToolUse hook may be using wrong field names for token extraction
4. **WIP stash** — Tasks A/B partially implemented, compile errors — do not pop until ready
5. **OpenCode hooks** — zero hooks installed, shell rewrite + Stop not wired

