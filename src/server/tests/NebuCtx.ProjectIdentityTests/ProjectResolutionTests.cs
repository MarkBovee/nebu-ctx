namespace NebuCtx.ProjectIdentityTests;

using NebuCtx.Contracts.Projects;
using NebuCtx.Projects;
using NebuCtx.Storage;
using NebuCtx.Storage.Sqlite;

/// <summary>
/// Tests that the project identity model resolves correctly across different local roots.
/// </summary>
public class ProjectResolutionTests : IAsyncLifetime
{
    private string _testDbPath = null!;
    private string _connectionString = null!;
    private ProjectRegistry _registry = null!;

    /// <summary>
    /// Sets up a fresh SQLite database and registry for each test.
    /// </summary>
    public async Task InitializeAsync()
    {
        _testDbPath = $"test_proj_{Guid.NewGuid():N}.db";

        _connectionString = $"Data Source={_testDbPath}";
        await SqliteSchemaInitializer.EnsureSchemaAsync(_connectionString);

        var projectStore = new SqliteProjectStore(_connectionString);
        var bindingStore = new SqliteWorkspaceBindingStore(_connectionString);
        _registry = new ProjectRegistry(projectStore, bindingStore);
    }

    /// <summary>
    /// Cleans up the test database.
    /// </summary>
    public Task DisposeAsync()
    {
        // SQLite connections are pooled; clear pool before deleting
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        if (File.Exists(_testDbPath))
        {
            File.Delete(_testDbPath);
        }
        return Task.CompletedTask;
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
}
