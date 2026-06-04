namespace NebuCtx.IntegrationTests;

using System.Collections.Concurrent;

using NebuCtx.Contracts.Mcp;
using NebuCtx.Contracts.Projects;
using NebuCtx.Storage;

/// <summary>
/// In-memory store implementations for integration tests.
/// Provided as standalone classes so <see cref="NebuCtxTestFactory"/> stays focused on wiring.
/// </summary>

internal sealed class InMemoryProjectStore : IProjectStore
{
    private readonly ConcurrentDictionary<string, ProjectRecord> _projects = new();

    public Task<ProjectRecord?> GetProjectAsync(string projectId, CancellationToken cancellationToken = default)
    {
        _projects.TryGetValue(projectId, out var project);
        return Task.FromResult(project);
    }

    public Task<ProjectRecord?> FindByFingerprintAsync(RepositoryFingerprint fingerprint, CancellationToken cancellationToken = default)
    {
        return ListByFingerprintAsync(fingerprint, cancellationToken)
            .ContinueWith(task => task.Result.Count == 1 ? task.Result[0] : null, cancellationToken);
    }

    public Task<IReadOnlyList<ProjectRecord>> ListByFingerprintAsync(RepositoryFingerprint fingerprint, CancellationToken cancellationToken = default)
    {
        var matches = _projects.Values
            .Where(p =>
                p.Fingerprint is not null &&
                string.Equals(p.Fingerprint.RemoteUrl, fingerprint.RemoteUrl, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(p.Fingerprint.Host, fingerprint.Host, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(p.Fingerprint.Owner, fingerprint.Owner, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(p.Fingerprint.RepoName, fingerprint.RepoName, StringComparison.OrdinalIgnoreCase))
            .ToList();
        return Task.FromResult<IReadOnlyList<ProjectRecord>>(matches);
    }

    public Task CreateProjectAsync(ProjectRecord project, CancellationToken cancellationToken = default)
    {
        _projects[project.ProjectId] = project;
        return Task.CompletedTask;
    }

    public Task UpdateProjectAsync(ProjectRecord project, CancellationToken cancellationToken = default)
    {
        _projects[project.ProjectId] = project;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<ProjectRecord>> ListProjectsAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<ProjectRecord>>(_projects.Values.ToList());

    public Task<bool> DeleteProjectAsync(string projectId, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_projects.TryRemove(projectId, out _));
    }
}

internal sealed class InMemoryCheckoutBindingStore : ICheckoutBindingStore
{
    private readonly ConcurrentDictionary<string, List<CheckoutBinding>> _bindings = new();

    public Task UpsertBindingAsync(CheckoutBinding binding, CancellationToken cancellationToken = default)
    {
        var list = _bindings.GetOrAdd(binding.ProjectId, _ => []);
        var idx = list.FindIndex(b => string.Equals(b.LocalRoot, binding.LocalRoot, StringComparison.OrdinalIgnoreCase));
        if (idx >= 0) list[idx] = binding;
        else list.Add(binding);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<CheckoutBinding>> GetBindingsAsync(string projectId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<CheckoutBinding>>(
            _bindings.TryGetValue(projectId, out var list) ? list.ToList() : []);

    public Task<int> ClearProjectAsync(string projectId, CancellationToken cancellationToken = default)
    {
        if (!_bindings.TryRemove(projectId, out var list))
        {
            return Task.FromResult(0);
        }

        return Task.FromResult(list.Count);
    }
}

internal sealed class InMemoryBrainStore : IBrainStore
{
    private readonly ConcurrentDictionary<(string ProjectId, string Key), BrainEntry> _entries = new();

    public Task<Dictionary<string, object?>?> GetStatusAsync(string projectId, CancellationToken cancellationToken = default)
    {
        var entries = _entries.Where(kv => kv.Key.ProjectId == projectId).Select(kv => kv.Value).ToList();
        return Task.FromResult<Dictionary<string, object?>?>(new Dictionary<string, object?>
        {
            ["entry_count"] = entries.Count,
            ["active_fact_count"] = entries.Count(entry => string.Equals(entry.LifecycleStatus, "current", StringComparison.OrdinalIgnoreCase)),
            ["project_id"] = projectId,
        });
    }

    public Task StoreAsync(string projectId, string key, string value, CancellationToken cancellationToken = default)
    {
        _entries[(projectId, key)] = new BrainEntry
        {
            ProjectId = projectId,
            Key = key,
            Value = value,
            Kind = "legacy",
            Category = "legacy",
            LogicalKey = key,
            PromotionIdentity = $"legacy:{projectId}:{key}",
            SourceType = "legacy",
            SourceScope = projectId,
            LifecycleStatus = "legacy",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        return Task.CompletedTask;
    }

    public Task StoreFactAsync(BrainEntry entry, CancellationToken cancellationToken = default)
    {
        entry.UpdatedAt = entry.UpdatedAt == default ? DateTimeOffset.UtcNow : entry.UpdatedAt;
        entry.CreatedAt = entry.CreatedAt == default ? entry.UpdatedAt : entry.CreatedAt;
        _entries[(entry.ProjectId, entry.Key)] = entry;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<BrainEntry>> RecallAsync(string projectId, string query, int limit = 10, CancellationToken cancellationToken = default)
    {
        var terms = query.Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(term => term.Length >= 2)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var results = _entries
            .Where(kv => kv.Key.ProjectId == projectId &&
                terms.Any(term =>
                    kv.Value.Key.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                    kv.Value.Value.Contains(term, StringComparison.OrdinalIgnoreCase)))
            .Select(kv => kv.Value)
            .OrderByDescending(entry => entry.CreatedAt)
            .Take(limit)
            .ToList();
        return Task.FromResult<IReadOnlyList<BrainEntry>>(results);
    }

    public Task<IReadOnlyList<BrainEntry>> ListAllAsync(string projectId, int limit = 200, CancellationToken cancellationToken = default)
    {
        var results = _entries
            .Where(kv => kv.Key.ProjectId == projectId)
            .Select(kv => kv.Value)
            .OrderByDescending(e => e.CreatedAt)
            .Take(limit)
            .ToList();
        return Task.FromResult<IReadOnlyList<BrainEntry>>(results);
    }

    public Task<(IReadOnlyList<BrainEntry> Entries, int Total)> ListFilteredAsync(string projectId, MemoryListFilter filter, CancellationToken cancellationToken = default)
    {
        var matches = _entries
            .Where(kv => kv.Key.ProjectId == projectId)
            .Select(kv => kv.Value)
            .Where(e => string.IsNullOrEmpty(filter.Category) || e.Category == filter.Category || e.Kind == filter.Category)
            .Where(e => string.IsNullOrEmpty(filter.SourceType) || e.SourceType == filter.SourceType)
            .Where(e => string.IsNullOrEmpty(filter.LifecycleStatus) || e.LifecycleStatus == filter.LifecycleStatus)
            .Where(e => !filter.CreatedAfter.HasValue || e.CreatedAt >= filter.CreatedAfter.Value)
            .Where(e => !filter.CreatedBefore.HasValue || e.CreatedAt <= filter.CreatedBefore.Value)
            .ToList();
        var total = matches.Count;
        var direction = string.Equals(filter.SortDirection, "asc", StringComparison.OrdinalIgnoreCase) ? 1 : -1;
        IEnumerable<BrainEntry> ordered = filter.SortField.ToLowerInvariant() switch
        {
            "created" => matches.OrderBy(e => e.CreatedAt),
            "confidence" => matches.OrderBy(e => e.Confidence),
            "key" => matches.OrderBy(e => e.Key),
            _ => matches.OrderBy(e => e.UpdatedAt),
        };
        if (direction < 0)
        {
            ordered = ordered.Reverse();
        }
        var sorted = ordered.ToList();
        var limit = Math.Clamp(filter.Limit <= 0 ? 20 : filter.Limit, 1, MemoryListFilter.MaxLimit);
        var offset = Math.Max(0, filter.Offset);
        var page = sorted.Skip(offset).Take(limit).ToList();
        return Task.FromResult<(IReadOnlyList<BrainEntry>, int)>((page, total));
    }

    public Task<bool> DeleteAsync(string projectId, string key, CancellationToken cancellationToken = default)
    {
        var removed = _entries.TryRemove((projectId, key), out _);
        return Task.FromResult(removed);
    }

    public Task<int> ClearProjectAsync(string projectId, CancellationToken cancellationToken = default)
    {
        var keys = _entries.Keys.Where(k => k.ProjectId == projectId).ToList();
        foreach (var k in keys) _entries.TryRemove(k, out _);
        return Task.FromResult(keys.Count);
    }

    public Task<int> DeleteByPrefixAsync(string projectId, string keyPrefix, CancellationToken cancellationToken = default)
    {
        var keys = _entries.Keys
            .Where(k => k.ProjectId == projectId && (
                k.Key.StartsWith(keyPrefix, StringComparison.OrdinalIgnoreCase)
                || (_entries.TryGetValue(k, out var entry) && string.Equals(entry.Kind, keyPrefix, StringComparison.OrdinalIgnoreCase))))
            .ToList();
        foreach (var k in keys) _entries.TryRemove(k, out _);
        return Task.FromResult(keys.Count);
    }
}

internal sealed class InMemoryKnowledgeStore : IKnowledgeStore
{
    private readonly List<KnowledgeEntry> _facts = [];
    private readonly List<KnowledgeCandidateEntry> _candidateFacts = [];
    private readonly Lock _lock = new();

    public Task UpsertFactAsync(KnowledgeEntry entry, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            var idx = _facts.FindIndex(f =>
                f.ProjectId == entry.ProjectId &&
                f.Category == entry.Category &&
                f.Key == entry.Key);
            if (idx >= 0) _facts[idx] = entry;
            else _facts.Add(entry);
        }
        return Task.CompletedTask;
    }

    public Task<KnowledgeEntry?> GetFactAsync(string projectId, string category, string key, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            var fact = _facts.FirstOrDefault(f => f.ProjectId == projectId && f.Category == category && f.Key == key);
            return Task.FromResult(fact);
        }
    }

    public Task<IReadOnlyList<KnowledgeEntry>> RecallAsync(string projectId, string? category, string query, int limit, CancellationToken cancellationToken = default)
    {
        var terms = query.Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(term => term.Length >= 2)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        lock (_lock)
        {
            var results = _facts
                .Where(f => f.ProjectId == projectId &&
                    (category is null || f.Category == category) &&
                    terms.Any(term =>
                        f.Category.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                        f.Key.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                        f.Value.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                        f.SourceScope.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                        f.SourceType.Contains(term, StringComparison.OrdinalIgnoreCase)))
                .OrderByDescending(f => f.LifecycleScore)
                .ThenByDescending(f => f.Confidence)
                .ThenByDescending(f => f.UpdatedAt)
                .Take(limit)
                .ToList();
            return Task.FromResult<IReadOnlyList<KnowledgeEntry>>(results);
        }
    }

    public Task<IReadOnlyList<(string Category, int Count)>> GetCategoriesAsync(string projectId, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            var cats = _facts
                .Where(f => f.ProjectId == projectId)
                .GroupBy(f => f.Category)
                .Select(g => (g.Key, g.Count()))
                .OrderBy(t => t.Key)
                .ToList();
            return Task.FromResult<IReadOnlyList<(string, int)>>(cats);
        }
    }

    public Task<int> GetFactCountAsync(string projectId, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            return Task.FromResult(_facts.Count(f => f.ProjectId == projectId));
        }
    }

    public Task<IReadOnlyList<KnowledgeEntry>> ListAllForProjectAsync(string projectId, int limit = 500, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            var results = _facts.Where(f => f.ProjectId == projectId).Take(limit).ToList();
            return Task.FromResult<IReadOnlyList<KnowledgeEntry>>(results);
        }
    }

    public Task<(IReadOnlyList<KnowledgeEntry> Entries, int Total)> ListFilteredAsync(string projectId, MemoryListFilter filter, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            var matches = _facts
                .Where(f => f.ProjectId == projectId)
                .Where(f => string.IsNullOrEmpty(filter.Category) || f.Category == filter.Category)
                .Where(f => string.IsNullOrEmpty(filter.SourceType) || f.SourceType == filter.SourceType)
                .Where(f => string.IsNullOrEmpty(filter.LifecycleStatus) || f.LifecycleStatus == filter.LifecycleStatus)
                .Where(f => !filter.CreatedAfter.HasValue || f.CreatedAt >= filter.CreatedAfter.Value)
                .Where(f => !filter.CreatedBefore.HasValue || f.CreatedAt <= filter.CreatedBefore.Value)
                .Where(f => string.IsNullOrEmpty(filter.PromotedFromSession) || f.SourceScope == filter.PromotedFromSession)
                .Where(f => string.IsNullOrEmpty(filter.PromotedFromBrainKey) || f.PromotedFromBrainKey == filter.PromotedFromBrainKey)
                .ToList();
            var total = matches.Count;
            var direction = string.Equals(filter.SortDirection, "asc", StringComparison.OrdinalIgnoreCase) ? 1 : -1;
            IEnumerable<KnowledgeEntry> ordered = filter.SortField.ToLowerInvariant() switch
            {
                "created" => matches.OrderBy(f => f.CreatedAt),
                "updated" => matches.OrderBy(f => f.UpdatedAt),
                "confidence" => matches.OrderBy(f => f.Confidence),
                "retrieval_count" => matches.OrderBy(f => f.RetrievalCount),
                "key" => matches.OrderBy(f => f.Category).ThenBy(f => f.Key),
                "relevance" => matches.OrderByDescending(f => f.LifecycleScore)
                    .ThenByDescending(f => f.Confidence),
                _ => matches.OrderByDescending(f => f.LifecycleScore)
                    .ThenByDescending(f => f.Confidence)
                    .ThenByDescending(f => f.UpdatedAt),
            };
            if (direction > 0)
            {
                ordered = ordered.Reverse();
            }
            var sorted = ordered.ToList();
            var limit = Math.Clamp(filter.Limit <= 0 ? 20 : filter.Limit, 1, MemoryListFilter.MaxLimit);
            var offset = Math.Max(0, filter.Offset);
            var page = sorted.Skip(offset).Take(limit).ToList();
            return Task.FromResult<(IReadOnlyList<KnowledgeEntry>, int)>((page, total));
        }
    }

    public Task<bool> RemoveFactAsync(string projectId, string category, string key, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            var idx = _facts.FindIndex(f => f.ProjectId == projectId && f.Category == category && f.Key == key);
            if (idx < 0) return Task.FromResult(false);
            _facts.RemoveAt(idx);
            return Task.FromResult(true);
        }
    }

    public Task<int> ClearProjectAsync(string projectId, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            var removed = _facts.RemoveAll(f => f.ProjectId == projectId);
            removed += _candidateFacts.RemoveAll(f => f.ProjectId == projectId);
            return Task.FromResult(removed);
        }
    }

    public Task<int> ReassignProjectAsync(string fromProjectId, string toProjectId, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            var moved = 0;
            for (var i = 0; i < _facts.Count; i++)
            {
                var fact = _facts[i];
                if (!string.Equals(fact.ProjectId, fromProjectId, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                _facts[i] = new KnowledgeEntry
                {
                    ProjectId = toProjectId,
                    Category = fact.Category,
                    Key = fact.Key,
                    Value = fact.Value,
                    Confidence = fact.Confidence,
                    CreatedAt = fact.CreatedAt,
                    UpdatedAt = fact.UpdatedAt,
                    LogicalKey = fact.LogicalKey,
                    PromotionIdentity = fact.PromotionIdentity,
                    SourceType = fact.SourceType,
                    SourceScope = fact.SourceScope,
                    LifecycleStatus = fact.LifecycleStatus,
                    LifecycleScore = fact.LifecycleScore,
                    ConfirmationCount = fact.ConfirmationCount,
                    LastConfirmedAt = fact.LastConfirmedAt,
                    RetrievalCount = fact.RetrievalCount,
                    LastRetrievedAt = fact.LastRetrievedAt,
                    History = fact.History.Select(item => new KnowledgeHistoryEntry
                    {
                        Value = item.Value,
                        Confidence = item.Confidence,
                        PromotionIdentity = item.PromotionIdentity,
                        SourceType = item.SourceType,
                        SourceScope = item.SourceScope,
                        ValidFrom = item.ValidFrom,
                        SupersededAt = item.SupersededAt,
                    }).ToList(),
                };
                moved++;
            }

            return Task.FromResult(moved);
        }
    }

    public Task UpsertCandidateAsync(KnowledgeCandidateEntry entry, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            var idx = _candidateFacts.FindIndex(f =>
                f.ProjectId == entry.ProjectId &&
                f.PromotionIdentity == entry.PromotionIdentity);
            if (idx >= 0) _candidateFacts[idx] = entry;
            else _candidateFacts.Add(entry);
        }

        return Task.CompletedTask;
    }

    public Task<KnowledgeCandidateEntry?> GetCandidateAsync(string projectId, string promotionIdentity, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            var candidate = _candidateFacts.FirstOrDefault(f =>
                f.ProjectId == projectId && f.PromotionIdentity == promotionIdentity);
            return Task.FromResult(candidate);
        }
    }

    public Task<IReadOnlyList<KnowledgeCandidateEntry>> ListCandidatesAsync(string projectId, int limit = 100, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            var results = _candidateFacts
                .Where(f => f.ProjectId == projectId)
                .OrderByDescending(f => f.UpdatedAt)
                .Take(limit)
                .ToList();
            return Task.FromResult<IReadOnlyList<KnowledgeCandidateEntry>>(results);
        }
    }

    public Task<int> GetCandidateCountAsync(string projectId, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            return Task.FromResult(_candidateFacts.Count(f => f.ProjectId == projectId));
        }
    }
}

internal sealed class InMemorySessionStore : ISessionStore
{
    private readonly ConcurrentDictionary<(string ProjectId, string SessionId), CloudSessionState> _sessions = new();

    public Task<CloudSessionState?> LoadLatestAsync(string projectId, CancellationToken cancellationToken = default)
    {
        var latest = _sessions
            .Where(kv => kv.Key.ProjectId == projectId)
            .OrderByDescending(kv => kv.Value.UpdatedAt)
            .Select(kv => kv.Value)
            .FirstOrDefault();
        return Task.FromResult(latest);
    }

    public Task<CloudSessionState?> LoadByIdAsync(string projectId, string sessionId, CancellationToken cancellationToken = default)
    {
        _sessions.TryGetValue((projectId, sessionId), out var state);
        return Task.FromResult(state);
    }

    public Task SaveAsync(string projectId, CloudSessionState state, CancellationToken cancellationToken = default)
    {
        _sessions[(projectId, state.SessionId)] = state;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<CloudSessionSummary>> ListAsync(string projectId, int limit, CancellationToken cancellationToken = default)
    {
        var summaries = _sessions
            .Where(kv => kv.Key.ProjectId == projectId)
            .OrderByDescending(kv => kv.Value.UpdatedAt)
            .Take(limit)
            .Select(kv => new CloudSessionSummary
            {
                SessionId = kv.Value.SessionId,
                Version = kv.Value.Version,
                Task = kv.Value.Task,
                ToolCalls = kv.Value.ToolCalls,
                UpdatedAt = kv.Value.UpdatedAt,
            })
            .ToList();
        return Task.FromResult<IReadOnlyList<CloudSessionSummary>>(summaries);
    }

    public Task<int> DeleteOlderThanAsync(string projectId, int daysOld, CancellationToken cancellationToken = default)
    {
        var cutoff = DateTimeOffset.UtcNow.AddDays(-daysOld);
        var old = _sessions
            .Where(kv => kv.Key.ProjectId == projectId && kv.Value.UpdatedAt < cutoff)
            .Select(kv => kv.Key)
            .ToList();
        foreach (var key in old) _sessions.TryRemove(key, out _);
        return Task.FromResult(old.Count);
    }

    public Task<int> ClearProjectAsync(string projectId, CancellationToken cancellationToken = default)
    {
        var keys = _sessions
            .Where(kv => kv.Key.ProjectId == projectId)
            .Select(kv => kv.Key)
            .ToList();
        foreach (var key in keys) _sessions.TryRemove(key, out _);
        return Task.FromResult(keys.Count);
    }
}

internal sealed class InMemoryCodeIndexStore : ICodeIndexStore
{
    private readonly ConcurrentDictionary<string, (List<IndexedFile> Files, List<IndexedSymbol> Symbols, List<IndexedCallEdge> Edges, DateTimeOffset At)> _index = new();

    public Task SyncIndexAsync(string projectId, IReadOnlyList<IndexedFile> files, IReadOnlyList<IndexedSymbol> symbols, IReadOnlyList<IndexedCallEdge> edges, CancellationToken cancellationToken = default)
    {
        _index[projectId] = ([.. files], [.. symbols], [.. edges], DateTimeOffset.UtcNow);
        return Task.CompletedTask;
    }

    public Task<CodeIndexStats> GetStatsAsync(string projectId, CancellationToken cancellationToken = default)
    {
        if (!_index.TryGetValue(projectId, out var entry))
            return Task.FromResult(new CodeIndexStats());
        var langDist = entry.Files.GroupBy(f => f.Language).ToDictionary(g => g.Key, g => g.Count());
        return Task.FromResult(new CodeIndexStats
        {
            FileCount = entry.Files.Count,
            SymbolCount = entry.Symbols.Count,
            EdgeCount = entry.Edges.Count,
            LanguageDistribution = langDist,
            LastIndexedAt = entry.At,
        });
    }

    public Task<IReadOnlyList<IndexedSymbol>> SearchSymbolsAsync(string projectId, string? query, string? kind, int limit = 200, CancellationToken cancellationToken = default)
    {
        if (!_index.TryGetValue(projectId, out var entry))
            return Task.FromResult<IReadOnlyList<IndexedSymbol>>([]);
        var results = entry.Symbols
            .Where(s => (query is null || s.Name.Contains(query, StringComparison.OrdinalIgnoreCase)) &&
                        (kind is null || s.Kind == kind))
            .Take(limit)
            .ToList();
        return Task.FromResult<IReadOnlyList<IndexedSymbol>>(results);
    }

    public Task<IReadOnlyList<IndexedCallEdge>> GetEdgesAsync(string projectId, int limit = 5000, CancellationToken cancellationToken = default)
    {
        if (!_index.TryGetValue(projectId, out var entry))
            return Task.FromResult<IReadOnlyList<IndexedCallEdge>>([]);
        return Task.FromResult<IReadOnlyList<IndexedCallEdge>>(entry.Edges.Take(limit).ToList());
    }

    public Task<IReadOnlyList<IndexedFile>> SearchFilesAsync(string projectId, string? query, int limit = 100, CancellationToken cancellationToken = default)
    {
        if (!_index.TryGetValue(projectId, out var entry))
            return Task.FromResult<IReadOnlyList<IndexedFile>>([]);
        var results = entry.Files
            .Where(f => query is null || f.Path.Contains(query, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(f => f.TokenCount)
            .Take(limit)
            .ToList();
        return Task.FromResult<IReadOnlyList<IndexedFile>>(results);
    }

    public Task<bool> ClearProjectAsync(string projectId, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_index.TryRemove(projectId, out _));
    }
}
