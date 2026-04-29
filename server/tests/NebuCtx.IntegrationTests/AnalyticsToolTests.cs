namespace NebuCtx.IntegrationTests;

using System.Text.Json;
using NebuCtx.Application;
using NebuCtx.Tools.Gain;

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
}
