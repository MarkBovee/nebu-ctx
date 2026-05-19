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
        await EnsureKnowledgeLifecycleColumnsAsync(conn, cancellationToken);
        await EnsureTelemetryCommandPreviewColumnAsync(conn, cancellationToken);
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
    /// Adds lifecycle metadata columns to knowledge_entries when upgrading existing databases.
    /// </summary>
    /// <param name="conn">Open Postgres connection.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    private static async Task EnsureKnowledgeLifecycleColumnsAsync(NpgsqlConnection conn, CancellationToken cancellationToken)
    {
        await using var cmd = new NpgsqlCommand(
            """
            ALTER TABLE knowledge_entries ADD COLUMN IF NOT EXISTS created_at TIMESTAMPTZ NOT NULL DEFAULT NOW();
            ALTER TABLE knowledge_entries ADD COLUMN IF NOT EXISTS logical_key TEXT NOT NULL DEFAULT '';
            ALTER TABLE knowledge_entries ADD COLUMN IF NOT EXISTS promotion_identity TEXT NOT NULL DEFAULT '';
            ALTER TABLE knowledge_entries ADD COLUMN IF NOT EXISTS source_type TEXT NOT NULL DEFAULT 'remember';
            ALTER TABLE knowledge_entries ADD COLUMN IF NOT EXISTS source_scope TEXT NOT NULL DEFAULT '';
            ALTER TABLE knowledge_entries ADD COLUMN IF NOT EXISTS lifecycle_status TEXT NOT NULL DEFAULT 'current';
            ALTER TABLE knowledge_entries ADD COLUMN IF NOT EXISTS lifecycle_score REAL NOT NULL DEFAULT 0.0;
            ALTER TABLE knowledge_entries ADD COLUMN IF NOT EXISTS confirmation_count INT NOT NULL DEFAULT 1;
            ALTER TABLE knowledge_entries ADD COLUMN IF NOT EXISTS last_confirmed_at TIMESTAMPTZ;
            ALTER TABLE knowledge_entries ADD COLUMN IF NOT EXISTS retrieval_count INT NOT NULL DEFAULT 0;
            ALTER TABLE knowledge_entries ADD COLUMN IF NOT EXISTS last_retrieved_at TIMESTAMPTZ;
            ALTER TABLE knowledge_entries ADD COLUMN IF NOT EXISTS history_json JSONB NOT NULL DEFAULT '[]'::jsonb;
            UPDATE knowledge_entries
            SET logical_key = CASE WHEN logical_key = '' THEN key ELSE logical_key END,
                promotion_identity = CASE WHEN promotion_identity = '' THEN concat('legacy:', project_id, ':', category, ':', key) ELSE promotion_identity END,
                source_scope = CASE WHEN source_scope = '' THEN project_id ELSE source_scope END,
                lifecycle_score = CASE WHEN lifecycle_score = 0.0 THEN confidence ELSE lifecycle_score END,
                last_confirmed_at = COALESCE(last_confirmed_at, updated_at),
                created_at = COALESCE(created_at, updated_at)
            WHERE logical_key = ''
               OR promotion_identity = ''
               OR source_scope = ''
               OR lifecycle_score = 0.0
               OR last_confirmed_at IS NULL;
            """,
            conn);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>
    /// Adds the optional command preview column for telemetry event detail.
    /// </summary>
    /// <param name="conn">Open Postgres connection.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    private static async Task EnsureTelemetryCommandPreviewColumnAsync(NpgsqlConnection conn, CancellationToken cancellationToken)
    {
        await using var cmd = new NpgsqlCommand("ALTER TABLE telemetry_events ADD COLUMN IF NOT EXISTS command_preview TEXT", conn);
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
            created_at  TIMESTAMPTZ NOT NULL DEFAULT NOW(),
            updated_at  TIMESTAMPTZ NOT NULL DEFAULT NOW(),
            logical_key TEXT NOT NULL DEFAULT '',
            promotion_identity TEXT NOT NULL DEFAULT '',
            source_type TEXT NOT NULL DEFAULT 'remember',
            source_scope TEXT NOT NULL DEFAULT '',
            lifecycle_status TEXT NOT NULL DEFAULT 'current',
            lifecycle_score REAL NOT NULL DEFAULT 0.0,
            confirmation_count INT NOT NULL DEFAULT 1,
            last_confirmed_at TIMESTAMPTZ,
            retrieval_count INT NOT NULL DEFAULT 0,
            last_retrieved_at TIMESTAMPTZ,
            history_json JSONB NOT NULL DEFAULT '[]'::jsonb,
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
            command_preview TEXT,
            tokens_original BIGINT NOT NULL DEFAULT 0,
            tokens_output   BIGINT NOT NULL DEFAULT 0,
            tokens_saved    BIGINT NOT NULL DEFAULT 0
        );

        CREATE INDEX IF NOT EXISTS idx_telemetry_events_occurred
            ON telemetry_events (occurred_at DESC);

        CREATE INDEX IF NOT EXISTS idx_telemetry_events_project
            ON telemetry_events (project_id, occurred_at DESC);

        CREATE TABLE IF NOT EXISTS project_files (
            project_id   TEXT NOT NULL,
            path         TEXT NOT NULL,
            hash         TEXT NOT NULL DEFAULT '',
            language     TEXT NOT NULL DEFAULT '',
            line_count   INT NOT NULL DEFAULT 0,
            token_count  INT NOT NULL DEFAULT 0,
            exports_json JSONB,
            summary      TEXT NOT NULL DEFAULT '',
            indexed_at   TIMESTAMPTZ NOT NULL DEFAULT NOW(),
            PRIMARY KEY (project_id, path)
        );

        CREATE INDEX IF NOT EXISTS idx_project_files_project
            ON project_files (project_id);

        CREATE TABLE IF NOT EXISTS project_symbols (
            project_id   TEXT NOT NULL,
            file_path    TEXT NOT NULL,
            name         TEXT NOT NULL,
            kind         TEXT NOT NULL DEFAULT '',
            start_line   INT NOT NULL DEFAULT 0,
            end_line     INT NOT NULL DEFAULT 0,
            is_exported  BOOLEAN NOT NULL DEFAULT FALSE,
            indexed_at   TIMESTAMPTZ NOT NULL DEFAULT NOW(),
            PRIMARY KEY (project_id, file_path, name)
        );

        CREATE INDEX IF NOT EXISTS idx_project_symbols_project
            ON project_symbols (project_id);

        CREATE INDEX IF NOT EXISTS idx_project_symbols_name
            ON project_symbols (project_id, name);

        CREATE TABLE IF NOT EXISTS project_call_edges (
            project_id   TEXT NOT NULL,
            from_symbol  TEXT NOT NULL,
            to_symbol    TEXT NOT NULL,
            kind         TEXT NOT NULL DEFAULT '',
            indexed_at   TIMESTAMPTZ NOT NULL DEFAULT NOW(),
            PRIMARY KEY (project_id, from_symbol, to_symbol)
        );

        CREATE INDEX IF NOT EXISTS idx_project_call_edges_project
            ON project_call_edges (project_id);
        """;
}
