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

    /// <summary>
    /// Lists all brain memory entries for a project, ordered by creation date descending.
    /// Used by the dashboard to display real Postgres-backed brain memory.
    /// </summary>
    /// <param name="projectId">Project identifier.</param>
    /// <param name="limit">Maximum number of entries to return.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>All brain entries up to the specified limit.</returns>
    Task<IReadOnlyList<BrainEntry>> ListAllAsync(string projectId, int limit = 200, CancellationToken cancellationToken = default);
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

/// <summary>
/// Abstraction for categorized knowledge storage (ctx_knowledge).
/// </summary>
public interface IKnowledgeStore
{
    /// <summary>
    /// Stores or updates a categorized knowledge fact for a project.
    /// </summary>
    /// <param name="entry">Knowledge entry to persist.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task UpsertFactAsync(KnowledgeEntry entry, CancellationToken cancellationToken = default);

    /// <summary>
    /// Searches knowledge facts by text query, optionally filtered by category.
    /// </summary>
    /// <param name="projectId">Project identifier.</param>
    /// <param name="category">Optional category filter.</param>
    /// <param name="query">Text search query matched against key and value.</param>
    /// <param name="limit">Maximum results to return.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Matching knowledge entries ordered by confidence descending.</returns>
    Task<IReadOnlyList<KnowledgeEntry>> RecallAsync(string projectId, string? category, string query, int limit, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists all distinct categories with their fact counts for a project.
    /// </summary>
    /// <param name="projectId">Project identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Category name → fact count pairs ordered by category name.</returns>
    Task<IReadOnlyList<(string Category, int Count)>> GetCategoriesAsync(string projectId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Counts the total number of knowledge facts stored for a project.
    /// </summary>
    /// <param name="projectId">Project identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<int> GetFactCountAsync(string projectId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists all knowledge facts for a project, ordered by category then key.
    /// Used by the dashboard to display real Postgres-backed knowledge.
    /// </summary>
    /// <param name="projectId">Project identifier.</param>
    /// <param name="limit">Maximum number of entries to return.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>All knowledge entries up to the specified limit.</returns>
    Task<IReadOnlyList<KnowledgeEntry>> ListAllForProjectAsync(string projectId, int limit = 500, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a specific knowledge fact by category and key.
    /// </summary>
    /// <param name="projectId">Project identifier.</param>
    /// <param name="category">Fact category.</param>
    /// <param name="key">Fact key.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if the entry was found and deleted; false if it did not exist.</returns>
    Task<bool> RemoveFactAsync(string projectId, string category, string key, CancellationToken cancellationToken = default);
}

/// <summary>
/// A single categorized knowledge fact.
/// </summary>
public sealed class KnowledgeEntry
{
    /// <summary>Project this fact belongs to.</summary>
    public required string ProjectId { get; set; }

    /// <summary>Logical grouping for the fact (e.g. "architecture", "conventions").</summary>
    public required string Category { get; set; }

    /// <summary>Unique key within the category.</summary>
    public required string Key { get; set; }

    /// <summary>The fact value.</summary>
    public required string Value { get; set; }

    /// <summary>Confidence score between 0 and 1. Defaults to 1.0 (certain).</summary>
    public float Confidence { get; set; } = 1.0f;

    /// <summary>When this fact was last updated.</summary>
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>
/// Abstraction for project-scoped session state storage (ctx_session).
/// </summary>
public interface ISessionStore
{
    /// <summary>
    /// Loads the most recently updated session for a project.
    /// </summary>
    /// <param name="projectId">Project identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The latest session state, or null if none exists.</returns>
    Task<CloudSessionState?> LoadLatestAsync(string projectId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads a specific session by its identifier.
    /// </summary>
    /// <param name="projectId">Project identifier.</param>
    /// <param name="sessionId">Session identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The session state, or null if not found.</returns>
    Task<CloudSessionState?> LoadByIdAsync(string projectId, string sessionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Saves or updates a session state for a project.
    /// </summary>
    /// <param name="projectId">Project identifier.</param>
    /// <param name="state">Session state to persist.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task SaveAsync(string projectId, CloudSessionState state, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists session summaries for a project, most recent first.
    /// </summary>
    /// <param name="projectId">Project identifier.</param>
    /// <param name="limit">Maximum sessions to return.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<CloudSessionSummary>> ListAsync(string projectId, int limit, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes sessions older than the specified number of days.
    /// </summary>
    /// <param name="projectId">Project identifier.</param>
    /// <param name="daysOld">Age threshold in days.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Number of sessions deleted.</returns>
    Task<int> DeleteOlderThanAsync(string projectId, int daysOld, CancellationToken cancellationToken = default);
}

/// <summary>
/// Cloud-persisted agent session state for a project.
/// </summary>
public sealed class CloudSessionState
{
    /// <summary>Short unique identifier for this session.</summary>
    public string SessionId { get; set; } = Guid.NewGuid().ToString("N")[..8];

    /// <summary>Version counter, incremented on each save.</summary>
    public int Version { get; set; }

    /// <summary>Current task description set by the agent.</summary>
    public string? Task { get; set; }

    /// <summary>Key findings recorded during this session.</summary>
    public List<string> Findings { get; set; } = [];

    /// <summary>Decisions recorded during this session.</summary>
    public List<string> Decisions { get; set; } = [];

    /// <summary>Number of tool calls made in this session.</summary>
    public int ToolCalls { get; set; }

    /// <summary>When this session was first created.</summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>When this session was last saved.</summary>
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Compact session summary used in session list responses.
/// </summary>
public sealed class CloudSessionSummary
{
    /// <summary>Session identifier.</summary>
    public required string SessionId { get; set; }

    /// <summary>Save version.</summary>
    public int Version { get; set; }

    /// <summary>Task description at last save.</summary>
    public string? Task { get; set; }

    /// <summary>Number of tool calls at last save.</summary>
    public int ToolCalls { get; set; }

    /// <summary>When the session was last saved.</summary>
    public DateTimeOffset UpdatedAt { get; set; }
}
