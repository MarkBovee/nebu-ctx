namespace NebuCtx.IntegrationTests;

using System.Text.Json;
using NebuCtx.Application;
using NebuCtx.Tools.Cost;
using NebuCtx.Tools.Gain;
using NebuCtx.Tools.Heatmap;
using NebuCtx.Tools.Stats;

/// <summary>
/// Integration tests for analytics tool handlers: ctx_gain, ctx_cost, ctx_heatmap, ctx_stats.
/// Tests use direct handler instantiation (no WebApplicationFactory needed for pure logic tests).
/// </summary>
public class AnalyticsToolTests
{
    /// <summary>Creates a TelemetryStore pre-populated with known tool calls across two projects and two actors.</summary>
    private static TelemetryStore CreatePopulatedStore()
    {
        var store = new TelemetryStore();
        var ctxA = new ToolExecutionContext { ProjectId = "proj-a", ProjectRoot = "/a", ActorLabel = "copilot" };
        var ctxB = new ToolExecutionContext { ProjectId = "proj-b", ProjectRoot = "/b", ActorLabel = "claude" };

        for (var i = 0; i < 5; i++)
            store.RecordToolCall("ctx_read", new Dictionary<string, object?> { ["path"] = $"/a/file{i}.cs" }, "r", ctxA);
        for (var i = 0; i < 3; i++)
            store.RecordToolCall("ctx_edit", new Dictionary<string, object?> { ["path"] = $"/b/file{i}.cs" }, "r", ctxB);

        return store;
    }

    // ── ctx_gain ──────────────────────────────────────────────────────────────

    /// <summary>Report action should include all tool names recorded in the store.</summary>
    [Fact]
    public async Task CtxGain_Report_ReturnsMarkdownWithToolStats()
    {
        var handler = new GainToolHandler(CreatePopulatedStore());
        var result = await handler.ExecuteAsync(
            new Dictionary<string, object?> { ["action"] = "report" },
            new ToolExecutionContext { ProjectId = "" },
            CancellationToken.None);

        var text = Assert.IsType<string>(result);
        Assert.Contains("ctx_read", text);
        Assert.Contains("ctx_edit", text);
    }

    /// <summary>Score action should return a line containing a numeric score.</summary>
    [Fact]
    public async Task CtxGain_Score_ReturnsNumericScore()
    {
        var handler = new GainToolHandler(CreatePopulatedStore());
        var result = await handler.ExecuteAsync(
            new Dictionary<string, object?> { ["action"] = "score" },
            new ToolExecutionContext { ProjectId = "" },
            CancellationToken.None);

        var text = Assert.IsType<string>(result);
        Assert.Contains("score", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("16/100", text);
    }

    /// <summary>Json action should return valid JSON with a total_tool_calls property.</summary>
    [Fact]
    public async Task CtxGain_Json_ReturnsDeserializableJson()
    {
        var handler = new GainToolHandler(CreatePopulatedStore());
        var result = await handler.ExecuteAsync(
            new Dictionary<string, object?> { ["action"] = "json" },
            new ToolExecutionContext { ProjectId = "" },
            CancellationToken.None);

        var text = Assert.IsType<string>(result);
        var doc = JsonDocument.Parse(text);
        Assert.True(doc.RootElement.TryGetProperty("total_tool_calls", out _));
    }

    /// <summary>Providing project_id should filter results to only that project's calls.</summary>
    [Fact]
    public async Task CtxGain_FiltersByProjectId_WhenProvided()
    {
        var handler = new GainToolHandler(CreatePopulatedStore());
        var result = await handler.ExecuteAsync(
            new Dictionary<string, object?> { ["action"] = "json", ["project_id"] = "proj-a" },
            new ToolExecutionContext { ProjectId = "" },
            CancellationToken.None);

        var text = Assert.IsType<string>(result);
        var doc = JsonDocument.Parse(text);
        Assert.True(doc.RootElement.TryGetProperty("total_tool_calls", out var calls));
        Assert.Equal(5, calls.GetInt32());
    }

    /// <summary>Agents action should list all actor labels present in the store.</summary>
    [Fact]
    public async Task CtxGain_Agents_ListsAgentBreakdown()
    {
        var handler = new GainToolHandler(CreatePopulatedStore());
        var result = await handler.ExecuteAsync(
            new Dictionary<string, object?> { ["action"] = "agents" },
            new ToolExecutionContext { ProjectId = "" },
            CancellationToken.None);

        var text = Assert.IsType<string>(result);
        Assert.Contains("copilot", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("claude", text, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>An unrecognised action should fall back to the default report instead of throwing.</summary>
    [Fact]
    public async Task CtxGain_UnknownAction_ReturnsFallbackReport()
    {
        var handler = new GainToolHandler(CreatePopulatedStore());
        var result = await handler.ExecuteAsync(
            new Dictionary<string, object?> { ["action"] = "nonexistent_action" },
            new ToolExecutionContext { ProjectId = "" },
            CancellationToken.None);

        var text = Assert.IsType<string>(result);
        Assert.Contains("## Context Gain Report", text);
        Assert.Contains("ctx_read", text);
    }

    /// <summary>Tasks action should bucket ctx_read into Read and ctx_edit into Write categories.</summary>
    [Fact]
    public async Task CtxGain_Tasks_BucketsToolsByCategory()
    {
        var handler = new GainToolHandler(CreatePopulatedStore());
        var result = await handler.ExecuteAsync(
            new Dictionary<string, object?> { ["action"] = "tasks" },
            new ToolExecutionContext { ProjectId = "" },
            CancellationToken.None);

        var text = Assert.IsType<string>(result);
        Assert.Contains("## Task Breakdown", text);
        // ctx_read contains "read" → Read bucket; ctx_edit contains "edit" → Write bucket
        Assert.Contains("Read", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Write", text, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Wrapped action should report the most-used tool (ctx_read with 5 calls vs ctx_edit with 3).</summary>
    [Fact]
    public async Task CtxGain_Wrapped_ReportsMostUsedTool()
    {
        var handler = new GainToolHandler(CreatePopulatedStore());
        var result = await handler.ExecuteAsync(
            new Dictionary<string, object?> { ["action"] = "wrapped" },
            new ToolExecutionContext { ProjectId = "" },
            CancellationToken.None);

        var text = Assert.IsType<string>(result);
        Assert.Contains("## nebu-ctx Wrapped", text);
        Assert.Contains("ctx_read", text); // most-used (5 calls vs 3)
    }

    // ── ctx_cost ──────────────────────────────────────────────────────────────

    /// <summary>Report action should return markdown containing a cost estimate.</summary>
    [Fact]
    public async Task CtxCost_Report_ReturnsMarkdownWithCostEstimate()
    {
        var handler = new CostToolHandler(CreatePopulatedStore());
        var result = await handler.ExecuteAsync(
            new Dictionary<string, object?> { ["action"] = "report" },
            new ToolExecutionContext { ProjectId = "" },
            CancellationToken.None);

        var text = Assert.IsType<string>(result);
        Assert.Contains("Cost", text, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Json action should return valid JSON with estimated_cost_usd and total_tokens properties.</summary>
    [Fact]
    public async Task CtxCost_Json_ReturnsDeserializableJson()
    {
        var handler = new CostToolHandler(CreatePopulatedStore());
        var result = await handler.ExecuteAsync(
            new Dictionary<string, object?> { ["action"] = "json" },
            new ToolExecutionContext { ProjectId = "" },
            CancellationToken.None);

        var text = Assert.IsType<string>(result);
        var doc = JsonDocument.Parse(text);
        Assert.True(doc.RootElement.TryGetProperty("estimated_cost_usd", out _));
        Assert.True(doc.RootElement.TryGetProperty("total_tokens", out _));
    }

    /// <summary>Json action with project_id should filter results to only that project's tools and return zero cost for unknown projects.</summary>
    [Fact]
    public async Task CtxCost_FiltersByProjectId_WhenProvided()
    {
        var handler = new CostToolHandler(CreatePopulatedStore());
        var resultA = await handler.ExecuteAsync(
            new Dictionary<string, object?> { ["action"] = "json", ["project_id"] = "proj-a" },
            new ToolExecutionContext { ProjectId = "" },
            CancellationToken.None);

        var docA = JsonDocument.Parse(Assert.IsType<string>(resultA));
        Assert.Equal("proj-a", docA.RootElement.GetProperty("project_id").GetString());
        // proj-a has only ctx_read (5 calls), proj-b has only ctx_edit — verify filtering
        var tools = docA.RootElement.GetProperty("tools").EnumerateArray().Select(t => t.GetProperty("name").GetString()).ToList();
        Assert.Contains("ctx_read", tools);
        Assert.DoesNotContain("ctx_edit", tools);

        // Unknown project_id must return zero cost, not global stats
        var resultUnknown = await handler.ExecuteAsync(
            new Dictionary<string, object?> { ["action"] = "json", ["project_id"] = "no-such-project" },
            new ToolExecutionContext { ProjectId = "" },
            CancellationToken.None);

        var docUnknown = JsonDocument.Parse(Assert.IsType<string>(resultUnknown));
        Assert.Equal(0, docUnknown.RootElement.GetProperty("total_tokens").GetInt64());
    }

    /// <summary>Status action should return a one-line summary containing the word "token".</summary>
    [Fact]
    public async Task CtxCost_Status_ReturnsStatusSummary()
    {
        var handler = new CostToolHandler(CreatePopulatedStore());
        var result = await handler.ExecuteAsync(
            new Dictionary<string, object?> { ["action"] = "status" },
            new ToolExecutionContext { ProjectId = "" },
            CancellationToken.None);

        var text = Assert.IsType<string>(result);
        Assert.Contains("token", text, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Tools action should list per-tool cost breakdown including ctx_read.</summary>
    [Fact]
    public async Task CtxCost_Tools_ReturnsPerToolBreakdown()
    {
        var handler = new CostToolHandler(CreatePopulatedStore());
        var result = await handler.ExecuteAsync(
            new Dictionary<string, object?> { ["action"] = "tools" },
            new ToolExecutionContext { ProjectId = "" },
            CancellationToken.None);

        var text = Assert.IsType<string>(result);
        Assert.Contains("ctx_read", text);
    }

    // ── ctx_heatmap ───────────────────────────────────────────────────────────

    /// <summary>Status action should return a summary mentioning tracked files.</summary>
    [Fact]
    public async Task CtxHeatmap_Status_ReturnsFileAccessSummary()
    {
        var handler = new HeatmapToolHandler(CreatePopulatedStore());
        var result = await handler.ExecuteAsync(
            new Dictionary<string, object?> { ["action"] = "status" },
            new ToolExecutionContext { ProjectId = "" },
            CancellationToken.None);

        var text = Assert.IsType<string>(result);
        Assert.Contains("file", text, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Json action should return valid JSON with a files array property.</summary>
    [Fact]
    public async Task CtxHeatmap_Json_ReturnsDeserializableJsonWithFiles()
    {
        var handler = new HeatmapToolHandler(CreatePopulatedStore());
        var result = await handler.ExecuteAsync(
            new Dictionary<string, object?> { ["action"] = "json" },
            new ToolExecutionContext { ProjectId = "" },
            CancellationToken.None);

        var text = Assert.IsType<string>(result);
        var doc = JsonDocument.Parse(text);
        Assert.True(doc.RootElement.TryGetProperty("files", out _));
    }

    /// <summary>Directory action should return a markdown heading for hot directories.</summary>
    [Fact]
    public async Task CtxHeatmap_Directory_ReturnsTopDirectories()
    {
        var handler = new HeatmapToolHandler(CreatePopulatedStore());
        var result = await handler.ExecuteAsync(
            new Dictionary<string, object?> { ["action"] = "directory" },
            new ToolExecutionContext { ProjectId = "" },
            CancellationToken.None);

        var text = Assert.IsType<string>(result);
        Assert.Contains("##", text); // has a heading
    }

    /// <summary>Json action filtered to proj-a should return exactly 5 files (file0..file4).</summary>
    [Fact]
    public async Task CtxHeatmap_FiltersByProjectId_WhenProvided()
    {
        var handler = new HeatmapToolHandler(CreatePopulatedStore());
        var result = await handler.ExecuteAsync(
            new Dictionary<string, object?> { ["action"] = "json", ["project_id"] = "proj-a" },
            new ToolExecutionContext { ProjectId = "" },
            CancellationToken.None);

        var text = Assert.IsType<string>(result);
        var doc = JsonDocument.Parse(text);
        Assert.True(doc.RootElement.TryGetProperty("project_id", out var pid));
        Assert.Equal("proj-a", pid.GetString());
        // proj-a has 5 file accesses (file0..file4), proj-b has 3
        Assert.True(doc.RootElement.TryGetProperty("files", out var files));
        Assert.Equal(5, files.GetArrayLength());
    }

    /// <summary>Cold action should return a string result without throwing.</summary>
    [Fact]
    public async Task CtxHeatmap_Cold_ReturnsUnaccessed()
    {
        var handler = new HeatmapToolHandler(CreatePopulatedStore());
        var result = await handler.ExecuteAsync(
            new Dictionary<string, object?> { ["action"] = "cold" },
            new ToolExecutionContext { ProjectId = "" },
            CancellationToken.None);

        Assert.IsType<string>(result);
    }

    // ── ctx_stats ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task CtxStats_Report_ReturnsMarkdownWithProjectBreakdown()
    {
        var handler = new StatsToolHandler(CreatePopulatedStore());
        var result = await handler.ExecuteAsync(
            new Dictionary<string, object?> { ["action"] = "report" },
            new ToolExecutionContext { ProjectId = "" },
            CancellationToken.None);

        var text = Assert.IsType<string>(result);
        Assert.Contains("proj-a", text);
        Assert.Contains("proj-b", text);
    }

    [Fact]
    public async Task CtxStats_Json_ReturnsAllProjects()
    {
        var handler = new StatsToolHandler(CreatePopulatedStore());
        var result = await handler.ExecuteAsync(
            new Dictionary<string, object?> { ["action"] = "json" },
            new ToolExecutionContext { ProjectId = "" },
            CancellationToken.None);

        var text = Assert.IsType<string>(result);
        var doc = JsonDocument.Parse(text);
        Assert.True(doc.RootElement.TryGetProperty("projects", out var projects));
        Assert.Equal(2, projects.GetArrayLength());
    }

    [Fact]
    public async Task CtxStats_FiltersByProjectId_WhenProvided()
    {
        var handler = new StatsToolHandler(CreatePopulatedStore());
        var result = await handler.ExecuteAsync(
            new Dictionary<string, object?> { ["action"] = "json", ["project_id"] = "proj-a" },
            new ToolExecutionContext { ProjectId = "" },
            CancellationToken.None);

        var text = Assert.IsType<string>(result);
        var doc = JsonDocument.Parse(text);
        // Single-project filter → projects array has 1 entry
        Assert.True(doc.RootElement.TryGetProperty("projects", out var projects));
        Assert.Equal(1, projects.GetArrayLength());
        Assert.Equal("proj-a", projects[0].GetProperty("project_id").GetString());
        Assert.Equal(5, projects[0].GetProperty("total_tool_calls").GetInt32());
    }

    [Fact]
    public async Task CtxStats_UnknownProjectId_ReturnsEmptyProjects()
    {
        var handler = new StatsToolHandler(CreatePopulatedStore());
        var result = await handler.ExecuteAsync(
            new Dictionary<string, object?> { ["action"] = "json", ["project_id"] = "no-such" },
            new ToolExecutionContext { ProjectId = "" },
            CancellationToken.None);

        var text = Assert.IsType<string>(result);
        var doc = JsonDocument.Parse(text);
        Assert.Equal(0, doc.RootElement.GetProperty("projects").GetArrayLength());
    }
}
