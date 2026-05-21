namespace NebuCtx.Tools.Ctx;

using System.Text.Json;
using NebuCtx.Server.Core;
using NebuCtx.Server.Core.Services;

/// <summary>
/// Public ctx meta-tool handler for hosted memory workflows.
/// Routes the public memory domain onto the existing session and knowledge services.
/// </summary>
public sealed class CtxToolHandler : IToolHandler
{
    private readonly KnowledgeService _knowledgeService;
    private readonly SessionService _sessionService;

    /// <summary>
    /// Initializes the hosted ctx handler.
    /// </summary>
    /// <param name="knowledgeService">Knowledge service for durable memory actions.</param>
    /// <param name="sessionService">Session service for working-memory actions.</param>
    public CtxToolHandler(KnowledgeService knowledgeService, SessionService sessionService)
    {
        _knowledgeService = knowledgeService;
        _sessionService = sessionService;
    }

    /// <inheritdoc />
    public string Name => "ctx";

    /// <inheritdoc />
    public string Description => "High-level meta-tool. domain=memory|context|graph|analytics|agents|inspect with action selecting the operation inside that domain.";

    /// <inheritdoc />
    public Dictionary<string, object?> InputSchema => new()
    {
        ["type"] = "object",
        ["properties"] = new Dictionary<string, object?>
        {
            ["domain"] = new Dictionary<string, object?> { ["type"] = "string", ["description"] = "memory|context|graph|analytics|agents|inspect" },
            ["action"] = new Dictionary<string, object?> { ["type"] = "string" },
            ["query"] = new Dictionary<string, object?> { ["type"] = "string" },
            ["value"] = new Dictionary<string, object?> { ["type"] = "string" },
            ["category"] = new Dictionary<string, object?> { ["type"] = "string" },
            ["key"] = new Dictionary<string, object?> { ["type"] = "string" },
            ["session_id"] = new Dictionary<string, object?> { ["type"] = "string" },
            ["mode"] = new Dictionary<string, object?> { ["type"] = "string" },
            ["limit"] = new Dictionary<string, object?> { ["type"] = "integer" },
            ["days"] = new Dictionary<string, object?> { ["type"] = "integer" },
            ["confidence"] = new Dictionary<string, object?> { ["type"] = "number" },
            ["items"] = new Dictionary<string, object?>
            {
                ["type"] = "array",
                ["items"] = new Dictionary<string, object?> { ["type"] = "object" },
            },
        },
        ["required"] = new[] { "domain", "action" },
    };

    /// <inheritdoc />
    public async Task<object> ExecuteAsync(Dictionary<string, object?> arguments, ToolExecutionContext context, CancellationToken cancellationToken = default)
    {
        var domain = GetStringArg(arguments, "domain") ?? throw new ArgumentException("'domain' is required for ctx.");
        var action = GetStringArg(arguments, "action") ?? throw new ArgumentException("'action' is required for ctx.");

        return domain switch
        {
            "memory" => await ExecuteMemoryAsync(action, arguments, context, cancellationToken),
            _ => throw new ArgumentException($"Hosted ctx currently supports only the 'memory' domain. Run the Rust client for '{domain}' workflows."),
        };
    }

    /// <summary>
    /// Executes public memory-domain actions by reusing the existing hosted services.
    /// </summary>
    private async Task<object> ExecuteMemoryAsync(string action, Dictionary<string, object?> arguments, ToolExecutionContext context, CancellationToken cancellationToken)
    {
        return action switch
        {
            "task" => await ExecuteTaskAsync(arguments, context, cancellationToken),
            "finding" => await ExecuteFindingAsync(arguments, context, cancellationToken),
            "decision" => await ExecuteDecisionAsync(arguments, context, cancellationToken),
            "save" => await _sessionService.SaveAsync(context.ProjectId, cancellationToken),
            "load" => await _sessionService.LoadAsync(context.ProjectId, GetStringArg(arguments, "session_id"), cancellationToken),
            "status" => await _sessionService.GetStatusAsync(context.ProjectId, cancellationToken),
            "reset" => await _sessionService.ResetAsync(context.ProjectId, cancellationToken),
            "list" => await _sessionService.ListAsync(context.ProjectId, GetIntArg(arguments, "limit") ?? 10, cancellationToken),
            "cleanup" => await _sessionService.CleanupAsync(context.ProjectId, GetIntArg(arguments, "days") ?? 7, cancellationToken),
            "store" or "set" or "remember" => await ExecuteRememberAsync(arguments, context, cancellationToken),
            "recall" => await ExecuteRecallAsync(arguments, context, cancellationToken),
            "search" => await ExecuteRecallAsync(arguments, context, cancellationToken),
            "categories" => await ExecuteCategoriesAsync(context, cancellationToken),
            "timeline" => await ExecuteTimelineAsync(arguments, context, cancellationToken),
            "consolidate" => await _knowledgeService.ConsolidateAsync(context.ProjectId, cancellationToken),
            "promote" => await ExecutePromoteAsync(arguments, context, cancellationToken),
            "upkeep" => await _knowledgeService.UpkeepAsync(context.ProjectId, cancellationToken),
            "wakeup" => await _knowledgeService.BuildWakeupAsync(context.ProjectId, cancellationToken),
            "triage" => await ExecuteTriageAsync(arguments, context, cancellationToken),
            "remove" => await ExecuteRemoveAsync(arguments, context, cancellationToken),
            _ => throw new ArgumentException("Unknown hosted memory action. Use one of: task, finding, decision, save, load, status, reset, list, cleanup, store, set, remember, recall, search, categories, timeline, consolidate, promote, upkeep, wakeup, triage, remove"),
        };
    }

    /// <summary>
    /// Stores a durable hosted knowledge fact through the public memory domain.
    /// </summary>
    private async Task<object> ExecuteRememberAsync(Dictionary<string, object?> arguments, ToolExecutionContext context, CancellationToken cancellationToken)
    {
        var category = GetStringArg(arguments, "category") ?? "general";
        var key = GetStringArg(arguments, "key") ?? throw new ArgumentException("'key' is required for memory remember.");
        var value = GetStringArg(arguments, "value") ?? throw new ArgumentException("'value' is required for memory remember.");
        var confidence = GetFloatArg(arguments, "confidence") ?? 1.0f;

        await _knowledgeService.RememberAsync(context.ProjectId, category, key, value, confidence, cancellationToken: cancellationToken);
        return new { remembered = true, category, key, confidence };
    }

    /// <summary>
    /// Recalls durable hosted knowledge through the public memory domain.
    /// </summary>
    private async Task<object> ExecuteRecallAsync(Dictionary<string, object?> arguments, ToolExecutionContext context, CancellationToken cancellationToken)
    {
        var query = GetStringArg(arguments, "query") ?? throw new ArgumentException("'query' is required for memory recall.");
        var category = GetStringArg(arguments, "category");
        var limit = GetIntArg(arguments, "limit") ?? 10;

        var entries = await _knowledgeService.RecallAsync(context.ProjectId, category, query, limit, cancellationToken);
        return new
        {
            count = entries.Count,
            entries = entries.Select(entry => new
            {
                entry.Category,
                entry.Key,
                entry.Value,
                entry.Confidence,
                created_at = entry.CreatedAt,
                updated_at = entry.UpdatedAt,
                logical_key = entry.LogicalKey,
                promotion_identity = entry.PromotionIdentity,
                source_type = entry.SourceType,
                source_scope = entry.SourceScope,
                lifecycle_status = entry.LifecycleStatus,
                lifecycle_score = entry.LifecycleScore,
                confirmation_count = entry.ConfirmationCount,
                last_confirmed_at = entry.LastConfirmedAt,
                retrieval_count = entry.RetrievalCount,
                last_retrieved_at = entry.LastRetrievedAt,
            }),
        };
    }

    /// <summary>
    /// Lists hosted knowledge categories through the public memory domain.
    /// </summary>
    private async Task<object> ExecuteCategoriesAsync(ToolExecutionContext context, CancellationToken cancellationToken)
    {
        var categories = await _knowledgeService.GetCategoriesAsync(context.ProjectId, cancellationToken);
        return new
        {
            count = categories.Count,
            categories = categories.Select(category => new { category = category.Category, facts = category.Count }),
        };
    }

    /// <summary>
    /// Lists hosted knowledge timeline entries through the public memory domain.
    /// </summary>
    private async Task<object> ExecuteTimelineAsync(Dictionary<string, object?> arguments, ToolExecutionContext context, CancellationToken cancellationToken)
    {
        var category = GetStringArg(arguments, "category") ?? throw new ArgumentException("'category' is required for memory timeline.");
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
    /// Removes a durable hosted knowledge fact through the public memory domain.
    /// </summary>
    private async Task<object> ExecuteRemoveAsync(Dictionary<string, object?> arguments, ToolExecutionContext context, CancellationToken cancellationToken)
    {
        var category = GetStringArg(arguments, "category") ?? throw new ArgumentException("'category' is required for memory remove.");
        var key = GetStringArg(arguments, "key") ?? throw new ArgumentException("'key' is required for memory remove.");
        var removed = await _knowledgeService.RemoveAsync(context.ProjectId, category, key, cancellationToken);
        return new { removed, category, key };
    }

    /// <summary>
    /// Promotes explicit hosted memory candidates through the public memory domain.
    /// </summary>
    private async Task<object> ExecutePromoteAsync(Dictionary<string, object?> arguments, ToolExecutionContext context, CancellationToken cancellationToken)
    {
        arguments.TryGetValue("items", out var rawItems);
        var items = KnowledgeService.ParsePromotionItems(rawItems);
        return await _knowledgeService.PromoteAsync(context.ProjectId, items, cancellationToken);
    }

    /// <summary>
    /// Executes hosted memory triage through the public memory domain.
    /// </summary>
    private async Task<object> ExecuteTriageAsync(Dictionary<string, object?> arguments, ToolExecutionContext context, CancellationToken cancellationToken)
    {
        var mode = GetStringArg(arguments, "mode");
        var apply = string.Equals(mode, "apply", StringComparison.OrdinalIgnoreCase);
        return await _knowledgeService.TriageAsync(context.ProjectId, apply, cancellationToken);
    }

    /// <summary>
    /// Sets the current hosted session task through the public memory domain.
    /// </summary>
    private async Task<object> ExecuteTaskAsync(Dictionary<string, object?> arguments, ToolExecutionContext context, CancellationToken cancellationToken)
    {
        var value = GetStringArg(arguments, "value") ?? throw new ArgumentException("'value' is required for memory task.");
        return await _sessionService.SetTaskAsync(context.ProjectId, value, cancellationToken);
    }

    /// <summary>
    /// Records a hosted session finding through the public memory domain.
    /// </summary>
    private async Task<object> ExecuteFindingAsync(Dictionary<string, object?> arguments, ToolExecutionContext context, CancellationToken cancellationToken)
    {
        var value = GetStringArg(arguments, "value") ?? throw new ArgumentException("'value' is required for memory finding.");
        return await _sessionService.AddFindingAsync(context.ProjectId, value, cancellationToken);
    }

    /// <summary>
    /// Records a hosted session decision through the public memory domain.
    /// </summary>
    private async Task<object> ExecuteDecisionAsync(Dictionary<string, object?> arguments, ToolExecutionContext context, CancellationToken cancellationToken)
    {
        var value = GetStringArg(arguments, "value") ?? throw new ArgumentException("'value' is required for memory decision.");
        return await _sessionService.AddDecisionAsync(context.ProjectId, value, cancellationToken);
    }

    /// <summary>
    /// Extracts a string argument.
    /// </summary>
    private static string? GetStringArg(Dictionary<string, object?> arguments, string key)
        => arguments.TryGetValue(key, out var value) ? value?.ToString() : null;

    /// <summary>
    /// Extracts an integer argument.
    /// </summary>
    private static int? GetIntArg(Dictionary<string, object?> arguments, string key)
    {
        if (!arguments.TryGetValue(key, out var value)) return null;

        return value switch
        {
            int integer => integer,
            JsonElement { ValueKind: JsonValueKind.Number } json when json.TryGetInt32(out var parsedJson) => parsedJson,
            JsonElement { ValueKind: JsonValueKind.String } json when int.TryParse(json.GetString(), out var parsedJson) => parsedJson,
            _ => int.TryParse(value?.ToString(), out var parsed) ? parsed : null,
        };
    }

    /// <summary>
    /// Extracts a float argument.
    /// </summary>
    private static float? GetFloatArg(Dictionary<string, object?> arguments, string key)
    {
        if (!arguments.TryGetValue(key, out var value)) return null;

        return value switch
        {
            double number => (float)number,
            float number => number,
            JsonElement { ValueKind: JsonValueKind.Number } json when json.TryGetDouble(out var parsedJson) => (float)parsedJson,
            JsonElement { ValueKind: JsonValueKind.String } json when float.TryParse(json.GetString(), out var parsedJson) => parsedJson,
            _ => float.TryParse(value?.ToString(), out var parsed) ? parsed : null,
        };
    }
}
