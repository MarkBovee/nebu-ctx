namespace NebuCtx.Server.Core.Services;

using NebuCtx.Contracts.Mcp;
using NebuCtx.Storage;

/// <summary>
/// Read-only lifecycle inspector. Produces the payloads that back the
/// <c>ctx_brain lifecycle</c> and <c>ctx_knowledge lifecycle</c> subcommands
/// (<c>stats</c>, <c>promotions</c>, <c>stale</c>, <c>scoring</c>).
/// Memory writes still flow through <c>BrainService</c> and
/// <c>KnowledgeService</c>; this service only derives insight.
/// </summary>
public sealed class MemoryLifecycleService
{
    private const int DefaultStaleDays = 30;
    private const float AutoPromotionThreshold = 1.0f;
    private const float StaleReviewThreshold = 0.4f;

    private readonly IBrainStore _brainStore;
    private readonly IKnowledgeStore _knowledgeStore;

    /// <summary>
    /// Initializes the lifecycle inspector.
    /// </summary>
    /// <param name="brainStore">Brain store for brain-side lifecycle payloads.</param>
    /// <param name="knowledgeStore">Knowledge store for canonical lifecycle payloads.</param>
    public MemoryLifecycleService(IBrainStore brainStore, IKnowledgeStore knowledgeStore)
    {
        _brainStore = brainStore;
        _knowledgeStore = knowledgeStore;
    }

    /// <summary>
    /// Returns aggregate lifecycle stats for the brain layer of a project.
    /// </summary>
    public async Task<Dictionary<string, object?>> BrainStatsAsync(string projectId, CancellationToken cancellationToken = default)
    {
        var entries = await _brainStore.ListAllAsync(projectId, 1000, cancellationToken);
        return BuildStats(entries, entry => entry.LifecycleStatus,
            entry => new LifecycleEntrySnapshot(
                entry.Confidence,
                entry.UpdatedAt,
                RetrievalCount: 0,
                ConfirmationCount: 0,
                LifecycleScore: ScoreBrainEntry(entry)));
    }

    /// <summary>
    /// Returns aggregate lifecycle stats for the knowledge layer of a project.
    /// </summary>
    public async Task<Dictionary<string, object?>> KnowledgeStatsAsync(string projectId, CancellationToken cancellationToken = default)
    {
        var entries = await _knowledgeStore.ListAllForProjectAsync(projectId, 1000, cancellationToken);
        return BuildStats(entries, entry => entry.LifecycleStatus,
            entry => new LifecycleEntrySnapshot(
                entry.Confidence,
                entry.LastRetrievedAt ?? entry.UpdatedAt,
                entry.RetrievalCount,
                entry.ConfirmationCount,
                entry.LifecycleScore));
    }

    /// <summary>
    /// Returns brain entries that look ready for promotion to canonical knowledge.
    /// </summary>
    public async Task<Dictionary<string, object?>> BrainPromotionCandidatesAsync(string projectId, MemoryListFilter filter, CancellationToken cancellationToken = default)
    {
        filter ??= new MemoryListFilter();
        var entries = await _brainStore.ListAllAsync(projectId, 1000, cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var candidates = entries
            .Where(IsBrainPromotionCandidate)
            .OrderByDescending(entry => ScoreBrainEntry(entry))
            .ThenByDescending(entry => entry.UpdatedAt)
            .Take(Math.Clamp(filter.Limit, 1, MemoryListFilter.MaxLimit))
            .ToList();
        return new Dictionary<string, object?>
        {
            ["candidates"] = candidates.Select(entry => new
            {
                key = entry.Key,
                category = entry.Category,
                confidence = entry.Confidence,
                retrieval_count = 0,
                confirmation_count = 0,
                lifecycle_score = ScoreBrainEntry(entry),
                updated_at = entry.UpdatedAt,
            }),
            ["count"] = candidates.Count,
            ["threshold_used"] = AutoPromotionThreshold,
            ["eligible_total"] = entries.Count(IsBrainPromotionCandidate),
            ["type"] = "brain",
        };
    }

    /// <summary>
    /// Returns canonical knowledge facts that are most valuable and recently used.
    /// </summary>
    public async Task<Dictionary<string, object?>> KnowledgePromotionCandidatesAsync(string projectId, MemoryListFilter filter, CancellationToken cancellationToken = default)
    {
        filter ??= new MemoryListFilter();
        var entries = await _knowledgeStore.ListAllForProjectAsync(projectId, 1000, cancellationToken);
        var eligible = entries
            .Where(e => e.LifecycleScore >= AutoPromotionThreshold)
            .OrderByDescending(e => e.LifecycleScore)
            .ThenByDescending(e => e.UpdatedAt)
            .Take(Math.Clamp(filter.Limit, 1, MemoryListFilter.MaxLimit))
            .ToList();
        return new Dictionary<string, object?>
        {
            ["candidates"] = eligible.Select(entry => new
            {
                key = entry.Key,
                category = entry.Category,
                confidence = entry.Confidence,
                retrieval_count = entry.RetrievalCount,
                confirmation_count = entry.ConfirmationCount,
                lifecycle_score = entry.LifecycleScore,
                updated_at = entry.UpdatedAt,
            }),
            ["count"] = eligible.Count,
            ["threshold_used"] = AutoPromotionThreshold,
            ["eligible_total"] = entries.Count(e => e.LifecycleScore >= AutoPromotionThreshold),
            ["type"] = "knowledge",
        };
    }

    /// <summary>
    /// Returns brain entries approaching staleness, ordered by last activity.
    /// </summary>
    public async Task<Dictionary<string, object?>> BrainStaleAsync(string projectId, int days, MemoryListFilter filter, CancellationToken cancellationToken = default)
    {
        filter ??= new MemoryListFilter();
        var entries = await _brainStore.ListAllAsync(projectId, 1000, cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var threshold = TimeSpan.FromDays(Math.Max(1, days));
        var stale = entries
            .Where(entry => now - entry.UpdatedAt >= threshold && IsStaleStatus(entry.LifecycleStatus))
            .OrderBy(entry => entry.UpdatedAt)
            .Take(Math.Clamp(filter.Limit, 1, MemoryListFilter.MaxLimit))
            .ToList();
        return new Dictionary<string, object?>
        {
            ["stale_memories"] = stale.Select(entry => new
            {
                key = entry.Key,
                category = entry.Category,
                last_activity_at = entry.UpdatedAt,
                days_since_activity = (int)Math.Round((now - entry.UpdatedAt).TotalDays),
                lifecycle_status = entry.LifecycleStatus,
            }),
            ["count"] = stale.Count,
            ["days_threshold_used"] = days,
            ["eligible_total"] = entries.Count(entry => now - entry.UpdatedAt >= threshold && IsStaleStatus(entry.LifecycleStatus)),
            ["type"] = "brain",
        };
    }

    /// <summary>
    /// Returns canonical knowledge facts that have not been accessed recently.
    /// </summary>
    public async Task<Dictionary<string, object?>> KnowledgeStaleAsync(string projectId, int days, MemoryListFilter filter, CancellationToken cancellationToken = default)
    {
        filter ??= new MemoryListFilter();
        var entries = await _knowledgeStore.ListAllForProjectAsync(projectId, 1000, cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var threshold = TimeSpan.FromDays(Math.Max(1, days));
        var stale = entries
            .Where(entry =>
            {
                var last = entry.LastRetrievedAt ?? entry.UpdatedAt;
                return now - last >= threshold && IsStaleStatus(entry.LifecycleStatus);
            })
            .OrderBy(entry => entry.LastRetrievedAt ?? entry.UpdatedAt)
            .Take(Math.Clamp(filter.Limit, 1, MemoryListFilter.MaxLimit))
            .ToList();
        return new Dictionary<string, object?>
        {
            ["stale_memories"] = stale.Select(entry => new
            {
                key = entry.Key,
                category = entry.Category,
                last_activity_at = entry.LastRetrievedAt ?? entry.UpdatedAt,
                days_since_activity = (int)Math.Round((now - (entry.LastRetrievedAt ?? entry.UpdatedAt)).TotalDays),
                lifecycle_status = entry.LifecycleStatus,
            }),
            ["count"] = stale.Count,
            ["days_threshold_used"] = days,
            ["eligible_total"] = entries.Count(entry =>
            {
                var last = entry.LastRetrievedAt ?? entry.UpdatedAt;
                return now - last >= threshold && IsStaleStatus(entry.LifecycleStatus);
            }),
            ["type"] = "knowledge",
        };
    }

    /// <summary>
    /// Returns a detailed scoring breakdown for a specific brain entry.
    /// </summary>
    public async Task<Dictionary<string, object?>?> BrainScoringAsync(string projectId, string key, CancellationToken cancellationToken = default)
    {
        var entries = await _brainStore.ListAllAsync(projectId, 1000, cancellationToken);
        var entry = entries.FirstOrDefault(e => string.Equals(e.Key, key, StringComparison.Ordinal));
        if (entry is null)
        {
            return null;
        }
        return BuildScoringPayload(entry.Key, entry.Category, entry.LifecycleStatus, ScoreBrainEntry(entry), BrainScoreFactors(entry));
    }

    /// <summary>
    /// Returns a detailed scoring breakdown for a specific knowledge fact.
    /// </summary>
    public async Task<Dictionary<string, object?>?> KnowledgeScoringAsync(string projectId, string category, string key, CancellationToken cancellationToken = default)
    {
        var entry = await _knowledgeStore.GetFactAsync(projectId, category, key, cancellationToken);
        if (entry is null)
        {
            return null;
        }
        return BuildScoringPayload($"{entry.Category}:{entry.Key}", entry.Category, entry.LifecycleStatus, entry.LifecycleScore, KnowledgeScoreFactors(entry));
    }

    /// <summary>
    /// Computes a deterministic lifecycle score for a brain entry using its
    /// confidence, age, and updated timestamp.
    /// </summary>
    internal static float ScoreBrainEntry(BrainEntry entry)
    {
        var confidence = Math.Clamp(entry.Confidence, 0f, 1f);
        var ageBoost = 0f;
        var days = Math.Max(0d, (DateTimeOffset.UtcNow - entry.UpdatedAt).TotalDays);
        if (days <= 7) ageBoost = 0.10f;
        else if (days <= 30) ageBoost = 0.05f;
        var score = confidence + ageBoost;
        return Math.Clamp(score, 0f, 1.5f);
    }

    private static bool IsBrainPromotionCandidate(BrainEntry entry) =>
        string.Equals(entry.LifecycleStatus, "current", StringComparison.OrdinalIgnoreCase)
        && entry.Confidence >= AutoPromotionThreshold - 0.05f
        && (DateTimeOffset.UtcNow - entry.UpdatedAt).TotalDays <= 30;

    private static bool IsStaleStatus(string status) =>
        string.Equals(status, "current", StringComparison.OrdinalIgnoreCase)
        || string.Equals(status, "stale", StringComparison.OrdinalIgnoreCase);

    private static Dictionary<string, object?> BuildStats<T>(
        IReadOnlyList<T> entries,
        Func<T, string> statusSelector,
        Func<T, LifecycleEntrySnapshot> snapshotSelector)
    {
        var statusCounts = entries
            .GroupBy(statusSelector)
            .ToDictionary(g => g.Key, g => g.Count());
        var statusAverages = entries
            .GroupBy(statusSelector)
            .ToDictionary(g => g.Key, g =>
            {
                var snaps = entries.Where(e => statusSelector(e) == g.Key).Select(snapshotSelector).ToList();
                return new Dictionary<string, object?>
                {
                    ["avg_confidence"] = snaps.Count == 0 ? 0d : snaps.Average(s => (double)s.Confidence),
                    ["avg_retrieval_count"] = snaps.Count == 0 ? 0d : snaps.Average(s => (double)s.RetrievalCount),
                    ["avg_confirmation_count"] = snaps.Count == 0 ? 0d : snaps.Average(s => (double)s.ConfirmationCount),
                    ["avg_lifecycle_score"] = snaps.Count == 0 ? 0d : snaps.Average(s => (double)s.LifecycleScore),
                };
            });
        var scoreDistribution = entries
            .Select(snapshotSelector)
            .GroupBy(s => ScoreBucket(s.LifecycleScore))
            .ToDictionary(g => g.Key, g => g.Count());
        return new Dictionary<string, object?>
        {
            ["total_memories"] = entries.Count,
            ["status_counts"] = statusCounts,
            ["status_averages"] = statusAverages,
            ["score_distribution"] = scoreDistribution,
        };
    }

    private static string ScoreBucket(float score) => score switch
    {
        < 0.25f => "0.00-0.25",
        < 0.5f => "0.25-0.50",
        < 0.75f => "0.50-0.75",
        < 1.0f => "0.75-1.00",
        < 1.25f => "1.00-1.25",
        _ => "1.25-1.50",
    };

    private static Dictionary<string, object?> BrainScoreFactors(BrainEntry entry)
    {
        var confidence = Math.Clamp(entry.Confidence, 0f, 1f);
        var days = Math.Max(0d, (DateTimeOffset.UtcNow - entry.UpdatedAt).TotalDays);
        var ageBoost = days <= 7 ? 0.10f : days <= 30 ? 0.05f : 0f;
        return new Dictionary<string, object?>
        {
            ["confidence"] = confidence,
            ["age_boost"] = ageBoost,
            ["updated_at"] = entry.UpdatedAt,
            ["days_since_update"] = (int)Math.Round(days),
        };
    }

    private static Dictionary<string, object?> KnowledgeScoreFactors(KnowledgeEntry entry)
    {
        var confirmationBoost = Math.Min(0.3f, Math.Max(0, entry.ConfirmationCount - 1) * 0.05f);
        var retrievalBoost = Math.Min(0.2f, entry.RetrievalCount * 0.01f);
        var recencyBoost = 0f;
        if (entry.LastRetrievedAt.HasValue)
        {
            var hours = Math.Max(0.0, (DateTimeOffset.UtcNow - entry.LastRetrievedAt.Value).TotalHours);
            recencyBoost = (float)Math.Max(0.0, 0.2 - Math.Min(0.2, hours / 168.0 * 0.2));
        }
        return new Dictionary<string, object?>
        {
            ["confidence"] = entry.Confidence,
            ["confirmation_boost"] = confirmationBoost,
            ["retrieval_boost"] = retrievalBoost,
            ["recency_boost"] = recencyBoost,
            ["confirmation_count"] = entry.ConfirmationCount,
            ["retrieval_count"] = entry.RetrievalCount,
            ["last_retrieved_at"] = entry.LastRetrievedAt,
        };
    }

    private static Dictionary<string, object?> BuildScoringPayload(string key, string category, string status, float score, Dictionary<string, object?> factors)
    {
        return new Dictionary<string, object?>
        {
            ["key"] = key,
            ["category"] = category,
            ["status"] = status,
            ["current_score"] = score,
            ["factors"] = factors,
            ["thresholds"] = new Dictionary<string, object?>
            {
                ["auto_promotion"] = AutoPromotionThreshold,
                ["stale_review"] = StaleReviewThreshold,
                ["stale_days"] = DefaultStaleDays,
            },
            ["recommendation"] = score >= AutoPromotionThreshold ? "promote" : score >= StaleReviewThreshold ? "review" : "archive",
        };
    }

    private readonly record struct LifecycleEntrySnapshot(
        float Confidence,
        DateTimeOffset LastActivity,
        int RetrievalCount,
        int ConfirmationCount,
        float LifecycleScore);
}
