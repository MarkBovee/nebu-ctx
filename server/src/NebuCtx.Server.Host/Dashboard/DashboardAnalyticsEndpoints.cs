namespace NebuCtx.Server.Host.Dashboard;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using NebuCtx.Server.Core;

/// <summary>
/// Dashboard analytics endpoints: stats, session, gain, mcp, agents, events,
/// pipeline-stats, context-ledger, intent, feedback, gotchas, buddy.
/// </summary>
public static class DashboardAnalyticsEndpoints
{
    /// <summary>
    /// Maps dashboard analytics routes on the provided endpoint builder.
    /// </summary>
    public static IEndpointRouteBuilder MapDashboardAnalytics(this IEndpointRouteBuilder app)
    {
        app.MapGet("/stats", async (
            ToolRegistry toolRegistry,
            ProjectRegistry projectRegistry,
            TelemetryStore telemetryStore,
            CancellationToken ct) =>
        {
            var projects = await projectRegistry.ListAsync(ct);
            return Results.Ok(DashboardPayloadFactory.BuildStatsPayload(toolRegistry, projects, telemetryStore));
        });

        app.MapGet("/session", (TelemetryStore telemetryStore) =>
            Results.Ok(DashboardPayloadFactory.BuildSessionPayload(telemetryStore)));

        app.MapGet("/gain", (TelemetryStore telemetryStore) =>
            Results.Ok(DashboardPayloadFactory.BuildGainPayload(telemetryStore)));

        app.MapGet("/mcp", (TelemetryStore telemetryStore) =>
            Results.Ok(DashboardPayloadFactory.BuildMcpPayload(telemetryStore)));

        app.MapGet("/agents", async (
            ProjectRegistry projectRegistry,
            TelemetryStore telemetryStore,
            CancellationToken ct) =>
        {
            var projects = await projectRegistry.ListAsync(ct);
            return Results.Ok(DashboardPayloadFactory.BuildAgentsPayload(telemetryStore, projects));
        });

        app.MapGet("/events", (TelemetryStore telemetryStore) =>
            Results.Ok(DashboardPayloadFactory.BuildEventsPayload(telemetryStore)));

        app.MapGet("/pipeline-stats", (TelemetryStore telemetryStore) =>
            Results.Ok(DashboardPayloadFactory.BuildPipelineStatsPayload(telemetryStore)));

        app.MapGet("/context-ledger", (TelemetryStore telemetryStore) =>
            Results.Ok(DashboardPayloadFactory.BuildContextLedgerPayload(telemetryStore)));

        app.MapGet("/intent", async (
            ProjectRegistry projectRegistry,
            TelemetryStore telemetryStore,
            CancellationToken ct) =>
        {
            var projects = await projectRegistry.ListAsync(ct);
            return Results.Ok(DashboardPayloadFactory.BuildIntentPayload(telemetryStore, projects));
        });

        app.MapGet("/feedback", async (
            ProjectRegistry projectRegistry,
            TelemetryStore telemetryStore,
            CancellationToken ct) =>
        {
            var projects = await projectRegistry.ListAsync(ct);
            return Results.Ok(DashboardPayloadFactory.BuildFeedbackPayload(telemetryStore, projects));
        });

        app.MapGet("/gotchas", (TelemetryStore telemetryStore) =>
            Results.Ok(DashboardPayloadFactory.BuildGotchasPayload(telemetryStore)));

        app.MapGet("/buddy", async (
            ProjectRegistry projectRegistry,
            TelemetryStore telemetryStore,
            CancellationToken ct) =>
        {
            var projects = await projectRegistry.ListAsync(ct);
            return Results.Ok(DashboardPayloadFactory.BuildBuddyPayload(telemetryStore, projects));
        });

        app.MapGet("/heatmap", () => Results.Ok(new { files = Array.Empty<object>() }));

        app.MapGet("/projects/{projectId}/stats", (string projectId, TelemetryStore telemetry) =>
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

        return app;
    }
}
