namespace NebuCtx.Contracts.Mcp;

using System.Text.Json.Serialization;

/// <summary>
/// Filter and sort parameters for memory listing endpoints.
/// Used by <c>ctx_brain list</c>, <c>ctx_knowledge list</c>, and <c>ctx memory list</c>.
/// </summary>
public sealed class MemoryListFilter
{
    /// <summary>Optional category filter (knowledge category or brain category/kind).</summary>
    [JsonPropertyName("category")]
    public string? Category { get; set; }

    /// <summary>Optional source type filter (e.g. "brain_ingest", "tool_activity", "remember").</summary>
    [JsonPropertyName("source_type")]
    public string? SourceType { get; set; }

    /// <summary>Optional lifecycle status filter (current, stale, superseded, archived, legacy).</summary>
    [JsonPropertyName("lifecycle_status")]
    public string? LifecycleStatus { get; set; }

    /// <summary>Optional earliest creation timestamp (inclusive).</summary>
    [JsonPropertyName("created_after")]
    public DateTimeOffset? CreatedAfter { get; set; }

    /// <summary>Optional most recent creation timestamp (inclusive).</summary>
    [JsonPropertyName("created_before")]
    public DateTimeOffset? CreatedBefore { get; set; }

    /// <summary>Sort field. One of: relevance (default), created, updated, confidence, retrieval_count.</summary>
    [JsonPropertyName("sort_field")]
    public string SortField { get; set; } = "relevance";

    /// <summary>Sort direction: asc or desc. Defaults to desc.</summary>
    [JsonPropertyName("sort_direction")]
    public string SortDirection { get; set; } = "desc";

    /// <summary>Maximum number of entries to return. Defaults to 20.</summary>
    [JsonPropertyName("limit")]
    public int Limit { get; set; } = 20;

    /// <summary>Number of entries to skip for pagination. Defaults to 0.</summary>
    [JsonPropertyName("offset")]
    public int Offset { get; set; } = 0;

    /// <summary>Optional source session id filter for promoted knowledge facts (memory-correlation).</summary>
    [JsonPropertyName("promoted_from_session")]
    public string? PromotedFromSession { get; set; }

    /// <summary>Optional source brain key filter for promoted knowledge facts (memory-correlation).</summary>
    [JsonPropertyName("promoted_from_brain_key")]
    public string? PromotedFromBrainKey { get; set; }

    /// <summary>Maximum allowed limit to keep response sizes predictable.</summary>
    public const int MaxLimit = 200;
}

/// <summary>
/// Consistent memory listing response envelope used by <c>list</c> actions on brain/knowledge tools.
/// </summary>
public sealed class MemoryListResult<TMemory>
{
    /// <summary>Memory entries that matched the filter and sort criteria, after limit/offset.</summary>
    [JsonPropertyName("memories")]
    public required IReadOnlyList<TMemory> Memories { get; init; }

    /// <summary>Number of entries returned in this page.</summary>
    [JsonPropertyName("count")]
    public int Count => Memories.Count;

    /// <summary>Total number of entries that matched the filters before limit/offset.</summary>
    [JsonPropertyName("total")]
    public int Total { get; init; }

    /// <summary>Echo of the active filters for client confirmation.</summary>
    [JsonPropertyName("filters_applied")]
    public required Dictionary<string, object?> FiltersApplied { get; init; }

    /// <summary>Echo of the active sort criteria.</summary>
    [JsonPropertyName("sort_applied")]
    public required Dictionary<string, object?> SortApplied { get; init; }
}

/// <summary>
/// Single memory entry projection used in listing responses. Shape matches the
/// spec requirement in <c>memory-browsing</c>: key, value, category, confidence,
/// source_type, source_scope, created_at, updated_at, retrieval_count,
/// confirmation_count, lifecycle_score, lifecycle_status.
/// </summary>
public sealed class MemoryListItem
{
    /// <summary>Memory key (brain) or category:key (knowledge).</summary>
    [JsonPropertyName("key")]
    public required string Key { get; init; }

    /// <summary>For knowledge entries, the category the fact belongs to.</summary>
    [JsonPropertyName("category")]
    public string? Category { get; init; }

    /// <summary>Memory value text (truncated to <see cref="MaxValueLength"/> in list view).</summary>
    [JsonPropertyName("value")]
    public required string Value { get; init; }

    /// <summary>Confidence score 0..1.</summary>
    [JsonPropertyName("confidence")]
    public float Confidence { get; init; }

    /// <summary>Source type that produced the memory.</summary>
    [JsonPropertyName("source_type")]
    public string SourceType { get; init; } = string.Empty;

    /// <summary>Source scope (project id, session id, etc.).</summary>
    [JsonPropertyName("source_scope")]
    public string SourceScope { get; init; } = string.Empty;

    /// <summary>When the memory was first created.</summary>
    [JsonPropertyName("created_at")]
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>When the memory was last updated.</summary>
    [JsonPropertyName("updated_at")]
    public DateTimeOffset UpdatedAt { get; init; }

    /// <summary>Times the memory has been retrieved via recall.</summary>
    [JsonPropertyName("retrieval_count")]
    public int RetrievalCount { get; init; }

    /// <summary>Times the memory has been explicitly confirmed.</summary>
    [JsonPropertyName("confirmation_count")]
    public int ConfirmationCount { get; init; }

    /// <summary>Composite lifecycle ranking score.</summary>
    [JsonPropertyName("lifecycle_score")]
    public float LifecycleScore { get; init; }

    /// <summary>Current lifecycle status.</summary>
    [JsonPropertyName("lifecycle_status")]
    public string LifecycleStatus { get; init; } = "current";

    /// <summary>Optional promotion trace when the entry was promoted from a brain event.</summary>
    [JsonPropertyName("promotion_trace")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public PromotionTrace? PromotionTrace { get; init; }

    /// <summary>Maximum length of the value text shown in list responses.</summary>
    public const int MaxValueLength = 240;
}
