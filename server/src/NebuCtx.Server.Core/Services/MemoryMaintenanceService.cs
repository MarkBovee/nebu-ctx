namespace NebuCtx.Server.Core.Services;

using Microsoft.Extensions.Logging;
using NebuCtx.Storage;

/// <summary>
/// Project-scoped maintenance service that reviews and cleans hosted brain and knowledge memory.
/// </summary>
public sealed class MemoryMaintenanceService
{
    private const float JunkFindingThreshold = 0.72f;
    private const float JunkApplyThreshold = 0.85f;

    private readonly IBrainStore _brainStore;
    private readonly IKnowledgeStore _knowledgeStore;
    private readonly KnowledgeService _knowledgeService;
    private readonly ILogger<MemoryMaintenanceService> _logger;

    /// <summary>
    /// Initializes the maintenance service.
    /// </summary>
    /// <param name="brainStore">Brain persistence store.</param>
    /// <param name="knowledgeStore">Knowledge persistence store.</param>
    /// <param name="knowledgeService">Knowledge helper service for lifecycle upkeep.</param>
    /// <param name="logger">Logger for maintenance runs.</param>
    public MemoryMaintenanceService(IBrainStore brainStore, IKnowledgeStore knowledgeStore, KnowledgeService knowledgeService, ILogger<MemoryMaintenanceService> logger)
    {
        _brainStore = brainStore;
        _knowledgeStore = knowledgeStore;
        _knowledgeService = knowledgeService;
        _logger = logger;
    }

    /// <summary>
    /// Reviews one project's hosted memory and optionally applies deterministic cleanup actions.
    /// </summary>
    /// <param name="projectId">Project identifier to inspect.</param>
    /// <param name="apply">Whether cleanup actions should be persisted.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Maintenance summary with findings and applied action counts.</returns>
    public async Task<Dictionary<string, object?>> RunAsync(string projectId, bool apply, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Running memory maintenance for project {ProjectId} (apply={Apply})", projectId, apply);

        var originalBrainEntries = await LoadBrainEntriesAsync(projectId, cancellationToken);
        var originalKnowledgeEntries = await LoadKnowledgeEntriesAsync(projectId, cancellationToken);
        var workingBrainEntries = originalBrainEntries.Select(CloneBrainEntry).ToList();
        var workingKnowledgeEntries = originalKnowledgeEntries.Select(CloneKnowledgeEntry).ToList();
        var findings = new List<MaintenanceFinding>();
        var appliedActions = new List<Dictionary<string, object?>>();

        // Remove legacy raw timeline rows before canonical fact cleanup runs.
        RemoveLegacyRawBrainEntries(workingBrainEntries, findings);

        // Normalize metadata first so later duplicate/projection logic compares stable identities.
        var now = DateTimeOffset.UtcNow;
        NormalizeBrainMetadata(projectId, workingBrainEntries, findings, now);
        NormalizeKnowledgeMetadata(projectId, workingKnowledgeEntries, findings, now);

        // Normalize formatting before duplicate clustering so punctuation/spacing variants collapse together.
        NormalizeBrainFormatting(workingBrainEntries, findings, now);
        NormalizeKnowledgeFormatting(workingKnowledgeEntries, findings, now);

        // Score likely junk quickly with deterministic heuristics and expose the confidence in the result.
        FlagBrainJunk(workingBrainEntries, findings, now);
        FlagKnowledgeJunk(workingKnowledgeEntries, findings, now);

        // Collapse deterministic duplicate groups down to one canonical entry per cluster.
        ResolveBrainDuplicates(workingBrainEntries, findings, now);
        ResolveKnowledgeDuplicates(workingKnowledgeEntries, findings, now);

        // Keep knowledge aligned with current brain facts so cleanup on brain side actually sticks.
        ReconcileKnowledgeProjection(projectId, workingBrainEntries, workingKnowledgeEntries, findings, now);

        findings = findings
            .OrderByDescending(finding => finding.Confidence)
            .ThenBy(finding => finding.Scope, StringComparer.Ordinal)
            .ThenBy(finding => finding.Kind, StringComparer.Ordinal)
            .ThenBy(finding => finding.Key, StringComparer.Ordinal)
            .ToList();

        var brainUpdates = 0;
        var knowledgeUpdates = 0;
        if (apply)
        {
            brainUpdates = await PersistBrainChangesAsync(originalBrainEntries, workingBrainEntries, appliedActions, cancellationToken);
            knowledgeUpdates = await PersistKnowledgeChangesAsync(originalKnowledgeEntries, workingKnowledgeEntries, appliedActions, cancellationToken);
        }

        Dictionary<string, object?>? upkeep = null;
        if (apply)
        {
            upkeep = await _knowledgeService.UpkeepAsync(projectId, cancellationToken);
        }

        return new Dictionary<string, object?>
        {
            ["project_id"] = projectId,
            ["mode"] = apply ? "apply" : "analyze",
            ["brain_scanned"] = originalBrainEntries.Count,
            ["knowledge_scanned"] = originalKnowledgeEntries.Count,
            ["finding_count"] = findings.Count,
            ["high_confidence_findings"] = findings.Count(finding => finding.Confidence >= 0.9f),
            ["brain_updates"] = brainUpdates,
            ["knowledge_updates"] = knowledgeUpdates,
            ["findings"] = findings.Select(finding => finding.ToPayload()).ToArray(),
            ["applied_actions"] = appliedActions.ToArray(),
            ["upkeep"] = upkeep,
        };
    }

    /// <summary>
    /// Loads all persisted brain entries for one project using the store's reported count.
    /// </summary>
    private async Task<List<BrainEntry>> LoadBrainEntriesAsync(string projectId, CancellationToken cancellationToken)
    {
        var status = await _brainStore.GetStatusAsync(projectId, cancellationToken);
        var totalEntries = ReadCount(status, "entry_count");
        if (totalEntries <= 0)
        {
            return [];
        }

        return (await _brainStore.ListAllAsync(projectId, totalEntries, cancellationToken)).Select(CloneBrainEntry).ToList();
    }

    /// <summary>
    /// Loads all persisted knowledge entries for one project using the store's reported count.
    /// </summary>
    private async Task<List<KnowledgeEntry>> LoadKnowledgeEntriesAsync(string projectId, CancellationToken cancellationToken)
    {
        var totalEntries = await _knowledgeStore.GetFactCountAsync(projectId, cancellationToken);
        if (totalEntries <= 0)
        {
            return [];
        }

        return (await _knowledgeStore.ListAllForProjectAsync(projectId, totalEntries, cancellationToken)).Select(CloneKnowledgeEntry).ToList();
    }

    /// <summary>
    /// Removes legacy raw journal-like brain rows from maintenance scope and reports them for deletion.
    /// </summary>
    private static void RemoveLegacyRawBrainEntries(List<BrainEntry> entries, List<MaintenanceFinding> findings)
    {
        var legacyEntries = entries.Where(IsLegacyRawBrainEntry).ToList();
        foreach (var entry in legacyEntries)
        {
            findings.Add(new MaintenanceFinding(
                "brain",
                "legacy_raw",
                1.0f,
                entry.Category,
                entry.Key,
                entry.Value,
                null,
                "deleted",
                "Legacy raw journal/timeline row should not remain in hosted brain memory."));
        }

        entries.RemoveAll(entry => legacyEntries.Any(legacy => string.Equals(legacy.Key, entry.Key, StringComparison.Ordinal)));
    }

    /// <summary>
    /// Fills missing brain metadata fields with deterministic defaults.
    /// </summary>
    private static void NormalizeBrainMetadata(string projectId, List<BrainEntry> entries, List<MaintenanceFinding> findings, DateTimeOffset now)
    {
        foreach (var entry in entries)
        {
            var changed = false;
            var notes = new List<string>();

            if (string.IsNullOrWhiteSpace(entry.Kind))
            {
                entry.Kind = "fact";
                changed = true;
                notes.Add("kind");
            }

            if (string.IsNullOrWhiteSpace(entry.Category))
            {
                entry.Category = "general";
                changed = true;
                notes.Add("category");
            }

            var normalizedLogicalKey = string.IsNullOrWhiteSpace(entry.LogicalKey)
                ? KnowledgeService.NormalizeToken(entry.Key)
                : KnowledgeService.NormalizeToken(entry.LogicalKey);
            if (!string.Equals(entry.LogicalKey, normalizedLogicalKey, StringComparison.Ordinal))
            {
                entry.LogicalKey = normalizedLogicalKey;
                changed = true;
                notes.Add("logical_key");
            }

            var normalizedSourceType = string.IsNullOrWhiteSpace(entry.SourceType) ? "brain" : entry.SourceType.Trim();
            if (!string.Equals(entry.SourceType, normalizedSourceType, StringComparison.Ordinal))
            {
                entry.SourceType = normalizedSourceType;
                changed = true;
                notes.Add("source_type");
            }

            var normalizedSourceScope = string.IsNullOrWhiteSpace(entry.SourceScope) ? projectId : entry.SourceScope.Trim();
            if (!string.Equals(entry.SourceScope, normalizedSourceScope, StringComparison.Ordinal))
            {
                entry.SourceScope = normalizedSourceScope;
                changed = true;
                notes.Add("source_scope");
            }

            var normalizedPromotionIdentity = string.IsNullOrWhiteSpace(entry.PromotionIdentity)
                ? $"brain:{KnowledgeService.NormalizeToken(projectId)}:{KnowledgeService.NormalizeToken(entry.LogicalKey)}"
                : entry.PromotionIdentity.Trim();
            if (!string.Equals(entry.PromotionIdentity, normalizedPromotionIdentity, StringComparison.Ordinal))
            {
                entry.PromotionIdentity = normalizedPromotionIdentity;
                changed = true;
                notes.Add("promotion_identity");
            }

            if (string.IsNullOrWhiteSpace(entry.LifecycleStatus))
            {
                entry.LifecycleStatus = "current";
                changed = true;
                notes.Add("lifecycle_status");
            }

            if (entry.CreatedAt == default)
            {
                entry.CreatedAt = now;
                changed = true;
                notes.Add("created_at");
            }

            if (changed)
            {
                entry.UpdatedAt = now;
                findings.Add(new MaintenanceFinding(
                    "brain",
                    "metadata_fix",
                    1.0f,
                    entry.Category,
                    entry.Key,
                    entry.Value,
                    null,
                    null,
                    $"Filled deterministic brain metadata: {string.Join(", ", notes)}."));
            }
        }
    }

    /// <summary>
    /// Fills missing knowledge metadata fields with deterministic defaults.
    /// </summary>
    private static void NormalizeKnowledgeMetadata(string projectId, List<KnowledgeEntry> entries, List<MaintenanceFinding> findings, DateTimeOffset now)
    {
        foreach (var entry in entries)
        {
            var changed = false;
            var notes = new List<string>();

            if (string.IsNullOrWhiteSpace(entry.Category))
            {
                entry.Category = "general";
                changed = true;
                notes.Add("category");
            }

            var normalizedLogicalKey = string.IsNullOrWhiteSpace(entry.LogicalKey)
                ? KnowledgeService.DeriveLogicalKey(entry.Category, entry.Key)
                : KnowledgeService.NormalizeToken(entry.LogicalKey);
            if (!string.Equals(entry.LogicalKey, normalizedLogicalKey, StringComparison.Ordinal))
            {
                entry.LogicalKey = normalizedLogicalKey;
                changed = true;
                notes.Add("logical_key");
            }

            var normalizedSourceType = string.IsNullOrWhiteSpace(entry.SourceType) ? "remember" : entry.SourceType.Trim();
            if (!string.Equals(entry.SourceType, normalizedSourceType, StringComparison.Ordinal))
            {
                entry.SourceType = normalizedSourceType;
                changed = true;
                notes.Add("source_type");
            }

            var normalizedSourceScope = string.IsNullOrWhiteSpace(entry.SourceScope) ? projectId : entry.SourceScope.Trim();
            if (!string.Equals(entry.SourceScope, normalizedSourceScope, StringComparison.Ordinal))
            {
                entry.SourceScope = normalizedSourceScope;
                changed = true;
                notes.Add("source_scope");
            }

            var normalizedPromotionIdentity = string.IsNullOrWhiteSpace(entry.PromotionIdentity)
                ? KnowledgeService.BuildPromotionIdentity(entry.SourceType, entry.SourceScope, entry.Category, entry.LogicalKey)
                : entry.PromotionIdentity.Trim();
            if (!string.Equals(entry.PromotionIdentity, normalizedPromotionIdentity, StringComparison.Ordinal))
            {
                entry.PromotionIdentity = normalizedPromotionIdentity;
                changed = true;
                notes.Add("promotion_identity");
            }

            if (string.IsNullOrWhiteSpace(entry.LifecycleStatus))
            {
                entry.LifecycleStatus = "current";
                changed = true;
                notes.Add("lifecycle_status");
            }

            if (entry.CreatedAt == default)
            {
                entry.CreatedAt = now;
                changed = true;
                notes.Add("created_at");
            }

            if (changed)
            {
                entry.UpdatedAt = now;
                findings.Add(new MaintenanceFinding(
                    "knowledge",
                    "metadata_fix",
                    1.0f,
                    entry.Category,
                    entry.Key,
                    entry.Value,
                    null,
                    null,
                    $"Filled deterministic knowledge metadata: {string.Join(", ", notes)}."));
            }
        }
    }

    /// <summary>
    /// Collapses obvious formatting noise in brain facts.
    /// </summary>
    private static void NormalizeBrainFormatting(List<BrainEntry> entries, List<MaintenanceFinding> findings, DateTimeOffset now)
    {
        foreach (var entry in entries)
        {
            var normalizedValue = NormalizeDisplayText(entry.Value);
            if (string.Equals(entry.Value, normalizedValue, StringComparison.Ordinal))
            {
                continue;
            }

            findings.Add(new MaintenanceFinding(
                "brain",
                "formatting",
                0.93f,
                entry.Category,
                entry.Key,
                entry.Value,
                normalizedValue,
                null,
                "Collapsed whitespace and trimmed display formatting."));
            entry.Value = normalizedValue;
            entry.UpdatedAt = now;
        }
    }

    /// <summary>
    /// Collapses obvious formatting noise in knowledge facts while preserving history on apply.
    /// </summary>
    private static void NormalizeKnowledgeFormatting(List<KnowledgeEntry> entries, List<MaintenanceFinding> findings, DateTimeOffset now)
    {
        foreach (var entry in entries)
        {
            var normalizedValue = NormalizeDisplayText(entry.Value);
            if (string.Equals(entry.Value, normalizedValue, StringComparison.Ordinal))
            {
                continue;
            }

            findings.Add(new MaintenanceFinding(
                "knowledge",
                "formatting",
                0.93f,
                entry.Category,
                entry.Key,
                entry.Value,
                normalizedValue,
                null,
                "Collapsed whitespace and trimmed display formatting."));
            ApplyKnowledgeValueChange(entry, normalizedValue, now);
        }
    }

    /// <summary>
    /// Flags likely junk brain facts and downgrades them to junk when confidence is high enough.
    /// </summary>
    private static void FlagBrainJunk(List<BrainEntry> entries, List<MaintenanceFinding> findings, DateTimeOffset now)
    {
        foreach (var entry in entries)
        {
            var confidence = EstimateJunkConfidence(entry.Key, entry.Value, entry.Category, entry.Confidence);
            if (confidence < JunkFindingThreshold)
            {
                continue;
            }

            findings.Add(new MaintenanceFinding(
                "brain",
                "junk",
                confidence,
                entry.Category,
                entry.Key,
                entry.Value,
                null,
                confidence >= JunkApplyThreshold ? "junk" : null,
                "Deterministic junk signals matched demo, placeholder, or test-like content."));

            if (confidence >= JunkApplyThreshold)
            {
                entry.LifecycleStatus = "junk";
                entry.UpdatedAt = now;
            }
        }
    }

    /// <summary>
    /// Flags likely junk knowledge facts and downgrades them to junk when confidence is high enough.
    /// </summary>
    private static void FlagKnowledgeJunk(List<KnowledgeEntry> entries, List<MaintenanceFinding> findings, DateTimeOffset now)
    {
        foreach (var entry in entries)
        {
            var confidence = EstimateJunkConfidence(entry.Key, entry.Value, entry.Category, entry.Confidence);
            if (confidence < JunkFindingThreshold)
            {
                continue;
            }

            findings.Add(new MaintenanceFinding(
                "knowledge",
                "junk",
                confidence,
                entry.Category,
                entry.Key,
                entry.Value,
                null,
                confidence >= JunkApplyThreshold ? "junk" : null,
                "Deterministic junk signals matched demo, placeholder, or test-like content."));

            if (confidence >= JunkApplyThreshold)
            {
                entry.LifecycleStatus = "junk";
                entry.UpdatedAt = now;
            }
        }
    }

    /// <summary>
    /// Picks one canonical brain fact per duplicate cluster and supersedes the rest.
    /// </summary>
    private static void ResolveBrainDuplicates(List<BrainEntry> entries, List<MaintenanceFinding> findings, DateTimeOffset now)
    {
        var duplicateGroups = entries
            .GroupBy(entry => BuildBrainDuplicateKey(entry), StringComparer.Ordinal)
            .Where(group => !string.IsNullOrWhiteSpace(group.Key) && group.Count() > 1)
            .ToList();

        foreach (var group in duplicateGroups)
        {
            var canonical = group
                .OrderByDescending(entry => IsCurrent(entry.LifecycleStatus))
                .ThenByDescending(entry => entry.Confidence)
                .ThenByDescending(entry => entry.UpdatedAt)
                .ThenBy(entry => entry.Key, StringComparer.Ordinal)
                .First();

            foreach (var duplicate in group.Where(entry => !string.Equals(entry.Key, canonical.Key, StringComparison.Ordinal)))
            {
                findings.Add(new MaintenanceFinding(
                    "brain",
                    "duplicate",
                    0.97f,
                    duplicate.Category,
                    duplicate.Key,
                    duplicate.Value,
                    null,
                    "superseded",
                    $"Duplicate brain fact of canonical key '{canonical.Key}'."));
                duplicate.LifecycleStatus = "superseded";
                duplicate.SupersededBy = canonical.PromotionIdentity;
                duplicate.InvalidatedBy = string.Empty;
                duplicate.UpdatedAt = now;
            }
        }
    }

    /// <summary>
    /// Picks one canonical knowledge fact per duplicate cluster and merges the rest.
    /// </summary>
    private static void ResolveKnowledgeDuplicates(List<KnowledgeEntry> entries, List<MaintenanceFinding> findings, DateTimeOffset now)
    {
        var duplicateGroups = entries
            .GroupBy(entry => BuildKnowledgeDuplicateKey(entry), StringComparer.Ordinal)
            .Where(group => !string.IsNullOrWhiteSpace(group.Key) && group.Count() > 1)
            .ToList();

        foreach (var group in duplicateGroups)
        {
            var canonical = group
                .OrderByDescending(entry => IsCurrent(entry.LifecycleStatus))
                .ThenByDescending(entry => entry.LifecycleScore)
                .ThenByDescending(entry => entry.Confidence)
                .ThenByDescending(entry => entry.UpdatedAt)
                .ThenBy(entry => entry.Key, StringComparer.Ordinal)
                .First();

            foreach (var duplicate in group.Where(entry => !string.Equals(entry.Key, canonical.Key, StringComparison.Ordinal)))
            {
                findings.Add(new MaintenanceFinding(
                    "knowledge",
                    "duplicate",
                    0.96f,
                    duplicate.Category,
                    duplicate.Key,
                    duplicate.Value,
                    null,
                    "merged",
                    $"Duplicate knowledge fact of canonical key '{canonical.Key}'."));
                duplicate.LifecycleStatus = "merged";
                duplicate.UpdatedAt = now;
            }
        }
    }

    /// <summary>
    /// Reconciles knowledge rows with the cleaned current brain projection.
    /// </summary>
    private static void ReconcileKnowledgeProjection(string projectId, List<BrainEntry> brainEntries, List<KnowledgeEntry> knowledgeEntries, List<MaintenanceFinding> findings, DateTimeOffset now)
    {
        foreach (var brainEntry in brainEntries)
        {
            var category = BuildProjectionCategory(brainEntry);
            var knowledgeEntry = knowledgeEntries.FirstOrDefault(entry =>
                string.Equals(entry.Category, category, StringComparison.OrdinalIgnoreCase)
                && string.Equals(entry.Key, brainEntry.Key, StringComparison.Ordinal));

            if (ShouldProject(brainEntry))
            {
                if (knowledgeEntry is null)
                {
                    findings.Add(new MaintenanceFinding(
                        "projection",
                        "missing_knowledge",
                        0.98f,
                        category,
                        brainEntry.Key,
                        brainEntry.Value,
                        brainEntry.Value,
                        "current",
                        "Current brain fact is missing its knowledge projection."));
                    knowledgeEntries.Add(CreateKnowledgeProjection(projectId, brainEntry, now));
                    continue;
                }

                if (!string.Equals(knowledgeEntry.Value, brainEntry.Value, StringComparison.Ordinal))
                {
                    findings.Add(new MaintenanceFinding(
                        "projection",
                        "outdated_knowledge",
                        0.95f,
                        knowledgeEntry.Category,
                        knowledgeEntry.Key,
                        knowledgeEntry.Value,
                        brainEntry.Value,
                        "current",
                        "Knowledge projection drifted from the current brain fact."));
                    ApplyKnowledgeValueChange(knowledgeEntry, brainEntry.Value, now);
                }

                if (!IsCurrent(knowledgeEntry.LifecycleStatus))
                {
                    findings.Add(new MaintenanceFinding(
                        "projection",
                        "lifecycle_repair",
                        0.94f,
                        knowledgeEntry.Category,
                        knowledgeEntry.Key,
                        knowledgeEntry.Value,
                        null,
                        "current",
                        "Knowledge projection should be current because the matching brain fact is current."));
                    knowledgeEntry.LifecycleStatus = "current";
                    knowledgeEntry.UpdatedAt = now;
                }

                knowledgeEntry.Confidence = brainEntry.Confidence;
                knowledgeEntry.SourceType = string.IsNullOrWhiteSpace(brainEntry.SourceType) ? "brain" : brainEntry.SourceType;
                knowledgeEntry.SourceScope = string.IsNullOrWhiteSpace(brainEntry.SourceScope) ? projectId : brainEntry.SourceScope;
                knowledgeEntry.PromotionIdentity = brainEntry.PromotionIdentity;
                knowledgeEntry.LogicalKey = KnowledgeService.DeriveLogicalKey(category, brainEntry.Key);
                knowledgeEntry.LastConfirmedAt ??= now;
                knowledgeEntry.LifecycleScore = KnowledgeService.ComputeLifecycleScore(knowledgeEntry.Confidence, Math.Max(1, knowledgeEntry.ConfirmationCount), now, knowledgeEntry.LastRetrievedAt, knowledgeEntry.RetrievalCount);
                continue;
            }

            if (knowledgeEntry is null)
            {
                continue;
            }

            var tracksBrain = string.Equals(knowledgeEntry.PromotionIdentity, brainEntry.PromotionIdentity, StringComparison.Ordinal)
                || string.Equals(knowledgeEntry.SourceType, "brain", StringComparison.OrdinalIgnoreCase);
            if (!tracksBrain || !IsCurrent(knowledgeEntry.LifecycleStatus))
            {
                continue;
            }

            var nextLifecycle = string.Equals(brainEntry.LifecycleStatus, "junk", StringComparison.OrdinalIgnoreCase)
                || string.Equals(brainEntry.LifecycleStatus, "invalidated", StringComparison.OrdinalIgnoreCase)
                ? "junk"
                : "merged";
            findings.Add(new MaintenanceFinding(
                "projection",
                "brain_not_current",
                0.91f,
                knowledgeEntry.Category,
                knowledgeEntry.Key,
                knowledgeEntry.Value,
                null,
                nextLifecycle,
                "Knowledge projection should no longer stay current because the matching brain fact is non-current."));
            knowledgeEntry.LifecycleStatus = nextLifecycle;
            knowledgeEntry.UpdatedAt = now;
        }
    }

    /// <summary>
    /// Persists changed brain facts back to the store.
    /// </summary>
    private async Task<int> PersistBrainChangesAsync(List<BrainEntry> originalEntries, List<BrainEntry> updatedEntries, List<Dictionary<string, object?>> appliedActions, CancellationToken cancellationToken)
    {
        var updates = 0;
        var updatedKeys = updatedEntries.Select(entry => entry.Key).ToHashSet(StringComparer.Ordinal);

        foreach (var originalEntry in originalEntries.Where(entry => !updatedKeys.Contains(entry.Key)))
        {
            if (!await _brainStore.DeleteAsync(originalEntry.ProjectId, originalEntry.Key, cancellationToken))
            {
                continue;
            }

            updates++;
            appliedActions.Add(new Dictionary<string, object?>
            {
                ["scope"] = "brain",
                ["action"] = "delete",
                ["key"] = originalEntry.Key,
                ["category"] = originalEntry.Category,
                ["lifecycle_status"] = originalEntry.LifecycleStatus,
            });
        }

        foreach (var updatedEntry in updatedEntries)
        {
            var originalEntry = originalEntries.FirstOrDefault(entry => string.Equals(entry.Key, updatedEntry.Key, StringComparison.Ordinal));
            if (originalEntry is not null && BrainEntriesEqual(originalEntry, updatedEntry))
            {
                continue;
            }

            await _brainStore.StoreFactAsync(updatedEntry, cancellationToken);
            updates++;
            appliedActions.Add(new Dictionary<string, object?>
            {
                ["scope"] = "brain",
                ["action"] = "upsert",
                ["key"] = updatedEntry.Key,
                ["category"] = updatedEntry.Category,
                ["lifecycle_status"] = updatedEntry.LifecycleStatus,
                ["value"] = updatedEntry.Value,
            });
        }

        return updates;
    }

    /// <summary>
    /// Persists changed knowledge facts back to the store.
    /// </summary>
    private async Task<int> PersistKnowledgeChangesAsync(List<KnowledgeEntry> originalEntries, List<KnowledgeEntry> updatedEntries, List<Dictionary<string, object?>> appliedActions, CancellationToken cancellationToken)
    {
        var updates = 0;
        foreach (var updatedEntry in updatedEntries)
        {
            var originalEntry = originalEntries.FirstOrDefault(entry =>
                string.Equals(entry.Category, updatedEntry.Category, StringComparison.Ordinal)
                && string.Equals(entry.Key, updatedEntry.Key, StringComparison.Ordinal));
            if (originalEntry is not null && KnowledgeEntriesEqual(originalEntry, updatedEntry))
            {
                continue;
            }

            await _knowledgeStore.UpsertFactAsync(updatedEntry, cancellationToken);
            updates++;
            appliedActions.Add(new Dictionary<string, object?>
            {
                ["scope"] = "knowledge",
                ["key"] = updatedEntry.Key,
                ["category"] = updatedEntry.Category,
                ["lifecycle_status"] = updatedEntry.LifecycleStatus,
                ["value"] = updatedEntry.Value,
            });
        }

        return updates;
    }

    /// <summary>
    /// Creates a new knowledge row from a current brain fact.
    /// </summary>
    private static KnowledgeEntry CreateKnowledgeProjection(string projectId, BrainEntry entry, DateTimeOffset now)
    {
        var category = BuildProjectionCategory(entry);
        return new KnowledgeEntry
        {
            ProjectId = projectId,
            Category = category,
            Key = entry.Key,
            Value = entry.Value,
            Confidence = entry.Confidence,
            CreatedAt = now,
            UpdatedAt = now,
            LogicalKey = KnowledgeService.DeriveLogicalKey(category, entry.Key),
            PromotionIdentity = entry.PromotionIdentity,
            SourceType = string.IsNullOrWhiteSpace(entry.SourceType) ? "brain" : entry.SourceType,
            SourceScope = string.IsNullOrWhiteSpace(entry.SourceScope) ? projectId : entry.SourceScope,
            LifecycleStatus = "current",
            LifecycleScore = KnowledgeService.ComputeLifecycleScore(entry.Confidence, 1, now, null, 0),
            ConfirmationCount = 1,
            LastConfirmedAt = now,
            RetrievalCount = 0,
            LastRetrievedAt = null,
            History = [],
        };
    }

    /// <summary>
    /// Preserves history when a knowledge value changes during cleanup.
    /// </summary>
    private static void ApplyKnowledgeValueChange(KnowledgeEntry entry, string newValue, DateTimeOffset now)
    {
        if (string.Equals(entry.Value, newValue, StringComparison.Ordinal))
        {
            return;
        }

        entry.History.Add(new KnowledgeHistoryEntry
        {
            Value = entry.Value,
            Confidence = entry.Confidence,
            PromotionIdentity = entry.PromotionIdentity,
            SourceType = entry.SourceType,
            SourceScope = entry.SourceScope,
            ValidFrom = entry.CreatedAt,
            SupersededAt = now,
        });
        entry.Value = newValue;
        entry.UpdatedAt = now;
        entry.LastConfirmedAt ??= now;
    }

    /// <summary>
    /// Builds a stable duplicate clustering key for brain facts.
    /// </summary>
    private static string BuildBrainDuplicateKey(BrainEntry entry)
    {
        var logicalKey = string.IsNullOrWhiteSpace(entry.LogicalKey)
            ? KnowledgeService.NormalizeToken(entry.Key)
            : KnowledgeService.NormalizeToken(entry.LogicalKey);
        var value = NormalizeComparisonText(entry.Value);
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return $"{KnowledgeService.NormalizeToken(entry.Category)}:{logicalKey}:{value}";
    }

    /// <summary>
    /// Builds a stable duplicate clustering key for knowledge facts.
    /// </summary>
    private static string BuildKnowledgeDuplicateKey(KnowledgeEntry entry)
    {
        var logicalKey = string.IsNullOrWhiteSpace(entry.LogicalKey)
            ? KnowledgeService.DeriveLogicalKey(entry.Category, entry.Key)
            : KnowledgeService.NormalizeToken(entry.LogicalKey);
        var value = NormalizeComparisonText(entry.Value);
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return $"{KnowledgeService.NormalizeToken(entry.Category)}:{logicalKey}:{value}";
    }

    /// <summary>
    /// Returns whether a brain fact should exist in public knowledge.
    /// </summary>
    private static bool ShouldProject(BrainEntry entry)
    {
        if (!IsCurrent(entry.LifecycleStatus))
        {
            return false;
        }

        if (string.Equals(entry.Kind, "session_event", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return !string.Equals(entry.LifecycleStatus, "legacy", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(entry.LifecycleStatus, "invalidated", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(entry.LifecycleStatus, "timeline", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(entry.LifecycleStatus, "junk", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Resolves the knowledge category used for one brain projection.
    /// </summary>
    private static string BuildProjectionCategory(BrainEntry entry)
    {
        return string.IsNullOrWhiteSpace(entry.Category) ? entry.Kind : entry.Category;
    }

    /// <summary>
    /// Normalizes display text while keeping user-meaningful punctuation intact.
    /// </summary>
    private static string NormalizeDisplayText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return string.Join(" ", value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)).Trim();
    }

    /// <summary>
    /// Normalizes text for duplicate comparison by ignoring casing and terminal punctuation.
    /// </summary>
    private static string NormalizeComparisonText(string? value)
    {
        var normalized = NormalizeDisplayText(value).ToLowerInvariant().Trim();
        return normalized.TrimEnd('.', '!', '?', ';', ':', ',');
    }

    /// <summary>
    /// Scores junk confidence from deterministic low-signal markers.
    /// </summary>
    private static float EstimateJunkConfidence(string? key, string? value, string? category, float recordConfidence)
    {
        var combined = $"{key} {value} {category}".ToLowerInvariant();
        float score = 0f;

        if (combined.Contains("placeholder", StringComparison.Ordinal)
            || combined.Contains("lorem ipsum", StringComparison.Ordinal)
            || combined.Contains("demo", StringComparison.Ordinal)
            || combined.Contains("dummy", StringComparison.Ordinal))
        {
            score += 0.55f;
        }

        if (combined.Contains("test data", StringComparison.Ordinal)
            || combined.Contains("sample data", StringComparison.Ordinal)
            || combined.Contains("todo", StringComparison.Ordinal)
            || combined.Contains("temp", StringComparison.Ordinal))
        {
            score += 0.25f;
        }

        if (KnowledgeService.NormalizeToken(category) is "testing-demo" or "testing:demo" or "test" or "demo")
        {
            score += 0.35f;
        }

        var normalizedValue = NormalizeComparisonText(value);
        if (normalizedValue.Length is > 0 and <= 8)
        {
            score += 0.15f;
        }

        if (normalizedValue is "foo" or "bar" or "baz" or "test" or "sample" or "placeholder")
        {
            score += 0.4f;
        }

        if (recordConfidence < 0.8f)
        {
            score += 0.1f;
        }

        return Math.Clamp(score, 0f, 0.99f);
    }

    /// <summary>
    /// Returns whether a lifecycle string should be treated as current.
    /// </summary>
    private static bool IsCurrent(string? lifecycleStatus)
    {
        return string.Equals(lifecycleStatus, "current", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Returns whether a brain row is legacy raw journal/timeline content rather than canonical memory.
    /// </summary>
    private static bool IsLegacyRawBrainEntry(BrainEntry entry)
    {
        if (string.Equals(entry.Kind, "session_event", StringComparison.OrdinalIgnoreCase)
            || string.Equals(entry.Category, "session_timeline", StringComparison.OrdinalIgnoreCase)
            || string.Equals(entry.LifecycleStatus, "timeline", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return string.Equals(entry.Kind, "user_prompt", StringComparison.OrdinalIgnoreCase)
            || string.Equals(entry.Kind, "assistant_output", StringComparison.OrdinalIgnoreCase)
            || string.Equals(entry.Kind, "tool_activity", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Clones a brain row so analyze mode stays side-effect free.
    /// </summary>
    private static BrainEntry CloneBrainEntry(BrainEntry entry)
    {
        return new BrainEntry
        {
            ProjectId = entry.ProjectId,
            Key = entry.Key,
            Value = entry.Value,
            Kind = entry.Kind,
            Category = entry.Category,
            LogicalKey = entry.LogicalKey,
            PromotionIdentity = entry.PromotionIdentity,
            SourceType = entry.SourceType,
            SourceScope = entry.SourceScope,
            LifecycleStatus = entry.LifecycleStatus,
            Confidence = entry.Confidence,
            Evidence = entry.Evidence,
            SupersededBy = entry.SupersededBy,
            InvalidatedBy = entry.InvalidatedBy,
            CreatedAt = entry.CreatedAt,
            UpdatedAt = entry.UpdatedAt,
        };
    }

    /// <summary>
    /// Clones a knowledge row so analyze mode stays side-effect free.
    /// </summary>
    private static KnowledgeEntry CloneKnowledgeEntry(KnowledgeEntry entry)
    {
        return new KnowledgeEntry
        {
            ProjectId = entry.ProjectId,
            Category = entry.Category,
            Key = entry.Key,
            Value = entry.Value,
            Confidence = entry.Confidence,
            CreatedAt = entry.CreatedAt,
            UpdatedAt = entry.UpdatedAt,
            LogicalKey = entry.LogicalKey,
            PromotionIdentity = entry.PromotionIdentity,
            SourceType = entry.SourceType,
            SourceScope = entry.SourceScope,
            LifecycleStatus = entry.LifecycleStatus,
            LifecycleScore = entry.LifecycleScore,
            ConfirmationCount = entry.ConfirmationCount,
            LastConfirmedAt = entry.LastConfirmedAt,
            RetrievalCount = entry.RetrievalCount,
            LastRetrievedAt = entry.LastRetrievedAt,
            History = entry.History.Select(CloneHistoryEntry).ToList(),
        };
    }

    /// <summary>
    /// Clones one knowledge history row.
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
    /// Reads an integer-like count value from a status payload.
    /// </summary>
    private static int ReadCount(IReadOnlyDictionary<string, object?>? payload, string key)
    {
        if (payload is null || !payload.TryGetValue(key, out var value) || value is null)
        {
            return 0;
        }

        return value switch
        {
            int integer => integer,
            long longValue when longValue > int.MaxValue => int.MaxValue,
            long longValue => (int)longValue,
            float singleValue when singleValue > int.MaxValue => int.MaxValue,
            float singleValue => (int)singleValue,
            double doubleValue when doubleValue > int.MaxValue => int.MaxValue,
            double doubleValue => (int)doubleValue,
            string text when int.TryParse(text, out var parsed) => parsed,
            _ => 0,
        };
    }

    /// <summary>
    /// Compares persisted brain rows for meaningful maintenance changes.
    /// </summary>
    private static bool BrainEntriesEqual(BrainEntry left, BrainEntry right)
    {
        return string.Equals(left.ProjectId, right.ProjectId, StringComparison.Ordinal)
            && string.Equals(left.Key, right.Key, StringComparison.Ordinal)
            && string.Equals(left.Value, right.Value, StringComparison.Ordinal)
            && string.Equals(left.Kind, right.Kind, StringComparison.Ordinal)
            && string.Equals(left.Category, right.Category, StringComparison.Ordinal)
            && string.Equals(left.LogicalKey, right.LogicalKey, StringComparison.Ordinal)
            && string.Equals(left.PromotionIdentity, right.PromotionIdentity, StringComparison.Ordinal)
            && string.Equals(left.SourceType, right.SourceType, StringComparison.Ordinal)
            && string.Equals(left.SourceScope, right.SourceScope, StringComparison.Ordinal)
            && string.Equals(left.LifecycleStatus, right.LifecycleStatus, StringComparison.Ordinal)
            && left.Confidence.Equals(right.Confidence)
            && string.Equals(left.Evidence, right.Evidence, StringComparison.Ordinal)
            && string.Equals(left.SupersededBy, right.SupersededBy, StringComparison.Ordinal)
            && string.Equals(left.InvalidatedBy, right.InvalidatedBy, StringComparison.Ordinal);
    }

    /// <summary>
    /// Compares persisted knowledge rows for meaningful maintenance changes.
    /// </summary>
    private static bool KnowledgeEntriesEqual(KnowledgeEntry left, KnowledgeEntry right)
    {
        return string.Equals(left.ProjectId, right.ProjectId, StringComparison.Ordinal)
            && string.Equals(left.Category, right.Category, StringComparison.Ordinal)
            && string.Equals(left.Key, right.Key, StringComparison.Ordinal)
            && string.Equals(left.Value, right.Value, StringComparison.Ordinal)
            && left.Confidence.Equals(right.Confidence)
            && string.Equals(left.LogicalKey, right.LogicalKey, StringComparison.Ordinal)
            && string.Equals(left.PromotionIdentity, right.PromotionIdentity, StringComparison.Ordinal)
            && string.Equals(left.SourceType, right.SourceType, StringComparison.Ordinal)
            && string.Equals(left.SourceScope, right.SourceScope, StringComparison.Ordinal)
            && string.Equals(left.LifecycleStatus, right.LifecycleStatus, StringComparison.Ordinal)
            && left.LifecycleScore.Equals(right.LifecycleScore)
            && left.ConfirmationCount == right.ConfirmationCount
            && Nullable.Equals(left.LastConfirmedAt, right.LastConfirmedAt)
            && left.RetrievalCount == right.RetrievalCount
            && Nullable.Equals(left.LastRetrievedAt, right.LastRetrievedAt)
            && KnowledgeHistoryEqual(left.History, right.History);
    }

    /// <summary>
    /// Compares retained knowledge history lists.
    /// </summary>
    private static bool KnowledgeHistoryEqual(IReadOnlyList<KnowledgeHistoryEntry> left, IReadOnlyList<KnowledgeHistoryEntry> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        for (var index = 0; index < left.Count; index++)
        {
            var leftEntry = left[index];
            var rightEntry = right[index];
            if (!string.Equals(leftEntry.Value, rightEntry.Value, StringComparison.Ordinal)
                || !leftEntry.Confidence.Equals(rightEntry.Confidence)
                || !string.Equals(leftEntry.PromotionIdentity, rightEntry.PromotionIdentity, StringComparison.Ordinal)
                || !string.Equals(leftEntry.SourceType, rightEntry.SourceType, StringComparison.Ordinal)
                || !string.Equals(leftEntry.SourceScope, rightEntry.SourceScope, StringComparison.Ordinal)
                || !Nullable.Equals(leftEntry.ValidFrom, rightEntry.ValidFrom)
                || leftEntry.SupersededAt != rightEntry.SupersededAt)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Lightweight finding model for one maintenance issue.
    /// </summary>
    private sealed record MaintenanceFinding(
        string Scope,
        string Kind,
        float Confidence,
        string Category,
        string Key,
        string CurrentValue,
        string? SuggestedValue,
        string? TargetStatus,
        string Description)
    {
        /// <summary>
        /// Converts the finding into a stable JSON-friendly payload.
        /// </summary>
        public Dictionary<string, object?> ToPayload()
        {
            return new Dictionary<string, object?>
            {
                ["scope"] = Scope,
                ["kind"] = Kind,
                ["confidence"] = Confidence,
                ["category"] = Category,
                ["key"] = Key,
                ["current_value"] = CurrentValue,
                ["suggested_value"] = SuggestedValue,
                ["target_status"] = TargetStatus,
                ["description"] = Description,
            };
        }
    }
}
