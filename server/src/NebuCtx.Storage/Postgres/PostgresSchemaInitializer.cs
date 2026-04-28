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

        await EnsureProjectMetadataColumnAsync(conn, cancellationToken);
        await MigrateWorkspaceBindingsTableAsync(conn, cancellationToken);
    }

    /// <summary>
    /// Adds the compact project metadata column when upgrading existing Postgres databases.
    /// </summary>
    /// <param name="conn">Open Postgres connection.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    private static async Task EnsureProjectMetadataColumnAsync(NpgsqlConnection conn, CancellationToken cancellationToken)
    {
        await using var cmd = new NpgsqlCommand("ALTER TABLE projects ADD COLUMN IF NOT EXISTS project_metadata_json JSONB", conn);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>
    /// Renames the legacy workspace_bindings table to checkout_bindings when upgrading existing databases.
    /// Safe to run repeatedly — no-ops if the table has already been renamed.
    /// </summary>
    private static async Task MigrateWorkspaceBindingsTableAsync(NpgsqlConnection conn, CancellationToken cancellationToken)
    {
        await using var cmd = new NpgsqlCommand(
            """
            DO $$
            BEGIN
                IF EXISTS (SELECT 1 FROM information_schema.tables WHERE table_name = 'workspace_bindings')
                   AND NOT EXISTS (SELECT 1 FROM information_schema.tables WHERE table_name = 'checkout_bindings') THEN
                    ALTER TABLE workspace_bindings RENAME TO checkout_bindings;
                END IF;
            END;
            $$;
            """,
            conn);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>
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
            project_metadata_json JSONB,
            created_at   TIMESTAMPTZ NOT NULL DEFAULT NOW(),
            updated_at   TIMESTAMPTZ NOT NULL DEFAULT NOW()
        );

        CREATE INDEX IF NOT EXISTS idx_projects_fingerprint
            ON projects (host, owner, repo_name);

        CREATE INDEX IF NOT EXISTS idx_projects_remote_url
            ON projects (remote_url) WHERE remote_url IS NOT NULL;

        CREATE TABLE IF NOT EXISTS checkout_bindings (
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

        CREATE TABLE IF NOT EXISTS knowledge_entries (
            project_id  TEXT NOT NULL,
            category    TEXT NOT NULL,
            key         TEXT NOT NULL,
            value       TEXT NOT NULL,
            confidence  REAL NOT NULL DEFAULT 1.0,
            updated_at  TIMESTAMPTZ NOT NULL DEFAULT NOW(),
            PRIMARY KEY (project_id, category, key)
        );

        CREATE INDEX IF NOT EXISTS idx_knowledge_entries_project
            ON knowledge_entries (project_id);

        CREATE TABLE IF NOT EXISTS session_state (
            project_id  TEXT NOT NULL,
            session_id  TEXT NOT NULL,
            state_json  JSONB NOT NULL,
            created_at  TIMESTAMPTZ NOT NULL DEFAULT NOW(),
            updated_at  TIMESTAMPTZ NOT NULL DEFAULT NOW(),
            PRIMARY KEY (project_id, session_id)
        );

        CREATE INDEX IF NOT EXISTS idx_session_state_project
            ON session_state (project_id, updated_at DESC);

        CREATE TABLE IF NOT EXISTS telemetry_events (
            id              BIGSERIAL PRIMARY KEY,
            occurred_at     TIMESTAMPTZ NOT NULL DEFAULT NOW(),
            event_type      TEXT NOT NULL DEFAULT '',
            tool_name       TEXT NOT NULL DEFAULT '',
            mode            TEXT NOT NULL DEFAULT '',
            project_id      TEXT NOT NULL DEFAULT '',
            actor_label     TEXT NOT NULL DEFAULT 'anonymous',
            path            TEXT,
            tokens_original BIGINT NOT NULL DEFAULT 0,
            tokens_output   BIGINT NOT NULL DEFAULT 0,
            tokens_saved    BIGINT NOT NULL DEFAULT 0
        );

        CREATE INDEX IF NOT EXISTS idx_telemetry_events_occurred
            ON telemetry_events (occurred_at DESC);

        CREATE INDEX IF NOT EXISTS idx_telemetry_events_project
            ON telemetry_events (project_id, occurred_at DESC);
        """;
}
