namespace NebuCtx.ProjectIdentityTests;

using System.Collections.Concurrent;
using NebuCtx.Contracts.Projects;
using NebuCtx.Server.Core;
using NebuCtx.Storage;

/// <summary>
/// Tests that the project identity model resolves correctly across different local roots.
/// </summary>
public class ProjectResolutionTests
{
    private ProjectRegistry _registry = null!;
    private InMemoryProjectStore _projectStore = null!;

    /// <summary>
    /// Sets up a fresh in-memory registry for each test.
    /// </summary>
    public ProjectResolutionTests()
    {
        _projectStore = new InMemoryProjectStore();
        var bindingStore = new InMemoryCheckoutBindingStore();
        _registry = new ProjectRegistry(
            _projectStore,
            bindingStore,
            new InMemoryBrainStore(),
            new InMemoryKnowledgeStore(),
            new InMemorySessionStore(),
            new InMemoryCodeIndexStore());
    }

    /// <summary>
    /// Same repository fingerprint from two different local roots resolves to the same project.
    /// </summary>
    [Fact]
    public async Task SameRepo_DifferentRoots_SameProject()
    {
        var fingerprint = new RepositoryFingerprint
        {
            RemoteUrl = "https://github.com/MarkBovee/nebu-ctx.git",
            Host = "github.com",
            Owner = "MarkBovee",
            RepoName = "nebu-ctx",
            DefaultBranch = "main",
        };

        // First resolution — creates the project
        var projectFromRootA = await _registry.ResolveOrCreateAsync(fingerprint, "nebu-ctx");
        Assert.NotNull(projectFromRootA);

        // Second resolution from a different local path — should find the same project
        var projectFromRootB = await _registry.ResolveOrCreateAsync(fingerprint, "nebu-ctx");
        Assert.NotNull(projectFromRootB);

        // Same project identity across both roots
        Assert.Equal(projectFromRootA.ProjectId, projectFromRootB.ProjectId);
    }

    /// <summary>
    /// Different repositories get different project identities.
    /// </summary>
    [Fact]
    public async Task DifferentRepos_DifferentProjects()
    {
        var fingerprintA = new RepositoryFingerprint
        {
            RemoteUrl = "https://github.com/MarkBovee/nebu-ctx.git",
            Host = "github.com",
            Owner = "MarkBovee",
            RepoName = "nebu-ctx",
        };

        var fingerprintB = new RepositoryFingerprint
        {
            RemoteUrl = "https://github.com/MarkBovee/nebula-rag.git",
            Host = "github.com",
            Owner = "MarkBovee",
            RepoName = "nebula-rag",
        };

        var projectA = await _registry.ResolveOrCreateAsync(fingerprintA, "nebu-ctx");
        var projectB = await _registry.ResolveOrCreateAsync(fingerprintB, "nebula-rag");

        Assert.NotEqual(projectA!.ProjectId, projectB!.ProjectId);
    }

    /// <summary>
    /// Checkout bindings are tracked per project.
    /// </summary>
    [Fact]
    public async Task CheckoutBinding_TrackedPerProject()
    {
        var fingerprint = new RepositoryFingerprint
        {
            RemoteUrl = "https://github.com/MarkBovee/nebu-ctx.git",
            Host = "github.com",
            Owner = "MarkBovee",
            RepoName = "nebu-ctx",
        };

        var project = await _registry.ResolveOrCreateAsync(fingerprint, "nebu-ctx");

        await _registry.BindCheckoutAsync(new CheckoutBinding
        {
            ProjectId = project!.ProjectId,
            LocalRoot = "/home/user/projects/nebu-ctx",
            Branch = "main",
            ClientLabel = "laptop",
            LastSync = DateTimeOffset.UtcNow,
        });

        await _registry.BindCheckoutAsync(new CheckoutBinding
        {
            ProjectId = project.ProjectId,
            LocalRoot = "C:\\Projects\\nebu-ctx",
            Branch = "main",
            ClientLabel = "desktop",
            LastSync = DateTimeOffset.UtcNow,
        });

        var bindings = await _registry.GetBindingsAsync(project.ProjectId);
        Assert.Equal(2, bindings.Count);
    }

    /// <summary>
    /// Project metadata snapshots persist on the canonical project record.
    /// </summary>
    [Fact]
    public async Task ProjectMetadata_PersistsOnProjectRecord()
    {
        var fingerprint = new RepositoryFingerprint
        {
            RemoteUrl = "https://github.com/MarkBovee/nebu-ctx.git",
            Host = "github.com",
            Owner = "MarkBovee",
            RepoName = "nebu-ctx",
        };

        var project = await _registry.ResolveOrCreateAsync(
            fingerprint,
            "nebu-ctx",
            new ProjectMetadataEnvelope
            {
                SchemaVersion = 1,
                Summary = new ProjectMetadataSummary
                {
                    TotalFileCount = 20,
                    SourceFileCount = 8,
                    Markers = [".git", "Cargo.toml"],
                    Languages = [new ProjectLanguageStat { Language = "rust", FileCount = 8 }],
                },
            });

        Assert.NotNull(project);
        Assert.NotNull(project!.ProjectMetadata);
        Assert.Equal(8, project.ProjectMetadata!.Summary.SourceFileCount);

        var reloaded = await _registry.GetAsync(project.ProjectId);
        Assert.NotNull(reloaded);
        Assert.NotNull(reloaded!.ProjectMetadata);
        Assert.Equal("rust", reloaded.ProjectMetadata!.Summary.Languages[0].Language);
    }

    /// <summary>
    /// Unsafe fingerprints must not create canonical project records.
    /// </summary>
    [Fact]
    public async Task UnsafeFingerprint_DoesNotResolveOrCreateProject()
    {
        var fingerprint = new RepositoryFingerprint();

        var project = await _registry.ResolveOrCreateAsync(fingerprint, "project-markb");

        Assert.Null(project);
    }

    /// <summary>
    /// Safe repository fingerprints still create canonical project records.
    /// </summary>
    [Fact]
    public async Task SafeFingerprint_StillCreatesProject()
    {
        var fingerprint = new RepositoryFingerprint
        {
            RemoteUrl = "https://github.com/MarkBovee/nebu-ctx.git",
            Host = "github.com",
            Owner = "MarkBovee",
            RepoName = "nebu-ctx",
        };

        var project = await _registry.ResolveOrCreateAsync(fingerprint, "nebu-ctx");

        Assert.NotNull(project);
        Assert.Equal("nebu-ctx", project!.Slug);
    }

    /// <summary>
    /// Ambiguous duplicate projects must not create a fresh canonical project.
    /// </summary>
    [Fact]
    public async Task AmbiguousFingerprint_DoesNotCreateAnotherProject()
    {
        var fingerprint = new RepositoryFingerprint
        {
            RemoteUrl = "https://github.com/MarkBovee/ha-addons.git",
            Host = "github.com",
            Owner = "MarkBovee",
            RepoName = "ha-addons",
            DefaultBranch = "master",
        };

        await _projectStore.CreateProjectAsync(new ProjectRecord
        {
            ProjectId = "proj_a",
            Slug = "ha-addons",
            Fingerprint = fingerprint,
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-1),
            UpdatedAt = DateTimeOffset.UtcNow.AddMinutes(-1),
        });
        await _projectStore.CreateProjectAsync(new ProjectRecord
        {
            ProjectId = "proj_b",
            Slug = "ha-addons",
            Fingerprint = fingerprint,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });

        var resolved = await _registry.ResolveOrCreateAsync(fingerprint, "ha-addons");

        Assert.Null(resolved);
        var allProjects = await _projectStore.ListProjectsAsync();
        Assert.Equal(2, allProjects.Count);
    }

    /// <summary>
    /// In-memory project store used to keep the test independent from runtime database providers.
    /// </summary>
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
            return ListByFingerprintAsync(fingerprint, cancellationToken)
                .ContinueWith(task => task.Result.Count == 1 ? task.Result[0] : null, cancellationToken);
        }

        /// <inheritdoc />
        public Task<IReadOnlyList<ProjectRecord>> ListByFingerprintAsync(RepositoryFingerprint fingerprint, CancellationToken cancellationToken = default)
        {
            var projects = _projects.Values
                .Where(project => project.Fingerprint is not null && FingerprintsMatch(project.Fingerprint, fingerprint))
                .ToList();
            return Task.FromResult<IReadOnlyList<ProjectRecord>>(projects);
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
        public Task<IReadOnlyList<ProjectRecord>> ListProjectsAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<ProjectRecord>>(_projects.Values.ToList());
        }

        /// <inheritdoc />
        public Task<bool> DeleteProjectAsync(string projectId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_projects.TryRemove(projectId, out _));
        }

        /// <summary>
        /// Matches two repository fingerprints using the fields the registry depends on for identity.
        /// </summary>
        /// <param name="left">Stored fingerprint.</param>
        /// <param name="right">Requested fingerprint.</param>
        /// <returns>True when the fingerprints identify the same repository.</returns>
        private static bool FingerprintsMatch(RepositoryFingerprint left, RepositoryFingerprint right)
        {
            return string.Equals(left.RemoteUrl, right.RemoteUrl, StringComparison.OrdinalIgnoreCase)
                && string.Equals(left.Host, right.Host, StringComparison.OrdinalIgnoreCase)
                && string.Equals(left.Owner, right.Owner, StringComparison.OrdinalIgnoreCase)
                && string.Equals(left.RepoName, right.RepoName, StringComparison.OrdinalIgnoreCase)
                && string.Equals(left.DefaultBranch, right.DefaultBranch, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// In-memory checkout binding store used by the registry tests.
    /// </summary>
    private sealed class InMemoryCheckoutBindingStore : ICheckoutBindingStore
    {
        private readonly ConcurrentDictionary<string, List<CheckoutBinding>> _bindingsByProject = new();

        /// <inheritdoc />
        public Task UpsertBindingAsync(CheckoutBinding binding, CancellationToken cancellationToken = default)
        {
            var bindings = _bindingsByProject.GetOrAdd(binding.ProjectId, _ => []);
            var existingIndex = bindings.FindIndex(existing => string.Equals(existing.LocalRoot, binding.LocalRoot, StringComparison.OrdinalIgnoreCase));

            if (existingIndex >= 0)
            {
                bindings[existingIndex] = binding;
            }
            else
            {
                bindings.Add(binding);
            }

            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task<IReadOnlyList<CheckoutBinding>> GetBindingsAsync(string projectId, CancellationToken cancellationToken = default)
        {
            var bindings = _bindingsByProject.TryGetValue(projectId, out var storedBindings)
                ? storedBindings.ToList()
                : [];

            return Task.FromResult<IReadOnlyList<CheckoutBinding>>(bindings);
        }

        /// <inheritdoc />
        public Task<int> ClearProjectAsync(string projectId, CancellationToken cancellationToken = default)
        {
            if (!_bindingsByProject.TryRemove(projectId, out var bindings))
            {
                return Task.FromResult(0);
            }

            return Task.FromResult(bindings.Count);
        }
    }

    private sealed class InMemoryBrainStore : IBrainStore
    {
        public Task<Dictionary<string, object?>?> GetStatusAsync(string projectId, CancellationToken cancellationToken = default)
            => Task.FromResult<Dictionary<string, object?>?>(null);

        public Task StoreAsync(string projectId, string key, string value, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<IReadOnlyList<BrainEntry>> RecallAsync(string projectId, string query, int limit = 10, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<BrainEntry>>([]);

        public Task<IReadOnlyList<BrainEntry>> ListAllAsync(string projectId, int limit = 200, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<BrainEntry>>([]);

        public Task<bool> DeleteAsync(string projectId, string key, CancellationToken cancellationToken = default)
            => Task.FromResult(false);

        public Task<int> ClearProjectAsync(string projectId, CancellationToken cancellationToken = default)
            => Task.FromResult(0);

        public Task<int> DeleteByPrefixAsync(string projectId, string keyPrefix, CancellationToken cancellationToken = default)
            => Task.FromResult(0);
    }

    private sealed class InMemoryKnowledgeStore : IKnowledgeStore
    {
        public Task UpsertFactAsync(KnowledgeEntry entry, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<KnowledgeEntry?> GetFactAsync(string projectId, string category, string key, CancellationToken cancellationToken = default) => Task.FromResult<KnowledgeEntry?>(null);
        public Task<IReadOnlyList<KnowledgeEntry>> RecallAsync(string projectId, string? category, string query, int limit, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<KnowledgeEntry>>([]);
        public Task<IReadOnlyList<(string Category, int Count)>> GetCategoriesAsync(string projectId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<(string Category, int Count)>>([]);
        public Task<int> GetFactCountAsync(string projectId, CancellationToken cancellationToken = default) => Task.FromResult(0);
        public Task<IReadOnlyList<KnowledgeEntry>> ListAllForProjectAsync(string projectId, int limit = 500, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<KnowledgeEntry>>([]);
        public Task<bool> RemoveFactAsync(string projectId, string category, string key, CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task<int> ClearProjectAsync(string projectId, CancellationToken cancellationToken = default) => Task.FromResult(0);
        public Task<int> ReassignProjectAsync(string fromProjectId, string toProjectId, CancellationToken cancellationToken = default) => Task.FromResult(0);
    }

    private sealed class InMemorySessionStore : ISessionStore
    {
        public Task<CloudSessionState?> LoadLatestAsync(string projectId, CancellationToken cancellationToken = default) => Task.FromResult<CloudSessionState?>(null);
        public Task<CloudSessionState?> LoadByIdAsync(string projectId, string sessionId, CancellationToken cancellationToken = default) => Task.FromResult<CloudSessionState?>(null);
        public Task SaveAsync(string projectId, CloudSessionState state, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<IReadOnlyList<CloudSessionSummary>> ListAsync(string projectId, int limit, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<CloudSessionSummary>>([]);
        public Task<int> DeleteOlderThanAsync(string projectId, int daysOld, CancellationToken cancellationToken = default) => Task.FromResult(0);
        public Task<int> ClearProjectAsync(string projectId, CancellationToken cancellationToken = default) => Task.FromResult(0);
    }

    private sealed class InMemoryCodeIndexStore : ICodeIndexStore
    {
        public Task SyncIndexAsync(string projectId, IReadOnlyList<IndexedFile> files, IReadOnlyList<IndexedSymbol> symbols, IReadOnlyList<IndexedCallEdge> edges, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<CodeIndexStats> GetStatsAsync(string projectId, CancellationToken cancellationToken = default) => Task.FromResult(new CodeIndexStats());
        public Task<IReadOnlyList<IndexedSymbol>> SearchSymbolsAsync(string projectId, string? query, string? kind, int limit = 200, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<IndexedSymbol>>([]);
        public Task<IReadOnlyList<IndexedCallEdge>> GetEdgesAsync(string projectId, int limit = 5000, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<IndexedCallEdge>>([]);
        public Task<IReadOnlyList<IndexedFile>> SearchFilesAsync(string projectId, string? query, int limit = 100, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<IndexedFile>>([]);
        public Task<bool> ClearProjectAsync(string projectId, CancellationToken cancellationToken = default) => Task.FromResult(false);
    }
}
