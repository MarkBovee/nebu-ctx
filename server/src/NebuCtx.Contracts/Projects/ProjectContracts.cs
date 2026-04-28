namespace NebuCtx.Contracts.Projects;

using System.Text.Json.Serialization;

/// <summary>
/// Represents a canonical project identity on the server.
/// Projects are the primary unit of persistent state ownership.
/// </summary>
public sealed class ProjectRecord
{
    /// <summary>
    /// Server-generated stable identifier (primary key for all project-scoped data).
    /// </summary>
    [JsonPropertyName("project_id")]
    public required string ProjectId { get; set; }

    /// <summary>
    /// Human-readable project name used in CLI and dashboard.
    /// </summary>
    [JsonPropertyName("slug")]
    public required string Slug { get; set; }

    /// <summary>
    /// Repository fingerprint used for auto-matching across machines and checkouts.
    /// </summary>
    [JsonPropertyName("fingerprint")]
    public RepositoryFingerprint? Fingerprint { get; set; }

    /// <summary>
    /// Timestamp when the project was first registered.
    /// </summary>
    [JsonPropertyName("created_at")]
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// Timestamp of the last state update for this project.
    /// </summary>
    [JsonPropertyName("updated_at")]
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>
    /// Most recent compact project metadata snapshot sent by a client.
    /// </summary>
    [JsonPropertyName("project_metadata")]
    public ProjectMetadataEnvelope? ProjectMetadata { get; set; }
}

/// <summary>
/// Non-canonical metadata for matching a local checkout to a server-side project.
/// </summary>
public sealed class RepositoryFingerprint
{
    /// <summary>
    /// Canonical git remote URL (e.g. origin), normalized for matching.
    /// </summary>
    [JsonPropertyName("remote_url")]
    public string? RemoteUrl { get; set; }

    /// <summary>
    /// Repository host/provider name (e.g. "github.com").
    /// </summary>
    [JsonPropertyName("host")]
    public string? Host { get; set; }

    /// <summary>
    /// Repository owner/organization (e.g. "MarkBovee").
    /// </summary>
    [JsonPropertyName("owner")]
    public string? Owner { get; set; }

    /// <summary>
    /// Repository name (e.g. "nebu-ctx").
    /// </summary>
    [JsonPropertyName("repo_name")]
    public string? RepoName { get; set; }

    /// <summary>
    /// Default branch name (e.g. "main").
    /// </summary>
    [JsonPropertyName("default_branch")]
    public string? DefaultBranch { get; set; }
}

/// <summary>
/// A checkout binding associates a local checkout with a server-side project.
/// This is diagnostic/alias data, not the identity source of truth.
/// </summary>
public sealed class CheckoutBinding
{
    /// <summary>
    /// The project this binding resolves to.
    /// </summary>
    [JsonPropertyName("project_id")]
    public required string ProjectId { get; set; }

    /// <summary>
    /// Local root path on the client machine (diagnostic only).
    /// </summary>
    [JsonPropertyName("local_root")]
    public string? LocalRoot { get; set; }

    /// <summary>
    /// Current branch name on the local checkout.
    /// </summary>
    [JsonPropertyName("branch")]
    public string? Branch { get; set; }

    /// <summary>
    /// Last seen commit hash from the local checkout.
    /// </summary>
    [JsonPropertyName("last_commit")]
    public string? LastCommit { get; set; }

    /// <summary>
    /// Client machine or environment label.
    /// </summary>
    [JsonPropertyName("client_label")]
    public string? ClientLabel { get; set; }

    /// <summary>
    /// When this binding was last synced.
    /// </summary>
    [JsonPropertyName("last_sync")]
    public DateTimeOffset? LastSync { get; set; }
}
