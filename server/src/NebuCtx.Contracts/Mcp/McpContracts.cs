namespace NebuCtx.Contracts.Mcp;

using NebuCtx.Contracts.Projects;
using System.Text.Json.Serialization;

/// <summary>
/// Request payload for POST /v1/tools/call.
/// </summary>
public sealed class ToolCallRequest
{
    private CheckoutBinding? _checkoutBinding;

    /// <summary>
    /// The name of the tool to execute.
    /// </summary>
    [JsonPropertyName("name")]
    public required string Name { get; set; }

    /// <summary>
    /// Tool-specific arguments as a key-value map.
    /// </summary>
    [JsonPropertyName("arguments")]
    public Dictionary<string, object?> Arguments { get; set; } = [];

    /// <summary>
    /// Explicit project identifier for the request when the client already has a binding.
    /// </summary>
    [JsonPropertyName("project_id")]
    public string? ProjectId { get; set; }

    /// <summary>
    /// Suggested project slug when the server needs to create or resolve a project.
    /// </summary>
    [JsonPropertyName("project_slug")]
    public string? ProjectSlug { get; set; }

    /// <summary>
    /// Repository fingerprint used to resolve the canonical project identity.
    /// </summary>
    [JsonPropertyName("repository_fingerprint")]
    public RepositoryFingerprint? RepositoryFingerprint { get; set; }

    /// <summary>
    /// Non-canonical local checkout binding metadata from the client.
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
/// Successful response from POST /v1/tools/call.
/// </summary>
public sealed class ToolCallResponse
{
    /// <summary>
    /// The result payload from the tool execution.
    /// </summary>
    [JsonPropertyName("result")]
    public required object Result { get; set; }
}

/// <summary>
/// Error response from POST /v1/tools/call.
/// </summary>
public sealed class ToolCallErrorResponse
{
    /// <summary>
    /// Human-readable error description.
    /// </summary>
    [JsonPropertyName("error")]
    public required string Error { get; set; }
}

/// <summary>
/// A single tool definition in the manifest.
/// </summary>
public sealed class ToolDefinition
{
    /// <summary>
    /// Unique tool name used in tool call requests.
    /// </summary>
    [JsonPropertyName("name")]
    public required string Name { get; set; }

    /// <summary>
    /// Human-readable description of what the tool does.
    /// </summary>
    [JsonPropertyName("description")]
    public required string Description { get; set; }

    /// <summary>
    /// JSON Schema describing the tool's input parameters.
    /// </summary>
    [JsonPropertyName("inputSchema")]
    public required Dictionary<string, object?> InputSchema { get; set; }
}

/// <summary>
/// Response for GET /v1/tools (paginated tool list).
/// </summary>
public sealed class ToolListResponse
{
    /// <summary>
    /// The list of available tools.
    /// </summary>
    [JsonPropertyName("tools")]
    public required List<ToolDefinition> Tools { get; set; }

    /// <summary>
    /// Total number of tools available.
    /// </summary>
    [JsonPropertyName("total")]
    public int Total { get; set; }
}

/// <summary>
/// Response for GET /v1/manifest.
/// </summary>
public sealed class ManifestResponse
{
    /// <summary>
    /// Server name identifier.
    /// </summary>
    [JsonPropertyName("name")]
    public required string Name { get; set; }

    /// <summary>
    /// Server version string.
    /// </summary>
    [JsonPropertyName("version")]
    public required string Version { get; set; }

    /// <summary>
    /// Full list of tool definitions with JSON schemas.
    /// </summary>
    [JsonPropertyName("tools")]
    public required List<ToolDefinition> Tools { get; set; }
}

/// <summary>
/// Request payload for POST /v1/telemetry/ingest.
/// Carries a single tool-call event from the Rust client for server-side dashboard aggregation.
/// No raw file content or shell output is included — only counts and metadata.
/// </summary>
public sealed class TelemetryIngestRequest
{
    /// <summary>Tool or command name (e.g. "ctx_read").</summary>
    [JsonPropertyName("tool_name")]
    public required string ToolName { get; set; }

    /// <summary>Estimated original token count before compression.</summary>
    [JsonPropertyName("tokens_original")]
    public long TokensOriginal { get; set; }

    /// <summary>Estimated tokens saved by compression.</summary>
    [JsonPropertyName("tokens_saved")]
    public long TokensSaved { get; set; }

    /// <summary>Wall-clock execution time in milliseconds.</summary>
    [JsonPropertyName("duration_ms")]
    public long DurationMs { get; set; }

    /// <summary>Compression mode used (e.g. "signatures", "full", "map").</summary>
    [JsonPropertyName("mode")]
    public string? Mode { get; set; }

    /// <summary>Repository fingerprint from the client for project resolution.</summary>
    [JsonPropertyName("repository_fingerprint")]
    public RepositoryFingerprint? RepositoryFingerprint { get; set; }

    /// <summary>Checkout binding (branch, commit, local root) from the client.</summary>
    [JsonPropertyName("checkout_binding")]
    public CheckoutBinding? CheckoutBinding { get; set; }

    /// <summary>Suggested project slug for new project auto-creation.</summary>
    [JsonPropertyName("project_slug")]
    public string? ProjectSlug { get; set; }
}
