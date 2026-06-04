namespace NebuCtx.IntegrationTests;

using Microsoft.Extensions.Logging.Abstractions;

using NebuCtx.Contracts.Mcp;
using NebuCtx.Server.Core.Services;
using NebuCtx.Storage;

/// <summary>
/// Integration tests for the lifecycle subcommands exposed by
/// <c>ctx_brain lifecycle</c> and <c>ctx_knowledge lifecycle</c>, plus
/// the underlying <c>MemoryLifecycleService</c> surface.
/// </summary>
public class MemoryLifecycleTests
{
    private static (InMemoryBrainStore BrainStore, InMemoryKnowledgeStore KnowledgeStore, MemoryLifecycleService Lifecycle) CreateServices()
    {
        var brainStore = new InMemoryBrainStore();
        var knowledgeStore = new InMemoryKnowledgeStore();
        var lifecycle = new MemoryLifecycleService(brainStore, knowledgeStore);
        return (brainStore, knowledgeStore, lifecycle);
    }

    private static BrainEntry BuildBrain(string projectId, string key, string category, float confidence, DateTimeOffset updatedAt, string status = "current")
    {
        return new BrainEntry
        {
            ProjectId = projectId,
            Key = key,
            Value = $"value for {key}",
            Kind = "fact",
            Category = category,
            CreatedAt = updatedAt,
            UpdatedAt = updatedAt,
            Confidence = confidence,
            LifecycleStatus = status,
        };
    }

    private static KnowledgeEntry BuildKnowledge(string projectId, string category, string key, float confidence, DateTimeOffset updatedAt, int retrievalCount = 0, int confirmationCount = 1, DateTimeOffset? lastRetrievedAt = null, float lifecycleScore = 0f, string status = "current")
    {
        return new KnowledgeEntry
        {
            ProjectId = projectId,
            Category = category,
            Key = key,
            Value = $"value for {category}:{key}",
            Confidence = confidence,
            CreatedAt = updatedAt,
            UpdatedAt = updatedAt,
            LastRetrievedAt = lastRetrievedAt,
            RetrievalCount = retrievalCount,
            ConfirmationCount = confirmationCount,
            LifecycleScore = lifecycleScore,
            LifecycleStatus = status,
        };
    }

    /// <summary>Brain stats counts current, stale, and archived buckets plus score distribution.</summary>
    [Fact]
    public async Task Brain_Stats_GroupsByStatusAndScoreBucket()
    {
        var (brain, _, lifecycle) = CreateServices();
        var now = DateTimeOffset.UtcNow;
        await brain.StoreFactAsync(BuildBrain("p1", "k1", "a", 0.9f, now));
        await brain.StoreFactAsync(BuildBrain("p1", "k2", "a", 0.6f, now, status: "stale"));
        await brain.StoreFactAsync(BuildBrain("p1", "k3", "b", 0.2f, now, status: "archived"));
        var stats = await lifecycle.BrainStatsAsync("p1");
        Assert.Equal(3, stats["total_memories"]);
        var counts = Assert.IsType<Dictionary<string, int>>(stats["status_counts"]);
        Assert.Equal(1, counts["current"]);
        Assert.Equal(1, counts["stale"]);
        Assert.Equal(1, counts["archived"]);
        var distribution = Assert.IsType<Dictionary<string, int>>(stats["score_distribution"]);
        Assert.NotEmpty(distribution);
    }

    /// <summary>Knowledge stats includes averages and the same status breakdown.</summary>
    [Fact]
    public async Task Knowledge_Stats_GroupsByStatusAndScoreBucket()
    {
        var (_, knowledge, lifecycle) = CreateServices();
        var now = DateTimeOffset.UtcNow;
        await knowledge.UpsertFactAsync(BuildKnowledge("p1", "arch", "k1", 0.9f, now, retrievalCount: 5));
        await knowledge.UpsertFactAsync(BuildKnowledge("p1", "arch", "k2", 0.4f, now, status: "stale"));
        var stats = await lifecycle.KnowledgeStatsAsync("p1");
        Assert.Equal(2, stats["total_memories"]);
        var counts = Assert.IsType<Dictionary<string, int>>(stats["status_counts"]);
        Assert.Equal(1, counts["current"]);
        Assert.Equal(1, counts["stale"]);
        var averages = Assert.IsType<Dictionary<string, Dictionary<string, object?>>>(stats["status_averages"]);
        Assert.True(averages.ContainsKey("current"));
        Assert.True(averages.ContainsKey("stale"));
        Assert.Contains("avg_lifecycle_score", averages["current"].Keys);
    }

    /// <summary>Brain promotions only surface current, high-confidence, recently-updated facts.</summary>
    [Fact]
    public async Task Brain_Promotions_FiltersToHighConfidenceCurrent()
    {
        var (brain, _, lifecycle) = CreateServices();
        var now = DateTimeOffset.UtcNow;
        await brain.StoreFactAsync(BuildBrain("p1", "fresh-strong", "x", 0.95f, now));
        await brain.StoreFactAsync(BuildBrain("p1", "weak", "x", 0.30f, now));
        await brain.StoreFactAsync(BuildBrain("p1", "stale-status", "x", 0.95f, now, status: "stale"));
        await brain.StoreFactAsync(BuildBrain("p1", "old-but-strong", "x", 0.95f, now.AddDays(-90)));
        var result = await lifecycle.BrainPromotionCandidatesAsync("p1", new MemoryListFilter());
        Assert.Equal(1, result["count"]);
        Assert.Equal(1, result["eligible_total"]);
        Assert.Equal("brain", result["type"]);
    }

    /// <summary>Knowledge promotions respect lifecycle score and confirm the type marker.</summary>
    [Fact]
    public async Task Knowledge_Promotions_FiltersByLifecycleScore()
    {
        var (_, knowledge, lifecycle) = CreateServices();
        var now = DateTimeOffset.UtcNow;
        await knowledge.UpsertFactAsync(BuildKnowledge("p1", "a", "high", 0.95f, now, lifecycleScore: 1.2f));
        await knowledge.UpsertFactAsync(BuildKnowledge("p1", "a", "low", 0.50f, now, lifecycleScore: 0.4f));
        var result = await lifecycle.KnowledgePromotionCandidatesAsync("p1", new MemoryListFilter());
        Assert.Equal(1, result["count"]);
        Assert.Equal(1, result["eligible_total"]);
        Assert.Equal("knowledge", result["type"]);
        Assert.Equal(1.0f, (float)result["threshold_used"]!);
    }

    /// <summary>Brain stale window trims to entries that have not been touched for the configured days.</summary>
    [Fact]
    public async Task Brain_Stale_RespectsDaysWindow()
    {
        var (brain, _, lifecycle) = CreateServices();
        var now = DateTimeOffset.UtcNow;
        await brain.StoreFactAsync(BuildBrain("p1", "fresh", "x", 0.9f, now));
        await brain.StoreFactAsync(BuildBrain("p1", "old", "x", 0.9f, now.AddDays(-60)));
        var result = await lifecycle.BrainStaleAsync("p1", 30, new MemoryListFilter());
        Assert.Equal(1, result["count"]);
        Assert.Equal(30, result["days_threshold_used"]);
    }

    /// <summary>Knowledge stale uses last-retrieved-at when available.</summary>
    [Fact]
    public async Task Knowledge_Stale_UsesLastRetrievedAt()
    {
        var (_, knowledge, lifecycle) = CreateServices();
        var now = DateTimeOffset.UtcNow;
        await knowledge.UpsertFactAsync(BuildKnowledge("p1", "x", "recently-used", 0.9f, now.AddDays(-30), lastRetrievedAt: now));
        await knowledge.UpsertFactAsync(BuildKnowledge("p1", "x", "abandoned", 0.9f, now.AddDays(-60), lastRetrievedAt: now.AddDays(-60)));
        var result = await lifecycle.KnowledgeStaleAsync("p1", 30, new MemoryListFilter());
        Assert.Equal(1, result["count"]);
    }

    /// <summary>Brain scoring returns null when the key is unknown.</summary>
    [Fact]
    public async Task Brain_Scoring_ReturnsNullForUnknownKey()
    {
        var (brain, _, lifecycle) = CreateServices();
        var result = await lifecycle.BrainScoringAsync("p1", "missing");
        Assert.Null(result);
    }

    /// <summary>Brain scoring returns a deterministic payload with factors, thresholds, and a recommendation.</summary>
    [Fact]
    public async Task Brain_Scoring_ReturnsFactorBreakdown()
    {
        var (brain, _, lifecycle) = CreateServices();
        var now = DateTimeOffset.UtcNow;
        await brain.StoreFactAsync(BuildBrain("p1", "scored", "x", 0.92f, now));
        var result = await lifecycle.BrainScoringAsync("p1", "scored");
        Assert.NotNull(result);
        Assert.Equal("scored", result!["key"]);
        Assert.Contains("factors", result.Keys);
        Assert.Contains("thresholds", result.Keys);
        Assert.Equal("promote", (string)result["recommendation"]!);
    }

    /// <summary>Knowledge scoring returns null when the category/key is unknown.</summary>
    [Fact]
    public async Task Knowledge_Scoring_ReturnsNullForUnknownEntry()
    {
        var (_, knowledge, lifecycle) = CreateServices();
        var result = await lifecycle.KnowledgeScoringAsync("p1", "missing", "nope");
        Assert.Null(result);
    }

    /// <summary>Knowledge scoring promotes the entry when the score crosses the threshold.</summary>
    [Fact]
    public async Task Knowledge_Scoring_PromotesWhenAboveThreshold()
    {
        var (_, knowledge, lifecycle) = CreateServices();
        var now = DateTimeOffset.UtcNow;
        await knowledge.UpsertFactAsync(BuildKnowledge(
            "p1", "x", "rich", 0.9f, now,
            retrievalCount: 10, confirmationCount: 4, lastRetrievedAt: now, lifecycleScore: 1.15f));
        var result = await lifecycle.KnowledgeScoringAsync("p1", "x", "rich");
        Assert.NotNull(result);
        Assert.Equal("promote", (string)result!["recommendation"]!);
        Assert.Equal("x:rich", (string)result["key"]!);
    }

    /// <summary>Lifecycle input is scoped per project to avoid leaking entries across projects.</summary>
    [Fact]
    public async Task Brain_Stats_AreProjectScoped()
    {
        var (brain, _, lifecycle) = CreateServices();
        var now = DateTimeOffset.UtcNow;
        await brain.StoreFactAsync(BuildBrain("p1", "k1", "a", 0.9f, now));
        await brain.StoreFactAsync(BuildBrain("p2", "k2", "a", 0.9f, now));
        var statsP1 = await lifecycle.BrainStatsAsync("p1");
        var statsP2 = await lifecycle.BrainStatsAsync("p2");
        Assert.Equal(1, statsP1["total_memories"]);
        Assert.Equal(1, statsP2["total_memories"]);
    }
}
