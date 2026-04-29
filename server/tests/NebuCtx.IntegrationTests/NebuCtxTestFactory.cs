namespace NebuCtx.IntegrationTests;

using System.Collections.Concurrent;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NebuCtx.Contracts.Projects;
using NebuCtx.Server.Host;
using NebuCtx.Storage;
using NebuCtx.Storage.Postgres;

/// <summary>
/// Custom <see cref="WebApplicationFactory{TProgram}"/> for integration tests.
/// Bypasses PostgreSQL startup by setting required environment variables,
/// skipping schema initialization (via ASPNETCORE_ENVIRONMENT=Test check in Program.cs),
/// and replacing all Postgres-backed stores with in-memory stubs.
/// </summary>
public sealed class NebuCtxTestFactory : WebApplicationFactory<Program>
{
    /// <summary>
    /// Sets environment variables required by <see cref="NebuCtx.Hosting.Validation.StartupValidator"/>
    /// before any test invokes the factory. These must be set before Program.cs entry runs.
    /// </summary>
    static NebuCtxTestFactory()
    {
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Test");
        Environment.SetEnvironmentVariable("NEBULA_STORE", "postgres");
        Environment.SetEnvironmentVariable("DATABASE_URL", "Host=localhost;Database=nebu_test;Username=test;Password=test");
    }

    /// <inheritdoc />
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Ensure ASP.NET Core environment is Test so Program.cs skips schema init.
        builder.UseEnvironment("Test");

        builder.ConfigureTestServices(services =>
        {
            // Replace Postgres-backed singletons with in-memory stubs.
            Replace<IProjectStore>(services, new InMemoryProjectStore());
            Replace<ICheckoutBindingStore>(services, new InMemoryCheckoutBindingStore());
            Replace<IBrainStore>(services, new InMemoryBrainStore());
            Replace<IKnowledgeStore>(services, new InMemoryKnowledgeStore());
            Replace<ISessionStore>(services, new InMemorySessionStore());
            Replace<ICodeIndexStore>(services, new InMemoryCodeIndexStore());

            // Replace PostgresTelemetryStore with a disconnected instance so no Postgres call is made.
            Replace<PostgresTelemetryStore>(services,
                new PostgresTelemetryStore("Host=localhost;Database=nebu_test;Username=test;Password=test"));

            // Remove TelemetryHydrationService — it would try to query Postgres on startup.
            var hydration = services.FirstOrDefault(d =>
                d.ServiceType == typeof(IHostedService) &&
                d.ImplementationType == typeof(TelemetryHydrationService));
            if (hydration is not null)
                services.Remove(hydration);
        });
    }

    /// <summary>Removes all registrations for <typeparamref name="T"/> and registers a new singleton instance.</summary>
    private static void Replace<T>(IServiceCollection services, T instance) where T : class
    {
        var existing = services.Where(d => d.ServiceType == typeof(T)).ToList();
        foreach (var descriptor in existing)
            services.Remove(descriptor);
        services.AddSingleton(instance);
    }

    // ── In-memory store stubs ─────────────────────────────────────────────────

    /// <summary>In-memory project store for use in integration tests.</summary>
    private sealed class InMemoryProjectStore : IProjectStore
    {
        private readonly ConcurrentDictionary<string, ProjectRecord> _projects = new();

        /// <inheritdoc />
        public Task<ProjectRecord?> GetProjectAsync(string projectId, CancellationToken cancellationToken = default)
        {
            _projects.TryGetValue(projectId, out var project);
            return Task.FromResult(project);
        }

        /// <inheritdoc />
        public Task<ProjectRecord?> FindByFingerprintAsync(RepositoryFingerprint fingerprint, CancellationToken cancellationToken = default)
        {
            var match = _projects.Values.FirstOrDefault(p =>
                p.Fingerprint is not null &&
                string.Equals(p.Fingerprint.RemoteUrl, fingerprint.RemoteUrl, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(p.Fingerprint.Host, fingerprint.Host, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(p.Fingerprint.Owner, fingerprint.Owner, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(p.Fingerprint.RepoName, fingerprint.RepoName, StringComparison.OrdinalIgnoreCase));
            return Task.FromResult(match);
        }

        /// <inheritdoc />
        public Task CreateProjectAsync(ProjectRecord project, CancellationToken cancellationToken = default)
        {
            _projects[project.ProjectId] = project;
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task UpdateProjectAsync(ProjectRecord project, CancellationToken cancellationToken = default)
        {
            _projects[project.ProjectId] = project;
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task<IReadOnlyList<ProjectRecord>> ListProjectsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ProjectRecord>>(_projects.Values.ToList());
    }

    /// <summary>In-memory checkout binding store for use in integration tests.</summary>
    private sealed class InMemoryCheckoutBindingStore : ICheckoutBindingStore
    {
        private readonly ConcurrentDictionary<string, List<CheckoutBinding>> _bindings = new();

        /// <inheritdoc />
        public Task UpsertBindingAsync(CheckoutBinding binding, CancellationToken cancellationToken = default)
        {
            var list = _bindings.GetOrAdd(binding.ProjectId, _ => []);
            var idx = list.FindIndex(b => string.Equals(b.LocalRoot, binding.LocalRoot, StringComparison.OrdinalIgnoreCase));
            if (idx >= 0) list[idx] = binding;
            else list.Add(binding);
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task<IReadOnlyList<CheckoutBinding>> GetBindingsAsync(string projectId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<CheckoutBinding>>(
                _bindings.TryGetValue(projectId, out var list) ? list.ToList() : []);
    }

    /// <summary>In-memory brain store for use in integration tests.</summary>
    private sealed class InMemoryBrainStore : IBrainStore
    {
        private readonly ConcurrentDictionary<(string ProjectId, string Key), BrainEntry> _entries = new();

        /// <inheritdoc />
        public Task<Dictionary<string, object?>?> GetStatusAsync(string projectId, CancellationToken cancellationToken = default)
        {
            var entries = _entries.Where(kv => kv.Key.ProjectId == projectId).Select(kv => kv.Value).ToList();
            var status = new Dictionary<string, object?>
            {
                ["entry_count"] = entries.Count,
                ["project_id"] = projectId,
            };
            return Task.FromResult<Dictionary<string, object?>?>(status);
        }

        /// <inheritdoc />
        public Task StoreAsync(string projectId, string key, string value, CancellationToken cancellationToken = default)
        {
            _entries[(projectId, key)] = new BrainEntry { Key = key, Value = value, CreatedAt = DateTimeOffset.UtcNow };
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task<IReadOnlyList<BrainEntry>> RecallAsync(string projectId, string query, int limit = 10, CancellationToken cancellationToken = default)
        {
            var results = _entries
                .Where(kv => kv.Key.ProjectId == projectId &&
                    (kv.Value.Key.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                     kv.Value.Value.Contains(query, StringComparison.OrdinalIgnoreCase)))
                .Select(kv => kv.Value)
                .Take(limit)
                .ToList();
            return Task.FromResult<IReadOnlyList<BrainEntry>>(results);
        }

        /// <inheritdoc />
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

        /// <inheritdoc />
        public Task<bool> DeleteAsync(string projectId, string key, CancellationToken cancellationToken = default)
        {
            var removed = _entries.TryRemove((projectId, key), out _);
            return Task.FromResult(removed);
        }

        /// <inheritdoc />
        public Task<int> ClearProjectAsync(string projectId, CancellationToken cancellationToken = default)
        {
            var keys = _entries.Keys.Where(k => k.ProjectId == projectId).ToList();
            foreach (var k in keys) _entries.TryRemove(k, out _);
            return Task.FromResult(keys.Count);
        }
    }

    /// <summary>In-memory knowledge store for use in integration tests.</summary>
    private sealed class InMemoryKnowledgeStore : IKnowledgeStore
    {
        private readonly List<KnowledgeEntry> _facts = [];
        private readonly Lock _lock = new();

        /// <inheritdoc />
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

        /// <inheritdoc />
        public Task<IReadOnlyList<KnowledgeEntry>> RecallAsync(string projectId, string? category, string query, int limit, CancellationToken cancellationToken = default)
        {
            lock (_lock)
            {
                var results = _facts
                    .Where(f => f.ProjectId == projectId &&
                        (category is null || f.Category == category) &&
                        (f.Key.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                         f.Value.Contains(query, StringComparison.OrdinalIgnoreCase)))
                    .Take(limit)
                    .ToList();
                return Task.FromResult<IReadOnlyList<KnowledgeEntry>>(results);
            }
        }

        /// <inheritdoc />
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

        /// <inheritdoc />
        public Task<int> GetFactCountAsync(string projectId, CancellationToken cancellationToken = default)
        {
            lock (_lock) { return Task.FromResult(_facts.Count(f => f.ProjectId == projectId)); }
        }

        /// <inheritdoc />
        public Task<IReadOnlyList<KnowledgeEntry>> ListAllForProjectAsync(string projectId, int limit = 500, CancellationToken cancellationToken = default)
        {
            lock (_lock)
            {
                var results = _facts.Where(f => f.ProjectId == projectId).Take(limit).ToList();
                return Task.FromResult<IReadOnlyList<KnowledgeEntry>>(results);
            }
        }

        /// <inheritdoc />
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
    }

    /// <summary>In-memory session store for use in integration tests.</summary>
    private sealed class InMemorySessionStore : ISessionStore
    {
        private readonly ConcurrentDictionary<(string ProjectId, string SessionId), CloudSessionState> _sessions = new();

        /// <inheritdoc />
        public Task<CloudSessionState?> LoadLatestAsync(string projectId, CancellationToken cancellationToken = default)
        {
            var latest = _sessions
                .Where(kv => kv.Key.ProjectId == projectId)
                .OrderByDescending(kv => kv.Value.UpdatedAt)
                .Select(kv => kv.Value)
                .FirstOrDefault();
            return Task.FromResult(latest);
        }

        /// <inheritdoc />
        public Task<CloudSessionState?> LoadByIdAsync(string projectId, string sessionId, CancellationToken cancellationToken = default)
        {
            _sessions.TryGetValue((projectId, sessionId), out var state);
            return Task.FromResult(state);
        }

        /// <inheritdoc />
        public Task SaveAsync(string projectId, CloudSessionState state, CancellationToken cancellationToken = default)
        {
            _sessions[(projectId, state.SessionId)] = state;
            return Task.CompletedTask;
        }

        /// <inheritdoc />
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

        /// <inheritdoc />
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
    }

    /// <summary>In-memory code index store for use in integration tests.</summary>
    private sealed class InMemoryCodeIndexStore : ICodeIndexStore
    {
        private readonly ConcurrentDictionary<string, (List<IndexedFile> Files, List<IndexedSymbol> Symbols, List<IndexedCallEdge> Edges, DateTimeOffset At)> _index = new();

        /// <inheritdoc />
        public Task SyncIndexAsync(string projectId, IReadOnlyList<IndexedFile> files, IReadOnlyList<IndexedSymbol> symbols, IReadOnlyList<IndexedCallEdge> edges, CancellationToken cancellationToken = default)
        {
            _index[projectId] = ([.. files], [.. symbols], [.. edges], DateTimeOffset.UtcNow);
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task<CodeIndexStats> GetStatsAsync(string projectId, CancellationToken cancellationToken = default)
        {
            if (!_index.TryGetValue(projectId, out var entry))
                return Task.FromResult(new CodeIndexStats());
            var langDist = entry.Files
                .GroupBy(f => f.Language)
                .ToDictionary(g => g.Key, g => g.Count());
            return Task.FromResult(new CodeIndexStats
            {
                FileCount = entry.Files.Count,
                SymbolCount = entry.Symbols.Count,
                EdgeCount = entry.Edges.Count,
                LanguageDistribution = langDist,
                LastIndexedAt = entry.At,
            });
        }

        /// <inheritdoc />
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

        /// <inheritdoc />
        public Task<IReadOnlyList<IndexedCallEdge>> GetEdgesAsync(string projectId, int limit = 5000, CancellationToken cancellationToken = default)
        {
            if (!_index.TryGetValue(projectId, out var entry))
                return Task.FromResult<IReadOnlyList<IndexedCallEdge>>([]);
            return Task.FromResult<IReadOnlyList<IndexedCallEdge>>(entry.Edges.Take(limit).ToList());
        }

        /// <inheritdoc />
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
    }
}
