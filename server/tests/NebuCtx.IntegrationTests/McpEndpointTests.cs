namespace NebuCtx.IntegrationTests;

using System.IO;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using NebuCtx.Contracts.Dashboard;
using NebuCtx.Server.Host.Dashboard;
using NebuCtx.Contracts.Mcp;
using NebuCtx.Contracts.Projects;
using NebuCtx.Storage;

/// <summary>
/// Integration tests for the MCP HTTP endpoints.
/// Uses <see cref="NebuCtxTestFactory"/> to test the full middleware pipeline
/// without requiring a real PostgreSQL connection.
/// </summary>
public class McpEndpointTests : IClassFixture<NebuCtxTestFactory>
{
    private readonly HttpClient _client;
    private readonly NebuCtxTestFactory _factory;

    /// <summary>
    /// Initializes the test with an in-memory test server.
    /// </summary>
    public McpEndpointTests(NebuCtxTestFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private async Task SeedLegacyProjectAsync(ProjectRecord project, params KnowledgeEntry[] facts)
    {
        using var scope = _factory.Services.CreateScope();
        var projectStore = scope.ServiceProvider.GetRequiredService<IProjectStore>();
        var knowledgeStore = scope.ServiceProvider.GetRequiredService<IKnowledgeStore>();

        await projectStore.CreateProjectAsync(project);
        foreach (var fact in facts)
        {
            await knowledgeStore.UpsertFactAsync(fact);
        }
    }

    /// <summary>
    /// Health endpoint responds with 200 OK and status "ok".
    /// </summary>
    [Fact]
    public async Task Health_Returns200()
    {
        var response = await _client.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    /// <summary>
    /// Manifest endpoint returns tool list with the server name and version.
    /// </summary>
    [Fact]
    public async Task Manifest_ReturnsToolList()
    {
        var manifest = await _client.GetFromJsonAsync<ManifestResponse>("/v1/manifest");
        Assert.NotNull(manifest);
        Assert.Equal("nebu-ctx", manifest.Name);
        Assert.Equal(5, manifest.Tools.Count);
        Assert.Equal(["ctx", "ctx_read", "ctx_search", "ctx_shell", "ctx_tree"], manifest.Tools.Select(tool => tool.Name).OrderBy(name => name, StringComparer.Ordinal).ToArray());
    }

    /// <summary>
    /// Tools endpoint returns paginated tool list.
    /// </summary>
    [Fact]
    public async Task Tools_ReturnsPaginatedList()
    {
        var toolList = await _client.GetFromJsonAsync<ToolListResponse>("/v1/tools");
        Assert.NotNull(toolList);
        Assert.Equal(5, toolList.Total);
        Assert.Equal(5, toolList.Tools.Count);
        Assert.Equal(["ctx", "ctx_read", "ctx_search", "ctx_shell", "ctx_tree"], toolList.Tools.Select(tool => tool.Name).OrderBy(name => name, StringComparer.Ordinal).ToArray());
    }

    /// <summary>
    /// Aggregated dashboard overview endpoint returns the simplified overview payload.
    /// </summary>
    [Fact]
    public async Task DashboardOverview_ReturnsAggregatedPayload()
    {
        var payload = await _client.GetFromJsonAsync<DashboardOverviewResponse>("/api/dashboard/overview");
        Assert.NotNull(payload);
        Assert.NotNull(payload!.Version);
        Assert.NotNull(payload.Stats);
        Assert.NotNull(payload.Gain);
    }

    /// <summary>
    /// Dashboard overview returns daily savings grouped per project plus active session data.
    /// </summary>
    [Fact]
    public async Task DashboardOverview_ReturnsProjectDailySavingsAndActiveSessions()
    {
        var projectId = $"proj_{Guid.NewGuid():N}";
        await SeedLegacyProjectAsync(new ProjectRecord
        {
            ProjectId = projectId,
            Slug = "overview-project",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            ProjectMetadata = new ProjectMetadataEnvelope
            {
                SchemaVersion = 1,
                Summary = new ProjectMetadataSummary
                {
                    TotalFileCount = 5,
                    SourceFileCount = 3,
                    Markers = [".git", "Cargo.toml"],
                    Languages = [new ProjectLanguageStat { Language = "rust", FileCount = 3 }],
                },
            },
        });

        var ingestResponse = await _client.PostAsJsonAsync("/v1/telemetry/ingest", new TelemetryIngestRequest
        {
            ToolName = "ctx_read",
            TokensOriginal = 1200,
            TokensSaved = 500,
            Mode = "map",
            ProjectSlug = "overview-project",
            CommandPreview = "ctx_read src/main.rs",
            CheckoutBinding = new CheckoutBinding
            {
                ProjectId = projectId,
                LocalRoot = "/workspace/overview-project",
                Branch = "main",
                ClientLabel = "integration-client-1",
            },
        });
        Assert.Equal(HttpStatusCode.OK, ingestResponse.StatusCode);

        var payload = await _client.GetFromJsonAsync<DashboardOverviewResponse>("/api/dashboard/overview");
        Assert.NotNull(payload);

        var dailySavings = Assert.Single(payload!.Stats.ProjectDailySavings, item => item.ProjectId == projectId);
        Assert.Equal(projectId, dailySavings.ProjectId);
        Assert.Equal("overview-project", dailySavings.ProjectName);
        Assert.Equal(500, dailySavings.TokensSaved);
        Assert.True(payload.Stats.ActiveSessions.Count >= 1);

        var session = Assert.Single(payload.Stats.ActiveSessions, item => item.ProjectId == projectId);
        Assert.Equal("overview-project", session.ProjectName);
        Assert.Equal("integration-client-1", session.ClientId);
        Assert.Equal(1, session.ToolCalls);
    }

    /// <summary>
    /// Dashboard domain endpoint groups the detailed panels into fewer operator areas.
    /// </summary>
    [Fact]
    public async Task DashboardDomains_ReturnsConsolidatedViewGroups()
    {
        var payload = await _client.GetFromJsonAsync<DashboardDomainsResponse>("/api/dashboard/domains");
        Assert.NotNull(payload);
        Assert.Equal(3, payload!.Domains.Count);

        var memoryDomain = Assert.Single(payload.Domains, domain => domain.Id == "memory");
        Assert.Contains(memoryDomain.Views, view => view.Id == "knowledge");
        Assert.Contains(memoryDomain.Views, view => view.Id == "brain");

        var allViewIds = payload.Domains.SelectMany(domain => domain.Views).Select(view => view.Id).ToArray();
        Assert.Contains("overview", allViewIds);
        Assert.Contains("agents", allViewIds);
        Assert.DoesNotContain("routes", allViewIds);
        Assert.DoesNotContain("contextlayer", allViewIds);
        Assert.DoesNotContain("learning", allViewIds);
    }

    /// <summary>
    /// Tool call with ctx_brain status action returns successfully.
    /// </summary>
    [Fact]
    public async Task ToolCall_BrainStatus_ReturnsOk()
    {
        var request = new ToolCallRequest
        {
            Name = "ctx_brain",
            Arguments = new Dictionary<string, object?> { ["action"] = "status" },
        };

        var response = await _client.PostAsJsonAsync("/v1/tools/call", request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<ToolCallResponse>();
        Assert.NotNull(result?.Result);
    }

    /// <summary>
     /// Tool call with unknown tool returns 400 Bad Request.
     /// </summary>
    [Fact]
    public async Task ToolCall_UnknownTool_Returns400()
    {
        var request = new ToolCallRequest
        {
            Name = "nonexistent_tool",
            Arguments = [],
        };

        var response = await _client.PostAsJsonAsync("/v1/tools/call", request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>
    /// Project resolve returns 409 when duplicate fingerprint records already exist.
    /// </summary>
    [Fact]
    public async Task ProjectResolve_DuplicateFingerprint_Returns409()
    {
        var fingerprint = new RepositoryFingerprint
        {
            RemoteUrl = "https://github.com/example/ha-addons.git",
            Host = "github.com",
            Owner = "example",
            RepoName = "ha-addons",
            DefaultBranch = "main",
        };

        await SeedLegacyProjectAsync(new ProjectRecord
        {
            ProjectId = "proj_dup_a",
            Slug = "ha-addons",
            Fingerprint = fingerprint,
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-2),
            UpdatedAt = DateTimeOffset.UtcNow.AddMinutes(-2),
        });
        await SeedLegacyProjectAsync(new ProjectRecord
        {
            ProjectId = "proj_dup_b",
            Slug = "ha-addons",
            Fingerprint = fingerprint,
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-1),
            UpdatedAt = DateTimeOffset.UtcNow.AddMinutes(-1),
        });

        var response = await _client.PostAsJsonAsync("/v1/projects/resolve", new ProjectResolutionRequest
        {
            SuggestedSlug = "ha-addons",
            Fingerprint = fingerprint,
        });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    /// <summary>
    /// Public ctx memory calls route to the hosted canonical knowledge path.
    /// </summary>
    [Fact]
    public async Task ToolCall_PublicCtxMemoryRememberAndRecall_ReturnsHostedKnowledge()
    {
        var fingerprint = new RepositoryFingerprint
        {
            RemoteUrl = "https://github.com/example/public-ctx-memory.git",
            Host = "github.com",
            Owner = "example",
            RepoName = "public-ctx-memory",
            DefaultBranch = "main",
        };

        var rememberResponse = await _client.PostAsJsonAsync("/v1/tools/call", new ToolCallRequest
        {
            Name = "ctx",
            RepositoryFingerprint = fingerprint,
            ProjectSlug = "public-ctx-memory",
            Arguments = new Dictionary<string, object?>
            {
                ["domain"] = "memory",
                ["action"] = "remember",
                ["category"] = "decision",
                ["key"] = "memory-owner",
                ["value"] = "server owns canonical memory",
                ["confidence"] = 0.95,
            },
        });
        Assert.Equal(HttpStatusCode.OK, rememberResponse.StatusCode);

        var recallResponse = await _client.PostAsJsonAsync("/v1/tools/call", new ToolCallRequest
        {
            Name = "ctx",
            RepositoryFingerprint = fingerprint,
            ProjectSlug = "public-ctx-memory",
            Arguments = new Dictionary<string, object?>
            {
                ["domain"] = "memory",
                ["action"] = "recall",
                ["query"] = "memory-owner",
            },
        });
        Assert.Equal(HttpStatusCode.OK, recallResponse.StatusCode);

        var recallPayload = await recallResponse.Content.ReadAsStringAsync();
        Assert.Contains("memory-owner", recallPayload, StringComparison.Ordinal);
        Assert.Contains("server owns canonical memory", recallPayload, StringComparison.Ordinal);
    }

    /// <summary>
    /// Hosted ctx_knowledge search aliases to the same recall path used by the client memory gateway.
    /// </summary>
    [Fact]
    public async Task ToolCall_CtxKnowledgeSearch_ReturnsHostedKnowledge()
    {
        var fingerprint = new RepositoryFingerprint
        {
            RemoteUrl = "https://github.com/example/knowledge-search-alias.git",
            Host = "github.com",
            Owner = "example",
            RepoName = "knowledge-search-alias",
            DefaultBranch = "main",
        };

        var rememberResponse = await _client.PostAsJsonAsync("/v1/tools/call", new ToolCallRequest
        {
            Name = "ctx_knowledge",
            RepositoryFingerprint = fingerprint,
            ProjectSlug = "knowledge-search-alias",
            Arguments = new Dictionary<string, object?>
            {
                ["action"] = "remember",
                ["category"] = "deployment",
                ["key"] = "plugin-hooks",
                ["value"] = "Fixed opencode plugin hooks and setup flow yesterday",
                ["confidence"] = 0.95,
            },
        });
        Assert.Equal(HttpStatusCode.OK, rememberResponse.StatusCode);

        var searchResponse = await _client.PostAsJsonAsync("/v1/tools/call", new ToolCallRequest
        {
            Name = "ctx_knowledge",
            RepositoryFingerprint = fingerprint,
            ProjectSlug = "knowledge-search-alias",
            Arguments = new Dictionary<string, object?>
            {
                ["action"] = "search",
                ["query"] = "what did we fix yesterday in plugin hooks",
            },
        });
        Assert.Equal(HttpStatusCode.OK, searchResponse.StatusCode);

        var searchPayload = await searchResponse.Content.ReadAsStringAsync();
        Assert.Contains("plugin-hooks", searchPayload, StringComparison.Ordinal);
        Assert.Contains("Fixed opencode plugin hooks and setup flow yesterday", searchPayload, StringComparison.Ordinal);
    }

    /// <summary>
    /// Hosted knowledge categories and timeline are available through both private and public memory contracts.
    /// </summary>
    [Fact]
    public async Task ToolCall_HostedKnowledgeCategoriesAndTimeline_ReturnKnowledgeViews()
    {
        var fingerprint = new RepositoryFingerprint
        {
            RemoteUrl = "https://github.com/example/knowledge-views.git",
            Host = "github.com",
            Owner = "example",
            RepoName = "knowledge-views",
            DefaultBranch = "main",
        };

        var rememberOne = await _client.PostAsJsonAsync("/v1/tools/call", new ToolCallRequest
        {
            Name = "ctx_knowledge",
            RepositoryFingerprint = fingerprint,
            ProjectSlug = "knowledge-views",
            Arguments = new Dictionary<string, object?>
            {
                ["action"] = "remember",
                ["category"] = "deployment",
                ["key"] = "plugin-hooks",
                ["value"] = "Initial plugin hook fix",
                ["confidence"] = 0.8,
            },
        });
        Assert.Equal(HttpStatusCode.OK, rememberOne.StatusCode);

        var rememberTwo = await _client.PostAsJsonAsync("/v1/tools/call", new ToolCallRequest
        {
            Name = "ctx_knowledge",
            RepositoryFingerprint = fingerprint,
            ProjectSlug = "knowledge-views",
            Arguments = new Dictionary<string, object?>
            {
                ["action"] = "remember",
                ["category"] = "deployment",
                ["key"] = "plugin-hooks",
                ["value"] = "Final plugin hook fix with setup cleanup",
                ["confidence"] = 0.95,
            },
        });
        Assert.Equal(HttpStatusCode.OK, rememberTwo.StatusCode);

        var categoriesResponse = await _client.PostAsJsonAsync("/v1/tools/call", new ToolCallRequest
        {
            Name = "ctx",
            RepositoryFingerprint = fingerprint,
            ProjectSlug = "knowledge-views",
            Arguments = new Dictionary<string, object?>
            {
                ["domain"] = "memory",
                ["action"] = "categories",
            },
        });
        Assert.Equal(HttpStatusCode.OK, categoriesResponse.StatusCode);
        var categoriesPayload = await categoriesResponse.Content.ReadAsStringAsync();
        Assert.Contains("deployment", categoriesPayload, StringComparison.Ordinal);

        var timelineResponse = await _client.PostAsJsonAsync("/v1/tools/call", new ToolCallRequest
        {
            Name = "ctx",
            RepositoryFingerprint = fingerprint,
            ProjectSlug = "knowledge-views",
            Arguments = new Dictionary<string, object?>
            {
                ["domain"] = "memory",
                ["action"] = "timeline",
                ["category"] = "deployment",
            },
        });
        Assert.Equal(HttpStatusCode.OK, timelineResponse.StatusCode);
        var timelinePayload = await timelineResponse.Content.ReadAsStringAsync();
        Assert.Contains("Initial plugin hook fix", timelinePayload, StringComparison.Ordinal);
        Assert.Contains("Final plugin hook fix with setup cleanup", timelinePayload, StringComparison.Ordinal);
    }

    /// <summary>
    /// Project resolution endpoint creates a canonical project and persists the workspace binding.
    /// </summary>
    [Fact]
    public async Task ProjectResolve_ReturnsCanonicalProject()
    {
        var request = new ProjectResolutionRequest
        {
            SuggestedSlug = "nebu-ctx",
            Fingerprint = new RepositoryFingerprint
            {
                RemoteUrl = "https://github.com/MarkBovee/nebu-ctx.git",
                Host = "github.com",
                Owner = "MarkBovee",
                RepoName = "nebu-ctx",
                DefaultBranch = "main",
            },
            WorkspaceBinding = new CheckoutBinding
            {
                ProjectId = "ignored-by-server",
                LocalRoot = "E:/Projects/Personal/nebu-ctx",
                Branch = "main",
                ClientLabel = "integration-test",
            },
            ProjectMetadata = new ProjectMetadataEnvelope
            {
                SchemaVersion = 1,
                Summary = new ProjectMetadataSummary
                {
                    TotalFileCount = 12,
                    SourceFileCount = 7,
                    Markers = [".git", "Cargo.toml"],
                    Languages = [new ProjectLanguageStat { Language = "rust", FileCount = 7 }],
                },
            },
        };

        var response = await _client.PostAsJsonAsync("/v1/projects/resolve", request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<ProjectResolutionResponse>();
        Assert.NotNull(payload);
        Assert.Equal("nebu-ctx", payload.Project.Slug);
        Assert.StartsWith("proj_", payload.Project.ProjectId, StringComparison.Ordinal);
        Assert.True(payload.WorkspaceBound);
        Assert.NotNull(payload.Project.ProjectMetadata);
        Assert.Equal(7, payload.Project.ProjectMetadata!.Summary.SourceFileCount);
    }

    /// <summary>
    /// Per-project dashboard memory endpoint returns project knowledge and brain entries.
    /// </summary>
    [Fact]
    public async Task DashboardProjectMemory_ReturnsProjectScopedEntries()
    {
        var resolveRequest = new ProjectResolutionRequest
        {
            SuggestedSlug = "project-memory",
            Fingerprint = new RepositoryFingerprint
            {
                RemoteUrl = "https://github.com/example/project-memory.git",
                Host = "github.com",
                Owner = "example",
                RepoName = "project-memory",
                DefaultBranch = "main",
            },
        };

        var resolveResponse = await _client.PostAsJsonAsync("/v1/projects/resolve", resolveRequest);
        Assert.Equal(HttpStatusCode.OK, resolveResponse.StatusCode);

        var resolved = await resolveResponse.Content.ReadFromJsonAsync<ProjectResolutionResponse>();
        Assert.NotNull(resolved);

        var knowledgeResponse = await _client.PostAsJsonAsync("/v1/tools/call", new ToolCallRequest
        {
            Name = "ctx_knowledge",
            ProjectId = resolved!.Project.ProjectId,
            Arguments = new Dictionary<string, object?>
            {
                ["action"] = "remember",
                ["category"] = "ARCHITECTURE",
                ["key"] = "storage",
                ["value"] = "postgres",
            },
        });
        Assert.Equal(HttpStatusCode.OK, knowledgeResponse.StatusCode);

        var brainResponse = await _client.PostAsJsonAsync("/v1/tools/call", new ToolCallRequest
        {
            Name = "ctx_brain",
            ProjectId = resolved.Project.ProjectId,
            Arguments = new Dictionary<string, object?>
            {
                ["action"] = "store",
                ["key"] = "session-demo",
                ["value"] = "dashboard memory smoke",
            },
        });
        Assert.Equal(HttpStatusCode.OK, brainResponse.StatusCode);

        var payload = await _client.GetFromJsonAsync<ProjectMemoryResponse>($"/api/dashboard/projects/{resolved.Project.ProjectId}/memory");
        Assert.NotNull(payload);
        Assert.Equal(resolved.Project.ProjectId, payload!.ProjectId);
        Assert.Contains(payload.Knowledge, item => item.Category == "ARCHITECTURE" && item.Key == "storage" && item.Value == "postgres");
        Assert.Contains(payload.Brain, item => item.Key == "session-demo");
    }

    /// <summary>
    /// Brain ingest persists semantic fact metadata and refreshes the public memory projection.
    /// </summary>
    [Fact]
    public async Task ToolCall_BrainIngest_PersistsSemanticFactAndProjectsToKnowledge()
    {
        var resolveResponse = await _client.PostAsJsonAsync("/v1/projects/resolve", new ProjectResolutionRequest
        {
            SuggestedSlug = "brain-ingest",
            Fingerprint = new RepositoryFingerprint
            {
                RemoteUrl = "https://github.com/example/brain-ingest.git",
                Host = "github.com",
                Owner = "example",
                RepoName = "brain-ingest",
                DefaultBranch = "main",
            },
        });
        Assert.Equal(HttpStatusCode.OK, resolveResponse.StatusCode);

        var resolved = await resolveResponse.Content.ReadFromJsonAsync<ProjectResolutionResponse>();
        Assert.NotNull(resolved?.Project?.ProjectId);
        var projectId = resolved!.Project!.ProjectId;

        var ingestResponse = await _client.PostAsJsonAsync("/v1/tools/call", new ToolCallRequest
        {
            Name = "ctx_brain",
            ProjectId = projectId,
            Arguments = new Dictionary<string, object?>
            {
                ["action"] = "ingest",
                ["key"] = "primary-ide",
                ["value"] = "OpenCode",
                ["kind"] = "preference",
                ["category"] = "workflow",
                ["source_type"] = "idle_flush",
                ["source_scope"] = "session-123",
                ["promotion_identity"] = "idle-flush:session-123:workflow:primary-ide",
                ["logical_key"] = "workflow:primary-ide",
                ["confidence"] = 0.98,
                ["evidence"] = "derived from user-stated primary IDE",
            },
        });
        Assert.Equal(HttpStatusCode.OK, ingestResponse.StatusCode);

        var payload = await _client.GetFromJsonAsync<ProjectMemoryResponse>($"/api/dashboard/projects/{projectId}/memory");
        Assert.NotNull(payload);
        var brain = Assert.Single(payload!.Brain, item => item.Key == "primary-ide");
        Assert.Equal("preference", brain.EntryType);
        Assert.Equal("workflow", brain.Category);
        Assert.Equal("idle_flush", brain.SourceType);
        Assert.Equal("current", brain.LifecycleStatus);
        Assert.Equal("workflow:primary-ide", brain.LogicalKey);
        Assert.Equal("OpenCode", brain.Value);
        Assert.Contains(payload.Knowledge, item => item.Category == "workflow" && item.Key == "primary-ide" && item.Value == "OpenCode");
        Assert.Contains(payload.Wakeup, item => item.Category == "workflow" && item.Key == "primary-ide");
    }

    /// <summary>
    /// Replaying the same brain ingest does not create duplicate active facts.
    /// </summary>
    [Fact]
    public async Task ToolCall_BrainIngest_ReplayKeepsSingleActiveFact()
    {
        var resolveResponse = await _client.PostAsJsonAsync("/v1/projects/resolve", new ProjectResolutionRequest
        {
            SuggestedSlug = "brain-ingest-replay",
            Fingerprint = new RepositoryFingerprint
            {
                RemoteUrl = "https://github.com/example/brain-ingest-replay.git",
                Host = "github.com",
                Owner = "example",
                RepoName = "brain-ingest-replay",
                DefaultBranch = "main",
            },
        });
        Assert.Equal(HttpStatusCode.OK, resolveResponse.StatusCode);

        var resolved = await resolveResponse.Content.ReadFromJsonAsync<ProjectResolutionResponse>();
        Assert.NotNull(resolved?.Project?.ProjectId);
        var projectId = resolved!.Project!.ProjectId;

        var arguments = new Dictionary<string, object?>
        {
            ["action"] = "ingest",
            ["key"] = "primary-ide",
            ["value"] = "OpenCode",
            ["kind"] = "preference",
            ["category"] = "workflow",
            ["source_type"] = "idle_flush",
            ["source_scope"] = "session-123",
            ["promotion_identity"] = "idle-flush:session-123:workflow:primary-ide",
            ["logical_key"] = "workflow:primary-ide",
            ["confidence"] = 0.98,
            ["evidence"] = "derived from user-stated primary IDE",
        };

        foreach (var _ in Enumerable.Range(0, 2))
        {
            var ingestResponse = await _client.PostAsJsonAsync("/v1/tools/call", new ToolCallRequest
            {
                Name = "ctx_brain",
                ProjectId = projectId,
                Arguments = arguments,
            });
            Assert.Equal(HttpStatusCode.OK, ingestResponse.StatusCode);
        }

        var payload = await _client.GetFromJsonAsync<ProjectMemoryResponse>($"/api/dashboard/projects/{projectId}/memory");
        Assert.NotNull(payload);
        Assert.Single(payload!.Brain, item => item.Key == "primary-ide" && item.LifecycleStatus == "current");
        Assert.Single(payload.Knowledge, item => item.Category == "workflow" && item.Key == "primary-ide" && item.LifecycleStatus == "current");
    }

    /// <summary>
    /// Session timeline ingest remains visible in dashboard brain views without projecting into canonical knowledge.
    /// </summary>
    [Fact]
    public async Task ToolCall_BrainIngest_SessionTimelineAppearsInDashboardButNotKnowledge()
    {
        var resolveResponse = await _client.PostAsJsonAsync("/v1/projects/resolve", new ProjectResolutionRequest
        {
            SuggestedSlug = "brain-timeline-dashboard",
            Fingerprint = new RepositoryFingerprint
            {
                RemoteUrl = "https://github.com/example/brain-timeline-dashboard.git",
                Host = "github.com",
                Owner = "example",
                RepoName = "brain-timeline-dashboard",
                DefaultBranch = "main",
            },
        });
        Assert.Equal(HttpStatusCode.OK, resolveResponse.StatusCode);

        var resolved = await resolveResponse.Content.ReadFromJsonAsync<ProjectResolutionResponse>();
        Assert.NotNull(resolved?.Project?.ProjectId);
        var projectId = resolved!.Project!.ProjectId;

        var createdAt = DateTimeOffset.Parse("2026-05-22T10:55:00Z");
        var ingestResponse = await _client.PostAsJsonAsync("/v1/tools/call", new ToolCallRequest
        {
            Name = "ctx_brain",
            ProjectId = projectId,
            Arguments = new Dictionary<string, object?>
            {
                ["action"] = "ingest",
                ["key"] = "timeline-e2e",
                ["value"] = "E2E timeline event visible in dashboard",
                ["kind"] = "session_event",
                ["category"] = "session_timeline",
                ["source_type"] = "user_turn",
                ["source_scope"] = "session-e2e",
                ["promotion_identity"] = "timeline:e2e",
                ["logical_key"] = "timeline-e2e",
                ["lifecycle_status"] = "timeline",
                ["created_at"] = createdAt.ToString("O"),
                ["confidence"] = 0.6,
                ["evidence"] = "source=e2e timestamp=2026-05-22T10:55:00Z",
            },
        });
        Assert.Equal(HttpStatusCode.OK, ingestResponse.StatusCode);

        var dashboardBrain = await _client.GetFromJsonAsync<JsonElement>("/api/brain");
        var dashboardEntries = dashboardBrain.GetProperty("entries").EnumerateArray().ToArray();
        var timelineEntry = Assert.Single(dashboardEntries, entry => entry.GetProperty("key").GetString() == "timeline-e2e");
        Assert.Equal("session_event", timelineEntry.GetProperty("entry_type").GetString());
        Assert.Equal("timeline", timelineEntry.GetProperty("lifecycle_status").GetString());
        Assert.Equal("session-e2e", timelineEntry.GetProperty("source_scope").GetString());
        Assert.Equal(createdAt, timelineEntry.GetProperty("created_at").GetDateTimeOffset());

        var payload = await _client.GetFromJsonAsync<ProjectMemoryResponse>($"/api/dashboard/projects/{projectId}/memory");
        Assert.NotNull(payload);
        var brainEntry = Assert.Single(payload!.Brain, item => item.Key == "timeline-e2e");
        Assert.Equal("session_event", brainEntry.EntryType);
        Assert.Equal("timeline", brainEntry.LifecycleStatus);
        Assert.Equal(createdAt, brainEntry.CreatedAt);
        Assert.DoesNotContain(payload.Knowledge, item => item.Key == "timeline-e2e");
    }

    /// <summary>
    /// A newer fact with the same logical key supersedes the prior active brain fact.
    /// </summary>
    [Fact]
    public async Task ToolCall_BrainIngest_SupersedesPriorFactWithSameLogicalKey()
    {
        var resolveResponse = await _client.PostAsJsonAsync("/v1/projects/resolve", new ProjectResolutionRequest
        {
            SuggestedSlug = "brain-ingest-supersession",
            Fingerprint = new RepositoryFingerprint
            {
                RemoteUrl = "https://github.com/example/brain-ingest-supersession.git",
                Host = "github.com",
                Owner = "example",
                RepoName = "brain-ingest-supersession",
                DefaultBranch = "main",
            },
        });
        Assert.Equal(HttpStatusCode.OK, resolveResponse.StatusCode);

        var resolved = await resolveResponse.Content.ReadFromJsonAsync<ProjectResolutionResponse>();
        Assert.NotNull(resolved?.Project?.ProjectId);
        var projectId = resolved!.Project!.ProjectId;

        foreach (var item in new[]
                 {
                     new Dictionary<string, object?>
                     {
                         ["action"] = "ingest",
                         ["key"] = "primary-ide-opencode",
                         ["value"] = "OpenCode",
                         ["kind"] = "preference",
                         ["category"] = "workflow",
                         ["source_type"] = "idle_flush",
                         ["source_scope"] = "session-100",
                         ["promotion_identity"] = "idle-flush:session-100:workflow:primary-ide",
                         ["logical_key"] = "workflow:primary-ide",
                         ["confidence"] = 0.95,
                     },
                     new Dictionary<string, object?>
                     {
                         ["action"] = "ingest",
                         ["key"] = "primary-ide-cursor",
                         ["value"] = "Cursor",
                         ["kind"] = "preference",
                         ["category"] = "workflow",
                         ["source_type"] = "idle_flush",
                         ["source_scope"] = "session-101",
                         ["promotion_identity"] = "idle-flush:session-101:workflow:primary-ide",
                         ["logical_key"] = "workflow:primary-ide",
                         ["confidence"] = 0.95,
                     },
                 })
        {
            var ingestResponse = await _client.PostAsJsonAsync("/v1/tools/call", new ToolCallRequest
            {
                Name = "ctx_brain",
                ProjectId = projectId,
                Arguments = item,
            });
            Assert.Equal(HttpStatusCode.OK, ingestResponse.StatusCode);
        }

        var payload = await _client.GetFromJsonAsync<ProjectMemoryResponse>($"/api/dashboard/projects/{projectId}/memory");
        Assert.NotNull(payload);
        Assert.Contains(payload!.Brain, item => item.Key == "primary-ide-opencode" && item.LifecycleStatus == "superseded");
        Assert.Contains(payload.Brain, item => item.Key == "primary-ide-cursor" && item.LifecycleStatus == "current");
    }

    /// <summary>
    /// A correction fact invalidates the prior active brain fact for the same logical key.
    /// </summary>
    [Fact]
    public async Task ToolCall_BrainIngest_CorrectionInvalidatesPriorFact()
    {
        var resolveResponse = await _client.PostAsJsonAsync("/v1/projects/resolve", new ProjectResolutionRequest
        {
            SuggestedSlug = "brain-ingest-invalidation",
            Fingerprint = new RepositoryFingerprint
            {
                RemoteUrl = "https://github.com/example/brain-ingest-invalidation.git",
                Host = "github.com",
                Owner = "example",
                RepoName = "brain-ingest-invalidation",
                DefaultBranch = "main",
            },
        });
        Assert.Equal(HttpStatusCode.OK, resolveResponse.StatusCode);

        var resolved = await resolveResponse.Content.ReadFromJsonAsync<ProjectResolutionResponse>();
        Assert.NotNull(resolved?.Project?.ProjectId);
        var projectId = resolved!.Project!.ProjectId;

        foreach (var item in new[]
                 {
                     new Dictionary<string, object?>
                     {
                         ["action"] = "ingest",
                         ["key"] = "test-runner-jest",
                         ["value"] = "jest",
                         ["kind"] = "fact",
                         ["category"] = "testing",
                         ["source_type"] = "stop",
                         ["source_scope"] = "session-201",
                         ["promotion_identity"] = "stop:session-201:testing:test-runner",
                         ["logical_key"] = "testing:test-runner",
                         ["confidence"] = 0.80,
                     },
                     new Dictionary<string, object?>
                     {
                         ["action"] = "ingest",
                         ["key"] = "test-runner-vitest",
                         ["value"] = "vitest",
                         ["kind"] = "correction",
                         ["category"] = "testing",
                         ["source_type"] = "stop",
                         ["source_scope"] = "session-202",
                         ["promotion_identity"] = "stop:session-202:testing:test-runner",
                         ["logical_key"] = "testing:test-runner",
                         ["confidence"] = 0.92,
                     },
                 })
        {
            var ingestResponse = await _client.PostAsJsonAsync("/v1/tools/call", new ToolCallRequest
            {
                Name = "ctx_brain",
                ProjectId = projectId,
                Arguments = item,
            });
            Assert.Equal(HttpStatusCode.OK, ingestResponse.StatusCode);
        }

        var payload = await _client.GetFromJsonAsync<ProjectMemoryResponse>($"/api/dashboard/projects/{projectId}/memory");
        Assert.NotNull(payload);
        Assert.Contains(payload!.Brain, item => item.Key == "test-runner-jest" && item.LifecycleStatus == "invalidated");
        Assert.Contains(payload.Brain, item => item.Key == "test-runner-vitest" && item.LifecycleStatus == "current" && item.EntryType == "correction");
    }

    /// <summary>
    /// Knowledge consolidate promotes the latest server session findings and decisions into project knowledge.
    /// </summary>
    [Fact]
    public async Task ToolCall_KnowledgeConsolidate_PromotesLatestSessionState()
    {
        var resolveResponse = await _client.PostAsJsonAsync("/v1/projects/resolve", new ProjectResolutionRequest
        {
            SuggestedSlug = "knowledge-consolidate",
            Fingerprint = new RepositoryFingerprint
            {
                RemoteUrl = "https://github.com/example/knowledge-consolidate.git",
                Host = "github.com",
                Owner = "example",
                RepoName = "knowledge-consolidate",
                DefaultBranch = "main",
            },
        });
        Assert.Equal(HttpStatusCode.OK, resolveResponse.StatusCode);

        var resolved = await resolveResponse.Content.ReadFromJsonAsync<ProjectResolutionResponse>();
        Assert.NotNull(resolved?.Project?.ProjectId);
        var projectId = resolved!.Project!.ProjectId;

        foreach (var action in new[]
                 {
                     new Dictionary<string, object?> { ["action"] = "task", ["value"] = "stabilize hosted memory" },
                     new Dictionary<string, object?> { ["action"] = "finding", ["value"] = "dashboard domain map is now overview/memory/agents" },
                     new Dictionary<string, object?> { ["action"] = "decision", ["value"] = "server owns canonical project knowledge" },
                 })
        {
            var sessionResponse = await _client.PostAsJsonAsync("/v1/tools/call", new ToolCallRequest
            {
                Name = "ctx_session",
                ProjectId = projectId,
                Arguments = action,
            });
            Assert.Equal(HttpStatusCode.OK, sessionResponse.StatusCode);
        }

        var consolidateResponse = await _client.PostAsJsonAsync("/v1/tools/call", new ToolCallRequest
        {
            Name = "ctx_knowledge",
            ProjectId = projectId,
            Arguments = new Dictionary<string, object?>
            {
                ["action"] = "consolidate",
            },
        });
        Assert.Equal(HttpStatusCode.OK, consolidateResponse.StatusCode);

        var payload = await _client.GetFromJsonAsync<ProjectMemoryResponse>($"/api/dashboard/projects/{projectId}/memory");
        Assert.NotNull(payload);
        Assert.Contains(payload!.Knowledge, item => item.Category == "finding" && item.Value.Contains("dashboard domain map", StringComparison.Ordinal));
        Assert.Contains(payload.Knowledge, item => item.Category == "decision" && item.Value.Contains("canonical project knowledge", StringComparison.Ordinal));
        Assert.Contains(payload.Knowledge, item => item.Category == "session" && item.Value.Contains("stabilize hosted memory", StringComparison.Ordinal));
    }

    /// <summary>
    /// Knowledge promote ingests explicit client-side memory candidates into canonical server knowledge.
    /// </summary>
    [Fact]
    public async Task ToolCall_KnowledgePromote_IngestsCandidates()
    {
        var resolveResponse = await _client.PostAsJsonAsync("/v1/projects/resolve", new ProjectResolutionRequest
        {
            SuggestedSlug = "knowledge-promote",
            Fingerprint = new RepositoryFingerprint
            {
                RemoteUrl = "https://github.com/example/knowledge-promote.git",
                Host = "github.com",
                Owner = "example",
                RepoName = "knowledge-promote",
                DefaultBranch = "main",
            },
        });
        Assert.Equal(HttpStatusCode.OK, resolveResponse.StatusCode);

        var resolved = await resolveResponse.Content.ReadFromJsonAsync<ProjectResolutionResponse>();
        Assert.NotNull(resolved?.Project?.ProjectId);
        var projectId = resolved!.Project!.ProjectId;

        var promoteResponse = await _client.PostAsJsonAsync("/v1/tools/call", new ToolCallRequest
        {
            Name = "ctx_knowledge",
            ProjectId = projectId,
            Arguments = new Dictionary<string, object?>
            {
                ["action"] = "promote",
                ["items"] = new object?[]
                {
                    new Dictionary<string, object?>
                    {
                        ["category"] = "decision",
                        ["key"] = "memory-owner",
                        ["value"] = "server owns canonical knowledge",
                        ["confidence"] = 0.95,
                    },
                    new Dictionary<string, object?>
                    {
                        ["category"] = "finding",
                        ["key"] = "hook-surface",
                        ["value"] = "OpenCode exposes system transform and compacting hooks",
                        ["confidence"] = 0.85,
                    },
                },
            },
        });
        Assert.Equal(HttpStatusCode.OK, promoteResponse.StatusCode);

        var payload = await _client.GetFromJsonAsync<ProjectMemoryResponse>($"/api/dashboard/projects/{projectId}/memory");
        Assert.NotNull(payload);
        Assert.Contains(payload!.Knowledge, item => item.Category == "decision" && item.Key == "memory-owner");
        Assert.Contains(payload.Knowledge, item => item.Category == "finding" && item.Key == "hook-surface");
        Assert.Contains(payload.Knowledge, item => item.PromotionIdentity.Contains("promote", StringComparison.Ordinal));
        Assert.All(payload.Knowledge, item =>
        {
            Assert.False(string.IsNullOrWhiteSpace(item.LogicalKey));
            Assert.False(string.IsNullOrWhiteSpace(item.PromotionIdentity));
            Assert.True(item.CreatedAt <= item.UpdatedAt);
        });
    }

    /// <summary>
    /// Re-promoting the same candidate keeps a stable promotion identity and retains historical revisions.
    /// </summary>
    [Fact]
    public async Task ToolCall_KnowledgePromote_ReusesIdentityAndPreservesHistory()
    {
        var resolveResponse = await _client.PostAsJsonAsync("/v1/projects/resolve", new ProjectResolutionRequest
        {
            SuggestedSlug = "knowledge-promote-history",
            Fingerprint = new RepositoryFingerprint
            {
                RemoteUrl = "https://github.com/example/knowledge-promote-history.git",
                Host = "github.com",
                Owner = "example",
                RepoName = "knowledge-promote-history",
                DefaultBranch = "main",
            },
        });
        Assert.Equal(HttpStatusCode.OK, resolveResponse.StatusCode);

        var resolved = await resolveResponse.Content.ReadFromJsonAsync<ProjectResolutionResponse>();
        Assert.NotNull(resolved?.Project?.ProjectId);
        var projectId = resolved!.Project!.ProjectId;

        foreach (var value in new[] { "server owns canonical knowledge", "server owns hosted canonical knowledge" })
        {
            var promoteResponse = await _client.PostAsJsonAsync("/v1/tools/call", new ToolCallRequest
            {
                Name = "ctx_knowledge",
                ProjectId = projectId,
                Arguments = new Dictionary<string, object?>
                {
                    ["action"] = "promote",
                    ["items"] = new object?[]
                    {
                        new Dictionary<string, object?>
                        {
                            ["category"] = "decision",
                            ["key"] = "memory-owner",
                            ["value"] = value,
                            ["confidence"] = 0.95,
                            ["source_type"] = "promote",
                            ["source_scope"] = "session-123",
                            ["promotion_identity"] = "promote:session-123:decision:decision-memory-owner",
                        },
                    },
                },
            });
            Assert.Equal(HttpStatusCode.OK, promoteResponse.StatusCode);
        }

        var payload = await _client.GetFromJsonAsync<ProjectMemoryResponse>($"/api/dashboard/projects/{projectId}/memory");
        Assert.NotNull(payload);

        var fact = Assert.Single(payload!.Knowledge, item => item.Category == "decision" && item.Key == "memory-owner");
        Assert.Equal("promote:session-123:decision:decision-memory-owner", fact.PromotionIdentity);
        var history = Assert.Single(fact.History);
        Assert.Equal("server owns canonical knowledge", history.Value);
        Assert.Equal("current", fact.LifecycleStatus);
        Assert.True(payload.Health?.HistoryEntries >= 1);
    }

    /// <summary>
    /// Knowledge upkeep rescales lifecycle state and surfaces wake-up candidates in status.
    /// </summary>
    [Fact]
    public async Task ToolCall_KnowledgeUpkeep_ReturnsLifecycleSummary()
    {
        var resolveResponse = await _client.PostAsJsonAsync("/v1/projects/resolve", new ProjectResolutionRequest
        {
            SuggestedSlug = "knowledge-upkeep",
            Fingerprint = new RepositoryFingerprint
            {
                RemoteUrl = "https://github.com/example/knowledge-upkeep.git",
                Host = "github.com",
                Owner = "example",
                RepoName = "knowledge-upkeep",
                DefaultBranch = "main",
            },
        });
        Assert.Equal(HttpStatusCode.OK, resolveResponse.StatusCode);

        var resolved = await resolveResponse.Content.ReadFromJsonAsync<ProjectResolutionResponse>();
        Assert.NotNull(resolved?.Project?.ProjectId);
        var projectId = resolved!.Project!.ProjectId;

        var promoteResponse = await _client.PostAsJsonAsync("/v1/tools/call", new ToolCallRequest
        {
            Name = "ctx_knowledge",
            ProjectId = projectId,
            Arguments = new Dictionary<string, object?>
            {
                ["action"] = "promote",
                ["items"] = new object?[]
                {
                    new Dictionary<string, object?>
                    {
                        ["category"] = "decision",
                        ["key"] = "hosted-owner",
                        ["value"] = "server owns canonical memory",
                        ["confidence"] = 0.95,
                        ["source_type"] = "promote",
                        ["source_scope"] = "session-keep",
                    },
                    new Dictionary<string, object?>
                    {
                        ["category"] = "finding",
                        ["key"] = "warmup",
                        ["value"] = "bounded wake-up should stay compact",
                        ["confidence"] = 0.72,
                        ["source_type"] = "promote",
                        ["source_scope"] = "session-keep",
                    },
                },
            },
        });
        Assert.Equal(HttpStatusCode.OK, promoteResponse.StatusCode);

        var upkeepResponse = await _client.PostAsJsonAsync("/v1/tools/call", new ToolCallRequest
        {
            Name = "ctx_knowledge",
            ProjectId = projectId,
            Arguments = new Dictionary<string, object?>
            {
                ["action"] = "upkeep",
            },
        });
        Assert.Equal(HttpStatusCode.OK, upkeepResponse.StatusCode);

        var upkeepPayload = await upkeepResponse.Content.ReadFromJsonAsync<ToolCallResponse>();
        Assert.NotNull(upkeepPayload);
        var upkeepJson = Assert.IsAssignableFrom<JsonElement>(upkeepPayload!.Result);
        Assert.True(upkeepJson.TryGetProperty("rescored", out var rescored));
        Assert.True(rescored.GetInt32() >= 2);
        Assert.True(upkeepJson.TryGetProperty("top_wakeup", out var wakeup));
        Assert.True(wakeup.GetArrayLength() >= 1);

        var statusResponse = await _client.PostAsJsonAsync("/v1/tools/call", new ToolCallRequest
        {
            Name = "ctx_knowledge",
            ProjectId = projectId,
            Arguments = new Dictionary<string, object?>
            {
                ["action"] = "status",
            },
        });
        Assert.Equal(HttpStatusCode.OK, statusResponse.StatusCode);

        var statusPayload = await statusResponse.Content.ReadFromJsonAsync<ToolCallResponse>();
        Assert.NotNull(statusPayload);
        var statusJson = Assert.IsAssignableFrom<JsonElement>(statusPayload!.Result);
        Assert.True(statusJson.TryGetProperty("average_lifecycle_score", out _));
        Assert.True(statusJson.TryGetProperty("current_fact_count", out var currentFactCount));
        Assert.Equal(2, currentFactCount.GetInt32());
    }

    /// <summary>
    /// Hosted wake-up returns a bounded memory briefing built from current canonical facts.
    /// </summary>
    [Fact]
    public async Task ToolCall_KnowledgeWakeup_ReturnsBoundedBriefing()
    {
        var resolveResponse = await _client.PostAsJsonAsync("/v1/projects/resolve", new ProjectResolutionRequest
        {
            SuggestedSlug = "knowledge-wakeup",
            Fingerprint = new RepositoryFingerprint
            {
                RemoteUrl = "https://github.com/example/knowledge-wakeup.git",
                Host = "github.com",
                Owner = "example",
                RepoName = "knowledge-wakeup",
                DefaultBranch = "main",
            },
        });
        Assert.Equal(HttpStatusCode.OK, resolveResponse.StatusCode);

        var resolved = await resolveResponse.Content.ReadFromJsonAsync<ProjectResolutionResponse>();
        Assert.NotNull(resolved?.Project?.ProjectId);
        var projectId = resolved!.Project!.ProjectId;

        var promoteResponse = await _client.PostAsJsonAsync("/v1/tools/call", new ToolCallRequest
        {
            Name = "ctx_knowledge",
            ProjectId = projectId,
            Arguments = new Dictionary<string, object?>
            {
                ["action"] = "promote",
                ["items"] = Enumerable.Range(1, 10).Select(index => (object?)new Dictionary<string, object?>
                {
                    ["category"] = index <= 5 ? "decision" : "finding",
                    ["key"] = $"fact-{index}",
                    ["value"] = $"memory item {index}",
                    ["confidence"] = 0.9 - (index * 0.02),
                    ["source_type"] = "promote",
                    ["source_scope"] = "session-wakeup",
                }).ToArray(),
            },
        });
        Assert.Equal(HttpStatusCode.OK, promoteResponse.StatusCode);

        var wakeupResponse = await _client.PostAsJsonAsync("/v1/tools/call", new ToolCallRequest
        {
            Name = "ctx_knowledge",
            ProjectId = projectId,
            Arguments = new Dictionary<string, object?>
            {
                ["action"] = "wakeup",
            },
        });
        Assert.Equal(HttpStatusCode.OK, wakeupResponse.StatusCode);

        var wakeupPayload = await wakeupResponse.Content.ReadFromJsonAsync<ToolCallResponse>();
        Assert.NotNull(wakeupPayload);
        var wakeupJson = Assert.IsAssignableFrom<JsonElement>(wakeupPayload!.Result);
        Assert.Equal(8, wakeupJson.GetProperty("budget").GetInt32());
        Assert.True(wakeupJson.GetProperty("selected_count").GetInt32() <= 8);
        Assert.Contains("WAKE-UP BRIEFING", wakeupJson.GetProperty("briefing").GetString(), StringComparison.Ordinal);
    }

    /// <summary>
    /// Hosted triage previews duplicate and junk-like candidates without mutating canonical memory by default.
    /// </summary>
    [Fact]
    public async Task ToolCall_KnowledgeTriage_PreviewsCandidates()
    {
        var resolveResponse = await _client.PostAsJsonAsync("/v1/projects/resolve", new ProjectResolutionRequest
        {
            SuggestedSlug = "knowledge-triage",
            Fingerprint = new RepositoryFingerprint
            {
                RemoteUrl = "https://github.com/example/knowledge-triage.git",
                Host = "github.com",
                Owner = "example",
                RepoName = "knowledge-triage",
                DefaultBranch = "main",
            },
        });
        Assert.Equal(HttpStatusCode.OK, resolveResponse.StatusCode);

        var resolved = await resolveResponse.Content.ReadFromJsonAsync<ProjectResolutionResponse>();
        Assert.NotNull(resolved?.Project?.ProjectId);
        var projectId = resolved!.Project!.ProjectId;

        var promoteResponse = await _client.PostAsJsonAsync("/v1/tools/call", new ToolCallRequest
        {
            Name = "ctx_knowledge",
            ProjectId = projectId,
            Arguments = new Dictionary<string, object?>
            {
                ["action"] = "promote",
                ["items"] = new object?[]
                {
                    new Dictionary<string, object?>
                    {
                        ["category"] = "decision",
                        ["key"] = "dup-a",
                        ["value"] = "server owns canonical memory",
                        ["confidence"] = 0.95,
                    },
                    new Dictionary<string, object?>
                    {
                        ["category"] = "decision",
                        ["key"] = "dup-b",
                        ["value"] = "server owns canonical memory",
                        ["confidence"] = 0.82,
                    },
                    new Dictionary<string, object?>
                    {
                        ["category"] = "testing:demo",
                        ["key"] = "demo-placeholder",
                        ["value"] = "demo placeholder memory",
                        ["confidence"] = 0.6,
                    },
                },
            },
        });
        Assert.Equal(HttpStatusCode.OK, promoteResponse.StatusCode);

        var triageResponse = await _client.PostAsJsonAsync("/v1/tools/call", new ToolCallRequest
        {
            Name = "ctx_knowledge",
            ProjectId = projectId,
            Arguments = new Dictionary<string, object?>
            {
                ["action"] = "triage",
            },
        });
        Assert.Equal(HttpStatusCode.OK, triageResponse.StatusCode);

        var triagePayload = await triageResponse.Content.ReadFromJsonAsync<ToolCallResponse>();
        Assert.NotNull(triagePayload);
        var triageJson = Assert.IsAssignableFrom<JsonElement>(triagePayload!.Result);
        Assert.Equal("preview", triageJson.GetProperty("mode").GetString());
        Assert.True(triageJson.GetProperty("duplicate_groups").GetArrayLength() >= 1);
        Assert.True(triageJson.GetProperty("junk_candidates").GetArrayLength() >= 1);

        var memoryPayload = await _client.GetFromJsonAsync<ProjectMemoryResponse>($"/api/dashboard/projects/{projectId}/memory");
        Assert.NotNull(memoryPayload);
        Assert.Null(memoryPayload!.Triage);

        var memoryWithTriage = await _client.GetFromJsonAsync<ProjectMemoryResponse>($"/api/dashboard/projects/{projectId}/memory?include_triage=true");
        Assert.NotNull(memoryWithTriage);
        Assert.NotNull(memoryWithTriage!.Triage);
        Assert.Equal("preview", memoryWithTriage.Triage!.Mode);
    }

    /// <summary>
    /// Dashboard triage apply returns applied-action summaries and mutates canonical memory safely.
    /// </summary>
    [Fact]
    public async Task DashboardProjectMemoryTriage_Apply_ReturnsAppliedActions()
    {
        var resolveResponse = await _client.PostAsJsonAsync("/v1/projects/resolve", new ProjectResolutionRequest
        {
            SuggestedSlug = "knowledge-triage-apply",
            Fingerprint = new RepositoryFingerprint
            {
                RemoteUrl = "https://github.com/example/knowledge-triage-apply.git",
                Host = "github.com",
                Owner = "example",
                RepoName = "knowledge-triage-apply",
                DefaultBranch = "main",
            },
        });
        Assert.Equal(HttpStatusCode.OK, resolveResponse.StatusCode);

        var resolved = await resolveResponse.Content.ReadFromJsonAsync<ProjectResolutionResponse>();
        Assert.NotNull(resolved?.Project?.ProjectId);
        var projectId = resolved!.Project!.ProjectId;

        var promoteResponse = await _client.PostAsJsonAsync("/v1/tools/call", new ToolCallRequest
        {
            Name = "ctx_knowledge",
            ProjectId = projectId,
            Arguments = new Dictionary<string, object?>
            {
                ["action"] = "promote",
                ["items"] = new object?[]
                {
                    new Dictionary<string, object?>
                    {
                        ["category"] = "decision",
                        ["key"] = "dup-a",
                        ["value"] = "server owns canonical memory",
                        ["confidence"] = 0.95,
                    },
                    new Dictionary<string, object?>
                    {
                        ["category"] = "decision",
                        ["key"] = "dup-b",
                        ["value"] = "server owns canonical memory",
                        ["confidence"] = 0.82,
                    },
                    new Dictionary<string, object?>
                    {
                        ["category"] = "testing:demo",
                        ["key"] = "demo-placeholder",
                        ["value"] = "demo placeholder memory",
                        ["confidence"] = 0.6,
                    },
                },
            },
        });
        Assert.Equal(HttpStatusCode.OK, promoteResponse.StatusCode);

        var triageApplyResponse = await _client.PostAsync($"/api/dashboard/projects/{projectId}/memory/triage?mode=apply", content: null);
        Assert.Equal(HttpStatusCode.OK, triageApplyResponse.StatusCode);

        using var triageDoc = JsonDocument.Parse(await triageApplyResponse.Content.ReadAsStringAsync());
        Assert.Equal("apply", triageDoc.RootElement.GetProperty("mode").GetString());
        Assert.True(triageDoc.RootElement.GetProperty("applied_actions").GetArrayLength() >= 2);

        var payload = await _client.GetFromJsonAsync<ProjectMemoryResponse>($"/api/dashboard/projects/{projectId}/memory");
        Assert.NotNull(payload);
        Assert.Null(payload!.Triage);
        Assert.Single(payload.Knowledge, item => item.Category == "decision" && item.LifecycleStatus == "current");
        Assert.Contains(payload.Knowledge, item => item.Key == "dup-b" && item.LifecycleStatus == "merged");
        Assert.Contains(payload.Knowledge, item => item.Key == "demo-placeholder" && item.LifecycleStatus == "junk");

        var payloadWithTriage = await _client.GetFromJsonAsync<ProjectMemoryResponse>($"/api/dashboard/projects/{projectId}/memory?include_triage=true");
        Assert.NotNull(payloadWithTriage);
        Assert.NotNull(payloadWithTriage!.Triage);
        Assert.Equal("preview", payloadWithTriage.Triage!.Mode);
    }

    /// <summary>
    /// Per-project dashboard memory endpoint returns not found for an unknown project.
    /// </summary>
    [Fact]
    public async Task DashboardProjectMemory_Returns404ForUnknownProject()
    {
        var response = await _client.GetAsync("/api/dashboard/projects/missing/memory");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>
    /// Dashboard project delete clears hosted project data and removes the project record.
    /// </summary>
    [Fact]
    public async Task DashboardProjectDelete_ClearsHostedProjectData()
    {
        var resolveResponse = await _client.PostAsJsonAsync("/v1/projects/resolve", new ProjectResolutionRequest
        {
            SuggestedSlug = "dashboard-delete-project",
            Fingerprint = new RepositoryFingerprint
            {
                RemoteUrl = "https://github.com/example/dashboard-delete-project.git",
                Host = "github.com",
                Owner = "example",
                RepoName = "dashboard-delete-project",
                DefaultBranch = "main",
            },
            CheckoutBinding = new CheckoutBinding
            {
                ProjectId = "ignored",
                LocalRoot = "/tmp/dashboard-delete-project",
                Branch = "main",
                ClientLabel = "test-client",
            },
        });
        Assert.Equal(HttpStatusCode.OK, resolveResponse.StatusCode);

        var resolved = await resolveResponse.Content.ReadFromJsonAsync<ProjectResolutionResponse>();
        Assert.NotNull(resolved?.Project?.ProjectId);
        var projectId = resolved!.Project!.ProjectId;

        var brainStoreResponse = await _client.PostAsJsonAsync("/v1/tools/call", new ToolCallRequest
        {
            Name = "ctx_brain",
            ProjectId = projectId,
            Arguments = new Dictionary<string, object?>
            {
                ["action"] = "store",
                ["key"] = "delete-me",
                ["value"] = "temporary memory",
            },
        });
        Assert.Equal(HttpStatusCode.OK, brainStoreResponse.StatusCode);

        var rememberResponse = await _client.PostAsJsonAsync("/v1/tools/call", new ToolCallRequest
        {
            Name = "ctx_knowledge",
            ProjectId = projectId,
            Arguments = new Dictionary<string, object?>
            {
                ["action"] = "remember",
                ["category"] = "testing",
                ["key"] = "delete-me",
                ["value"] = "temporary fact",
                ["confidence"] = 0.9,
            },
        });
        Assert.Equal(HttpStatusCode.OK, rememberResponse.StatusCode);

        var deleteResponse = await _client.DeleteAsync($"/api/projects/{projectId}");
        Assert.Equal(HttpStatusCode.OK, deleteResponse.StatusCode);

        using var deleteDoc = JsonDocument.Parse(await deleteResponse.Content.ReadAsStringAsync());
        Assert.True(deleteDoc.RootElement.GetProperty("deleted").GetBoolean());
        Assert.Equal(projectId, deleteDoc.RootElement.GetProperty("projectId").GetString());

        var projectListResponse = await _client.GetAsync("/api/projects");
        Assert.Equal(HttpStatusCode.OK, projectListResponse.StatusCode);
        using var projectListDoc = JsonDocument.Parse(await projectListResponse.Content.ReadAsStringAsync());
        Assert.DoesNotContain(projectListDoc.RootElement.GetProperty("projects").EnumerateArray(), item => item.GetProperty("project_id").GetString() == projectId);

        var memoryResponse = await _client.GetAsync($"/api/dashboard/projects/{projectId}/memory");
        Assert.Equal(HttpStatusCode.NotFound, memoryResponse.StatusCode);
    }

    /// <summary>
    /// Dashboard brain cleanup by entry type deletes entries by stored kind, not by key prefix.
    /// </summary>
    [Fact]
    public async Task DashboardProjectMemoryBrainTypeDelete_UsesEntryKind()
    {
        var resolveResponse = await _client.PostAsJsonAsync("/v1/projects/resolve", new ProjectResolutionRequest
        {
            SuggestedSlug = "dashboard-brain-type-delete",
            Fingerprint = new RepositoryFingerprint
            {
                RemoteUrl = "https://github.com/example/dashboard-brain-type-delete.git",
                Host = "github.com",
                Owner = "example",
                RepoName = "dashboard-brain-type-delete",
                DefaultBranch = "main",
            },
        });
        Assert.Equal(HttpStatusCode.OK, resolveResponse.StatusCode);

        var resolved = await resolveResponse.Content.ReadFromJsonAsync<ProjectResolutionResponse>();
        Assert.NotNull(resolved?.Project?.ProjectId);
        var projectId = resolved!.Project!.ProjectId;

        foreach (var arguments in new[]
                 {
                     new Dictionary<string, object?>
                     {
                         ["action"] = "ingest",
                         ["key"] = "prompt-001",
                         ["value"] = "user asked for cleanup",
                         ["kind"] = "user_prompt",
                         ["category"] = "workflow",
                         ["source_type"] = "hook",
                         ["source_scope"] = "session-1",
                         ["promotion_identity"] = "hook:session-1:user-prompt",
                         ["logical_key"] = "workflow:user-prompt:1",
                     },
                     new Dictionary<string, object?>
                     {
                         ["action"] = "ingest",
                         ["key"] = "assistant-001",
                         ["value"] = "assistant replied",
                         ["kind"] = "assistant_output",
                         ["category"] = "workflow",
                         ["source_type"] = "hook",
                         ["source_scope"] = "session-1",
                         ["promotion_identity"] = "hook:session-1:assistant-output",
                         ["logical_key"] = "workflow:assistant-output:1",
                     },
                 })
        {
            var ingestResponse = await _client.PostAsJsonAsync("/v1/tools/call", new ToolCallRequest
            {
                Name = "ctx_brain",
                ProjectId = projectId,
                Arguments = arguments,
            });
            Assert.Equal(HttpStatusCode.OK, ingestResponse.StatusCode);
        }

        var deleteResponse = await _client.DeleteAsync($"/api/dashboard/projects/{projectId}/memory/brain/type/user_prompt");
        Assert.Equal(HttpStatusCode.OK, deleteResponse.StatusCode);

        using var deleteDoc = JsonDocument.Parse(await deleteResponse.Content.ReadAsStringAsync());
        Assert.Equal(1, deleteDoc.RootElement.GetProperty("deleted").GetInt32());

        var payload = await _client.GetFromJsonAsync<ProjectMemoryResponse>($"/api/dashboard/projects/{projectId}/memory");
        Assert.NotNull(payload);
        Assert.DoesNotContain(payload!.Brain, item => item.EntryType == "user_prompt");
        Assert.Contains(payload.Brain, item => item.EntryType == "assistant_output");
    }

    /// <summary>
    /// Tool calls with the same repository fingerprint reuse the same canonical project across different local roots.
    /// </summary>
    [Fact]
    public async Task ToolCall_ProjectFingerprint_ReusesCanonicalProject()
    {
        var fingerprint = new RepositoryFingerprint
        {
            RemoteUrl = "https://github.com/MarkBovee/nebu-ctx.git",
            Host = "github.com",
            Owner = "MarkBovee",
            RepoName = "nebu-ctx",
            DefaultBranch = "main",
        };

        var storeResponse = await _client.PostAsJsonAsync("/v1/tools/call", new ToolCallRequest
        {
            Name = "ctx_brain",
            RepositoryFingerprint = fingerprint,
            ProjectSlug = "nebu-ctx",
            WorkspaceBinding = new CheckoutBinding
            {
                ProjectId = "ignored-by-server",
                LocalRoot = "/tmp/root-a",
                Branch = "main",
                ClientLabel = "root-a",
            },
            Arguments = new Dictionary<string, object?>
            {
                ["action"] = "store",
                ["key"] = "shared-key",
                ["value"] = "shared-value",
            },
        });
        Assert.Equal(HttpStatusCode.OK, storeResponse.StatusCode);

        var recallResponse = await _client.PostAsJsonAsync("/v1/tools/call", new ToolCallRequest
        {
            Name = "ctx_brain",
            RepositoryFingerprint = fingerprint,
            ProjectSlug = "nebu-ctx",
            WorkspaceBinding = new CheckoutBinding
            {
                ProjectId = "ignored-by-server",
                LocalRoot = "/tmp/root-b",
                Branch = "main",
                ClientLabel = "root-b",
            },
            Arguments = new Dictionary<string, object?>
            {
                ["action"] = "recall",
                ["query"] = "shared-key",
            },
        });
        Assert.Equal(HttpStatusCode.OK, recallResponse.StatusCode);

        var recallPayload = await recallResponse.Content.ReadAsStringAsync();
        Assert.Contains("shared-key", recallPayload, StringComparison.Ordinal);
        Assert.Contains("shared-value", recallPayload, StringComparison.Ordinal);
    }

    /// <summary>
    /// Dashboard root serves the shipped HTML asset.
    /// </summary>
    [Fact]
    public async Task DashboardRoot_ReturnsHtml()
    {
        var response = await _client.GetAsync("/");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);

        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("nebu-ctx Observatory", html, StringComparison.Ordinal);
    }

    /// <summary>
    /// Dashboard HTML loader falls back to the published Dashboard/ subdirectory.
    /// </summary>
    [Fact]
    public void DashboardHtmlProvider_LoadHtml_ReadsPublishedDashboardSubdirectory()
    {
        var baseDirectory = AppContext.BaseDirectory;
        var dashboardDirectory = Path.Combine(baseDirectory, "Dashboard");
        var dashboardPath = Path.Combine(dashboardDirectory, "dashboard.html");
        var backupPath = Path.Combine(baseDirectory, "dashboard.html.test-backup");
        var rootDashboardPath = Path.Combine(baseDirectory, "dashboard.html");
        var rootDashboardExisted = File.Exists(rootDashboardPath);

        Directory.CreateDirectory(dashboardDirectory);

        if (rootDashboardExisted)
        {
            File.Move(rootDashboardPath, backupPath, overwrite: true);
        }

        try
        {
            File.WriteAllText(dashboardPath, "<html><body>published dashboard asset</body></html>");

            var html = DashboardHtmlProvider.LoadHtml();

            Assert.Contains("published dashboard asset", html, StringComparison.Ordinal);
            Assert.DoesNotContain("was not copied to the output directory", html, StringComparison.Ordinal);
        }
        finally
        {
            if (File.Exists(dashboardPath))
            {
                File.Delete(dashboardPath);
            }

            if (rootDashboardExisted && File.Exists(backupPath))
            {
                File.Move(backupPath, rootDashboardPath, overwrite: true);
            }
        }
    }

    /// <summary>
    /// Dashboard version endpoint returns the compatibility fields used by the legacy UI.
    /// </summary>
    [Fact]
    public async Task DashboardVersion_ReturnsLegacyCompatibilityFields()
    {
        var response = await _client.GetAsync("/api/version");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadAsStringAsync();
        Assert.Contains("current", payload, StringComparison.Ordinal);
        Assert.Contains("update_available", payload, StringComparison.Ordinal);
    }

    /// <summary>
    /// Dashboard search index endpoint returns tool metadata.
    /// </summary>
    [Fact]
    public async Task DashboardSearchIndex_ReturnsMetadata()
    {
        var response = await _client.GetAsync("/api/search-index");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadAsStringAsync();
        Assert.Contains("doc_count", payload, StringComparison.Ordinal);
        Assert.Contains("top_chunks_by_token_count", payload, StringComparison.Ordinal);
    }

    /// <summary>
    /// Dashboard graph endpoint returns known source files for graph- and compression-driven views.
    /// </summary>
    [Fact]
    public async Task DashboardGraph_ReturnsKnownFiles()
    {
        await _client.PostAsJsonAsync("/v1/projects/resolve", new ProjectResolutionRequest
        {
            SuggestedSlug = "nebu-ctx",
            Fingerprint = new RepositoryFingerprint
            {
                RemoteUrl = "https://github.com/MarkBovee/nebu-ctx.git",
                Host = "github.com",
                Owner = "MarkBovee",
                RepoName = "nebu-ctx",
                DefaultBranch = "main",
            },
            ProjectMetadata = new ProjectMetadataEnvelope
            {
                SchemaVersion = 1,
                Summary = new ProjectMetadataSummary
                {
                    TotalFileCount = 12,
                    SourceFileCount = 7,
                    Markers = [".git", "Cargo.toml"],
                    Languages = [new ProjectLanguageStat { Language = "rust", FileCount = 7 }],
                },
            },
        });

        var response = await _client.GetAsync("/api/graph");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadAsStringAsync();
        Assert.Contains("files", payload, StringComparison.Ordinal);
        Assert.Contains("project/nebu-ctx", payload, StringComparison.Ordinal);
        Assert.Contains("project-language", payload, StringComparison.Ordinal);
        Assert.Contains("indexed_file_count", payload, StringComparison.Ordinal);
    }

    /// <summary>
    /// Dashboard knowledge endpoint returns facts derived from persisted project metadata.
    /// </summary>
    [Fact]
    public async Task DashboardKnowledge_ReturnsProjectFacts()
    {
        await _client.PostAsJsonAsync("/v1/projects/resolve", new ProjectResolutionRequest
        {
            SuggestedSlug = "nebu-ctx",
            Fingerprint = new RepositoryFingerprint
            {
                RemoteUrl = "https://github.com/MarkBovee/nebu-ctx.git",
                Host = "github.com",
                Owner = "MarkBovee",
                RepoName = "nebu-ctx",
                DefaultBranch = "main",
            },
            ProjectMetadata = new ProjectMetadataEnvelope
            {
                SchemaVersion = 1,
                Summary = new ProjectMetadataSummary
                {
                    TotalFileCount = 20,
                    SourceFileCount = 8,
                    Markers = [".git", "Cargo.toml"],
                    Languages = [new ProjectLanguageStat { Language = "rust", FileCount = 8 }],
                },
            },
        });

        var response = await _client.GetAsync("/api/knowledge");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadAsStringAsync();
        Assert.Contains("facts", payload, StringComparison.Ordinal);
        Assert.Contains("project_name", payload, StringComparison.Ordinal);
        Assert.Contains("fact_name", payload, StringComparison.Ordinal);
        Assert.Contains("Repository Host", payload, StringComparison.Ordinal);
        Assert.Contains("architecture:language", payload, StringComparison.Ordinal);
        Assert.Contains("rust", payload, StringComparison.Ordinal);
    }

    /// <summary>
    /// Dashboard knowledge clear endpoint removes persisted facts for a project.
    /// </summary>
    [Fact]
    public async Task DashboardKnowledge_ClearProjectFacts_RemovesFacts()
    {
        var resolveResponse = await _client.PostAsJsonAsync("/v1/projects/resolve", new ProjectResolutionRequest
        {
            SuggestedSlug = "stale-project",
            Fingerprint = new RepositoryFingerprint
            {
                RemoteUrl = "https://github.com/MarkBovee/stale-project.git",
                Host = "github.com",
                Owner = "MarkBovee",
                RepoName = "stale-project",
                DefaultBranch = "main",
            },
            ProjectMetadata = new ProjectMetadataEnvelope
            {
                SchemaVersion = 1,
                Summary = new ProjectMetadataSummary
                {
                    TotalFileCount = 6,
                    SourceFileCount = 3,
                    Markers = [".git"],
                    Languages = [new ProjectLanguageStat { Language = "rust", FileCount = 3 }],
                },
            },
        });

        var resolution = await resolveResponse.Content.ReadFromJsonAsync<ProjectResolutionResponse>();
        Assert.NotNull(resolution?.Project?.ProjectId);

        var rememberResponse = await _client.PostAsJsonAsync("/v1/tools/call", new ToolCallRequest
        {
            Name = "ctx_knowledge",
            ProjectSlug = resolution!.Project!.Slug,
            RepositoryFingerprint = new RepositoryFingerprint
            {
                RemoteUrl = "https://github.com/MarkBovee/stale-project.git",
                Host = "github.com",
                Owner = "MarkBovee",
                RepoName = "stale-project",
                DefaultBranch = "main",
            },
            Arguments = new Dictionary<string, object?>
            {
                ["action"] = "remember",
                ["category"] = "workflow:notes",
                ["key"] = "stale-note",
                ["value"] = "stale project persisted fact",
                ["confidence"] = 0.95,
            },
        });
        Assert.Equal(HttpStatusCode.OK, rememberResponse.StatusCode);

        var beforeResponse = await _client.GetAsync("/api/knowledge");
        var beforePayload = await beforeResponse.Content.ReadAsStringAsync();
        Assert.Contains(resolution!.Project!.ProjectId, beforePayload, StringComparison.Ordinal);
        Assert.Contains("stale project persisted fact", beforePayload, StringComparison.Ordinal);

        var clearResponse = await _client.PostAsync($"/api/knowledge/projects/{resolution.Project.ProjectId}/clear", content: null);
        Assert.Equal(HttpStatusCode.OK, clearResponse.StatusCode);

        var response = await _client.GetAsync("/api/knowledge");
        var payload = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain(resolution.Project.ProjectId, payload, StringComparison.Ordinal);
        Assert.DoesNotContain("stale project persisted fact", payload, StringComparison.Ordinal);
    }

    /// <summary>
    /// Dashboard knowledge payload keeps projects with the same slug isolated by project identifier.
    /// </summary>
    [Fact]
    public void DashboardKnowledgePayload_DoesNotConflateProjectsWithSameSlug()
    {
        var projects = new[]
        {
            new ProjectRecord
            {
                ProjectId = "proj_mark_a",
                Slug = "mark",
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
                ProjectMetadata = new ProjectMetadataEnvelope
                {
                    SchemaVersion = 1,
                    Summary = new ProjectMetadataSummary
                    {
                        TotalFileCount = 4,
                        SourceFileCount = 2,
                        Markers = [".git"],
                        Languages = [new ProjectLanguageStat { Language = "rust", FileCount = 2 }],
                    },
                },
            },
            new ProjectRecord
            {
                ProjectId = "proj_mark_b",
                Slug = "mark",
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
                ProjectMetadata = new ProjectMetadataEnvelope
                {
                    SchemaVersion = 1,
                    Summary = new ProjectMetadataSummary
                    {
                        TotalFileCount = 9,
                        SourceFileCount = 5,
                        Markers = ["Cargo.toml"],
                        Languages = [new ProjectLanguageStat { Language = "csharp", FileCount = 5 }],
                    },
                },
            },
        };

        var payload = DashboardPayloadFactory.BuildKnowledgePayload(projects);
        var payloadJson = System.Text.Json.JsonSerializer.Serialize(payload);

        Assert.Contains("proj_mark_a", payloadJson, StringComparison.Ordinal);
        Assert.Contains("proj_mark_b", payloadJson, StringComparison.Ordinal);
        Assert.Contains("project:proj_mark_a:source-files", payloadJson, StringComparison.Ordinal);
        Assert.Contains("project:proj_mark_b:source-files", payloadJson, StringComparison.Ordinal);
    }

    /// <summary>
    /// Knowledge repair clears ambiguous short-slug project facts so the graph can rebuild cleanly.
    /// </summary>
    [Fact]
    public async Task DashboardKnowledge_Repair_ClearsAmbiguousShortSlugProjects()
    {
        var projectId = $"proj_{Guid.NewGuid():N}";
        await SeedLegacyProjectAsync(
            new ProjectRecord
            {
                ProjectId = projectId,
                Slug = "mark",
                Fingerprint = new RepositoryFingerprint(),
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
                ProjectMetadata = new ProjectMetadataEnvelope
                {
                    SchemaVersion = 1,
                    Summary = new ProjectMetadataSummary
                    {
                        TotalFileCount = 2,
                        SourceFileCount = 1,
                        Markers = ["README.md"],
                        Languages = [new ProjectLanguageStat { Language = "rust", FileCount = 1 }],
                    },
                },
            },
            new KnowledgeEntry
            {
                ProjectId = projectId,
                Category = "architecture:notes",
                Key = "ambiguous-mark",
                Value = "ambiguous short slug fact",
                Confidence = 0.9f,
                UpdatedAt = DateTimeOffset.UtcNow,
            });

        var beforeResponse = await _client.GetAsync("/api/knowledge");
        var beforePayload = await beforeResponse.Content.ReadAsStringAsync();
        Assert.Contains("ambiguous short slug fact", beforePayload, StringComparison.Ordinal);

        var repairResponse = await _client.PostAsync("/api/knowledge/repair", content: null);
        Assert.Equal(HttpStatusCode.OK, repairResponse.StatusCode);

        var knowledgeResponse = await _client.GetAsync("/api/knowledge");
        var payload = await knowledgeResponse.Content.ReadAsStringAsync();
        Assert.DoesNotContain("ambiguous short slug fact", payload, StringComparison.Ordinal);
        Assert.DoesNotContain("\"project_name\":\"mark\"", payload, StringComparison.Ordinal);
    }

    /// <summary>
    /// Knowledge repair leaves unrelated short slugs alone when they are not part of the known legacy issue.
    /// </summary>
    [Fact]
    public async Task DashboardKnowledge_Repair_DoesNotClearUnrelatedShortSlugProjects()
    {
        var projectId = $"proj_{Guid.NewGuid():N}";
        await SeedLegacyProjectAsync(
            new ProjectRecord
            {
                ProjectId = projectId,
                Slug = "api",
                Fingerprint = new RepositoryFingerprint(),
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
                ProjectMetadata = new ProjectMetadataEnvelope
                {
                    SchemaVersion = 1,
                    Summary = new ProjectMetadataSummary
                    {
                        TotalFileCount = 2,
                        SourceFileCount = 1,
                        Markers = ["README.md"],
                        Languages = [new ProjectLanguageStat { Language = "rust", FileCount = 1 }],
                    },
                },
            });

        var beforeResponse = await _client.GetAsync("/api/knowledge");
        var beforePayload = await beforeResponse.Content.ReadAsStringAsync();
        Assert.Contains("\"project_name\":\"api\"", beforePayload, StringComparison.Ordinal);

        var repairResponse = await _client.PostAsync("/api/knowledge/repair", content: null);
        Assert.Equal(HttpStatusCode.OK, repairResponse.StatusCode);

        var knowledgeResponse = await _client.GetAsync("/api/knowledge");
        var payload = await knowledgeResponse.Content.ReadAsStringAsync();
        Assert.Contains("\"project_name\":\"api\"", payload, StringComparison.Ordinal);
    }

    /// <summary>
    /// Dashboard stats endpoint reflects persisted project language metadata.
    /// </summary>
    [Fact]
    public async Task DashboardStats_ReturnsProjectMetadataSummary()
    {
        await _client.PostAsJsonAsync("/v1/projects/resolve", new ProjectResolutionRequest
        {
            SuggestedSlug = "nebu-ctx",
            Fingerprint = new RepositoryFingerprint
            {
                RemoteUrl = "https://github.com/MarkBovee/nebu-ctx.git",
                Host = "github.com",
                Owner = "MarkBovee",
                RepoName = "nebu-ctx",
                DefaultBranch = "main",
            },
            ProjectMetadata = new ProjectMetadataEnvelope
            {
                SchemaVersion = 1,
                Summary = new ProjectMetadataSummary
                {
                    TotalFileCount = 24,
                    SourceFileCount = 10,
                    Markers = [".git", "Cargo.toml"],
                    Languages = [new ProjectLanguageStat { Language = "rust", FileCount = 10 }],
                },
            },
        });

        var response = await _client.GetAsync("/api/stats");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadAsStringAsync();
        Assert.Contains("language_distribution", payload, StringComparison.Ordinal);
        Assert.Contains("indexed_file_count", payload, StringComparison.Ordinal);
        Assert.Contains("rust", payload, StringComparison.Ordinal);
    }

    /// <summary>
    /// Dashboard live telemetry endpoints expose multi-user tool activity and derived context metrics.
    /// </summary>
    [Fact]
    public async Task DashboardTelemetry_ReturnsMultiUserLiveData()
    {
        var fingerprint = new RepositoryFingerprint
        {
            RemoteUrl = "https://github.com/MarkBovee/nebu-ctx.git",
            Host = "github.com",
            Owner = "MarkBovee",
            RepoName = "nebu-ctx",
            DefaultBranch = "main",
        };
        var actorA = $"telemetry-a-{Guid.NewGuid():N}";
        var actorB = $"telemetry-b-{Guid.NewGuid():N}";

        var firstResponse = await _client.PostAsJsonAsync("/v1/tools/call", new ToolCallRequest
        {
            Name = "ctx_brain",
            ProjectSlug = "nebu-ctx",
            RepositoryFingerprint = fingerprint,
            WorkspaceBinding = new CheckoutBinding
            {
                ProjectId = "ignored-by-server",
                LocalRoot = "/tmp/telemetry-a",
                Branch = "main",
                ClientLabel = actorA,
            },
            Arguments = new Dictionary<string, object?>
            {
                ["action"] = "status",
            },
        });
        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);

        var secondResponse = await _client.PostAsJsonAsync("/v1/tools/call", new ToolCallRequest
        {
            Name = "ctx_brain",
            ProjectSlug = "nebu-ctx",
            RepositoryFingerprint = fingerprint,
            WorkspaceBinding = new CheckoutBinding
            {
                ProjectId = "ignored-by-server",
                LocalRoot = "/tmp/telemetry-b",
                Branch = "main",
                ClientLabel = actorB,
            },
            Arguments = new Dictionary<string, object?>
            {
                ["action"] = "status",
            },
        });
        Assert.Equal(HttpStatusCode.OK, secondResponse.StatusCode);

        var statsPayload = await (await _client.GetAsync("/api/stats")).Content.ReadAsStringAsync();
        var mcpPayload = await (await _client.GetAsync("/api/mcp")).Content.ReadAsStringAsync();
        var eventsPayload = await (await _client.GetAsync("/api/events")).Content.ReadAsStringAsync();
        var pipelinePayload = await (await _client.GetAsync("/api/pipeline-stats")).Content.ReadAsStringAsync();
        var ledgerPayload = await (await _client.GetAsync("/api/context-ledger")).Content.ReadAsStringAsync();

        Assert.Contains("total_commands", statsPayload, StringComparison.Ordinal);
        Assert.Contains("ctx_brain", statsPayload, StringComparison.Ordinal);
        Assert.Contains(actorA, mcpPayload, StringComparison.Ordinal);
        Assert.Contains(actorB, mcpPayload, StringComparison.Ordinal);
        Assert.Contains("ToolCall", eventsPayload, StringComparison.Ordinal);
        Assert.Contains(actorA, eventsPayload, StringComparison.Ordinal);
        Assert.Contains("command_preview", eventsPayload, StringComparison.Ordinal);
        Assert.Contains("runs", pipelinePayload, StringComparison.Ordinal);
        Assert.Contains("entries_count", ledgerPayload, StringComparison.Ordinal);
        Assert.Contains(actorB, ledgerPayload, StringComparison.Ordinal);
    }

    /// <summary>
    /// Dashboard operator views return derived payloads for agents, buddy, learning, intent, and compression demo.
    /// </summary>
    [Fact]
    public async Task DashboardOperatorViews_ReturnDerivedPayloads()
    {
        var fingerprint = new RepositoryFingerprint
        {
            RemoteUrl = "https://github.com/MarkBovee/nebu-ctx.git",
            Host = "github.com",
            Owner = "MarkBovee",
            RepoName = "nebu-ctx",
            DefaultBranch = "main",
        };
        var actorLabel = $"operator-view-{Guid.NewGuid():N}";

        var resolveResponse = await _client.PostAsJsonAsync("/v1/projects/resolve", new ProjectResolutionRequest
        {
            SuggestedSlug = "nebu-ctx",
            Fingerprint = fingerprint,
            WorkspaceBinding = new CheckoutBinding
            {
                ProjectId = "ignored-by-server",
                LocalRoot = "/tmp/operator-view",
                Branch = "main",
                ClientLabel = actorLabel,
            },
            ProjectMetadata = new ProjectMetadataEnvelope
            {
                SchemaVersion = 1,
                Summary = new ProjectMetadataSummary
                {
                    TotalFileCount = 30,
                    SourceFileCount = 12,
                    Markers = [".git", "Cargo.toml"],
                    Languages = [new ProjectLanguageStat { Language = "rust", FileCount = 12 }],
                },
            },
        });
        Assert.Equal(HttpStatusCode.OK, resolveResponse.StatusCode);

        var toolCallResponse = await _client.PostAsJsonAsync("/v1/tools/call", new ToolCallRequest
        {
            Name = "ctx_brain",
            ProjectSlug = "nebu-ctx",
            RepositoryFingerprint = fingerprint,
            WorkspaceBinding = new CheckoutBinding
            {
                ProjectId = "ignored-by-server",
                LocalRoot = "/tmp/operator-view",
                Branch = "main",
                ClientLabel = actorLabel,
            },
            Arguments = new Dictionary<string, object?>
            {
                ["action"] = "status",
            },
        });
        Assert.Equal(HttpStatusCode.OK, toolCallResponse.StatusCode);

        var agentsPayload = await (await _client.GetAsync("/api/agents")).Content.ReadAsStringAsync();
        var buddyPayload = await (await _client.GetAsync("/api/buddy")).Content.ReadAsStringAsync();
        var feedbackPayload = await (await _client.GetAsync("/api/feedback")).Content.ReadAsStringAsync();
        var intentPayload = await (await _client.GetAsync("/api/intent")).Content.ReadAsStringAsync();
        var compressionPayload = await (await _client.GetAsync("/api/compression-demo?path=NebuCtx.Tools&task=brain")).Content.ReadAsStringAsync();

        Assert.Contains(actorLabel, agentsPayload, StringComparison.Ordinal);
        Assert.Contains("thin-client", agentsPayload, StringComparison.Ordinal);
        Assert.Contains("Nebby", buddyPayload, StringComparison.Ordinal);
        Assert.Contains("rarity", buddyPayload, StringComparison.Ordinal);
        Assert.Contains("learned_thresholds", feedbackPayload, StringComparison.Ordinal);
        Assert.Contains("rust", feedbackPayload, StringComparison.Ordinal);
        Assert.Contains("task_type", intentPayload, StringComparison.Ordinal);
        Assert.Contains("memory", intentPayload, StringComparison.Ordinal);
        Assert.Contains("original_tokens", compressionPayload, StringComparison.Ordinal);
        Assert.Contains("modes", compressionPayload, StringComparison.Ordinal);
        Assert.Contains("task", compressionPayload, StringComparison.Ordinal);
    }

    /// <summary>
    /// Dashboard search endpoint can find tool metadata.
    /// </summary>
    [Fact]
    public async Task DashboardSearch_ReturnsToolMatches()
    {
        var response = await _client.GetAsync("/api/search?q=ctx_brain&limit=5");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadAsStringAsync();
        Assert.Contains("ctx_brain", payload, StringComparison.Ordinal);
    }

    /// <summary>
     /// Hosted private handlers should honor numeric JsonElement arguments for limit/days.
     /// </summary>
    [Fact]
    public async Task HostedKnowledgeAndSessionHandlers_HonorNumericArguments()
    {
        var fingerprint = new RepositoryFingerprint
        {
            RemoteUrl = "https://github.com/MarkBovee/nebu-ctx.git",
            Host = "github.com",
            Owner = "MarkBovee",
            RepoName = "nebu-ctx",
            DefaultBranch = "main",
        };

        for (var index = 0; index < 3; index++)
        {
            var rememberResponse = await _client.PostAsJsonAsync("/v1/tools/call", new ToolCallRequest
            {
                Name = "ctx_knowledge",
                ProjectSlug = "nebu-ctx",
                RepositoryFingerprint = fingerprint,
                Arguments = new Dictionary<string, object?>
                {
                    ["action"] = "remember",
                    ["category"] = "decision",
                    ["key"] = $"json-limit-{index}",
                    ["value"] = $"json limit item {index}",
                },
            });
            Assert.Equal(HttpStatusCode.OK, rememberResponse.StatusCode);
        }

        var recallResponse = await _client.PostAsJsonAsync("/v1/tools/call", new ToolCallRequest
        {
            Name = "ctx_knowledge",
            ProjectSlug = "nebu-ctx",
            RepositoryFingerprint = fingerprint,
            Arguments = new Dictionary<string, object?>
            {
                ["action"] = "recall",
                ["query"] = "json limit item",
                ["limit"] = JsonDocument.Parse("1").RootElement,
            },
        });
        Assert.Equal(HttpStatusCode.OK, recallResponse.StatusCode);
        var recallPayload = await recallResponse.Content.ReadAsStringAsync();
        Assert.Contains("\"count\":1", recallPayload, StringComparison.Ordinal);

        var taskResponse = await _client.PostAsJsonAsync("/v1/tools/call", new ToolCallRequest
        {
            Name = "ctx_session",
            ProjectSlug = "nebu-ctx",
            RepositoryFingerprint = fingerprint,
            Arguments = new Dictionary<string, object?>
            {
                ["action"] = "task",
                ["value"] = "json arg session",
            },
        });
        Assert.Equal(HttpStatusCode.OK, taskResponse.StatusCode);

        var listResponse = await _client.PostAsJsonAsync("/v1/tools/call", new ToolCallRequest
        {
            Name = "ctx_session",
            ProjectSlug = "nebu-ctx",
            RepositoryFingerprint = fingerprint,
            Arguments = new Dictionary<string, object?>
            {
                ["action"] = "list",
                ["limit"] = JsonDocument.Parse("1").RootElement,
            },
        });
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        var listPayload = await listResponse.Content.ReadAsStringAsync();
        Assert.Contains("\"count\":1", listPayload, StringComparison.Ordinal);
    }

    /// <summary>
    /// Explicit project ids must resolve to a canonical project before tools accept them.
    /// </summary>
    [Fact]
    public async Task ToolCall_WithUnknownProjectId_ReturnsConflict()
    {
        var response = await _client.PostAsJsonAsync("/v1/tools/call", new ToolCallRequest
        {
            Name = "ctx_session",
            ProjectId = "proj_missing",
            Arguments = new Dictionary<string, object?>
            {
                ["action"] = "status",
            },
        });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var payload = await response.Content.ReadAsStringAsync();
        Assert.Contains("Unknown project_id", payload, StringComparison.Ordinal);
    }
}
