namespace NebuCtx.Server.Host.Dashboard;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using NebuCtx.Contracts.Dashboard;
using NebuCtx.Server.Core;
using NebuCtx.Storage;

/// <summary>
/// Dashboard data endpoints: knowledge, brain, graph, call-graph, projects,
/// symbols, search, search-index, compression-demo.
/// </summary>
public static class DashboardDataEndpoints
{
    /// <summary>
    /// Maps dashboard data routes on the provided endpoint builder.
    /// </summary>
    public static IEndpointRouteBuilder MapDashboardData(this IEndpointRouteBuilder app)
    {
        var knowledge = app.MapGroup("/knowledge");
        var brain = app.MapGroup("/brain");

        // Knowledge
        knowledge.MapGet("/", async (
            ProjectRegistry projectRegistry,
            IKnowledgeStore knowledgeStore,
            CancellationToken ct) =>
        {
            var projects = await projectRegistry.ListAsync(ct);
            var allEntries = new List<KnowledgeEntry>();
            foreach (var project in projects)
                allEntries.AddRange(await knowledgeStore.ListAllForProjectAsync(project.ProjectId, cancellationToken: ct));
            return Results.Ok(DashboardPayloadFactory.BuildKnowledgePayload(projects, allEntries));
        });

        knowledge.MapPost("/projects/{projectId}/clear", async (
            string projectId,
            ProjectRegistry projectRegistry,
            IKnowledgeStore knowledgeStore,
            CancellationToken ct) =>
        {
            await knowledgeStore.ClearProjectAsync(projectId, ct);
            var cleared = await projectRegistry.ClearProjectMetadataAsync(projectId, ct);
            return cleared
                ? Results.Ok(new { cleared = true, project_id = projectId })
                : Results.NotFound(new { cleared = false, project_id = projectId });
        });

        knowledge.MapPost("/repair", async (KnowledgeRepairService repairService, CancellationToken ct) =>
        {
            return Results.Ok(await repairService.RepairAsync(ct));
        });

        // Brain
        brain.MapGet("/", async (
            ProjectRegistry projectRegistry,
            IBrainStore brainStore,
            CancellationToken ct) =>
        {
            var projects = await projectRegistry.ListAsync(ct);
            var entriesByProject = new Dictionary<string, IReadOnlyList<BrainEntry>>();
            foreach (var project in projects)
                entriesByProject[project.ProjectId] = await brainStore.ListAllAsync(project.ProjectId, cancellationToken: ct);
            return Results.Ok(DashboardPayloadFactory.BuildBrainPayload(projects, entriesByProject));
        });

        brain.MapDelete("/{projectId}/{key}", async (
            string projectId,
            string key,
            IBrainStore brainStore,
            CancellationToken ct) =>
        {
            var deleted = await brainStore.DeleteAsync(projectId, key, ct);
            return deleted ? Results.Ok(new { deleted = true }) : Results.NotFound(new { deleted = false });
        });

        brain.MapDelete("/{projectId}", async (
            string projectId,
            IBrainStore brainStore,
            CancellationToken ct) =>
        {
            var count = await brainStore.ClearProjectAsync(projectId, ct);
            return Results.Ok(new { deleted = count, project_id = projectId });
        });

        // Graph
        app.MapGet("/graph", async (ProjectRegistry projectRegistry, CancellationToken ct) =>
        {
            var projects = await projectRegistry.ListAsync(ct);
            return Results.Ok(DashboardPayloadFactory.BuildGraphPayload(projects));
        });

        app.MapGet("/call-graph", async (
            string? project_id,
            ICodeIndexStore codeIndexStore,
            ToolRegistry toolRegistry,
            CancellationToken ct) =>
        {
            if (!string.IsNullOrWhiteSpace(project_id))
            {
                var edges = await codeIndexStore.GetEdgesAsync(project_id, 5000, ct);
                var stats = await codeIndexStore.GetStatsAsync(project_id, ct);
                return Results.Ok(new
                {
                    edges = edges.Select(e => new { caller_symbol = e.FromSymbol, callee_name = e.ToSymbol, kind = e.Kind }),
                    indexed_file_count = stats.FileCount,
                    indexed_symbol_count = stats.SymbolCount,
                    analyzed_file_count = stats.FileCount,
                    last_indexed_at = stats.LastIndexedAt,
                });
            }
            return Results.Ok(DashboardPayloadFactory.BuildCallGraphPayload(toolRegistry));
        });

        // Projects
        app.MapGet("/projects", async (ProjectRegistry projectRegistry, CancellationToken ct) =>
        {
            var projects = await projectRegistry.ListAsync(ct);
            var duplicateSlugGroups = ProjectIdentityDiagnostics.FindDuplicateSlugGroups(projects);
            var duplicateFingerprintGroups = ProjectIdentityDiagnostics.FindDuplicateFingerprintGroups(projects);
            var duplicateSlugProjectIds = duplicateSlugGroups.SelectMany(group => group.ProjectIds).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var duplicateFingerprintProjectIds = duplicateFingerprintGroups.SelectMany(group => group.ProjectIds).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var payload = projects.Select(p => new
            {
                project_id = p.ProjectId,
                slug = p.Slug,
                languages = p.ProjectMetadata?.Summary.Languages.Select(l => l.Language).ToArray() ?? [],
                source_file_count = p.ProjectMetadata?.Summary.SourceFileCount ?? 0,
                total_file_count = p.ProjectMetadata?.Summary.TotalFileCount ?? 0,
                created_at = p.CreatedAt,
                has_duplicate_slug = duplicateSlugProjectIds.Contains(p.ProjectId),
                has_duplicate_fingerprint = duplicateFingerprintProjectIds.Contains(p.ProjectId),
            }).ToArray();
            return Results.Ok(new
            {
                projects = payload,
                total = payload.Length,
                duplicate_slug_groups = duplicateSlugGroups.Select(group => new
                {
                    slug = group.Slug,
                    count = group.ProjectIds.Count,
                    project_ids = group.ProjectIds,
                }).ToArray(),
                duplicate_fingerprint_groups = duplicateFingerprintGroups.Select(group => new
                {
                    fingerprint_key = group.FingerprintKey,
                    canonical_project_id = group.CanonicalProjectId,
                    count = group.ProjectIds.Count,
                    project_ids = group.ProjectIds,
                }).ToArray(),
            });
        });

        app.MapDelete("/projects/{projectId}", async (
            string projectId,
            ProjectRegistry projectRegistry,
            CancellationToken ct) =>
        {
            var result = await projectRegistry.DeleteProjectAsync(projectId, ct);
            return result.Deleted
                ? Results.Ok(result)
                : Results.NotFound(result);
        });

        app.MapGet("/dashboard/projects/{projectId}/memory", async (
            string projectId,
            ProjectRegistry projectRegistry,
            IKnowledgeStore knowledgeStore,
            IBrainStore brainStore,
            NebuCtx.Server.Core.Services.KnowledgeService knowledgeService,
            CancellationToken ct) =>
        {
            var projects = await projectRegistry.ListAsync(ct);
            var project = projects.FirstOrDefault(item => item.ProjectId == projectId);
            if (project is null)
            {
                return Results.NotFound(new { error = "project not found", project_id = projectId });
            }

            var knowledgeEntries = await knowledgeStore.ListAllForProjectAsync(projectId, cancellationToken: ct);
            var brainEntries = await brainStore.ListAllAsync(projectId, cancellationToken: ct);
            var triage = await knowledgeService.TriageAsync(projectId, apply: false, cancellationToken: ct);
            var duplicateSlugProjectIds = ProjectIdentityDiagnostics
                .FindDuplicateSlugGroups(projects)
                .SelectMany(group => group.ProjectIds)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var duplicateFingerprintProjectIds = ProjectIdentityDiagnostics
                .FindDuplicateFingerprintGroups(projects)
                .SelectMany(group => group.ProjectIds)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var payload = DashboardPayloadFactory.BuildProjectMemoryPayload(project, knowledgeEntries, brainEntries, triage);
            payload.Flags.HasDuplicateSlug = duplicateSlugProjectIds.Contains(projectId);
            payload.Flags.HasDuplicateFingerprint = duplicateFingerprintProjectIds.Contains(projectId);

            return Results.Ok(payload);
        });

        app.MapPost("/dashboard/projects/{projectId}/memory/triage", async (
            string projectId,
            HttpRequest request,
            NebuCtx.Server.Core.Services.KnowledgeService knowledgeService,
            CancellationToken ct) =>
        {
            var mode = request.Query["mode"].ToString();
            var apply = string.Equals(mode, "apply", StringComparison.OrdinalIgnoreCase);
            var result = await knowledgeService.TriageAsync(projectId, apply, ct);
            return Results.Ok(result);
        });

        app.MapDelete("/dashboard/projects/{projectId}/memory/brain/{key}", async (
            string projectId,
            string key,
            IBrainStore brainStore,
            CancellationToken ct) =>
        {
            var deleted = await brainStore.DeleteAsync(projectId, key, ct);
            return deleted
                ? Results.Ok(new { deleted = true, project_id = projectId, key })
                : Results.NotFound(new { deleted = false, project_id = projectId, key });
        });

        app.MapDelete("/dashboard/projects/{projectId}/memory/brain", async (
            string projectId,
            IBrainStore brainStore,
            CancellationToken ct) =>
        {
            var count = await brainStore.ClearProjectAsync(projectId, ct);
            return Results.Ok(new { deleted = count, project_id = projectId });
        });

        app.MapDelete("/dashboard/projects/{projectId}/memory/brain/type/{entryType}", async (
            string projectId,
            string entryType,
            IBrainStore brainStore,
            CancellationToken ct) =>
        {
            var count = await brainStore.DeleteByPrefixAsync(projectId, entryType, ct);
            return Results.Ok(new { deleted = count, project_id = projectId, entry_type = entryType });
        });

        app.MapDelete("/dashboard/projects/{projectId}/memory/knowledge/{category}/{key}", async (
            string projectId,
            string category,
            string key,
            IKnowledgeStore knowledgeStore,
            CancellationToken ct) =>
        {
            var deleted = await knowledgeStore.RemoveFactAsync(projectId, category, key, ct);
            return deleted
                ? Results.Ok(new { deleted = true, project_id = projectId, category, key })
                : Results.NotFound(new { deleted = false, project_id = projectId, category, key });
        });

        app.MapDelete("/dashboard/projects/{projectId}/memory/knowledge", async (
            string projectId,
            IKnowledgeStore knowledgeStore,
            CancellationToken ct) =>
        {
            var count = await knowledgeStore.ClearProjectAsync(projectId, ct);
            return Results.Ok(new { deleted = count, project_id = projectId });
        });

        // Symbols
        app.MapGet("/symbols", async (
            string? q,
            string? kind,
            string? project_id,
            ICodeIndexStore codeIndexStore,
            ToolRegistry toolRegistry,
            CancellationToken ct) =>
        {
            if (!string.IsNullOrWhiteSpace(project_id))
            {
                var symbols = await codeIndexStore.SearchSymbolsAsync(project_id, q, kind, 500, ct);
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
            var serverSymbols = DashboardPayloadFactory.BuildSymbolsPayload(toolRegistry).AsEnumerable();
            if (!string.IsNullOrWhiteSpace(q))
                serverSymbols = serverSymbols.Where(s => s.ToString()?.Contains(q, StringComparison.OrdinalIgnoreCase) == true);
            return Results.Ok(serverSymbols.ToArray());
        });

        // Search
        app.MapGet("/search-index", async (
            string? project_id,
            ICodeIndexStore codeIndexStore,
            ToolRegistry toolRegistry,
            CancellationToken ct) =>
        {
            if (!string.IsNullOrWhiteSpace(project_id))
            {
                var stats = await codeIndexStore.GetStatsAsync(project_id, ct);
                var topFiles = await codeIndexStore.SearchFilesAsync(project_id, null, 20, ct);
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

        app.MapGet("/search", async (
            string? q,
            int? limit,
            string? project_id,
            ICodeIndexStore codeIndexStore,
            ToolRegistry toolRegistry,
            CancellationToken ct) =>
        {
            if (!string.IsNullOrWhiteSpace(project_id) && !string.IsNullOrWhiteSpace(q))
            {
                var files = await codeIndexStore.SearchFilesAsync(project_id, q, limit ?? 20, ct);
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

        // Compression demo
        app.MapGet("/compression-demo", async (
            string? path,
            string? task,
            ToolRegistry toolRegistry,
            ProjectRegistry projectRegistry,
            CancellationToken ct) =>
        {
            var projects = await projectRegistry.ListAsync(ct);
            return Results.Ok(DashboardPayloadFactory.BuildCompressionDemoPayload(path, task, toolRegistry, projects));
        });

        return app;
    }
}
