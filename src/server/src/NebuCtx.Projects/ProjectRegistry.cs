namespace NebuCtx.Projects;

using NebuCtx.Contracts.Projects;
using NebuCtx.Storage;

/// <summary>
/// Project registry service. Handles project creation, lookup, and identity resolution.
/// This is the canonical owner of the project identity model.
/// </summary>
public sealed class ProjectRegistry
{
    private readonly IProjectStore _projectStore;
    private readonly IWorkspaceBindingStore _bindingStore;

    /// <summary>
    /// Initializes the project registry.
    /// </summary>
    /// <param name="projectStore">Project persistence store.</param>
    /// <param name="bindingStore">Workspace binding persistence store.</param>
    public ProjectRegistry(IProjectStore projectStore, IWorkspaceBindingStore bindingStore)
    {
        _projectStore = projectStore;
        _bindingStore = bindingStore;
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
        var existing = await _projectStore.FindByFingerprintAsync(fingerprint, cancellationToken);
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
    /// Binds a workspace (local checkout) to a project.
    /// </summary>
    /// <param name="binding">The workspace binding to persist.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task BindWorkspaceAsync(WorkspaceBinding binding, CancellationToken cancellationToken = default)
    {
        return _bindingStore.UpsertBindingAsync(binding, cancellationToken);
    }

    /// <summary>
    /// Gets all workspace bindings for a project.
    /// </summary>
    /// <param name="projectId">Project identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>All bindings for the project.</returns>
    public Task<IReadOnlyList<WorkspaceBinding>> GetBindingsAsync(string projectId, CancellationToken cancellationToken = default)
    {
        return _bindingStore.GetBindingsAsync(projectId, cancellationToken);
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
