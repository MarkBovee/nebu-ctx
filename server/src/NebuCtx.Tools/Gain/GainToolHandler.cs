namespace NebuCtx.Tools.Gain;

using System.Text;
using System.Text.Json;
using NebuCtx.Application;

/// <summary>
/// MCP tool handler for ctx_gain — reports token-savings and tool-usage analytics.
/// Supports actions: report, score, tasks, agents, wrapped, json.
/// Optionally filters by project_id argument.
/// </summary>
public sealed class GainToolHandler(TelemetryStore telemetry) : IToolHandler
{
    private static readonly JsonSerializerOptions IndentedJson = new() { WriteIndented = true };
    /// <inheritdoc/>
    public string Name => "ctx_gain";

    /// <inheritdoc/>
    public string Description =>
        "Context gain analytics: token savings, tool usage, agent breakdown. " +
        "Actions: report (default), score, tasks, agents, wrapped, json.";

    /// <inheritdoc/>
    public Dictionary<string, object?> InputSchema => new()
    {
        ["type"] = "object",
        ["properties"] = new Dictionary<string, object?>
        {
            ["action"] = new Dictionary<string, object?>
            {
                ["type"] = "string",
                ["description"] = "Report type: report | score | tasks | agents | wrapped | json",
                ["default"] = "report",
            },
            ["project_id"] = new Dictionary<string, object?>
            {
                ["type"] = "string",
                ["description"] = "Optional project filter. Omit for global stats.",
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

        var result = action switch
        {
            "score" => BuildScore(snapshot, projectId),
            "tasks" => BuildTasks(snapshot, projectId),
            "agents" => BuildAgents(snapshot, projectId),
            "wrapped" => BuildWrapped(snapshot, projectId),
            "json" => BuildJson(snapshot, projectId),
            _ => BuildReport(snapshot, projectId),
        };

        return Task.FromResult<object>(result);
    }

    /// <summary>Builds a human-readable markdown report of tool usage and token savings.</summary>
    /// <param name="snapshot">Current telemetry snapshot.</param>
    /// <param name="projectId">Optional project filter; null for global stats.</param>
    /// <returns>Markdown-formatted report string.</returns>
    private static string BuildReport(TelemetryStore.Snapshot snapshot, string? projectId)
    {
        var commands = GetCommands(snapshot, projectId);
        var sb = new StringBuilder();

        var totalCalls = commands.Values.Sum(c => c.Count);
        var totalInputTokens = commands.Values.Sum(c => c.InputTokens);
        var totalOutputTokens = commands.Values.Sum(c => c.OutputTokens);

        sb.AppendLine("## Context Gain Report");
        sb.AppendLine();

        if (projectId is not null)
            sb.AppendLine($"> Project: `{projectId}`");

        sb.AppendLine($"**Total tool calls:** {totalCalls}");
        sb.AppendLine($"**Total input tokens:** {totalInputTokens:N0}");
        sb.AppendLine($"**Total output tokens:** {totalOutputTokens:N0}");
        sb.AppendLine();
        sb.AppendLine("### Top Tools");

        foreach (var cmd in commands.Values.OrderByDescending(c => c.Count).Take(10))
            sb.AppendLine($"- `{cmd.Name}`: {cmd.Count} calls ({cmd.InputTokens:N0} in / {cmd.OutputTokens:N0} out tokens)");

        return sb.ToString();
    }

    /// <summary>Returns an activity score summary line for quick display.</summary>
    /// <param name="snapshot">Current telemetry snapshot.</param>
    /// <param name="projectId">Optional project filter; null for global stats.</param>
    /// <returns>Activity score summary string.</returns>
    private static string BuildScore(TelemetryStore.Snapshot snapshot, string? projectId)
    {
        var commands = GetCommands(snapshot, projectId);
        var totalCalls = commands.Values.Sum(c => c.Count);
        var totalTokens = commands.Values.Sum(c => c.InputTokens + c.OutputTokens);
        var score = totalCalls > 0 ? (int)Math.Min(100, totalCalls * 2 + totalTokens / 1000) : 0;

        return $"Activity score: **{score}/100** ({totalCalls} tool calls, {totalTokens:N0} tokens processed)";
    }

    /// <summary>Lists tool calls grouped by inferred task category.</summary>
    /// <param name="snapshot">Current telemetry snapshot.</param>
    /// <param name="projectId">Optional project filter; null for global stats.</param>
    /// <returns>Markdown-formatted task breakdown string.</returns>
    private static string BuildTasks(TelemetryStore.Snapshot snapshot, string? projectId)
    {
        var commands = GetCommands(snapshot, projectId);
        var sb = new StringBuilder();
        sb.AppendLine("## Task Breakdown");
        sb.AppendLine();

        var groups = new Dictionary<string, List<TelemetryStore.CommandTelemetrySnapshot>>(StringComparer.OrdinalIgnoreCase)
        {
            ["read"] = [],
            ["write"] = [],
            ["search"] = [],
            ["analysis"] = [],
            ["other"] = [],
        };

        foreach (var cmd in commands.Values)
        {
            var bucket = cmd.Name switch
            {
                var n when n.Contains("read") || n.Contains("outline") || n.Contains("symbol") => "read",
                var n when n.Contains("edit") || n.Contains("write") => "write",
                var n when n.Contains("search") || n.Contains("grep") || n.Contains("glob") => "search",
                var n when n.Contains("analyze") || n.Contains("impact") || n.Contains("graph") => "analysis",
                _ => "other",
            };
            groups[bucket].Add(cmd);
        }

        foreach (var (category, cmds) in groups.Where(g => g.Value.Count > 0))
        {
            sb.AppendLine($"### {char.ToUpperInvariant(category[0])}{category[1..]}");
            foreach (var cmd in cmds.OrderByDescending(c => c.Count))
                sb.AppendLine($"- `{cmd.Name}`: {cmd.Count} calls");
            sb.AppendLine();
        }

        return sb.ToString();
    }

    /// <summary>Breaks down tool usage by agent/actor label using session records.</summary>
    /// <param name="snapshot">Current telemetry snapshot.</param>
    /// <param name="projectId">Optional project filter; null for global stats.</param>
    /// <returns>Markdown-formatted agent breakdown string.</returns>
    private static string BuildAgents(TelemetryStore.Snapshot snapshot, string? projectId)
    {
        // Sessions carry ActorLabel; Commands only carry a source-bucket label ("mcp"/"hook").
        var sessions = snapshot.Sessions
            .Where(s => projectId is null || string.Equals(s.ProjectId, projectId, StringComparison.OrdinalIgnoreCase));

        var byAgent = sessions
            .GroupBy(s => string.IsNullOrWhiteSpace(s.ActorLabel) ? "unknown" : s.ActorLabel, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Sum(s => s.ToolCalls));

        var sb = new StringBuilder();
        sb.AppendLine("## Agent Breakdown");
        sb.AppendLine();

        foreach (var group in byAgent)
        {
            var total = group.Sum(s => s.ToolCalls);
            sb.AppendLine($"### {group.Key} ({total} calls)");
            sb.AppendLine();
        }

        return sb.ToString();
    }

    /// <summary>Returns a compact period-summary similar to a "wrapped" stats view.</summary>
    /// <param name="snapshot">Current telemetry snapshot.</param>
    /// <param name="projectId">Optional project filter; null for global stats.</param>
    /// <returns>Markdown-formatted wrapped summary string.</returns>
    private static string BuildWrapped(TelemetryStore.Snapshot snapshot, string? projectId)
    {
        var commands = GetCommands(snapshot, projectId);
        var totalCalls = commands.Values.Sum(c => c.Count);
        var topTool = commands.Values.OrderByDescending(c => c.Count).FirstOrDefault();

        var sb = new StringBuilder();
        sb.AppendLine("## nebu-ctx Wrapped 🎁");
        sb.AppendLine();
        sb.AppendLine($"You made **{totalCalls}** tool calls.");
        if (topTool is not null)
            sb.AppendLine($"Your most-used tool: **`{topTool.Name}`** ({topTool.Count} times)");
        sb.AppendLine();
        sb.AppendLine("Keep shipping! 🚀");

        return sb.ToString();
    }

    /// <summary>Returns raw JSON snapshot for programmatic consumption.</summary>
    /// <param name="snapshot">Current telemetry snapshot.</param>
    /// <param name="projectId">Optional project filter; null for global stats.</param>
    /// <returns>Indented JSON string with tool-call counts and token totals.</returns>
    private static string BuildJson(TelemetryStore.Snapshot snapshot, string? projectId)
    {
        var commands = GetCommands(snapshot, projectId);
        var payload = new
        {
            total_tool_calls = commands.Values.Sum(c => c.Count),
            total_input_tokens = commands.Values.Sum(c => c.InputTokens),
            total_output_tokens = commands.Values.Sum(c => c.OutputTokens),
            project_id = projectId,
            commands = commands.Values
                .OrderByDescending(c => c.Count)
                .Select(c => new { name = c.Name, count = c.Count, input_tokens = c.InputTokens, output_tokens = c.OutputTokens }),
        };

        return JsonSerializer.Serialize(payload, IndentedJson);
    }

    /// <summary>
    /// Returns the commands dictionary for the given project filter, or global commands when no filter is provided.
    /// </summary>
    /// <param name="snapshot">Current telemetry snapshot.</param>
    /// <param name="projectId">Project identifier to filter by, or null for global view.</param>
    /// <returns>Read-only dictionary of command telemetry keyed by tool name.</returns>
    private static IReadOnlyDictionary<string, TelemetryStore.CommandTelemetrySnapshot> GetCommands(
        TelemetryStore.Snapshot snapshot, string? projectId)
        => projectId is null
            ? snapshot.Commands
            : snapshot.PerProject.TryGetValue(projectId, out var proj)
                ? proj.Commands
                : new Dictionary<string, TelemetryStore.CommandTelemetrySnapshot>(StringComparer.OrdinalIgnoreCase);
}
