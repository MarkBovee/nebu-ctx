namespace NebuCtx.Tools.Cost;

using System.Text;
using System.Text.Json;
using NebuCtx.Application;

/// <summary>
/// MCP tool handler for ctx_cost — reports token usage and estimated cost per session and per tool.
/// Supports actions: report (default), tools, status, json.
/// Uses a fixed price of $2.50 per million tokens (input + output combined).
/// Optionally filters by project_id argument.
/// </summary>
public sealed class CostToolHandler(TelemetryStore telemetry) : IToolHandler
{
    private const decimal PricePerMillionTokens = 2.50m;

    private static readonly JsonSerializerOptions IndentedJson = new() { WriteIndented = true };

    /// <inheritdoc/>
    public string Name => "ctx_cost";

    /// <inheritdoc/>
    public string Description =>
        "Token usage and estimated cost (at $2.50/1M tokens) across all tool calls. " +
        "Actions: report (default), tools, status, json.";

    /// <inheritdoc/>
    public Dictionary<string, object?> InputSchema => new()
    {
        ["type"] = "object",
        ["properties"] = new Dictionary<string, object?>
        {
            ["action"] = new Dictionary<string, object?>
            {
                ["type"] = "string",
                ["description"] = "Report type: report | tools | status | json",
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
            "tools" => BuildTools(snapshot, projectId),
            "status" => BuildStatus(snapshot, projectId),
            "json" => BuildJson(snapshot, projectId),
            _ => BuildReport(snapshot, projectId),
        };

        return Task.FromResult<object>(result);
    }

    /// <summary>Estimates cost in USD for a given token count using the fixed pricing constant.</summary>
    /// <param name="tokens">Total token count (input + output).</param>
    /// <returns>Estimated cost in USD.</returns>
    private static decimal EstimateCost(long tokens)
        => tokens / 1_000_000m * PricePerMillionTokens;

    /// <summary>Returns the commands dictionary for the given project filter, or global when none provided.</summary>
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

    /// <summary>Builds a human-readable cost summary report.</summary>
    /// <param name="snapshot">Current telemetry snapshot.</param>
    /// <param name="projectId">Optional project filter; null for global stats.</param>
    /// <returns>Markdown-formatted cost report string.</returns>
    private static string BuildReport(TelemetryStore.Snapshot snapshot, string? projectId)
    {
        var commands = GetCommands(snapshot, projectId);
        var totalInput = commands.Values.Sum(c => c.InputTokens);
        var totalOutput = commands.Values.Sum(c => c.OutputTokens);
        var totalTokens = totalInput + totalOutput;
        var cost = EstimateCost(totalTokens);

        var sb = new StringBuilder();
        sb.AppendLine("## Cost Report");
        sb.AppendLine();

        if (projectId is not null)
            sb.AppendLine($"> Project: `{projectId}`");

        sb.AppendLine($"**Estimated Cost:** ${cost:F4} USD");
        sb.AppendLine($"**Total Tokens:** {totalTokens:N0} (input: {totalInput:N0} / output: {totalOutput:N0})");
        sb.AppendLine($"**Pricing:** ${PricePerMillionTokens}/1M tokens");

        return sb.ToString();
    }

    /// <summary>Returns a per-tool token and cost breakdown.</summary>
    /// <param name="snapshot">Current telemetry snapshot.</param>
    /// <param name="projectId">Optional project filter; null for global stats.</param>
    /// <returns>Markdown-formatted per-tool cost breakdown string.</returns>
    private static string BuildTools(TelemetryStore.Snapshot snapshot, string? projectId)
    {
        var commands = GetCommands(snapshot, projectId);
        var sb = new StringBuilder();
        sb.AppendLine("## Cost by Tool");
        sb.AppendLine();

        if (projectId is not null)
            sb.AppendLine($"> Project: `{projectId}`");

        foreach (var cmd in commands.Values.OrderByDescending(c => c.InputTokens + c.OutputTokens))
        {
            var tokens = cmd.InputTokens + cmd.OutputTokens;
            var cost = EstimateCost(tokens);
            sb.AppendLine($"- `{cmd.Name}`: {tokens:N0} tokens → ${cost:F6}");
        }

        return sb.ToString();
    }

    /// <summary>Returns a one-line status summary of token usage and cost.</summary>
    /// <param name="snapshot">Current telemetry snapshot.</param>
    /// <param name="projectId">Optional project filter; null for global stats.</param>
    /// <returns>Single-line status string with total token count and estimated cost.</returns>
    private static string BuildStatus(TelemetryStore.Snapshot snapshot, string? projectId)
    {
        var commands = GetCommands(snapshot, projectId);
        var totalTokens = commands.Values.Sum(c => c.InputTokens + c.OutputTokens);
        var cost = EstimateCost(totalTokens);
        var projectSuffix = projectId is not null ? $" (project: {projectId})" : string.Empty;

        return $"Token usage{projectSuffix}: {totalTokens:N0} tokens — estimated cost: ${cost:F4} USD";
    }

    /// <summary>Returns raw JSON for programmatic consumption.</summary>
    /// <param name="snapshot">Current telemetry snapshot.</param>
    /// <param name="projectId">Optional project filter; null for global stats.</param>
    /// <returns>Indented JSON string with token totals, cost estimate, and per-tool breakdown.</returns>
    private static string BuildJson(TelemetryStore.Snapshot snapshot, string? projectId)
    {
        var commands = GetCommands(snapshot, projectId);
        var totalInput = commands.Values.Sum(c => c.InputTokens);
        var totalOutput = commands.Values.Sum(c => c.OutputTokens);
        var totalTokens = totalInput + totalOutput;

        var payload = new
        {
            project_id = projectId,
            total_tokens = totalTokens,
            total_input_tokens = totalInput,
            total_output_tokens = totalOutput,
            estimated_cost_usd = (double)EstimateCost(totalTokens),
            price_per_million_tokens = (double)PricePerMillionTokens,
            tools = commands.Values
                .OrderByDescending(c => c.InputTokens + c.OutputTokens)
                .Select(c => new
                {
                    name = c.Name,
                    tokens = c.InputTokens + c.OutputTokens,
                    input_tokens = c.InputTokens,
                    output_tokens = c.OutputTokens,
                    estimated_cost_usd = (double)EstimateCost(c.InputTokens + c.OutputTokens),
                }),
        };

        return JsonSerializer.Serialize(payload, IndentedJson);
    }
}
