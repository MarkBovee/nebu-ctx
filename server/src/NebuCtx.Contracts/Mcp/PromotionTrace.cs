namespace NebuCtx.Contracts.Mcp;

using System.Text.Json.Serialization;

/// <summary>
/// Provenance payload describing where a canonical knowledge fact
/// originated. Populated when a brain session event is promoted into
/// knowledge; null when a fact was added directly via remember.
/// </summary>
public sealed class PromotionTrace
{
    /// <summary>ID of the brain session where the event originated.</summary>
    [JsonPropertyName("source_session_id")]
    public string SourceSessionId { get; set; } = string.Empty;

    /// <summary>Key of the original brain memory entry.</summary>
    [JsonPropertyName("source_brain_key")]
    public string SourceBrainKey { get; set; } = string.Empty;

    /// <summary>Category of the original brain memory entry.</summary>
    [JsonPropertyName("source_brain_category")]
    public string SourceBrainCategory { get; set; } = string.Empty;

    /// <summary>Truncated value of the original brain memory entry.</summary>
    [JsonPropertyName("source_brain_value")]
    public string SourceBrainValue { get; set; } = string.Empty;

    /// <summary>ISO 8601 timestamp when the brain event was created.</summary>
    [JsonPropertyName("source_timestamp")]
    public DateTimeOffset? SourceTimestamp { get; set; }

    /// <summary>Action that promoted the fact (manual_promote, auto_promote, consolidation).</summary>
    [JsonPropertyName("promotion_action")]
    public string PromotionAction { get; set; } = "manual_promote";

    /// <summary>ISO 8601 timestamp when the fact was promoted to knowledge.</summary>
    [JsonPropertyName("promotion_timestamp")]
    public DateTimeOffset? PromotionTimestamp { get; set; }

    /// <summary>True when at least one trace field is populated.</summary>
    [JsonIgnore]
    public bool HasTrace =>
        !string.IsNullOrEmpty(SourceSessionId)
        || !string.IsNullOrEmpty(SourceBrainKey)
        || !string.IsNullOrEmpty(SourceBrainCategory)
        || SourceTimestamp.HasValue
        || PromotionTimestamp.HasValue;
}
