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
    /// Persisted knowledge facts for the project.
    /// </summary>
    [JsonPropertyName("knowledge")]
    public required IReadOnlyList<ProjectKnowledgeFactResponse> Knowledge { get; set; }

    /// <summary>
    /// Persisted brain entries for the project.
    /// </summary>
    [JsonPropertyName("brain")]
    public required IReadOnlyList<ProjectBrainEntryResponse> Brain { get; set; }
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
    /// Brain value.
    /// </summary>
    [JsonPropertyName("value")]
    public required string Value { get; set; }

    /// <summary>
    /// Creation time.
    /// </summary>
    [JsonPropertyName("created_at")]
    public DateTimeOffset CreatedAt { get; set; }
}
