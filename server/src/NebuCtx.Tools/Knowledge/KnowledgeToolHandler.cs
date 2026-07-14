namespace NebuCtx.Tools.Knowledge;

using System.Globalization;

using NebuCtx.Contracts.Mcp;
using NebuCtx.Server.Core;
using NebuCtx.Server.Core.Services;
using NebuCtx.Storage;
using NebuCtx.Tools.Brain;
using System.Text.Json;

/// <summary>
/// Tool handler for ctx_knowledge — project-scoped categorized knowledge store.
/// Actions: remember, recall, search, status, remove, categories, timeline, consolidate, promote, candidates, review_candidate, upkeep, wakeup, triage, upvote, downvote, confirm, reject.
/// </summary>
public sealed class KnowledgeToolHandler : IToolHandler
{
    private readonly KnowledgeService _knowledgeService;
    private readonly MemoryLifecycleService _lifecycleService;

    /// <summary>
    /// Initializes the knowledge tool handler.
    /// </summary>
    /// <param name="knowledgeService">Knowledge service for fact operations.</param>
    /// <param name="lifecycleService">Lifecycle service for stats/promotions/stale/scoring.</param>
    public KnowledgeToolHandler(KnowledgeService knowledgeService, MemoryLifecycleService lifecycleService)
    {
        _knowledgeService = knowledgeService;
        _lifecycleService = lifecycleService;
    }

    /// <inheritdoc />
    public string Name => "ctx_knowledge";

    /// <inheritdoc />
    public string Description => "Project-scoped categorized knowledge store. Actions: remember, recall, search, list, lifecycle, status, remove, categories, timeline, consolidate, promote, candidates, review_candidate, upvote, downvote, confirm, reject, upkeep, wakeup, triage.";

    /// <inheritdoc />
    public Dictionary<string, object?> InputSchema => new()
    {
        ["type"] = "object",
        ["properties"] = new Dictionary<string, object?>
        {
            ["action"] = new Dictionary<string, object?>
            {
                ["type"] = "string",
                ["description"] = "Action: remember, recall, search, list, lifecycle, status, remove, categories, timeline, consolidate, promote, candidates, review_candidate, upvote, downvote, confirm, reject, upkeep, wakeup, triage, import",
                ["enum"] = new[] { "remember", "recall", "search", "list", "lifecycle", "status", "remove", "categories", "timeline", "consolidate", "promote", "candidates", "review_candidate", "upvote", "downvote", "confirm", "reject", "upkeep", "wakeup", "triage", "import" },
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
                ["description"] = "Maximum results for recall/search (default: 10) or list (default: 20).",
            },
            ["offset"] = new Dictionary<string, object?>
            {
                ["type"] = "integer",
                ["description"] = "Pagination offset for list (default: 0).",
            },
            ["source_type"] = new Dictionary<string, object?>
            {
                ["type"] = "string",
                ["description"] = "Filter list results by source_type.",
            },
            ["lifecycle_status"] = new Dictionary<string, object?>
            {
                ["type"] = "string",
                ["description"] = "Filter list results by lifecycle_status (current, stale, superseded, archived).",
            },
            ["sort_field"] = new Dictionary<string, object?>
            {
                ["type"] = "string",
                ["description"] = "Sort field for list: created, updated, confidence, retrieval_count, key, relevance (default: relevance).",
            },
            ["sort_direction"] = new Dictionary<string, object?>
            {
                ["type"] = "string",
                ["description"] = "Sort direction for list: asc or desc (default: desc).",
            },
            ["created_after"] = new Dictionary<string, object?>
            {
                ["type"] = "string",
                ["description"] = "ISO 8601 timestamp; only list entries created at or after this time.",
            },
            ["created_before"] = new Dictionary<string, object?>
            {
                ["type"] = "string",
                ["description"] = "ISO 8601 timestamp; only list entries created at or before this time.",
            },
            ["since"] = new Dictionary<string, object?>
            {
                ["type"] = "string",
                ["description"] = "Relative time window for list filtering, e.g. 1h, 7d, 2w, 1m, 1y.",
            },
            ["promoted_from_session"] = new Dictionary<string, object?>
            {
                ["type"] = "string",
                ["description"] = "Filter list to knowledge facts promoted from this brain session id (memory-correlation).",
            },
            ["promoted_from_brain_key"] = new Dictionary<string, object?>
            {
                ["type"] = "string",
                ["description"] = "Filter list to knowledge facts promoted from this brain entry key (memory-correlation).",
            },
            ["lifecycle_subaction"] = new Dictionary<string, object?>
            {
                ["type"] = "string",
                ["description"] = "Lifecycle sub-action: stats, promotions, stale, scoring.",
            },
            ["lifecycle_days"] = new Dictionary<string, object?>
            {
                ["type"] = "integer",
                ["description"] = "Days threshold for the stale sub-action (default: 30).",
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
            ["promotion_identity"] = new Dictionary<string, object?>
            {
                ["type"] = "string",
                ["description"] = "Stable candidate identity for review actions.",
            },
            ["decision"] = new Dictionary<string, object?>
            {
                ["type"] = "string",
                ["description"] = "Review decision for review_candidate: accept or reject.",
            },
            ["import_payload"] = new Dictionary<string, object?>
            {
                ["type"] = "object",
                ["description"] = "Import payload from a ctx memory export. Contains memories array and overwrite flag.",
            },
            ["overwrite"] = new Dictionary<string, object?>
            {
                ["type"] = "boolean",
                ["description"] = "When true, existing memories with the same key are replaced. Default false.",
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
            "list"       => await ExecuteListAsync(arguments, context, cancellationToken),
            "lifecycle"  => await ExecuteLifecycleAsync(arguments, context, cancellationToken),
            "categories" => await ExecuteCategoriesAsync(context, cancellationToken),
            "timeline"   => await ExecuteTimelineAsync(arguments, context, cancellationToken),
            "status"     => await _knowledgeService.GetStatusAsync(context.ProjectId, cancellationToken),
            "remove"     => await ExecuteRemoveAsync(arguments, context, cancellationToken),
            "consolidate" => await _knowledgeService.ConsolidateAsync(context.ProjectId, cancellationToken),
            "promote"     => await ExecutePromoteAsync(arguments, context, cancellationToken),
            "candidates"  => await ExecuteCandidatesAsync(arguments, context, cancellationToken),
            "review_candidate" => await ExecuteReviewCandidateAsync(arguments, context, cancellationToken),
            "upvote" => await ExecuteReviewCandidateAsync(WithDecision(arguments, "accept"), context, cancellationToken),
            "confirm" => await ExecuteReviewCandidateAsync(WithDecision(arguments, "accept"), context, cancellationToken),
            "downvote" => await ExecuteReviewCandidateAsync(WithDecision(arguments, "reject"), context, cancellationToken),
            "reject" => await ExecuteReviewCandidateAsync(WithDecision(arguments, "reject"), context, cancellationToken),
            "upkeep"      => await _knowledgeService.UpkeepAsync(context.ProjectId, cancellationToken),
            "wakeup"      => await _knowledgeService.BuildWakeupAsync(context.ProjectId, cancellationToken),
            "triage"      => await ExecuteTriageAsync(arguments, context, cancellationToken),
            "import"      => await ExecuteImportAsync(arguments, context, cancellationToken),
            _             => throw new ArgumentException($"Unknown knowledge action: '{action}'. Use: remember, recall, search, list, lifecycle, status, remove, categories, timeline, consolidate, promote, candidates, review_candidate, upvote, downvote, confirm, reject, upkeep, wakeup, triage, import"),
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
                promotion_trace = BuildPromotionTrace(e),
            }),
        };
    }

    private static PromotionTrace? BuildPromotionTrace(KnowledgeEntry entry)
    {
        if (string.IsNullOrEmpty(entry.PromotedFromBrainKey)
            && string.IsNullOrEmpty(entry.PromotedFromBrainCategory)
            && string.IsNullOrEmpty(entry.PromotedFromBrainValue)
            && !entry.PromotedFromTimestamp.HasValue
            && !entry.PromotionTimestamp.HasValue)
        {
            return null;
        }
        return new PromotionTrace
        {
            SourceSessionId = entry.SourceScope,
            SourceBrainKey = entry.PromotedFromBrainKey,
            SourceBrainCategory = entry.PromotedFromBrainCategory,
            SourceBrainValue = entry.PromotedFromBrainValue,
            SourceTimestamp = entry.PromotedFromTimestamp,
            PromotionAction = entry.PromotionAction,
            PromotionTimestamp = entry.PromotionTimestamp,
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
    /// Lists persisted durable memory candidates for the current project.
    /// </summary>
    private async Task<object> ExecuteCandidatesAsync(Dictionary<string, object?> arguments, ToolExecutionContext context, CancellationToken cancellationToken)
    {
        var limit = GetIntArg(arguments, "limit") ?? 25;
        return await _knowledgeService.ListCandidatesAsync(context.ProjectId, limit, cancellationToken);
    }

    /// <summary>
    /// Applies a review decision to a queued durable memory candidate.
    /// </summary>
    private async Task<object> ExecuteReviewCandidateAsync(Dictionary<string, object?> arguments, ToolExecutionContext context, CancellationToken cancellationToken)
    {
        var promotionIdentity = GetStringArg(arguments, "promotion_identity") ?? throw new ArgumentException("'promotion_identity' is required for review_candidate.");
        var decision = GetStringArg(arguments, "decision") ?? throw new ArgumentException("'decision' is required for review_candidate.");
        return await _knowledgeService.ReviewCandidateAsync(context.ProjectId, promotionIdentity, decision, cancellationToken);
    }

    /// <summary>
    /// Copies a review payload and injects a decision alias.
    /// </summary>
    private static Dictionary<string, object?> WithDecision(Dictionary<string, object?> arguments, string decision)
    {
        var cloned = new Dictionary<string, object?>(arguments)
        {
            ["decision"] = decision,
        };
        return cloned;
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

    /// <summary>
    /// Projects a <see cref="KnowledgeEntry"/> into the canonical list envelope item.
    /// </summary>
    private static MemoryListItem ProjectToListItem(KnowledgeEntry entry)
    {
        var value = entry.Value ?? string.Empty;
        if (value.Length > MemoryListItem.MaxValueLength)
        {
            value = string.Concat(value.AsSpan(0, MemoryListItem.MaxValueLength - 1), "…");
        }

        return new MemoryListItem
        {
            Key = $"{entry.Category}:{entry.Key}",
            Category = entry.Category,
            Value = value,
            Confidence = entry.Confidence,
            SourceType = entry.SourceType,
            SourceScope = entry.SourceScope,
            CreatedAt = entry.CreatedAt,
            UpdatedAt = entry.UpdatedAt,
            RetrievalCount = entry.RetrievalCount,
            ConfirmationCount = entry.ConfirmationCount,
            LifecycleScore = entry.LifecycleScore,
            LifecycleStatus = entry.LifecycleStatus,
            PromotionTrace = BuildPromotionTrace(entry),
        };
    }

    /// <summary>
    /// Echoes the active filter values back to the caller in a stable shape.
    /// </summary>
    private static Dictionary<string, object?> FiltersEcho(MemoryListFilter filter)
    {
        var dict = new Dictionary<string, object?> { ["active"] = false };
        if (!string.IsNullOrEmpty(filter.Category)) { dict["active"] = true; dict["category"] = filter.Category; }
        if (!string.IsNullOrEmpty(filter.SourceType)) { dict["active"] = true; dict["source_type"] = filter.SourceType; }
        if (!string.IsNullOrEmpty(filter.LifecycleStatus)) { dict["active"] = true; dict["lifecycle_status"] = filter.LifecycleStatus; }
        if (filter.CreatedAfter.HasValue) { dict["active"] = true; dict["created_after"] = filter.CreatedAfter.Value; }
        if (filter.CreatedBefore.HasValue) { dict["active"] = true; dict["created_before"] = filter.CreatedBefore.Value; }
        if (!string.IsNullOrEmpty(filter.PromotedFromSession)) { dict["active"] = true; dict["promoted_from_session"] = filter.PromotedFromSession; }
        if (!string.IsNullOrEmpty(filter.PromotedFromBrainKey)) { dict["active"] = true; dict["promoted_from_brain_key"] = filter.PromotedFromBrainKey; }
        return dict;
    }

    /// <summary>
    /// Echoes the active sort criteria back to the caller.
    /// </summary>
    private static Dictionary<string, object?> SortEcho(MemoryListFilter filter) => new()
    {
        ["field"] = filter.SortField,
        ["direction"] = filter.SortDirection,
        ["limit"] = filter.Limit,
        ["offset"] = filter.Offset,
    };

    /// <summary>
    /// Builds a <see cref="MemoryListFilter"/> from the tool argument dictionary.
    /// </summary>
    internal static MemoryListFilter BuildKnowledgeListFilter(Dictionary<string, object?> arguments)
    {
        var filter = new MemoryListFilter
        {
            Category = GetStringArg(arguments, "category"),
            SourceType = GetStringArg(arguments, "source_type"),
            LifecycleStatus = GetStringArg(arguments, "lifecycle_status"),
            SortField = GetStringArg(arguments, "sort_field") ?? "relevance",
            SortDirection = GetStringArg(arguments, "sort_direction") ?? "desc",
            Limit = GetIntArg(arguments, "limit") ?? 20,
            Offset = GetIntArg(arguments, "offset") ?? 0,
            PromotedFromSession = GetStringArg(arguments, "promoted_from_session"),
            PromotedFromBrainKey = GetStringArg(arguments, "promoted_from_brain_key"),
        };
        if (arguments.TryGetValue("created_after", out var afterObj) && afterObj is not null)
        {
            filter.CreatedAfter = GetDateTimeOffsetArg(arguments, "created_after");
        }
        if (arguments.TryGetValue("created_before", out var beforeObj) && beforeObj is not null)
        {
            filter.CreatedBefore = GetDateTimeOffsetArg(arguments, "created_before");
        }
        if (!filter.CreatedAfter.HasValue && arguments.TryGetValue("since", out var sinceObj) && sinceObj is not null)
        {
            var raw = sinceObj.ToString();
            if (!string.IsNullOrWhiteSpace(raw) && BrainToolHandler.TryParseRelativeTime(raw, out var since))
            {
                filter.CreatedAfter = since;
            }
        }
        return filter;
    }

    /// <summary>
    /// Executes the list action — returns knowledge facts that match the supplied
    /// filter in a consistent envelope that matches <c>memory-browsing</c>.
    /// </summary>
    private async Task<object> ExecuteListAsync(Dictionary<string, object?> arguments, ToolExecutionContext context, CancellationToken cancellationToken)
    {
        var filter = BuildKnowledgeListFilter(arguments);
        var (entries, total) = await _knowledgeService.ListAsync(context.ProjectId, filter, cancellationToken);
        var items = entries.Select(ProjectToListItem).ToList();
        return new MemoryListResult<MemoryListItem>
        {
            Memories = items,
            Total = total,
            FiltersApplied = FiltersEcho(filter),
            SortApplied = SortEcho(filter),
        };
    }

    /// <summary>
    /// Executes the lifecycle action — dispatches to stats, promotions, stale, or scoring.
    /// </summary>
    private async Task<object> ExecuteLifecycleAsync(Dictionary<string, object?> arguments, ToolExecutionContext context, CancellationToken cancellationToken)
    {
        var sub = GetStringArg(arguments, "lifecycle_subaction")
            ?? throw new ArgumentException("'lifecycle_subaction' is required for knowledge lifecycle (stats|promotions|stale|scoring).");
        var filter = BuildKnowledgeListFilter(arguments);
        var days = GetIntArg(arguments, "lifecycle_days") ?? 30;
        return sub.ToLowerInvariant() switch
        {
            "stats" => await _lifecycleService.KnowledgeStatsAsync(context.ProjectId, cancellationToken),
            "promotions" => await _lifecycleService.KnowledgePromotionCandidatesAsync(context.ProjectId, filter, cancellationToken),
            "stale" => await _lifecycleService.KnowledgeStaleAsync(context.ProjectId, days, filter, cancellationToken),
            "scoring" => await ExecuteKnowledgeScoringAsync(arguments, context, cancellationToken),
            _ => throw new ArgumentException($"Unknown knowledge lifecycle sub-action: '{sub}'. Use stats|promotions|stale|scoring."),
        };
    }

    private async Task<object> ExecuteKnowledgeScoringAsync(Dictionary<string, object?> arguments, ToolExecutionContext context, CancellationToken cancellationToken)
    {
        var category = GetStringArg(arguments, "category")
            ?? throw new ArgumentException("'category' is required for knowledge lifecycle scoring.");
        var key = GetStringArg(arguments, "key")
            ?? throw new ArgumentException("'key' is required for knowledge lifecycle scoring.");
        var scoring = await _lifecycleService.KnowledgeScoringAsync(context.ProjectId, category, key, cancellationToken);
        if (scoring is null)
        {
            throw new ArgumentException($"No knowledge entry found for {category}:{key}.");
        }
        return scoring;
    }

    /// <summary>
    /// Imports knowledge entries from a <c>ctx memory export</c> payload.
    /// Honors <c>overwrite</c> to either skip or replace existing entries.
    /// </summary>
    private async Task<object> ExecuteImportAsync(Dictionary<string, object?> arguments, ToolExecutionContext context, CancellationToken cancellationToken)
    {
        var raw = arguments.TryGetValue("import_payload", out var payload) ? payload : null;
        if (raw is null)
        {
            throw new ArgumentException("'import_payload' is required for knowledge import.");
        }
        var payloadJson = raw as string ?? System.Text.Json.JsonSerializer.Serialize(raw);
        var payloadDoc = System.Text.Json.JsonDocument.Parse(payloadJson);
        var root = payloadDoc.RootElement;
        if (!root.TryGetProperty("memories", out var memoriesElement) || memoriesElement.ValueKind != System.Text.Json.JsonValueKind.Array)
        {
            throw new ArgumentException("Import payload must contain a 'memories' array.");
        }
        var overwrite = GetBoolArg(arguments, "overwrite") ?? false;
        var added = 0;
        var updated = 0;
        var skipped = 0;
        var failed = 0;
        var errors = new List<string>();

        foreach (var mem in memoriesElement.EnumerateArray())
        {
            try
            {
                var category = mem.TryGetProperty("category", out var catEl) && catEl.ValueKind == System.Text.Json.JsonValueKind.String
                    ? catEl.GetString() ?? string.Empty
                    : string.Empty;
                var key = mem.TryGetProperty("key", out var keyEl) && keyEl.ValueKind == System.Text.Json.JsonValueKind.String
                    ? keyEl.GetString() ?? string.Empty
                    : string.Empty;
                if (string.IsNullOrEmpty(category) || string.IsNullOrEmpty(key))
                {
                    failed++;
                    errors.Add("memory entry missing 'category' or 'key'");
                    continue;
                }
                var value = mem.TryGetProperty("value", out var valEl) && valEl.ValueKind == System.Text.Json.JsonValueKind.String
                    ? valEl.GetString() ?? string.Empty
                    : string.Empty;
                var confidence = mem.TryGetProperty("confidence", out var confEl) && confEl.ValueKind == System.Text.Json.JsonValueKind.Number
                    ? (float)confEl.GetDouble()
                    : 1.0f;
                var sourceType = mem.TryGetProperty("source_type", out var stEl) && stEl.ValueKind == System.Text.Json.JsonValueKind.String
                    ? stEl.GetString() ?? "import"
                    : "import";
                var sourceScope = mem.TryGetProperty("source_scope", out var ssEl) && ssEl.ValueKind == System.Text.Json.JsonValueKind.String
                    ? ssEl.GetString() ?? string.Empty
                    : string.Empty;

                var existing = await _knowledgeService.GetFactAsync(context.ProjectId, category, key, cancellationToken);
                if (existing is not null && !overwrite)
                {
                    skipped++;
                    continue;
                }

                if (existing is not null && overwrite)
                {
                    await _knowledgeService.RemoveAsync(context.ProjectId, category, key, cancellationToken);
                }

                await _knowledgeService.RememberAsync(context.ProjectId, category, key, value, confidence, sourceType, sourceScope, null, cancellationToken);
                if (existing is not null)
                {
                    updated++;
                }
                else
                {
                    added++;
                }
            }
            catch (Exception ex)
            {
                failed++;
                errors.Add(ex.Message);
            }
        }

        return new
        {
            added,
            updated,
            skipped,
            failed,
            errors,
        };
    }

    /// <summary>
    /// Extracts a string argument from the arguments dictionary.
    /// </summary>
    private static string? GetStringArg(Dictionary<string, object?> arguments, string key)
    {
        if (!arguments.TryGetValue(key, out var value) || value is null)
        {
            return null;
        }
        if (value is string s)
        {
            return s;
        }
        if (value is JsonElement { ValueKind: JsonValueKind.String } json)
        {
            return json.GetString();
        }
        return value.ToString();
    }

    /// <summary>
    /// Extracts an int argument from the arguments dictionary.
    /// </summary>
    private static int? GetIntArg(Dictionary<string, object?> arguments, string key)
    {
        if (!arguments.TryGetValue(key, out var value) || value is null)
        {
            return null;
        }
        return value switch
        {
            int i => i,
            long l => (int)l,
            double d => (int)d,
            float f => (int)f,
            decimal dec => (int)dec,
            JsonElement { ValueKind: JsonValueKind.Number } json when json.TryGetInt32(out var parsed) => parsed,
            JsonElement { ValueKind: JsonValueKind.String } json when int.TryParse(json.GetString(), out var parsedStr) => parsedStr,
            _ when int.TryParse(value.ToString(), out var parsed) => parsed,
            _ => null,
        };
    }

    /// <summary>
    /// Extracts a timestamp argument from the arguments dictionary.
    /// </summary>
    private static DateTimeOffset? GetDateTimeOffsetArg(Dictionary<string, object?> arguments, string key)
    {
        if (!arguments.TryGetValue(key, out var value) || value is null)
        {
            return null;
        }
        if (value is DateTimeOffset dto) return dto;
        if (value is DateTime dt) return new DateTimeOffset(DateTime.SpecifyKind(dt, DateTimeKind.Utc));
        if (value is JsonElement json)
        {
            return json.ValueKind switch
            {
                JsonValueKind.String when DateTimeOffset.TryParse(json.GetString(), out var parsed) => parsed,
                JsonValueKind.Number when json.TryGetInt64(out var unix) => DateTimeOffset.FromUnixTimeSeconds(unix),
                _ => null,
            };
        }
        return DateTimeOffset.TryParse(value.ToString(), out var fallback) ? fallback : null;
    }

    /// <summary>
    /// Extracts a bool argument from the arguments dictionary.
    /// </summary>
    private static bool? GetBoolArg(Dictionary<string, object?> arguments, string key)
    {
        if (!arguments.TryGetValue(key, out var value) || value is null)
        {
            return null;
        }

        return value switch
        {
            bool b => b,
            string s when bool.TryParse(s, out var parsed) => parsed,
            _ => null,
        };
    }

    /// <summary>
    /// Extracts a float argument from the arguments dictionary.
    /// </summary>
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
