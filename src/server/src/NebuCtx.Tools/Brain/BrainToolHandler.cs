namespace NebuCtx.Tools.Brain;

using NebuCtx.Application;
using NebuCtx.Application.Services;

/// <summary>
/// Tool handler for ctx_brain — project-scoped persistent memory.
/// Dispatches to status, store, and recall actions based on the "action" argument.
/// </summary>
public sealed class BrainToolHandler : IToolHandler
{
    private readonly BrainService _brainService;

    /// <summary>
    /// Initializes the brain tool handler.
    /// </summary>
    /// <param name="brainService">Brain service for memory operations.</param>
    public BrainToolHandler(BrainService brainService)
    {
        _brainService = brainService;
    }

    /// <inheritdoc />
    public string Name => "ctx_brain";

    /// <inheritdoc />
    public string Description => "Project-scoped persistent memory. Actions: status, store, recall.";

    /// <inheritdoc />
    public Dictionary<string, object?> InputSchema => new()
    {
        ["type"] = "object",
        ["properties"] = new Dictionary<string, object?>
        {
            ["action"] = new Dictionary<string, object?>
            {
                ["type"] = "string",
                ["description"] = "Action to perform: status, store, recall",
                ["enum"] = new[] { "status", "store", "recall" },
            },
            ["key"] = new Dictionary<string, object?>
            {
                ["type"] = "string",
                ["description"] = "Memory key (required for store)",
            },
            ["value"] = new Dictionary<string, object?>
            {
                ["type"] = "string",
                ["description"] = "Memory value (required for store)",
            },
            ["query"] = new Dictionary<string, object?>
            {
                ["type"] = "string",
                ["description"] = "Search query (required for recall)",
            },
            ["limit"] = new Dictionary<string, object?>
            {
                ["type"] = "integer",
                ["description"] = "Maximum results for recall (default: 10)",
            },
        },
        ["required"] = new[] { "action" },
    };

    /// <inheritdoc />
    public async Task<object> ExecuteAsync(Dictionary<string, object?> arguments, ToolExecutionContext context, CancellationToken cancellationToken = default)
    {
        var action = GetStringArg(arguments, "action");

        return action switch
        {
            "status" => await _brainService.GetStatusAsync(context.ProjectId, cancellationToken),
            "store" => await ExecuteStoreAsync(arguments, context, cancellationToken),
            "recall" => await ExecuteRecallAsync(arguments, context, cancellationToken),
            _ => throw new ArgumentException($"Unknown brain action: '{action}'"),
        };
    }

    /// <summary>
    /// Executes the store action — persists a key-value memory entry.
    /// </summary>
    private async Task<object> ExecuteStoreAsync(Dictionary<string, object?> arguments, ToolExecutionContext context, CancellationToken cancellationToken)
    {
        var key = GetStringArg(arguments, "key")
            ?? throw new ArgumentException("'key' is required for brain store.");
        var value = GetStringArg(arguments, "value")
            ?? throw new ArgumentException("'value' is required for brain store.");

        await _brainService.StoreAsync(context.ProjectId, key, value, cancellationToken);
        return new { stored = true, key };
    }

    /// <summary>
    /// Executes the recall action — searches memory entries by query.
    /// </summary>
    private async Task<object> ExecuteRecallAsync(Dictionary<string, object?> arguments, ToolExecutionContext context, CancellationToken cancellationToken)
    {
        var query = GetStringArg(arguments, "query")
            ?? throw new ArgumentException("'query' is required for brain recall.");

        var limit = 10;
        if (arguments.TryGetValue("limit", out var limitObj) && limitObj is int limitVal)
        {
            limit = limitVal;
        }

        var entries = await _brainService.RecallAsync(context.ProjectId, query, limit, cancellationToken);
        return new { entries, count = entries.Count };
    }

    /// <summary>
    /// Extracts a string argument from the arguments dictionary.
    /// </summary>
    private static string? GetStringArg(Dictionary<string, object?> arguments, string key)
    {
        return arguments.TryGetValue(key, out var value) ? value?.ToString() : null;
    }
}
