namespace NebuCtx.Server.Core;

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
    private static readonly string[] PublicToolNames = ["ctx", "ctx_read", "ctx_search", "ctx_shell", "ctx_tree"];
    private static readonly string[] MetadataOnlyPublicToolNames = ["ctx_read", "ctx_search", "ctx_shell", "ctx_tree"];
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
            if (MetadataOnlyPublicToolNames.Contains(toolName, StringComparer.OrdinalIgnoreCase))
            {
                _logger.LogWarning("Hosted HTTP call requested metadata-only public tool: {ToolName}", toolName);
                throw new ArgumentException($"Hosted HTTP endpoint advertises '{toolName}' for public contract metadata, but execution is handled by the Rust client/stdio MCP server. Use the nebu-ctx client for '{toolName}' calls.");
            }

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
            Tools = GetPublicToolDefinitions(),
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
        var allTools = GetPublicToolDefinitions();
        var paged = allTools.Skip(offset).Take(limit).ToList();

        return new ToolListResponse
        {
            Tools = paged,
            Total = allTools.Count,
        };
    }

    /// <summary>
    /// Gets paginated tool definitions for all registered internal handlers.
    /// Dashboard views use this to show the full server capability set.
    /// </summary>
    /// <param name="offset">Number of tools to skip.</param>
    /// <param name="limit">Maximum number of tools to return.</param>
    /// <returns>Paginated tool list response.</returns>
    public ToolListResponse GetRegisteredTools(int offset = 0, int limit = 200)
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

    /// <summary>
    /// Builds the fixed public 5-tool MCP surface exposed over HTTP metadata.
    /// </summary>
    private List<ToolDefinition> GetPublicToolDefinitions()
    {
        var internalTools = GetToolDefinitions().ToDictionary(t => t.Name, StringComparer.OrdinalIgnoreCase);
        return PublicToolNames
            .Select(name => internalTools.TryGetValue(name, out var tool) ? tool : BuildPublicPlaceholder(name))
            .OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Builds placeholder metadata for public tools whose execution is routed by the client rather than implemented as host handlers.
    /// </summary>
    /// <param name="name">Public tool name.</param>
    /// <returns>Tool definition aligned with the public contract.</returns>
    private static ToolDefinition BuildPublicPlaceholder(string name)
    {
        return name switch
        {
            "ctx_read" => new ToolDefinition
            {
                Name = "ctx_read",
                Description = "Read code and archived output. target=file|files|symbol|outline|archive. mode=auto|full|map|signatures|diff|aggressive|entropy|task|reference|lines:N-M.",
                InputSchema = new Dictionary<string, object?>
                {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object?>
                    {
                        ["target"] = new Dictionary<string, object?> { ["type"] = "string", ["description"] = "file|files|symbol|outline|archive" },
                        ["path"] = new Dictionary<string, object?> { ["type"] = "string", ["description"] = "File path" },
                        ["paths"] = new Dictionary<string, object?> { ["type"] = "array", ["items"] = new Dictionary<string, object?> { ["type"] = "string" }, ["description"] = "Multiple file paths when target=files" },
                        ["name"] = new Dictionary<string, object?> { ["type"] = "string", ["description"] = "Symbol name when target=symbol" },
                        ["kind"] = new Dictionary<string, object?> { ["type"] = "string", ["description"] = "Symbol kind or outline filter" },
                        ["mode"] = new Dictionary<string, object?> { ["type"] = "string" },
                        ["id"] = new Dictionary<string, object?> { ["type"] = "string", ["description"] = "Archive id when target=archive" },
                        ["action"] = new Dictionary<string, object?> { ["type"] = "string", ["description"] = "Archive retrieval action when target=archive" },
                        ["start_line"] = new Dictionary<string, object?> { ["type"] = "integer" },
                        ["fresh"] = new Dictionary<string, object?> { ["type"] = "boolean" },
                    },
                    ["required"] = Array.Empty<string>(),
                },
            },
            "ctx_search" => new ToolDefinition
            {
                Name = "ctx_search",
                Description = "Search code by regex or semantics. mode=regex|semantic.",
                InputSchema = new Dictionary<string, object?>
                {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object?>
                    {
                        ["mode"] = new Dictionary<string, object?> { ["type"] = "string", ["description"] = "regex|semantic" },
                        ["pattern"] = new Dictionary<string, object?> { ["type"] = "string", ["description"] = "Regex pattern when mode=regex" },
                        ["query"] = new Dictionary<string, object?> { ["type"] = "string", ["description"] = "Natural language query when mode=semantic" },
                        ["path"] = new Dictionary<string, object?> { ["type"] = "string" },
                        ["ext"] = new Dictionary<string, object?> { ["type"] = "string" },
                        ["top_k"] = new Dictionary<string, object?> { ["type"] = "integer" },
                        ["path_glob"] = new Dictionary<string, object?> { ["type"] = "string" },
                        ["ignore_gitignore"] = new Dictionary<string, object?> { ["type"] = "boolean" },
                    },
                    ["required"] = Array.Empty<string>(),
                },
            },
            "ctx_tree" => new ToolDefinition
            {
                Name = "ctx_tree",
                Description = "Directory listing with file counts.",
                InputSchema = new Dictionary<string, object?>
                {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object?>
                    {
                        ["path"] = new Dictionary<string, object?> { ["type"] = "string" },
                        ["depth"] = new Dictionary<string, object?> { ["type"] = "integer" },
                        ["show_hidden"] = new Dictionary<string, object?> { ["type"] = "boolean" },
                    },
                },
            },
            "ctx_shell" => new ToolDefinition
            {
                Name = "ctx_shell",
                Description = "Run shell command (compressed output). Output includes active shell. raw=true skips compression. cwd sets working directory. shell_path overrides executable per call.",
                InputSchema = new Dictionary<string, object?>
                {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object?>
                    {
                        ["command"] = new Dictionary<string, object?> { ["type"] = "string", ["description"] = "Shell command" },
                        ["raw"] = new Dictionary<string, object?> { ["type"] = "boolean", ["description"] = "Skip compression for full output" },
                        ["cwd"] = new Dictionary<string, object?> { ["type"] = "string", ["description"] = "Working directory (defaults to last cd or project root)" },
                        ["shell_path"] = new Dictionary<string, object?> { ["type"] = "string", ["description"] = "Optional shell executable or path for this call. Legacy alias: shell." },
                    },
                    ["required"] = new[] { "command" },
                },
            },
            "ctx" => new ToolDefinition
            {
                Name = "ctx",
                Description = "High-level meta-tool. domain=memory|context|graph|analytics|agents|inspect with action selecting the operation inside that domain.",
                InputSchema = new Dictionary<string, object?>
                {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object?>
                    {
                        ["domain"] = new Dictionary<string, object?> { ["type"] = "string", ["description"] = "memory|context|graph|analytics|agents|inspect" },
                        ["action"] = new Dictionary<string, object?> { ["type"] = "string" },
                        ["view"] = new Dictionary<string, object?> { ["type"] = "string" },
                        ["path"] = new Dictionary<string, object?> { ["type"] = "string" },
                        ["paths"] = new Dictionary<string, object?> { ["type"] = "array", ["items"] = new Dictionary<string, object?> { ["type"] = "string" } },
                        ["query"] = new Dictionary<string, object?> { ["type"] = "string" },
                        ["pattern"] = new Dictionary<string, object?> { ["type"] = "string" },
                        ["value"] = new Dictionary<string, object?> { ["type"] = "string" },
                        ["category"] = new Dictionary<string, object?> { ["type"] = "string" },
                        ["key"] = new Dictionary<string, object?> { ["type"] = "string" },
                        ["to"] = new Dictionary<string, object?> { ["type"] = "string" },
                        ["spec"] = new Dictionary<string, object?> { ["type"] = "string" },
                        ["budget"] = new Dictionary<string, object?> { ["type"] = "integer" },
                        ["task"] = new Dictionary<string, object?> { ["type"] = "string" },
                        ["mode"] = new Dictionary<string, object?> { ["type"] = "string" },
                        ["text"] = new Dictionary<string, object?> { ["type"] = "string" },
                        ["message"] = new Dictionary<string, object?> { ["type"] = "string" },
                        ["session_id"] = new Dictionary<string, object?> { ["type"] = "string" },
                        ["period"] = new Dictionary<string, object?> { ["type"] = "string" },
                        ["format"] = new Dictionary<string, object?> { ["type"] = "string" },
                        ["agent_type"] = new Dictionary<string, object?> { ["type"] = "string" },
                        ["role"] = new Dictionary<string, object?> { ["type"] = "string" },
                        ["status"] = new Dictionary<string, object?> { ["type"] = "string" },
                        ["pattern_type"] = new Dictionary<string, object?> { ["type"] = "string" },
                        ["examples"] = new Dictionary<string, object?> { ["type"] = "array", ["items"] = new Dictionary<string, object?> { ["type"] = "string" } },
                        ["confidence"] = new Dictionary<string, object?> { ["type"] = "number" },
                        ["project_root"] = new Dictionary<string, object?> { ["type"] = "string" },
                        ["include_signatures"] = new Dictionary<string, object?> { ["type"] = "boolean" },
                        ["limit"] = new Dictionary<string, object?> { ["type"] = "integer" },
                        ["to_agent"] = new Dictionary<string, object?> { ["type"] = "string" },
                        ["task_id"] = new Dictionary<string, object?> { ["type"] = "string" },
                        ["agent_id"] = new Dictionary<string, object?> { ["type"] = "string" },
                        ["description"] = new Dictionary<string, object?> { ["type"] = "string" },
                        ["state"] = new Dictionary<string, object?> { ["type"] = "string" },
                        ["root"] = new Dictionary<string, object?> { ["type"] = "string" },
                        ["depth"] = new Dictionary<string, object?> { ["type"] = "integer" },
                        ["show_hidden"] = new Dictionary<string, object?> { ["type"] = "boolean" },
                    },
                    ["required"] = new[] { "domain", "action" },
                },
            },
            _ => throw new ArgumentOutOfRangeException(nameof(name), name, "Unknown public tool name."),
        };
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
    public const string Current = "0.8.29";
}
