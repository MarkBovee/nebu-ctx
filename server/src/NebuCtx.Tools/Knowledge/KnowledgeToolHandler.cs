namespace NebuCtx.Tools.Knowledge;

using NebuCtx.Server.Core;
using NebuCtx.Server.Core.Services;
using System.Text.Json;

/// <summary>
/// Tool handler for ctx_knowledge — project-scoped categorized knowledge store.
/// Actions: remember, recall, search, status, remove, categories, timeline, consolidate, promote, upkeep, wakeup, triage.
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
    public string Description => "Project-scoped categorized knowledge store. Actions: remember, recall, search, status, remove, categories, timeline, consolidate, promote, upkeep, wakeup, triage.";

    /// <inheritdoc />
    public Dictionary<string, object?> InputSchema => new()
    {
        ["type"] = "object",
        ["properties"] = new Dictionary<string, object?>
        {
            ["action"] = new Dictionary<string, object?>
            {
                ["type"] = "string",
                ["description"] = "Action: remember, recall, search, status, remove, categories, timeline, consolidate, promote, upkeep, wakeup, triage",
                ["enum"] = new[] { "remember", "recall", "search", "status", "remove", "categories", "timeline", "consolidate", "promote", "upkeep", "wakeup", "triage" },
            },
            ["mode"] = new Dictionary<string, object?>
            {
                ["type"] = "string",
                ["description"] = "Optional execution mode for triage actions: preview or apply.",
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
                ["description"] = "Text search query. Required for recall and search.",
            },
            ["limit"] = new Dictionary<string, object?>
            {
                ["type"] = "integer",
                ["description"] = "Maximum results for recall/search (default: 10).",
            },
            ["items"] = new Dictionary<string, object?>
            {
                ["type"] = "array",
                ["description"] = "Explicit memory candidates for promote.",
                ["items"] = new Dictionary<string, object?>
                {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object?>
                    {
                        ["category"] = new Dictionary<string, object?> { ["type"] = "string" },
                        ["key"] = new Dictionary<string, object?> { ["type"] = "string" },
                        ["value"] = new Dictionary<string, object?> { ["type"] = "string" },
                        ["confidence"] = new Dictionary<string, object?> { ["type"] = "number" },
                        ["source_type"] = new Dictionary<string, object?> { ["type"] = "string" },
                        ["source_scope"] = new Dictionary<string, object?> { ["type"] = "string" },
                    },
                },
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
            "search"     => await ExecuteRecallAsync(arguments, context, cancellationToken),
            "categories" => await ExecuteCategoriesAsync(context, cancellationToken),
            "timeline"   => await ExecuteTimelineAsync(arguments, context, cancellationToken),
            "status"     => await _knowledgeService.GetStatusAsync(context.ProjectId, cancellationToken),
            "remove"     => await ExecuteRemoveAsync(arguments, context, cancellationToken),
            "consolidate" => await _knowledgeService.ConsolidateAsync(context.ProjectId, cancellationToken),
            "promote"     => await ExecutePromoteAsync(arguments, context, cancellationToken),
            "upkeep"      => await _knowledgeService.UpkeepAsync(context.ProjectId, cancellationToken),
            "wakeup"      => await _knowledgeService.BuildWakeupAsync(context.ProjectId, cancellationToken),
            "triage"      => await ExecuteTriageAsync(arguments, context, cancellationToken),
            _             => throw new ArgumentException($"Unknown knowledge action: '{action}'. Use: remember, recall, search, status, remove, categories, timeline, consolidate, promote, upkeep, wakeup, triage"),
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

        await _knowledgeService.RememberAsync(
            context.ProjectId,
            category,
            key,
            value,
            confidence,
            cancellationToken: cancellationToken);
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
            entries = entries.Select(e => new
            {
                e.Category,
                e.Key,
                e.Value,
                e.Confidence,
                created_at = e.CreatedAt,
                updated_at = e.UpdatedAt,
                logical_key = e.LogicalKey,
                promotion_identity = e.PromotionIdentity,
                source_type = e.SourceType,
                source_scope = e.SourceScope,
                lifecycle_status = e.LifecycleStatus,
                lifecycle_score = e.LifecycleScore,
                confirmation_count = e.ConfirmationCount,
                last_confirmed_at = e.LastConfirmedAt,
                retrieval_count = e.RetrievalCount,
                last_retrieved_at = e.LastRetrievedAt,
            }),
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

    /// <summary>
    /// Lists hosted knowledge timeline entries for a category.
    /// </summary>
    private async Task<object> ExecuteTimelineAsync(Dictionary<string, object?> arguments, ToolExecutionContext context, CancellationToken cancellationToken)
    {
        var category = GetStringArg(arguments, "category") ?? throw new ArgumentException("'category' is required for timeline.");
        var limit = GetIntArg(arguments, "limit") ?? 50;
        var entries = await _knowledgeService.GetTimelineAsync(context.ProjectId, category, limit, cancellationToken);
        return new
        {
            category,
            count = entries.Count,
            entries,
        };
    }

    /// <summary>
    /// Promotes explicit memory candidates into canonical project knowledge.
    /// </summary>
    private async Task<object> ExecutePromoteAsync(Dictionary<string, object?> arguments, ToolExecutionContext context, CancellationToken cancellationToken)
    {
        arguments.TryGetValue("items", out var rawItems);
        var items = KnowledgeService.ParsePromotionItems(rawItems);
        return await _knowledgeService.PromoteAsync(context.ProjectId, items, cancellationToken);
    }

    /// <summary>
    /// Previews or applies hosted memory triage for a project.
    /// </summary>
    private async Task<object> ExecuteTriageAsync(Dictionary<string, object?> arguments, ToolExecutionContext context, CancellationToken cancellationToken)
    {
        var mode = GetStringArg(arguments, "mode");
        var apply = string.Equals(mode, "apply", StringComparison.OrdinalIgnoreCase);
        return await _knowledgeService.TriageAsync(context.ProjectId, apply, cancellationToken);
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
        return v switch
        {
            double d => (float)d,
            float f => f,
            JsonElement { ValueKind: JsonValueKind.Number } json when json.TryGetDouble(out var dbl) => (float)dbl,
            JsonElement { ValueKind: JsonValueKind.String } json when float.TryParse(json.GetString(), out var parsedJson) => parsedJson,
            _ => float.TryParse(v?.ToString(), out var parsed) ? parsed : null,
        };
    }
}
