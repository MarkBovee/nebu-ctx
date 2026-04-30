namespace NebuCtx.Tools.Stats;

using System.Text;
using System.Text.Json;
using NebuCtx.Server.Core;

/// <summary>
/// MCP tool handler for ctx_stats — per-project tool-usage statistics.
/// Aggregates data from TelemetryStore's per-project counters.
/// Supports actions: report (default), json.
/// Optionally filters to a single project via the project_id argument.
/// </summary>
public sealed class StatsToolHandler(TelemetryStore telemetry) : IToolHandler
{
    private static readonly JsonSerializerOptions IndentedJson = new() { WriteIndented = true };
    /// <inheritdoc/>
    public string Name => "ctx_stats";

    /// <inheritdoc/>
    public string Description =>
        "Per-project tool usage statistics. " +
        "Actions: report (default), json.";

    /// <inheritdoc/>
    public Dictionary<string, object?> InputSchema => new()
    {
        ["type"] = "object",
        ["properties"] = new Dictionary<string, object?>
        {
            ["action"] = new Dictionary<string, object?>
            {
                ["type"] = "string",
                ["description"] = "Report type: report | json",
                ["default"] = "report",
            },
            ["project_id"] = new Dictionary<string, object?>
            {
                ["type"] = "string",
                ["description"] = "Optional: filter to a single project. Omit for all projects.",
            },
        },
        ["required"] = new[] { "action" },
    };

    /// <inheritdoc/>
    public Task<object> ExecuteAsync(
        Dictionary<string, object?> arguments,
        ToolExecutionContext context,
        CancellationToken ct)
    {
        var action = arguments.TryGetValue("action", out var a) ? a?.ToString() ?? "report" : "report";
        var projectId = arguments.TryGetValue("project_id", out var p) ? p?.ToString() : null;
        var snapshot = telemetry.GetSnapshot();
        var projects = GetProjects(snapshot, projectId);

        var result = action switch
        {
            "json" => BuildJson(projects, projectId),
            _ => BuildReport(projects, projectId),
        };

        return Task.FromResult<object>(result);
    }

    /// <summary>
    /// Returns the filtered project snapshots — either a single project, all projects, or empty for unknown project_id.
    /// </summary>
    /// <param name="snapshot">Current telemetry snapshot.</param>
    /// <param name="projectId">Optional project filter; null returns all projects.</param>
    /// <returns>Ordered sequence of matching project snapshots.</returns>
    private static IEnumerable<TelemetryStore.ProjectTelemetrySnapshot> GetProjects(
        TelemetryStore.Snapshot snapshot, string? projectId)
    {
        if (projectId is null)
            return snapshot.PerProject.Values.OrderByDescending(p => p.TotalToolCalls);

        return snapshot.PerProject.TryGetValue(projectId, out var proj)
            ? [proj]
            : [];
    }

    /// <summary>Builds a human-readable markdown report of per-project tool usage.</summary>
    /// <param name="projects">Project snapshots to include.</param>
    /// <param name="projectId">Optional project filter label.</param>
    private static string BuildReport(
        IEnumerable<TelemetryStore.ProjectTelemetrySnapshot> projects, string? projectId)
    {
        var sb = new StringBuilder();
        sb.AppendLine("## Project Stats");
        sb.AppendLine();

        if (projectId is not null)
            sb.AppendLine($"> Project: `{projectId}`");

        foreach (var proj in projects)
        {
            sb.AppendLine($"### `{proj.ProjectId}`");
            sb.AppendLine($"- Total calls: {proj.TotalToolCalls}");
            sb.AppendLine($"- Input tokens: {proj.TotalInputTokens:N0}");
            sb.AppendLine($"- Output tokens: {proj.TotalOutputTokens:N0}");

            var topTools = proj.Commands.Values.OrderByDescending(c => c.Count).Take(5);
            foreach (var cmd in topTools)
                sb.AppendLine($"  - `{cmd.Name}`: {cmd.Count}");

            sb.AppendLine();
        }

        return sb.ToString();
    }

    /// <summary>Returns raw JSON with per-project stats for programmatic consumption.</summary>
    /// <param name="projects">Project snapshots to include.</param>
    /// <param name="projectId">Optional project filter label included in the output.</param>
    private static string BuildJson(
        IEnumerable<TelemetryStore.ProjectTelemetrySnapshot> projects, string? projectId)
    {
        var payload = new
        {
            project_id_filter = projectId,
            projects = projects.Select(p => new
            {
                project_id = p.ProjectId,
                total_tool_calls = p.TotalToolCalls,
                total_input_tokens = p.TotalInputTokens,
                total_output_tokens = p.TotalOutputTokens,
                top_tools = p.Commands.Values
                    .OrderByDescending(c => c.Count)
                    .Take(10)
                    .Select(c => new { name = c.Name, count = c.Count }),
            }),
        };

        return JsonSerializer.Serialize(payload, IndentedJson);
    }
}
