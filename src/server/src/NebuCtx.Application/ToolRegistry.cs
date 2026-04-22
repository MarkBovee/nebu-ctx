namespace NebuCtx.Application;

using System.Collections.Frozen;
using NebuCtx.Contracts.Mcp;
using Microsoft.Extensions.Logging;

/// <summary>
/// Registry and dispatcher for MCP tool handlers.
/// Tools register at startup and are dispatched by name during tool call execution.
/// Generates the manifest and tool list from registered handlers.
/// </summary>
public sealed class ToolRegistry
{
    private readonly FrozenDictionary<string, IToolHandler> _handlers;
    private readonly ILogger<ToolRegistry> _logger;
    private readonly TelemetryStore _telemetryStore;

    /// <summary>
    /// Initializes the tool registry with all registered handlers.
    /// </summary>
    /// <param name="handlers">Enumerable of all tool handler instances.</param>
    /// <param name="logger">Logger for tool dispatch events.</param>
    /// <param name="telemetryStore">Telemetry store for dashboard statistics.</param>
    public ToolRegistry(IEnumerable<IToolHandler> handlers, ILogger<ToolRegistry> logger, TelemetryStore telemetryStore)
    {
        _handlers = handlers.ToFrozenDictionary(h => h.Name, h => h, StringComparer.OrdinalIgnoreCase);
        _logger = logger;
        _telemetryStore = telemetryStore;
    }

    /// <summary>
    /// Executes a tool by name with the provided arguments and context.
    /// </summary>
    /// <param name="toolName">Name of the tool to execute.</param>
    /// <param name="arguments">Tool-specific arguments.</param>
    /// <param name="context">Execution context with project and session info.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The tool result object.</returns>
    /// <exception cref="KeyNotFoundException">Thrown when the tool name is not registered.</exception>
    public async Task<object> ExecuteToolAsync(string toolName, Dictionary<string, object?> arguments, ToolExecutionContext context, CancellationToken cancellationToken = default)
    {
        if (!_handlers.TryGetValue(toolName, out var handler))
        {
            _logger.LogWarning("Unknown tool requested: {ToolName}", toolName);
            throw new KeyNotFoundException($"Tool '{toolName}' is not registered.");
        }

        _logger.LogInformation("Executing tool {ToolName} for project {ProjectId} by {ActorLabel}", toolName, context.ProjectId, context.ActorLabel ?? "anonymous");
        var result = await handler.ExecuteAsync(arguments, context, cancellationToken);
        _telemetryStore.RecordToolCall(toolName, arguments, result, context);
        return result;
    }

    /// <summary>
    /// Gets the complete tool manifest for the /v1/manifest endpoint.
    /// </summary>
    /// <returns>Manifest response containing all registered tools.</returns>
    public ManifestResponse GetManifest()
    {
        return new ManifestResponse
        {
            Name = "nebu-ctx",
            Version = ServerVersion.Current,
            Tools = GetToolDefinitions(),
        };
    }

    /// <summary>
    /// Gets paginated tool definitions for the /v1/tools endpoint.
    /// </summary>
    /// <param name="offset">Number of tools to skip.</param>
    /// <param name="limit">Maximum number of tools to return.</param>
    /// <returns>Paginated tool list response.</returns>
    public ToolListResponse GetTools(int offset = 0, int limit = 200)
    {
        var allTools = GetToolDefinitions();
        var paged = allTools.Skip(offset).Take(limit).ToList();

        return new ToolListResponse
        {
            Tools = paged,
            Total = allTools.Count,
        };
    }

    /// <summary>
    /// Builds the full list of tool definitions from registered handlers.
    /// </summary>
    private List<ToolDefinition> GetToolDefinitions()
    {
        return _handlers.Values
            .Select(h => new ToolDefinition
            {
                Name = h.Name,
                Description = h.Description,
                InputSchema = h.InputSchema,
            })
            .OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}

/// <summary>
/// Static accessor for the server version.
/// </summary>
public static class ServerVersion
{
    /// <summary>
    /// Current server version string, matching the Cargo.toml version.
    /// </summary>
    public const string Current = "0.2.6";
}
