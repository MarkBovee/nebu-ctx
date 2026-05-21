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

        await using var activeCmd = new NpgsqlCommand(
            "SELECT COUNT(*) FROM brain_entries WHERE project_id = @project_id AND lifecycle_status = 'current'",
            conn);
        activeCmd.Parameters.AddWithValue("project_id", projectId);
        var activeFactCount = (long)(await activeCmd.ExecuteScalarAsync(cancellationToken) ?? 0L);

        return new Dictionary<string, object?>
        {
            ["project_id"] = projectId,
            ["entry_count"] = entryCount,
            ["active_fact_count"] = activeFactCount,
            ["store"] = "postgres",
        };
    }

    /// <inheritdoc />
    public async Task StoreAsync(string projectId, string key, string value, CancellationToken cancellationToken = default)
    {
        await StoreFactAsync(new BrainEntry
        {
            Key = key,
            Value = value,
            ProjectId = projectId,
            Kind = "legacy",
            Category = "legacy",
            LogicalKey = key,
            PromotionIdentity = $"legacy:{projectId}:{key}",
            SourceType = "legacy",
            SourceScope = projectId,
            LifecycleStatus = "legacy",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        }, cancellationToken);
    }

    /// <inheritdoc />
    public async Task StoreFactAsync(BrainEntry entry, CancellationToken cancellationToken = default)
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);

        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO brain_entries (project_id, key, value, created_at, updated_at, kind, category, logical_key, promotion_identity, source_type, source_scope, lifecycle_status, confidence, evidence, superseded_by, invalidated_by)
            VALUES (@project_id, @key, @value, @created_at, @updated_at, @kind, @category, @logical_key, @promotion_identity, @source_type, @source_scope, @lifecycle_status, @confidence, @evidence, @superseded_by, @invalidated_by)
            ON CONFLICT (project_id, key) DO UPDATE SET
                value = EXCLUDED.value,
                created_at = EXCLUDED.created_at,
                updated_at = EXCLUDED.updated_at,
                kind = EXCLUDED.kind,
                category = EXCLUDED.category,
                logical_key = EXCLUDED.logical_key,
                promotion_identity = EXCLUDED.promotion_identity,
                source_type = EXCLUDED.source_type,
                source_scope = EXCLUDED.source_scope,
                lifecycle_status = EXCLUDED.lifecycle_status,
                confidence = EXCLUDED.confidence,
                evidence = EXCLUDED.evidence,
                superseded_by = EXCLUDED.superseded_by,
                invalidated_by = EXCLUDED.invalidated_by
            """,
            conn);

        cmd.Parameters.AddWithValue("project_id", entry.ProjectId);
        cmd.Parameters.AddWithValue("key", entry.Key);
        cmd.Parameters.AddWithValue("value", entry.Value);
        cmd.Parameters.AddWithValue("created_at", entry.CreatedAt == default ? DateTimeOffset.UtcNow : entry.CreatedAt);
        cmd.Parameters.AddWithValue("updated_at", entry.UpdatedAt == default ? DateTimeOffset.UtcNow : entry.UpdatedAt);
        cmd.Parameters.AddWithValue("kind", entry.Kind);
        cmd.Parameters.AddWithValue("category", entry.Category);
        cmd.Parameters.AddWithValue("logical_key", entry.LogicalKey);
        cmd.Parameters.AddWithValue("promotion_identity", entry.PromotionIdentity);
        cmd.Parameters.AddWithValue("source_type", entry.SourceType);
        cmd.Parameters.AddWithValue("source_scope", entry.SourceScope);
        cmd.Parameters.AddWithValue("lifecycle_status", entry.LifecycleStatus);
        cmd.Parameters.AddWithValue("confidence", entry.Confidence);
        cmd.Parameters.AddWithValue("evidence", entry.Evidence);
        cmd.Parameters.AddWithValue("superseded_by", entry.SupersededBy);
        cmd.Parameters.AddWithValue("invalidated_by", entry.InvalidatedBy);

        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<BrainEntry>> RecallAsync(string projectId, string query, int limit = 10, CancellationToken cancellationToken = default)
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

        var clauses = new List<string>();
        for (var index = 0; index < terms.Length; index++)
        {
            clauses.Add($"(key ILIKE @term{index} OR value ILIKE @term{index})");
        }

        await using var cmd = new NpgsqlCommand(
            $"SELECT key, value, created_at, updated_at, kind, category, logical_key, promotion_identity, source_type, source_scope, lifecycle_status, confidence, evidence, superseded_by, invalidated_by FROM brain_entries WHERE project_id = @project_id AND ({string.Join(" OR ", clauses)}) ORDER BY updated_at DESC LIMIT @limit",
            conn);

        cmd.Parameters.AddWithValue("project_id", projectId);
        cmd.Parameters.AddWithValue("limit", limit);
        for (var index = 0; index < terms.Length; index++)
        {
            cmd.Parameters.AddWithValue($"term{index}", $"%{terms[index]}%");
        }

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        var entries = new List<BrainEntry>();

        while (await reader.ReadAsync(cancellationToken))
        {
            entries.Add(new BrainEntry
            {
                ProjectId = projectId,
                Key = reader.GetString(0),
                Value = reader.GetString(1),
                CreatedAt = reader.GetFieldValue<DateTimeOffset>(2),
                UpdatedAt = reader.GetFieldValue<DateTimeOffset>(3),
                Kind = reader.GetString(4),
                Category = reader.GetString(5),
                LogicalKey = reader.GetString(6),
                PromotionIdentity = reader.GetString(7),
                SourceType = reader.GetString(8),
                SourceScope = reader.GetString(9),
                LifecycleStatus = reader.GetString(10),
                Confidence = reader.GetFloat(11),
                Evidence = reader.GetString(12),
                SupersededBy = reader.GetString(13),
                InvalidatedBy = reader.GetString(14),
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
            SELECT key, value, created_at, updated_at, kind, category, logical_key, promotion_identity, source_type, source_scope, lifecycle_status, confidence, evidence, superseded_by, invalidated_by FROM brain_entries
            WHERE project_id = @project_id
            ORDER BY updated_at DESC
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
                ProjectId = projectId,
                Key = reader.GetString(0),
                Value = reader.GetString(1),
                CreatedAt = reader.GetFieldValue<DateTimeOffset>(2),
                UpdatedAt = reader.GetFieldValue<DateTimeOffset>(3),
                Kind = reader.GetString(4),
                Category = reader.GetString(5),
                LogicalKey = reader.GetString(6),
                PromotionIdentity = reader.GetString(7),
                SourceType = reader.GetString(8),
                SourceScope = reader.GetString(9),
                LifecycleStatus = reader.GetString(10),
                Confidence = reader.GetFloat(11),
                Evidence = reader.GetString(12),
                SupersededBy = reader.GetString(13),
                InvalidatedBy = reader.GetString(14),
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

    /// <inheritdoc />
    public async Task<int> DeleteByPrefixAsync(string projectId, string keyPrefix, CancellationToken cancellationToken = default)
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);

        await using var cmd = new NpgsqlCommand(
            "DELETE FROM brain_entries WHERE project_id = @project_id AND key LIKE @key_prefix",
            conn);

        cmd.Parameters.AddWithValue("project_id", projectId);
        cmd.Parameters.AddWithValue("key_prefix", $"{keyPrefix}%");

        return await cmd.ExecuteNonQueryAsync(cancellationToken);
    }
}
