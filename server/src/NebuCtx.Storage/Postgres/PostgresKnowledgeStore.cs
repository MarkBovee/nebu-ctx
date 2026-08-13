namespace NebuCtx.Storage.Postgres;

using System.Text.Json;

using NebuCtx.Contracts.Mcp;
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
                retrieval_count, last_retrieved_at, history_json,
                promoted_from_brain_key, promoted_from_brain_category, promoted_from_brain_value,
                promoted_from_timestamp, promotion_action, promotion_timestamp)
            VALUES (
                @project_id, @category, @key, @value, @confidence, @created_at, @updated_at,
                @logical_key, @promotion_identity, @source_type, @source_scope,
                @lifecycle_status, @lifecycle_score, @confirmation_count, @last_confirmed_at,
                @retrieval_count, @last_retrieved_at, @history_json::jsonb,
                @promoted_from_brain_key, @promoted_from_brain_category, @promoted_from_brain_value,
                @promoted_from_timestamp, @promotion_action, @promotion_timestamp)
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
                history_json = EXCLUDED.history_json,
                promoted_from_brain_key = EXCLUDED.promoted_from_brain_key,
                promoted_from_brain_category = EXCLUDED.promoted_from_brain_category,
                promoted_from_brain_value = EXCLUDED.promoted_from_brain_value,
                promoted_from_timestamp = EXCLUDED.promoted_from_timestamp,
                promotion_action = EXCLUDED.promotion_action,
                promotion_timestamp = EXCLUDED.promotion_timestamp
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
        cmd.Parameters.AddWithValue("promoted_from_brain_key", entry.PromotedFromBrainKey);
        cmd.Parameters.AddWithValue("promoted_from_brain_category", entry.PromotedFromBrainCategory);
        cmd.Parameters.AddWithValue("promoted_from_brain_value", entry.PromotedFromBrainValue);
        cmd.Parameters.AddWithValue("promoted_from_timestamp", (object?)entry.PromotedFromTimestamp ?? DBNull.Value);
        cmd.Parameters.AddWithValue("promotion_action", entry.PromotionAction);
        cmd.Parameters.AddWithValue("promotion_timestamp", (object?)entry.PromotionTimestamp ?? DBNull.Value);

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
                   retrieval_count, last_retrieved_at, history_json,
                   promoted_from_brain_key, promoted_from_brain_category, promoted_from_brain_value,
                   promoted_from_timestamp, promotion_action, promotion_timestamp
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
        var sql = $"SELECT project_id, category, key, value, confidence, created_at, updated_at, logical_key, promotion_identity, source_type, source_scope, lifecycle_status, lifecycle_score, confirmation_count, last_confirmed_at, retrieval_count, last_retrieved_at, history_json, promoted_from_brain_key, promoted_from_brain_category, promoted_from_brain_value, promoted_from_timestamp, promotion_action, promotion_timestamp FROM knowledge_entries WHERE {string.Join(" AND ", filters)} ORDER BY lifecycle_score DESC, confidence DESC, updated_at DESC LIMIT @limit";

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
                   retrieval_count, last_retrieved_at, history_json,
                   promoted_from_brain_key, promoted_from_brain_category, promoted_from_brain_value,
                   promoted_from_timestamp, promotion_action, promotion_timestamp
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
    public async Task<(IReadOnlyList<KnowledgeEntry> Entries, int Total)> ListFilteredAsync(string projectId, MemoryListFilter filter, CancellationToken cancellationToken = default)
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);

        var (whereClause, parameters) = BuildKnowledgeListWhereClause(projectId, filter);
        var orderBy = BuildKnowledgeListOrderBy(filter);

        var limit = Math.Clamp(filter.Limit <= 0 ? 20 : filter.Limit, 1, MemoryListFilter.MaxLimit);
        var offset = Math.Max(0, filter.Offset);

        await using var countCmd = new NpgsqlCommand($"SELECT COUNT(*) FROM knowledge_entries WHERE {whereClause}", conn);
        foreach (var (k, v) in parameters) countCmd.Parameters.AddWithValue(k, v ?? DBNull.Value);
        var total = (int)(long)(await countCmd.ExecuteScalarAsync(cancellationToken) ?? 0L);

        var sql = $"SELECT project_id, category, key, value, confidence, created_at, updated_at, logical_key, promotion_identity, source_type, source_scope, lifecycle_status, lifecycle_score, confirmation_count, last_confirmed_at, retrieval_count, last_retrieved_at, history_json, promoted_from_brain_key, promoted_from_brain_category, promoted_from_brain_value, promoted_from_timestamp, promotion_action, promotion_timestamp FROM knowledge_entries WHERE {whereClause} ORDER BY {orderBy} LIMIT @limit OFFSET @offset";
        await using var cmd = new NpgsqlCommand(sql, conn);
        foreach (var (k, v) in parameters) cmd.Parameters.AddWithValue(k, v ?? DBNull.Value);
        cmd.Parameters.AddWithValue("limit", limit);
        cmd.Parameters.AddWithValue("offset", offset);

        var entries = await ReadEntriesAsync(cmd, cancellationToken);
        return (entries, total);
    }

    private static (string Where, Dictionary<string, object?> Parameters) BuildKnowledgeListWhereClause(string projectId, MemoryListFilter filter)
    {
        var clauses = new List<string> { "project_id = @project_id" };
        var parameters = new Dictionary<string, object?> { ["project_id"] = projectId };
        if (!string.IsNullOrEmpty(filter.Category))
        {
            clauses.Add("category = @category");
            parameters["category"] = filter.Category;
        }
        if (!string.IsNullOrEmpty(filter.SourceType))
        {
            clauses.Add("source_type = @source_type");
            parameters["source_type"] = filter.SourceType;
        }
        if (!string.IsNullOrEmpty(filter.LifecycleStatus))
        {
            clauses.Add("lifecycle_status = @lifecycle_status");
            parameters["lifecycle_status"] = filter.LifecycleStatus;
        }
        if (filter.CreatedAfter.HasValue)
        {
            clauses.Add("created_at >= @created_after");
            parameters["created_after"] = filter.CreatedAfter.Value;
        }
        if (filter.CreatedBefore.HasValue)
        {
            clauses.Add("created_at <= @created_before");
            parameters["created_before"] = filter.CreatedBefore.Value;
        }
        if (!string.IsNullOrEmpty(filter.PromotedFromSession))
        {
            clauses.Add("source_scope = @promoted_from_session");
            parameters["promoted_from_session"] = filter.PromotedFromSession;
        }
        if (!string.IsNullOrEmpty(filter.PromotedFromBrainKey))
        {
            clauses.Add("logical_key = @promoted_from_brain_key");
            parameters["promoted_from_brain_key"] = filter.PromotedFromBrainKey;
        }
        return (string.Join(" AND ", clauses), parameters);
    }

    private static string BuildKnowledgeListOrderBy(MemoryListFilter filter)
    {
        var direction = string.Equals(filter.SortDirection, "asc", StringComparison.OrdinalIgnoreCase) ? "ASC" : "DESC";
        return filter.SortField.ToLowerInvariant() switch
        {
            "created" => $"created_at {direction}",
            "updated" => $"updated_at {direction}",
            "confidence" => $"confidence {direction}",
            "retrieval_count" => $"retrieval_count {direction}",
            "relevance" => $"lifecycle_score {direction}, confidence {direction}",
            "key" => $"category {direction}, key {direction}",
            _ => $"lifecycle_score {direction}, confidence {direction}, updated_at {direction}",
        };
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
    public async Task<int> RemoveExpiredFactsAsync(string projectId, int maxAgeDays = 90, CancellationToken cancellationToken = default)
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);

        await using var cmd = new NpgsqlCommand(
            "DELETE FROM knowledge_entries WHERE project_id = @project_id AND lifecycle_status = 'stale' AND created_at < @cutoff",
            conn);
        cmd.Parameters.AddWithValue("project_id", projectId);
        cmd.Parameters.AddWithValue("cutoff", DateTimeOffset.UtcNow.AddDays(-maxAgeDays));

        return await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<int> ClearProjectAsync(string projectId, CancellationToken cancellationToken = default)
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);

        await using var cmd = new NpgsqlCommand(
            "DELETE FROM knowledge_candidates WHERE project_id = @project_id; DELETE FROM knowledge_entries WHERE project_id = @project_id",
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
                PromotedFromBrainKey = reader.GetString(18),
                PromotedFromBrainCategory = reader.GetString(19),
                PromotedFromBrainValue = reader.GetString(20),
                PromotedFromTimestamp = reader.IsDBNull(21) ? null : reader.GetFieldValue<DateTimeOffset>(21),
                PromotionAction = reader.GetString(22),
                PromotionTimestamp = reader.IsDBNull(23) ? null : reader.GetFieldValue<DateTimeOffset>(23),
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
