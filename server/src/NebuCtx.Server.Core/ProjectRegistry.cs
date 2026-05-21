namespace NebuCtx.Server.Core;

using NebuCtx.Contracts.Projects;
using NebuCtx.Storage;

/// <summary>
/// Project registry service. Handles project creation, lookup, and identity resolution.
/// This is the canonical owner of the project identity model.
/// </summary>
public sealed class ProjectRegistry
{
    private readonly IProjectStore _projectStore;
    private readonly ICheckoutBindingStore _bindingStore;
    private readonly IBrainStore _brainStore;
    private readonly IKnowledgeStore _knowledgeStore;
    private readonly ISessionStore _sessionStore;
    private readonly ICodeIndexStore _codeIndexStore;

    /// <summary>
    /// Initializes the project registry.
    /// </summary>
    /// <param name="projectStore">Project persistence store.</param>
    /// <param name="bindingStore">Checkout binding persistence store.</param>
    /// <param name="brainStore">Brain persistence store.</param>
    /// <param name="knowledgeStore">Knowledge persistence store.</param>
    /// <param name="sessionStore">Session persistence store.</param>
    /// <param name="codeIndexStore">Code index persistence store.</param>
    public ProjectRegistry(IProjectStore projectStore, ICheckoutBindingStore bindingStore, IBrainStore brainStore, IKnowledgeStore knowledgeStore, ISessionStore sessionStore, ICodeIndexStore codeIndexStore)
    {
        _projectStore = projectStore;
        _bindingStore = bindingStore;
        _brainStore = brainStore;
        _knowledgeStore = knowledgeStore;
        _sessionStore = sessionStore;
        _codeIndexStore = codeIndexStore;
    }

    /// <summary>
    /// Resolves a project from a fingerprint, creating a new project if no match exists.
    /// Returns null only if the match is ambiguous and explicit binding is required.
    /// </summary>
    /// <param name="fingerprint">Repository fingerprint from the client.</param>
    /// <param name="suggestedSlug">Suggested human-readable slug for new project creation.</param>
    /// <param name="projectMetadata">Optional compact project metadata snapshot from the client.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The resolved project record, or null if ambiguous.</returns>
    public async Task<ProjectRecord?> ResolveOrCreateAsync(RepositoryFingerprint fingerprint, string suggestedSlug, ProjectMetadataEnvelope? projectMetadata = null, CancellationToken cancellationToken = default)
    {
        if (!HasSafeFingerprint(fingerprint))
        {
            return null;
        }

        var matches = await _projectStore.ListByFingerprintAsync(fingerprint, cancellationToken);
        if (matches.Count > 1)
        {
            return null;
        }

        var existing = matches.Count == 1 ? matches[0] : null;
        if (existing is not null)
        {
            if (projectMetadata is not null)
            {
                existing.ProjectMetadata = projectMetadata;
                existing.UpdatedAt = DateTimeOffset.UtcNow;
                await _projectStore.UpdateProjectAsync(existing, cancellationToken);
            }

            return existing;
        }

        var projectId = GenerateProjectId();
        var now = DateTimeOffset.UtcNow;

        var project = new ProjectRecord
        {
            ProjectId = projectId,
            Slug = suggestedSlug,
            Fingerprint = fingerprint,
            CreatedAt = now,
            UpdatedAt = now,
            ProjectMetadata = projectMetadata,
        };

        await _projectStore.CreateProjectAsync(project, cancellationToken);
        return project;
    }

    /// <summary>
    /// Returns true when the provided repository fingerprint is safe enough to use for canonical project identity.
    /// </summary>
    private static bool HasSafeFingerprint(RepositoryFingerprint fingerprint)
    {
        return !string.IsNullOrWhiteSpace(fingerprint.RemoteUrl)
            || (!string.IsNullOrWhiteSpace(fingerprint.Host)
                && !string.IsNullOrWhiteSpace(fingerprint.Owner)
                && !string.IsNullOrWhiteSpace(fingerprint.RepoName));
    }

    /// <summary>
    /// Gets a project by its stable identifier.
    /// </summary>
    /// <param name="projectId">Project identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The project record, or null if not found.</returns>
    public Task<ProjectRecord?> GetAsync(string projectId, CancellationToken cancellationToken = default)
    {
        return _projectStore.GetProjectAsync(projectId, cancellationToken);
    }

    /// <summary>
    /// Returns true when the provided project identifier exists in the canonical registry.
    /// </summary>
    /// <param name="projectId">Project identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True when the project exists.</returns>
    public async Task<bool> ExistsAsync(string projectId, CancellationToken cancellationToken = default)
    {
        return await _projectStore.GetProjectAsync(projectId, cancellationToken) is not null;
    }

    /// <summary>
    /// Lists all registered projects.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>All project records.</returns>
    public Task<IReadOnlyList<ProjectRecord>> ListAsync(CancellationToken cancellationToken = default)
    {
        return _projectStore.ListProjectsAsync(cancellationToken);
    }

    /// <summary>
    /// Persists the latest compact project metadata snapshot for an existing project.
    /// </summary>
    /// <param name="projectId">Project identifier.</param>
    /// <param name="projectMetadata">Compact project metadata snapshot.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task SyncProjectMetadataAsync(string projectId, ProjectMetadataEnvelope? projectMetadata, CancellationToken cancellationToken = default)
    {
        if (projectMetadata is null)
        {
            return;
        }

        var project = await _projectStore.GetProjectAsync(projectId, cancellationToken);
        if (project is null)
        {
            return;
        }

        project.ProjectMetadata = projectMetadata;
        project.UpdatedAt = DateTimeOffset.UtcNow;
        await _projectStore.UpdateProjectAsync(project, cancellationToken);
    }

    /// <summary>
    /// Clears the persisted project metadata for a single project.
    /// </summary>
    /// <param name="projectId">Project identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True when the project exists and was updated.</returns>
    public async Task<bool> ClearProjectMetadataAsync(string projectId, CancellationToken cancellationToken = default)
    {
        var project = await _projectStore.GetProjectAsync(projectId, cancellationToken);
        if (project is null)
        {
            return false;
        }

        project.ProjectMetadata = null;
        project.UpdatedAt = DateTimeOffset.UtcNow;
        await _projectStore.UpdateProjectAsync(project, cancellationToken);
        return true;
    }

    /// <summary>
    /// Binds a local checkout to a project.
    /// </summary>
    /// <param name="binding">The checkout binding to persist.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task BindCheckoutAsync(CheckoutBinding binding, CancellationToken cancellationToken = default)
    {
        return _bindingStore.UpsertBindingAsync(binding, cancellationToken);
    }

    /// <summary>
    /// Gets all checkout bindings for a project.
    /// </summary>
    /// <param name="projectId">Project identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>All bindings for the project.</returns>
    public Task<IReadOnlyList<CheckoutBinding>> GetBindingsAsync(string projectId, CancellationToken cancellationToken = default)
    {
        return _bindingStore.GetBindingsAsync(projectId, cancellationToken);
    }

    /// <summary>
    /// Deletes a project and clears dependent project-scoped stores first.
    /// </summary>
    /// <param name="projectId">Project identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Delete summary payload.</returns>
    public async Task<ProjectDeleteResult> DeleteProjectAsync(string projectId, CancellationToken cancellationToken = default)
    {
        var project = await _projectStore.GetProjectAsync(projectId, cancellationToken);
        if (project is null)
        {
            return new ProjectDeleteResult
            {
                ProjectId = projectId,
                Deleted = false,
            };
        }

        var checkoutBindingsDeleted = await _bindingStore.ClearProjectAsync(projectId, cancellationToken);
        var sessionsDeleted = await _sessionStore.ClearProjectAsync(projectId, cancellationToken);
        var brainEntriesDeleted = await _brainStore.ClearProjectAsync(projectId, cancellationToken);
        var knowledgeEntriesDeleted = await _knowledgeStore.ClearProjectAsync(projectId, cancellationToken);
        await _codeIndexStore.ClearProjectAsync(projectId, cancellationToken);
        var deleted = await _projectStore.DeleteProjectAsync(projectId, cancellationToken);

        return new ProjectDeleteResult
        {
            ProjectId = projectId,
            Deleted = deleted,
            CheckoutBindingsDeleted = checkoutBindingsDeleted,
            SessionsDeleted = sessionsDeleted,
            BrainEntriesDeleted = brainEntriesDeleted,
            KnowledgeEntriesDeleted = knowledgeEntriesDeleted,
        };
    }

    /// <summary>
    /// Generates a stable, unique project identifier.
    /// Format: "proj_" prefix + short GUID for readability.
    /// </summary>
    private static string GenerateProjectId()
    {
        return $"proj_{Guid.NewGuid():N}";
    }
}

/// <summary>
/// Result payload for project delete operations.
/// </summary>
public sealed class ProjectDeleteResult
{
    /// <summary>Project identifier.</summary>
    public required string ProjectId { get; set; }

    /// <summary>Whether the project record was deleted.</summary>
    public bool Deleted { get; set; }

    /// <summary>Number of checkout bindings removed.</summary>
    public int CheckoutBindingsDeleted { get; set; }

    /// <summary>Number of hosted sessions removed.</summary>
    public int SessionsDeleted { get; set; }

    /// <summary>Number of brain entries removed.</summary>
    public int BrainEntriesDeleted { get; set; }

    /// <summary>Number of knowledge entries removed.</summary>
    public int KnowledgeEntriesDeleted { get; set; }
}
