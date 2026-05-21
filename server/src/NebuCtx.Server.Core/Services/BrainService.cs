namespace NebuCtx.Server.Core.Services;

using Microsoft.Extensions.Logging;

using NebuCtx.Storage;

/// <summary>
/// Brain memory service. Provides project-scoped memory operations
/// for the ctx_brain tool family (status, store, recall).
/// </summary>
public sealed class BrainService
{
    private readonly IBrainStore _brainStore;
    private readonly KnowledgeService _knowledgeService;
    private readonly ILogger<BrainService> _logger;

    /// <summary>
    /// Initializes the brain service.
    /// </summary>
    /// <param name="brainStore">Brain memory store.</param>
    /// <param name="knowledgeService">Knowledge service used for public projection refresh.</param>
    /// <param name="logger">Logger for brain operations.</param>
    public BrainService(IBrainStore brainStore, KnowledgeService knowledgeService, ILogger<BrainService> logger)
    {
        _brainStore = brainStore;
        _knowledgeService = knowledgeService;
        _logger = logger;
    }

    /// <summary>
    /// Gets the brain status for a project.
    /// </summary>
    /// <param name="projectId">Project identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Brain status payload.</returns>
    public async Task<Dictionary<string, object?>> GetStatusAsync(string projectId, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Getting brain status for project {ProjectId}", projectId);
        var status = await _brainStore.GetStatusAsync(projectId, cancellationToken);
        return status ?? new Dictionary<string, object?> { ["project_id"] = projectId, ["entry_count"] = 0, ["active_fact_count"] = 0 };
    }

    /// <summary>
    /// Stores a memory entry for a project.
    /// </summary>
    /// <param name="projectId">Project identifier.</param>
    /// <param name="key">Memory key.</param>
    /// <param name="value">Memory value.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task StoreAsync(string projectId, string key, string value, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new ArgumentException("Memory key cannot be empty.", nameof(key));
        }

        _logger.LogInformation("Storing brain entry '{Key}' for project {ProjectId}", key, projectId);
        await _brainStore.StoreAsync(projectId, key, value, cancellationToken);
    }

    /// <summary>
    /// Stores or updates a canonical brain fact and refreshes the public memory projection.
    /// </summary>
    /// <param name="projectId">Project identifier.</param>
    /// <param name="entry">Brain fact entry.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task StoreFactAsync(string projectId, BrainEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entry.Key);
        entry.ProjectId = projectId;
        if (string.IsNullOrWhiteSpace(entry.LogicalKey))
        {
            entry.LogicalKey = NormalizeToken(entry.Key);
        }

        if (string.IsNullOrWhiteSpace(entry.PromotionIdentity))
        {
            entry.PromotionIdentity = $"brain:{NormalizeToken(projectId)}:{NormalizeToken(entry.LogicalKey)}";
        }

        if (entry.CreatedAt == default)
        {
            entry.CreatedAt = DateTimeOffset.UtcNow;
        }

        entry.UpdatedAt = DateTimeOffset.UtcNow;
        if (string.IsNullOrWhiteSpace(entry.LifecycleStatus))
        {
            entry.LifecycleStatus = "current";
        }

        await ApplySupersessionAsync(projectId, entry, cancellationToken);

        await _brainStore.StoreFactAsync(entry, cancellationToken);
        await ProjectToKnowledgeAsync(projectId, entry, cancellationToken);
    }

    /// <summary>
    /// Recalls memory entries matching a query.
    /// </summary>
    /// <param name="projectId">Project identifier.</param>
    /// <param name="query">Search query.</param>
    /// <param name="limit">Maximum results.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Matching brain entries.</returns>
    public async Task<IReadOnlyList<BrainEntry>> RecallAsync(string projectId, string query, int limit = 10, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Recalling brain entries for project {ProjectId} with query '{Query}'", projectId, query);

        var overscan = Math.Max(limit * 4, 24);
        var entries = await _brainStore.RecallAsync(projectId, query, overscan, cancellationToken);
        var reranked = RerankEntries(entries, query, limit);
        if (reranked.Count > 0)
        {
            return reranked;
        }

        var allEntries = await _brainStore.ListAllAsync(projectId, 200, cancellationToken);
        return RerankEntries(allEntries, query, limit);
    }

    /// <summary>
    /// Deletes a specific brain entry by key.
    /// </summary>
    /// <param name="projectId">Project identifier.</param>
    /// <param name="key">Brain entry key.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True when the entry existed and was deleted.</returns>
    public Task<bool> DeleteAsync(string projectId, string key, CancellationToken cancellationToken = default)
        => _brainStore.DeleteAsync(projectId, key, cancellationToken);

    /// <summary>
    /// Re-ranks brain entries with lightweight token scoring so natural-language recall is more forgiving.
    /// </summary>
    private static IReadOnlyList<BrainEntry> RerankEntries(IEnumerable<BrainEntry> entries, string query, int limit)
    {
        var profile = CreateSearchProfile(query);
        if (profile.Terms.Count == 0)
        {
            return [];
        }

        return entries
            .Select(entry => new { Entry = entry, Score = ScoreEntry(entry, profile) })
            .Where(item => item.Score > 0f)
            .OrderByDescending(item => item.Score)
            .ThenByDescending(item => item.Entry.CreatedAt)
            .Take(limit)
            .Select(item => item.Entry)
            .ToList();
    }

    /// <summary>
    /// Scores one brain entry against the normalized query.
    /// </summary>
    private static float ScoreEntry(BrainEntry entry, SearchProfile profile)
    {
        var haystack = NormalizeSearchText($"{entry.Key} {entry.Value} {entry.Kind} {entry.Category} {entry.Evidence}");
        var exactTerms = haystack.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var exactHits = profile.Terms.Count(term => exactTerms.Contains(term, StringComparer.Ordinal));
        var partialHits = profile.Terms.Count(term => haystack.Contains(term, StringComparison.Ordinal));
        var phraseHit = !string.IsNullOrWhiteSpace(profile.Normalized)
            && haystack.Contains(profile.Normalized, StringComparison.Ordinal)
            ? 1f
            : 0f;
        if (exactHits == 0 && partialHits == 0 && phraseHit == 0f)
        {
            return 0f;
        }

        var tokenCount = Math.Max(1, profile.Terms.Count);
        var score = (exactHits / (float)tokenCount) * 0.65f
            + (partialHits / (float)tokenCount) * 0.2f
            + phraseHit * 0.15f;
        if (profile.RecentIntent)
        {
            var ageDays = Math.Max(0d, (DateTimeOffset.UtcNow - entry.CreatedAt).TotalDays);
            score += ageDays switch
            {
                <= 1d => 0.25f,
                <= 7d => 0.12f,
                <= 30d => 0.05f,
                _ => 0f,
            };
        }

        return score;
    }

    /// <summary>
    /// Builds a normalized query profile for brain recall.
    /// </summary>
    private static SearchProfile CreateSearchProfile(string query)
    {
        var trimmed = query.Trim();
        var sanitized = trimmed.Length <= 220
            ? trimmed
            : trimmed.Split('\n').Reverse().Select(line => line.Trim()).FirstOrDefault(line => line.Length is >= 12 and <= 220)
                ?? trimmed[^220..].Trim();
        var normalized = NormalizeSearchText(sanitized);
        var terms = normalized
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(term => term.Length >= 2)
            .Where(term => !IsStopword(term))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var recentIntent = query.Contains("yesterday", StringComparison.OrdinalIgnoreCase)
            || query.Contains("today", StringComparison.OrdinalIgnoreCase)
            || query.Contains("latest", StringComparison.OrdinalIgnoreCase)
            || query.Contains("recent", StringComparison.OrdinalIgnoreCase)
            || query.Contains("fixes", StringComparison.OrdinalIgnoreCase)
            || query.Contains("changes", StringComparison.OrdinalIgnoreCase);
        return new SearchProfile(normalized, terms, recentIntent);
    }

    /// <summary>
    /// Lowercases text and replaces punctuation with spaces for stable matching.
    /// </summary>
    private static string NormalizeSearchText(string value)
        => new(value.Select(ch => char.IsLetterOrDigit(ch) ? char.ToLowerInvariant(ch) : ' ').ToArray());

    /// <summary>
    /// Drops low-signal natural-language filler terms.
    /// </summary>
    private static bool IsStopword(string term)
        => term is "the" or "and" or "for" or "with" or "from" or "that" or "this" or "what" or "when" or "where" or "which" or "were" or "have" or "about" or "into" or "then" or "than" or "just" or "does" or "did" or "our" or "your" or "yesterday" or "today" or "latest" or "recent" or "changes" or "change" or "fixes" or "fixed" or "work" or "worked";

    /// <summary>
    /// Lightweight normalized query profile.
    /// </summary>
    private sealed record SearchProfile(string Normalized, IReadOnlyList<string> Terms, bool RecentIntent);

    private async Task ProjectToKnowledgeAsync(string projectId, BrainEntry entry, CancellationToken cancellationToken)
    {
        if (string.Equals(entry.LifecycleStatus, "invalidated", StringComparison.OrdinalIgnoreCase)
            || string.Equals(entry.LifecycleStatus, "legacy", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var category = string.IsNullOrWhiteSpace(entry.Category) ? entry.Kind : entry.Category;
        var sourceType = string.IsNullOrWhiteSpace(entry.SourceType) ? "brain" : entry.SourceType;
        var sourceScope = string.IsNullOrWhiteSpace(entry.SourceScope) ? projectId : entry.SourceScope;
        await _knowledgeService.RememberAsync(
            projectId,
            category,
            entry.Key,
            entry.Value,
            entry.Confidence,
            sourceType,
            sourceScope,
            entry.PromotionIdentity,
            cancellationToken);
    }

    private async Task ApplySupersessionAsync(string projectId, BrainEntry entry, CancellationToken cancellationToken)
    {
        if (!string.Equals(entry.LifecycleStatus, "current", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(entry.LogicalKey))
        {
            return;
        }

        var existing = await _brainStore.ListAllAsync(projectId, 500, cancellationToken);
        foreach (var prior in existing)
        {
            if (!string.Equals(prior.LogicalKey, entry.LogicalKey, StringComparison.OrdinalIgnoreCase)
                || string.Equals(prior.PromotionIdentity, entry.PromotionIdentity, StringComparison.Ordinal)
                || !string.Equals(prior.LifecycleStatus, "current", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            prior.LifecycleStatus = string.Equals(entry.Kind, "correction", StringComparison.OrdinalIgnoreCase)
                ? "invalidated"
                : "superseded";
            prior.SupersededBy = string.Equals(prior.LifecycleStatus, "superseded", StringComparison.OrdinalIgnoreCase)
                ? entry.PromotionIdentity
                : prior.SupersededBy;
            prior.InvalidatedBy = string.Equals(prior.LifecycleStatus, "invalidated", StringComparison.OrdinalIgnoreCase)
                ? entry.PromotionIdentity
                : prior.InvalidatedBy;
            prior.UpdatedAt = entry.UpdatedAt;
            await _brainStore.StoreFactAsync(prior, cancellationToken);
        }
    }

    private static string NormalizeToken(string value)
    {
        var lowered = value.Trim().ToLowerInvariant();
        var chars = lowered.Select(ch => char.IsLetterOrDigit(ch) ? ch : '-').ToArray();
        var normalized = new string(chars);
        while (normalized.Contains("--", StringComparison.Ordinal))
        {
            normalized = normalized.Replace("--", "-", StringComparison.Ordinal);
        }

        return string.IsNullOrWhiteSpace(normalized.Trim('-')) ? "unknown" : normalized.Trim('-');
    }
}
