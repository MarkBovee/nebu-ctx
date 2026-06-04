namespace NebuCtx.IntegrationTests;

using Microsoft.Extensions.Logging.Abstractions;

using NebuCtx.Server.Core;
using NebuCtx.Server.Core.Services;
using NebuCtx.Storage;
using NebuCtx.Tools.Brain;
using NebuCtx.Tools.Knowledge;

/// <summary>
/// Integration tests for the memory-portability spec: import + export of
/// brain and knowledge entries through the tool handlers, with overwrite
/// and skip semantics preserved.
/// </summary>
public class MemoryPortabilityTests
{
    private static (BrainService Brain, KnowledgeService Knowledge, BrainToolHandler BrainHandler, KnowledgeToolHandler KnowledgeHandler) CreateServices()
    {
        var brainStore = new InMemoryBrainStore();
        var knowledgeStore = new InMemoryKnowledgeStore();
        var sessionStore = new InMemorySessionStore();
        var knowledgeService = new KnowledgeService(knowledgeStore, sessionStore, NullLogger<KnowledgeService>.Instance);
        var brainService = new BrainService(brainStore, knowledgeService, NullLogger<BrainService>.Instance);
        var lifecycle = new MemoryLifecycleService(brainStore, knowledgeStore);
        var brainHandler = new BrainToolHandler(brainService, lifecycle);
        var knowledgeHandler = new KnowledgeToolHandler(knowledgeService, lifecycle);
        return (brainService, knowledgeService, brainHandler, knowledgeHandler);
    }

    private static ToolExecutionContext Ctx(string projectId) => new() { ProjectId = projectId };

    private static Dictionary<string, object?> ImportPayload(params object[] memories) => new()
    {
        ["action"] = "import",
        ["import_payload"] = new Dictionary<string, object?>
        {
            ["memories"] = memories,
        },
    };

    /// <summary>Importing a fresh payload adds the entries to the project store.</summary>
    [Fact]
    public async Task Knowledge_Import_AddsFreshEntries()
    {
        var (_, knowledge, _, handler) = CreateServices();
        var payload = ImportPayload(
            new Dictionary<string, object?> { ["category"] = "arch", ["key"] = "k1", ["value"] = "value 1" },
            new Dictionary<string, object?> { ["category"] = "arch", ["key"] = "k2", ["value"] = "value 2" }
        );
        var result = await handler.ExecuteAsync(payload, Ctx("p1"), CancellationToken.None);
        var json = System.Text.Json.JsonSerializer.Serialize(result);
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        Assert.Equal(2, doc.RootElement.GetProperty("added").GetInt32());
        Assert.Equal(0, doc.RootElement.GetProperty("updated").GetInt32());
        Assert.Equal(0, doc.RootElement.GetProperty("skipped").GetInt32());
        var recalled = await knowledge.RecallAsync("p1", "arch", "k1", 10, CancellationToken.None);
        Assert.NotEmpty(recalled);
    }

    /// <summary>Re-importing the same payload without --overwrite skips existing entries.</summary>
    [Fact]
    public async Task Knowledge_Import_DefaultSkipsExisting()
    {
        var (_, knowledge, _, handler) = CreateServices();
        await knowledge.RememberAsync("p1", "arch", "k1", "original", 1.0f, "remember", null, null, CancellationToken.None);
        var payload = ImportPayload(
            new Dictionary<string, object?> { ["category"] = "arch", ["key"] = "k1", ["value"] = "replacement" }
        );
        var result = await handler.ExecuteAsync(payload, Ctx("p1"), CancellationToken.None);
        var json = System.Text.Json.JsonSerializer.Serialize(result);
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        Assert.Equal(0, doc.RootElement.GetProperty("added").GetInt32());
        Assert.Equal(0, doc.RootElement.GetProperty("updated").GetInt32());
        Assert.Equal(1, doc.RootElement.GetProperty("skipped").GetInt32());
        var existing = await knowledge.GetFactAsync("p1", "arch", "k1", CancellationToken.None);
        Assert.Equal("original", existing!.Value);
    }

    /// <summary>With --overwrite, re-importing the same payload replaces the existing entries.</summary>
    [Fact]
    public async Task Knowledge_Import_OverwriteReplacesExisting()
    {
        var (_, knowledge, _, handler) = CreateServices();
        await knowledge.RememberAsync("p1", "arch", "k1", "original", 1.0f, "remember", null, null, CancellationToken.None);
        var payload = ImportPayload(
            new Dictionary<string, object?> { ["category"] = "arch", ["key"] = "k1", ["value"] = "replacement" }
        );
        payload["overwrite"] = true;
        payload["action"] = "import";
        var result = await handler.ExecuteAsync(payload, Ctx("p1"), CancellationToken.None);
        var json = System.Text.Json.JsonSerializer.Serialize(result);
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        Assert.Equal(0, doc.RootElement.GetProperty("added").GetInt32());
        Assert.Equal(1, doc.RootElement.GetProperty("updated").GetInt32());
        Assert.Equal(0, doc.RootElement.GetProperty("skipped").GetInt32());
        var existing = await knowledge.GetFactAsync("p1", "arch", "k1", CancellationToken.None);
        Assert.Equal("replacement", existing!.Value);
    }

    /// <summary>Importing an empty memory list returns zeros across the summary.</summary>
    [Fact]
    public async Task Knowledge_Import_EmptyMemoriesIsNoOp()
    {
        var (_, _, _, handler) = CreateServices();
        var payload = ImportPayload();
        var result = await handler.ExecuteAsync(payload, Ctx("p1"), CancellationToken.None);
        var json = System.Text.Json.JsonSerializer.Serialize(result);
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        Assert.Equal(0, doc.RootElement.GetProperty("added").GetInt32());
        Assert.Equal(0, doc.RootElement.GetProperty("updated").GetInt32());
        Assert.Equal(0, doc.RootElement.GetProperty("skipped").GetInt32());
    }

    /// <summary>An import without a memories array is rejected as a validation error.</summary>
    [Fact]
    public async Task Knowledge_Import_RejectsMissingMemories()
    {
        var (_, _, _, handler) = CreateServices();
        var payload = new Dictionary<string, object?>
        {
            ["import_payload"] = new Dictionary<string, object?> { ["other"] = "value" },
        };
        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await handler.ExecuteAsync(payload, Ctx("p1"), CancellationToken.None));
    }

    /// <summary>Brain import honors the same skip/overwrite semantics.</summary>
    [Fact]
    public async Task Brain_Import_SkipsAndOverwrites()
    {
        var (brain, _, handler, _) = CreateServices();
        await brain.StoreAsync("p1", "k1", "original");
        var skipPayload = new Dictionary<string, object?>
        {
            ["action"] = "import",
            ["import_payload"] = new Dictionary<string, object?>
            {
                ["memories"] = new object[] { new Dictionary<string, object?> { ["key"] = "k1", ["value"] = "new" } },
            },
        };
        var skip = await handler.ExecuteAsync(skipPayload, Ctx("p1"), CancellationToken.None);
        var skipJson = System.Text.Json.JsonSerializer.Serialize(skip);
        using var skipDoc = System.Text.Json.JsonDocument.Parse(skipJson);
        Assert.Equal(1, skipDoc.RootElement.GetProperty("skipped").GetInt32());

        var overwritePayload = new Dictionary<string, object?>
        {
            ["action"] = "import",
            ["import_payload"] = new Dictionary<string, object?>
            {
                ["memories"] = new object[] { new Dictionary<string, object?> { ["key"] = "k1", ["value"] = "new" } },
            },
            ["overwrite"] = true,
        };
        var overwrite = await handler.ExecuteAsync(overwritePayload, Ctx("p1"), CancellationToken.None);
        var overwriteJson = System.Text.Json.JsonSerializer.Serialize(overwrite);
        using var overwriteDoc = System.Text.Json.JsonDocument.Parse(overwriteJson);
        Assert.Equal(1, overwriteDoc.RootElement.GetProperty("updated").GetInt32());
    }

    /// <summary>Brain import adds brand-new entries.</summary>
    [Fact]
    public async Task Brain_Import_AddsNewEntries()
    {
        var (brain, _, handler, _) = CreateServices();
        var payload = new Dictionary<string, object?>
        {
            ["action"] = "import",
            ["import_payload"] = new Dictionary<string, object?>
            {
                ["memories"] = new object[]
                {
                    new Dictionary<string, object?> { ["key"] = "alpha", ["value"] = "value-a" },
                    new Dictionary<string, object?> { ["key"] = "beta", ["value"] = "value-b" },
                },
            },
        };
        var result = await handler.ExecuteAsync(payload, Ctx("p1"), CancellationToken.None);
        var json = System.Text.Json.JsonSerializer.Serialize(result);
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        Assert.Equal(2, doc.RootElement.GetProperty("added").GetInt32());
        var alpha = await brain.RecallAsync("p1", "alpha", 1, CancellationToken.None);
        Assert.NotEmpty(alpha);
    }

    /// <summary>Round-trip: remember via service, then export-style payload re-imported after delete.</summary>
    [Fact]
    public async Task Knowledge_RoundTrip_PreservesFields()
    {
        var (_, knowledge, _, handler) = CreateServices();
        await knowledge.RememberAsync("p1", "arch", "k1", "value", 0.85f, "manual", "scope-1", null, CancellationToken.None);
        var entries = await knowledge.RecallAsync("p1", "arch", "k1", 10, CancellationToken.None);
        var source = entries.First();
        var memoryPayload = new Dictionary<string, object?>
        {
            ["category"] = source.Category,
            ["key"] = source.Key,
            ["value"] = source.Value,
            ["confidence"] = source.Confidence,
            ["source_type"] = source.SourceType,
            ["source_scope"] = source.SourceScope,
        };
        await knowledge.RemoveAsync("p1", source.Category, source.Key, CancellationToken.None);
        var importPayload = ImportPayload(memoryPayload);
        var result = await handler.ExecuteAsync(importPayload, Ctx("p1"), CancellationToken.None);
        var json = System.Text.Json.JsonSerializer.Serialize(result);
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        Assert.Equal(1, doc.RootElement.GetProperty("added").GetInt32());
        var restored = await knowledge.GetFactAsync("p1", "arch", "k1", CancellationToken.None);
        Assert.NotNull(restored);
        Assert.Equal("value", restored!.Value);
        Assert.Equal("scope-1", restored.SourceScope);
    }
}
