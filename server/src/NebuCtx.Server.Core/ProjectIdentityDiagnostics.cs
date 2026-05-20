namespace NebuCtx.Server.Core;

using NebuCtx.Contracts.Projects;

/// <summary>
/// Shared project-identity diagnostics and canonical-selection helpers.
/// </summary>
public static class ProjectIdentityDiagnostics
{
    /// <summary>
    /// Returns true when the project carries a trustworthy repository fingerprint.
    /// </summary>
    public static bool HasSafeFingerprint(ProjectRecord project)
    {
        return LegacyProjectCleanupRules.HasSafeFingerprint(project);
    }

    /// <summary>
    /// Builds a stable grouping key for a project's repository fingerprint.
    /// </summary>
    public static bool TryBuildFingerprintKey(ProjectRecord project, out string key)
    {
        if (!HasSafeFingerprint(project))
        {
            key = string.Empty;
            return false;
        }

        var fingerprint = project.Fingerprint;
        if (!string.IsNullOrWhiteSpace(fingerprint?.RemoteUrl))
        {
            key = $"remote:{fingerprint.RemoteUrl.Trim()}";
            return true;
        }

        key = $"repo:{fingerprint?.Host?.Trim()}|{fingerprint?.Owner?.Trim()}|{fingerprint?.RepoName?.Trim()}";
        return true;
    }

    /// <summary>
    /// Picks the canonical project to keep when duplicate project records exist.
    /// </summary>
    public static ProjectRecord SelectCanonicalProject(IEnumerable<ProjectRecord> projects)
    {
        return projects
            .OrderByDescending(project => project.ProjectMetadata?.Summary.SourceFileCount ?? 0)
            .ThenByDescending(project => project.ProjectMetadata?.Summary.TotalFileCount ?? 0)
            .ThenBy(project => project.CreatedAt)
            .First();
    }

    /// <summary>
    /// Finds duplicate project groups that share the same safe repository fingerprint.
    /// </summary>
    public static IReadOnlyList<ProjectDuplicateFingerprintGroup> FindDuplicateFingerprintGroups(IEnumerable<ProjectRecord> projects)
    {
        return projects
            .Where(HasSafeFingerprint)
            .Where(project => TryBuildFingerprintKey(project, out _))
            .GroupBy(project => BuildFingerprintKey(project), StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group =>
            {
                var records = group.OrderBy(project => project.CreatedAt).ToArray();
                var canonical = SelectCanonicalProject(records);
                return new ProjectDuplicateFingerprintGroup
                {
                    FingerprintKey = group.Key,
                    CanonicalProjectId = canonical.ProjectId,
                    ProjectIds = records.Select(project => project.ProjectId).ToArray(),
                    Projects = records,
                };
            })
            .OrderBy(group => group.FingerprintKey, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    /// <summary>
    /// Finds duplicate project groups that share the same slug.
    /// </summary>
    public static IReadOnlyList<ProjectDuplicateSlugGroup> FindDuplicateSlugGroups(IEnumerable<ProjectRecord> projects)
    {
        return projects
            .Where(project => !string.IsNullOrWhiteSpace(project.Slug))
            .GroupBy(project => project.Slug.Trim(), StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => new ProjectDuplicateSlugGroup
            {
                Slug = group.Key,
                ProjectIds = group.Select(project => project.ProjectId).OrderBy(projectId => projectId, StringComparer.OrdinalIgnoreCase).ToArray(),
            })
            .OrderBy(group => group.Slug, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string BuildFingerprintKey(ProjectRecord project)
    {
        return TryBuildFingerprintKey(project, out var key)
            ? key
            : string.Empty;
    }
}

/// <summary>
/// Duplicate project group that shares the same repository fingerprint.
/// </summary>
public sealed class ProjectDuplicateFingerprintGroup
{
    /// <summary>Stable grouping key derived from the fingerprint.</summary>
    public required string FingerprintKey { get; set; }

    /// <summary>Canonical project identifier selected to keep.</summary>
    public required string CanonicalProjectId { get; set; }

    /// <summary>All project identifiers in the duplicate group.</summary>
    public required IReadOnlyList<string> ProjectIds { get; set; }

    /// <summary>Full project records in the duplicate group.</summary>
    public required IReadOnlyList<ProjectRecord> Projects { get; set; }
}

/// <summary>
/// Duplicate project group that shares the same slug.
/// </summary>
public sealed class ProjectDuplicateSlugGroup
{
    /// <summary>Shared slug value.</summary>
    public required string Slug { get; set; }

    /// <summary>Project identifiers currently using that slug.</summary>
    public required IReadOnlyList<string> ProjectIds { get; set; }
}
