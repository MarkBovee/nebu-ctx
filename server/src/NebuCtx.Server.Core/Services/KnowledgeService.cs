namespace NebuCtx.Server.Core.Services;

using System.Globalization;
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
    /// <param name="sourceType">Source type that produced the fact.</param>
    /// <param name="sourceScope">Source scope used for deterministic promotion identity.</param>
    /// <param name="promotionIdentity">Optional precomputed deterministic promotion identity.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task RememberAsync(
        string projectId,
        string category,
        string key,
        string value,
        float confidence = 1.0f,
        string sourceType = "remember",
        string? sourceScope = null,
        string? promotionIdentity = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(category)) throw new ArgumentException("Category is required.", nameof(category));
        if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("Key is required.", nameof(key));

        _logger.LogInformation("Storing knowledge fact [{Category}/{Key}] for project {ProjectId}", category, key, projectId);

        var now = DateTimeOffset.UtcNow;
        var normalizedCategory = NormalizeToken(category);
        var normalizedKey = NormalizeToken(key);
        var normalizedLogicalKey = DeriveLogicalKey(normalizedCategory, normalizedKey);
        var normalizedSourceScope = string.IsNullOrWhiteSpace(sourceScope) ? projectId : sourceScope.Trim();
        var existing = await _knowledgeStore.GetFactAsync(projectId, category, key, cancellationToken);
        var identity = string.IsNullOrWhiteSpace(promotionIdentity) ? existing?.PromotionIdentity : promotionIdentity;
        if (string.IsNullOrWhiteSpace(identity))
        {
            identity = BuildPromotionIdentity(sourceType, normalizedSourceScope, normalizedCategory, normalizedLogicalKey);
        }

        var history = existing?.History.Select(CloneHistoryEntry).ToList() ?? [];
        var confirmationCount = 1;
        var createdAt = now;
        var lifecycleStatus = "current";

        if (existing is not null)
        {
            createdAt = existing.CreatedAt == default ? now : existing.CreatedAt;

            if (string.Equals(existing.Value, value, StringComparison.Ordinal))
            {
                confirmationCount = Math.Max(1, existing.ConfirmationCount) + 1;
            }
            else
            {
                history.Add(new KnowledgeHistoryEntry
                {
                    Value = existing.Value,
                    Confidence = existing.Confidence,
                PromotionIdentity = string.IsNullOrWhiteSpace(existing.PromotionIdentity) ? identity : existing.PromotionIdentity,
                    SourceType = existing.SourceType,
                    SourceScope = existing.SourceScope,
                    ValidFrom = existing.CreatedAt == default ? existing.LastConfirmedAt ?? createdAt : existing.CreatedAt,
                    SupersededAt = now,
                });
            }

            lifecycleStatus = existing.LifecycleStatus;
        }

        var clampedConfidence = Math.Clamp(confidence, 0f, 1f);
        var lifecycleScore = ComputeLifecycleScore(clampedConfidence, confirmationCount, now, existing?.LastRetrievedAt, existing?.RetrievalCount ?? 0);

        await _knowledgeStore.UpsertFactAsync(new KnowledgeEntry
        {
            ProjectId = projectId,
            Category = category,
            Key = key,
            Value = value,
            Confidence = clampedConfidence,
            CreatedAt = createdAt,
            UpdatedAt = now,
            LogicalKey = normalizedLogicalKey,
            PromotionIdentity = string.IsNullOrWhiteSpace(existing?.PromotionIdentity) ? identity : existing.PromotionIdentity,
            SourceType = sourceType,
            SourceScope = normalizedSourceScope,
            LifecycleStatus = lifecycleStatus,
            LifecycleScore = lifecycleScore,
            ConfirmationCount = confirmationCount,
            LastConfirmedAt = now,
            RetrievalCount = existing?.RetrievalCount ?? 0,
            LastRetrievedAt = existing?.LastRetrievedAt,
            History = history,
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

        return RecallAndRefreshAsync(projectId, category, query, limit, cancellationToken);
    }

    /// <summary>
    /// Gets a status summary: total fact count and category breakdown.
    /// </summary>
    /// <param name="projectId">Project identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Status payload including fact count and category list.</returns>
    public async Task<Dictionary<string, object?>> GetStatusAsync(string projectId, CancellationToken cancellationToken = default)
    {
        await UpkeepAsync(projectId, cancellationToken);
        var factCount = await _knowledgeStore.GetFactCountAsync(projectId, cancellationToken);
        var categories = await _knowledgeStore.GetCategoriesAsync(projectId, cancellationToken);
        var allFacts = await _knowledgeStore.ListAllForProjectAsync(projectId, 1000, cancellationToken);
        var currentFacts = allFacts.Count(entry => string.Equals(entry.LifecycleStatus, "current", StringComparison.OrdinalIgnoreCase));
        var historyEntries = allFacts.Sum(entry => entry.History.Count);

        return new Dictionary<string, object?>
        {
            ["project_id"] = projectId,
            ["fact_count"] = factCount,
            ["current_fact_count"] = currentFacts,
            ["non_current_fact_count"] = Math.Max(0, factCount - currentFacts),
            ["history_entry_count"] = historyEntries,
            ["average_lifecycle_score"] = currentFacts == 0
                ? 0f
                : allFacts.Where(entry => string.Equals(entry.LifecycleStatus, "current", StringComparison.OrdinalIgnoreCase)).Average(entry => entry.LifecycleScore),
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

            await RememberAsync(
                projectId,
                item.Category,
                item.Key,
                item.Value,
                item.Confidence,
                item.SourceType,
                item.SourceScope,
                item.PromotionIdentity,
                cancellationToken);
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
        var upkeep = await UpkeepAsync(projectId, cancellationToken);
        result["upkeep"] = upkeep;
        return result;
    }

    /// <summary>
    /// Recomputes lifecycle ranking and upkeep metadata for canonical project knowledge.
    /// </summary>
    /// <param name="projectId">Project identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Summary of lifecycle upkeep work.</returns>
    public async Task<Dictionary<string, object?>> UpkeepAsync(string projectId, CancellationToken cancellationToken = default)
    {
        var facts = await _knowledgeStore.ListAllForProjectAsync(projectId, 1000, cancellationToken);
        if (facts.Count == 0)
        {
            return new Dictionary<string, object?>
            {
                ["project_id"] = projectId,
                ["rescored"] = 0,
                ["stale_marked"] = 0,
                ["top_wakeup"] = Array.Empty<object>(),
            };
        }

        var now = DateTimeOffset.UtcNow;
        var rescored = 0;
        var staleMarked = 0;

        foreach (var fact in facts)
        {
            var status = fact.LifecycleStatus;
            if (string.IsNullOrWhiteSpace(status))
            {
                status = "current";
            }

            if (string.Equals(status, "current", StringComparison.OrdinalIgnoreCase)
                && fact.LastConfirmedAt.HasValue
                && (now - fact.LastConfirmedAt.Value).TotalDays > 30
                && fact.ConfirmationCount <= 1
                && fact.RetrievalCount == 0)
            {
                status = "stale";
                staleMarked++;
            }

            fact.LifecycleStatus = status;
            fact.LifecycleScore = ComputeLifecycleScore(fact.Confidence, fact.ConfirmationCount, now, fact.LastRetrievedAt, fact.RetrievalCount);
            fact.UpdatedAt = now;
            await _knowledgeStore.UpsertFactAsync(fact, cancellationToken);
            rescored++;
        }

        var wakeup = facts
            .Where(fact => string.Equals(fact.LifecycleStatus, "current", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(fact => fact.LifecycleScore)
            .ThenByDescending(fact => fact.Confidence)
            .ThenByDescending(fact => fact.LastConfirmedAt)
            .Take(8)
            .Select(fact => new
            {
                category = fact.Category,
                key = fact.Key,
                value = fact.Value,
                lifecycle_score = fact.LifecycleScore,
            })
            .ToArray();

        return new Dictionary<string, object?>
        {
            ["project_id"] = projectId,
            ["rescored"] = rescored,
            ["stale_marked"] = staleMarked,
            ["top_wakeup"] = wakeup,
        };
    }

    /// <summary>
    /// Builds a bounded hosted wake-up snapshot from canonical project knowledge.
    /// </summary>
    /// <param name="projectId">Project identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Bounded wake-up payload and selected entries.</returns>
    public async Task<Dictionary<string, object?>> BuildWakeupAsync(string projectId, CancellationToken cancellationToken = default)
    {
        await UpkeepAsync(projectId, cancellationToken);
        var facts = await _knowledgeStore.ListAllForProjectAsync(projectId, 1000, cancellationToken);
        var selected = facts
            .Where(fact => string.Equals(fact.LifecycleStatus, "current", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(fact => fact.LifecycleScore)
            .ThenByDescending(fact => fact.Confidence)
            .ThenByDescending(fact => fact.LastConfirmedAt)
            .Take(8)
            .ToArray();

        var lines = selected
            .Select(fact => $"[{fact.Category}] {fact.Key}: {fact.Value} (score: {fact.LifecycleScore:F2})")
            .ToArray();
        var briefing = lines.Length == 0
            ? "No hosted memory available for wake-up."
            : $"WAKE-UP BRIEFING:\n{string.Join("\n", lines)}";

        return new Dictionary<string, object?>
        {
            ["project_id"] = projectId,
            ["briefing"] = briefing,
            ["selected_count"] = selected.Length,
            ["budget"] = 8,
            ["entries"] = selected.Select(fact => new
            {
                category = fact.Category,
                key = fact.Key,
                value = fact.Value,
                lifecycle_score = fact.LifecycleScore,
                confidence = fact.Confidence,
            }).ToArray(),
        };
    }

    /// <summary>
    /// Analyzes canonical project memory for duplicate, stale, or junk-like cleanup candidates.
    /// </summary>
    /// <param name="projectId">Project identifier.</param>
    /// <param name="apply">Whether triage recommendations should be applied.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Triage preview or apply summary.</returns>
    public async Task<Dictionary<string, object?>> TriageAsync(string projectId, bool apply, CancellationToken cancellationToken = default)
    {
        var facts = (await _knowledgeStore.ListAllForProjectAsync(projectId, 1000, cancellationToken)).ToList();
        var duplicateGroups = facts
            .GroupBy(fact => $"{NormalizeToken(fact.Category)}:{NormalizeToken(fact.Value)}", StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.OrderByDescending(fact => fact.LifecycleScore).ThenByDescending(fact => fact.Confidence).ToArray())
            .ToArray();

        var junkCandidates = facts
            .Where(IsLikelyJunkCandidate)
            .Select(fact => new
            {
                category = fact.Category,
                key = fact.Key,
                value = fact.Value,
                reason = "suspected_junk_or_demo",
            })
            .ToArray();

        var staleCandidates = facts
            .Where(fact => string.Equals(fact.LifecycleStatus, "stale", StringComparison.OrdinalIgnoreCase))
            .Select(fact => new
            {
                category = fact.Category,
                key = fact.Key,
                value = fact.Value,
                reason = "stale",
            })
            .ToArray();

        var applied = new List<object>();
        if (apply)
        {
            foreach (var group in duplicateGroups)
            {
                var keep = group[0];
                foreach (var duplicate in group.Skip(1))
                {
                    duplicate.LifecycleStatus = "merged";
                    duplicate.UpdatedAt = DateTimeOffset.UtcNow;
                    await _knowledgeStore.UpsertFactAsync(duplicate, cancellationToken);
                    applied.Add(new
                    {
                        action = "merge",
                        keep = keep.Key,
                        duplicate = duplicate.Key,
                    });
                }
            }

            foreach (var fact in facts.Where(IsLikelyJunkCandidate))
            {
                fact.LifecycleStatus = "junk";
                fact.UpdatedAt = DateTimeOffset.UtcNow;
                await _knowledgeStore.UpsertFactAsync(fact, cancellationToken);
                applied.Add(new
                {
                    action = "mark_junk",
                    key = fact.Key,
                });
            }
        }

        return new Dictionary<string, object?>
        {
            ["project_id"] = projectId,
            ["mode"] = apply ? "apply" : "preview",
            ["duplicate_groups"] = duplicateGroups.Select(group => new
            {
                reason = "duplicate_value",
                entries = group.Select(fact => new
                {
                    category = fact.Category,
                    key = fact.Key,
                    value = fact.Value,
                    lifecycle_status = fact.LifecycleStatus,
                    lifecycle_score = fact.LifecycleScore,
                }).ToArray(),
            }).ToArray(),
            ["stale_candidates"] = staleCandidates,
            ["junk_candidates"] = junkCandidates,
            ["applied_actions"] = applied.ToArray(),
        };
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
                SourceType = "consolidate",
                SourceScope = state.SessionId,
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
                SourceType = "consolidate",
                SourceScope = state.SessionId,
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
                SourceType = "consolidate",
                SourceScope = state.SessionId,
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
                SourceType = GetJsonStringOrDefault(element, "source_type", "promote"),
                SourceScope = GetJsonString(element, "source_scope"),
                PromotionIdentity = GetJsonString(element, "promotion_identity"),
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
                SourceType = itemDict.TryGetValue("source_type", out var sourceType) ? sourceType?.ToString() ?? "promote" : "promote",
                SourceScope = itemDict.TryGetValue("source_scope", out var sourceScope) ? sourceScope?.ToString() ?? string.Empty : string.Empty,
                PromotionIdentity = itemDict.TryGetValue("promotion_identity", out var promotionIdentity) ? promotionIdentity?.ToString() ?? string.Empty : string.Empty,
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
    /// Reads a string property from a JSON object, falling back to a default value.
    /// </summary>
    private static string GetJsonStringOrDefault(JsonElement element, string propertyName, string defaultValue)
    {
        var value = GetJsonString(element, propertyName);
        return string.IsNullOrWhiteSpace(value) ? defaultValue : value;
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

    /// <summary>
    /// Recalls knowledge and refreshes retrieval lifecycle metadata for the returned facts.
    /// </summary>
    private async Task<IReadOnlyList<KnowledgeEntry>> RecallAndRefreshAsync(string projectId, string? category, string query, int limit, CancellationToken cancellationToken)
    {
        var entries = await _knowledgeStore.RecallAsync(projectId, category, query, limit, cancellationToken);
        if (entries.Count == 0)
        {
            return entries;
        }

        var now = DateTimeOffset.UtcNow;
        foreach (var entry in entries)
        {
            entry.RetrievalCount++;
            entry.LastRetrievedAt = now;
            entry.LifecycleScore = ComputeLifecycleScore(entry.Confidence, entry.ConfirmationCount, now, entry.LastRetrievedAt, entry.RetrievalCount);
            await _knowledgeStore.UpsertFactAsync(entry, cancellationToken);
        }

        return entries;
    }

    /// <summary>
    /// Derives a stable logical key from category and key.
    /// </summary>
    public static string DeriveLogicalKey(string category, string key)
    {
        return string.IsNullOrWhiteSpace(category)
            ? NormalizeToken(key)
            : $"{NormalizeToken(category)}:{NormalizeToken(key)}";
    }

    /// <summary>
    /// Builds a deterministic promotion identity for replay-safe knowledge ingestion.
    /// </summary>
    public static string BuildPromotionIdentity(string sourceType, string sourceScope, string category, string logicalKey)
    {
        return $"{NormalizeToken(sourceType)}:{NormalizeToken(sourceScope)}:{NormalizeToken(category)}:{NormalizeToken(logicalKey)}";
    }

    /// <summary>
    /// Computes a simple lifecycle score from confidence, confirmations, and retrieval history.
    /// </summary>
    public static float ComputeLifecycleScore(float confidence, int confirmationCount, DateTimeOffset referenceTime, DateTimeOffset? lastRetrievedAt, int retrievalCount)
    {
        var confirmationBoost = Math.Min(0.3f, Math.Max(0, confirmationCount - 1) * 0.05f);
        var retrievalBoost = Math.Min(0.2f, retrievalCount * 0.01f);
        var recencyBoost = 0f;
        if (lastRetrievedAt.HasValue)
        {
            var hours = Math.Max(0.0, (referenceTime - lastRetrievedAt.Value).TotalHours);
            recencyBoost = (float)Math.Max(0.0, 0.2 - Math.Min(0.2, hours / 168.0 * 0.2));
        }

        return Math.Clamp(confidence + confirmationBoost + retrievalBoost + recencyBoost, 0f, 1.5f);
    }

    /// <summary>
    /// Normalizes lifecycle identity tokens so retries produce the same identifiers.
    /// </summary>
    public static string NormalizeToken(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "unknown";
        }

        var trimmed = value.Trim().ToLowerInvariant();
        var chars = trimmed
            .Select(ch => char.IsLetterOrDigit(ch) ? ch : '-')
            .ToArray();
        var normalized = new string(chars);
        while (normalized.Contains("--", StringComparison.Ordinal))
        {
            normalized = normalized.Replace("--", "-", StringComparison.Ordinal);
        }

        return normalized.Trim('-');
    }

    /// <summary>
    /// Clones a retained historical lifecycle entry so existing state remains immutable.
    /// </summary>
    private static KnowledgeHistoryEntry CloneHistoryEntry(KnowledgeHistoryEntry entry)
    {
        return new KnowledgeHistoryEntry
        {
            Value = entry.Value,
            Confidence = entry.Confidence,
            PromotionIdentity = entry.PromotionIdentity,
            SourceType = entry.SourceType,
            SourceScope = entry.SourceScope,
            ValidFrom = entry.ValidFrom,
            SupersededAt = entry.SupersededAt,
        };
    }

    /// <summary>
    /// Flags obvious low-value memories without deleting them automatically.
    /// </summary>
    private static bool IsLikelyJunkCandidate(KnowledgeEntry fact)
    {
        var value = fact.Value.ToLowerInvariant();
        var key = fact.Key.ToLowerInvariant();
        return value.Contains("demo", StringComparison.Ordinal)
            || value.Contains("placeholder", StringComparison.Ordinal)
            || value.Contains("test data", StringComparison.Ordinal)
            || key.Contains("demo", StringComparison.Ordinal)
            || key.Contains("placeholder", StringComparison.Ordinal)
            || key.Contains("test", StringComparison.Ordinal) && fact.Confidence < 0.8f;
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

    /// <summary>Source type that produced the candidate.</summary>
    public string SourceType { get; set; } = "promote";

    /// <summary>Source scope used to derive deterministic replay identity.</summary>
    public string SourceScope { get; set; } = string.Empty;

    /// <summary>Optional precomputed deterministic promotion identity supplied by the caller.</summary>
    public string PromotionIdentity { get; set; } = string.Empty;
}
