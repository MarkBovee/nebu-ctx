namespace NebuCtx.Tools.Session;

using System.Text.Json;
using NebuCtx.Server.Core;
using NebuCtx.Server.Core.Services;

/// <summary>
/// Tool handler for ctx_session — project-scoped agent session state.
/// Tracks the current task, findings, and decisions across tool calls.
/// Actions: status, task, finding, decision, save, load, reset, list, cleanup.
/// </summary>
public sealed class SessionToolHandler : IToolHandler
{
    private readonly SessionService _sessionService;

    /// <summary>
    /// Initializes the session tool handler.
    /// </summary>
    /// <param name="sessionService">Session service for state operations.</param>
    public SessionToolHandler(SessionService sessionService)
    {
        _sessionService = sessionService;
    }

    /// <inheritdoc />
    public string Name => "ctx_session";

    /// <inheritdoc />
    public string Description => "Project-scoped agent session state. Track current task, findings, and decisions. Actions: status, task, finding, decision, save, load, reset, list, cleanup.";

    /// <inheritdoc />
    public Dictionary<string, object?> InputSchema => new()
    {
        ["type"] = "object",
        ["properties"] = new Dictionary<string, object?>
        {
            ["action"] = new Dictionary<string, object?>
            {
                ["type"] = "string",
                ["description"] = "Action: status, task, finding, decision, save, load, reset, list, cleanup",
                ["enum"] = new[] { "status", "task", "finding", "decision", "save", "load", "reset", "list", "cleanup" },
            },
            ["value"] = new Dictionary<string, object?>
            {
                ["type"] = "string",
                ["description"] = "Text value. Required for task, finding, and decision actions.",
            },
            ["session_id"] = new Dictionary<string, object?>
            {
                ["type"] = "string",
                ["description"] = "Session identifier. Used by load to target a specific session.",
            },
            ["limit"] = new Dictionary<string, object?>
            {
                ["type"] = "integer",
                ["description"] = "Maximum sessions for list (default: 10).",
            },
            ["days"] = new Dictionary<string, object?>
            {
                ["type"] = "integer",
                ["description"] = "Age threshold in days for cleanup (default: 7).",
            },
        },
        ["required"] = new[] { "action" },
    };

    /// <inheritdoc />
    public async Task<object> ExecuteAsync(Dictionary<string, object?> arguments, ToolExecutionContext context, CancellationToken cancellationToken = default)
    {
        var action = GetStringArg(arguments, "action");

        return action switch
        {
            "status"   => await _sessionService.GetStatusAsync(context.ProjectId, cancellationToken),
            "task"     => await ExecuteTaskAsync(arguments, context, cancellationToken),
            "finding"  => await ExecuteFindingAsync(arguments, context, cancellationToken),
            "decision" => await ExecuteDecisionAsync(arguments, context, cancellationToken),
            "save"     => await _sessionService.SaveAsync(context.ProjectId, cancellationToken),
            "load"     => await _sessionService.LoadAsync(context.ProjectId, GetStringArg(arguments, "session_id"), cancellationToken),
            "reset"    => await _sessionService.ResetAsync(context.ProjectId, cancellationToken),
            "list"     => await _sessionService.ListAsync(context.ProjectId, GetIntArg(arguments, "limit") ?? 10, cancellationToken),
            "cleanup"  => await _sessionService.CleanupAsync(context.ProjectId, GetIntArg(arguments, "days") ?? 7, cancellationToken),
            _          => throw new ArgumentException($"Unknown session action: '{action}'. Use: status, task, finding, decision, save, load, reset, list, cleanup"),
        };
    }

    /// <summary>Sets the current task description.</summary>
    private async Task<object> ExecuteTaskAsync(Dictionary<string, object?> arguments, ToolExecutionContext context, CancellationToken cancellationToken)
    {
        var value = GetStringArg(arguments, "value") ?? throw new ArgumentException("'value' is required for task.");
        return await _sessionService.SetTaskAsync(context.ProjectId, value, cancellationToken);
    }

    /// <summary>Appends a finding to the current session.</summary>
    private async Task<object> ExecuteFindingAsync(Dictionary<string, object?> arguments, ToolExecutionContext context, CancellationToken cancellationToken)
    {
        var value = GetStringArg(arguments, "value") ?? throw new ArgumentException("'value' is required for finding.");
        return await _sessionService.AddFindingAsync(context.ProjectId, value, cancellationToken);
    }

    /// <summary>Appends a decision to the current session.</summary>
    private async Task<object> ExecuteDecisionAsync(Dictionary<string, object?> arguments, ToolExecutionContext context, CancellationToken cancellationToken)
    {
        var value = GetStringArg(arguments, "value") ?? throw new ArgumentException("'value' is required for decision.");
        return await _sessionService.AddDecisionAsync(context.ProjectId, value, cancellationToken);
    }

    /// <summary>Extracts a string argument.</summary>
    private static string? GetStringArg(Dictionary<string, object?> arguments, string key)
        => arguments.TryGetValue(key, out var v) ? v?.ToString() : null;

    /// <summary>Extracts an integer argument.</summary>
    private static int? GetIntArg(Dictionary<string, object?> arguments, string key)
    {
        if (!arguments.TryGetValue(key, out var v)) return null;
        return v switch
        {
            int integer => integer,
            JsonElement { ValueKind: JsonValueKind.Number } json when json.TryGetInt32(out var parsedJson) => parsedJson,
            JsonElement { ValueKind: JsonValueKind.String } json when int.TryParse(json.GetString(), out var parsedJson) => parsedJson,
            _ => int.TryParse(v?.ToString(), out var parsed) ? parsed : null,
        };
    }
}
