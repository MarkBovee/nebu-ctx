namespace NebuCtx.Contracts.Dashboard;

using System.Text.Json.Serialization;

/// <summary>
/// Response for GET /api/stats — token savings statistics.
/// </summary>
public sealed class StatsResponse
{
    /// <summary>
    /// Total tokens saved across all sessions.
    /// </summary>
    [JsonPropertyName("total_tokens_saved")]
    public long TotalTokensSaved { get; set; }

    /// <summary>
    /// Total tokens processed as input.
    /// </summary>
    [JsonPropertyName("total_tokens_input")]
    public long TotalTokensInput { get; set; }

    /// <summary>
    /// Number of cache hits.
    /// </summary>
    [JsonPropertyName("cache_hits")]
    public int CacheHits { get; set; }

    /// <summary>
    /// Total tool calls executed.
    /// </summary>
    [JsonPropertyName("total_tool_calls")]
    public int TotalToolCalls { get; set; }
}

/// <summary>
/// Response for GET /api/session — current session state.
/// </summary>
public sealed class SessionResponse
{
    /// <summary>
    /// Unique session identifier.
    /// </summary>
    [JsonPropertyName("id")]
    public required string Id { get; set; }

    /// <summary>
    /// Session version number, incremented on state changes.
    /// </summary>
    [JsonPropertyName("version")]
    public int Version { get; set; }

    /// <summary>
    /// When the session started.
    /// </summary>
    [JsonPropertyName("started_at")]
    public DateTimeOffset StartedAt { get; set; }

    /// <summary>
    /// When the session was last updated.
    /// </summary>
    [JsonPropertyName("updated_at")]
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>
    /// Detected project root path.
    /// </summary>
    [JsonPropertyName("project_root")]
    public string? ProjectRoot { get; set; }

    /// <summary>
    /// Aggregated session statistics.
    /// </summary>
    [JsonPropertyName("stats")]
    public StatsResponse? Stats { get; set; }
}

/// <summary>
/// Response for GET /api/version.
/// </summary>
public sealed class VersionResponse
{
    /// <summary>
    /// Current server version.
    /// </summary>
    [JsonPropertyName("version")]
    public required string Version { get; set; }

    /// <summary>
    /// Server name.
    /// </summary>
    [JsonPropertyName("name")]
    public required string Name { get; set; }
}

/// <summary>
/// Response for GET /api/pulse — lightweight change detection.
/// </summary>
public sealed class PulseResponse
{
    /// <summary>
    /// Hash of the current stats file for change detection.
    /// </summary>
    [JsonPropertyName("hash")]
    public string? Hash { get; set; }

    /// <summary>
    /// Last modification time of the stats file.
    /// </summary>
    [JsonPropertyName("mtime")]
    public DateTimeOffset? Mtime { get; set; }
}

/// <summary>
/// Response for GET /api/auth-token.
/// </summary>
public sealed class AuthTokenResponse
{
    /// <summary>
    /// The current auth token value read from the token file.
    /// </summary>
    [JsonPropertyName("token")]
    public string? Token { get; set; }
}

/// <summary>
/// Response for GET /health.
/// </summary>
public sealed class HealthResponse
{
    /// <summary>
    /// Health status indicator.
    /// </summary>
    [JsonPropertyName("status")]
    public required string Status { get; set; }
}

/// <summary>
/// Aggregated dashboard overview payload used by the simplified overview UI.
/// </summary>
public sealed class DashboardOverviewResponse
{
    /// <summary>
    /// Version payload for the current server.
    /// </summary>
    [JsonPropertyName("version")]
    public required DashboardVersionPayload Version { get; set; }

    /// <summary>
    /// Aggregated telemetry and project overview statistics.
    /// </summary>
    [JsonPropertyName("stats")]
    public required DashboardStatsPayload Stats { get; set; }

    /// <summary>
    /// Gain summary for the overview page.
    /// </summary>
    [JsonPropertyName("gain")]
    public required DashboardGainPayload Gain { get; set; }

    /// <summary>
    /// Token value for admin and setup workflows.
    /// </summary>
    [JsonPropertyName("auth_token")]
    public string? AuthToken { get; set; }
}

/// <summary>
/// Dashboard domain navigation payload used to group detailed screens into fewer operator areas.
/// </summary>
public sealed class DashboardDomainsResponse
{
    /// <summary>Dashboard domain groups in display order.</summary>
    [JsonPropertyName("domains")]
    public required IReadOnlyList<DashboardDomainPayload> Domains { get; set; }
}

/// <summary>
/// Dashboard domain group containing related legacy views.
/// </summary>
public sealed class DashboardDomainPayload
{
    /// <summary>Stable domain identifier.</summary>
    [JsonPropertyName("id")]
    public required string Id { get; set; }

    /// <summary>Domain display label.</summary>
    [JsonPropertyName("label")]
    public required string Label { get; set; }

    /// <summary>Short explanation of what the domain contains.</summary>
    [JsonPropertyName("description")]
    public required string Description { get; set; }

    /// <summary>Detailed dashboard views assigned to this domain.</summary>
    [JsonPropertyName("views")]
    public required IReadOnlyList<DashboardDomainViewPayload> Views { get; set; }
}

/// <summary>
/// Reference to an existing dashboard view inside a domain group.
/// </summary>
public sealed class DashboardDomainViewPayload
{
    /// <summary>Stable view identifier used by the dashboard UI.</summary>
    [JsonPropertyName("id")]
    public required string Id { get; set; }

    /// <summary>View display label.</summary>
    [JsonPropertyName("label")]
    public required string Label { get; set; }
}

/// <summary>
/// Dashboard version payload including legacy compatibility fields.
/// </summary>
public sealed class DashboardVersionPayload
{
    /// <summary>Server product name.</summary>
    [JsonPropertyName("name")]
    public required string Name { get; set; }

    /// <summary>Current server version.</summary>
    [JsonPropertyName("version")]
    public required string Version { get; set; }

    /// <summary>Legacy current-version field used by dashboard update UI.</summary>
    [JsonPropertyName("current")]
    public required string Current { get; set; }

    /// <summary>Latest known version.</summary>
    [JsonPropertyName("latest")]
    public required string Latest { get; set; }

    /// <summary>Whether an update is available.</summary>
    [JsonPropertyName("update_available")]
    public bool UpdateAvailable { get; set; }
}

/// <summary>
/// Aggregated overview stats payload used by the dashboard overview.
/// </summary>
public sealed class DashboardStatsPayload
{
    /// <summary>Total tokens saved.</summary>
    [JsonPropertyName("total_tokens_saved")]
    public long TotalTokensSaved { get; set; }

    /// <summary>Total input tokens.</summary>
    [JsonPropertyName("total_tokens_input")]
    public long TotalTokensInput { get; set; }

    /// <summary>Legacy alias for total input tokens.</summary>
    [JsonPropertyName("total_input_tokens")]
    public long TotalInputTokensLegacy { get; set; }

    /// <summary>Total output tokens.</summary>
    [JsonPropertyName("total_output_tokens")]
    public long TotalOutputTokens { get; set; }

    /// <summary>Total cache hits.</summary>
    [JsonPropertyName("cache_hits")]
    public int CacheHits { get; set; }

    /// <summary>Total tool calls.</summary>
    [JsonPropertyName("total_tool_calls")]
    public int TotalToolCalls { get; set; }

    /// <summary>Legacy alias for total tool calls.</summary>
    [JsonPropertyName("total_commands")]
    public int TotalCommands { get; set; }

    /// <summary>First observed use timestamp.</summary>
    [JsonPropertyName("first_use")]
    public string? FirstUse { get; set; }

    /// <summary>Daily token/call aggregates.</summary>
    [JsonPropertyName("daily")]
    public required IReadOnlyList<DashboardDailyPayload> Daily { get; set; }

    /// <summary>Per-command aggregates.</summary>
    [JsonPropertyName("commands")]
    public required IReadOnlyDictionary<string, DashboardCommandPayload> Commands { get; set; }

    /// <summary>Registered project count.</summary>
    [JsonPropertyName("project_count")]
    public int ProjectCount { get; set; }

    /// <summary>Registered tool count.</summary>
    [JsonPropertyName("registered_tool_count")]
    public int RegisteredToolCount { get; set; }

    /// <summary>Indexed source file count.</summary>
    [JsonPropertyName("indexed_file_count")]
    public long IndexedFileCount { get; set; }

    /// <summary>Total known file count.</summary>
    [JsonPropertyName("total_file_count")]
    public long TotalFileCount { get; set; }

    /// <summary>Aggregated language distribution.</summary>
    [JsonPropertyName("language_distribution")]
    public required IReadOnlyList<DashboardLanguagePayload> LanguageDistribution { get; set; }

    /// <summary>Daily token savings grouped by project.</summary>
    [JsonPropertyName("project_daily_savings")]
    public required IReadOnlyList<DashboardProjectDailySavingsPayload> ProjectDailySavings { get; set; }

    /// <summary>Current active sessions shown on the overview.</summary>
    [JsonPropertyName("active_sessions")]
    public required IReadOnlyList<DashboardActiveSessionPayload> ActiveSessions { get; set; }
}

/// <summary>
/// Daily dashboard aggregate.
/// </summary>
public sealed class DashboardDailyPayload
{
    /// <summary>Date key.</summary>
    [JsonPropertyName("date")]
    public required string Date { get; set; }

    /// <summary>Input tokens.</summary>
    [JsonPropertyName("input_tokens")]
    public long InputTokens { get; set; }

    /// <summary>Output tokens.</summary>
    [JsonPropertyName("output_tokens")]
    public long OutputTokens { get; set; }

    /// <summary>Command count.</summary>
    [JsonPropertyName("commands")]
    public int Commands { get; set; }
}

/// <summary>
/// Per-command dashboard aggregate.
/// </summary>
public sealed class DashboardCommandPayload
{
    /// <summary>Display source bucket.</summary>
    [JsonPropertyName("source")]
    public required string Source { get; set; }

    /// <summary>Call count.</summary>
    [JsonPropertyName("count")]
    public int Count { get; set; }

    /// <summary>Input tokens.</summary>
    [JsonPropertyName("input_tokens")]
    public long InputTokens { get; set; }

    /// <summary>Output tokens.</summary>
    [JsonPropertyName("output_tokens")]
    public long OutputTokens { get; set; }
}

/// <summary>
/// Language distribution entry.
/// </summary>
public sealed class DashboardLanguagePayload
{
    /// <summary>Language name.</summary>
    [JsonPropertyName("language")]
    public required string Language { get; set; }

    /// <summary>File count.</summary>
    [JsonPropertyName("file_count")]
    public long FileCount { get; set; }
}

/// <summary>
/// Daily token savings entry for a single project.
/// </summary>
public sealed class DashboardProjectDailySavingsPayload
{
    /// <summary>Date key.</summary>
    [JsonPropertyName("date")]
    public required string Date { get; set; }

    /// <summary>Project identifier.</summary>
    [JsonPropertyName("project_id")]
    public required string ProjectId { get; set; }

    /// <summary>Project display name.</summary>
    [JsonPropertyName("project_name")]
    public required string ProjectName { get; set; }

    /// <summary>Saved token count for the day.</summary>
    [JsonPropertyName("tokens_saved")]
    public long TokensSaved { get; set; }

    /// <summary>Input token count for the day.</summary>
    [JsonPropertyName("input_tokens")]
    public long InputTokens { get; set; }

    /// <summary>Output token count for the day.</summary>
    [JsonPropertyName("output_tokens")]
    public long OutputTokens { get; set; }

    /// <summary>Recorded command count for the day.</summary>
    [JsonPropertyName("commands")]
    public int Commands { get; set; }
}

/// <summary>
/// Compact active-session entry for the overview panel.
/// </summary>
public sealed class DashboardActiveSessionPayload
{
    /// <summary>Stable session identifier.</summary>
    [JsonPropertyName("session_id")]
    public required string SessionId { get; set; }

    /// <summary>Project identifier.</summary>
    [JsonPropertyName("project_id")]
    public required string ProjectId { get; set; }

    /// <summary>Project display name.</summary>
    [JsonPropertyName("project_name")]
    public required string ProjectName { get; set; }

    /// <summary>Client or actor label.</summary>
    [JsonPropertyName("client_id")]
    public required string ClientId { get; set; }

    /// <summary>Last-seen timestamp.</summary>
    [JsonPropertyName("updated_at")]
    public required string UpdatedAt { get; set; }

    /// <summary>Number of tool calls in the session.</summary>
    [JsonPropertyName("tool_calls")]
    public int ToolCalls { get; set; }

    /// <summary>Saved token count for the session.</summary>
    [JsonPropertyName("tokens_saved")]
    public long TokensSaved { get; set; }
}

/// <summary>
/// Overview gain payload.
/// </summary>
public sealed class DashboardGainPayload
{
    /// <summary>Gain score summary.</summary>
    [JsonPropertyName("summary")]
    public required DashboardGainSummaryPayload Summary { get; set; }

    /// <summary>Task/category gain list.</summary>
    [JsonPropertyName("tasks")]
    public required IReadOnlyList<DashboardGainTaskPayload> Tasks { get; set; }
}

/// <summary>
/// Gain summary payload.
/// </summary>
public sealed class DashboardGainSummaryPayload
{
    /// <summary>Score breakdown.</summary>
    [JsonPropertyName("score")]
    public required DashboardGainScorePayload Score { get; set; }

    /// <summary>Pricing model payload.</summary>
    [JsonPropertyName("model")]
    public required DashboardGainModelPayload Model { get; set; }
}

/// <summary>
/// Gain score breakdown.
/// </summary>
public sealed class DashboardGainScorePayload
{
    /// <summary>Total score.</summary>
    [JsonPropertyName("total")]
    public int Total { get; set; }

    /// <summary>Compression score.</summary>
    [JsonPropertyName("compression")]
    public int Compression { get; set; }

    /// <summary>Cost efficiency score.</summary>
    [JsonPropertyName("cost_efficiency")]
    public int CostEfficiency { get; set; }

    /// <summary>Quality score.</summary>
    [JsonPropertyName("quality")]
    public int Quality { get; set; }

    /// <summary>Consistency score.</summary>
    [JsonPropertyName("consistency")]
    public int Consistency { get; set; }
}

/// <summary>
/// Gain model payload.
/// </summary>
public sealed class DashboardGainModelPayload
{
    /// <summary>Cost payload.</summary>
    [JsonPropertyName("cost")]
    public required DashboardGainCostPayload Cost { get; set; }
}

/// <summary>
/// Gain model cost payload.
/// </summary>
public sealed class DashboardGainCostPayload
{
    /// <summary>Input price per million tokens.</summary>
    [JsonPropertyName("input_per_m")]
    public decimal InputPerMillion { get; set; }

    /// <summary>Output price per million tokens.</summary>
    [JsonPropertyName("output_per_m")]
    public decimal OutputPerMillion { get; set; }
}

/// <summary>
/// Gain task/category payload.
/// </summary>
public sealed class DashboardGainTaskPayload
{
    /// <summary>Task or command category.</summary>
    [JsonPropertyName("category")]
    public required string Category { get; set; }

    /// <summary>Tokens saved.</summary>
    [JsonPropertyName("tokens_saved")]
    public long TokensSaved { get; set; }

    /// <summary>Estimated tool spend saved.</summary>
    [JsonPropertyName("tool_spend_usd")]
    public decimal ToolSpendUsd { get; set; }
}

/// <summary>
/// Project memory payload for dashboard and admin workflows.
/// </summary>
public sealed class ProjectMemoryResponse
{
    /// <summary>
    /// Project identifier.
    /// </summary>
    [JsonPropertyName("project_id")]
    public required string ProjectId { get; set; }

    /// <summary>
    /// Project display name.
    /// </summary>
    [JsonPropertyName("project_name")]
    public required string ProjectName { get; set; }

    /// <summary>
    /// Source file count from project metadata.
    /// </summary>
    [JsonPropertyName("source_file_count")]
    public int SourceFileCount { get; set; }

    /// <summary>
    /// Total file count from project metadata.
    /// </summary>
    [JsonPropertyName("total_file_count")]
    public int TotalFileCount { get; set; }

    /// <summary>
    /// Creation timestamp for the project record.
    /// </summary>
    [JsonPropertyName("project_created_at")]
    public DateTimeOffset ProjectCreatedAt { get; set; }

    /// <summary>
    /// Marker flags that help operators identify cleanup candidates quickly.
    /// </summary>
    [JsonPropertyName("flags")]
    public ProjectMemoryFlagsResponse Flags { get; set; } = new();

    /// <summary>
    /// Persisted knowledge facts for the project.
    /// </summary>
    [JsonPropertyName("knowledge")]
    public required IReadOnlyList<ProjectKnowledgeFactResponse> Knowledge { get; set; }

    /// <summary>
    /// Persisted brain entries for the project.
    /// </summary>
    [JsonPropertyName("brain")]
    public required IReadOnlyList<ProjectBrainEntryResponse> Brain { get; set; }

    /// <summary>
    /// Lifecycle summary for the project's canonical memory.
    /// </summary>
    [JsonPropertyName("health")]
    public ProjectMemoryHealthResponse? Health { get; set; }

    /// <summary>
    /// Latest triage preview for the project's canonical memory.
    /// </summary>
    [JsonPropertyName("triage")]
    public ProjectMemoryTriageResponse? Triage { get; set; }

    /// <summary>
    /// Current bounded wake-up composition for the project.
    /// </summary>
    [JsonPropertyName("wakeup")]
    public IReadOnlyList<ProjectMemoryWakeupEntryResponse> Wakeup { get; set; } = [];

    /// <summary>
    /// Bounded durable memory candidate review queue for the project.
    /// </summary>
    [JsonPropertyName("candidates")]
    public IReadOnlyList<ProjectMemoryCandidateResponse> Candidates { get; set; } = [];

    /// <summary>
    /// Candidate and promotion summary for the project.
    /// </summary>
    [JsonPropertyName("candidate_summary")]
    public ProjectMemoryCandidateSummaryResponse? CandidateSummary { get; set; }
}

/// <summary>
/// Dashboard view model for a persisted durable memory candidate.
/// </summary>
public sealed class ProjectMemoryCandidateResponse
{
    /// <summary>Candidate category.</summary>
    [JsonPropertyName("category")]
    public string Category { get; set; } = string.Empty;

    /// <summary>Candidate key.</summary>
    [JsonPropertyName("key")]
    public string Key { get; set; } = string.Empty;

    /// <summary>Candidate value.</summary>
    [JsonPropertyName("value")]
    public string Value { get; set; } = string.Empty;

    /// <summary>Candidate confidence score.</summary>
    [JsonPropertyName("confidence")]
    public float Confidence { get; set; }

    /// <summary>Candidate review or promotion status.</summary>
    [JsonPropertyName("review_status")]
    public string ReviewStatus { get; set; } = string.Empty;

    /// <summary>Supporting evidence for the candidate.</summary>
    [JsonPropertyName("evidence")]
    public string Evidence { get; set; } = string.Empty;

    /// <summary>Replay-safe identity.</summary>
    [JsonPropertyName("promotion_identity")]
    public string PromotionIdentity { get; set; } = string.Empty;

    /// <summary>Stable logical key.</summary>
    [JsonPropertyName("logical_key")]
    public string LogicalKey { get; set; } = string.Empty;

    /// <summary>Candidate source type.</summary>
    [JsonPropertyName("source_type")]
    public string SourceType { get; set; } = string.Empty;

    /// <summary>Candidate source scope.</summary>
    [JsonPropertyName("source_scope")]
    public string SourceScope { get; set; } = string.Empty;

    /// <summary>Candidate creation time.</summary>
    [JsonPropertyName("created_at")]
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Candidate update time.</summary>
    [JsonPropertyName("updated_at")]
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>Review time when applicable.</summary>
    [JsonPropertyName("reviewed_at")]
    public DateTimeOffset? ReviewedAt { get; set; }

    /// <summary>Promoted knowledge key when candidate became canonical.</summary>
    [JsonPropertyName("promoted_knowledge_key")]
    public string PromotedKnowledgeKey { get; set; } = string.Empty;
}

/// <summary>
/// Dashboard summary for durable candidate and promotion outcomes.
/// </summary>
public sealed class ProjectMemoryCandidateSummaryResponse
{
    /// <summary>Total candidate count returned or stored for the project.</summary>
    [JsonPropertyName("total")]
    public int Total { get; set; }

    /// <summary>Queued review candidate count.</summary>
    [JsonPropertyName("pending_review")]
    public int PendingReview { get; set; }

    /// <summary>Auto-promoted candidate count.</summary>
    [JsonPropertyName("auto_promoted")]
    public int AutoPromoted { get; set; }

    /// <summary>Accepted candidate count.</summary>
    [JsonPropertyName("accepted")]
    public int Accepted { get; set; }

    /// <summary>Rejected candidate count.</summary>
    [JsonPropertyName("rejected")]
    public int Rejected { get; set; }
}

/// <summary>
/// Dashboard view model for a single persisted knowledge fact.
/// </summary>
public sealed class ProjectKnowledgeFactResponse
{
    /// <summary>
    /// Fact category.
    /// </summary>
    [JsonPropertyName("category")]
    public required string Category { get; set; }

    /// <summary>
    /// Fact key.
    /// </summary>
    [JsonPropertyName("key")]
    public required string Key { get; set; }

    /// <summary>
    /// Fact value.
    /// </summary>
    [JsonPropertyName("value")]
    public required string Value { get; set; }

    /// <summary>
    /// Confidence score.
    /// </summary>
    [JsonPropertyName("confidence")]
    public float Confidence { get; set; }

    /// <summary>
    /// Last update time.
    /// </summary>
    [JsonPropertyName("updated_at")]
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>
    /// When the fact was first created.
    /// </summary>
    [JsonPropertyName("created_at")]
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// Stable logical key used for deterministic identity.
    /// </summary>
    [JsonPropertyName("logical_key")]
    public string LogicalKey { get; set; } = string.Empty;

    /// <summary>
    /// Stable deterministic promotion identity.
    /// </summary>
    [JsonPropertyName("promotion_identity")]
    public string PromotionIdentity { get; set; } = string.Empty;

    /// <summary>
    /// Source type that produced the fact.
    /// </summary>
    [JsonPropertyName("source_type")]
    public string SourceType { get; set; } = string.Empty;

    /// <summary>
    /// Source scope for deterministic replay identity.
    /// </summary>
    [JsonPropertyName("source_scope")]
    public string SourceScope { get; set; } = string.Empty;

    /// <summary>
    /// Lifecycle status for the canonical fact.
    /// </summary>
    [JsonPropertyName("lifecycle_status")]
    public string LifecycleStatus { get; set; } = string.Empty;

    /// <summary>
    /// Lifecycle score used for ranking and wake-up selection.
    /// </summary>
    [JsonPropertyName("lifecycle_score")]
    public float LifecycleScore { get; set; }

    /// <summary>
    /// Number of explicit confirmations retained for the fact.
    /// </summary>
    [JsonPropertyName("confirmation_count")]
    public int ConfirmationCount { get; set; }

    /// <summary>
    /// When the fact was last explicitly confirmed.
    /// </summary>
    [JsonPropertyName("last_confirmed_at")]
    public DateTimeOffset? LastConfirmedAt { get; set; }

    /// <summary>
    /// Number of times the fact has been retrieved.
    /// </summary>
    [JsonPropertyName("retrieval_count")]
    public int RetrievalCount { get; set; }

    /// <summary>
    /// When the fact was last retrieved.
    /// </summary>
    [JsonPropertyName("last_retrieved_at")]
    public DateTimeOffset? LastRetrievedAt { get; set; }

    /// <summary>
    /// Historical revisions retained after canonical updates.
    /// </summary>
    [JsonPropertyName("history")]
    public IReadOnlyList<ProjectKnowledgeHistoryResponse> History { get; set; } = [];
}

/// <summary>
/// Lifecycle summary for a project's canonical memory.
/// </summary>
public sealed class ProjectMemoryHealthResponse
{
    /// <summary>Total persisted knowledge entries.</summary>
    [JsonPropertyName("total_facts")]
    public int TotalFacts { get; set; }

    /// <summary>Current canonical facts that remain active.</summary>
    [JsonPropertyName("current_facts")]
    public int CurrentFacts { get; set; }

    /// <summary>Facts marked stale, superseded, or otherwise non-current.</summary>
    [JsonPropertyName("non_current_facts")]
    public int NonCurrentFacts { get; set; }

    /// <summary>Historical revisions retained across facts.</summary>
    [JsonPropertyName("history_entries")]
    public int HistoryEntries { get; set; }

    /// <summary>Average lifecycle score across current facts.</summary>
    [JsonPropertyName("average_lifecycle_score")]
    public float AverageLifecycleScore { get; set; }

    /// <summary>The latest maintenance-related update time.</summary>
    [JsonPropertyName("last_maintenance_at")]
    public DateTimeOffset? LastMaintenanceAt { get; set; }

    /// <summary>Memory density score derived from current facts and retained history.</summary>
    [JsonPropertyName("density_score")]
    public float DensityScore { get; set; }

    /// <summary>Maintenance summary string for operator display.</summary>
    [JsonPropertyName("maintenance_summary")]
    public string MaintenanceSummary { get; set; } = string.Empty;
}

/// <summary>
/// Dashboard view model for a historical knowledge revision.
/// </summary>
public sealed class ProjectKnowledgeHistoryResponse
{
    /// <summary>Historical fact value.</summary>
    [JsonPropertyName("value")]
    public string Value { get; set; } = string.Empty;

    /// <summary>Confidence when the revision was current.</summary>
    [JsonPropertyName("confidence")]
    public float Confidence { get; set; }

    /// <summary>Promotion identity for the historical revision.</summary>
    [JsonPropertyName("promotion_identity")]
    public string PromotionIdentity { get; set; } = string.Empty;

    /// <summary>Source type for the historical revision.</summary>
    [JsonPropertyName("source_type")]
    public string SourceType { get; set; } = string.Empty;

    /// <summary>Source scope for the historical revision.</summary>
    [JsonPropertyName("source_scope")]
    public string SourceScope { get; set; } = string.Empty;

    /// <summary>When the revision became current.</summary>
    [JsonPropertyName("valid_from")]
    public DateTimeOffset? ValidFrom { get; set; }

    /// <summary>When the revision stopped being current.</summary>
    [JsonPropertyName("superseded_at")]
    public DateTimeOffset SupersededAt { get; set; }
}

/// <summary>
/// Dashboard view model for hosted memory triage summaries.
/// </summary>
public sealed class ProjectMemoryTriageResponse
{
    /// <summary>Triage execution mode.</summary>
    [JsonPropertyName("mode")]
    public string Mode { get; set; } = "preview";

    /// <summary>Duplicate groups proposed by triage.</summary>
    [JsonPropertyName("duplicate_groups")]
    public IReadOnlyList<object> DuplicateGroups { get; set; } = [];

    /// <summary>Stale cleanup candidates.</summary>
    [JsonPropertyName("stale_candidates")]
    public IReadOnlyList<object> StaleCandidates { get; set; } = [];

    /// <summary>Likely junk or demo cleanup candidates.</summary>
    [JsonPropertyName("junk_candidates")]
    public IReadOnlyList<object> JunkCandidates { get; set; } = [];

    /// <summary>Applied triage actions, when triage ran in apply mode.</summary>
    [JsonPropertyName("applied_actions")]
    public IReadOnlyList<object> AppliedActions { get; set; } = [];
}

/// <summary>
/// Dashboard view model for a bounded wake-up entry.
/// </summary>
public sealed class ProjectMemoryWakeupEntryResponse
{
    /// <summary>Fact category.</summary>
    [JsonPropertyName("category")]
    public string Category { get; set; } = string.Empty;

    /// <summary>Fact key.</summary>
    [JsonPropertyName("key")]
    public string Key { get; set; } = string.Empty;

    /// <summary>Fact value.</summary>
    [JsonPropertyName("value")]
    public string Value { get; set; } = string.Empty;

    /// <summary>Lifecycle score used for wake-up ranking.</summary>
    [JsonPropertyName("lifecycle_score")]
    public float LifecycleScore { get; set; }
}

/// <summary>
/// Dashboard view model for a single persisted brain entry.
/// </summary>
public sealed class ProjectBrainEntryResponse
{
    /// <summary>
    /// Brain key.
    /// </summary>
    [JsonPropertyName("key")]
    public required string Key { get; set; }

    /// <summary>
    /// Derived brain entry type for dashboard filtering.
    /// </summary>
    [JsonPropertyName("entry_type")]
    public string EntryType { get; set; } = "other";

    /// <summary>
    /// Brain fact category.
    /// </summary>
    [JsonPropertyName("category")]
    public string Category { get; set; } = string.Empty;

    /// <summary>
    /// Lifecycle state.
    /// </summary>
    [JsonPropertyName("lifecycle_status")]
    public string LifecycleStatus { get; set; } = string.Empty;

    /// <summary>
    /// Provenance source type.
    /// </summary>
    [JsonPropertyName("source_type")]
    public string SourceType { get; set; } = string.Empty;

    /// <summary>
    /// Provenance scope.
    /// </summary>
    [JsonPropertyName("source_scope")]
    public string SourceScope { get; set; } = string.Empty;

    /// <summary>
    /// Stable logical key.
    /// </summary>
    [JsonPropertyName("logical_key")]
    public string LogicalKey { get; set; } = string.Empty;

    /// <summary>
    /// Stable promotion identity.
    /// </summary>
    [JsonPropertyName("promotion_identity")]
    public string PromotionIdentity { get; set; } = string.Empty;

    /// <summary>
    /// Confidence score.
    /// </summary>
    [JsonPropertyName("confidence")]
    public float Confidence { get; set; }

    /// <summary>
    /// Optional evidence text.
    /// </summary>
    [JsonPropertyName("evidence")]
    public string Evidence { get; set; } = string.Empty;

    /// <summary>
    /// Brain value.
    /// </summary>
    [JsonPropertyName("value")]
    public required string Value { get; set; }

    /// <summary>
    /// Creation time.
    /// </summary>
    [JsonPropertyName("created_at")]
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// Last update time.
    /// </summary>
    [JsonPropertyName("updated_at")]
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>
/// Operator flags for project memory cleanup workflows.
/// </summary>
public sealed class ProjectMemoryFlagsResponse
{
    /// <summary>True when project contains no knowledge and no brain entries.</summary>
    [JsonPropertyName("is_empty")]
    public bool IsEmpty { get; set; }

    /// <summary>True when slug looks like a test, demo, temp, or scratch project.</summary>
    [JsonPropertyName("is_test_project")]
    public bool IsTestProject { get; set; }

    /// <summary>True when duplicate slug was detected by project diagnostics.</summary>
    [JsonPropertyName("has_duplicate_slug")]
    public bool HasDuplicateSlug { get; set; }

    /// <summary>True when duplicate fingerprint was detected by project diagnostics.</summary>
    [JsonPropertyName("has_duplicate_fingerprint")]
    public bool HasDuplicateFingerprint { get; set; }
}
