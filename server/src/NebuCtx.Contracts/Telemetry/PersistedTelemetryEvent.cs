namespace NebuCtx.Contracts.Telemetry;

/// <summary>
/// Telemetry event record persisted to and loaded from the PostgreSQL <c>telemetry_events</c> table.
/// Used as the shared DTO between the Storage and Application layers.
/// </summary>
public sealed class PersistedTelemetryEvent
{
    /// <summary>
    /// UTC timestamp when the event occurred.
    /// </summary>
    public DateTimeOffset OccurredAt { get; init; }

    /// <summary>
    /// Event type label, e.g. <c>ToolCall</c> or <c>ClientIngest</c>.
    /// </summary>
    public required string EventType { get; init; }

    /// <summary>
    /// Tool or command name.
    /// </summary>
    public required string ToolName { get; init; }

    /// <summary>
    /// Source bucket, e.g. <c>mcp</c> or <c>hook</c>.
    /// </summary>
    public required string Mode { get; init; }

    /// <summary>
    /// Project identifier associated with the event.
    /// </summary>
    public required string ProjectId { get; init; }

    /// <summary>
    /// Actor label for the event, e.g. <c>anonymous</c> or <c>rust-client</c>.
    /// </summary>
    public required string ActorLabel { get; init; }

    /// <summary>
    /// Project root or file path context, if available.
    /// </summary>
    public string? Path { get; init; }

    /// <summary>
    /// Optional sanitized command preview for shell and hook telemetry.
    /// </summary>
    public string? CommandPreview { get; init; }

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
