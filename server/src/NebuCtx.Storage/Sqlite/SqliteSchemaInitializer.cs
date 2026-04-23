namespace NebuCtx.Storage.Sqlite;

using Microsoft.Data.Sqlite;

/// <summary>
/// Creates the required database schema for SQLite.
/// Runs additive migrations on startup without dropping existing data.
/// </summary>
public static class SqliteSchemaInitializer
{
    /// <summary>
    /// Ensures all required tables exist. Additive only — never drops or alters existing columns.
    /// </summary>
    /// <param name="connectionString">SQLite connection string.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public static async Task EnsureSchemaAsync(string connectionString, CancellationToken cancellationToken = default)
    {
        await using var conn = new SqliteConnection(connectionString);
        await conn.OpenAsync(cancellationToken);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = SchemaSql;
        await cmd.ExecuteNonQueryAsync(cancellationToken);

        await EnsureProjectMetadataColumnAsync(conn, cancellationToken);
    }

    /// <summary>
    /// Adds the compact project metadata column when upgrading existing SQLite databases.
    /// </summary>
    /// <param name="conn">Open SQLite connection.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    private static async Task EnsureProjectMetadataColumnAsync(SqliteConnection conn, CancellationToken cancellationToken)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "ALTER TABLE projects ADD COLUMN project_metadata_json TEXT";

        try
        {
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 1 && ex.Message.Contains("duplicate column name", StringComparison.OrdinalIgnoreCase))
        {
        }
    }

    /// <summary>
    /// Additive schema DDL. Uses IF NOT EXISTS to be safe for repeated runs.
    /// </summary>
    private const string SchemaSql =
        """
        CREATE TABLE IF NOT EXISTS projects (
            project_id   TEXT PRIMARY KEY,
            slug         TEXT NOT NULL,
            remote_url   TEXT,
            host         TEXT,
            owner        TEXT,
            repo_name    TEXT,
            default_branch TEXT,
            project_metadata_json TEXT,
            created_at   TEXT NOT NULL,
            updated_at   TEXT NOT NULL
        );

        CREATE INDEX IF NOT EXISTS idx_projects_fingerprint
            ON projects (host, owner, repo_name);

        CREATE INDEX IF NOT EXISTS idx_projects_remote_url
            ON projects (remote_url) WHERE remote_url IS NOT NULL;

        CREATE TABLE IF NOT EXISTS workspace_bindings (
            project_id   TEXT NOT NULL REFERENCES projects(project_id),
            local_root   TEXT,
            branch       TEXT,
            last_commit  TEXT,
            client_label TEXT,
            last_sync    TEXT,
            PRIMARY KEY (project_id, local_root)
        );

        CREATE TABLE IF NOT EXISTS brain_entries (
            project_id   TEXT NOT NULL,
            key          TEXT NOT NULL,
            value        TEXT NOT NULL,
            created_at   TEXT NOT NULL,
            PRIMARY KEY (project_id, key)
        );

        CREATE INDEX IF NOT EXISTS idx_brain_entries_project
            ON brain_entries (project_id);
        """;
}
