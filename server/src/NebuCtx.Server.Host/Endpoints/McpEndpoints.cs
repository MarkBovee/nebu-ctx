namespace NebuCtx.Server.Host.Endpoints;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using NebuCtx.Contracts.Mcp;
using NebuCtx.Server.Core;
using NebuCtx.Server.Host.Projects;
using NebuCtx.Storage;

/// <summary>
/// Maps the MCP HTTP API endpoints: manifest, tools, tool-call, telemetry ingest, and index sync.
/// </summary>
public static class McpEndpoints
{
    /// <summary>
    /// Registers all /v1 MCP endpoints on the provided route builder.
    /// </summary>
    public static IEndpointRouteBuilder MapMcpApi(this IEndpointRouteBuilder app)
    {
        var mcp = app.MapGroup("/v1");

        mcp.MapGet("/manifest", (ToolRegistry toolRegistry) => Results.Ok(toolRegistry.GetManifest()));

        mcp.MapGet("/tools", (ToolRegistry toolRegistry, int? offset, int? limit) =>
            Results.Ok(toolRegistry.GetTools(offset ?? 0, limit ?? 200)));

        mcp.MapPost("/tools/call", async (ToolCallRequest request, ToolRegistry toolRegistry, ProjectRegistry projectRegistry, CancellationToken ct) =>
        {
            try
            {
                var context = await ProjectApiEndpoints.ResolveToolExecutionContextAsync(request, projectRegistry, ct);
                var result = await toolRegistry.ExecuteToolAsync(request.Name, request.Arguments, context, ct);
                return Results.Ok(new ToolCallResponse { Result = result });
            }
            catch (KeyNotFoundException ex)
            {
                return Results.BadRequest(new ToolCallErrorResponse { Error = ex.Message });
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new ToolCallErrorResponse { Error = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return Results.Conflict(new ToolCallErrorResponse { Error = ex.Message });
            }
        });

        mcp.MapPost("/telemetry/ingest", async (
            TelemetryIngestRequest request,
            TelemetryStore telemetryStore,
            ProjectRegistry projectRegistry,
            CancellationToken ct) =>
        {
            var projectId = request.CheckoutBinding?.ProjectId ?? string.Empty;
            if (request.RepositoryFingerprint is not null)
            {
                var project = await projectRegistry.ResolveOrCreateAsync(
                    request.RepositoryFingerprint,
                    request.ProjectSlug ?? "unknown",
                    cancellationToken: ct);
                if (project is not null)
                {
                    projectId = project.ProjectId;
                }
            }

            telemetryStore.IngestEvent(request, projectId);
            return Results.Ok(new { ingested = true });
        });

        mcp.MapPost("/index/sync", async (
            IndexSyncRequest request,
            ICodeIndexStore codeIndexStore,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.ProjectId))
                return Results.BadRequest(new { error = "project_id is required" });

            var files = (request.Files ?? []).Select(f => new IndexedFile
            {
                Path = f.Path,
                Hash = f.Hash ?? "",
                Language = f.Language ?? "",
                LineCount = f.LineCount,
                TokenCount = f.TokenCount,
                Exports = f.Exports ?? [],
                Summary = f.Summary ?? "",
            }).ToList();

            var symbols = (request.Symbols ?? []).Select(s => new IndexedSymbol
            {
                FilePath = s.FilePath,
                Name = s.Name,
                Kind = s.Kind ?? "",
                StartLine = s.StartLine,
                EndLine = s.EndLine,
                IsExported = s.IsExported,
            }).ToList();

            var edges = (request.Edges ?? []).Select(e => new IndexedCallEdge
            {
                FromSymbol = e.FromSymbol,
                ToSymbol = e.ToSymbol,
                Kind = e.Kind ?? "call",
            }).ToList();

            await codeIndexStore.SyncIndexAsync(request.ProjectId, files, symbols, edges, ct);

            return Results.Ok(new
            {
                synced = true,
                project_id = request.ProjectId,
                files = files.Count,
                symbols = symbols.Count,
                edges = edges.Count,
            });
        });

        return app;
    }
}
