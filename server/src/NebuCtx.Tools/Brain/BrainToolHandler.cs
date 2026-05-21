namespace NebuCtx.Tools.Brain;

using NebuCtx.Server.Core;
using NebuCtx.Server.Core.Services;
using NebuCtx.Storage;

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
    public string Description => "Project-scoped canonical fact memory. Actions: status, store, ingest, recall, forget.";

    /// <inheritdoc />
    public Dictionary<string, object?> InputSchema => new()
    {
        ["type"] = "object",
        ["properties"] = new Dictionary<string, object?>
        {
            ["action"] = new Dictionary<string, object?>
            {
                ["type"] = "string",
                ["description"] = "Action to perform: status, store, ingest, recall, forget",
                ["enum"] = new[] { "status", "store", "ingest", "recall", "forget" },
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
            ["kind"] = new Dictionary<string, object?>
            {
                ["type"] = "string",
                ["description"] = "Fact kind for ingest/store actions",
            },
            ["category"] = new Dictionary<string, object?>
            {
                ["type"] = "string",
                ["description"] = "Fact category for ingest/store actions",
            },
            ["source_type"] = new Dictionary<string, object?>
            {
                ["type"] = "string",
                ["description"] = "Source type for fact ingest",
            },
            ["source_scope"] = new Dictionary<string, object?>
            {
                ["type"] = "string",
                ["description"] = "Source scope for fact ingest",
            },
            ["promotion_identity"] = new Dictionary<string, object?>
            {
                ["type"] = "string",
                ["description"] = "Deterministic replay-safe identity",
            },
            ["logical_key"] = new Dictionary<string, object?>
            {
                ["type"] = "string",
                ["description"] = "Stable logical key for canonicalization",
            },
            ["confidence"] = new Dictionary<string, object?>
            {
                ["type"] = "number",
                ["description"] = "Confidence score for fact ingest",
            },
            ["lifecycle_status"] = new Dictionary<string, object?>
            {
                ["type"] = "string",
                ["description"] = "Lifecycle state for fact ingest",
            },
            ["evidence"] = new Dictionary<string, object?>
            {
                ["type"] = "string",
                ["description"] = "Optional evidence text for fact ingest",
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
            "ingest" => await ExecuteIngestAsync(arguments, context, cancellationToken),
            "recall" => await ExecuteRecallAsync(arguments, context, cancellationToken),
            "forget" => await ExecuteForgetAsync(arguments, context, cancellationToken),
            _ => throw new ArgumentException($"Unknown brain action: '{action}'"),
        };
    }

    /// <summary>
    /// Executes the store action — persists a legacy key-value memory entry.
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
    /// Executes the ingest action — persists a typed canonical brain fact.
    /// </summary>
    private async Task<object> ExecuteIngestAsync(Dictionary<string, object?> arguments, ToolExecutionContext context, CancellationToken cancellationToken)
    {
        var key = GetStringArg(arguments, "key") ?? throw new ArgumentException("'key' is required for brain ingest.");
        var value = GetStringArg(arguments, "value") ?? throw new ArgumentException("'value' is required for brain ingest.");
        var confidence = GetFloatArg(arguments, "confidence") ?? 0.85f;

        var entry = new BrainEntry
        {
            Key = key,
            Value = value,
            Kind = GetStringArg(arguments, "kind") ?? "fact",
            Category = GetStringArg(arguments, "category") ?? "general",
            SourceType = GetStringArg(arguments, "source_type") ?? "brain_ingest",
            SourceScope = GetStringArg(arguments, "source_scope") ?? context.ProjectId,
            PromotionIdentity = GetStringArg(arguments, "promotion_identity") ?? string.Empty,
            LogicalKey = GetStringArg(arguments, "logical_key") ?? string.Empty,
            LifecycleStatus = GetStringArg(arguments, "lifecycle_status") ?? "current",
            Confidence = confidence,
            Evidence = GetStringArg(arguments, "evidence") ?? string.Empty,
        };

        await _brainService.StoreFactAsync(context.ProjectId, entry, cancellationToken);
        return new { stored = true, key, kind = entry.Kind, category = entry.Category, confidence };
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
    /// Executes the forget action.
    /// </summary>
    private async Task<object> ExecuteForgetAsync(Dictionary<string, object?> arguments, ToolExecutionContext context, CancellationToken cancellationToken)
    {
        var key = GetStringArg(arguments, "key")
            ?? throw new ArgumentException("'key' is required for brain forget.");
        var removed = await _brainService.DeleteAsync(context.ProjectId, key, cancellationToken);
        return new { removed, key };
    }

    /// <summary>
    /// Extracts a string argument from the arguments dictionary.
    /// </summary>
    private static string? GetStringArg(Dictionary<string, object?> arguments, string key)
    {
        return arguments.TryGetValue(key, out var value) ? value?.ToString() : null;
    }

    /// <summary>
    /// Extracts a float argument from the arguments dictionary.
    /// </summary>
    private static float? GetFloatArg(Dictionary<string, object?> arguments, string key)
    {
        if (!arguments.TryGetValue(key, out var value) || value is null)
        {
            return null;
        }

        return value switch
        {
            float single => single,
            double dbl => (float)dbl,
            decimal dec => (float)dec,
            _ when float.TryParse(value.ToString(), out var parsed) => parsed,
            _ => null,
        };
    }
}
