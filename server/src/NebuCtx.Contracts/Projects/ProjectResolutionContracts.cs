namespace NebuCtx.Contracts.Projects;

using System.Text.Json.Serialization;

/// <summary>
/// Request payload for resolving or creating a canonical project identity.
/// </summary>
public sealed class ProjectResolutionRequest
{
    private CheckoutBinding? _checkoutBinding;

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
    /// Optional checkout binding that should be persisted after resolution.
    /// </summary>
    [JsonPropertyName("checkout_binding")]
    public CheckoutBinding? CheckoutBinding
    {
        get => _checkoutBinding;
        set => _checkoutBinding = value;
    }

    /// <summary>
    /// Legacy workspace binding alias kept for older clients.
    /// </summary>
    [JsonPropertyName("workspace_binding")]
    public CheckoutBinding? WorkspaceBinding
    {
        get => _checkoutBinding;
        set => _checkoutBinding = value;
    }

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
    private bool _checkoutBound;

    /// <summary>
    /// The resolved canonical project.
    /// </summary>
    [JsonPropertyName("project")]
    public required ProjectRecord Project { get; set; }

    /// <summary>
    /// Indicates whether the request also persisted a checkout binding.
    /// </summary>
    [JsonPropertyName("checkout_bound")]
    public bool CheckoutBound
    {
        get => _checkoutBound;
        set => _checkoutBound = value;
    }

    /// <summary>
    /// Legacy workspace-bound alias kept for older clients.
    /// Not emitted in responses — use checkout_bound.
    /// </summary>
    [JsonIgnore]
    public bool WorkspaceBound
    {
        get => _checkoutBound;
        set => _checkoutBound = value;
    }
}