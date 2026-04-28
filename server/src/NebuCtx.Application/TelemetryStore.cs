namespace NebuCtx.Application;

using System.Text.Json;

/// <summary>
/// In-memory telemetry store for dashboard and operator-facing usage statistics.
/// </summary>
public sealed class TelemetryStore
{
    private const int MaxEvents = 250;
    private readonly Lock _gate = new();
    private readonly Dictionary<string, CommandTelemetrySnapshot> _commands = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DailyTelemetrySnapshot> _daily = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, SessionTelemetrySnapshot> _sessions = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<TelemetryEventSnapshot> _events = [];
    private DateTimeOffset? _firstUse;
    private DateTimeOffset? _lastUpdated;
    private long _totalInputTokens;
    private long _totalOutputTokens;
    private int _totalToolCalls;
    private int _cacheHits = 0;

    /// <summary>
    /// Immutable telemetry snapshot used by dashboard payload builders.
    /// </summary>
    public sealed class Snapshot
    {
        /// <summary>
        /// First observed usage timestamp.
        /// </summary>
        public DateTimeOffset? FirstUse { get; init; }

        /// <summary>
        /// Last observed telemetry update timestamp.
        /// </summary>
        public DateTimeOffset? LastUpdated { get; init; }

        /// <summary>
        /// Total input tokens observed across all recorded tool calls.
        /// </summary>
        public long TotalInputTokens { get; init; }

        /// <summary>
        /// Total output tokens observed across all recorded tool calls.
        /// </summary>
        public long TotalOutputTokens { get; init; }

        /// <summary>
        /// Total tool calls observed.
        /// </summary>
        public int TotalToolCalls { get; init; }

        /// <summary>
        /// Total cache hits observed.
        /// </summary>
        public int CacheHits { get; init; }

        /// <summary>
        /// Per-command telemetry aggregation.
        /// </summary>
        public IReadOnlyDictionary<string, CommandTelemetrySnapshot> Commands { get; init; } = new Dictionary<string, CommandTelemetrySnapshot>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Daily telemetry aggregation.
        /// </summary>
        public IReadOnlyList<DailyTelemetrySnapshot> Daily { get; init; } = [];

        /// <summary>
        /// Recorded sessions grouped by actor and project.
        /// </summary>
        public IReadOnlyList<SessionTelemetrySnapshot> Sessions { get; init; } = [];

        /// <summary>
        /// Recent telemetry events.
        /// </summary>
        public IReadOnlyList<TelemetryEventSnapshot> Events { get; init; } = [];
    }

    /// <summary>
    /// Aggregated command telemetry entry.
    /// </summary>
    public sealed class CommandTelemetrySnapshot
    {
        /// <summary>
        /// Command or tool name.
        /// </summary>
        public required string Name { get; init; }

        /// <summary>
        /// Source bucket used by the dashboard UI.
        /// </summary>
        public required string Source { get; init; }

        /// <summary>
        /// Number of recorded calls.
        /// </summary>
        public int Count { get; set; }

        /// <summary>
        /// Total estimated input tokens.
        /// </summary>
        public long InputTokens { get; set; }

        /// <summary>
        /// Total estimated output tokens.
        /// </summary>
        public long OutputTokens { get; set; }
    }

    /// <summary>
    /// Daily telemetry aggregation entry.
    /// </summary>
    public sealed class DailyTelemetrySnapshot
    {
        /// <summary>
        /// UTC date key in yyyy-MM-dd format.
        /// </summary>
        public required string Date { get; init; }

        /// <summary>
        /// Total estimated input tokens for the day.
        /// </summary>
        public long InputTokens { get; set; }

        /// <summary>
        /// Total estimated output tokens for the day.
        /// </summary>
        public long OutputTokens { get; set; }

        /// <summary>
        /// Total recorded commands for the day.
        /// </summary>
        public int Commands { get; set; }
    }

    /// <summary>
    /// Session telemetry grouped by actor label and project.
    /// </summary>
    public sealed class SessionTelemetrySnapshot
    {
        /// <summary>
        /// Stable in-memory session key.
        /// </summary>
        public required string SessionKey { get; init; }

        /// <summary>
        /// Project identifier for the session.
        /// </summary>
        public required string ProjectId { get; init; }

        /// <summary>
        /// Actor label for the session.
        /// </summary>
        public required string ActorLabel { get; init; }

        /// <summary>
        /// Session start timestamp.
        /// </summary>
        public DateTimeOffset StartedAt { get; set; }

        /// <summary>
        /// Session last update timestamp.
        /// </summary>
        public DateTimeOffset UpdatedAt { get; set; }

        /// <summary>
        /// Project root associated with the session.
        /// </summary>
        public string? ProjectRoot { get; set; }

        /// <summary>
        /// Total recorded tool calls for the session.
        /// </summary>
        public int ToolCalls { get; set; }

        /// <summary>
        /// Total estimated input tokens for the session.
        /// </summary>
        public long TokensOriginal { get; set; }

        /// <summary>
        /// Total estimated output tokens for the session.
        /// </summary>
        public long TokensOutput { get; set; }

        /// <summary>
        /// Total estimated tokens saved for the session.
        /// </summary>
        public long TokensSaved { get; set; }
    }

    /// <summary>
    /// Recent telemetry event entry.
    /// </summary>
    public sealed class TelemetryEventSnapshot
    {
        /// <summary>
        /// Event timestamp.
        /// </summary>
        public DateTimeOffset Timestamp { get; init; }

        /// <summary>
        /// Event type label.
        /// </summary>
        public required string Type { get; init; }

        /// <summary>
        /// Tool or command name for the event.
        /// </summary>
        public required string ToolName { get; init; }

        /// <summary>
        /// Execution mode bucket.
        /// </summary>
        public required string Mode { get; init; }

        /// <summary>
        /// Related project identifier.
        /// </summary>
        public required string ProjectId { get; init; }

        /// <summary>
        /// Actor label for the event.
        /// </summary>
        public required string ActorLabel { get; init; }

        /// <summary>
        /// Project root or path context for the event.
        /// </summary>
        public string? Path { get; init; }

        /// <summary>
        /// Estimated original token count.
        /// </summary>
        public long TokensOriginal { get; init; }

        /// <summary>
        /// Estimated output token count.
        /// </summary>
        public long TokensOutput { get; init; }

        /// <summary>
        /// Estimated saved token count.
        /// </summary>
        public long TokensSaved { get; init; }
    }

    /// <summary>
    /// Records a successful tool call for dashboard telemetry.
    /// </summary>
    /// <param name="toolName">Executed tool name.</param>
    /// <param name="arguments">Tool arguments.</param>
    /// <param name="result">Tool result payload.</param>
    /// <param name="context">Resolved execution context.</param>
    public void RecordToolCall(string toolName, Dictionary<string, object?> arguments, object result, ToolExecutionContext context)
    {
        var now = DateTimeOffset.UtcNow;
        var actorLabel = string.IsNullOrWhiteSpace(context.ActorLabel) ? "anonymous" : context.ActorLabel!;
        var source = InferSource(toolName);
        var inputTokens = EstimateTokens(arguments);
        var outputTokens = EstimateTokens(result);
        var tokensSaved = Math.Max(0, inputTokens - outputTokens);
        var sessionKey = BuildSessionKey(context.ProjectId, actorLabel);
        var dateKey = now.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

        lock (_gate)
        {
            _firstUse ??= now;
            _lastUpdated = now;
            _totalInputTokens += inputTokens;
            _totalOutputTokens += outputTokens;
            _totalToolCalls++;

            var commandEntry = GetOrCreateCommand(toolName, source);
            commandEntry.Count++;
            commandEntry.InputTokens += inputTokens;
            commandEntry.OutputTokens += outputTokens;

            var dailyEntry = GetOrCreateDaily(dateKey);
            dailyEntry.InputTokens += inputTokens;
            dailyEntry.OutputTokens += outputTokens;
            dailyEntry.Commands++;

            var sessionEntry = GetOrCreateSession(sessionKey, context.ProjectId, actorLabel, now, context.ProjectRoot);
            sessionEntry.UpdatedAt = now;
            sessionEntry.ProjectRoot = context.ProjectRoot ?? sessionEntry.ProjectRoot;
            sessionEntry.ToolCalls++;
            sessionEntry.TokensOriginal += inputTokens;
            sessionEntry.TokensOutput += outputTokens;
            sessionEntry.TokensSaved += tokensSaved;

            _events.Add(new TelemetryEventSnapshot
            {
                Timestamp = now,
                Type = "ToolCall",
                ToolName = toolName,
                Mode = source,
                ProjectId = context.ProjectId,
                ActorLabel = actorLabel,
                Path = context.ProjectRoot ?? context.Cwd,
                TokensOriginal = inputTokens,
                TokensOutput = outputTokens,
                TokensSaved = tokensSaved,
            });

            TrimEvents();
        }
    }

    /// <summary>
    /// Returns a point-in-time snapshot of the current telemetry state.
    /// </summary>
    /// <returns>Immutable telemetry snapshot.</returns>
    public Snapshot GetSnapshot()
    {
        lock (_gate)
        {
            return new Snapshot
            {
                FirstUse = _firstUse,
                LastUpdated = _lastUpdated,
                TotalInputTokens = _totalInputTokens,
                TotalOutputTokens = _totalOutputTokens,
                TotalToolCalls = _totalToolCalls,
                CacheHits = _cacheHits,
                Commands = _commands.ToDictionary(pair => pair.Key, pair => CloneCommand(pair.Value), StringComparer.OrdinalIgnoreCase),
                Daily = _daily.Values.OrderBy(item => item.Date, StringComparer.Ordinal).Select(CloneDaily).ToArray(),
                Sessions = _sessions.Values.OrderByDescending(item => item.UpdatedAt).Select(CloneSession).ToArray(),
                Events = _events.OrderByDescending(item => item.Timestamp).Select(CloneEvent).ToArray(),
            };
        }
    }

    /// <summary>
    /// Records a tool-call event received from the Rust client via POST /v1/telemetry/ingest.
    /// Only counts and metadata are accepted — no raw content is ingested.
    /// </summary>
    /// <param name="request">Ingest request from the client.</param>
    /// <param name="projectId">Resolved project identifier (may be empty for unknown projects).</param>
    public void IngestEvent(Contracts.Mcp.TelemetryIngestRequest request, string projectId)
    {
        var now = DateTimeOffset.UtcNow;
        var source = InferSource(request.ToolName);
        var inputTokens = request.TokensOriginal;
        var outputTokens = Math.Max(0, request.TokensOriginal - request.TokensSaved);
        var actorLabel = "rust-client";
        var sessionKey = BuildSessionKey(projectId, actorLabel);
        var dateKey = now.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

        lock (_gate)
        {
            _firstUse ??= now;
            _lastUpdated = now;
            _totalInputTokens += inputTokens;
            _totalOutputTokens += outputTokens;
            _totalToolCalls++;

            var commandEntry = GetOrCreateCommand(request.ToolName, source);
            commandEntry.Count++;
            commandEntry.InputTokens += inputTokens;
            commandEntry.OutputTokens += outputTokens;

            var dailyEntry = GetOrCreateDaily(dateKey);
            dailyEntry.InputTokens += inputTokens;
            dailyEntry.OutputTokens += outputTokens;
            dailyEntry.Commands++;

            var sessionEntry = GetOrCreateSession(sessionKey, projectId, actorLabel, now, null);
            sessionEntry.UpdatedAt = now;
            sessionEntry.ToolCalls++;
            sessionEntry.TokensOriginal += inputTokens;
            sessionEntry.TokensOutput += outputTokens;
            sessionEntry.TokensSaved += request.TokensSaved;

            _events.Add(new TelemetryEventSnapshot
            {
                Timestamp = now,
                Type = "ClientIngest",
                ToolName = request.ToolName,
                Mode = request.Mode ?? source,
                ProjectId = projectId,
                ActorLabel = actorLabel,
                TokensOriginal = inputTokens,
                TokensOutput = outputTokens,
                TokensSaved = request.TokensSaved,
            });

            TrimEvents();
        }
    }

    /// <summary>
    /// Gets or creates a per-command telemetry aggregate.
    /// </summary>
    /// <param name="toolName">Command or tool name.</param>
    /// <param name="source">Dashboard source bucket.</param>
    /// <returns>Mutable telemetry aggregate.</returns>
    private CommandTelemetrySnapshot GetOrCreateCommand(string toolName, string source)
    {
        if (_commands.TryGetValue(toolName, out var entry))
        {
            return entry;
        }

        entry = new CommandTelemetrySnapshot
        {
            Name = toolName,
            Source = source,
        };
        _commands[toolName] = entry;
        return entry;
    }

    /// <summary>
    /// Gets or creates a daily telemetry aggregate.
    /// </summary>
    /// <param name="dateKey">UTC date key.</param>
    /// <returns>Mutable daily telemetry aggregate.</returns>
    private DailyTelemetrySnapshot GetOrCreateDaily(string dateKey)
    {
        if (_daily.TryGetValue(dateKey, out var entry))
        {
            return entry;
        }

        entry = new DailyTelemetrySnapshot
        {
            Date = dateKey,
        };
        _daily[dateKey] = entry;
        return entry;
    }

    /// <summary>
    /// Gets or creates an actor-scoped session aggregate.
    /// </summary>
    /// <param name="sessionKey">Stable in-memory session key.</param>
    /// <param name="projectId">Project identifier.</param>
    /// <param name="actorLabel">Actor label.</param>
    /// <param name="timestamp">Current timestamp.</param>
    /// <param name="projectRoot">Project root if available.</param>
    /// <returns>Mutable session aggregate.</returns>
    private SessionTelemetrySnapshot GetOrCreateSession(string sessionKey, string projectId, string actorLabel, DateTimeOffset timestamp, string? projectRoot)
    {
        if (_sessions.TryGetValue(sessionKey, out var entry))
        {
            return entry;
        }

        entry = new SessionTelemetrySnapshot
        {
            SessionKey = sessionKey,
            ProjectId = projectId,
            ActorLabel = actorLabel,
            StartedAt = timestamp,
            UpdatedAt = timestamp,
            ProjectRoot = projectRoot,
        };
        _sessions[sessionKey] = entry;
        return entry;
    }

    /// <summary>
    /// Estimates token count from a JSON-serializable payload.
    /// </summary>
    /// <param name="value">Payload to estimate.</param>
    /// <returns>Approximate token count.</returns>
    private static long EstimateTokens(object? value)
    {
        if (value is null)
        {
            return 0;
        }

        var payload = JsonSerializer.Serialize(value);
        return Math.Max(1, payload.Length / 4);
    }

    /// <summary>
    /// Infers the dashboard source bucket from a command name.
    /// </summary>
    /// <param name="toolName">Command or tool name.</param>
    /// <returns>Source bucket label.</returns>
    private static string InferSource(string toolName)
    {
        return toolName.StartsWith("ctx_", StringComparison.OrdinalIgnoreCase) ? "mcp" : "hook";
    }

    /// <summary>
    /// Builds a stable actor-scoped session key.
    /// </summary>
    /// <param name="projectId">Project identifier.</param>
    /// <param name="actorLabel">Actor label.</param>
    /// <returns>Stable in-memory session key.</returns>
    private static string BuildSessionKey(string projectId, string actorLabel)
    {
        return $"{projectId}:{actorLabel}";
    }

    /// <summary>
    /// Trims the recent event buffer to its configured maximum.
    /// </summary>
    private void TrimEvents()
    {
        if (_events.Count <= MaxEvents)
        {
            return;
        }

        _events.RemoveRange(0, _events.Count - MaxEvents);
    }

    /// <summary>
    /// Clones a command telemetry entry for snapshot export.
    /// </summary>
    /// <param name="entry">Mutable telemetry entry.</param>
    /// <returns>Detached command telemetry snapshot.</returns>
    private static CommandTelemetrySnapshot CloneCommand(CommandTelemetrySnapshot entry)
    {
        return new CommandTelemetrySnapshot
        {
            Name = entry.Name,
            Source = entry.Source,
            Count = entry.Count,
            InputTokens = entry.InputTokens,
            OutputTokens = entry.OutputTokens,
        };
    }

    /// <summary>
    /// Clones a daily telemetry entry for snapshot export.
    /// </summary>
    /// <param name="entry">Mutable telemetry entry.</param>
    /// <returns>Detached daily telemetry snapshot.</returns>
    private static DailyTelemetrySnapshot CloneDaily(DailyTelemetrySnapshot entry)
    {
        return new DailyTelemetrySnapshot
        {
            Date = entry.Date,
            InputTokens = entry.InputTokens,
            OutputTokens = entry.OutputTokens,
            Commands = entry.Commands,
        };
    }

    /// <summary>
    /// Clones a session telemetry entry for snapshot export.
    /// </summary>
    /// <param name="entry">Mutable telemetry entry.</param>
    /// <returns>Detached session telemetry snapshot.</returns>
    private static SessionTelemetrySnapshot CloneSession(SessionTelemetrySnapshot entry)
    {
        return new SessionTelemetrySnapshot
        {
            SessionKey = entry.SessionKey,
            ProjectId = entry.ProjectId,
            ActorLabel = entry.ActorLabel,
            StartedAt = entry.StartedAt,
            UpdatedAt = entry.UpdatedAt,
            ProjectRoot = entry.ProjectRoot,
            ToolCalls = entry.ToolCalls,
            TokensOriginal = entry.TokensOriginal,
            TokensOutput = entry.TokensOutput,
            TokensSaved = entry.TokensSaved,
        };
    }

    /// <summary>
    /// Clones a telemetry event for snapshot export.
    /// </summary>
    /// <param name="entry">Mutable telemetry entry.</param>
    /// <returns>Detached telemetry event snapshot.</returns>
    private static TelemetryEventSnapshot CloneEvent(TelemetryEventSnapshot entry)
    {
        return new TelemetryEventSnapshot
        {
            Timestamp = entry.Timestamp,
            Type = entry.Type,
            ToolName = entry.ToolName,
            Mode = entry.Mode,
            ProjectId = entry.ProjectId,
            ActorLabel = entry.ActorLabel,
            Path = entry.Path,
            TokensOriginal = entry.TokensOriginal,
            TokensOutput = entry.TokensOutput,
            TokensSaved = entry.TokensSaved,
        };
    }
}