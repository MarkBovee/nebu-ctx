namespace NebuCtx.Application;

using NebuCtx.Contracts.Mcp;

/// <summary>
/// Abstraction for an executable MCP tool handler.
/// Each tool implements this interface with its own execution logic.
/// </summary>
public interface IToolHandler
{
    /// <summary>
    /// The tool's unique name as it appears in the manifest and tool call requests.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Human-readable description of the tool's purpose.
    /// </summary>
    string Description { get; }

    /// <summary>
    /// JSON Schema describing the tool's input parameters.
    /// </summary>
    Dictionary<string, object?> InputSchema { get; }

    /// <summary>
    /// Executes the tool with the provided arguments.
    /// </summary>
    /// <param name="arguments">Tool-specific arguments from the client.</param>
    /// <param name="context">Execution context with project and session info.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The tool result as an object to be serialized to JSON.</returns>
    Task<object> ExecuteAsync(Dictionary<string, object?> arguments, ToolExecutionContext context, CancellationToken cancellationToken = default);
}

/// <summary>
/// Context provided to tool handlers during execution.
/// Contains project identity and session information without exposing storage details.
/// </summary>
public sealed class ToolExecutionContext
{
    /// <summary>
    /// The resolved project identifier for this request.
    /// </summary>
    public required string ProjectId { get; init; }

    /// <summary>
    /// Current working directory from the client, if provided (execution context only).
    /// </summary>
    public string? Cwd { get; init; }

    /// <summary>
    /// Project root path from the client, if provided (execution context only).
    /// </summary>
    public string? ProjectRoot { get; init; }

    /// <summary>
    /// Actor label for the calling client or user when available.
    /// </summary>
    public string? ActorLabel { get; init; }
}
