namespace NebuCtx.Dashboard;

using NebuCtx.Application;
using NebuCtx.Contracts.Dashboard;
using NebuCtx.Projects;
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
        app.MapGet("/api/stats", async (ToolRegistry toolRegistry, ProjectRegistry projectRegistry, CancellationToken cancellationToken) =>
        {
            var projects = await projectRegistry.ListAsync(cancellationToken);
            return Results.Ok(DashboardPayloadFactory.BuildStatsPayload(toolRegistry, projects));
        });

        // Session endpoint — placeholder returning minimal session for MVP
        app.MapGet("/api/session", () => Results.Ok(new SessionResponse
        {
            Id = "server",
            Version = 1,
            StartedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        }));

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
        app.MapGet("/api/gain", (ToolRegistry toolRegistry) => Results.Ok(DashboardPayloadFactory.BuildGainPayload(toolRegistry)));
        app.MapGet("/api/mcp", () => Results.Ok(DashboardPayloadFactory.BuildMcpPayload()));
        app.MapGet("/api/agents", () => Results.Ok(DashboardPayloadFactory.BuildAgentsPayload()));
        app.MapGet("/api/knowledge", async (ProjectRegistry projectRegistry, CancellationToken cancellationToken) =>
        {
            var projects = await projectRegistry.ListAsync(cancellationToken);
            return Results.Ok(new
            {
                items = projects.Select(project => new
                {
                    category = "project",
                    key = project.ProjectId,
                    value = project.Slug,
                }).ToArray(),
            });
        });
        app.MapGet("/api/gotchas", () => Results.Ok(DashboardPayloadFactory.BuildGotchasPayload()));
        app.MapGet("/api/buddy", () => Results.Ok(new { state = (object?)null }));
        app.MapGet("/api/heatmap", () => Results.Ok(new { files = Array.Empty<object>() }));
        app.MapGet("/api/events", () => Results.Ok(Array.Empty<object>()));
        app.MapGet("/api/graph", () => Results.Ok(new { nodes = Array.Empty<object>(), edges = Array.Empty<object>() }));
        app.MapGet("/api/call-graph", (ToolRegistry toolRegistry) => Results.Ok(DashboardPayloadFactory.BuildCallGraphPayload(toolRegistry)));
        app.MapGet("/api/feedback", () => Results.Ok(DashboardPayloadFactory.BuildFeedbackPayload()));
        app.MapGet("/api/symbols", (string? q, string? kind, ToolRegistry toolRegistry) =>
        {
            var symbols = DashboardPayloadFactory.BuildSymbolsPayload(toolRegistry).AsEnumerable();
            if (!string.IsNullOrWhiteSpace(q))
            {
                symbols = symbols.Where(symbol => symbol.ToString()?.Contains(q, StringComparison.OrdinalIgnoreCase) == true);
            }

            return Results.Ok(symbols.ToArray());
        });
        app.MapGet("/api/routes", () => Results.Ok(DashboardPayloadFactory.BuildRoutesPayload()));
        app.MapGet("/api/search-index", (ToolRegistry toolRegistry) => Results.Ok(DashboardPayloadFactory.BuildSearchIndexPayload(toolRegistry)));
        app.MapGet("/api/search", (string? q, int? limit, ToolRegistry toolRegistry) => Results.Ok(DashboardPayloadFactory.BuildSearchPayload(q, limit, toolRegistry)));
        app.MapGet("/api/compression-demo", (string? path, string? task) => Results.Ok(new { modes = Array.Empty<object>() }));
        app.MapGet("/api/pipeline-stats", () => Results.Ok(DashboardPayloadFactory.BuildPipelineStatsPayload()));
        app.MapGet("/api/context-ledger", () => Results.Ok(DashboardPayloadFactory.BuildContextLedgerPayload()));
        app.MapGet("/api/intent", () => Results.Ok(DashboardPayloadFactory.BuildIntentPayload()));

        // Favicon — 204 No Content
        app.MapGet("/favicon.ico", () => Results.NoContent());

        return app;
    }
}
