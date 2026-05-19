namespace NebuCtx.Storage.Postgres;

using System.Text.Json;
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
            INSERT INTO knowledge_entries (
                project_id, category, key, value, confidence, created_at, updated_at,
                logical_key, promotion_identity, source_type, source_scope,
                lifecycle_status, lifecycle_score, confirmation_count, last_confirmed_at,
                retrieval_count, last_retrieved_at, history_json)
            VALUES (
                @project_id, @category, @key, @value, @confidence, @created_at, @updated_at,
                @logical_key, @promotion_identity, @source_type, @source_scope,
                @lifecycle_status, @lifecycle_score, @confirmation_count, @last_confirmed_at,
                @retrieval_count, @last_retrieved_at, @history_json::jsonb)
            ON CONFLICT (project_id, category, key) DO UPDATE SET
                value      = EXCLUDED.value,
                confidence = EXCLUDED.confidence,
                created_at = COALESCE(knowledge_entries.created_at, EXCLUDED.created_at),
                updated_at = EXCLUDED.updated_at,
                logical_key = EXCLUDED.logical_key,
                promotion_identity = EXCLUDED.promotion_identity,
                source_type = EXCLUDED.source_type,
                source_scope = EXCLUDED.source_scope,
                lifecycle_status = EXCLUDED.lifecycle_status,
                lifecycle_score = EXCLUDED.lifecycle_score,
                confirmation_count = EXCLUDED.confirmation_count,
                last_confirmed_at = EXCLUDED.last_confirmed_at,
                retrieval_count = EXCLUDED.retrieval_count,
                last_retrieved_at = EXCLUDED.last_retrieved_at,
                history_json = EXCLUDED.history_json
            """,
            conn);

        cmd.Parameters.AddWithValue("project_id", entry.ProjectId);
        cmd.Parameters.AddWithValue("category", entry.Category);
        cmd.Parameters.AddWithValue("key", entry.Key);
        cmd.Parameters.AddWithValue("value", entry.Value);
        cmd.Parameters.AddWithValue("confidence", entry.Confidence);
        cmd.Parameters.AddWithValue("created_at", entry.CreatedAt);
        cmd.Parameters.AddWithValue("updated_at", entry.UpdatedAt);
        cmd.Parameters.AddWithValue("logical_key", entry.LogicalKey);
        cmd.Parameters.AddWithValue("promotion_identity", entry.PromotionIdentity);
        cmd.Parameters.AddWithValue("source_type", entry.SourceType);
        cmd.Parameters.AddWithValue("source_scope", entry.SourceScope);
        cmd.Parameters.AddWithValue("lifecycle_status", entry.LifecycleStatus);
        cmd.Parameters.AddWithValue("lifecycle_score", entry.LifecycleScore);
        cmd.Parameters.AddWithValue("confirmation_count", entry.ConfirmationCount);
        cmd.Parameters.AddWithValue("last_confirmed_at", (object?)entry.LastConfirmedAt ?? DBNull.Value);
        cmd.Parameters.AddWithValue("retrieval_count", entry.RetrievalCount);
        cmd.Parameters.AddWithValue("last_retrieved_at", (object?)entry.LastRetrievedAt ?? DBNull.Value);
        cmd.Parameters.AddWithValue("history_json", JsonSerializer.Serialize(entry.History));

        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<KnowledgeEntry?> GetFactAsync(string projectId, string category, string key, CancellationToken cancellationToken = default)
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);

        await using var cmd = new NpgsqlCommand(
            """
            SELECT project_id, category, key, value, confidence, created_at, updated_at,
                   logical_key, promotion_identity, source_type, source_scope,
                   lifecycle_status, lifecycle_score, confirmation_count, last_confirmed_at,
                   retrieval_count, last_retrieved_at, history_json
            FROM knowledge_entries
            WHERE project_id = @project_id AND category = @category AND key = @key
            LIMIT 1
            """,
            conn);
        cmd.Parameters.AddWithValue("project_id", projectId);
        cmd.Parameters.AddWithValue("category", category);
        cmd.Parameters.AddWithValue("key", key);

        var entries = await ReadEntriesAsync(cmd, cancellationToken);
        return entries.FirstOrDefault();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<KnowledgeEntry>> RecallAsync(string projectId, string? category, string query, int limit, CancellationToken cancellationToken = default)
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);

        // Full-text search across key and value using ILIKE; optionally scoped to a category.
        var sql = category is null
            ? "SELECT project_id, category, key, value, confidence, created_at, updated_at, logical_key, promotion_identity, source_type, source_scope, lifecycle_status, lifecycle_score, confirmation_count, last_confirmed_at, retrieval_count, last_retrieved_at, history_json FROM knowledge_entries WHERE project_id = @project_id AND (key ILIKE @query OR value ILIKE @query) ORDER BY lifecycle_score DESC, confidence DESC LIMIT @limit"
            : "SELECT project_id, category, key, value, confidence, created_at, updated_at, logical_key, promotion_identity, source_type, source_scope, lifecycle_status, lifecycle_score, confirmation_count, last_confirmed_at, retrieval_count, last_retrieved_at, history_json FROM knowledge_entries WHERE project_id = @project_id AND category = @category AND (key ILIKE @query OR value ILIKE @query) ORDER BY lifecycle_score DESC, confidence DESC LIMIT @limit";

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
            SELECT project_id, category, key, value, confidence, created_at, updated_at,
                   logical_key, promotion_identity, source_type, source_scope,
                   lifecycle_status, lifecycle_score, confirmation_count, last_confirmed_at,
                   retrieval_count, last_retrieved_at, history_json
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

    /// <inheritdoc />
    public async Task<int> ClearProjectAsync(string projectId, CancellationToken cancellationToken = default)
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);

        await using var cmd = new NpgsqlCommand(
            "DELETE FROM knowledge_entries WHERE project_id = @project_id",
            conn);
        cmd.Parameters.AddWithValue("project_id", projectId);

        return await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<int> ReassignProjectAsync(string fromProjectId, string toProjectId, CancellationToken cancellationToken = default)
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);

        await using var cmd = new NpgsqlCommand(
            "UPDATE knowledge_entries SET project_id = @to_project_id WHERE project_id = @from_project_id",
            conn);
        cmd.Parameters.AddWithValue("from_project_id", fromProjectId);
        cmd.Parameters.AddWithValue("to_project_id", toProjectId);

        return await cmd.ExecuteNonQueryAsync(cancellationToken);
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
            var historyJson = reader.IsDBNull(17) ? "[]" : reader.GetString(17);
            entries.Add(new KnowledgeEntry
            {
                ProjectId = reader.GetString(0),
                Category = reader.GetString(1),
                Key = reader.GetString(2),
                Value = reader.GetString(3),
                Confidence = reader.GetFloat(4),
                CreatedAt = reader.GetFieldValue<DateTimeOffset>(5),
                UpdatedAt = reader.GetFieldValue<DateTimeOffset>(6),
                LogicalKey = reader.GetString(7),
                PromotionIdentity = reader.GetString(8),
                SourceType = reader.GetString(9),
                SourceScope = reader.GetString(10),
                LifecycleStatus = reader.GetString(11),
                LifecycleScore = reader.GetFloat(12),
                ConfirmationCount = reader.GetInt32(13),
                LastConfirmedAt = reader.IsDBNull(14) ? null : reader.GetFieldValue<DateTimeOffset>(14),
                RetrievalCount = reader.GetInt32(15),
                LastRetrievedAt = reader.IsDBNull(16) ? null : reader.GetFieldValue<DateTimeOffset>(16),
                History = JsonSerializer.Deserialize<List<KnowledgeHistoryEntry>>(historyJson) ?? [],
            });
        }

        return entries;
    }
}
