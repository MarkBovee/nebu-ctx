namespace NebuCtx.IntegrationTests;

using Microsoft.Extensions.Logging.Abstractions;

using NebuCtx.Contracts.Mcp;
using NebuCtx.Server.Core;
using NebuCtx.Server.Core.Services;
using NebuCtx.Storage;
using NebuCtx.Tools.Brain;
using NebuCtx.Tools.Knowledge;

/// <summary>
/// Integration tests for the memory-browsing list action exposed by
/// <c>ctx_brain list</c> and <c>ctx_knowledge list</c> tool handlers,
/// plus the underlying <c>BrainService.ListAsync</c> /
/// <c>KnowledgeService.ListAsync</c> surface.
/// </summary>
public class MemoryListTests
{
    private static (BrainService Brain, KnowledgeService Knowledge, InMemoryBrainStore BrainStore, InMemoryKnowledgeStore KnowledgeStore, MemoryLifecycleService Lifecycle) CreateServices(string projectId)
    {
        var brainStore = new InMemoryBrainStore();
        var knowledgeStore = new InMemoryKnowledgeStore();
        var sessionStore = new InMemorySessionStore();
        var knowledgeService = new KnowledgeService(knowledgeStore, sessionStore, NullLogger<KnowledgeService>.Instance);
        var brainService = new BrainService(brainStore, knowledgeService, NullLogger<BrainService>.Instance);
        var lifecycle = new MemoryLifecycleService(brainStore, knowledgeStore);
        return (brainService, knowledgeService, brainStore, knowledgeStore, lifecycle);
    }

    private static ToolExecutionContext Ctx(string projectId) => new() { ProjectId = projectId };

    /// <summary>List with no filter returns all entries paginated by the default limit.</summary>
    [Fact]
    public async Task Brain_List_DefaultReturnsAllWithinLimit()
    {
        var (brain, _, _, _, lifecycle) = CreateServices("p1");
        for (var i = 0; i < 5; i++)
        {
            await brain.StoreAsync("p1", $"key-{i}", $"value {i}");
        }
        var handler = new BrainToolHandler(brain, lifecycle);
        var result = await handler.ExecuteAsync(
            new Dictionary<string, object?> { ["action"] = "list" },
            Ctx("p1"),
            CancellationToken.None);
        var typed = Assert.IsType<MemoryListResult<MemoryListItem>>(result);
        Assert.Equal(5, typed.Memories.Count);
        Assert.Equal(5, typed.Total);
        Assert.NotEmpty(typed.FiltersApplied);
    }

    /// <summary>Filter by category trims the result set and echoes the filter back.</summary>
    [Fact]
    public async Task Brain_List_FilterByCategory()
    {
        var (brain, _, _, _, lifecycle) = CreateServices("p1");
        await brain.StoreFactAsync("p1", new BrainEntry
        {
            Key = "finding-1",
            Value = "x",
            Kind = "finding",
            Category = "finding",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        await brain.StoreFactAsync("p1", new BrainEntry
        {
            Key = "decision-1",
            Value = "y",
            Kind = "decision",
            Category = "decision",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        var handler = new BrainToolHandler(brain, lifecycle);
        var result = await handler.ExecuteAsync(
            new Dictionary<string, object?>
            {
                ["action"] = "list",
                ["category"] = "decision",
            },
            Ctx("p1"),
            CancellationToken.None);
        var typed = Assert.IsType<MemoryListResult<MemoryListItem>>(result);
        Assert.Single(typed.Memories);
        Assert.Equal("decision-1", typed.Memories[0].Key);
        Assert.Equal("decision", typed.FiltersApplied["category"]);
    }

    /// <summary>Relative <c>--since</c> shorthand is translated to a created_after filter.</summary>
    [Fact]
    public void Brain_List_SinceFilterApplied()
    {
        var result = BrainToolHandler.BuildListFilter(new Dictionary<string, object?>
        {
            ["since"] = "30d",
        });
        Assert.NotNull(result.CreatedAfter);
        Assert.True(result.CreatedAfter!.Value < DateTimeOffset.UtcNow.AddDays(-29));
    }

    /// <summary>Limit and offset apply on top of the matching set.</summary>
    [Fact]
    public async Task Brain_List_LimitAndOffset()
    {
        var (brain, _, _, _, lifecycle) = CreateServices("p1");
        for (var i = 0; i < 10; i++)
        {
            await brain.StoreAsync("p1", $"k{i:D2}", $"v{i}");
        }
        var handler = new BrainToolHandler(brain, lifecycle);
        var result = await handler.ExecuteAsync(
            new Dictionary<string, object?>
            {
                ["action"] = "list",
                ["limit"] = 3,
                ["offset"] = 2,
            },
            Ctx("p1"),
            CancellationToken.None);
        var typed = Assert.IsType<MemoryListResult<MemoryListItem>>(result);
        Assert.Equal(3, typed.Memories.Count);
        Assert.Equal(10, typed.Total);
    }

    /// <summary>Knowledge list filters by category, source_type, and lifecycle_status.</summary>
    [Fact]
    public async Task Knowledge_List_FilteredAndEchoed()
    {
        var (_, knowledge, _, _, lifecycle) = CreateServices("p1");
        await knowledge.RememberAsync("p1", "root_cause", "k1", "v1", 0.9f, sourceType: "tool_activity");
        await knowledge.RememberAsync("p1", "deployment", "k2", "v2", 0.5f, sourceType: "remember");
        var handler = new KnowledgeToolHandler(knowledge, lifecycle);
        var result = await handler.ExecuteAsync(
            new Dictionary<string, object?>
            {
                ["action"] = "list",
                ["category"] = "root_cause",
                ["source_type"] = "tool_activity",
            },
            Ctx("p1"),
            CancellationToken.None);
        var typed = Assert.IsType<MemoryListResult<MemoryListItem>>(result);
        Assert.Single(typed.Memories);
        Assert.Equal("root_cause:k1", typed.Memories[0].Key);
        Assert.Equal("root_cause", typed.FiltersApplied["category"]);
        Assert.Equal("tool_activity", typed.FiltersApplied["source_type"]);
    }

    /// <summary>Knowledge list honours <c>promoted_from_session</c> filter (memory-correlation).</summary>
    [Fact]
    public async Task Knowledge_List_FilterByPromotedFromSession()
    {
        var (_, knowledge, _, _, lifecycle) = CreateServices("p1");
        await knowledge.RememberAsync("p1", "root_cause", "k1", "v1", 0.9f, sourceScope: "session-abc");
        await knowledge.RememberAsync("p1", "root_cause", "k2", "v2", 0.5f, sourceScope: "session-xyz");
        var handler = new KnowledgeToolHandler(knowledge, lifecycle);
        var result = await handler.ExecuteAsync(
            new Dictionary<string, object?>
            {
                ["action"] = "list",
                ["promoted_from_session"] = "session-abc",
            },
            Ctx("p1"),
            CancellationToken.None);
        var typed = Assert.IsType<MemoryListResult<MemoryListItem>>(result);
        Assert.Single(typed.Memories);
        Assert.Equal("root_cause:k1", typed.Memories[0].Key);
        Assert.Equal("session-abc", typed.FiltersApplied["promoted_from_session"]);
    }

    /// <summary>Empty result set still returns the consistent envelope with count 0.</summary>
    [Fact]
    public async Task Brain_List_EmptyResult()
    {
        var (brain, _, _, _, lifecycle) = CreateServices("p1");
        var handler = new BrainToolHandler(brain, lifecycle);
        var result = await handler.ExecuteAsync(
            new Dictionary<string, object?> { ["action"] = "list" },
            Ctx("p1"),
            CancellationToken.None);
        var typed = Assert.IsType<MemoryListResult<MemoryListItem>>(result);
        Assert.Empty(typed.Memories);
        Assert.Equal(0, typed.Total);
        Assert.Equal(0, typed.Count);
    }

    /// <summary>Unknown action still raises the canonical error to keep backward compatibility.</summary>
    [Fact]
    public async Task Brain_List_UnknownActionRaisesError()
    {
        var (brain, _, _, _, lifecycle) = CreateServices("p1");
        var handler = new BrainToolHandler(brain, lifecycle);
        await Assert.ThrowsAsync<ArgumentException>(async () => await handler.ExecuteAsync(
            new Dictionary<string, object?> { ["action"] = "not-a-real-action" },
            Ctx("p1"),
            CancellationToken.None));
    }

    /// <summary>Recall still works after the list action is added (backward compatibility).</summary>
    [Fact]
    public async Task Brain_Recall_StillWorksAfterListAction()
    {
        var (brain, _, _, _, lifecycle) = CreateServices("p1");
        await brain.StoreAsync("p1", "findme", "needle in haystack");
        var handler = new BrainToolHandler(brain, lifecycle);
        var result = await handler.ExecuteAsync(
            new Dictionary<string, object?>
            {
                ["action"] = "recall",
                ["query"] = "findme",
            },
            Ctx("p1"),
            CancellationToken.None);
        // Recall returns an anonymous type { entries, count }.
        var json = System.Text.Json.JsonSerializer.Serialize(result);
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        Assert.Equal(1, doc.RootElement.GetProperty("count").GetInt32());
        Assert.Equal(1, doc.RootElement.GetProperty("entries").GetArrayLength());
    }
}
