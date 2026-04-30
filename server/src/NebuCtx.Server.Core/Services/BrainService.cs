namespace NebuCtx.Server.Core.Services;

using NebuCtx.Storage;
using Microsoft.Extensions.Logging;

/// <summary>
/// Brain memory service. Provides project-scoped memory operations
/// for the ctx_brain tool family (status, store, recall).
/// </summary>
public sealed class BrainService
{
    private readonly IBrainStore _brainStore;
    private readonly ILogger<BrainService> _logger;

    /// <summary>
    /// Initializes the brain service.
    /// </summary>
    /// <param name="brainStore">Brain memory store.</param>
    /// <param name="logger">Logger for brain operations.</param>
    public BrainService(IBrainStore brainStore, ILogger<BrainService> logger)
    {
        _brainStore = brainStore;
        _logger = logger;
    }

    /// <summary>
    /// Gets the brain status for a project.
    /// </summary>
    /// <param name="projectId">Project identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Brain status payload.</returns>
    public async Task<Dictionary<string, object?>> GetStatusAsync(string projectId, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Getting brain status for project {ProjectId}", projectId);
        var status = await _brainStore.GetStatusAsync(projectId, cancellationToken);
        return status ?? new Dictionary<string, object?> { ["project_id"] = projectId, ["entry_count"] = 0 };
    }

    /// <summary>
    /// Stores a memory entry for a project.
    /// </summary>
    /// <param name="projectId">Project identifier.</param>
    /// <param name="key">Memory key.</param>
    /// <param name="value">Memory value.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task StoreAsync(string projectId, string key, string value, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new ArgumentException("Memory key cannot be empty.", nameof(key));
        }

        _logger.LogInformation("Storing brain entry '{Key}' for project {ProjectId}", key, projectId);
        await _brainStore.StoreAsync(projectId, key, value, cancellationToken);
    }

    /// <summary>
    /// Recalls memory entries matching a query.
    /// </summary>
    /// <param name="projectId">Project identifier.</param>
    /// <param name="query">Search query.</param>
    /// <param name="limit">Maximum results.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Matching brain entries.</returns>
    public async Task<IReadOnlyList<BrainEntry>> RecallAsync(string projectId, string query, int limit = 10, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Recalling brain entries for project {ProjectId} with query '{Query}'", projectId, query);
        return await _brainStore.RecallAsync(projectId, query, limit, cancellationToken);
    }
}
