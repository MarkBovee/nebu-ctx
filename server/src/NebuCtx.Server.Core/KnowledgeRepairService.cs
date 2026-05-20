namespace NebuCtx.Server.Core;

using NebuCtx.Contracts.Projects;
using NebuCtx.Storage;

/// <summary>
/// Repairs known knowledge graph data issues by consolidating safely-identifiable duplicate projects
/// and clearing stale legacy short-slug knowledge that predates the current project identity rules.
/// </summary>
public sealed class KnowledgeRepairService
{
    private readonly IProjectStore _projectStore;
    private readonly IKnowledgeStore _knowledgeStore;

    /// <summary>
    /// Initializes the repair service.
    /// </summary>
    public KnowledgeRepairService(IProjectStore projectStore, IKnowledgeStore knowledgeStore)
    {
        _projectStore = projectStore;
        _knowledgeStore = knowledgeStore;
    }

    /// <summary>
    /// Repairs duplicate and legacy ambiguous knowledge-project mappings.
    /// </summary>
    public async Task<object> RepairAsync(CancellationToken cancellationToken = default)
    {
        var projects = await _projectStore.ListProjectsAsync(cancellationToken);
        var mergedProjects = new List<object>();
        var clearedProjects = new List<object>();

        foreach (var duplicateGroup in projects
                     .Where(ProjectIdentityDiagnostics.HasSafeFingerprint)
                     .GroupBy(BuildFingerprintKey, StringComparer.OrdinalIgnoreCase)
                     .Where(group => group.Count() > 1))
        {
            var canonical = ProjectIdentityDiagnostics.SelectCanonicalProject(duplicateGroup);

            foreach (var duplicate in duplicateGroup.Where(project => !string.Equals(project.ProjectId, canonical.ProjectId, StringComparison.OrdinalIgnoreCase)))
            {
                var movedFacts = await _knowledgeStore.ReassignProjectAsync(duplicate.ProjectId, canonical.ProjectId, cancellationToken);
                await _projectStore.DeleteProjectAsync(duplicate.ProjectId, cancellationToken);
                mergedProjects.Add(new
                {
                    from_project_id = duplicate.ProjectId,
                    to_project_id = canonical.ProjectId,
                    moved_facts = movedFacts,
                });
            }
        }

        foreach (var ambiguous in projects.Where(project => IsLegacyAmbiguousSlug(project) && !ProjectIdentityDiagnostics.HasSafeFingerprint(project)))
        {
            var clearedFacts = await _knowledgeStore.ClearProjectAsync(ambiguous.ProjectId, cancellationToken);
            ambiguous.ProjectMetadata = null;
            ambiguous.Slug = $"project-{ambiguous.Slug}";
            ambiguous.UpdatedAt = DateTimeOffset.UtcNow;
            await _projectStore.UpdateProjectAsync(ambiguous, cancellationToken);
            clearedProjects.Add(new
            {
                project_id = ambiguous.ProjectId,
                slug = ambiguous.Slug,
                cleared_facts = clearedFacts,
            });
        }

        return new
        {
            merged_count = mergedProjects.Count,
            cleared_count = clearedProjects.Count,
            merged_projects = mergedProjects,
            cleared_projects = clearedProjects,
        };
    }

    private static bool IsLegacyAmbiguousSlug(ProjectRecord project)
    {
        return string.Equals(project.Slug, "mark", StringComparison.OrdinalIgnoreCase)
            || string.Equals(project.Slug, "project-mark", StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildFingerprintKey(ProjectRecord project)
    {
        return ProjectIdentityDiagnostics.TryBuildFingerprintKey(project, out var key)
            ? key
            : string.Empty;
    }
}
