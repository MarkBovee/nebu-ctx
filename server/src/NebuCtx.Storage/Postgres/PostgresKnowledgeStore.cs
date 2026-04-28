namespace NebuCtx.Storage.Postgres;

using Npgsql;

/// <summary>
/// Postgres implementation of <see cref="IKnowledgeStore"/>.
/// Stores categorized knowledge facts per project in the knowledge_entries table.
/// </summary>
public sealed class PostgresKnowledgeStore : IKnowledgeStore
{
    private readonly string _connectionString;

    /// <summary>
    /// Initializes the Postgres knowledge store.
    /// </summary>
    /// <param name="connectionString">Postgres connection string.</param>
    public PostgresKnowledgeStore(string connectionString)
    {
        _connectionString = connectionString;
    }

    /// <inheritdoc />
    public async Task UpsertFactAsync(KnowledgeEntry entry, CancellationToken cancellationToken = default)
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);

        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO knowledge_entries (project_id, category, key, value, confidence, updated_at)
            VALUES (@project_id, @category, @key, @value, @confidence, NOW())
            ON CONFLICT (project_id, category, key) DO UPDATE SET
                value      = EXCLUDED.value,
                confidence = EXCLUDED.confidence,
                updated_at = NOW()
            """,
            conn);

        cmd.Parameters.AddWithValue("project_id", entry.ProjectId);
        cmd.Parameters.AddWithValue("category", entry.Category);
        cmd.Parameters.AddWithValue("key", entry.Key);
        cmd.Parameters.AddWithValue("value", entry.Value);
        cmd.Parameters.AddWithValue("confidence", entry.Confidence);

        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<KnowledgeEntry>> RecallAsync(string projectId, string? category, string query, int limit, CancellationToken cancellationToken = default)
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);

        // Full-text search across key and value using ILIKE; optionally scoped to a category.
        var sql = category is null
            ? "SELECT project_id, category, key, value, confidence, updated_at FROM knowledge_entries WHERE project_id = @project_id AND (key ILIKE @query OR value ILIKE @query) ORDER BY confidence DESC LIMIT @limit"
            : "SELECT project_id, category, key, value, confidence, updated_at FROM knowledge_entries WHERE project_id = @project_id AND category = @category AND (key ILIKE @query OR value ILIKE @query) ORDER BY confidence DESC LIMIT @limit";

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("project_id", projectId);
        cmd.Parameters.AddWithValue("query", $"%{query}%");
        cmd.Parameters.AddWithValue("limit", limit);
        if (category is not null)
        {
            cmd.Parameters.AddWithValue("category", category);
        }

        return await ReadEntriesAsync(cmd, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<(string Category, int Count)>> GetCategoriesAsync(string projectId, CancellationToken cancellationToken = default)
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);

        await using var cmd = new NpgsqlCommand(
            "SELECT category, COUNT(*) FROM knowledge_entries WHERE project_id = @project_id GROUP BY category ORDER BY category",
            conn);
        cmd.Parameters.AddWithValue("project_id", projectId);

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        var results = new List<(string, int)>();

        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add((reader.GetString(0), (int)reader.GetInt64(1)));
        }

        return results;
    }

    /// <inheritdoc />
    public async Task<int> GetFactCountAsync(string projectId, CancellationToken cancellationToken = default)
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);

        await using var cmd = new NpgsqlCommand(
            "SELECT COUNT(*) FROM knowledge_entries WHERE project_id = @project_id",
            conn);
        cmd.Parameters.AddWithValue("project_id", projectId);

        var result = await cmd.ExecuteScalarAsync(cancellationToken);
        return (int)(long)(result ?? 0L);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<KnowledgeEntry>> ListAllForProjectAsync(string projectId, int limit = 500, CancellationToken cancellationToken = default)
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);

        await using var cmd = new NpgsqlCommand(
            """
            SELECT project_id, category, key, value, confidence, updated_at
            FROM knowledge_entries
            WHERE project_id = @project_id
            ORDER BY category ASC, key ASC
            LIMIT @limit
            """,
            conn);

        cmd.Parameters.AddWithValue("project_id", projectId);
        cmd.Parameters.AddWithValue("limit", limit);

        return await ReadEntriesAsync(cmd, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<bool> RemoveFactAsync(string projectId, string category, string key, CancellationToken cancellationToken = default)
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);

        await using var cmd = new NpgsqlCommand(
            "DELETE FROM knowledge_entries WHERE project_id = @project_id AND category = @category AND key = @key",
            conn);
        cmd.Parameters.AddWithValue("project_id", projectId);
        cmd.Parameters.AddWithValue("category", category);
        cmd.Parameters.AddWithValue("key", key);

        var rows = await cmd.ExecuteNonQueryAsync(cancellationToken);
        return rows > 0;
    }

    /// <summary>
    /// Reads knowledge entries from an open command's result set.
    /// </summary>
    private static async Task<IReadOnlyList<KnowledgeEntry>> ReadEntriesAsync(NpgsqlCommand cmd, CancellationToken cancellationToken)
    {
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        var entries = new List<KnowledgeEntry>();

        while (await reader.ReadAsync(cancellationToken))
        {
            entries.Add(new KnowledgeEntry
            {
                ProjectId = reader.GetString(0),
                Category = reader.GetString(1),
                Key = reader.GetString(2),
                Value = reader.GetString(3),
                Confidence = reader.GetFloat(4),
                UpdatedAt = reader.GetDateTime(5),
            });
        }

        return entries;
    }
}
