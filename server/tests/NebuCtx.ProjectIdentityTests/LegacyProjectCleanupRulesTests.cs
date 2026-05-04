namespace NebuCtx.ProjectIdentityTests;

using NebuCtx.Contracts.Projects;
using NebuCtx.Server.Core;

/// <summary>
/// Verifies which legacy ambiguous project records are safe to hard-delete during live cleanup.
/// </summary>
public sealed class LegacyProjectCleanupRulesTests
{
    /// <summary>
    /// Legacy mark-style projects without a safe fingerprint or useful metadata are removable.
    /// </summary>
    [Fact]
    public void IsSafeToDelete_ReturnsTrue_ForLegacyAmbiguousProjectWithoutIdentity()
    {
        var project = new ProjectRecord
        {
            ProjectId = "proj_deadbeef",
            Slug = "project-mark",
            Fingerprint = new RepositoryFingerprint(),
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            ProjectMetadata = new ProjectMetadataEnvelope
            {
                SchemaVersion = 1,
                Summary = new ProjectMetadataSummary
                {
                    TotalFileCount = 0,
                    SourceFileCount = 0,
                },
            },
        };

        Assert.True(LegacyProjectCleanupRules.IsSafeToDelete(project));
    }

    /// <summary>
    /// Projects with a real repository fingerprint must never be hard-deleted by the legacy cleanup.
    /// </summary>
    [Fact]
    public void IsSafeToDelete_ReturnsFalse_ForProjectWithSafeFingerprint()
    {
        var project = new ProjectRecord
        {
            ProjectId = "proj_realrepo",
            Slug = "project-mark",
            Fingerprint = new RepositoryFingerprint
            {
                RemoteUrl = "https://github.com/MarkBovee/nebu-ctx.git",
                Host = "github.com",
                Owner = "MarkBovee",
                RepoName = "nebu-ctx",
            },
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };

        Assert.False(LegacyProjectCleanupRules.IsSafeToDelete(project));
    }

    /// <summary>
    /// Projects with non-trivial synced metadata are preserved even if their slug is legacy.
    /// </summary>
    [Fact]
    public void IsSafeToDelete_ReturnsFalse_ForProjectWithUsefulMetadata()
    {
        var project = new ProjectRecord
        {
            ProjectId = "proj_usefulmeta",
            Slug = "mark",
            Fingerprint = new RepositoryFingerprint(),
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            ProjectMetadata = new ProjectMetadataEnvelope
            {
                SchemaVersion = 1,
                Summary = new ProjectMetadataSummary
                {
                    TotalFileCount = 12,
                    SourceFileCount = 7,
                },
            },
        };

        Assert.False(LegacyProjectCleanupRules.IsSafeToDelete(project));
    }

    /// <summary>
    /// Repository-derived project slugs should come directly from the canonical repo name.
    /// </summary>
    [Fact]
    public void CanonicalSlugFromRepoName_UsesRepositoryName()
    {
        Assert.Equal("nebu-ctx", LegacyProjectCleanupRules.CanonicalSlugFromRepoName("nebu-ctx"));
    }

    /// <summary>
    /// Legacy projects with no stored or inferred repository identity remain unresolved.
    /// </summary>
    [Fact]
    public void IsUnresolvedLegacyProject_ReturnsTrue_WhenNoRepoIdentityExists()
    {
        var project = new ProjectRecord
        {
            ProjectId = "proj_markb",
            Slug = "markb",
            Fingerprint = new RepositoryFingerprint(),
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            ProjectMetadata = new ProjectMetadataEnvelope
            {
                SchemaVersion = 1,
                Summary = new ProjectMetadataSummary
                {
                    TotalFileCount = 154215,
                    SourceFileCount = 60713,
                },
            },
        };

        Assert.True(LegacyProjectCleanupRules.IsUnresolvedLegacyProject(project, null));
    }

    /// <summary>
    /// Legacy projects with an inferred remote are no longer unresolved.
    /// </summary>
    [Fact]
    public void IsUnresolvedLegacyProject_ReturnsFalse_WhenInferredRepoIdentityExists()
    {
        var project = new ProjectRecord
        {
            ProjectId = "proj_markb",
            Slug = "markb",
            Fingerprint = new RepositoryFingerprint(),
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };

        var inferredFingerprint = new RepositoryFingerprint
        {
            RemoteUrl = "https://github.com/MarkBovee/nebu-ctx.git",
            Host = "github.com",
            Owner = "MarkBovee",
            RepoName = "nebu-ctx",
        };

        Assert.False(LegacyProjectCleanupRules.IsUnresolvedLegacyProject(project, inferredFingerprint));
    }
}
