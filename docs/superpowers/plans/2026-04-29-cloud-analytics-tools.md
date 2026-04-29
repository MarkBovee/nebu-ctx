# Cloud Analytics Tools Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Promote `ctx_gain`, `ctx_cost`, `ctx_heatmap`, `ctx_stats` from local stubs to real cloud-backed MCP tool handlers, add per-project stats aggregation to TelemetryStore, and delete the dead CLI stub commands.

**Architecture:** Four new `IToolHandler` implementations on the .NET server are fed by `TelemetryStore`, which gains per-project command counters and file-access tracking. The Rust client adds these tools to `CLOUD_ONLY_TOOLS`, deletes the local dispatch stubs, deletes three dead CLI commands (`cep`, `stats`, `heatmap`), and rewires `gain` to call the server.

**Tech Stack:** .NET 10 / C# 13, xUnit integration tests via `WebApplicationFactory<Program>`, Rust 2024 edition, `serde_json::Map`, `ureq` HTTP client.

---

## File Map

**New files:**
- `server/src/NebuCtx.Tools/Gain/GainToolHandler.cs`
- `server/src/NebuCtx.Tools/Cost/CostToolHandler.cs`
- `server/src/NebuCtx.Tools/Heatmap/HeatmapToolHandler.cs`
- `server/src/NebuCtx.Tools/Stats/StatsToolHandler.cs`
- `server/tests/NebuCtx.IntegrationTests/TelemetryStoreTests.cs`
- `server/tests/NebuCtx.IntegrationTests/AnalyticsToolTests.cs`

**Modified files:**
- `server/src/NebuCtx.Application/TelemetryStore.cs` — per-project counters, file-access map, `PerProject` on `Snapshot`
- `server/src/NebuCtx.Tools/ToolRegistration.cs` — register four new handlers
- `server/src/NebuCtx.Dashboard/DashboardEndpoints.cs` — add `/api/projects/{projectId}/stats`
- `client/src/mcp_server/mod.rs` — add four tools to `CLOUD_ONLY_TOOLS`
- `client/src/mcp_server/dispatch.rs` — delete stub arms for `ctx_gain`, `ctx_cost`, `ctx_heatmap`
- `client/src/main.rs` — delete `cep`/top-level `stats`/`heatmap` arms; rewrite `gain`; rewrite `dashboard`/`watch`
- `client/src/cli/mod.rs` — delete `cmd_stats`; update help text

---

## Task 1: TelemetryStore — per-project counters + file-access tracking

**Files:**
- Modify: `server/src/NebuCtx.Application/TelemetryStore.cs`
- Create: `server/tests/NebuCtx.IntegrationTests/TelemetryStoreTests.cs`

- [ ] **Step 1: Write the failing tests**

Create `server/tests/NebuCtx.IntegrationTests/TelemetryStoreTests.cs`:

```csharp
namespace NebuCtx.IntegrationTests;

using NebuCtx.Application;

/// <summary>
/// Unit tests for per-project aggregation and file-access tracking in TelemetryStore.
/// </summary>
public class TelemetryStoreTests
{
    private static TelemetryStore CreateStore() => new();

    [Fact]
    public void RecordToolCall_PopulatesPerProjectCounters()
    {
        var store = CreateStore();
        var ctxA = new ToolExecutionContext { ProjectId = "proj-a", ProjectRoot = "/a" };
        var ctxB = new ToolExecutionContext { ProjectId = "proj-b", ProjectRoot = "/b" };

        store.RecordToolCall("ctx_read", new Dictionary<string, object?>(), "result-a", ctxA);
        store.RecordToolCall("ctx_read", new Dictionary<string, object?>(), "result-b", ctxB);
        store.RecordToolCall("ctx_edit", new Dictionary<string, object?>(), "result-a2", ctxA);

        var snapshot = store.GetSnapshot();

        Assert.True(snapshot.PerProject.ContainsKey("proj-a"));
        Assert.True(snapshot.PerProject.ContainsKey("proj-b"));
        Assert.Equal(2, snapshot.PerProject["proj-a"].TotalToolCalls);
        Assert.Equal(1, snapshot.PerProject["proj-b"].TotalToolCalls);
        Assert.True(snapshot.PerProject["proj-a"].Commands.ContainsKey("ctx_read"));
        Assert.Equal(1, snapshot.PerProject["proj-b"].Commands["ctx_read"].Count);
    }

    [Fact]
    public void RecordToolCall_TracksFileAccess_ForFileAccessTools()
    {
        var store = CreateStore();
        var ctx = new ToolExecutionContext { ProjectId = "proj-a", ProjectRoot = "/a" };

        store.RecordToolCall("ctx_read",
            new Dictionary<string, object?> { ["path"] = "/a/src/foo.cs" }, "r", ctx);
        store.RecordToolCall("ctx_read",
            new Dictionary<string, object?> { ["path"] = "/a/src/foo.cs" }, "r", ctx);
        store.RecordToolCall("ctx_edit",
            new Dictionary<string, object?> { ["path"] = "/a/src/bar.cs" }, "r", ctx);

        var snapshot = store.GetSnapshot();
        var fa = snapshot.GetFileAccess("proj-a");

        Assert.Equal(2, fa["/a/src/foo.cs"]);
        Assert.Equal(1, fa["/a/src/bar.cs"]);
    }

    [Fact]
    public void RecordToolCall_DoesNotTrackFileAccess_ForNonFileTools()
    {
        var store = CreateStore();
        var ctx = new ToolExecutionContext { ProjectId = "proj-a" };

        store.RecordToolCall("ctx_brain",
            new Dictionary<string, object?> { ["path"] = "/a/src/foo.cs" }, "r", ctx);

        var snapshot = store.GetSnapshot();
        Assert.Empty(snapshot.GetFileAccess("proj-a"));
    }

    [Fact]
    public void RecordToolCall_DoesNotTrackFileAccess_WhenPathMissing()
    {
        var store = CreateStore();
        var ctx = new ToolExecutionContext { ProjectId = "proj-a" };

        store.RecordToolCall("ctx_read", new Dictionary<string, object?>(), "r", ctx);

        var snapshot = store.GetSnapshot();
        Assert.Empty(snapshot.GetFileAccess("proj-a"));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
cd server
dotnet build NebuCtx.slnx -p:AllowMissingPrunePackageData=true 2>&1 | tail -5
dotnet vstest tests/NebuCtx.IntegrationTests/bin/Debug/net10.0/NebuCtx.IntegrationTests.dll \
  --testcasefilter:"FullyQualifiedName~TelemetryStoreTests" \
  --logger:"console;verbosity=detailed" 2>&1 | tail -20
```

Expected: compile error — `PerProject`, `GetFileAccess`, `ProjectTelemetrySnapshot` don't exist yet.

- [ ] **Step 3: Add `ProjectTelemetrySnapshot` and file-access tracking to `TelemetryStore.cs`**

Inside the `TelemetryStore` class, add the static file-access tool set immediately after the `MaxEvents` constant:

```csharp
private static readonly FrozenSet<string> FileAccessTools = FrozenSet.Create(
    StringComparer.OrdinalIgnoreCase,
    "ctx_read", "ctx_edit", "ctx_search", "ctx_outline", "ctx_symbol",
    "ctx_callees", "ctx_callers", "ctx_delta", "ctx_benchmark", "ctx_analyze",
    "ctx_smart_read", "ctx_multi_read");
```

Add two new private fields alongside the existing `_commands` field:

```csharp
private readonly Dictionary<string, Dictionary<string, CommandTelemetrySnapshot>> _projectCommands
    = new(StringComparer.OrdinalIgnoreCase);
private readonly Dictionary<(string ProjectId, string Path), int> _fileAccessCounts = new();
```

Add `ProjectTelemetrySnapshot` as a nested class inside `TelemetryStore`, alongside `CommandTelemetrySnapshot`:

```csharp
/// <summary>
/// Per-project telemetry aggregation entry.
/// </summary>
public sealed class ProjectTelemetrySnapshot
{
    /// <summary>Project identifier.</summary>
    public required string ProjectId { get; init; }

    /// <summary>Total tool calls recorded for this project.</summary>
    public int TotalToolCalls { get; set; }

    /// <summary>Total estimated input tokens for this project.</summary>
    public long TotalInputTokens { get; set; }

    /// <summary>Total estimated output tokens for this project.</summary>
    public long TotalOutputTokens { get; set; }

    /// <summary>Per-command aggregation for this project.</summary>
    public IReadOnlyDictionary<string, CommandTelemetrySnapshot> Commands { get; init; }
        = new Dictionary<string, CommandTelemetrySnapshot>(StringComparer.OrdinalIgnoreCase);
}
```

Add `PerProject` to the existing `Snapshot` class (alongside `Commands`):

```csharp
/// <summary>Per-project telemetry aggregation.</summary>
public IReadOnlyDictionary<string, ProjectTelemetrySnapshot> PerProject { get; init; }
    = new Dictionary<string, ProjectTelemetrySnapshot>(StringComparer.OrdinalIgnoreCase);
```

Add `GetFileAccess` helper to `Snapshot`:

```csharp
/// <summary>Returns file-access counts for a specific project.</summary>
/// <param name="projectId">Project identifier to filter by.</param>
/// <returns>Dictionary mapping file path to access count.</returns>
public IReadOnlyDictionary<string, int> GetFileAccess(string projectId)
    => PerProject.TryGetValue(projectId, out var proj)
        ? proj.FileAccess
        : new Dictionary<string, int>();
```

Add `FileAccess` to `ProjectTelemetrySnapshot`:

```csharp
/// <summary>File-access counts for this project (path → count).</summary>
public IReadOnlyDictionary<string, int> FileAccess { get; init; }
    = new Dictionary<string, int>();
```

Inside `RecordToolCall`, after the `_commands` update block (inside the `lock`), add per-project tracking:

```csharp
// Per-project counters
if (!_projectCommands.TryGetValue(context.ProjectId, out var projCmds))
{
    projCmds = new Dictionary<string, CommandTelemetrySnapshot>(StringComparer.OrdinalIgnoreCase);
    _projectCommands[context.ProjectId] = projCmds;
}
var projCommandEntry = GetOrCreateProjectCommand(projCmds, toolName, source);
projCommandEntry.Count++;
projCommandEntry.InputTokens += inputTokens;
projCommandEntry.OutputTokens += outputTokens;

// File-access tracking
if (FileAccessTools.Contains(toolName)
    && arguments.TryGetValue("path", out var pathArg)
    && pathArg is string filePath
    && !string.IsNullOrWhiteSpace(filePath))
{
    var key = (context.ProjectId, filePath);
    _fileAccessCounts[key] = _fileAccessCounts.TryGetValue(key, out var prev) ? prev + 1 : 1;
}
```

Add the private helper (alongside `GetOrCreateCommand`):

```csharp
/// <summary>Gets or creates a command snapshot within a per-project commands dictionary.</summary>
private static CommandTelemetrySnapshot GetOrCreateProjectCommand(
    Dictionary<string, CommandTelemetrySnapshot> commands, string toolName, string source)
{
    if (!commands.TryGetValue(toolName, out var entry))
    {
        entry = new CommandTelemetrySnapshot { Name = toolName, Source = source };
        commands[toolName] = entry;
    }
    return entry;
}
```

Update `GetSnapshot()` to include `PerProject` in the returned `Snapshot`:

```csharp
PerProject = _projectCommands.ToDictionary(
    kvp => kvp.Key,
    kvp => new ProjectTelemetrySnapshot
    {
        ProjectId = kvp.Key,
        TotalToolCalls = kvp.Value.Values.Sum(c => c.Count),
        TotalInputTokens = kvp.Value.Values.Sum(c => c.InputTokens),
        TotalOutputTokens = kvp.Value.Values.Sum(c => c.OutputTokens),
        Commands = kvp.Value.ToDictionary(
            c => c.Key, c => CloneCommand(c.Value), StringComparer.OrdinalIgnoreCase),
        FileAccess = _fileAccessCounts
            .Where(fa => fa.Key.ProjectId == kvp.Key)
            .ToDictionary(fa => fa.Key.Path, fa => fa.Value),
    },
    StringComparer.OrdinalIgnoreCase),
```

- [ ] **Step 4: Build and run tests**

```bash
cd server
dotnet build NebuCtx.slnx -p:AllowMissingPrunePackageData=true 2>&1 | tail -5
dotnet vstest tests/NebuCtx.IntegrationTests/bin/Debug/net10.0/NebuCtx.IntegrationTests.dll \
  --testcasefilter:"FullyQualifiedName~TelemetryStoreTests" \
  --logger:"console;verbosity=detailed" 2>&1 | tail -20
```

Expected: all 4 tests pass.

- [ ] **Step 5: Commit**

```bash
git add server/src/NebuCtx.Application/TelemetryStore.cs \
        server/tests/NebuCtx.IntegrationTests/TelemetryStoreTests.cs
git commit -m "feat(server): per-project telemetry + file-access tracking in TelemetryStore

Co-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>"
```

---

## Task 2: GainToolHandler

**Files:**
- Create: `server/src/NebuCtx.Tools/Gain/GainToolHandler.cs`
- Create: `server/tests/NebuCtx.IntegrationTests/AnalyticsToolTests.cs`

- [ ] **Step 1: Write the failing test**

Create `server/tests/NebuCtx.IntegrationTests/AnalyticsToolTests.cs`:

```csharp
namespace NebuCtx.IntegrationTests;

using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using NebuCtx.Contracts.Mcp;

/// <summary>
/// Integration tests for cloud analytics tool handlers: ctx_gain, ctx_cost, ctx_heatmap, ctx_stats.
/// </summary>
public class AnalyticsToolTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    /// <summary>Initializes with an in-memory test server.</summary>
    public AnalyticsToolTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.CreateClient();
    }

    [Theory]
    [InlineData("report")]
    [InlineData("score")]
    [InlineData("tasks")]
    [InlineData("agents")]
    [InlineData("wrapped")]
    [InlineData("json")]
    public async Task CtxGain_AllActions_ReturnOk(string action)
    {
        var request = new ToolCallRequest
        {
            Name = "ctx_gain",
            Arguments = new Dictionary<string, object?> { ["action"] = action },
        };
        var response = await _client.PostAsJsonAsync("/v1/tools/call", request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<ToolCallResponse>();
        Assert.NotNull(result?.Result);
    }

    [Fact]
    public async Task CtxGain_InManifest()
    {
        var manifest = await _client.GetFromJsonAsync<ManifestResponse>("/v1/manifest");
        Assert.Contains(manifest!.Tools, t => t.Name == "ctx_gain");
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

```bash
cd server && dotnet build NebuCtx.slnx -p:AllowMissingPrunePackageData=true 2>&1 | tail -5
dotnet vstest tests/NebuCtx.IntegrationTests/bin/Debug/net10.0/NebuCtx.IntegrationTests.dll \
  --testcasefilter:"FullyQualifiedName~AnalyticsToolTests.CtxGain" \
  --logger:"console;verbosity=detailed" 2>&1 | tail -20
```

Expected: tests fail — `ctx_gain` not in manifest / call returns 404.

- [ ] **Step 3: Implement GainToolHandler**

Create `server/src/NebuCtx.Tools/Gain/GainToolHandler.cs`:

```csharp
namespace NebuCtx.Tools.Gain;

using NebuCtx.Application;

/// <summary>
/// Tool handler for ctx_gain — token savings and compression analytics.
/// Reads from TelemetryStore; supports optional per-project filtering via project_id argument.
/// </summary>
public sealed class GainToolHandler(TelemetryStore telemetryStore) : IToolHandler
{
    private const double PricePerMillionTokens = 2.50;

    /// <inheritdoc />
    public string Name => "ctx_gain";

    /// <inheritdoc />
    public string Description => "Token savings and compression analytics. Actions: report, score, tasks, agents, wrapped, json. Optional: project_id to filter by project.";

    /// <inheritdoc />
    public Dictionary<string, object?> InputSchema => new()
    {
        ["type"] = "object",
        ["properties"] = new Dictionary<string, object?>
        {
            ["action"] = new Dictionary<string, object?> { ["type"] = "string",
                ["enum"] = new[] { "report", "score", "tasks", "agents", "wrapped", "json" } },
            ["project_id"] = new Dictionary<string, object?> { ["type"] = "string",
                ["description"] = "Filter results to a specific project (optional)" },
            ["period"] = new Dictionary<string, object?> { ["type"] = "string",
                ["enum"] = new[] { "week", "month", "all" },
                ["description"] = "Time period for wrapped summaries (default: all)" },
            ["limit"] = new Dictionary<string, object?> { ["type"] = "integer",
                ["description"] = "Maximum results for ranked lists (default: 10)" },
        },
        ["required"] = new[] { "action" },
    };

    /// <inheritdoc />
    public Task<object> ExecuteAsync(Dictionary<string, object?> arguments, ToolExecutionContext context, CancellationToken cancellationToken = default)
    {
        var action = arguments.TryGetValue("action", out var a) ? a?.ToString() ?? "report" : "report";
        var projectId = ResolveProjectId(arguments, context);
        var limit = arguments.TryGetValue("limit", out var l) && l is int li ? li : 10;
        var snapshot = telemetryStore.GetSnapshot();
        var result = BuildResult(action, projectId, limit, snapshot);
        return Task.FromResult(result);
    }

    private static string? ResolveProjectId(Dictionary<string, object?> arguments, ToolExecutionContext context)
    {
        if (arguments.TryGetValue("project_id", out var pid) && pid is string s && !string.IsNullOrWhiteSpace(s))
            return s;
        return string.IsNullOrWhiteSpace(context.ProjectId) ? null : context.ProjectId;
    }

    private object BuildResult(string action, string? projectId, int limit, TelemetryStore.Snapshot snapshot)
    {
        var commands = ResolveCommands(projectId, snapshot);
        var totalIn = ResolveInputTokens(projectId, snapshot);
        var totalOut = ResolveOutputTokens(projectId, snapshot);
        var totalSaved = Math.Max(0, totalIn - totalOut);
        var score = totalIn > 0 ? (int)Math.Min(100, Math.Round((double)totalSaved / totalIn * 100)) : 0;

        return action switch
        {
            "score" => (object)new { score, note = projectId != null ? $"Filtered to project: {projectId}" : "Server-wide" },
            "tasks" => BuildTasks(commands, limit),
            "agents" => BuildAgents(projectId, snapshot, limit),
            "json" => BuildJson(snapshot, projectId),
            "wrapped" => BuildWrapped(snapshot, projectId),
            _ => BuildReport(score, totalSaved, totalIn, commands, snapshot, projectId, limit),
        };
    }

    private static object BuildReport(int score, long totalSaved, long totalIn,
        IReadOnlyDictionary<string, TelemetryStore.CommandTelemetrySnapshot> commands,
        TelemetryStore.Snapshot snapshot, string? projectId, int limit)
    {
        return new
        {
            score,
            total_tokens_saved = totalSaved,
            total_input_tokens = totalIn,
            estimated_usd_saved = Math.Round(totalSaved / 1_000_000d * PricePerMillionTokens, 4),
            scope = projectId ?? "server-wide",
            note = totalIn == 0 ? "No data recorded yet." : null,
            top_tools = commands.Values
                .OrderByDescending(c => Math.Max(0, c.InputTokens - c.OutputTokens))
                .Take(limit)
                .Select(c => new { tool = c.Name, tokens_saved = Math.Max(0, c.InputTokens - c.OutputTokens) })
                .ToArray(),
            daily = snapshot.Daily.TakeLast(7).Select(d => new
            {
                date = d.Date,
                tokens_saved = Math.Max(0, d.InputTokens - d.OutputTokens),
            }).ToArray(),
        };
    }

    private static object BuildTasks(
        IReadOnlyDictionary<string, TelemetryStore.CommandTelemetrySnapshot> commands, int limit)
    {
        return new
        {
            tasks = commands.Values
                .OrderByDescending(c => Math.Max(0, c.InputTokens - c.OutputTokens))
                .Take(limit)
                .Select(c => new
                {
                    tool = c.Name,
                    calls = c.Count,
                    tokens_saved = Math.Max(0, c.InputTokens - c.OutputTokens),
                    usd_saved = Math.Round(Math.Max(0, c.InputTokens - c.OutputTokens) / 1_000_000d * PricePerMillionTokens, 4),
                }).ToArray(),
        };
    }

    private static object BuildAgents(string? projectId, TelemetryStore.Snapshot snapshot, int limit)
    {
        var sessions = projectId != null
            ? snapshot.Sessions.Where(s => string.Equals(s.ProjectId, projectId, StringComparison.OrdinalIgnoreCase))
            : snapshot.Sessions;

        return new
        {
            agents = sessions
                .OrderByDescending(s => s.TokensSaved)
                .Take(limit)
                .Select(s => new
                {
                    actor = s.ActorLabel,
                    project_id = s.ProjectId,
                    tokens_saved = s.TokensSaved,
                    tool_calls = s.ToolCalls,
                }).ToArray(),
        };
    }

    private static object BuildWrapped(TelemetryStore.Snapshot snapshot, string? projectId)
    {
        var totalIn = projectId != null && snapshot.PerProject.TryGetValue(projectId, out var proj)
            ? proj.TotalInputTokens : snapshot.TotalInputTokens;
        var totalOut = projectId != null && snapshot.PerProject.TryGetValue(projectId, out var proj2)
            ? proj2.TotalOutputTokens : snapshot.TotalOutputTokens;
        var saved = Math.Max(0, totalIn - totalOut);

        return new
        {
            scope = projectId ?? "server-wide",
            total_tokens_saved = saved,
            total_tool_calls = projectId != null && snapshot.PerProject.TryGetValue(projectId, out var proj3)
                ? proj3.TotalToolCalls : snapshot.TotalToolCalls,
            usd_saved = Math.Round(saved / 1_000_000d * PricePerMillionTokens, 4),
            first_use = snapshot.FirstUse?.ToString("O"),
        };
    }

    private static object BuildJson(TelemetryStore.Snapshot snapshot, string? projectId)
    {
        return projectId != null && snapshot.PerProject.TryGetValue(projectId, out var proj)
            ? (object)proj
            : snapshot;
    }

    private static IReadOnlyDictionary<string, TelemetryStore.CommandTelemetrySnapshot> ResolveCommands(
        string? projectId, TelemetryStore.Snapshot snapshot)
    {
        return projectId != null && snapshot.PerProject.TryGetValue(projectId, out var proj)
            ? proj.Commands
            : snapshot.Commands;
    }

    private static long ResolveInputTokens(string? projectId, TelemetryStore.Snapshot snapshot)
        => projectId != null && snapshot.PerProject.TryGetValue(projectId, out var proj)
            ? proj.TotalInputTokens : snapshot.TotalInputTokens;

    private static long ResolveOutputTokens(string? projectId, TelemetryStore.Snapshot snapshot)
        => projectId != null && snapshot.PerProject.TryGetValue(projectId, out var proj)
            ? proj.TotalOutputTokens : snapshot.TotalOutputTokens;
}
```

- [ ] **Step 4: Register in ToolRegistration.cs**

Add to `server/src/NebuCtx.Tools/ToolRegistration.cs`:

```csharp
// Add using at top:
using NebuCtx.Tools.Gain;

// Add in AddToolHandlers():
services.AddSingleton<IToolHandler, GainToolHandler>();
```

- [ ] **Step 5: Run tests**

```bash
cd server && dotnet build NebuCtx.slnx -p:AllowMissingPrunePackageData=true 2>&1 | tail -5
dotnet vstest tests/NebuCtx.IntegrationTests/bin/Debug/net10.0/NebuCtx.IntegrationTests.dll \
  --testcasefilter:"FullyQualifiedName~AnalyticsToolTests.CtxGain" \
  --logger:"console;verbosity=detailed" 2>&1 | tail -20
```

Expected: all 7 tests pass.

- [ ] **Step 6: Commit**

```bash
git add server/src/NebuCtx.Tools/Gain/GainToolHandler.cs \
        server/src/NebuCtx.Tools/ToolRegistration.cs \
        server/tests/NebuCtx.IntegrationTests/AnalyticsToolTests.cs
git commit -m "feat(server): add ctx_gain tool handler with per-project support

Co-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>"
```

---

## Task 3: CostToolHandler

**Files:**
- Create: `server/src/NebuCtx.Tools/Cost/CostToolHandler.cs`
- Modify: `server/src/NebuCtx.Tools/ToolRegistration.cs`
- Modify: `server/tests/NebuCtx.IntegrationTests/AnalyticsToolTests.cs`

- [ ] **Step 1: Add failing test to `AnalyticsToolTests.cs`**

```csharp
[Theory]
[InlineData("report")]
[InlineData("tools")]
[InlineData("status")]
[InlineData("json")]
public async Task CtxCost_AllActions_ReturnOk(string action)
{
    var request = new ToolCallRequest
    {
        Name = "ctx_cost",
        Arguments = new Dictionary<string, object?> { ["action"] = action },
    };
    var response = await _client.PostAsJsonAsync("/v1/tools/call", request);
    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    var result = await response.Content.ReadFromJsonAsync<ToolCallResponse>();
    Assert.NotNull(result?.Result);
}

[Fact]
public async Task CtxCost_InManifest()
{
    var manifest = await _client.GetFromJsonAsync<ManifestResponse>("/v1/manifest");
    Assert.Contains(manifest!.Tools, t => t.Name == "ctx_cost");
}
```

- [ ] **Step 2: Run to verify failure**

```bash
cd server && dotnet build NebuCtx.slnx -p:AllowMissingPrunePackageData=true 2>&1 | tail -5
dotnet vstest tests/NebuCtx.IntegrationTests/bin/Debug/net10.0/NebuCtx.IntegrationTests.dll \
  --testcasefilter:"FullyQualifiedName~AnalyticsToolTests.CtxCost" \
  --logger:"console;verbosity=detailed" 2>&1 | tail -10
```

Expected: `ctx_cost` not in manifest / 404.

- [ ] **Step 3: Implement CostToolHandler**

Create `server/src/NebuCtx.Tools/Cost/CostToolHandler.cs`:

```csharp
namespace NebuCtx.Tools.Cost;

using NebuCtx.Application;

/// <summary>
/// Tool handler for ctx_cost — USD cost-savings estimation from token telemetry.
/// Actions: report, tools, status, json. Optional project_id filter.
/// </summary>
public sealed class CostToolHandler(TelemetryStore telemetryStore) : IToolHandler
{
    private const double PricePerMillionTokens = 2.50;

    /// <inheritdoc />
    public string Name => "ctx_cost";

    /// <inheritdoc />
    public string Description => "USD cost-savings estimation. Actions: report, tools, status, json. Optional: project_id.";

    /// <inheritdoc />
    public Dictionary<string, object?> InputSchema => new()
    {
        ["type"] = "object",
        ["properties"] = new Dictionary<string, object?>
        {
            ["action"] = new Dictionary<string, object?> { ["type"] = "string",
                ["enum"] = new[] { "report", "tools", "status", "json" } },
            ["project_id"] = new Dictionary<string, object?> { ["type"] = "string",
                ["description"] = "Filter to a specific project (optional)" },
            ["limit"] = new Dictionary<string, object?> { ["type"] = "integer",
                ["description"] = "Maximum results for ranked lists (default: 10)" },
        },
        ["required"] = new[] { "action" },
    };

    /// <inheritdoc />
    public Task<object> ExecuteAsync(Dictionary<string, object?> arguments, ToolExecutionContext context, CancellationToken cancellationToken = default)
    {
        var action = arguments.TryGetValue("action", out var a) ? a?.ToString() ?? "report" : "report";
        var projectId = GetProjectId(arguments, context);
        var limit = arguments.TryGetValue("limit", out var l) && l is int li ? li : 10;
        var snapshot = telemetryStore.GetSnapshot();
        var result = BuildResult(action, projectId, limit, snapshot);
        return Task.FromResult(result);
    }

    private static string? GetProjectId(Dictionary<string, object?> arguments, ToolExecutionContext context)
    {
        if (arguments.TryGetValue("project_id", out var pid) && pid is string s && !string.IsNullOrWhiteSpace(s))
            return s;
        return string.IsNullOrWhiteSpace(context.ProjectId) ? null : context.ProjectId;
    }

    private object BuildResult(string action, string? projectId, int limit, TelemetryStore.Snapshot snapshot)
    {
        var commands = projectId != null && snapshot.PerProject.TryGetValue(projectId, out var proj)
            ? proj.Commands : snapshot.Commands;
        var totalIn = projectId != null && snapshot.PerProject.TryGetValue(projectId, out var proj2)
            ? proj2.TotalInputTokens : snapshot.TotalInputTokens;
        var totalOut = projectId != null && snapshot.PerProject.TryGetValue(projectId, out var proj3)
            ? proj3.TotalOutputTokens : snapshot.TotalOutputTokens;
        var saved = Math.Max(0, totalIn - totalOut);
        var totalUsd = Math.Round(saved / 1_000_000d * PricePerMillionTokens, 4);

        return action switch
        {
            "status" => (object)new
            {
                pricing_model = $"${PricePerMillionTokens:F2} / 1M tokens (Claude Sonnet estimate)",
                first_use = snapshot.FirstUse?.ToString("O"),
                scope = projectId ?? "server-wide",
                total_tool_calls = projectId != null && snapshot.PerProject.TryGetValue(projectId, out var proj4)
                    ? proj4.TotalToolCalls : snapshot.TotalToolCalls,
            },
            "tools" => new
            {
                tools = commands.Values
                    .Select(c => new { tool = c.Name, calls = c.Count, usd_saved = EstimateSavedUsd(c) })
                    .OrderByDescending(t => t.usd_saved)
                    .Take(limit)
                    .ToArray(),
            },
            "json" => (object)new
            {
                scope = projectId ?? "server-wide",
                total_usd_saved = totalUsd,
                total_tokens_saved = saved,
                commands = commands,
            },
            _ => new
            {
                total_usd_saved = totalUsd,
                total_tokens_saved = saved,
                scope = projectId ?? "server-wide",
                note = totalIn == 0 ? "No data recorded yet." : null,
                top_tools = commands.Values
                    .OrderByDescending(c => EstimateSavedUsd(c))
                    .Take(limit)
                    .Select(c => new { tool = c.Name, usd_saved = EstimateSavedUsd(c), tokens_saved = Math.Max(0, c.InputTokens - c.OutputTokens) })
                    .ToArray(),
            },
        };
    }

    private static double EstimateSavedUsd(TelemetryStore.CommandTelemetrySnapshot c)
        => Math.Round(Math.Max(0, c.InputTokens - c.OutputTokens) / 1_000_000d * PricePerMillionTokens, 4);
}
```

- [ ] **Step 4: Register**

Add to `ToolRegistration.cs`:

```csharp
using NebuCtx.Tools.Cost;

// in AddToolHandlers():
services.AddSingleton<IToolHandler, CostToolHandler>();
```

- [ ] **Step 5: Run tests and commit**

```bash
cd server && dotnet build NebuCtx.slnx -p:AllowMissingPrunePackageData=true 2>&1 | tail -5
dotnet vstest tests/NebuCtx.IntegrationTests/bin/Debug/net10.0/NebuCtx.IntegrationTests.dll \
  --testcasefilter:"FullyQualifiedName~AnalyticsToolTests.CtxCost" \
  --logger:"console;verbosity=detailed" 2>&1 | tail -10
```

Expected: all 5 tests pass.

```bash
git add server/src/NebuCtx.Tools/Cost/CostToolHandler.cs \
        server/src/NebuCtx.Tools/ToolRegistration.cs \
        server/tests/NebuCtx.IntegrationTests/AnalyticsToolTests.cs
git commit -m "feat(server): add ctx_cost tool handler

Co-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>"
```

---

## Task 4: HeatmapToolHandler

**Files:**
- Create: `server/src/NebuCtx.Tools/Heatmap/HeatmapToolHandler.cs`
- Modify: `server/src/NebuCtx.Tools/ToolRegistration.cs`
- Modify: `server/tests/NebuCtx.IntegrationTests/AnalyticsToolTests.cs`

- [ ] **Step 1: Add failing tests to `AnalyticsToolTests.cs`**

```csharp
[Theory]
[InlineData("status")]
[InlineData("directory")]
[InlineData("dirs")]
[InlineData("cold")]
[InlineData("json")]
public async Task CtxHeatmap_AllActions_ReturnOk(string action)
{
    var request = new ToolCallRequest
    {
        Name = "ctx_heatmap",
        Arguments = new Dictionary<string, object?> { ["action"] = action },
    };
    var response = await _client.PostAsJsonAsync("/v1/tools/call", request);
    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    var result = await response.Content.ReadFromJsonAsync<ToolCallResponse>();
    Assert.NotNull(result?.Result);
}

[Fact]
public async Task CtxHeatmap_InManifest()
{
    var manifest = await _client.GetFromJsonAsync<ManifestResponse>("/v1/manifest");
    Assert.Contains(manifest!.Tools, t => t.Name == "ctx_heatmap");
}
```

- [ ] **Step 2: Run to verify failure**

```bash
cd server && dotnet build NebuCtx.slnx -p:AllowMissingPrunePackageData=true 2>&1 | tail -5
dotnet vstest tests/NebuCtx.IntegrationTests/bin/Debug/net10.0/NebuCtx.IntegrationTests.dll \
  --testcasefilter:"FullyQualifiedName~AnalyticsToolTests.CtxHeatmap" \
  --logger:"console;verbosity=detailed" 2>&1 | tail -10
```

Expected: fail — `ctx_heatmap` not in manifest.

- [ ] **Step 3: Implement HeatmapToolHandler**

Create `server/src/NebuCtx.Tools/Heatmap/HeatmapToolHandler.cs`:

```csharp
namespace NebuCtx.Tools.Heatmap;

using NebuCtx.Application;

/// <summary>
/// Tool handler for ctx_heatmap — file-access frequency heatmap.
/// Reads from TelemetryStore file-access counters tracked at tool-call time.
/// Actions: status, directory, dirs, cold, json. Optional: project_id, path prefix.
/// </summary>
public sealed class HeatmapToolHandler(TelemetryStore telemetryStore) : IToolHandler
{
    /// <inheritdoc />
    public string Name => "ctx_heatmap";

    /// <inheritdoc />
    public string Description => "File-access frequency heatmap. Actions: status, directory, dirs, cold, json. Optional: project_id, path.";

    /// <inheritdoc />
    public Dictionary<string, object?> InputSchema => new()
    {
        ["type"] = "object",
        ["properties"] = new Dictionary<string, object?>
        {
            ["action"] = new Dictionary<string, object?> { ["type"] = "string",
                ["enum"] = new[] { "status", "directory", "dirs", "cold", "json" } },
            ["project_id"] = new Dictionary<string, object?> { ["type"] = "string",
                ["description"] = "Filter to a specific project (optional)" },
            ["path"] = new Dictionary<string, object?> { ["type"] = "string",
                ["description"] = "Directory prefix filter for 'directory' and 'dirs' actions (optional)" },
        },
        ["required"] = new[] { "action" },
    };

    /// <inheritdoc />
    public Task<object> ExecuteAsync(Dictionary<string, object?> arguments, ToolExecutionContext context, CancellationToken cancellationToken = default)
    {
        var action = arguments.TryGetValue("action", out var a) ? a?.ToString() ?? "status" : "status";
        var projectId = GetProjectId(arguments, context);
        var pathPrefix = arguments.TryGetValue("path", out var p) ? p?.ToString() : null;
        var snapshot = telemetryStore.GetSnapshot();
        var fileAccess = snapshot.GetFileAccess(projectId ?? context.ProjectId);
        var result = BuildResult(action, fileAccess, pathPrefix, projectId);
        return Task.FromResult(result);
    }

    private static string? GetProjectId(Dictionary<string, object?> arguments, ToolExecutionContext context)
    {
        if (arguments.TryGetValue("project_id", out var pid) && pid is string s && !string.IsNullOrWhiteSpace(s))
            return s;
        return string.IsNullOrWhiteSpace(context.ProjectId) ? null : context.ProjectId;
    }

    private static object BuildResult(string action, IReadOnlyDictionary<string, int> fileAccess,
        string? pathPrefix, string? projectId)
    {
        var filtered = pathPrefix != null
            ? fileAccess.Where(kvp => kvp.Key.StartsWith(pathPrefix, StringComparison.OrdinalIgnoreCase))
                        .ToDictionary(kvp => kvp.Key, kvp => kvp.Value)
            : fileAccess;

        return action switch
        {
            "status" => (object)new
            {
                scope = projectId ?? "server-wide",
                total_tracked_files = fileAccess.Count,
                note = fileAccess.Count == 0 ? "No file access data yet. File access is tracked as tools like ctx_read and ctx_edit are used." : null,
                hot_files = fileAccess.OrderByDescending(kvp => kvp.Value)
                    .Take(5)
                    .Select(kvp => new { path = kvp.Key, count = kvp.Value })
                    .ToArray(),
            },
            "directory" => new
            {
                scope = pathPrefix ?? "/",
                files = filtered.OrderByDescending(kvp => kvp.Value)
                    .Select(kvp => new { path = kvp.Key, count = kvp.Value })
                    .ToArray(),
            },
            "dirs" => new
            {
                dirs = filtered
                    .GroupBy(kvp => Path.GetDirectoryName(kvp.Key) ?? "/")
                    .Select(g => new { dir = g.Key, total_accesses = g.Sum(kvp => kvp.Value), file_count = g.Count() })
                    .OrderByDescending(d => d.total_accesses)
                    .ToArray(),
            },
            "cold" => new
            {
                cold_files = fileAccess.Where(kvp => kvp.Value <= 1)
                    .Select(kvp => new { path = kvp.Key, count = kvp.Value })
                    .OrderBy(f => f.path)
                    .ToArray(),
            },
            "json" => (object)new
            {
                scope = projectId ?? "server-wide",
                files = fileAccess.OrderByDescending(kvp => kvp.Value)
                    .ToDictionary(kvp => kvp.Key, kvp => kvp.Value),
            },
            _ => new { error = $"Unknown action '{action}'" },
        };
    }
}
```

- [ ] **Step 4: Register and run tests**

Add to `ToolRegistration.cs`:

```csharp
using NebuCtx.Tools.Heatmap;
// in AddToolHandlers():
services.AddSingleton<IToolHandler, HeatmapToolHandler>();
```

```bash
cd server && dotnet build NebuCtx.slnx -p:AllowMissingPrunePackageData=true 2>&1 | tail -5
dotnet vstest tests/NebuCtx.IntegrationTests/bin/Debug/net10.0/NebuCtx.IntegrationTests.dll \
  --testcasefilter:"FullyQualifiedName~AnalyticsToolTests.CtxHeatmap" \
  --logger:"console;verbosity=detailed" 2>&1 | tail -10
```

Expected: all 6 tests pass.

- [ ] **Step 5: Commit**

```bash
git add server/src/NebuCtx.Tools/Heatmap/HeatmapToolHandler.cs \
        server/src/NebuCtx.Tools/ToolRegistration.cs \
        server/tests/NebuCtx.IntegrationTests/AnalyticsToolTests.cs
git commit -m "feat(server): add ctx_heatmap tool handler

Co-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>"
```

---

## Task 5: StatsToolHandler + dashboard per-project endpoint

**Files:**
- Create: `server/src/NebuCtx.Tools/Stats/StatsToolHandler.cs`
- Modify: `server/src/NebuCtx.Tools/ToolRegistration.cs`
- Modify: `server/src/NebuCtx.Dashboard/DashboardEndpoints.cs`
- Modify: `server/tests/NebuCtx.IntegrationTests/AnalyticsToolTests.cs`

- [ ] **Step 1: Add failing tests**

```csharp
[Theory]
[InlineData("report")]
[InlineData("json")]
public async Task CtxStats_AllActions_ReturnOk(string action)
{
    var request = new ToolCallRequest
    {
        Name = "ctx_stats",
        Arguments = new Dictionary<string, object?> { ["action"] = action },
    };
    var response = await _client.PostAsJsonAsync("/v1/tools/call", request);
    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    var result = await response.Content.ReadFromJsonAsync<ToolCallResponse>();
    Assert.NotNull(result?.Result);
}

[Fact]
public async Task CtxStats_InManifest()
{
    var manifest = await _client.GetFromJsonAsync<ManifestResponse>("/v1/manifest");
    Assert.Contains(manifest!.Tools, t => t.Name == "ctx_stats");
}

[Fact]
public async Task ProjectStats_Endpoint_ReturnsOk()
{
    var response = await _client.GetAsync("/api/projects/any-project-id/stats");
    // Returns 200 with empty stats (project not registered) or populated stats
    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
}
```

- [ ] **Step 2: Run to verify failure**

```bash
cd server && dotnet build NebuCtx.slnx -p:AllowMissingPrunePackageData=true 2>&1 | tail -5
dotnet vstest tests/NebuCtx.IntegrationTests/bin/Debug/net10.0/NebuCtx.IntegrationTests.dll \
  --testcasefilter:"FullyQualifiedName~AnalyticsToolTests.CtxStats|FullyQualifiedName~AnalyticsToolTests.ProjectStats" \
  --logger:"console;verbosity=detailed" 2>&1 | tail -10
```

Expected: fail — `ctx_stats` not in manifest, `/api/projects/*/stats` returns 404.

- [ ] **Step 3: Implement StatsToolHandler**

Create `server/src/NebuCtx.Tools/Stats/StatsToolHandler.cs`:

```csharp
namespace NebuCtx.Tools.Stats;

using NebuCtx.Application;
using NebuCtx.Projects;

/// <summary>
/// Tool handler for ctx_stats — server-wide or per-project usage statistics.
/// Actions: report, json. Optional: project_id.
/// </summary>
public sealed class StatsToolHandler(TelemetryStore telemetryStore, ProjectRegistry projectRegistry) : IToolHandler
{
    /// <inheritdoc />
    public string Name => "ctx_stats";

    /// <inheritdoc />
    public string Description => "Server usage statistics. Actions: report, json. Optional: project_id to scope to one project.";

    /// <inheritdoc />
    public Dictionary<string, object?> InputSchema => new()
    {
        ["type"] = "object",
        ["properties"] = new Dictionary<string, object?>
        {
            ["action"] = new Dictionary<string, object?> { ["type"] = "string",
                ["enum"] = new[] { "report", "json" },
                ["description"] = "Action to perform (default: report)" },
            ["project_id"] = new Dictionary<string, object?> { ["type"] = "string",
                ["description"] = "Filter to a specific project (optional)" },
        },
    };

    /// <inheritdoc />
    public async Task<object> ExecuteAsync(Dictionary<string, object?> arguments, ToolExecutionContext context, CancellationToken cancellationToken = default)
    {
        var action = arguments.TryGetValue("action", out var a) ? a?.ToString() ?? "report" : "report";
        var projectId = GetProjectId(arguments, context);
        var snapshot = telemetryStore.GetSnapshot();
        var projects = await projectRegistry.ListAsync(cancellationToken);

        return action == "json"
            ? BuildJson(snapshot, projectId, projects)
            : BuildReport(snapshot, projectId, projects);
    }

    private static string? GetProjectId(Dictionary<string, object?> arguments, ToolExecutionContext context)
    {
        if (arguments.TryGetValue("project_id", out var pid) && pid is string s && !string.IsNullOrWhiteSpace(s))
            return s;
        return string.IsNullOrWhiteSpace(context.ProjectId) ? null : context.ProjectId;
    }

    private static object BuildReport(TelemetryStore.Snapshot snapshot, string? projectId, IReadOnlyList<NebuCtx.Contracts.Projects.ProjectRecord> projects)
    {
        if (projectId != null)
        {
            snapshot.PerProject.TryGetValue(projectId, out var proj);
            return new
            {
                scope = projectId,
                total_tool_calls = proj?.TotalToolCalls ?? 0,
                total_input_tokens = proj?.TotalInputTokens ?? 0L,
                total_output_tokens = proj?.TotalOutputTokens ?? 0L,
                note = proj == null ? $"No data recorded for project '{projectId}' yet." : null,
                top_tools = proj?.Commands.Values
                    .OrderByDescending(c => c.Count)
                    .Take(5)
                    .Select(c => new { tool = c.Name, calls = c.Count })
                    .ToArray() ?? Array.Empty<object>(),
            };
        }

        return new
        {
            scope = "server-wide",
            total_tool_calls = snapshot.TotalToolCalls,
            total_input_tokens = snapshot.TotalInputTokens,
            total_output_tokens = snapshot.TotalOutputTokens,
            cache_hits = snapshot.CacheHits,
            registered_projects = projects.Count,
            active_projects = snapshot.PerProject.Count,
            first_use = snapshot.FirstUse?.ToString("O"),
            last_updated = snapshot.LastUpdated?.ToString("O"),
            projects = snapshot.PerProject.Values
                .OrderByDescending(p => p.TotalToolCalls)
                .Select(p => new { project_id = p.ProjectId, tool_calls = p.TotalToolCalls })
                .ToArray(),
        };
    }

    private static object BuildJson(TelemetryStore.Snapshot snapshot, string? projectId, IReadOnlyList<NebuCtx.Contracts.Projects.ProjectRecord> projects)
    {
        return projectId != null && snapshot.PerProject.TryGetValue(projectId, out var proj)
            ? (object)proj
            : snapshot;
    }
}
```

- [ ] **Step 4: Register StatsToolHandler**

Add to `ToolRegistration.cs`:

```csharp
using NebuCtx.Tools.Stats;
// in AddToolHandlers():
services.AddSingleton<IToolHandler, StatsToolHandler>();
```

- [ ] **Step 5: Add `/api/projects/{projectId}/stats` endpoint**

In `DashboardEndpoints.cs`, add after the existing `/api/projects` endpoint:

```csharp
app.MapGet("/api/projects/{projectId}/stats", async (
    string projectId,
    TelemetryStore telemetryStore,
    ProjectRegistry projectRegistry,
    ToolRegistry toolRegistry,
    CancellationToken cancellationToken) =>
{
    var projects = await projectRegistry.ListAsync(cancellationToken);
    var snapshot = telemetryStore.GetSnapshot();
    snapshot.PerProject.TryGetValue(projectId, out var projectSnapshot);

    return Results.Ok(new
    {
        project_id = projectId,
        slug = projects.FirstOrDefault(p => p.ProjectId == projectId)?.Slug,
        total_tool_calls = projectSnapshot?.TotalToolCalls ?? 0,
        total_input_tokens = projectSnapshot?.TotalInputTokens ?? 0L,
        total_output_tokens = projectSnapshot?.TotalOutputTokens ?? 0L,
        registered_tool_count = toolRegistry.GetTools().Total,
        note = projectSnapshot == null ? $"No telemetry recorded for project '{projectId}' yet." : null,
        commands = projectSnapshot?.Commands.Values
            .OrderByDescending(c => c.Count)
            .Select(c => new { tool = c.Name, calls = c.Count, input_tokens = c.InputTokens, output_tokens = c.OutputTokens })
            .ToArray() ?? Array.Empty<object>(),
    });
});
```

- [ ] **Step 6: Run all analytics tests**

```bash
cd server && dotnet build NebuCtx.slnx -p:AllowMissingPrunePackageData=true 2>&1 | tail -5
dotnet vstest tests/NebuCtx.IntegrationTests/bin/Debug/net10.0/NebuCtx.IntegrationTests.dll \
  --testcasefilter:"FullyQualifiedName~AnalyticsToolTests|FullyQualifiedName~TelemetryStoreTests" \
  --logger:"console;verbosity=detailed" 2>&1 | tail -20
```

Expected: all tests pass.

- [ ] **Step 7: Commit**

```bash
git add server/src/NebuCtx.Tools/Stats/StatsToolHandler.cs \
        server/src/NebuCtx.Tools/ToolRegistration.cs \
        server/src/NebuCtx.Dashboard/DashboardEndpoints.cs \
        server/tests/NebuCtx.IntegrationTests/AnalyticsToolTests.cs
git commit -m "feat(server): add ctx_stats handler + /api/projects/{id}/stats endpoint

Co-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>"
```

---

## Task 6: Rust client — CLOUD_ONLY_TOOLS + delete dispatch stubs

**Files:**
- Modify: `client/src/mcp_server/mod.rs`
- Modify: `client/src/mcp_server/dispatch.rs`

- [ ] **Step 1: Write the failing test**

In `client/tests/integration_tests.rs` (or create `client/tests/cloud_routing_tests.rs`), add:

```rust
#[test]
fn analytics_tools_are_cloud_only() {
    use lean_ctx::mcp_server::{CLOUD_ONLY_TOOLS};
    assert!(CLOUD_ONLY_TOOLS.contains(&"ctx_gain"));
    assert!(CLOUD_ONLY_TOOLS.contains(&"ctx_cost"));
    assert!(CLOUD_ONLY_TOOLS.contains(&"ctx_heatmap"));
    assert!(CLOUD_ONLY_TOOLS.contains(&"ctx_stats"));
}
```

Note: `CLOUD_ONLY_TOOLS` is currently `const` (not pub). You will need to expose it or change to `pub const` in the implementation step.

- [ ] **Step 2: Run to verify it fails**

```bash
cargo test --manifest-path client/Cargo.toml analytics_tools_are_cloud_only 2>&1 | tail -10
```

Expected: compile error — `CLOUD_ONLY_TOOLS` not accessible.

- [ ] **Step 3: Add tools to CLOUD_ONLY_TOOLS in `mod.rs`**

In `client/src/mcp_server/mod.rs`, change the constant:

```rust
pub const CLOUD_ONLY_TOOLS: &[&str] = &[
    "ctx_brain",
    "ctx_routes",
    "ctx_gain",
    "ctx_cost",
    "ctx_heatmap",
    "ctx_stats",
];
```

(Change `const` to `pub const` so the test can access it.)

- [ ] **Step 4: Delete stub dispatch arms in `dispatch.rs`**

Delete the three stub arms from `client/src/mcp_server/dispatch.rs`. They look like:

```rust
"ctx_cost" => {
    let action = get_str(args, "action").unwrap_or_else(|| "report".to_string());
    let result = crate::cli::cloud_analytics_only_message(&format!("ctx_cost ({action})"));
    self.record_call("ctx_cost", 0, 0, Some(action)).await;
    result
}
```

```rust
"ctx_gain" => {
    let action = get_str(args, "action").unwrap_or_else(|| "status".to_string());
    let result = crate::cli::cloud_analytics_only_message(&format!("ctx_gain ({action})"));
    self.record_call("ctx_gain", 0, 0, Some(action)).await;
    result
}
```

```rust
"ctx_heatmap" => {
    let action = get_str(args, "action").unwrap_or_else(|| "status".to_string());
    let result = crate::cli::cloud_analytics_only_message(&format!("ctx_heatmap ({action})"));
    self.record_call("ctx_heatmap", 0, 0, Some(action)).await;
    result
}
```

Delete all three blocks. (`ctx_stats` was never in dispatch — nothing to delete for it.)

- [ ] **Step 5: Build and run tests**

```bash
cargo test --manifest-path client/Cargo.toml analytics_tools_are_cloud_only 2>&1 | tail -5
cargo test --manifest-path client/Cargo.toml --lib 2>&1 | grep -E 'FAILED|passed|error' | tail -5
```

Expected: `analytics_tools_are_cloud_only` passes; no new errors.

- [ ] **Step 6: Commit**

```bash
git add client/src/mcp_server/mod.rs client/src/mcp_server/dispatch.rs
git commit -m "feat(client): add gain/cost/heatmap/stats to CLOUD_ONLY_TOOLS; delete local stubs

Co-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>"
```

---

## Task 7: Rust client — CLI cleanup + `gain` rewrite

**Files:**
- Modify: `client/src/main.rs`
- Modify: `client/src/cli/mod.rs`

- [ ] **Step 1: Write the failing test**

```rust
#[test]
fn cep_command_removed_from_cli() {
    // If cep were still a command, this test verifies it's gone.
    // Since we can't easily test CLI dispatch in unit tests without main(),
    // this serves as a documentation marker. The real validation is the build
    // succeeding without the cep/stats/heatmap arms.
    assert!(!std::env::args().any(|a| a == "cep")); // trivially true in test context
}
```

Instead, the real test for this task is that `cargo build` succeeds with no warnings and no dead-code paths.

- [ ] **Step 2: Delete the `exit_cloud_analytics_only` guard and dead CLI commands in `main.rs`**

In `client/src/main.rs`, remove line 28:

```rust
// DELETE this entire block:
if matches!(command, "gain" | "cep" | "dashboard" | "watch" | "heatmap" | "stats") {
    cli::exit_cloud_analytics_only(command);
}
```

Delete the `"cep"` match arm (around line 243):

```rust
// DELETE:
"cep" => {
    println!("{}", tools::ctx_gain::handle("score", None, None, Some(10)));
    return;
}
```

Delete the top-level `"stats"` match arm that calls `cli::cmd_stats` (around line 664 — verify it is the top-level stats command, not `gotchas stats` or `buddy stats`):

```rust
// DELETE:
"stats" => {
    cli::cmd_stats(&rest);
    return;
}
```

Delete the top-level `"heatmap"` match arm if one exists in the outer match (search for it — it may only be in the guard, not as a match arm).

- [ ] **Step 3: Rewrite the `gain` match arm**

Replace the entire existing `"gain" => { ... }` block (the large block from ~line 127 to ~line 240) with:

```rust
"gain" => {
    if rest.iter().any(|a| a == "--reset") {
        core::stats::reset_all();
        println!("Stats reset. All token savings data cleared.");
        return;
    }
    let action = if rest.iter().any(|a| a == "--score") { "score" }
        else if rest.iter().any(|a| a == "--tasks") { "tasks" }
        else if rest.iter().any(|a| a == "--agents") { "agents" }
        else if rest.iter().any(|a| a == "--wrapped") { "wrapped" }
        else if rest.iter().any(|a| a == "--json") { "json" }
        else { "report" };
    match crate::cloud_client::ServerClient::load() {
        Ok(client) => {
            let ctx = crate::git_context::discover_project_context(
                &std::env::current_dir().unwrap_or_default(),
            );
            let mut args = serde_json::Map::new();
            args.insert("action".to_string(), serde_json::json!(action));
            match client.call_tool("ctx_gain", args, &ctx) {
                Ok(result) => println!(
                    "{}",
                    serde_json::to_string_pretty(&result).unwrap_or_default()
                ),
                Err(e) => eprintln!("ctx_gain: {e}"),
            }
        }
        Err(_) => eprintln!(
            "ctx_gain requires a connected server. Run `nebu-ctx setup` to connect one."
        ),
    }
    return;
}
```

- [ ] **Step 4: Rewrite `dashboard` and `watch` arms**

Find and replace the `"dashboard"` and `"watch"` match arms. They currently call `exit_cloud_analytics_only`. Replace each with:

```rust
"dashboard" | "watch" => {
    let url = crate::cloud_client::config::load_connection()
        .ok()
        .flatten()
        .map(|c| {
            // Replace MCP port 4242 with dashboard port 3333
            c.endpoint
                .replace(":4242", ":3333")
                .trim_end_matches("/v1")
                .trim_end_matches('/')
                .to_string()
        })
        .unwrap_or_else(|| "http://localhost:3333".to_string());
    println!("Open your nebu-ctx dashboard at: {url}");
    return;
}
```

- [ ] **Step 5: Delete `cmd_stats` from `cli/mod.rs` and update help text**

In `client/src/cli/mod.rs`, delete the `cmd_stats` function:

```rust
// DELETE:
pub fn cmd_stats(args: &[String]) {
    let _ = args;
    exit_cloud_analytics_only("stats");
}
```

In the help text string (around line 879), replace:

```
    gain|cep|watch|dashboard|heatmap|stats  Cloud-only analytics surfaces (not served locally)
```

with:

```
    gain [--score|--tasks|--json]  Token savings summary (requires connected server)
    dashboard|watch                Open the nebu-ctx dashboard (browser required)
```

- [ ] **Step 6: Build and verify**

```bash
cargo build --manifest-path client/Cargo.toml 2>&1 | grep -E 'error|warning.*unused|Finished'
cargo test --manifest-path client/Cargo.toml --lib 2>&1 | grep -E 'FAILED|passed|error' | tail -5
```

Expected: builds with no errors, no new dead-code warnings.

- [ ] **Step 7: Commit**

```bash
git add client/src/main.rs client/src/cli/mod.rs
git commit -m "feat(client): delete cep/stats/heatmap stubs; rewrite gain to call server

Co-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>"
```

---

## Task 8: Full server test suite + install

- [ ] **Step 1: Run the complete server test suite**

```bash
cd server
dotnet build NebuCtx.slnx -p:AllowMissingPrunePackageData=true 2>&1 | tail -5
dotnet vstest tests/NebuCtx.IntegrationTests/bin/Debug/net10.0/NebuCtx.IntegrationTests.dll \
             tests/NebuCtx.ContractTests/bin/Debug/net10.0/NebuCtx.ContractTests.dll \
             tests/NebuCtx.ProjectIdentityTests/bin/Debug/net10.0/NebuCtx.ProjectIdentityTests.dll \
  --logger:"console;verbosity=detailed" 2>&1 | tail -30
```

Expected: all tests pass. Fix any regressions before continuing.

- [ ] **Step 2: Rebuild the server container**

```bash
cd /mnt/work/Projects/Personal/nebu-ctx
podman build -t nebu-ctx-server -f Dockerfile . 2>&1 | tail -10
```

Expected: build succeeds.

- [ ] **Step 3: Restart the local server**

```bash
podman stop nebu-ctx-local 2>/dev/null; podman rm nebu-ctx-local 2>/dev/null
podman run -d --name nebu-ctx-local \
  -p 127.0.0.1:3333:3333 -p 127.0.0.1:4242:4242 \
  --env-file .env \
  nebu-ctx-server
sleep 3
curl -s http://127.0.0.1:4242/health
```

Expected: `{"status":"ok"}`.

- [ ] **Step 4: Smoke-test each new tool via MCP HTTP**

```bash
TOKEN=$(grep '^NEBULA_CTX_HTTP_TOKEN=' .env | cut -d= -f2)

# ctx_gain
curl -s -X POST http://127.0.0.1:4242/v1/tools/call \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"name":"ctx_gain","arguments":{"action":"report"}}' | jq .result.scope

# ctx_cost
curl -s -X POST http://127.0.0.1:4242/v1/tools/call \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"name":"ctx_cost","arguments":{"action":"status"}}' | jq .result.pricing_model

# ctx_heatmap
curl -s -X POST http://127.0.0.1:4242/v1/tools/call \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"name":"ctx_heatmap","arguments":{"action":"status"}}' | jq .result.total_tracked_files

# ctx_stats
curl -s -X POST http://127.0.0.1:4242/v1/tools/call \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"name":"ctx_stats","arguments":{"action":"report"}}' | jq .result.scope

# /api/projects/{id}/stats
curl -s http://127.0.0.1:3333/api/projects/test-project/stats \
  -H "Authorization: Bearer $TOKEN" | jq .project_id
```

Expected: each command returns non-null JSON with the expected field.

- [ ] **Step 5: Rebuild and install the Rust client**

```bash
cargo install --path client/ 2>&1 | tail -5
nebu-ctx gain --score
```

Expected: `nebu-ctx gain --score` calls the server and prints a JSON score object.

- [ ] **Step 6: Commit and version bump**

All three version locations must be bumped together (e.g., `0.5.5` → `0.5.6`):

```bash
# 1. client/Cargo.toml — change version = "0.5.5" to "0.5.6"
# 2. homeassistant/config.yaml — change version: "0.5.5" to "0.5.6"
# 3. server/src/NebuCtx.Application/ToolRegistry.cs — change ServerVersion.Current to "0.5.6"

git add client/Cargo.toml homeassistant/config.yaml \
        server/src/NebuCtx.Application/ToolRegistry.cs
git commit -m "chore: bump version to 0.5.6

Co-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>"
```

---

## Testing Section

This section is the complete verification checklist for this feature. Run it after all tasks are done. Every item must pass before the branch is merged.

### 1. Unit tests — TelemetryStore

```bash
cd server
dotnet vstest tests/NebuCtx.IntegrationTests/bin/Debug/net10.0/NebuCtx.IntegrationTests.dll \
  --testcasefilter:"FullyQualifiedName~TelemetryStoreTests" \
  --logger:"console;verbosity=detailed"
```

| Test | Verifies |
|------|---------|
| `RecordToolCall_PopulatesPerProjectCounters` | Two different projects accumulate separately |
| `RecordToolCall_TracksFileAccess_ForFileAccessTools` | ctx_read/ctx_edit increment per-path counter |
| `RecordToolCall_DoesNotTrackFileAccess_ForNonFileTools` | ctx_brain does not affect file-access map |
| `RecordToolCall_DoesNotTrackFileAccess_WhenPathMissing` | Missing `path` arg does not panic or add empty key |

### 2. Integration tests — analytics tool handlers

```bash
dotnet vstest tests/NebuCtx.IntegrationTests/bin/Debug/net10.0/NebuCtx.IntegrationTests.dll \
  --testcasefilter:"FullyQualifiedName~AnalyticsToolTests" \
  --logger:"console;verbosity=detailed"
```

| Test | Verifies |
|------|---------|
| `CtxGain_AllActions_ReturnOk` (×6 actions) | report/score/tasks/agents/wrapped/json all return 200 |
| `CtxGain_InManifest` | ctx_gain appears in `/v1/manifest` |
| `CtxCost_AllActions_ReturnOk` (×4 actions) | report/tools/status/json all return 200 |
| `CtxCost_InManifest` | ctx_cost appears in `/v1/manifest` |
| `CtxHeatmap_AllActions_ReturnOk` (×5 actions) | status/directory/dirs/cold/json all return 200 |
| `CtxHeatmap_InManifest` | ctx_heatmap appears in `/v1/manifest` |
| `CtxStats_AllActions_ReturnOk` (×2 actions) | report/json return 200 |
| `CtxStats_InManifest` | ctx_stats appears in `/v1/manifest` |
| `ProjectStats_Endpoint_ReturnsOk` | `/api/projects/any-id/stats` returns 200 |

### 3. Full server test suite

```bash
dotnet vstest tests/NebuCtx.IntegrationTests/bin/Debug/net10.0/NebuCtx.IntegrationTests.dll \
             tests/NebuCtx.ContractTests/bin/Debug/net10.0/NebuCtx.ContractTests.dll \
             tests/NebuCtx.ProjectIdentityTests/bin/Debug/net10.0/NebuCtx.ProjectIdentityTests.dll \
  --logger:"console;verbosity=detailed" 2>&1 | tail -10
```

Expected: zero failures.

### 4. Rust client tests

```bash
cargo test --manifest-path client/Cargo.toml 2>&1 | grep -E 'FAILED|passed|test result'
```

Expected: `analytics_tools_are_cloud_only` passes; no regressions. Pre-existing known failures (`help_shows_environment_section`, `pipe_guard_rust_side_defense_in_depth`) are acceptable if they were already failing before this branch.

### 5. CLOUD_ONLY_TOOLS correctness

Manually verify in `client/src/mcp_server/mod.rs`:

```
CLOUD_ONLY_TOOLS must contain: ctx_brain, ctx_routes, ctx_gain, ctx_cost, ctx_heatmap, ctx_stats
CLOUD_PREFERRED_TOOLS must contain: ctx_knowledge, ctx_session
No tool should appear in both lists.
```

### 6. Per-project filtering — end-to-end

With the server running, call `ctx_stats report` from two different project directories and verify the `active_projects` count changes:

```bash
TOKEN=$(grep '^NEBULA_CTX_HTTP_TOKEN=' .env | cut -d= -f2)

# Ingest an event for a fake project to populate telemetry
curl -s -X POST http://127.0.0.1:4242/v1/telemetry/ingest \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"tool_name":"ctx_read","tokens_original":1000,"tokens_saved":500,"session_id":"test"}' 

# Check stats for the test project
curl -s -X POST http://127.0.0.1:4242/v1/tools/call \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"name":"ctx_stats","arguments":{"action":"report"}}' | jq '.result.active_projects'
```

Expected: returns a number ≥ 1 after ingesting.

### 7. Deleted CLI commands no longer exist

```bash
nebu-ctx cep 2>&1 | head -3     # Expected: "Unknown command: cep" or usage error, NOT "cloud-only" message
nebu-ctx stats 2>&1 | head -3   # Expected: same (deleted or unknown)
nebu-ctx heatmap 2>&1 | head -3 # Expected: same
nebu-ctx gain --score            # Expected: JSON from server OR "requires connected server" message
nebu-ctx dashboard               # Expected: "Open your nebu-ctx dashboard at: http://..."
nebu-ctx watch                   # Expected: same as dashboard
```

### 8. Dashboard per-project endpoint

```bash
TOKEN=$(grep '^NEBULA_CTX_HTTP_TOKEN=' .env | cut -d= -f2)
curl -s http://127.0.0.1:3333/api/projects/any-id/stats \
  -H "Authorization: Bearer $TOKEN" | jq '{project_id, total_tool_calls, note}'
```

Expected: `{"project_id": "any-id", "total_tool_calls": 0, "note": "No telemetry recorded..."}`.

### 9. No regressions in existing tools

```bash
TOKEN=$(grep '^NEBULA_CTX_HTTP_TOKEN=' .env | cut -d= -f2)
curl -s -X POST http://127.0.0.1:4242/v1/tools/call \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"name":"ctx_brain","arguments":{"action":"status"}}' | jq .result
```

Expected: returns brain status (not error).
