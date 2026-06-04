namespace NebuCtx.Tools.Brain;

using System.Globalization;
using System.Text.Json;

using NebuCtx.Contracts.Mcp;
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
    private readonly MemoryLifecycleService _lifecycleService;

    /// <summary>
    /// Initializes the brain tool handler.
    /// </summary>
    /// <param name="brainService">Brain service for memory operations.</param>
    /// <param name="lifecycleService">Lifecycle service for stats/promotions/stale/scoring.</param>
    public BrainToolHandler(BrainService brainService, MemoryLifecycleService lifecycleService)
    {
        _brainService = brainService;
        _lifecycleService = lifecycleService;
    }

    /// <inheritdoc />
    public string Name => "ctx_brain";

    /// <inheritdoc />
    public string Description => "Project-scoped canonical fact memory. Actions: status, store, ingest, recall, list, lifecycle, forget.";

    /// <inheritdoc />
    public Dictionary<string, object?> InputSchema => new()
    {
        ["type"] = "object",
        ["properties"] = new Dictionary<string, object?>
        {
            ["action"] = new Dictionary<string, object?>
            {
                ["type"] = "string",
                ["description"] = "Action to perform: status, store, ingest, recall, list, lifecycle, forget, import",
                ["enum"] = new[] { "status", "store", "ingest", "recall", "list", "lifecycle", "forget", "import" },
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
                ["description"] = "Maximum results for recall (default: 10) or list (default: 20)",
            },
            ["offset"] = new Dictionary<string, object?>
            {
                ["type"] = "integer",
                ["description"] = "Pagination offset for list (default: 0)",
            },
            ["sort_field"] = new Dictionary<string, object?>
            {
                ["type"] = "string",
                ["description"] = "Sort field for list: created, updated, confidence, key, relevance (default: relevance).",
            },
            ["sort_direction"] = new Dictionary<string, object?>
            {
                ["type"] = "string",
                ["description"] = "Sort direction for list: asc or desc (default: desc).",
            },
            ["lifecycle_status"] = new Dictionary<string, object?>
            {
                ["type"] = "string",
                ["description"] = "Filter list results by lifecycle_status (current, stale, superseded, archived, legacy).",
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
                ["description"] = "Relative time window for list filtering, e.g. 1h, 7d, 2w, 1m, 1y. Combined with created_after when both are supplied.",
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
            "status" => await _brainService.GetStatusAsync(context.ProjectId, cancellationToken),
            "store" => await ExecuteStoreAsync(arguments, context, cancellationToken),
            "ingest" => await ExecuteIngestAsync(arguments, context, cancellationToken),
            "recall" => await ExecuteRecallAsync(arguments, context, cancellationToken),
            "list" => await ExecuteListAsync(arguments, context, cancellationToken),
            "lifecycle" => await ExecuteLifecycleAsync(arguments, context, cancellationToken),
            "forget" => await ExecuteForgetAsync(arguments, context, cancellationToken),
            "import" => await ExecuteImportAsync(arguments, context, cancellationToken),
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
            CreatedAt = GetDateTimeOffsetArg(arguments, "created_at") ?? default,
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
    /// Imports brain entries from a <c>ctx memory export</c> payload.
    /// </summary>
    private async Task<object> ExecuteImportAsync(Dictionary<string, object?> arguments, ToolExecutionContext context, CancellationToken cancellationToken)
    {
        var raw = arguments.TryGetValue("import_payload", out var payload) ? payload : null;
        if (raw is null)
        {
            throw new ArgumentException("'import_payload' is required for brain import.");
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
                var key = mem.TryGetProperty("key", out var keyEl) && keyEl.ValueKind == System.Text.Json.JsonValueKind.String
                    ? keyEl.GetString() ?? string.Empty
                    : string.Empty;
                if (string.IsNullOrEmpty(key))
                {
                    failed++;
                    errors.Add("memory entry missing 'key'");
                    continue;
                }
                var value = mem.TryGetProperty("value", out var valEl) && valEl.ValueKind == System.Text.Json.JsonValueKind.String
                    ? valEl.GetString() ?? string.Empty
                    : string.Empty;
                var existing = await _brainService.RecallAsync(context.ProjectId, key, 1, cancellationToken);
                var isExisting = existing.Any(e => string.Equals(e.Key, key, StringComparison.Ordinal));
                if (isExisting && !overwrite)
                {
                    skipped++;
                    continue;
                }
                if (isExisting && overwrite)
                {
                    await _brainService.DeleteAsync(context.ProjectId, key, cancellationToken);
                }
                await _brainService.StoreAsync(context.ProjectId, key, value, cancellationToken);
                if (isExisting) updated++; else added++;
            }
            catch (Exception ex)
            {
                failed++;
                errors.Add(ex.Message);
            }
        }

        return new { added, updated, skipped, failed, errors };
    }

    /// <summary>
    /// Executes the list action — returns brain memory entries that match the supplied
    /// filter in a consistent envelope that matches <c>memory-browsing</c>.
    /// </summary>
    private async Task<object> ExecuteListAsync(Dictionary<string, object?> arguments, ToolExecutionContext context, CancellationToken cancellationToken)
    {
        var filter = BuildListFilter(arguments);
        var (entries, total) = await _brainService.ListAsync(context.ProjectId, filter, cancellationToken);
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
            ?? throw new ArgumentException("'lifecycle_subaction' is required for brain lifecycle (stats|promotions|stale|scoring).");
        var filter = BuildListFilter(arguments);
        var days = GetIntArg(arguments, "lifecycle_days") ?? 30;
        return sub.ToLowerInvariant() switch
        {
            "stats" => await _lifecycleService.BrainStatsAsync(context.ProjectId, cancellationToken),
            "promotions" => await _lifecycleService.BrainPromotionCandidatesAsync(context.ProjectId, filter, cancellationToken),
            "stale" => await _lifecycleService.BrainStaleAsync(context.ProjectId, days, filter, cancellationToken),
            "scoring" => await ExecuteBrainScoringAsync(arguments, context, cancellationToken),
            _ => throw new ArgumentException($"Unknown brain lifecycle sub-action: '{sub}'. Use stats|promotions|stale|scoring."),
        };
    }

    private async Task<object> ExecuteBrainScoringAsync(Dictionary<string, object?> arguments, ToolExecutionContext context, CancellationToken cancellationToken)
    {
        var key = GetStringArg(arguments, "key")
            ?? throw new ArgumentException("'key' is required for brain lifecycle scoring.");
        var scoring = await _lifecycleService.BrainScoringAsync(context.ProjectId, key, cancellationToken);
        if (scoring is null)
        {
            throw new ArgumentException($"No brain entry found for key '{key}'.");
        }
        return scoring;
    }

    /// <summary>
    /// Maps a brain entry into the shared memory listing projection.
    /// </summary>
    private static MemoryListItem ProjectToListItem(BrainEntry entry)
    {
        var value = entry.Value ?? string.Empty;
        if (value.Length > MemoryListItem.MaxValueLength)
        {
            value = string.Concat(value.AsSpan(0, MemoryListItem.MaxValueLength - 1), "…");
        }

        return new MemoryListItem
        {
            Key = entry.Key,
            Category = string.IsNullOrEmpty(entry.Category) ? entry.Kind : entry.Category,
            Value = value,
            Confidence = entry.Confidence,
            SourceType = entry.SourceType,
            SourceScope = entry.SourceScope,
            CreatedAt = entry.CreatedAt,
            UpdatedAt = entry.UpdatedAt,
            LifecycleStatus = entry.LifecycleStatus,
        };
    }

    /// <summary>
    /// Echoes the active filter values back to the caller in a stable shape.
    /// Always includes a <c>active</c> marker so consumers can distinguish
    /// "no filters" from "missing echo".
    /// </summary>
    private static Dictionary<string, object?> FiltersEcho(MemoryListFilter filter)
    {
        var dict = new Dictionary<string, object?> { ["active"] = false };
        if (!string.IsNullOrEmpty(filter.Category))
        {
            dict["active"] = true;
            dict["category"] = filter.Category;
        }
        if (!string.IsNullOrEmpty(filter.SourceType))
        {
            dict["active"] = true;
            dict["source_type"] = filter.SourceType;
        }
        if (!string.IsNullOrEmpty(filter.LifecycleStatus))
        {
            dict["active"] = true;
            dict["lifecycle_status"] = filter.LifecycleStatus;
        }
        if (filter.CreatedAfter.HasValue)
        {
            dict["active"] = true;
            dict["created_after"] = filter.CreatedAfter.Value;
        }
        if (filter.CreatedBefore.HasValue)
        {
            dict["active"] = true;
            dict["created_before"] = filter.CreatedBefore.Value;
        }
        if (!string.IsNullOrEmpty(filter.PromotedFromSession))
        {
            dict["active"] = true;
            dict["promoted_from_session"] = filter.PromotedFromSession;
        }
        if (!string.IsNullOrEmpty(filter.PromotedFromBrainKey))
        {
            dict["active"] = true;
            dict["promoted_from_brain_key"] = filter.PromotedFromBrainKey;
        }
        return dict;
    }

    /// <summary>
    /// Builds a <see cref="MemoryListFilter"/> from the tool argument dictionary, applying
    /// default sort/limit/offset and translating the <c>--since</c> shorthand.
    /// </summary>
    internal static MemoryListFilter BuildListFilter(Dictionary<string, object?> arguments)
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
            if (!string.IsNullOrWhiteSpace(raw) && TryParseRelativeTime(raw, out var since))
            {
                filter.CreatedAfter = since;
            }
        }
        return filter;
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
    /// Parses compact relative-time shorthand such as "1h", "7d", "2w", "3m", "1y"
    /// into an absolute UTC timestamp. Returns false on invalid input.
    /// </summary>
    internal static bool TryParseRelativeTime(string value, out DateTimeOffset result)
    {
        result = default;
        if (string.IsNullOrWhiteSpace(value) || value.Length < 2)
        {
            return false;
        }

        var unit = value[^1];
        var numberPart = value[..^1];
        if (!double.TryParse(numberPart, NumberStyles.Float, CultureInfo.InvariantCulture, out var amount) || amount <= 0)
        {
            return false;
        }

        result = unit switch
        {
            'h' or 'H' => DateTimeOffset.UtcNow.AddHours(-amount),
            'd' or 'D' => DateTimeOffset.UtcNow.AddDays(-amount),
            'w' or 'W' => DateTimeOffset.UtcNow.AddDays(-7 * amount),
            'm' or 'M' => DateTimeOffset.UtcNow.AddDays(-30 * amount),
            'y' or 'Y' => DateTimeOffset.UtcNow.AddDays(-365 * amount),
            _ => default,
        };
        return result != default;
    }

    /// <summary>
    /// Extracts a string argument from the arguments dictionary.
    /// </summary>
    private static string? GetStringArg(Dictionary<string, object?> arguments, string key)
    {
        return arguments.TryGetValue(key, out var value) ? value?.ToString() : null;
    }

    /// <summary>
    /// Extracts an integer argument from the arguments dictionary.
    /// </summary>
    private static int? GetIntArg(Dictionary<string, object?> arguments, string key)
    {
        if (!arguments.TryGetValue(key, out var value) || value is null)
        {
            return null;
        }

        return value switch
        {
            int integer => integer,
            long longValue => (int)longValue,
            double dbl => (int)dbl,
            float single => (int)single,
            decimal dec => (int)dec,
            JsonElement json => json.ValueKind switch
            {
                JsonValueKind.Number when json.TryGetInt32(out var parsedInt) => parsedInt,
                JsonValueKind.String when int.TryParse(json.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedStr) => parsedStr,
                _ => null,
            },
            _ when int.TryParse(value.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) => parsed,
            _ => null,
        };
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

    /// <summary>
    /// Extracts a timestamp argument from the arguments dictionary.
    /// </summary>
    private static DateTimeOffset? GetDateTimeOffsetArg(Dictionary<string, object?> arguments, string key)
    {
        if (!arguments.TryGetValue(key, out var value) || value is null)
        {
            return null;
        }

        return value switch
        {
            DateTimeOffset timestamp => timestamp,
            JsonElement json when json.ValueKind == JsonValueKind.String
                && DateTimeOffset.TryParse(json.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsedJson) => parsedJson,
            string raw when DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed) => parsed,
            _ => null,
        };
    }
}
