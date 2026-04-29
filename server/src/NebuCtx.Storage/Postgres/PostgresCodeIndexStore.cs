namespace NebuCtx.Storage.Postgres;

using Npgsql;
using NpgsqlTypes;
using System.Text.Json;

/// <summary>
/// Postgres implementation of <see cref="ICodeIndexStore"/>.
/// Persists per-project source file metadata, symbols, and call edges
/// uploaded by the Rust client after a local index build.
/// </summary>
public sealed class PostgresCodeIndexStore : ICodeIndexStore
{
    private readonly string _connectionString;

    /// <summary>
    /// Initializes the Postgres code index store.
    /// </summary>
    /// <param name="connectionString">Postgres connection string.</param>
    public PostgresCodeIndexStore(string connectionString)
    {
        _connectionString = connectionString;
    }

    /// <inheritdoc />
    public async Task SyncIndexAsync(string projectId, IReadOnlyList<IndexedFile> files, IReadOnlyList<IndexedSymbol> symbols, IReadOnlyList<IndexedCallEdge> edges, CancellationToken cancellationToken = default)
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);

        // Delete existing index data for this project and replace with fresh snapshot.
        await using (var del = new NpgsqlCommand(
            """
            DELETE FROM project_call_edges WHERE project_id = @project_id;
            DELETE FROM project_symbols WHERE project_id = @project_id;
            DELETE FROM project_files WHERE project_id = @project_id;
            """,
            conn))
        {
            del.Parameters.AddWithValue("project_id", projectId);
            await del.ExecuteNonQueryAsync(cancellationToken);
        }

        // Bulk insert files using COPY for performance.
        if (files.Count > 0)
        {
            await using var writer = await conn.BeginBinaryImportAsync(
                "COPY project_files (project_id, path, hash, language, line_count, token_count, exports_json, summary, indexed_at) FROM STDIN (FORMAT BINARY)",
                cancellationToken);

            var now = DateTimeOffset.UtcNow;
            foreach (var f in files)
            {
                await writer.StartRowAsync(cancellationToken);
                await writer.WriteAsync(projectId, NpgsqlDbType.Text, cancellationToken);
                await writer.WriteAsync(f.Path, NpgsqlDbType.Text, cancellationToken);
                await writer.WriteAsync(f.Hash, NpgsqlDbType.Text, cancellationToken);
                await writer.WriteAsync(f.Language, NpgsqlDbType.Text, cancellationToken);
                await writer.WriteAsync(f.LineCount, NpgsqlDbType.Integer, cancellationToken);
                await writer.WriteAsync(f.TokenCount, NpgsqlDbType.Integer, cancellationToken);
                var exportsJson = JsonSerializer.Serialize(f.Exports);
                await writer.WriteAsync(exportsJson, NpgsqlDbType.Jsonb, cancellationToken);
                await writer.WriteAsync(f.Summary, NpgsqlDbType.Text, cancellationToken);
                await writer.WriteAsync(now, NpgsqlDbType.TimestampTz, cancellationToken);
            }

            await writer.CompleteAsync(cancellationToken);
        }

        // Bulk insert symbols.
        if (symbols.Count > 0)
        {
            await using var writer = await conn.BeginBinaryImportAsync(
                "COPY project_symbols (project_id, file_path, name, kind, start_line, end_line, is_exported, indexed_at) FROM STDIN (FORMAT BINARY)",
                cancellationToken);

            var now = DateTimeOffset.UtcNow;
            foreach (var s in symbols)
            {
                await writer.StartRowAsync(cancellationToken);
                await writer.WriteAsync(projectId, NpgsqlDbType.Text, cancellationToken);
                await writer.WriteAsync(s.FilePath, NpgsqlDbType.Text, cancellationToken);
                await writer.WriteAsync(s.Name, NpgsqlDbType.Text, cancellationToken);
                await writer.WriteAsync(s.Kind, NpgsqlDbType.Text, cancellationToken);
                await writer.WriteAsync(s.StartLine, NpgsqlDbType.Integer, cancellationToken);
                await writer.WriteAsync(s.EndLine, NpgsqlDbType.Integer, cancellationToken);
                await writer.WriteAsync(s.IsExported, NpgsqlDbType.Boolean, cancellationToken);
                await writer.WriteAsync(now, NpgsqlDbType.TimestampTz, cancellationToken);
            }

            await writer.CompleteAsync(cancellationToken);
        }

        // Bulk insert call edges, deduplicating on (from_symbol, to_symbol).
        if (edges.Count > 0)
        {
            // Deduplicate edges before inserting to avoid primary key conflicts.
            var distinctEdges = edges
                .GroupBy(e => (e.FromSymbol, e.ToSymbol))
                .Select(g => g.First())
                .ToList();

            await using var writer = await conn.BeginBinaryImportAsync(
                "COPY project_call_edges (project_id, from_symbol, to_symbol, kind, indexed_at) FROM STDIN (FORMAT BINARY)",
                cancellationToken);

            var now = DateTimeOffset.UtcNow;
            foreach (var e in distinctEdges)
            {
                await writer.StartRowAsync(cancellationToken);
                await writer.WriteAsync(projectId, NpgsqlDbType.Text, cancellationToken);
                await writer.WriteAsync(e.FromSymbol, NpgsqlDbType.Text, cancellationToken);
                await writer.WriteAsync(e.ToSymbol, NpgsqlDbType.Text, cancellationToken);
                await writer.WriteAsync(e.Kind, NpgsqlDbType.Text, cancellationToken);
                await writer.WriteAsync(now, NpgsqlDbType.TimestampTz, cancellationToken);
            }

            await writer.CompleteAsync(cancellationToken);
        }
    }

    /// <inheritdoc />
    public async Task<CodeIndexStats> GetStatsAsync(string projectId, CancellationToken cancellationToken = default)
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);

        // File count + language distribution + last indexed timestamp.
        await using var fileCmd = new NpgsqlCommand(
            """
            SELECT language, COUNT(*) as cnt, MAX(indexed_at) as last_indexed
            FROM project_files
            WHERE project_id = @project_id
            GROUP BY language
            ORDER BY cnt DESC
            """,
            conn);
        fileCmd.Parameters.AddWithValue("project_id", projectId);

        var langDist = new Dictionary<string, int>();
        DateTimeOffset? lastIndexedAt = null;
        int fileCount = 0;

        await using (var reader = await fileCmd.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                var lang = reader.GetString(0);
                var cnt = (int)reader.GetInt64(1);
                langDist[lang] = cnt;
                fileCount += cnt;
                var ts = reader.GetDateTime(2);
                if (lastIndexedAt == null || ts > lastIndexedAt)
                    lastIndexedAt = new DateTimeOffset(ts, TimeSpan.Zero);
            }
        }

        // Symbol count.
        await using var symCmd = new NpgsqlCommand(
            "SELECT COUNT(*) FROM project_symbols WHERE project_id = @project_id",
            conn);
        symCmd.Parameters.AddWithValue("project_id", projectId);
        var symbolCount = (int)(long)(await symCmd.ExecuteScalarAsync(cancellationToken) ?? 0L);

        // Edge count.
        await using var edgeCmd = new NpgsqlCommand(
            "SELECT COUNT(*) FROM project_call_edges WHERE project_id = @project_id",
            conn);
        edgeCmd.Parameters.AddWithValue("project_id", projectId);
        var edgeCount = (int)(long)(await edgeCmd.ExecuteScalarAsync(cancellationToken) ?? 0L);

        return new CodeIndexStats
        {
            FileCount = fileCount,
            SymbolCount = symbolCount,
            EdgeCount = edgeCount,
            LanguageDistribution = langDist,
            LastIndexedAt = lastIndexedAt,
        };
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<IndexedSymbol>> SearchSymbolsAsync(string projectId, string? query, string? kind, int limit = 200, CancellationToken cancellationToken = default)
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);

        var sql = """
            SELECT file_path, name, kind, start_line, end_line, is_exported
            FROM project_symbols
            WHERE project_id = @project_id
            """;

        var hasQuery = !string.IsNullOrWhiteSpace(query);
        var hasKind = !string.IsNullOrWhiteSpace(kind);

        if (hasQuery) sql += " AND name ILIKE @query";
        if (hasKind) sql += " AND kind = @kind";
        sql += " ORDER BY name LIMIT @limit";

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("project_id", projectId);
        if (hasQuery) cmd.Parameters.AddWithValue("query", $"%{query}%");
        if (hasKind) cmd.Parameters.AddWithValue("kind", kind!);
        cmd.Parameters.AddWithValue("limit", limit);

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        var results = new List<IndexedSymbol>();

        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(new IndexedSymbol
            {
                FilePath = reader.GetString(0),
                Name = reader.GetString(1),
                Kind = reader.GetString(2),
                StartLine = reader.GetInt32(3),
                EndLine = reader.GetInt32(4),
                IsExported = reader.GetBoolean(5),
            });
        }

        return results;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<IndexedCallEdge>> GetEdgesAsync(string projectId, int limit = 5000, CancellationToken cancellationToken = default)
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);

        await using var cmd = new NpgsqlCommand(
            """
            SELECT from_symbol, to_symbol, kind
            FROM project_call_edges
            WHERE project_id = @project_id
            LIMIT @limit
            """,
            conn);
        cmd.Parameters.AddWithValue("project_id", projectId);
        cmd.Parameters.AddWithValue("limit", limit);

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        var edges = new List<IndexedCallEdge>();

        while (await reader.ReadAsync(cancellationToken))
        {
            edges.Add(new IndexedCallEdge
            {
                FromSymbol = reader.GetString(0),
                ToSymbol = reader.GetString(1),
                Kind = reader.GetString(2),
            });
        }

        return edges;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<IndexedFile>> SearchFilesAsync(string projectId, string? query, int limit = 100, CancellationToken cancellationToken = default)
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);

        var sql = """
            SELECT path, hash, language, line_count, token_count, exports_json, summary
            FROM project_files
            WHERE project_id = @project_id
            """;

        if (!string.IsNullOrWhiteSpace(query))
            sql += " AND path ILIKE @query";

        sql += " ORDER BY token_count DESC LIMIT @limit";

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("project_id", projectId);
        if (!string.IsNullOrWhiteSpace(query))
            cmd.Parameters.AddWithValue("query", $"%{query}%");
        cmd.Parameters.AddWithValue("limit", limit);

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        var files = new List<IndexedFile>();

        while (await reader.ReadAsync(cancellationToken))
        {
            var exportsJson = reader.IsDBNull(5) ? "[]" : reader.GetString(5);
            var exports = JsonSerializer.Deserialize<List<string>>(exportsJson) ?? [];

            files.Add(new IndexedFile
            {
                Path = reader.GetString(0),
                Hash = reader.GetString(1),
                Language = reader.GetString(2),
                LineCount = reader.GetInt32(3),
                TokenCount = reader.GetInt32(4),
                Exports = exports,
                Summary = reader.GetString(6),
            });
        }

        return files;
    }
}
