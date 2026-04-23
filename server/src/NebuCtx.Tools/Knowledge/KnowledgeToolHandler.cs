namespace NebuCtx.Tools.Knowledge;

using NebuCtx.Application;
using NebuCtx.Application.Services;

/// <summary>
/// Tool handler for ctx_knowledge — project-scoped categorized knowledge store.
/// Actions: remember, recall, status, remove, categories.
/// </summary>
public sealed class KnowledgeToolHandler : IToolHandler
{
    private readonly KnowledgeService _knowledgeService;

    /// <summary>
    /// Initializes the knowledge tool handler.
    /// </summary>
    /// <param name="knowledgeService">Knowledge service for fact operations.</param>
    public KnowledgeToolHandler(KnowledgeService knowledgeService)
    {
        _knowledgeService = knowledgeService;
    }

    /// <inheritdoc />
    public string Name => "ctx_knowledge";

    /// <inheritdoc />
    public string Description => "Project-scoped categorized knowledge store. Actions: remember, recall, status, remove, categories.";

    /// <inheritdoc />
    public Dictionary<string, object?> InputSchema => new()
    {
        ["type"] = "object",
        ["properties"] = new Dictionary<string, object?>
        {
            ["action"] = new Dictionary<string, object?>
            {
                ["type"] = "string",
                ["description"] = "Action: remember, recall, status, remove, categories",
                ["enum"] = new[] { "remember", "recall", "status", "remove", "categories" },
            },
            ["category"] = new Dictionary<string, object?>
            {
                ["type"] = "string",
                ["description"] = "Knowledge category (e.g. 'architecture', 'conventions'). Required for remember and remove.",
            },
            ["key"] = new Dictionary<string, object?>
            {
                ["type"] = "string",
                ["description"] = "Unique fact key within the category. Required for remember and remove.",
            },
            ["value"] = new Dictionary<string, object?>
            {
                ["type"] = "string",
                ["description"] = "Fact value. Required for remember.",
            },
            ["confidence"] = new Dictionary<string, object?>
            {
                ["type"] = "number",
                ["description"] = "Confidence score 0–1. Defaults to 1.0.",
            },
            ["query"] = new Dictionary<string, object?>
            {
                ["type"] = "string",
                ["description"] = "Text search query. Required for recall.",
            },
            ["limit"] = new Dictionary<string, object?>
            {
                ["type"] = "integer",
                ["description"] = "Maximum results for recall (default: 10).",
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
            "remember"   => await ExecuteRememberAsync(arguments, context, cancellationToken),
            "recall"     => await ExecuteRecallAsync(arguments, context, cancellationToken),
            "status"     => await _knowledgeService.GetStatusAsync(context.ProjectId, cancellationToken),
            "remove"     => await ExecuteRemoveAsync(arguments, context, cancellationToken),
            "categories" => await ExecuteCategoriesAsync(context, cancellationToken),
            _            => throw new ArgumentException($"Unknown knowledge action: '{action}'. Use: remember, recall, status, remove, categories"),
        };
    }

    /// <summary>
    /// Stores a categorized knowledge fact.
    /// </summary>
    private async Task<object> ExecuteRememberAsync(Dictionary<string, object?> arguments, ToolExecutionContext context, CancellationToken cancellationToken)
    {
        var category   = GetStringArg(arguments, "category") ?? throw new ArgumentException("'category' is required for remember.");
        var key        = GetStringArg(arguments, "key")      ?? throw new ArgumentException("'key' is required for remember.");
        var value      = GetStringArg(arguments, "value")    ?? throw new ArgumentException("'value' is required for remember.");
        var confidence = GetFloatArg(arguments, "confidence") ?? 1.0f;

        await _knowledgeService.RememberAsync(context.ProjectId, category, key, value, confidence, cancellationToken);
        return new { remembered = true, category, key, confidence };
    }

    /// <summary>
    /// Searches knowledge facts by text query.
    /// </summary>
    private async Task<object> ExecuteRecallAsync(Dictionary<string, object?> arguments, ToolExecutionContext context, CancellationToken cancellationToken)
    {
        var query    = GetStringArg(arguments, "query")    ?? throw new ArgumentException("'query' is required for recall.");
        var category = GetStringArg(arguments, "category");
        var limit    = GetIntArg(arguments, "limit") ?? 10;

        var entries = await _knowledgeService.RecallAsync(context.ProjectId, category, query, limit, cancellationToken);
        return new
        {
            count = entries.Count,
            entries = entries.Select(e => new { e.Category, e.Key, e.Value, e.Confidence, updated_at = e.UpdatedAt }),
        };
    }

    /// <summary>
    /// Removes a knowledge fact by category and key.
    /// </summary>
    private async Task<object> ExecuteRemoveAsync(Dictionary<string, object?> arguments, ToolExecutionContext context, CancellationToken cancellationToken)
    {
        var category = GetStringArg(arguments, "category") ?? throw new ArgumentException("'category' is required for remove.");
        var key      = GetStringArg(arguments, "key")      ?? throw new ArgumentException("'key' is required for remove.");

        var removed = await _knowledgeService.RemoveAsync(context.ProjectId, category, key, cancellationToken);
        return new { removed, category, key };
    }

    /// <summary>
    /// Lists all knowledge categories with fact counts.
    /// </summary>
    private async Task<object> ExecuteCategoriesAsync(ToolExecutionContext context, CancellationToken cancellationToken)
    {
        var categories = await _knowledgeService.GetCategoriesAsync(context.ProjectId, cancellationToken);
        return new
        {
            count = categories.Count,
            categories = categories.Select(c => new { category = c.Category, facts = c.Count }),
        };
    }

    /// <summary>Extracts a string argument.</summary>
    private static string? GetStringArg(Dictionary<string, object?> arguments, string key)
        => arguments.TryGetValue(key, out var v) ? v?.ToString() : null;

    /// <summary>Extracts an integer argument.</summary>
    private static int? GetIntArg(Dictionary<string, object?> arguments, string key)
    {
        if (!arguments.TryGetValue(key, out var v)) return null;
        return v is int i ? i : (int.TryParse(v?.ToString(), out var parsed) ? parsed : null);
    }

    /// <summary>Extracts a float argument.</summary>
    private static float? GetFloatArg(Dictionary<string, object?> arguments, string key)
    {
        if (!arguments.TryGetValue(key, out var v)) return null;
        return v is double d ? (float)d : v is float f ? f : (float.TryParse(v?.ToString(), out var parsed) ? parsed : null);
    }
}
