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

        var terms = query
            .Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(term => term.Trim())
            .Where(term => term.Length >= 2)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (terms.Length == 0)
        {
            return [];
        }

        var filters = new List<string> { "project_id = @project_id" };
        if (category is not null)
        {
            filters.Add("category = @category");
        }

        var matchClauses = new List<string>();
        for (var index = 0; index < terms.Length; index++)
        {
            matchClauses.Add($"(category ILIKE @term{index} OR key ILIKE @term{index} OR value ILIKE @term{index} OR source_scope ILIKE @term{index} OR source_type ILIKE @term{index})");
        }

        filters.Add($"({string.Join(" OR ", matchClauses)})");
        var sql = $"SELECT project_id, category, key, value, confidence, created_at, updated_at, logical_key, promotion_identity, source_type, source_scope, lifecycle_status, lifecycle_score, confirmation_count, last_confirmed_at, retrieval_count, last_retrieved_at, history_json FROM knowledge_entries WHERE {string.Join(" AND ", filters)} ORDER BY lifecycle_score DESC, confidence DESC, updated_at DESC LIMIT @limit";

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("project_id", projectId);
        cmd.Parameters.AddWithValue("limit", limit);
        if (category is not null)
        {
            cmd.Parameters.AddWithValue("category", category);
        }
        for (var index = 0; index < terms.Length; index++)
        {
            cmd.Parameters.AddWithValue($"term{index}", $"%{terms[index]}%");
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

    /// <inheritdoc />
    public async Task UpsertCandidateAsync(KnowledgeCandidateEntry entry, CancellationToken cancellationToken = default)
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);

        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO knowledge_candidates (
                project_id, promotion_identity, category, key, value, logical_key,
                source_type, source_scope, confidence, evidence, review_status,
                created_at, updated_at, reviewed_at, promoted_knowledge_key)
            VALUES (
                @project_id, @promotion_identity, @category, @key, @value, @logical_key,
                @source_type, @source_scope, @confidence, @evidence, @review_status,
                @created_at, @updated_at, @reviewed_at, @promoted_knowledge_key)
            ON CONFLICT (project_id, promotion_identity) DO UPDATE SET
                category = EXCLUDED.category,
                key = EXCLUDED.key,
                value = EXCLUDED.value,
                logical_key = EXCLUDED.logical_key,
                source_type = EXCLUDED.source_type,
                source_scope = EXCLUDED.source_scope,
                confidence = EXCLUDED.confidence,
                evidence = EXCLUDED.evidence,
                review_status = EXCLUDED.review_status,
                created_at = COALESCE(knowledge_candidates.created_at, EXCLUDED.created_at),
                updated_at = EXCLUDED.updated_at,
                reviewed_at = EXCLUDED.reviewed_at,
                promoted_knowledge_key = EXCLUDED.promoted_knowledge_key
            """,
            conn);

        cmd.Parameters.AddWithValue("project_id", entry.ProjectId);
        cmd.Parameters.AddWithValue("promotion_identity", entry.PromotionIdentity);
        cmd.Parameters.AddWithValue("category", entry.Category);
        cmd.Parameters.AddWithValue("key", entry.Key);
        cmd.Parameters.AddWithValue("value", entry.Value);
        cmd.Parameters.AddWithValue("logical_key", entry.LogicalKey);
        cmd.Parameters.AddWithValue("source_type", entry.SourceType);
        cmd.Parameters.AddWithValue("source_scope", entry.SourceScope);
        cmd.Parameters.AddWithValue("confidence", entry.Confidence);
        cmd.Parameters.AddWithValue("evidence", entry.Evidence);
        cmd.Parameters.AddWithValue("review_status", entry.ReviewStatus);
        cmd.Parameters.AddWithValue("created_at", entry.CreatedAt);
        cmd.Parameters.AddWithValue("updated_at", entry.UpdatedAt);
        cmd.Parameters.AddWithValue("reviewed_at", (object?)entry.ReviewedAt ?? DBNull.Value);
        cmd.Parameters.AddWithValue("promoted_knowledge_key", entry.PromotedKnowledgeKey);

        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<KnowledgeCandidateEntry?> GetCandidateAsync(string projectId, string promotionIdentity, CancellationToken cancellationToken = default)
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);

        await using var cmd = new NpgsqlCommand(
            """
            SELECT project_id, promotion_identity, category, key, value, logical_key,
                   source_type, source_scope, confidence, evidence, review_status,
                   created_at, updated_at, reviewed_at, promoted_knowledge_key
            FROM knowledge_candidates
            WHERE project_id = @project_id AND promotion_identity = @promotion_identity
            LIMIT 1
            """,
            conn);
        cmd.Parameters.AddWithValue("project_id", projectId);
        cmd.Parameters.AddWithValue("promotion_identity", promotionIdentity);

        var entries = await ReadCandidatesAsync(cmd, cancellationToken);
        return entries.FirstOrDefault();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<KnowledgeCandidateEntry>> ListCandidatesAsync(string projectId, int limit = 100, CancellationToken cancellationToken = default)
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);

        await using var cmd = new NpgsqlCommand(
            """
            SELECT project_id, promotion_identity, category, key, value, logical_key,
                   source_type, source_scope, confidence, evidence, review_status,
                   created_at, updated_at, reviewed_at, promoted_knowledge_key
            FROM knowledge_candidates
            WHERE project_id = @project_id
            ORDER BY updated_at DESC, promotion_identity ASC
            LIMIT @limit
            """,
            conn);
        cmd.Parameters.AddWithValue("project_id", projectId);
        cmd.Parameters.AddWithValue("limit", limit);

        return await ReadCandidatesAsync(cmd, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<int> GetCandidateCountAsync(string projectId, CancellationToken cancellationToken = default)
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);

        await using var cmd = new NpgsqlCommand(
            "SELECT COUNT(*) FROM knowledge_candidates WHERE project_id = @project_id",
            conn);
        cmd.Parameters.AddWithValue("project_id", projectId);

        var result = await cmd.ExecuteScalarAsync(cancellationToken);
        return (int)(long)(result ?? 0L);
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

    /// <summary>
    /// Reads persisted durable memory candidates from an open command result set.
    /// </summary>
    private static async Task<IReadOnlyList<KnowledgeCandidateEntry>> ReadCandidatesAsync(NpgsqlCommand cmd, CancellationToken cancellationToken)
    {
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        var entries = new List<KnowledgeCandidateEntry>();

        while (await reader.ReadAsync(cancellationToken))
        {
            entries.Add(new KnowledgeCandidateEntry
            {
                ProjectId = reader.GetString(0),
                PromotionIdentity = reader.GetString(1),
                Category = reader.GetString(2),
                Key = reader.GetString(3),
                Value = reader.GetString(4),
                LogicalKey = reader.GetString(5),
                SourceType = reader.GetString(6),
                SourceScope = reader.GetString(7),
                Confidence = reader.GetFloat(8),
                Evidence = reader.GetString(9),
                ReviewStatus = reader.GetString(10),
                CreatedAt = reader.GetFieldValue<DateTimeOffset>(11),
                UpdatedAt = reader.GetFieldValue<DateTimeOffset>(12),
                ReviewedAt = reader.IsDBNull(13) ? null : reader.GetFieldValue<DateTimeOffset>(13),
                PromotedKnowledgeKey = reader.GetString(14),
            });
        }

        return entries;
    }
}
