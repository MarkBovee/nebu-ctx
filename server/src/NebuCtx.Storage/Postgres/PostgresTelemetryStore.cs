namespace NebuCtx.Storage.Postgres;

using Npgsql;
using NpgsqlTypes;
using NebuCtx.Contracts.Telemetry;

/// <summary>
/// Persists and hydrates telemetry events in PostgreSQL via <see cref="PersistedTelemetryEvent"/>.
/// Designed for fire-and-forget writes so callers are never blocked.
/// Both the local dev container and the HA addon share the same database,
/// giving a consistent dashboard view regardless of which server instance handles a request.
/// </summary>
public sealed class PostgresTelemetryStore
{
    private readonly string _connectionString;

    /// <summary>
    /// Maximum number of events loaded on startup for hydration.
    /// Keeps startup time bounded for long-running deployments.
    /// </summary>
    private const int HydrationLimit = 50_000;

    /// <summary>
    /// Initializes the Postgres telemetry store.
    /// </summary>
    /// <param name="connectionString">Postgres connection string.</param>
    public PostgresTelemetryStore(string connectionString)
    {
        _connectionString = connectionString;
    }

    /// <summary>
    /// Persists a single telemetry event to the database.
    /// Intended for fire-and-forget invocation — exceptions are swallowed by the caller.
    /// </summary>
    /// <param name="evt">Telemetry event to persist.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task PersistEventAsync(PersistedTelemetryEvent evt, CancellationToken cancellationToken = default)
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);

        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO telemetry_events
                (occurred_at, event_type, tool_name, mode, project_id, actor_label, path, tokens_original, tokens_output, tokens_saved)
            VALUES
                (@occurred_at, @event_type, @tool_name, @mode, @project_id, @actor_label, @path, @tokens_original, @tokens_output, @tokens_saved)
            """,
            conn);

        cmd.Parameters.AddWithValue("occurred_at", evt.OccurredAt.UtcDateTime);
        cmd.Parameters.AddWithValue("event_type", evt.EventType);
        cmd.Parameters.AddWithValue("tool_name", evt.ToolName);
        cmd.Parameters.AddWithValue("mode", evt.Mode);
        cmd.Parameters.AddWithValue("project_id", evt.ProjectId);
        cmd.Parameters.AddWithValue("actor_label", evt.ActorLabel);
        cmd.Parameters.Add(new NpgsqlParameter("path", NpgsqlDbType.Text) { Value = (object?)evt.Path ?? DBNull.Value });
        cmd.Parameters.AddWithValue("tokens_original", evt.TokensOriginal);
        cmd.Parameters.AddWithValue("tokens_output", evt.TokensOutput);
        cmd.Parameters.AddWithValue("tokens_saved", evt.TokensSaved);

        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>
    /// Loads persisted telemetry events in chronological order for in-memory hydration.
    /// Returns at most <see cref="HydrationLimit"/> events to bound startup time.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Ordered list of persisted telemetry events (oldest first).</returns>
    public async Task<IReadOnlyList<PersistedTelemetryEvent>> LoadAllEventsAsync(CancellationToken cancellationToken = default)
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);

        // Load oldest-first so aggregates are accumulated in the right order during hydration.
        await using var cmd = new NpgsqlCommand(
            $"""
            SELECT occurred_at, event_type, tool_name, mode, project_id, actor_label, path,
                   tokens_original, tokens_output, tokens_saved
            FROM (
                SELECT * FROM telemetry_events ORDER BY occurred_at DESC LIMIT {HydrationLimit}
            ) recent
            ORDER BY occurred_at ASC
            """,
            conn);

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        var events = new List<PersistedTelemetryEvent>();

        while (await reader.ReadAsync(cancellationToken))
        {
            events.Add(new PersistedTelemetryEvent
            {
                OccurredAt = reader.GetFieldValue<DateTimeOffset>(0),
                EventType = reader.GetString(1),
                ToolName = reader.GetString(2),
                Mode = reader.GetString(3),
                ProjectId = reader.GetString(4),
                ActorLabel = reader.GetString(5),
                Path = reader.IsDBNull(6) ? null : reader.GetString(6),
                TokensOriginal = reader.GetInt64(7),
                TokensOutput = reader.GetInt64(8),
                TokensSaved = reader.GetInt64(9),
            });
        }

        return events;
    }
}
