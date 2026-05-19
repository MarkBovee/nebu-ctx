namespace NebuCtx.Server.Core.Services;

using NebuCtx.Storage;
using Microsoft.Extensions.Logging;
using System.Text.Json;

/// <summary>
/// Knowledge service. Provides project-scoped categorized fact operations
/// for the ctx_knowledge tool (remember, recall, status, remove, categories).
/// </summary>
public sealed class KnowledgeService
{
    private readonly IKnowledgeStore _knowledgeStore;
    private readonly ISessionStore _sessionStore;
    private readonly ILogger<KnowledgeService> _logger;

    /// <summary>
    /// Initializes the knowledge service.
    /// </summary>
    /// <param name="knowledgeStore">Knowledge persistence store.</param>
    /// <param name="sessionStore">Session store used for promotion and consolidation.</param>
    /// <param name="logger">Logger for knowledge operations.</param>
    public KnowledgeService(IKnowledgeStore knowledgeStore, ISessionStore sessionStore, ILogger<KnowledgeService> logger)
    {
        _knowledgeStore = knowledgeStore;
        _sessionStore = sessionStore;
        _logger = logger;
    }

    /// <summary>
    /// Stores or updates a categorized knowledge fact.
    /// </summary>
    /// <param name="projectId">Project identifier.</param>
    /// <param name="category">Logical grouping for the fact.</param>
    /// <param name="key">Unique key within the category.</param>
    /// <param name="value">Fact value.</param>
    /// <param name="confidence">Confidence score between 0 and 1. Defaults to 1.0.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task RememberAsync(string projectId, string category, string key, string value, float confidence = 1.0f, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(category)) throw new ArgumentException("Category is required.", nameof(category));
        if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("Key is required.", nameof(key));

        _logger.LogInformation("Storing knowledge fact [{Category}/{Key}] for project {ProjectId}", category, key, projectId);

        await _knowledgeStore.UpsertFactAsync(new KnowledgeEntry
        {
            ProjectId = projectId,
            Category = category,
            Key = key,
            Value = value,
            Confidence = Math.Clamp(confidence, 0f, 1f),
        }, cancellationToken);
    }

    /// <summary>
    /// Searches knowledge facts by text query, optionally filtered by category.
    /// </summary>
    /// <param name="projectId">Project identifier.</param>
    /// <param name="category">Optional category filter.</param>
    /// <param name="query">Text search query.</param>
    /// <param name="limit">Maximum results. Defaults to 10.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Matching knowledge entries ordered by confidence descending.</returns>
    public Task<IReadOnlyList<KnowledgeEntry>> RecallAsync(string projectId, string? category, string query, int limit = 10, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query)) throw new ArgumentException("Query is required for recall.", nameof(query));

        _logger.LogDebug("Recalling knowledge for project {ProjectId} query='{Query}' category='{Category}'", projectId, query, category ?? "*");
        return _knowledgeStore.RecallAsync(projectId, category, query, limit, cancellationToken);
    }

    /// <summary>
    /// Gets a status summary: total fact count and category breakdown.
    /// </summary>
    /// <param name="projectId">Project identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Status payload including fact count and category list.</returns>
    public async Task<Dictionary<string, object?>> GetStatusAsync(string projectId, CancellationToken cancellationToken = default)
    {
        var factCount = await _knowledgeStore.GetFactCountAsync(projectId, cancellationToken);
        var categories = await _knowledgeStore.GetCategoriesAsync(projectId, cancellationToken);

        return new Dictionary<string, object?>
        {
            ["project_id"] = projectId,
            ["fact_count"] = factCount,
            ["categories"] = categories.Select(c => new { category = c.Category, count = c.Count }).ToList(),
        };
    }

    /// <summary>
    /// Lists all categories with their fact counts.
    /// </summary>
    /// <param name="projectId">Project identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Category name → fact count pairs.</returns>
    public Task<IReadOnlyList<(string Category, int Count)>> GetCategoriesAsync(string projectId, CancellationToken cancellationToken = default)
    {
        return _knowledgeStore.GetCategoriesAsync(projectId, cancellationToken);
    }

    /// <summary>
    /// Removes a specific knowledge fact.
    /// </summary>
    /// <param name="projectId">Project identifier.</param>
    /// <param name="category">Fact category.</param>
    /// <param name="key">Fact key.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if the fact was found and deleted.</returns>
    public async Task<bool> RemoveAsync(string projectId, string category, string key, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Removing knowledge fact [{Category}/{Key}] for project {ProjectId}", category, key, projectId);
        return await _knowledgeStore.RemoveFactAsync(projectId, category, key, cancellationToken);
    }

    /// <summary>
    /// Promotes explicit memory candidates into canonical project knowledge.
    /// </summary>
    /// <param name="projectId">Project identifier.</param>
    /// <param name="items">Candidate facts to persist.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Promotion summary payload.</returns>
    public async Task<Dictionary<string, object?>> PromoteAsync(string projectId, IReadOnlyList<KnowledgePromotionItem> items, CancellationToken cancellationToken = default)
    {
        if (items.Count == 0)
        {
            return new Dictionary<string, object?>
            {
                ["promoted"] = 0,
                ["skipped"] = 0,
                ["project_id"] = projectId,
            };
        }

        var promoted = 0;
        var skipped = 0;

        foreach (var item in items)
        {
            if (string.IsNullOrWhiteSpace(item.Category) || string.IsNullOrWhiteSpace(item.Key) || string.IsNullOrWhiteSpace(item.Value))
            {
                skipped++;
                continue;
            }

            await RememberAsync(projectId, item.Category, item.Key, item.Value, item.Confidence, cancellationToken);
            promoted++;
        }

        _logger.LogInformation("Promoted {Promoted} knowledge item(s) for project {ProjectId}", promoted, projectId);
        return new Dictionary<string, object?>
        {
            ["promoted"] = promoted,
            ["skipped"] = skipped,
            ["project_id"] = projectId,
        };
    }

    /// <summary>
    /// Consolidates the latest persisted session into canonical project knowledge.
    /// </summary>
    /// <param name="projectId">Project identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Consolidation result payload.</returns>
    public async Task<Dictionary<string, object?>> ConsolidateAsync(string projectId, CancellationToken cancellationToken = default)
    {
        var state = await _sessionStore.LoadLatestAsync(projectId, cancellationToken);
        if (state is null)
        {
            return new Dictionary<string, object?>
            {
                ["promoted"] = 0,
                ["project_id"] = projectId,
                ["message"] = "No persisted session to consolidate.",
            };
        }

        var items = BuildPromotionItems(state);
        var result = await PromoteAsync(projectId, items, cancellationToken);
        result["session_id"] = state.SessionId;
        result["summary"] = BuildSessionSummary(state);
        return result;
    }

    /// <summary>
    /// Parses promotion items from a raw tool argument value.
    /// </summary>
    /// <param name="rawItems">Raw tool argument payload.</param>
    /// <returns>Parsed promotion items.</returns>
    public static IReadOnlyList<KnowledgePromotionItem> ParsePromotionItems(object? rawItems)
    {
        if (rawItems is null)
        {
            return [];
        }

        if (rawItems is JsonElement jsonElement)
        {
            return ParsePromotionItemsFromJsonElement(jsonElement);
        }

        if (rawItems is IEnumerable<object?> objectItems)
        {
            var parsed = new List<KnowledgePromotionItem>();
            foreach (var item in objectItems)
            {
                var candidate = ParsePromotionItem(item);
                if (candidate is not null)
                {
                    parsed.Add(candidate);
                }
            }

            return parsed;
        }

        return [];
    }

    /// <summary>
    /// Builds promotion candidates from a persisted session state.
    /// </summary>
    private static IReadOnlyList<KnowledgePromotionItem> BuildPromotionItems(CloudSessionState state)
    {
        var items = new List<KnowledgePromotionItem>();

        for (var index = 0; index < state.Findings.Count; index++)
        {
            var finding = state.Findings[index];
            if (string.IsNullOrWhiteSpace(finding))
            {
                continue;
            }

            items.Add(new KnowledgePromotionItem
            {
                Category = "finding",
                Key = $"{state.SessionId}-finding-{index + 1}",
                Value = finding.Trim(),
                Confidence = 0.7f,
            });
        }

        for (var index = 0; index < state.Decisions.Count; index++)
        {
            var decision = state.Decisions[index];
            if (string.IsNullOrWhiteSpace(decision))
            {
                continue;
            }

            items.Add(new KnowledgePromotionItem
            {
                Category = "decision",
                Key = $"{state.SessionId}-decision-{index + 1}",
                Value = decision.Trim(),
                Confidence = 0.85f,
            });
        }

        var summary = BuildSessionSummary(state);
        if (!string.IsNullOrWhiteSpace(summary))
        {
            items.Add(new KnowledgePromotionItem
            {
                Category = "session",
                Key = $"session-{state.SessionId}",
                Value = summary,
                Confidence = 0.8f,
            });
        }

        return items;
    }

    /// <summary>
    /// Builds a durable session summary string.
    /// </summary>
    private static string BuildSessionSummary(CloudSessionState state)
    {
        var task = string.IsNullOrWhiteSpace(state.Task) ? "(no task)" : state.Task.Trim();
        return $"Session {state.SessionId}: {task} — {state.Findings.Count} findings, {state.Decisions.Count} decisions consolidated";
    }

    /// <summary>
    /// Parses promotion items from a JSON array argument.
    /// </summary>
    private static IReadOnlyList<KnowledgePromotionItem> ParsePromotionItemsFromJsonElement(JsonElement itemsElement)
    {
        if (itemsElement.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var parsed = new List<KnowledgePromotionItem>();
        foreach (var element in itemsElement.EnumerateArray())
        {
            var candidate = ParsePromotionItem(element);
            if (candidate is not null)
            {
                parsed.Add(candidate);
            }
        }

        return parsed;
    }

    /// <summary>
    /// Parses a single promotion item from a raw object.
    /// </summary>
    private static KnowledgePromotionItem? ParsePromotionItem(object? rawItem)
    {
        if (rawItem is null)
        {
            return null;
        }

        if (rawItem is JsonElement element)
        {
            if (element.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            return new KnowledgePromotionItem
            {
                Category = GetJsonString(element, "category"),
                Key = GetJsonString(element, "key"),
                Value = GetJsonString(element, "value"),
                Confidence = GetJsonFloat(element, "confidence") ?? 0.8f,
            };
        }

        if (rawItem is Dictionary<string, object?> itemDict)
        {
            return new KnowledgePromotionItem
            {
                Category = itemDict.TryGetValue("category", out var category) ? category?.ToString() ?? string.Empty : string.Empty,
                Key = itemDict.TryGetValue("key", out var key) ? key?.ToString() ?? string.Empty : string.Empty,
                Value = itemDict.TryGetValue("value", out var value) ? value?.ToString() ?? string.Empty : string.Empty,
                Confidence = itemDict.TryGetValue("confidence", out var confidence)
                    ? TryParseFloat(confidence) ?? 0.8f
                    : 0.8f,
            };
        }

        return null;
    }

    /// <summary>
    /// Reads a string property from a JSON object.
    /// </summary>
    private static string GetJsonString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString() ?? string.Empty
            : string.Empty;
    }

    /// <summary>
    /// Reads a float property from a JSON object.
    /// </summary>
    private static float? GetJsonFloat(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        return property.ValueKind switch
        {
            JsonValueKind.Number when property.TryGetSingle(out var single) => single,
            JsonValueKind.Number when property.TryGetDouble(out var dbl) => (float)dbl,
            JsonValueKind.String => TryParseFloat(property.GetString()),
            _ => null,
        };
    }

    /// <summary>
    /// Parses a float from an arbitrary object/string.
    /// </summary>
    private static float? TryParseFloat(object? value)
    {
        return value switch
        {
            float f => f,
            double d => (float)d,
            decimal m => (float)m,
            string s when float.TryParse(s, out var parsed) => parsed,
            _ => null,
        };
    }
}

/// <summary>
/// Explicit memory candidate promoted into canonical knowledge.
/// </summary>
public sealed class KnowledgePromotionItem
{
    /// <summary>Logical category for the fact.</summary>
    public string Category { get; set; } = string.Empty;

    /// <summary>Unique key within the category.</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>Fact value to persist.</summary>
    public string Value { get; set; } = string.Empty;

    /// <summary>Confidence score for the promoted fact.</summary>
    public float Confidence { get; set; } = 0.8f;
}
