namespace NebuCtx.Storage.Postgres;

using Npgsql;

/// <summary>
/// Postgres implementation of <see cref="IBrainStore"/>.
/// Provides project-scoped brain memory (ctx_brain) operations.
/// </summary>
public sealed class PostgresBrainStore : IBrainStore
{
    private readonly string _connectionString;

    /// <summary>
    /// Initializes the Postgres brain store.
    /// </summary>
    /// <param name="connectionString">Postgres connection string.</param>
    public PostgresBrainStore(string connectionString)
    {
        _connectionString = connectionString;
    }

    /// <inheritdoc />
    public async Task<Dictionary<string, object?>?> GetStatusAsync(string projectId, CancellationToken cancellationToken = default)
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);

        await using var cmd = new NpgsqlCommand(
            "SELECT COUNT(*) FROM brain_entries WHERE project_id = @project_id",
            conn);
        cmd.Parameters.AddWithValue("project_id", projectId);

        var entryCount = (long)(await cmd.ExecuteScalarAsync(cancellationToken) ?? 0L);

        return new Dictionary<string, object?>
        {
            ["project_id"] = projectId,
            ["entry_count"] = entryCount,
            ["store"] = "postgres",
        };
    }

    /// <inheritdoc />
    public async Task StoreAsync(string projectId, string key, string value, CancellationToken cancellationToken = default)
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);

        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO brain_entries (project_id, key, value, created_at)
            VALUES (@project_id, @key, @value, @created_at)
            ON CONFLICT (project_id, key) DO UPDATE SET
                value = EXCLUDED.value,
                created_at = EXCLUDED.created_at
            """,
            conn);

        cmd.Parameters.AddWithValue("project_id", projectId);
        cmd.Parameters.AddWithValue("key", key);
        cmd.Parameters.AddWithValue("value", value);
        cmd.Parameters.AddWithValue("created_at", DateTimeOffset.UtcNow);

        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<BrainEntry>> RecallAsync(string projectId, string query, int limit = 10, CancellationToken cancellationToken = default)
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);

        // Simple text search for MVP — full-text or embedding search comes later
        await using var cmd = new NpgsqlCommand(
            """
            SELECT key, value, created_at FROM brain_entries
            WHERE project_id = @project_id
              AND (key ILIKE @query OR value ILIKE @query)
            ORDER BY created_at DESC
            LIMIT @limit
            """,
            conn);

        cmd.Parameters.AddWithValue("project_id", projectId);
        cmd.Parameters.AddWithValue("query", $"%{query}%");
        cmd.Parameters.AddWithValue("limit", limit);

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        var entries = new List<BrainEntry>();

        while (await reader.ReadAsync(cancellationToken))
        {
            entries.Add(new BrainEntry
            {
                Key = reader.GetString(0),
                Value = reader.GetString(1),
                CreatedAt = reader.GetDateTime(2),
            });
        }

        return entries;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<BrainEntry>> ListAllAsync(string projectId, int limit = 200, CancellationToken cancellationToken = default)
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);

        await using var cmd = new NpgsqlCommand(
            """
            SELECT key, value, created_at FROM brain_entries
            WHERE project_id = @project_id
            ORDER BY created_at DESC
            LIMIT @limit
            """,
            conn);

        cmd.Parameters.AddWithValue("project_id", projectId);
        cmd.Parameters.AddWithValue("limit", limit);

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        var entries = new List<BrainEntry>();

        while (await reader.ReadAsync(cancellationToken))
        {
            entries.Add(new BrainEntry
            {
                Key = reader.GetString(0),
                Value = reader.GetString(1),
                CreatedAt = reader.GetDateTime(2),
            });
        }

        return entries;
    }

    /// <inheritdoc />
    public async Task<bool> DeleteAsync(string projectId, string key, CancellationToken cancellationToken = default)
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);

        await using var cmd = new NpgsqlCommand(
            "DELETE FROM brain_entries WHERE project_id = @project_id AND key = @key",
            conn);

        cmd.Parameters.AddWithValue("project_id", projectId);
        cmd.Parameters.AddWithValue("key", key);

        var rows = await cmd.ExecuteNonQueryAsync(cancellationToken);
        return rows > 0;
    }

    /// <inheritdoc />
    public async Task<int> ClearProjectAsync(string projectId, CancellationToken cancellationToken = default)
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);

        await using var cmd = new NpgsqlCommand(
            "DELETE FROM brain_entries WHERE project_id = @project_id",
            conn);

        cmd.Parameters.AddWithValue("project_id", projectId);

        return await cmd.ExecuteNonQueryAsync(cancellationToken);
    }
}
