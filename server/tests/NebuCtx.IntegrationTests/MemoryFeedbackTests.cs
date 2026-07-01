namespace NebuCtx.IntegrationTests;

using Microsoft.Extensions.Logging.Abstractions;

using NebuCtx.Contracts.Mcp;
using NebuCtx.Server.Core;
using NebuCtx.Server.Core.Services;
using NebuCtx.Storage;
using NebuCtx.Tools.Ctx;
using NebuCtx.Tools.Knowledge;

/// <summary>
/// Integration tests for durable memory feedback aliases.
/// </summary>
public class MemoryFeedbackTests
{
    /// <summary>
    /// Creates the in-memory services used by the feedback tests.
    /// </summary>
    private static (KnowledgeService Knowledge, KnowledgeToolHandler KnowledgeHandler, CtxToolHandler CtxHandler) CreateServices()
    {
        var brainStore = new InMemoryBrainStore();
        var knowledgeStore = new InMemoryKnowledgeStore();
        var sessionStore = new InMemorySessionStore();
        var knowledgeService = new KnowledgeService(knowledgeStore, sessionStore, NullLogger<KnowledgeService>.Instance);
        var maintenanceService = new MemoryMaintenanceService(brainStore, knowledgeStore, knowledgeService, NullLogger<MemoryMaintenanceService>.Instance);
        var sessionService = new SessionService(sessionStore, NullLogger<SessionService>.Instance);
        var knowledgeHandler = new KnowledgeToolHandler(knowledgeService, new MemoryLifecycleService(brainStore, knowledgeStore));
        var ctxHandler = new CtxToolHandler(knowledgeService, maintenanceService, sessionService);
        return (knowledgeService, knowledgeHandler, ctxHandler);
    }

    /// <summary>
    /// Creates one queued candidate and returns its promotion identity.
    /// </summary>
    private static async Task<string> CreateCandidateAsync(KnowledgeService knowledgeService, string projectId, string key)
    {
        var promotionIdentity = $"{projectId}:{key}:promotion";
        await knowledgeService.IngestCandidatesAsync(projectId, new[]
        {
            new KnowledgePromotionItem
            {
                Category = "root_cause",
                Key = key,
                Value = "Root cause is the shared-memory alias path.",
                Confidence = 0.8f,
                SourceType = "test",
                SourceScope = "session-1",
                PromotionIdentity = promotionIdentity,
            },
        }, CancellationToken.None);

        return promotionIdentity;
    }

    /// <summary>
    /// The knowledge tool accepts the upvote alias as an accept decision.
    /// </summary>
    [Fact]
    public async Task KnowledgeTool_UpvoteAliasAcceptsCandidate()
    {
        var (knowledgeService, knowledgeHandler, _) = CreateServices();
        var promotionIdentity = await CreateCandidateAsync(knowledgeService, "p1", "candidate-1");

        var result = await knowledgeHandler.ExecuteAsync(
            new Dictionary<string, object?>
            {
                ["action"] = "upvote",
                ["promotion_identity"] = promotionIdentity,
            },
            new ToolExecutionContext { ProjectId = "p1" },
            CancellationToken.None);

        var json = System.Text.Json.JsonSerializer.Serialize(result);
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        Assert.Equal("accepted", doc.RootElement.GetProperty("review_status").GetString());
    }

    /// <summary>
    /// The public ctx memory tool accepts the reject alias as a reject decision.
    /// </summary>
    [Fact]
    public async Task CtxTool_RejectAliasRejectsCandidate()
    {
        var (knowledgeService, _, ctxHandler) = CreateServices();
        var promotionIdentity = await CreateCandidateAsync(knowledgeService, "p2", "candidate-2");

        var result = await ctxHandler.ExecuteAsync(
            new Dictionary<string, object?>
            {
                ["domain"] = "memory",
                ["action"] = "reject",
                ["promotion_identity"] = promotionIdentity,
            },
            new ToolExecutionContext { ProjectId = "p2" },
            CancellationToken.None);

        var json = System.Text.Json.JsonSerializer.Serialize(result);
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        Assert.Equal("rejected", doc.RootElement.GetProperty("review_status").GetString());
    }
}
