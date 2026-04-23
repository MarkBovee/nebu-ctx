namespace NebuCtx.Contracts.Projects;

using System.Text.Json.Serialization;

/// <summary>
/// Compact metadata envelope that can be synced to the server without transferring raw file contents.
/// </summary>
public sealed class ProjectMetadataEnvelope
{
    /// <summary>
    /// Schema version for the compact metadata payload.
    /// </summary>
    [JsonPropertyName("schema_version")]
    public int SchemaVersion { get; set; }

    /// <summary>
    /// Compact project summary for graph and search bootstrap flows.
    /// </summary>
    [JsonPropertyName("summary")]
    public required ProjectMetadataSummary Summary { get; set; }
}

/// <summary>
/// Compact project summary used by future hybrid metadata sync flows.
/// </summary>
public sealed class ProjectMetadataSummary
{
    /// <summary>
    /// Total file count seen during client-side traversal.
    /// </summary>
    [JsonPropertyName("total_file_count")]
    public long TotalFileCount { get; set; }

    /// <summary>
    /// Count of source files that contributed to the language summary.
    /// </summary>
    [JsonPropertyName("source_file_count")]
    public long SourceFileCount { get; set; }

    /// <summary>
    /// Root markers observed in the local project checkout.
    /// </summary>
    [JsonPropertyName("markers")]
    public List<string> Markers { get; set; } = [];

    /// <summary>
    /// Top language buckets inferred from the local project checkout.
    /// </summary>
    [JsonPropertyName("languages")]
    public List<ProjectLanguageStat> Languages { get; set; } = [];
}

/// <summary>
/// Per-language source counts inside the compact project summary.
/// </summary>
public sealed class ProjectLanguageStat
{
    /// <summary>
    /// Language bucket name.
    /// </summary>
    [JsonPropertyName("language")]
    public required string Language { get; set; }

    /// <summary>
    /// Number of files that contributed to this language bucket.
    /// </summary>
    [JsonPropertyName("file_count")]
    public long FileCount { get; set; }
}