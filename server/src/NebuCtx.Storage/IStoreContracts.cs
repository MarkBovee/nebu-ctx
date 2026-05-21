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
    /// Lists all projects that match a repository fingerprint.
    /// </summary>
    /// <param name="fingerprint">Repository fingerprint to search for.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>All matching projects, including ambiguous duplicate records.</returns>
    Task<IReadOnlyList<ProjectRecord>> ListByFingerprintAsync(RepositoryFingerprint fingerprint, CancellationToken cancellationToken = default);

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

    /// <summary>
    /// Deletes a project record after its dependent data has been moved or cleared.
    /// </summary>
    /// <param name="projectId">Project identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True when the project existed and was deleted.</returns>
    Task<bool> DeleteProjectAsync(string projectId, CancellationToken cancellationToken = default);
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

    /// <summary>
    /// Deletes all checkout bindings for a project.
    /// </summary>
    /// <param name="projectId">Project identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Number of bindings deleted.</returns>
    Task<int> ClearProjectAsync(string projectId, CancellationToken cancellationToken = default);
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

    /// <summary>
    /// Deletes a specific brain memory entry by project and key.
    /// </summary>
    /// <param name="projectId">Project identifier.</param>
    /// <param name="key">Memory key to delete.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if the entry was found and deleted, false if not found.</returns>
    Task<bool> DeleteAsync(string projectId, string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes all brain memory entries for a project.
    /// </summary>
    /// <param name="projectId">Project identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Number of entries deleted.</returns>
    Task<int> ClearProjectAsync(string projectId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes all brain entries for a project whose key starts with the provided prefix.
    /// </summary>
    /// <param name="projectId">Project identifier.</param>
    /// <param name="keyPrefix">Entry key prefix to delete.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Number of entries deleted.</returns>
    Task<int> DeleteByPrefixAsync(string projectId, string keyPrefix, CancellationToken cancellationToken = default);
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
    /// Loads a single categorized knowledge fact for a project.
    /// </summary>
    /// <param name="projectId">Project identifier.</param>
    /// <param name="category">Fact category.</param>
    /// <param name="key">Fact key.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The matching knowledge entry when present; otherwise <see langword="null"/>.</returns>
    Task<KnowledgeEntry?> GetFactAsync(string projectId, string category, string key, CancellationToken cancellationToken = default);

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

    /// <summary>
    /// Deletes all persisted knowledge facts for a project.
    /// </summary>
    /// <param name="projectId">Project identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Number of entries deleted.</returns>
    Task<int> ClearProjectAsync(string projectId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reassigns all knowledge facts from one project to another.
    /// </summary>
    /// <param name="fromProjectId">Source project identifier.</param>
    /// <param name="toProjectId">Destination project identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Number of entries moved.</returns>
    Task<int> ReassignProjectAsync(string fromProjectId, string toProjectId, CancellationToken cancellationToken = default);
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

    /// <summary>When this fact was first created.</summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>When this fact was last updated.</summary>
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Stable logical key used for deterministic promotion identity derivation.</summary>
    public string LogicalKey { get; set; } = string.Empty;

    /// <summary>Stable deterministic identity for replay-safe promotion and lifecycle tracking.</summary>
    public string PromotionIdentity { get; set; } = string.Empty;

    /// <summary>Source type that produced or last confirmed this fact.</summary>
    public string SourceType { get; set; } = "remember";

    /// <summary>Source scope that produced this fact, such as a project or session identifier.</summary>
    public string SourceScope { get; set; } = string.Empty;

    /// <summary>Lifecycle status for the current canonical fact.</summary>
    public string LifecycleStatus { get; set; } = "current";

    /// <summary>Current lifecycle score used for wake-up ranking and recall ordering.</summary>
    public float LifecycleScore { get; set; }

    /// <summary>How many times this fact has been explicitly confirmed.</summary>
    public int ConfirmationCount { get; set; } = 1;

    /// <summary>When this fact was last explicitly confirmed.</summary>
    public DateTimeOffset? LastConfirmedAt { get; set; }

    /// <summary>How often this fact has been retrieved through hosted recall or wake-up selection.</summary>
    public int RetrievalCount { get; set; }

    /// <summary>When this fact was last retrieved through hosted recall or wake-up selection.</summary>
    public DateTimeOffset? LastRetrievedAt { get; set; }

    /// <summary>Historical revisions retained when the canonical fact changes over time.</summary>
    public List<KnowledgeHistoryEntry> History { get; set; } = [];
}

/// <summary>
/// Historical revision retained for a knowledge fact after a canonical update.
/// </summary>
public sealed class KnowledgeHistoryEntry
{
    /// <summary>Historical fact value.</summary>
    public string Value { get; set; } = string.Empty;

    /// <summary>Confidence at the time this revision was current.</summary>
    public float Confidence { get; set; }

    /// <summary>Promotion identity that produced this historical revision.</summary>
    public string PromotionIdentity { get; set; } = string.Empty;

    /// <summary>Source type that produced this historical revision.</summary>
    public string SourceType { get; set; } = string.Empty;

    /// <summary>Source scope that produced this historical revision.</summary>
    public string SourceScope { get; set; } = string.Empty;

    /// <summary>When this revision became current.</summary>
    public DateTimeOffset? ValidFrom { get; set; }

    /// <summary>When this revision stopped being current.</summary>
    public DateTimeOffset SupersededAt { get; set; }
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

    /// <summary>
    /// Deletes all persisted sessions for a project.
    /// </summary>
    /// <param name="projectId">Project identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Number of sessions deleted.</returns>
    Task<int> ClearProjectAsync(string projectId, CancellationToken cancellationToken = default);
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

/// <summary>
/// Abstraction for project-scoped source code index storage.
/// Persists file metadata, symbols, and call edges uploaded by the Rust client.
/// </summary>
public interface ICodeIndexStore
{
    /// <summary>
    /// Replaces all indexed files and symbols for a project in a single batch operation.
    /// Existing data for the project is deleted and replaced with the new snapshot.
    /// </summary>
    /// <param name="projectId">Project identifier.</param>
    /// <param name="files">Indexed file entries.</param>
    /// <param name="symbols">Indexed symbol entries.</param>
    /// <param name="edges">Call graph edges.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task SyncIndexAsync(string projectId, IReadOnlyList<IndexedFile> files, IReadOnlyList<IndexedSymbol> symbols, IReadOnlyList<IndexedCallEdge> edges, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns index summary stats for a project.
    /// </summary>
    /// <param name="projectId">Project identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>File count, symbol count, edge count, and language distribution.</returns>
    Task<CodeIndexStats> GetStatsAsync(string projectId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Searches symbols by name for a project, optionally filtered by kind.
    /// </summary>
    /// <param name="projectId">Project identifier.</param>
    /// <param name="query">Substring query matched against symbol name.</param>
    /// <param name="kind">Optional kind filter (fn, struct, class, etc.).</param>
    /// <param name="limit">Maximum results.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Matching symbol entries.</returns>
    Task<IReadOnlyList<IndexedSymbol>> SearchSymbolsAsync(string projectId, string? query, string? kind, int limit = 200, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns call graph edges for a project.
    /// </summary>
    /// <param name="projectId">Project identifier.</param>
    /// <param name="limit">Maximum edges to return.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Call edge list.</returns>
    Task<IReadOnlyList<IndexedCallEdge>> GetEdgesAsync(string projectId, int limit = 5000, CancellationToken cancellationToken = default);

    /// <summary>
    /// Searches files by path for a project.
    /// </summary>
    /// <param name="projectId">Project identifier.</param>
    /// <param name="query">Substring query matched against file path.</param>
    /// <param name="limit">Maximum results.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Matching file entries ordered by token count descending.</returns>
    Task<IReadOnlyList<IndexedFile>> SearchFilesAsync(string projectId, string? query, int limit = 100, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes all indexed files, symbols, and call edges for a project.
    /// </summary>
    /// <param name="projectId">Project identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True when project index data existed and was cleared.</returns>
    Task<bool> ClearProjectAsync(string projectId, CancellationToken cancellationToken = default);
}

/// <summary>
/// A single indexed source file entry.
/// </summary>
public sealed class IndexedFile
{
    /// <summary>Relative path within the project.</summary>
    public required string Path { get; set; }

    /// <summary>Content hash for change detection.</summary>
    public string Hash { get; set; } = "";

    /// <summary>Detected language (e.g. rs, cs, ts).</summary>
    public string Language { get; set; } = "";

    /// <summary>Total line count.</summary>
    public int LineCount { get; set; }

    /// <summary>Estimated token count.</summary>
    public int TokenCount { get; set; }

    /// <summary>Top-level exported names.</summary>
    public List<string> Exports { get; set; } = [];

    /// <summary>One-line summary of the file's primary purpose.</summary>
    public string Summary { get; set; } = "";
}

/// <summary>
/// A single indexed symbol (function, struct, class, etc.).
/// </summary>
public sealed class IndexedSymbol
{
    /// <summary>Relative file path containing this symbol.</summary>
    public required string FilePath { get; set; }

    /// <summary>Symbol name.</summary>
    public required string Name { get; set; }

    /// <summary>Symbol kind: fn, struct, class, method, trait, enum, etc.</summary>
    public string Kind { get; set; } = "";

    /// <summary>Start line (1-based).</summary>
    public int StartLine { get; set; }

    /// <summary>End line (1-based).</summary>
    public int EndLine { get; set; }

    /// <summary>Whether the symbol is publicly exported.</summary>
    public bool IsExported { get; set; }
}

/// <summary>
/// A directed call edge between two symbols.
/// </summary>
public sealed class IndexedCallEdge
{
    /// <summary>Calling symbol name.</summary>
    public required string FromSymbol { get; set; }

    /// <summary>Called symbol name.</summary>
    public required string ToSymbol { get; set; }

    /// <summary>Edge kind (call, import, use, etc.).</summary>
    public string Kind { get; set; } = "call";
}

/// <summary>
/// Aggregate stats for a project's code index.
/// </summary>
public sealed class CodeIndexStats
{
    /// <summary>Number of indexed files.</summary>
    public int FileCount { get; set; }

    /// <summary>Number of indexed symbols.</summary>
    public int SymbolCount { get; set; }

    /// <summary>Number of call edges.</summary>
    public int EdgeCount { get; set; }

    /// <summary>Language → file count distribution.</summary>
    public Dictionary<string, int> LanguageDistribution { get; set; } = [];

    /// <summary>When the index was last synced.</summary>
    public DateTimeOffset? LastIndexedAt { get; set; }
}
