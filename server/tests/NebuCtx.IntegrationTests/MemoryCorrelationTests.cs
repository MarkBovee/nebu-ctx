namespace NebuCtx.IntegrationTests;

using Microsoft.Extensions.Logging.Abstractions;

using NebuCtx.Contracts.Mcp;
using NebuCtx.Server.Core;
using NebuCtx.Server.Core.Services;
using NebuCtx.Storage;
using NebuCtx.Tools.Knowledge;

/// <summary>
/// Integration tests for the cross-domain correlation (memory-correlation)
/// spec: brain session events promoted to knowledge must surface a
/// <c>promotion_trace</c> object on both recall and list responses, and the
/// list/filter must accept <c>--promoted-from-session</c> and
/// <c>--promoted-from-brain-key</c> filters.
/// </summary>
public class MemoryCorrelationTests
{
    private static (KnowledgeService Knowledge, KnowledgeToolHandler Handler) CreateServices()
    {
        var knowledgeStore = new InMemoryKnowledgeStore();
        var sessionStore = new InMemorySessionStore();
        var knowledgeService = new KnowledgeService(knowledgeStore, sessionStore, NullLogger<KnowledgeService>.Instance);
        var handler = new KnowledgeToolHandler(knowledgeService, new MemoryLifecycleService(new InMemoryBrainStore(), knowledgeStore));
        return (knowledgeService, handler);
    }

    private static ToolExecutionContext Ctx(string projectId) => new() { ProjectId = projectId };

    private static IReadOnlyList<KnowledgePromotionItem> BuildConsolidationItems(string sessionId, DateTimeOffset? sourceTimestamp = null)
    {
        var ts = sourceTimestamp ?? DateTimeOffset.UtcNow;
        return
        [
            new KnowledgePromotionItem
            {
                Category = "finding",
                Key = $"{sessionId}-finding-1",
                Value = "pnpm test runner is the only way",
                Confidence = 0.7f,
                SourceType = "consolidate",
                SourceScope = sessionId,
                PromotedFromBrainKey = "finding-1",
                PromotedFromBrainCategory = "finding",
                PromotedFromBrainValue = "pnpm test runner is the only way",
                PromotedFromTimestamp = ts,
                PromotionAction = "consolidation",
                PromotionTimestamp = ts,
            },
            new KnowledgePromotionItem
            {
                Category = "decision",
                Key = $"{sessionId}-decision-1",
                Value = "use postgres for production",
                Confidence = 0.85f,
                SourceType = "consolidate",
                SourceScope = sessionId,
                PromotedFromBrainKey = "decision-1",
                PromotedFromBrainCategory = "decision",
                PromotedFromBrainValue = "use postgres for production",
                PromotedFromTimestamp = ts,
                PromotionAction = "consolidation",
                PromotionTimestamp = ts,
            },
        ];
    }

    /// <summary>Promoted knowledge facts surface a non-null <c>promotion_trace</c> on recall.</summary>
    [Fact]
    public async Task Recall_SurfacesPromotionTrace()
    {
        var (knowledge, handler) = CreateServices();
        var sessionId = "s-42";
        await knowledge.PromoteAsync("p1", BuildConsolidationItems(sessionId), CancellationToken.None);
        var result = await handler.ExecuteAsync(
            new Dictionary<string, object?>
            {
                ["action"] = "recall",
                ["query"] = "pnpm",
            },
            Ctx("p1"),
            CancellationToken.None);
        var json = System.Text.Json.JsonSerializer.Serialize(result);
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var entries = doc.RootElement.GetProperty("entries");
        Assert.Equal(1, entries.GetArrayLength());
        var trace = entries[0].GetProperty("promotion_trace");
        Assert.Equal(sessionId, trace.GetProperty("source_session_id").GetString());
        Assert.Equal("finding-1", trace.GetProperty("source_brain_key").GetString());
        Assert.Equal("finding", trace.GetProperty("source_brain_category").GetString());
        Assert.Equal("consolidation", trace.GetProperty("promotion_action").GetString());
        Assert.True(trace.TryGetProperty("source_timestamp", out _));
        Assert.True(trace.TryGetProperty("promotion_timestamp", out _));
    }

    /// <summary>Direct remember calls do not produce a <c>promotion_trace</c> field.</summary>
    [Fact]
    public async Task Recall_OmitsPromotionTraceForDirectRemember()
    {
        var (knowledge, handler) = CreateServices();
        await knowledge.RememberAsync("p1", "general", "k1", "value", 1.0f, "remember", null, null, CancellationToken.None);
        var result = await handler.ExecuteAsync(
            new Dictionary<string, object?>
            {
                ["action"] = "recall",
                ["query"] = "k1",
            },
            Ctx("p1"),
            CancellationToken.None);
        var json = System.Text.Json.JsonSerializer.Serialize(result);
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var entry = doc.RootElement.GetProperty("entries")[0];
        Assert.Equal(System.Text.Json.JsonValueKind.Null, entry.GetProperty("promotion_trace").ValueKind);
    }

    /// <summary>List filter <c>promoted_from_session</c> returns only facts promoted from that session.</summary>
    [Fact]
    public async Task List_FilterByPromotedFromSession()
    {
        var (knowledge, handler) = CreateServices();
        await knowledge.PromoteAsync("p1", BuildConsolidationItems("session-A"), CancellationToken.None);
        await knowledge.PromoteAsync("p1", BuildConsolidationItems("session-B"), CancellationToken.None);
        var result = await handler.ExecuteAsync(
            new Dictionary<string, object?>
            {
                ["action"] = "list",
                ["promoted_from_session"] = "session-A",
            },
            Ctx("p1"),
            CancellationToken.None);
        var typed = Assert.IsType<MemoryListResult<MemoryListItem>>(result);
        Assert.Equal(2, typed.Total);
        Assert.All(typed.Memories, item =>
        {
            Assert.NotNull(item.PromotionTrace);
            Assert.Equal("session-A", item.PromotionTrace!.SourceSessionId);
        });
    }

    /// <summary>List filter <c>promoted_from_brain_key</c> returns only facts promoted from a specific brain entry.</summary>
    [Fact]
    public async Task List_FilterByPromotedFromBrainKey()
    {
        var (knowledge, handler) = CreateServices();
        var sessionId = "session-X";
        await knowledge.PromoteAsync("p1", BuildConsolidationItems(sessionId), CancellationToken.None);
        var result = await handler.ExecuteAsync(
            new Dictionary<string, object?>
            {
                ["action"] = "list",
                ["promoted_from_brain_key"] = "decision-1",
            },
            Ctx("p1"),
            CancellationToken.None);
        var typed = Assert.IsType<MemoryListResult<MemoryListItem>>(result);
        Assert.Single(typed.Memories);
        Assert.Equal("decision-1", typed.Memories[0].PromotionTrace!.SourceBrainKey);
    }

    /// <summary>Facts promoted from a session are projected with a populated <c>promotion_trace</c> on list responses.</summary>
    [Fact]
    public async Task List_PromotedFactsIncludePromotionTrace()
    {
        var (knowledge, handler) = CreateServices();
        await knowledge.PromoteAsync("p1", BuildConsolidationItems("s-7"), CancellationToken.None);
        var result = await handler.ExecuteAsync(
            new Dictionary<string, object?> { ["action"] = "list" },
            Ctx("p1"),
            CancellationToken.None);
        var typed = Assert.IsType<MemoryListResult<MemoryListItem>>(result);
        Assert.Equal(2, typed.Total);
        Assert.All(typed.Memories, item => Assert.NotNull(item.PromotionTrace));
    }

    /// <summary>Direct remember entries have no <c>promotion_trace</c> on list responses (omitted via WhenWritingNull).</summary>
    [Fact]
    public async Task List_DirectRememberOmitsPromotionTrace()
    {
        var (knowledge, handler) = CreateServices();
        await knowledge.RememberAsync("p1", "general", "k1", "v", 1.0f, "remember", null, null, CancellationToken.None);
        var result = await handler.ExecuteAsync(
            new Dictionary<string, object?> { ["action"] = "list" },
            Ctx("p1"),
            CancellationToken.None);
        var json = System.Text.Json.JsonSerializer.Serialize(result);
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var memory = doc.RootElement.GetProperty("memories")[0];
        Assert.False(memory.TryGetProperty("promotion_trace", out _));
    }

    /// <summary>Backward compat: a direct remember call still returns the canonical recall shape without the trace field having meaningful data.</summary>
    [Fact]
    public async Task Recall_DirectRememberKeepsCanonicalShape()
    {
        var (knowledge, handler) = CreateServices();
        await knowledge.RememberAsync("p1", "general", "alpha", "alpha value", 0.9f, "remember", null, null, CancellationToken.None);
        var result = await handler.ExecuteAsync(
            new Dictionary<string, object?> { ["action"] = "recall", ["query"] = "alpha" },
            Ctx("p1"),
            CancellationToken.None);
        var json = System.Text.Json.JsonSerializer.Serialize(result);
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var entry = doc.RootElement.GetProperty("entries")[0];
        Assert.Equal("alpha", entry.GetProperty("Key").GetString());
        Assert.Equal("alpha value", entry.GetProperty("Value").GetString());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, entry.GetProperty("promotion_trace").ValueKind);
    }

    /// <summary>Multiple sessions do not leak: filtering by session keeps results scoped.</summary>
    [Fact]
    public async Task List_FilterBySessionIsStrictlyScoped()
    {
        var (knowledge, handler) = CreateServices();
        await knowledge.PromoteAsync("p1", BuildConsolidationItems("session-A"), CancellationToken.None);
        await knowledge.PromoteAsync("p1", BuildConsolidationItems("session-B"), CancellationToken.None);
        var result = await handler.ExecuteAsync(
            new Dictionary<string, object?>
            {
                ["action"] = "list",
                ["promoted_from_session"] = "session-A",
            },
            Ctx("p1"),
            CancellationToken.None);
        var typed = Assert.IsType<MemoryListResult<MemoryListItem>>(result);
        Assert.DoesNotContain(typed.Memories, m => m.PromotionTrace?.SourceSessionId == "session-B");
    }
}
