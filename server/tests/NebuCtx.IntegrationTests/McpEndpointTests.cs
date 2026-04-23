namespace NebuCtx.IntegrationTests;

using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using NebuCtx.Contracts.Mcp;
using NebuCtx.Contracts.Projects;

/// <summary>
/// Integration tests for the MCP HTTP endpoints.
/// Uses WebApplicationFactory to test the full middleware pipeline.
/// </summary>
public class McpEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    /// <summary>
    /// Initializes the test with an in-memory test server.
    /// </summary>
    public McpEndpointTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.CreateClient();
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
        Assert.NotEmpty(manifest.Tools);
        Assert.Contains(manifest.Tools, tool => tool.Name == "ctx_routes");
    }

    /// <summary>
    /// Tools endpoint returns paginated tool list.
    /// </summary>
    [Fact]
    public async Task Tools_ReturnsPaginatedList()
    {
        var toolList = await _client.GetFromJsonAsync<ToolListResponse>("/v1/tools");
        Assert.NotNull(toolList);
        Assert.True(toolList.Total > 0);
        Assert.NotEmpty(toolList.Tools);
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
    /// Tool call with ctx_routes returns known routes and respects path filtering.
    /// </summary>
    [Fact]
    public async Task ToolCall_Routes_ReturnsKnownRoutes()
    {
        var request = new ToolCallRequest
        {
            Name = "ctx_routes",
            Arguments = new Dictionary<string, object?> { ["path"] = "/api" },
        };

        var response = await _client.PostAsJsonAsync("/v1/tools/call", request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadAsStringAsync();
        Assert.Contains("/api/routes", payload, StringComparison.Ordinal);
        Assert.DoesNotContain("/v1/projects", payload, StringComparison.Ordinal);
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
            WorkspaceBinding = new WorkspaceBinding
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
            WorkspaceBinding = new WorkspaceBinding
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
            WorkspaceBinding = new WorkspaceBinding
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
    /// Dashboard routes endpoint returns the known .NET host route map.
    /// </summary>
    [Fact]
    public async Task DashboardRoutes_ReturnsKnownRoutes()
    {
        var response = await _client.GetAsync("/api/routes");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadAsStringAsync();
        Assert.Contains("/v1/tools/call", payload, StringComparison.Ordinal);
        Assert.Contains("/api/search", payload, StringComparison.Ordinal);
    }

    /// <summary>
    /// Dashboard search index endpoint returns route and tool metadata.
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
        Assert.Contains("NebuCtx.Server.Host/Program.cs", payload, StringComparison.Ordinal);
        Assert.Contains("project/nebu-ctx", payload, StringComparison.Ordinal);
        Assert.Contains("project-language", payload, StringComparison.Ordinal);
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

        var beforeResponse = await _client.GetAsync("/api/knowledge");
        var beforePayload = await beforeResponse.Content.ReadAsStringAsync();
        Assert.Contains(resolution!.Project!.ProjectId, beforePayload, StringComparison.Ordinal);

        var clearResponse = await _client.PostAsync($"/api/knowledge/projects/{resolution.Project.ProjectId}/clear", content: null);
        Assert.Equal(HttpStatusCode.OK, clearResponse.StatusCode);

        var response = await _client.GetAsync("/api/knowledge");
        var payload = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain(resolution.Project.ProjectId, payload, StringComparison.Ordinal);
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
            WorkspaceBinding = new WorkspaceBinding
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
            Name = "ctx_routes",
            ProjectSlug = "nebu-ctx",
            RepositoryFingerprint = fingerprint,
            WorkspaceBinding = new WorkspaceBinding
            {
                ProjectId = "ignored-by-server",
                LocalRoot = "/tmp/telemetry-b",
                Branch = "main",
                ClientLabel = actorB,
            },
            Arguments = new Dictionary<string, object?>
            {
                ["path"] = "/api",
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
        Assert.Contains("runs", pipelinePayload, StringComparison.Ordinal);
        Assert.Contains("entries_count", ledgerPayload, StringComparison.Ordinal);
        Assert.Contains(actorB, ledgerPayload, StringComparison.Ordinal);
    }

    /// <summary>
    /// Dashboard operator views return derived payloads for agents, buddy, gotchas, learning, intent, and compression demo.
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
            WorkspaceBinding = new WorkspaceBinding
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
            Name = "ctx_routes",
            ProjectSlug = "nebu-ctx",
            RepositoryFingerprint = fingerprint,
            WorkspaceBinding = new WorkspaceBinding
            {
                ProjectId = "ignored-by-server",
                LocalRoot = "/tmp/operator-view",
                Branch = "main",
                ClientLabel = actorLabel,
            },
            Arguments = new Dictionary<string, object?>
            {
                ["path"] = "/api",
            },
        });
        Assert.Equal(HttpStatusCode.OK, toolCallResponse.StatusCode);

        var agentsPayload = await (await _client.GetAsync("/api/agents")).Content.ReadAsStringAsync();
        var buddyPayload = await (await _client.GetAsync("/api/buddy")).Content.ReadAsStringAsync();
        var gotchasPayload = await (await _client.GetAsync("/api/gotchas")).Content.ReadAsStringAsync();
        var feedbackPayload = await (await _client.GetAsync("/api/feedback")).Content.ReadAsStringAsync();
        var intentPayload = await (await _client.GetAsync("/api/intent")).Content.ReadAsStringAsync();
        var compressionPayload = await (await _client.GetAsync("/api/compression-demo?path=NebuCtx.Tools&task=routes")).Content.ReadAsStringAsync();

        Assert.Contains(actorLabel, agentsPayload, StringComparison.Ordinal);
        Assert.Contains("thin-client", agentsPayload, StringComparison.Ordinal);
        Assert.Contains("Nebby", buddyPayload, StringComparison.Ordinal);
        Assert.Contains("rarity", buddyPayload, StringComparison.Ordinal);
        Assert.Contains("gotchas", gotchasPayload, StringComparison.Ordinal);
        Assert.Contains("compression", gotchasPayload, StringComparison.Ordinal);
        Assert.Contains("learned_thresholds", feedbackPayload, StringComparison.Ordinal);
        Assert.Contains("rust", feedbackPayload, StringComparison.Ordinal);
        Assert.Contains("task_type", intentPayload, StringComparison.Ordinal);
        Assert.Contains("routing", intentPayload, StringComparison.Ordinal);
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
}
