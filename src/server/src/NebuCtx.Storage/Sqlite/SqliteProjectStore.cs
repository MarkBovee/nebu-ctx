namespace NebuCtx.Storage.Sqlite;

using Microsoft.Data.Sqlite;
using NebuCtx.Contracts.Projects;

/// <summary>
/// SQLite implementation of <see cref="IProjectStore"/>.
/// Uses explicit SQL to preserve the current schema during migration.
/// </summary>
public sealed class SqliteProjectStore : IProjectStore
{
    private readonly string _connectionString;

    /// <summary>
    /// Initializes the SQLite project store.
    /// </summary>
    /// <param name="connectionString">SQLite connection string.</param>
    public SqliteProjectStore(string connectionString)
    {
        _connectionString = connectionString;
    }

    /// <inheritdoc />
    public async Task<ProjectRecord?> GetProjectAsync(string projectId, CancellationToken cancellationToken = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT project_id, slug, remote_url, host, owner, repo_name, default_branch, created_at, updated_at FROM projects WHERE project_id = @id";
        cmd.Parameters.AddWithValue("@id", projectId);

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return MapProjectRecord(reader);
    }

    /// <inheritdoc />
    public async Task<ProjectRecord?> FindByFingerprintAsync(RepositoryFingerprint fingerprint, CancellationToken cancellationToken = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            """
            SELECT project_id, slug, remote_url, host, owner, repo_name, default_branch, created_at, updated_at
            FROM projects
            WHERE remote_url = @remote_url
               OR (host = @host AND owner = @owner AND repo_name = @repo_name)
            """;
        cmd.Parameters.AddWithValue("@remote_url", (object?)fingerprint.RemoteUrl ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@host", (object?)fingerprint.Host ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@owner", (object?)fingerprint.Owner ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@repo_name", (object?)fingerprint.RepoName ?? DBNull.Value);

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        ProjectRecord? match = null;
        var count = 0;

        while (await reader.ReadAsync(cancellationToken))
        {
            count++;
            match = MapProjectRecord(reader);
        }

        return count == 1 ? match : null;
    }

    /// <inheritdoc />
    public async Task CreateProjectAsync(ProjectRecord project, CancellationToken cancellationToken = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            """
            INSERT INTO projects (project_id, slug, remote_url, host, owner, repo_name, default_branch, created_at, updated_at)
            VALUES (@project_id, @slug, @remote_url, @host, @owner, @repo_name, @default_branch, @created_at, @updated_at)
            """;
        BindProjectParameters(cmd, project);

        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task UpdateProjectAsync(ProjectRecord project, CancellationToken cancellationToken = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            """
            UPDATE projects SET slug = @slug, remote_url = @remote_url, host = @host, owner = @owner,
                repo_name = @repo_name, default_branch = @default_branch, updated_at = @updated_at
            WHERE project_id = @project_id
            """;
        BindProjectParameters(cmd, project);

        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ProjectRecord>> ListProjectsAsync(CancellationToken cancellationToken = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT project_id, slug, remote_url, host, owner, repo_name, default_branch, created_at, updated_at FROM projects ORDER BY created_at";

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        var projects = new List<ProjectRecord>();

        while (await reader.ReadAsync(cancellationToken))
        {
            projects.Add(MapProjectRecord(reader));
        }

        return projects;
    }

    /// <summary>
    /// Maps a data reader row to a <see cref="ProjectRecord"/>.
    /// </summary>
    private static ProjectRecord MapProjectRecord(SqliteDataReader reader)
    {
        return new ProjectRecord
        {
            ProjectId = reader.GetString(0),
            Slug = reader.GetString(1),
            Fingerprint = new RepositoryFingerprint
            {
                RemoteUrl = reader.IsDBNull(2) ? null : reader.GetString(2),
                Host = reader.IsDBNull(3) ? null : reader.GetString(3),
                Owner = reader.IsDBNull(4) ? null : reader.GetString(4),
                RepoName = reader.IsDBNull(5) ? null : reader.GetString(5),
                DefaultBranch = reader.IsDBNull(6) ? null : reader.GetString(6),
            },
            CreatedAt = DateTimeOffset.Parse(reader.GetString(7)),
            UpdatedAt = DateTimeOffset.Parse(reader.GetString(8)),
        };
    }

    /// <summary>
    /// Binds project parameters to a SQLite command.
    /// </summary>
    private static void BindProjectParameters(SqliteCommand cmd, ProjectRecord project)
    {
        cmd.Parameters.AddWithValue("@project_id", project.ProjectId);
        cmd.Parameters.AddWithValue("@slug", project.Slug);
        cmd.Parameters.AddWithValue("@remote_url", (object?)project.Fingerprint?.RemoteUrl ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@host", (object?)project.Fingerprint?.Host ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@owner", (object?)project.Fingerprint?.Owner ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@repo_name", (object?)project.Fingerprint?.RepoName ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@default_branch", (object?)project.Fingerprint?.DefaultBranch ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@created_at", project.CreatedAt.ToString("O"));
        cmd.Parameters.AddWithValue("@updated_at", project.UpdatedAt.ToString("O"));
    }
}
