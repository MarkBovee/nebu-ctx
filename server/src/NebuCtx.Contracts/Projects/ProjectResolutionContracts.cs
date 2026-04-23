namespace NebuCtx.Contracts.Projects;

using System.Text.Json.Serialization;

/// <summary>
/// Request payload for resolving or creating a canonical project identity.
/// </summary>
public sealed class ProjectResolutionRequest
{
    /// <summary>
    /// Repository fingerprint used for project matching.
    /// </summary>
    [JsonPropertyName("fingerprint")]
    public required RepositoryFingerprint Fingerprint { get; set; }

    /// <summary>
    /// Suggested slug used when a new project must be created.
    /// </summary>
    [JsonPropertyName("suggested_slug")]
    public string? SuggestedSlug { get; set; }

    /// <summary>
    /// Optional workspace binding that should be persisted after resolution.
    /// </summary>
    [JsonPropertyName("workspace_binding")]
    public WorkspaceBinding? WorkspaceBinding { get; set; }

    /// <summary>
    /// Optional compact client-side project metadata for future hybrid sync flows.
    /// </summary>
    [JsonPropertyName("project_metadata")]
    public ProjectMetadataEnvelope? ProjectMetadata { get; set; }
}

/// <summary>
/// Response payload for project resolution requests.
/// </summary>
public sealed class ProjectResolutionResponse
{
    /// <summary>
    /// The resolved canonical project.
    /// </summary>
    [JsonPropertyName("project")]
    public required ProjectRecord Project { get; set; }

    /// <summary>
    /// Indicates whether the request also persisted a workspace binding.
    /// </summary>
    [JsonPropertyName("workspace_bound")]
    public bool WorkspaceBound { get; set; }
}