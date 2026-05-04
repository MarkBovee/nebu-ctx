namespace NebuCtx.Server.Core;

using NebuCtx.Contracts.Projects;

/// <summary>
/// Centralizes the safety rules for deleting legacy ambiguous project records.
/// </summary>
public static class LegacyProjectCleanupRules
{
    /// <summary>
    /// Returns true only when a project matches the known legacy mark-style corruption pattern
    /// and does not carry a trustworthy repository identity or useful synced metadata.
    /// </summary>
    public static bool IsSafeToDelete(ProjectRecord project)
    {
        ArgumentNullException.ThrowIfNull(project);

        return IsLegacyAmbiguousSlug(project.Slug)
            && !HasSafeFingerprint(project)
            && !HasUsefulMetadata(project);
    }

    /// <summary>
    /// Returns true when the project slug matches the known legacy ambiguous mark aliases.
    /// </summary>
    public static bool IsLegacyAmbiguousSlug(string? slug)
    {
        return string.Equals(slug, "mark", StringComparison.OrdinalIgnoreCase)
            || string.Equals(slug, "markb", StringComparison.OrdinalIgnoreCase)
            || string.Equals(slug, "project-mark", StringComparison.OrdinalIgnoreCase)
            || string.Equals(slug, "project-markb", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Returns true when the project has a repository fingerprint safe enough to preserve.
    /// </summary>
    public static bool HasSafeFingerprint(ProjectRecord project)
    {
        var fingerprint = project.Fingerprint;
        return fingerprint is not null
            && (!string.IsNullOrWhiteSpace(fingerprint.RemoteUrl)
                || (!string.IsNullOrWhiteSpace(fingerprint.Host)
                    && !string.IsNullOrWhiteSpace(fingerprint.Owner)
                    && !string.IsNullOrWhiteSpace(fingerprint.RepoName)));
    }

    /// <summary>
    /// Returns true when the project still carries non-trivial synced metadata.
    /// </summary>
    public static bool HasUsefulMetadata(ProjectRecord project)
    {
        var summary = project.ProjectMetadata?.Summary;
        return summary is not null && (summary.SourceFileCount > 0 || summary.TotalFileCount > 0);
    }

    /// <summary>
    /// Derives the canonical slug from a repository name discovered from git remote metadata.
    /// </summary>
    public static string CanonicalSlugFromRepoName(string repoName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repoName);
        return repoName.Trim();
    }

    /// <summary>
    /// Returns true when a legacy ambiguous project still has no trustworthy repository identity
    /// after all available inference has been attempted.
    /// </summary>
    public static bool IsUnresolvedLegacyProject(ProjectRecord project, RepositoryFingerprint? inferredFingerprint)
    {
        ArgumentNullException.ThrowIfNull(project);

        if (!IsLegacyAmbiguousSlug(project.Slug) || HasSafeFingerprint(project))
        {
            return false;
        }

        return inferredFingerprint is null
            || (string.IsNullOrWhiteSpace(inferredFingerprint.RemoteUrl)
                && (string.IsNullOrWhiteSpace(inferredFingerprint.Host)
                    || string.IsNullOrWhiteSpace(inferredFingerprint.Owner)
                    || string.IsNullOrWhiteSpace(inferredFingerprint.RepoName)));
    }
}
