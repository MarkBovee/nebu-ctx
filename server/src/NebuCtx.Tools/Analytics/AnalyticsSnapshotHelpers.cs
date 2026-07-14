namespace NebuCtx.Tools.Analytics;

using System.Text.Json;
using NebuCtx.Server.Core;

/// <summary>
/// Shared helpers for analytics tool handlers (Cost, Gain, Heatmap, Stats).
/// Consolidates duplicated IndentedJson and GetCommands.
/// </summary>
internal static class AnalyticsSnapshotHelpers
{
    internal static readonly JsonSerializerOptions IndentedJson = new() { WriteIndented = true };

    internal static IReadOnlyDictionary<string, TelemetryStore.CommandTelemetrySnapshot> GetCommands(
        TelemetryStore.Snapshot snapshot, string? projectId)
        => projectId is null
            ? snapshot.Commands
            : snapshot.PerProject.TryGetValue(projectId, out var proj)
                ? proj.Commands
                : new Dictionary<string, TelemetryStore.CommandTelemetrySnapshot>(StringComparer.OrdinalIgnoreCase);
}
