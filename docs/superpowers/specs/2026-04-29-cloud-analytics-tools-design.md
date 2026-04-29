# Cloud Analytics Tools — Design Spec

**Date:** 2026-04-29  
**Status:** Approved (pending user sign-off)  
**Scope:** All analytics stubs → real cloud-backed MCP tools + per-project stats for multi-project server

---

## Problem

### Stub inventory

All of the following currently return "go look at the dashboard" and produce zero value for LLM agents or CLI users:

**CLI-level stubs** (`exit_cloud_analytics_only` fires immediately at `main.rs:28`):

| Command | Intent |
|---------|--------|
| `nebu-ctx gain` | Token savings summary |
| `nebu-ctx cep` | Context-efficiency panel (= gain score) |
| `nebu-ctx heatmap` | File-access heatmap |
| `nebu-ctx stats` | Server-wide stats |
| `nebu-ctx dashboard` | Open the web dashboard |
| `nebu-ctx watch` | Live dashboard watch mode |

**MCP tool stubs** (return a message string, never hit the server):

| Tool | Intent |
|------|--------|
| `ctx_gain` | Token savings analytics for LLM agents |
| `ctx_cost` | USD cost-savings estimation |
| `ctx_heatmap` | File-access frequency heatmap |

### Multi-project gap

The server is shared across multiple repositories (one server, many projects). Today:
- All telemetry counters are **global** — `CommandTelemetrySnapshot` has no `ProjectId`.
- Dashboard shows total stats only; no per-project breakdown.
- `ctx_gain`, `ctx_cost`, `ctx_heatmap` have no `project_id` filter parameter.
- LLM agents working in project A can't ask "how much did I save in this project today?"

---

## Approach

1. **MCP tools** — add `ctx_gain`, `ctx_cost`, `ctx_heatmap`, `ctx_stats` as real `IToolHandler` implementations on the .NET server (same pattern as `ctx_brain`). Add all four to `CLOUD_ONLY_TOOLS` in the Rust client. **Remove** the local dispatch arms for these four from `dispatch.rs` — they will never reach local code once they are cloud-only.
2. **CLI cleanup** — the old CLI commands are replaced by the server. Most are **deleted**. `gain` is kept as the one useful terminal shortcut (calls `ctx_gain report` via cloud client). See the table below.
3. **Per-project stats** — add per-project aggregation to `TelemetryStore` so all analytics tools can filter by `project_id`. Add a `/api/projects/{projectId}/stats` dashboard endpoint.

### CLI command disposition

| Command | Disposition | Reason |
|---------|-------------|--------|
| `nebu-ctx gain` | **Keep** — wire to `ctx_gain report` via cloud | Most useful one-shot terminal shortcut |
| `nebu-ctx cep` | **Delete** | Redundant — just `gain --score`; no user value |
| `nebu-ctx stats` | **Delete** | Replaced by `ctx_stats` MCP tool and the dashboard |
| `nebu-ctx heatmap` | **Delete** | Replaced by `ctx_heatmap` MCP tool and the dashboard |
| `nebu-ctx dashboard` | **Keep** — print server URL | Needs a browser; informational only |
| `nebu-ctx watch` | **Keep** — print server URL | Needs a browser; informational only |

The `exit_cloud_analytics_only` guard at `main.rs:28` is removed entirely. `gain` gets a proper cloud-call implementation; `cep`/`stats`/`heatmap` match arms are deleted; `dashboard`/`watch` print the server URL.

---

## Architecture

### Rust client changes

1. **`client/src/mcp_server/mod.rs`** — add `"ctx_gain"`, `"ctx_cost"`, `"ctx_heatmap"`, `"ctx_stats"` to `CLOUD_ONLY_TOOLS`.
2. **`client/src/mcp_server/dispatch.rs`** — **delete** the local stub dispatch arms for these four tools entirely. The CLOUD_ONLY routing handles them before dispatch is reached; no stub needed.
3. **`client/src/main.rs`**:
   - Remove the `exit_cloud_analytics_only` guard at line 28.
   - **Delete** `cep`, top-level `stats`, and top-level `heatmap` match arms entirely.
   - Keep `gain` but rewrite it to call `ctx_gain report` on the cloud server via `ServerClient::call_tool()` and print the result. Preserve `--reset` (clears local stats) and `--json` as pass-through flags.
   - Keep `dashboard` and `watch` but replace the error exit with a helpful URL message: `"Open your nebu-ctx dashboard at http://localhost:3333"` (reading the configured server address from `server_connection.json`).
4. **`client/src/cli/mod.rs`** — delete `cmd_stats`. Update help text: remove the stale `gain|cep|watch|dashboard|heatmap|stats  Cloud-only analytics surfaces` entry; replace with `gain [--json]   Token savings summary (requires server)`.

### .NET server — new tool handlers

Each is a thin class implementing `IToolHandler`, registered in `ToolRegistration.cs`.

#### `GainToolHandler` (`ctx_gain`)

**Parameters:** `action` (required), `project_id` (optional), `period` (optional: `week|month|all`), `limit` (optional, default 10)  
**Data source:** `TelemetryStore.GetSnapshot(projectId?)`

| Action | Returns |
|--------|---------|
| `report` | Overall score, per-tool token-savings breakdown, daily trend |
| `score` | Single integer 0–100 |
| `tasks` | Ranked list of tools by tokens saved |
| `agents` | Per-agent/session savings breakdown |
| `wrapped` | Period summary (week/month/all) |
| `json` | Raw snapshot |

#### `CostToolHandler` (`ctx_cost`)

**Parameters:** `action` (required), `project_id` (optional), `limit` (optional)  
**Data source:** `TelemetryStore.GetSnapshot(projectId?)` + `EstimateSavedCost()`

| Action | Returns |
|--------|---------|
| `report` | Total USD saved, cost per tool |
| `tools` | Ranked breakdown by estimated cost saved |
| `status` | Pricing model + first-use date |
| `json` | Raw snapshot |

Pricing constant: `$2.50 / 1M tokens` (matches existing `EstimateSavedCost`).

#### `HeatmapToolHandler` (`ctx_heatmap`)

**Parameters:** `action` (required), `project_id` (optional), `path` (optional directory prefix)  
**Data source:** new `_fileAccessCounts` on `TelemetryStore`

| Action | Returns |
|--------|---------|
| `status` | Total tracked files, top-5 hot files |
| `directory` | File access counts under the given path prefix |
| `dirs` | Directory-level aggregated access counts |
| `cold` | Files accessed ≤ once |
| `json` | Full raw file-access map |

**File-access tracking:** In `TelemetryStore.RecordToolCall`, when `toolName` ∈ {`ctx_read`, `ctx_edit`, `ctx_search`, `ctx_outline`, `ctx_symbol`, `ctx_callees`, `ctx_callers`, `ctx_delta`, `ctx_benchmark`, `ctx_analyze`, `ctx_smart_read`, `ctx_multi_read`} and `arguments` contains a non-empty `path` key, increment `_fileAccessCounts[(projectId, path)]`.  
Storage: `Dictionary<(string ProjectId, string Path), int>` — in-memory, same lifecycle as other telemetry. Postgres persistence is a follow-up.

#### `StatsToolHandler` (`ctx_stats`)

**Parameters:** `project_id` (optional), `action` (optional: `report|json`, default `report`)  
**Data source:** `TelemetryStore.GetSnapshot()` + `ProjectRegistry`

| Action | Returns |
|--------|---------|
| `report` | Tool call counts, tokens in/out, cache hits, first use, registered projects |
| `json` | Raw stats JSON |

### .NET server — per-project aggregation

**`TelemetryStore` changes:**

Add a second commands dictionary keyed by project:
```csharp
private readonly Dictionary<string, Dictionary<string, CommandTelemetrySnapshot>> _projectCommands = new(StringComparer.OrdinalIgnoreCase);
```

In `RecordToolCall`: after updating global `_commands`, also update `_projectCommands[projectId][toolName]`.

Add a new `ProjectTelemetrySnapshot` record:
```csharp
public sealed class ProjectTelemetrySnapshot {
    public required string ProjectId { get; init; }
    public int TotalToolCalls { get; set; }
    public long TotalInputTokens { get; set; }
    public long TotalOutputTokens { get; set; }
    public IReadOnlyDictionary<string, CommandTelemetrySnapshot> Commands { get; init; }
}
```

`Snapshot` gains:
```csharp
public IReadOnlyDictionary<string, ProjectTelemetrySnapshot> PerProject { get; init; }
```

`GetSnapshot(string? projectId = null)` — when `projectId` is provided, returns a filtered view (just that project's commands + sessions). Global counters remain unchanged.

**New dashboard endpoint:**
```
GET /api/projects/{projectId}/stats
```
Returns `BuildStatsPayload` filtered to that project via `GetSnapshot(projectId)`.

---

## Data Flow

```
nebu-ctx gain / ctx_gain (LLM or CLI)
        │
        ▼
Rust client — CLOUD_ONLY_TOOLS routing
        │
        ▼
.NET MCP server /v1/tool-call
        │
        ▼
GainToolHandler (accepts optional project_id)
        │
        ▼
TelemetryStore.GetSnapshot(projectId?)
    ├─ Global snapshot  →  server-wide totals
    └─ Per-project view →  filtered by ProjectId
        │
        ▼
JSON result returned to agent / CLI
```

---

## Error Handling

- **No server configured** → client returns: "nebu-ctx {tool} requires a connected server. Run `nebu-ctx setup` to connect one."
- **Server unreachable** → existing cloud routing error propagates.
- **Unknown project_id** → handler returns empty payload with `note: "no data for project '{id}'"`.
- **No telemetry data yet** → graceful empty payloads with `note: "no data recorded yet"`.

---

## What Is NOT in Scope

- Heatmap Postgres persistence (in-memory only for now)
- Custom/configurable pricing models (hardcoded `$2.50/M`)
- `nebu-ctx watch` and `nebu-ctx dashboard` as fully functional CLI commands (they require a browser; keep as informational messages pointing to `http://localhost:3333`)
- Cross-project comparison views (single-project filter only; aggregation is global)

---

## Testing

- Unit tests for each handler: mock `TelemetryStore.Snapshot` with seeded data, assert correct action dispatch
- Integration test: call each tool via MCP HTTP, assert non-error JSON response
- Per-project test: ingest two events for different projects, call `ctx_gain project_id=A`, assert only project-A data returned
- Client-side: `CLOUD_ONLY_TOOLS` contains all four; CLI commands call server and print output

---

## Files Changed

**Rust client — deletions:**
- `client/src/mcp_server/dispatch.rs` — delete stub dispatch arms for `ctx_gain`, `ctx_cost`, `ctx_heatmap`, `ctx_stats`
- `client/src/main.rs` — delete `cep`, top-level `stats`, top-level `heatmap` match arms; delete line-28 `exit_cloud_analytics_only` guard for these
- `client/src/cli/mod.rs` — delete `cmd_stats` function; update help text

**Rust client — modifications:**
- `client/src/mcp_server/mod.rs` — add `ctx_gain`, `ctx_cost`, `ctx_heatmap`, `ctx_stats` to `CLOUD_ONLY_TOOLS`
- `client/src/main.rs` — rewrite `gain` arm to call cloud `ctx_gain report`; rewrite `dashboard`/`watch` to print server URL

**.NET server — new files:**
- `server/src/NebuCtx.Tools/Gain/GainToolHandler.cs`
- `server/src/NebuCtx.Tools/Cost/CostToolHandler.cs`
- `server/src/NebuCtx.Tools/Heatmap/HeatmapToolHandler.cs`
- `server/src/NebuCtx.Tools/Stats/StatsToolHandler.cs`

**.NET server — modifications:**
- `server/src/NebuCtx.Tools/ToolRegistration.cs` — register all four
- `server/src/NebuCtx.Application/TelemetryStore.cs` — per-project counters, `_fileAccessCounts`, updated `GetSnapshot(projectId?)`
- `server/src/NebuCtx.Dashboard/DashboardEndpoints.cs` — add `/api/projects/{projectId}/stats`
- `server/tests/NebuCtx.IntegrationTests/McpEndpointTests.cs` — tests for all four tools + per-project filtering
