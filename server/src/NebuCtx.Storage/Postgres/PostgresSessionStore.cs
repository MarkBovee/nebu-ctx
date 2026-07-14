namespace NebuCtx.Storage.Postgres;

using System.Text.Json;
using Npgsql;

/// <summary>
/// Postgres implementation of <see cref="ISessionStore"/>.
/// Persists cloud session state as JSONB per project in the session_state table.
/// </summary>
public sealed class PostgresSessionStore : ISessionStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly string _connectionString;

    /// <summary>
    /// Initializes the Postgres session store.
    /// </summary>
    /// <param name="connectionString">Postgres connection string.</param>
    public PostgresSessionStore(string connectionString)
    {
        _connectionString = connectionString;
    }

    /// <inheritdoc />
    public async Task<CloudSessionState?> LoadLatestAsync(string projectId, CancellationToken cancellationToken = default)
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);

        await using var cmd = new NpgsqlCommand(
            "SELECT state_json FROM session_state WHERE project_id = @project_id ORDER BY updated_at DESC LIMIT 1",
            conn);
        cmd.Parameters.AddWithValue("project_id", projectId);

        var json = (string?)await cmd.ExecuteScalarAsync(cancellationToken);
        return json is null ? null : JsonSerializer.Deserialize<CloudSessionState>(json, JsonOptions);
    }

    /// <inheritdoc />
    public async Task<CloudSessionState?> LoadByIdAsync(string projectId, string sessionId, CancellationToken cancellationToken = default)
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);

        await using var cmd = new NpgsqlCommand(
            "SELECT state_json FROM session_state WHERE project_id = @project_id AND session_id = @session_id",
            conn);
        cmd.Parameters.AddWithValue("project_id", projectId);
        cmd.Parameters.AddWithValue("session_id", sessionId);

        var json = (string?)await cmd.ExecuteScalarAsync(cancellationToken);
        return json is null ? null : JsonSerializer.Deserialize<CloudSessionState>(json, JsonOptions);
    }

    /// <inheritdoc />
    public async Task SaveAsync(string projectId, CloudSessionState state, CancellationToken cancellationToken = default)
    {
        state.UpdatedAt = DateTimeOffset.UtcNow;
        state.Version++;
        state.SchemaVersion = 1; // migrate-on-write: unversioned rows get upgraded on next save

        var json = JsonSerializer.Serialize(state, JsonOptions);

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);

        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO session_state (project_id, session_id, state_json, created_at, updated_at)
            VALUES (@project_id, @session_id, @state_json::jsonb, @created_at, @updated_at)
            ON CONFLICT (project_id, session_id) DO UPDATE SET
                state_json = EXCLUDED.state_json,
                updated_at = EXCLUDED.updated_at
            """,
            conn);

        cmd.Parameters.AddWithValue("project_id", projectId);
        cmd.Parameters.AddWithValue("session_id", state.SessionId);
        cmd.Parameters.AddWithValue("state_json", json);
        cmd.Parameters.AddWithValue("created_at", state.CreatedAt);
        cmd.Parameters.AddWithValue("updated_at", state.UpdatedAt);

        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<CloudSessionSummary>> ListAsync(string projectId, int limit, CancellationToken cancellationToken = default)
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);

        await using var cmd = new NpgsqlCommand(
            """
            SELECT session_id,
                   state_json->>'task'       AS task,
                   (state_json->>'version')::int AS version,
                   (state_json->>'toolCalls')::int AS tool_calls,
                   updated_at
            FROM session_state
            WHERE project_id = @project_id
            ORDER BY updated_at DESC
            LIMIT @limit
            """,
            conn);
        cmd.Parameters.AddWithValue("project_id", projectId);
        cmd.Parameters.AddWithValue("limit", limit);

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        var summaries = new List<CloudSessionSummary>();

        while (await reader.ReadAsync(cancellationToken))
        {
            summaries.Add(new CloudSessionSummary
            {
                SessionId = reader.GetString(0),
                Task = reader.IsDBNull(1) ? null : reader.GetString(1),
                Version = reader.IsDBNull(2) ? 0 : reader.GetInt32(2),
                ToolCalls = reader.IsDBNull(3) ? 0 : reader.GetInt32(3),
                UpdatedAt = reader.GetDateTime(4),
            });
        }

        return summaries;
    }

    /// <inheritdoc />
    public async Task<int> DeleteOlderThanAsync(string projectId, int daysOld, CancellationToken cancellationToken = default)
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);

        await using var cmd = new NpgsqlCommand(
            "DELETE FROM session_state WHERE project_id = @project_id AND updated_at < NOW() - INTERVAL '1 day' * @days",
            conn);
        cmd.Parameters.AddWithValue("project_id", projectId);
        cmd.Parameters.AddWithValue("days", daysOld);

        return await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<int> ClearProjectAsync(string projectId, CancellationToken cancellationToken = default)
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);

        await using var cmd = new NpgsqlCommand(
            "DELETE FROM session_state WHERE project_id = @project_id",
            conn);
        cmd.Parameters.AddWithValue("project_id", projectId);

        return await cmd.ExecuteNonQueryAsync(cancellationToken);
    }
}
