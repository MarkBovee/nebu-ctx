namespace NebuCtx.Storage.Postgres;

using Npgsql;

/// <summary>
/// Creates the required database schema for Postgres.
/// Runs additive migrations on startup without dropping existing data.
/// </summary>
public static class PostgresSchemaInitializer
{
    /// <summary>
    /// Ensures all required tables exist. Additive only — never drops or alters existing columns.
    /// </summary>
    /// <param name="connectionString">Postgres connection string.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public static async Task EnsureSchemaAsync(string connectionString, CancellationToken cancellationToken = default)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync(cancellationToken);

        await using var cmd = new NpgsqlCommand(SchemaSql, conn);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
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
            created_at   TIMESTAMPTZ NOT NULL DEFAULT NOW(),
            updated_at   TIMESTAMPTZ NOT NULL DEFAULT NOW()
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
            last_sync    TIMESTAMPTZ,
            PRIMARY KEY (project_id, local_root)
        );

        CREATE TABLE IF NOT EXISTS brain_entries (
            project_id   TEXT NOT NULL,
            key          TEXT NOT NULL,
            value        TEXT NOT NULL,
            created_at   TIMESTAMPTZ NOT NULL DEFAULT NOW(),
            PRIMARY KEY (project_id, key)
        );

        CREATE INDEX IF NOT EXISTS idx_brain_entries_project
            ON brain_entries (project_id);
        """;
}
