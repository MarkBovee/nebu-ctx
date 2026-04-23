namespace NebuCtx.Storage;

using NebuCtx.Contracts.Projects;

/// <summary>
/// Abstraction for project-scoped persistent storage.
/// The supported runtime implementation is Postgres-backed.
/// </summary>
public interface IProjectStore
{
    /// <summary>
    /// Retrieves a project by its stable server-generated identifier.
    /// </summary>
    /// <param name="projectId">The project's unique identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The project record, or null if not found.</returns>
    Task<ProjectRecord?> GetProjectAsync(string projectId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds a project by its repository fingerprint for auto-matching.
    /// Returns null if no match or if the match is ambiguous.
    /// </summary>
    /// <param name="fingerprint">Repository fingerprint to search for.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The matched project, or null if no unique match.</returns>
    Task<ProjectRecord?> FindByFingerprintAsync(RepositoryFingerprint fingerprint, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a new project record.
    /// </summary>
    /// <param name="project">Project record to persist.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task CreateProjectAsync(ProjectRecord project, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates an existing project record.
    /// </summary>
    /// <param name="project">Project record with updated values.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task UpdateProjectAsync(ProjectRecord project, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists all registered projects.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>All project records.</returns>
    Task<IReadOnlyList<ProjectRecord>> ListProjectsAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Abstraction for checkout binding persistence.
/// </summary>
public interface ICheckoutBindingStore
{
    /// <summary>
    /// Saves or updates a checkout binding for a project.
    /// </summary>
    /// <param name="binding">The binding to persist.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task UpsertBindingAsync(CheckoutBinding binding, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists all checkout bindings for a given project.
    /// </summary>
    /// <param name="projectId">The project's unique identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>All bindings for the project.</returns>
    Task<IReadOnlyList<CheckoutBinding>> GetBindingsAsync(string projectId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Abstraction for brain memory storage (ctx_brain).
/// </summary>
public interface IBrainStore
{
    /// <summary>
    /// Retrieves the brain status for a project.
    /// </summary>
    /// <param name="projectId">Project identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Brain status payload as a dictionary, or null if no state exists.</returns>
    Task<Dictionary<string, object?>?> GetStatusAsync(string projectId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stores a memory entry in the brain for a project.
    /// </summary>
    /// <param name="projectId">Project identifier.</param>
    /// <param name="key">Memory key.</param>
    /// <param name="value">Memory value payload.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task StoreAsync(string projectId, string key, string value, CancellationToken cancellationToken = default);

    /// <summary>
    /// Recalls memory entries matching the given query.
    /// </summary>
    /// <param name="projectId">Project identifier.</param>
    /// <param name="query">Search query for memory recall.</param>
    /// <param name="limit">Maximum number of results.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of matching memory entries as key-value pairs.</returns>
    Task<IReadOnlyList<BrainEntry>> RecallAsync(string projectId, string query, int limit = 10, CancellationToken cancellationToken = default);
}

/// <summary>
/// A single brain memory entry.
/// </summary>
public sealed class BrainEntry
{
    /// <summary>
    /// Memory key identifier.
    /// </summary>
    public required string Key { get; set; }

    /// <summary>
    /// Memory value content.
    /// </summary>
    public required string Value { get; set; }

    /// <summary>
    /// When this entry was stored.
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; }
}
