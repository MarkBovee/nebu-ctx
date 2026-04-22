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
