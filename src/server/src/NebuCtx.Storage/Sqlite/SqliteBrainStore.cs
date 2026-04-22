namespace NebuCtx.Storage.Sqlite;

using Microsoft.Data.Sqlite;

/// <summary>
/// SQLite implementation of <see cref="IBrainStore"/>.
/// Provides project-scoped brain memory (ctx_brain) operations.
/// </summary>
public sealed class SqliteBrainStore : IBrainStore
{
    private readonly string _connectionString;

    /// <summary>
    /// Initializes the SQLite brain store.
    /// </summary>
    /// <param name="connectionString">SQLite connection string.</param>
    public SqliteBrainStore(string connectionString)
    {
        _connectionString = connectionString;
    }

    /// <inheritdoc />
    public async Task<Dictionary<string, object?>?> GetStatusAsync(string projectId, CancellationToken cancellationToken = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM brain_entries WHERE project_id = @project_id";
        cmd.Parameters.AddWithValue("@project_id", projectId);

        var entryCount = (long)(await cmd.ExecuteScalarAsync(cancellationToken) ?? 0L);

        return new Dictionary<string, object?>
        {
            ["project_id"] = projectId,
            ["entry_count"] = entryCount,
            ["store"] = "sqlite",
        };
    }

    /// <inheritdoc />
    public async Task StoreAsync(string projectId, string key, string value, CancellationToken cancellationToken = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            """
            INSERT INTO brain_entries (project_id, key, value, created_at)
            VALUES (@project_id, @key, @value, @created_at)
            ON CONFLICT (project_id, key) DO UPDATE SET
                value = excluded.value,
                created_at = excluded.created_at
            """;

        cmd.Parameters.AddWithValue("@project_id", projectId);
        cmd.Parameters.AddWithValue("@key", key);
        cmd.Parameters.AddWithValue("@value", value);
        cmd.Parameters.AddWithValue("@created_at", DateTimeOffset.UtcNow.ToString("O"));

        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<BrainEntry>> RecallAsync(string projectId, string query, int limit = 10, CancellationToken cancellationToken = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            """
            SELECT key, value, created_at FROM brain_entries
            WHERE project_id = @project_id
              AND (key LIKE @query OR value LIKE @query)
            ORDER BY created_at DESC
            LIMIT @limit
            """;

        cmd.Parameters.AddWithValue("@project_id", projectId);
        cmd.Parameters.AddWithValue("@query", $"%{query}%");
        cmd.Parameters.AddWithValue("@limit", limit);

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        var entries = new List<BrainEntry>();

        while (await reader.ReadAsync(cancellationToken))
        {
            entries.Add(new BrainEntry
            {
                Key = reader.GetString(0),
                Value = reader.GetString(1),
                CreatedAt = DateTimeOffset.Parse(reader.GetString(2)),
            });
        }

        return entries;
    }
}
