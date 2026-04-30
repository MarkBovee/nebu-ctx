namespace NebuCtx.Server.Core.Services;

using NebuCtx.Storage;
using Microsoft.Extensions.Logging;

/// <summary>
/// Session service. Provides project-scoped agent session state operations
/// for the ctx_session tool (status, task, finding, decision, save, load, reset, list, cleanup).
/// </summary>
public sealed class SessionService
{
    private readonly ISessionStore _sessionStore;
    private readonly ILogger<SessionService> _logger;

    /// <summary>
    /// Initializes the session service.
    /// </summary>
    /// <param name="sessionStore">Session persistence store.</param>
    /// <param name="logger">Logger for session operations.</param>
    public SessionService(ISessionStore sessionStore, ILogger<SessionService> logger)
    {
        _sessionStore = sessionStore;
        _logger = logger;
    }

    /// <summary>
    /// Returns the current (latest) session state for a project, creating a fresh one if none exists.
    /// </summary>
    /// <param name="projectId">Project identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Current session state as a status payload.</returns>
    public async Task<Dictionary<string, object?>> GetStatusAsync(string projectId, CancellationToken cancellationToken = default)
    {
        var state = await _sessionStore.LoadLatestAsync(projectId, cancellationToken) ?? new CloudSessionState();
        return FormatState(state);
    }

    /// <summary>
    /// Sets the task description on the current session and saves it.
    /// </summary>
    /// <param name="projectId">Project identifier.</param>
    /// <param name="description">Task description to set.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Confirmation message.</returns>
    public async Task<string> SetTaskAsync(string projectId, string description, CancellationToken cancellationToken = default)
    {
        var state = await _sessionStore.LoadLatestAsync(projectId, cancellationToken) ?? new CloudSessionState();
        state.Task = description;
        await _sessionStore.SaveAsync(projectId, state, cancellationToken);

        _logger.LogInformation("Session task set for project {ProjectId}: {Task}", projectId, description);
        return $"Task set: {description}";
    }

    /// <summary>
    /// Appends a finding to the current session and saves it.
    /// </summary>
    /// <param name="projectId">Project identifier.</param>
    /// <param name="finding">Finding text to record.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Confirmation message.</returns>
    public async Task<string> AddFindingAsync(string projectId, string finding, CancellationToken cancellationToken = default)
    {
        var state = await _sessionStore.LoadLatestAsync(projectId, cancellationToken) ?? new CloudSessionState();
        state.Findings.Add(finding);
        await _sessionStore.SaveAsync(projectId, state, cancellationToken);

        _logger.LogInformation("Session finding added for project {ProjectId}", projectId);
        return $"Finding recorded: {finding}";
    }

    /// <summary>
    /// Appends a decision to the current session and saves it.
    /// </summary>
    /// <param name="projectId">Project identifier.</param>
    /// <param name="decision">Decision text to record.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Confirmation message.</returns>
    public async Task<string> AddDecisionAsync(string projectId, string decision, CancellationToken cancellationToken = default)
    {
        var state = await _sessionStore.LoadLatestAsync(projectId, cancellationToken) ?? new CloudSessionState();
        state.Decisions.Add(decision);
        await _sessionStore.SaveAsync(projectId, state, cancellationToken);

        _logger.LogInformation("Session decision recorded for project {ProjectId}", projectId);
        return $"Decision recorded: {decision}";
    }

    /// <summary>
    /// Explicitly saves the current session state (increments version).
    /// </summary>
    /// <param name="projectId">Project identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Confirmation with session id and version.</returns>
    public async Task<string> SaveAsync(string projectId, CancellationToken cancellationToken = default)
    {
        var state = await _sessionStore.LoadLatestAsync(projectId, cancellationToken) ?? new CloudSessionState();
        await _sessionStore.SaveAsync(projectId, state, cancellationToken);
        return $"Session {state.SessionId} saved (v{state.Version}).";
    }

    /// <summary>
    /// Loads a specific session by id, or the latest one when no id is provided.
    /// </summary>
    /// <param name="projectId">Project identifier.</param>
    /// <param name="sessionId">Optional session id to load.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Session status payload, or a not-found message.</returns>
    public async Task<object> LoadAsync(string projectId, string? sessionId, CancellationToken cancellationToken = default)
    {
        var state = sessionId is not null
            ? await _sessionStore.LoadByIdAsync(projectId, sessionId, cancellationToken)
            : await _sessionStore.LoadLatestAsync(projectId, cancellationToken);

        if (state is null)
        {
            var idStr = sessionId ?? "latest";
            return new { found = false, message = $"No session found (id: {idStr}). Starting fresh." };
        }

        return new { found = true, session = FormatState(state) };
    }

    /// <summary>
    /// Saves the current session and creates a fresh one.
    /// </summary>
    /// <param name="projectId">Project identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Reset confirmation with old and new session ids.</returns>
    public async Task<string> ResetAsync(string projectId, CancellationToken cancellationToken = default)
    {
        var oldState = await _sessionStore.LoadLatestAsync(projectId, cancellationToken);
        if (oldState is not null)
        {
            await _sessionStore.SaveAsync(projectId, oldState, cancellationToken);
        }

        var newState = new CloudSessionState();
        await _sessionStore.SaveAsync(projectId, newState, cancellationToken);

        var oldId = oldState?.SessionId ?? "(none)";
        _logger.LogInformation("Session reset for project {ProjectId}: {OldId} → {NewId}", projectId, oldId, newState.SessionId);
        return $"Session reset. Previous: {oldId}. New: {newState.SessionId}";
    }

    /// <summary>
    /// Lists recent sessions for a project.
    /// </summary>
    /// <param name="projectId">Project identifier.</param>
    /// <param name="limit">Maximum sessions to list. Defaults to 10.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Session summary list.</returns>
    public async Task<object> ListAsync(string projectId, int limit = 10, CancellationToken cancellationToken = default)
    {
        var sessions = await _sessionStore.ListAsync(projectId, limit, cancellationToken);
        return new
        {
            count = sessions.Count,
            sessions = sessions.Select(s => new
            {
                session_id = s.SessionId,
                version = s.Version,
                task = s.Task ?? "(no task)",
                tool_calls = s.ToolCalls,
                updated_at = s.UpdatedAt,
            }),
        };
    }

    /// <summary>
    /// Deletes sessions older than the specified number of days.
    /// </summary>
    /// <param name="projectId">Project identifier.</param>
    /// <param name="daysOld">Age threshold in days. Defaults to 7.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Cleanup result message.</returns>
    public async Task<string> CleanupAsync(string projectId, int daysOld = 7, CancellationToken cancellationToken = default)
    {
        var removed = await _sessionStore.DeleteOlderThanAsync(projectId, daysOld, cancellationToken);
        return $"Cleaned up {removed} old session(s) (>{daysOld} days).";
    }

    /// <summary>
    /// Formats a session state as a status dictionary for tool responses.
    /// </summary>
    private static Dictionary<string, object?> FormatState(CloudSessionState state)
    {
        return new Dictionary<string, object?>
        {
            ["session_id"] = state.SessionId,
            ["version"] = state.Version,
            ["task"] = state.Task,
            ["findings"] = state.Findings,
            ["decisions"] = state.Decisions,
            ["tool_calls"] = state.ToolCalls,
            ["updated_at"] = state.UpdatedAt,
        };
    }
}
