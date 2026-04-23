namespace NebuCtx.ProjectIdentityTests;

using System.Collections.Concurrent;
using NebuCtx.Contracts.Projects;
using NebuCtx.Projects;
using NebuCtx.Storage;

/// <summary>
/// Tests that the project identity model resolves correctly across different local roots.
/// </summary>
public class ProjectResolutionTests
{
    private ProjectRegistry _registry = null!;

    /// <summary>
    /// Sets up a fresh in-memory registry for each test.
    /// </summary>
    public ProjectResolutionTests()
    {
        var projectStore = new InMemoryProjectStore();
        var bindingStore = new InMemoryWorkspaceBindingStore();
        _registry = new ProjectRegistry(projectStore, bindingStore);
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
    /// Workspace bindings are tracked per project.
    /// </summary>
    [Fact]
    public async Task WorkspaceBinding_TrackedPerProject()
    {
        var fingerprint = new RepositoryFingerprint
        {
            RemoteUrl = "https://github.com/MarkBovee/nebu-ctx.git",
            Host = "github.com",
            Owner = "MarkBovee",
            RepoName = "nebu-ctx",
        };

        var project = await _registry.ResolveOrCreateAsync(fingerprint, "nebu-ctx");

        await _registry.BindWorkspaceAsync(new WorkspaceBinding
        {
            ProjectId = project!.ProjectId,
            LocalRoot = "/home/user/projects/nebu-ctx",
            Branch = "main",
            ClientLabel = "laptop",
            LastSync = DateTimeOffset.UtcNow,
        });

        await _registry.BindWorkspaceAsync(new WorkspaceBinding
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
            var project = _projects.Values.FirstOrDefault(project => FingerprintsMatch(project.Fingerprint, fingerprint));
            return Task.FromResult(project);
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
    /// In-memory workspace binding store used by the registry tests.
    /// </summary>
    private sealed class InMemoryWorkspaceBindingStore : IWorkspaceBindingStore
    {
        private readonly ConcurrentDictionary<string, List<WorkspaceBinding>> _bindingsByProject = new();

        /// <inheritdoc />
        public Task UpsertBindingAsync(WorkspaceBinding binding, CancellationToken cancellationToken = default)
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
        public Task<IReadOnlyList<WorkspaceBinding>> GetBindingsAsync(string projectId, CancellationToken cancellationToken = default)
        {
            var bindings = _bindingsByProject.TryGetValue(projectId, out var storedBindings)
                ? storedBindings.ToList()
                : [];

            return Task.FromResult<IReadOnlyList<WorkspaceBinding>>(bindings);
        }
    }
}
