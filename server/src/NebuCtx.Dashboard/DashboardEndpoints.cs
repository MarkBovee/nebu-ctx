namespace NebuCtx.Dashboard;

using NebuCtx.Application;
using NebuCtx.Contracts.Dashboard;
using NebuCtx.Projects;
using NebuCtx.Storage;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

/// <summary>
/// Maps dashboard API endpoints.
/// Preserves the current /api/* contract expected by the dashboard UI.
/// </summary>
public static class DashboardEndpoints
{
    /// <summary>
    /// Maps all dashboard API routes on the provided endpoint builder.
    /// </summary>
    /// <param name="app">Endpoint route builder (typically the WebApplication).</param>
    /// <returns>The same builder for chaining.</returns>
    public static IEndpointRouteBuilder MapDashboardApi(this IEndpointRouteBuilder app)
    {
        app.MapGet("/", () => Results.Content(DashboardHtmlProvider.LoadHtml(), "text/html"));
        app.MapGet("/index.html", () => Results.Content(DashboardHtmlProvider.LoadHtml(), "text/html"));
        app.MapGet("/dashboard", () => Results.Content(DashboardHtmlProvider.LoadHtml(), "text/html"));

        // Version endpoint
        app.MapGet("/api/version", () => Results.Ok(DashboardPayloadFactory.BuildVersionPayload()));

        // Health endpoint (auth-exempt, shared with MCP)
        app.MapGet("/health", () => Results.Ok(new HealthResponse { Status = "ok" }));

        // Pulse endpoint for change detection
        app.MapGet("/api/pulse", () => Results.Ok(new PulseResponse
        {
            Hash = null,
            Mtime = DateTimeOffset.UtcNow,
        }));

        // Stats endpoint — placeholder returning empty stats for MVP
        app.MapGet("/api/stats", async (ToolRegistry toolRegistry, ProjectRegistry projectRegistry, TelemetryStore telemetryStore, CancellationToken cancellationToken) =>
        {
            var projects = await projectRegistry.ListAsync(cancellationToken);
            return Results.Ok(DashboardPayloadFactory.BuildStatsPayload(toolRegistry, projects, telemetryStore));
        });

        app.MapGet("/api/session", (TelemetryStore telemetryStore) => Results.Ok(DashboardPayloadFactory.BuildSessionPayload(telemetryStore)));

        // Auth token endpoint — reads from env/token file
        app.MapGet("/api/auth-token", () =>
        {
            var tokenPath = Environment.GetEnvironmentVariable("NEBULA_CTX_TOKEN_FILE")
                ?? Environment.GetEnvironmentVariable("NEBU_CTX_TOKEN_FILE");

            string? tokenValue = null;
            if (!string.IsNullOrEmpty(tokenPath) && File.Exists(tokenPath))
            {
                tokenValue = File.ReadAllText(tokenPath).Trim();
            }

            return Results.Ok(new AuthTokenResponse { Token = tokenValue });
        });

        // Placeholder endpoints that return empty arrays/objects for MVP
        // These preserve the dashboard API surface without full implementation yet
        app.MapGet("/api/gain", (TelemetryStore telemetryStore) => Results.Ok(DashboardPayloadFactory.BuildGainPayload(telemetryStore)));
        app.MapGet("/api/mcp", (TelemetryStore telemetryStore) => Results.Ok(DashboardPayloadFactory.BuildMcpPayload(telemetryStore)));
        app.MapGet("/api/agents", async (ProjectRegistry projectRegistry, TelemetryStore telemetryStore, CancellationToken cancellationToken) =>
        {
            var projects = await projectRegistry.ListAsync(cancellationToken);
            return Results.Ok(DashboardPayloadFactory.BuildAgentsPayload(telemetryStore, projects));
        });
        app.MapGet("/api/knowledge", async (ProjectRegistry projectRegistry, IKnowledgeStore knowledgeStore, CancellationToken cancellationToken) =>
        {
            var projects = await projectRegistry.ListAsync(cancellationToken);
            // Load real Postgres facts for all projects and merge with synthetic project facts
            var allEntries = new List<KnowledgeEntry>();
            foreach (var project in projects)
                allEntries.AddRange(await knowledgeStore.ListAllForProjectAsync(project.ProjectId, cancellationToken: cancellationToken));
            return Results.Ok(DashboardPayloadFactory.BuildKnowledgePayload(projects, allEntries));
        });
        app.MapPost("/api/knowledge/projects/{projectId}/clear", async (string projectId, ProjectRegistry projectRegistry, CancellationToken cancellationToken) =>
        {
            var cleared = await projectRegistry.ClearProjectMetadataAsync(projectId, cancellationToken);
            return cleared ? Results.Ok(new { cleared = true, project_id = projectId }) : Results.NotFound(new { cleared = false, project_id = projectId });
        });

        // Brain memory management — delete a single entry or all entries for a project.
        app.MapDelete("/api/brain/{projectId}/{key}", async (string projectId, string key, IBrainStore brainStore, CancellationToken cancellationToken) =>
        {
            var deleted = await brainStore.DeleteAsync(projectId, key, cancellationToken);
            return deleted ? Results.Ok(new { deleted = true }) : Results.NotFound(new { deleted = false });
        });

        app.MapDelete("/api/brain/{projectId}", async (string projectId, IBrainStore brainStore, CancellationToken cancellationToken) =>
        {
            var count = await brainStore.ClearProjectAsync(projectId, cancellationToken);
            return Results.Ok(new { deleted = count, project_id = projectId });
        });
        app.MapGet("/api/brain", async (ProjectRegistry projectRegistry, IBrainStore brainStore, CancellationToken cancellationToken) =>
        {
            var projects = await projectRegistry.ListAsync(cancellationToken);
            var entriesByProject = new Dictionary<string, IReadOnlyList<BrainEntry>>();
            foreach (var project in projects)
                entriesByProject[project.ProjectId] = await brainStore.ListAllAsync(project.ProjectId, cancellationToken: cancellationToken);
            return Results.Ok(DashboardPayloadFactory.BuildBrainPayload(projects, entriesByProject));
        });
        app.MapGet("/api/gotchas", (TelemetryStore telemetryStore) => Results.Ok(DashboardPayloadFactory.BuildGotchasPayload(telemetryStore)));
        app.MapGet("/api/buddy", async (ProjectRegistry projectRegistry, TelemetryStore telemetryStore, CancellationToken cancellationToken) =>
        {
            var projects = await projectRegistry.ListAsync(cancellationToken);
            return Results.Ok(DashboardPayloadFactory.BuildBuddyPayload(telemetryStore, projects));
        });
        app.MapGet("/api/heatmap", () => Results.Ok(new { files = Array.Empty<object>() }));
        app.MapGet("/api/events", (TelemetryStore telemetryStore) => Results.Ok(DashboardPayloadFactory.BuildEventsPayload(telemetryStore)));
        app.MapGet("/api/graph", async (ProjectRegistry projectRegistry, CancellationToken cancellationToken) =>
        {
            var projects = await projectRegistry.ListAsync(cancellationToken);
            return Results.Ok(DashboardPayloadFactory.BuildGraphPayload(projects));
        });
        app.MapGet("/api/call-graph", async (string? project_id, ICodeIndexStore codeIndexStore, ToolRegistry toolRegistry, CancellationToken cancellationToken) =>
        {
            if (!string.IsNullOrWhiteSpace(project_id))
            {
                var edges = await codeIndexStore.GetEdgesAsync(project_id, 5000, cancellationToken);
                var stats = await codeIndexStore.GetStatsAsync(project_id, cancellationToken);
                return Results.Ok(new
                {
                    edges = edges.Select(e => new { caller_symbol = e.FromSymbol, callee_name = e.ToSymbol, kind = e.Kind }),
                    indexed_file_count = stats.FileCount,
                    indexed_symbol_count = stats.SymbolCount,
                    analyzed_file_count = stats.FileCount,
                    last_indexed_at = stats.LastIndexedAt,
                });
            }
            // Fall back to server-internal call graph when no project is selected.
            return Results.Ok(DashboardPayloadFactory.BuildCallGraphPayload(toolRegistry));
        });

        // Project management — list all registered projects
        app.MapGet("/api/projects", async (ProjectRegistry projectRegistry, CancellationToken cancellationToken) =>
        {
            var projects = await projectRegistry.ListAsync(cancellationToken);
            var payload = projects.Select(p => new
            {
                project_id = p.ProjectId,
                slug = p.Slug,
                languages = p.ProjectMetadata?.Summary.Languages.Select(l => l.Language).ToArray() ?? [],
                source_file_count = p.ProjectMetadata?.Summary.SourceFileCount ?? 0,
                total_file_count = p.ProjectMetadata?.Summary.TotalFileCount ?? 0,
                created_at = p.CreatedAt,
            }).ToArray();
            return Results.Ok(new { projects = payload, total = payload.Length });
        });
        // Per-project stats endpoint — returns 200 with zero counts when the project has no telemetry yet
        app.MapGet("/api/projects/{projectId}/stats", (string projectId, TelemetryStore telemetry) =>
        {
            var snapshot = telemetry.GetSnapshot();
            snapshot.PerProject.TryGetValue(projectId, out var proj);

            return Results.Ok(new
            {
                project_id = projectId,
                total_tool_calls = proj?.TotalToolCalls ?? 0,
                total_input_tokens = proj?.TotalInputTokens ?? 0L,
                total_output_tokens = proj?.TotalOutputTokens ?? 0L,
                top_tools = proj?.Commands.Values
                    .OrderByDescending(c => c.Count)
                    .Take(20)
                    .Select(c => new { name = c.Name, count = c.Count })
                    ?? [],
                file_access = proj?.FileAccess
                    .OrderByDescending(f => f.Value)
                    .Take(50)
                    .Select(f => new { path = f.Key, count = f.Value })
                    ?? [],
            });
        });

        app.MapGet("/api/feedback", async (ProjectRegistry projectRegistry, TelemetryStore telemetryStore, CancellationToken cancellationToken) =>
        {
            var projects = await projectRegistry.ListAsync(cancellationToken);
            return Results.Ok(DashboardPayloadFactory.BuildFeedbackPayload(telemetryStore, projects));
        });
        app.MapGet("/api/symbols", async (string? q, string? kind, string? project_id, ICodeIndexStore codeIndexStore, ToolRegistry toolRegistry, CancellationToken cancellationToken) =>
        {
            if (!string.IsNullOrWhiteSpace(project_id))
            {
                var symbols = await codeIndexStore.SearchSymbolsAsync(project_id, q, kind, 500, cancellationToken);
                return Results.Ok(symbols.Select(s => new
                {
                    name = s.Name,
                    kind = s.Kind,
                    file = s.FilePath,
                    start_line = s.StartLine,
                    end_line = s.EndLine,
                    is_exported = s.IsExported,
                }).ToArray());
            }
            // Fall back to server-internal symbols when no project selected.
            var serverSymbols = DashboardPayloadFactory.BuildSymbolsPayload(toolRegistry).AsEnumerable();
            if (!string.IsNullOrWhiteSpace(q))
                serverSymbols = serverSymbols.Where(symbol => symbol.ToString()?.Contains(q, StringComparison.OrdinalIgnoreCase) == true);
            return Results.Ok(serverSymbols.ToArray());
        });
        app.MapGet("/api/routes", () => Results.Ok(DashboardPayloadFactory.BuildRoutesPayload()));
        app.MapGet("/api/search-index", async (string? project_id, ICodeIndexStore codeIndexStore, ToolRegistry toolRegistry, CancellationToken cancellationToken) =>
        {
            if (!string.IsNullOrWhiteSpace(project_id))
            {
                var stats = await codeIndexStore.GetStatsAsync(project_id, cancellationToken);
                var topFiles = await codeIndexStore.SearchFilesAsync(project_id, null, 20, cancellationToken);
                return Results.Ok(new
                {
                    doc_count = stats.FileCount,
                    chunk_count = stats.SymbolCount,
                    language_distribution = stats.LanguageDistribution,
                    last_indexed_at = stats.LastIndexedAt,
                    top_chunks_by_token_count = topFiles.Select(f => new
                    {
                        symbol_name = f.Path,
                        file_path = f.Path,
                        kind = f.Language,
                        token_count = f.TokenCount,
                        start_line = 1,
                        end_line = f.LineCount,
                    }),
                });
            }
            return Results.Ok(DashboardPayloadFactory.BuildSearchIndexPayload(toolRegistry));
        });
        app.MapGet("/api/search", async (string? q, int? limit, string? project_id, ICodeIndexStore codeIndexStore, ToolRegistry toolRegistry, CancellationToken cancellationToken) =>
        {
            if (!string.IsNullOrWhiteSpace(project_id) && !string.IsNullOrWhiteSpace(q))
            {
                var files = await codeIndexStore.SearchFilesAsync(project_id, q, limit ?? 20, cancellationToken);
                return Results.Ok(new
                {
                    results = files.Select(f => new
                    {
                        score = 1.0,
                        symbol_name = f.Path,
                        kind = f.Language,
                        file_path = f.Path,
                        start_line = 1,
                        end_line = f.LineCount,
                        snippet = f.Summary,
                    }),
                });
            }
            return Results.Ok(DashboardPayloadFactory.BuildSearchPayload(q, limit, toolRegistry));
        });
        app.MapGet("/api/compression-demo", async (string? path, string? task, ToolRegistry toolRegistry, ProjectRegistry projectRegistry, CancellationToken cancellationToken) =>
        {
            var projects = await projectRegistry.ListAsync(cancellationToken);
            return Results.Ok(DashboardPayloadFactory.BuildCompressionDemoPayload(path, task, toolRegistry, projects));
        });
        app.MapGet("/api/pipeline-stats", (TelemetryStore telemetryStore) => Results.Ok(DashboardPayloadFactory.BuildPipelineStatsPayload(telemetryStore)));
        app.MapGet("/api/context-ledger", (TelemetryStore telemetryStore) => Results.Ok(DashboardPayloadFactory.BuildContextLedgerPayload(telemetryStore)));
        app.MapGet("/api/intent", async (ProjectRegistry projectRegistry, TelemetryStore telemetryStore, CancellationToken cancellationToken) =>
        {
            var projects = await projectRegistry.ListAsync(cancellationToken);
            return Results.Ok(DashboardPayloadFactory.BuildIntentPayload(telemetryStore, projects));
        });

        // Favicon — 204 No Content
        app.MapGet("/favicon.ico", () => Results.NoContent());

        return app;
    }
}
