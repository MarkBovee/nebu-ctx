namespace NebuCtx.Storage.Postgres;

using Npgsql;
using NebuCtx.Contracts.Projects;
using System.Text.Json;

/// <summary>
/// Postgres implementation of <see cref="IProjectStore"/>.
/// Uses explicit SQL to preserve the current schema and behavior during migration.
/// </summary>
public sealed class PostgresProjectStore : IProjectStore
{
    private readonly string _connectionString;

    /// <summary>
    /// Initializes the Postgres project store.
    /// </summary>
    /// <param name="connectionString">Postgres connection string.</param>
    public PostgresProjectStore(string connectionString)
    {
        _connectionString = connectionString;
    }

    /// <inheritdoc />
    public async Task<ProjectRecord?> GetProjectAsync(string projectId, CancellationToken cancellationToken = default)
    {
        await using var conn = await OpenConnectionAsync(cancellationToken);
        await using var cmd = new NpgsqlCommand(
            "SELECT project_id, slug, remote_url, host, owner, repo_name, default_branch, project_metadata_json::text, created_at, updated_at FROM projects WHERE project_id = @id",
            conn);
        cmd.Parameters.AddWithValue("id", projectId);

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
        await using var conn = await OpenConnectionAsync(cancellationToken);

        // Match on remote_url first (most specific), then host+owner+repo_name
        await using var cmd = new NpgsqlCommand(
            """
            SELECT project_id, slug, remote_url, host, owner, repo_name, default_branch, project_metadata_json::text, created_at, updated_at
            FROM projects
            WHERE remote_url = @remote_url
               OR (host = @host AND owner = @owner AND repo_name = @repo_name)
            """,
            conn);
        cmd.Parameters.AddWithValue("remote_url", (object?)fingerprint.RemoteUrl ?? DBNull.Value);
        cmd.Parameters.AddWithValue("host", (object?)fingerprint.Host ?? DBNull.Value);
        cmd.Parameters.AddWithValue("owner", (object?)fingerprint.Owner ?? DBNull.Value);
        cmd.Parameters.AddWithValue("repo_name", (object?)fingerprint.RepoName ?? DBNull.Value);

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        ProjectRecord? match = null;
        var count = 0;

        while (await reader.ReadAsync(cancellationToken))
        {
            count++;
            match = MapProjectRecord(reader);
        }

        // Return null if ambiguous (more than one match)
        return count == 1 ? match : null;
    }

    /// <inheritdoc />
    public async Task CreateProjectAsync(ProjectRecord project, CancellationToken cancellationToken = default)
    {
        await using var conn = await OpenConnectionAsync(cancellationToken);
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO projects (project_id, slug, remote_url, host, owner, repo_name, default_branch, project_metadata_json, created_at, updated_at)
            VALUES (@project_id, @slug, @remote_url, @host, @owner, @repo_name, @default_branch, CAST(@project_metadata_json AS jsonb), @created_at, @updated_at)
            """,
            conn);

        BindProjectParameters(cmd, project);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task UpdateProjectAsync(ProjectRecord project, CancellationToken cancellationToken = default)
    {
        await using var conn = await OpenConnectionAsync(cancellationToken);
        await using var cmd = new NpgsqlCommand(
            """
            UPDATE projects SET slug = @slug, remote_url = @remote_url, host = @host, owner = @owner,
                repo_name = @repo_name, default_branch = @default_branch, project_metadata_json = CAST(@project_metadata_json AS jsonb), updated_at = @updated_at
            WHERE project_id = @project_id
            """,
            conn);

        BindProjectParameters(cmd, project);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ProjectRecord>> ListProjectsAsync(CancellationToken cancellationToken = default)
    {
        await using var conn = await OpenConnectionAsync(cancellationToken);
        await using var cmd = new NpgsqlCommand(
            "SELECT project_id, slug, remote_url, host, owner, repo_name, default_branch, project_metadata_json::text, created_at, updated_at FROM projects ORDER BY created_at",
            conn);

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        var projects = new List<ProjectRecord>();

        while (await reader.ReadAsync(cancellationToken))
        {
            projects.Add(MapProjectRecord(reader));
        }

        return projects;
    }

    /// <inheritdoc />
    public async Task<bool> DeleteProjectAsync(string projectId, CancellationToken cancellationToken = default)
    {
        await using var conn = await OpenConnectionAsync(cancellationToken);
        await using var cmd = new NpgsqlCommand("DELETE FROM projects WHERE project_id = @project_id", conn);
        cmd.Parameters.AddWithValue("project_id", projectId);
        return await cmd.ExecuteNonQueryAsync(cancellationToken) > 0;
    }

    /// <summary>
    /// Opens a new Postgres connection.
    /// </summary>
    private async Task<NpgsqlConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);
        return conn;
    }

    /// <summary>
    /// Maps a data reader row to a <see cref="ProjectRecord"/>.
    /// </summary>
    private static ProjectRecord MapProjectRecord(NpgsqlDataReader reader)
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
            ProjectMetadata = DeserializeProjectMetadata(reader.IsDBNull(7) ? null : reader.GetString(7)),
            CreatedAt = reader.GetFieldValue<DateTimeOffset>(8),
            UpdatedAt = reader.GetFieldValue<DateTimeOffset>(9),
        };
    }

    /// <summary>
    /// Binds project parameters to a command.
    /// </summary>
    private static void BindProjectParameters(NpgsqlCommand cmd, ProjectRecord project)
    {
        cmd.Parameters.AddWithValue("project_id", project.ProjectId);
        cmd.Parameters.AddWithValue("slug", project.Slug);
        cmd.Parameters.AddWithValue("remote_url", (object?)project.Fingerprint?.RemoteUrl ?? DBNull.Value);
        cmd.Parameters.AddWithValue("host", (object?)project.Fingerprint?.Host ?? DBNull.Value);
        cmd.Parameters.AddWithValue("owner", (object?)project.Fingerprint?.Owner ?? DBNull.Value);
        cmd.Parameters.AddWithValue("repo_name", (object?)project.Fingerprint?.RepoName ?? DBNull.Value);
        cmd.Parameters.AddWithValue("default_branch", (object?)project.Fingerprint?.DefaultBranch ?? DBNull.Value);
        cmd.Parameters.AddWithValue("project_metadata_json", (object?)SerializeProjectMetadata(project.ProjectMetadata) ?? DBNull.Value);
        cmd.Parameters.AddWithValue("created_at", project.CreatedAt);
        cmd.Parameters.AddWithValue("updated_at", project.UpdatedAt);
    }

    /// <summary>
    /// Serializes the compact project metadata snapshot for storage.
    /// </summary>
    /// <param name="projectMetadata">Project metadata snapshot.</param>
    /// <returns>JSON payload or null.</returns>
    private static string? SerializeProjectMetadata(ProjectMetadataEnvelope? projectMetadata)
    {
        return projectMetadata is null ? null : JsonSerializer.Serialize(projectMetadata);
    }

    /// <summary>
    /// Deserializes the compact project metadata snapshot from storage.
    /// </summary>
    /// <param name="value">Stored JSON payload.</param>
    /// <returns>Parsed project metadata snapshot or null.</returns>
    private static ProjectMetadataEnvelope? DeserializeProjectMetadata(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : JsonSerializer.Deserialize<ProjectMetadataEnvelope>(value);
    }
}
