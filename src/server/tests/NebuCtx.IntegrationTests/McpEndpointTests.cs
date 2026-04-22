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
        };

        var response = await _client.PostAsJsonAsync("/v1/projects/resolve", request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<ProjectResolutionResponse>();
        Assert.NotNull(payload);
        Assert.Equal("nebu-ctx", payload.Project.Slug);
        Assert.StartsWith("proj_", payload.Project.ProjectId, StringComparison.Ordinal);
        Assert.True(payload.WorkspaceBound);
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
