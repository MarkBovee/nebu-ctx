namespace NebuCtx.Server.Core.Services;

using NebuCtx.Storage;
using Microsoft.Extensions.Logging;

/// <summary>
/// Knowledge service. Provides project-scoped categorized fact operations
/// for the ctx_knowledge tool (remember, recall, status, remove, categories).
/// </summary>
public sealed class KnowledgeService
{
    private readonly IKnowledgeStore _knowledgeStore;
    private readonly ILogger<KnowledgeService> _logger;

    /// <summary>
    /// Initializes the knowledge service.
    /// </summary>
    /// <param name="knowledgeStore">Knowledge persistence store.</param>
    /// <param name="logger">Logger for knowledge operations.</param>
    public KnowledgeService(IKnowledgeStore knowledgeStore, ILogger<KnowledgeService> logger)
    {
        _knowledgeStore = knowledgeStore;
        _logger = logger;
    }

    /// <summary>
    /// Stores or updates a categorized knowledge fact.
    /// </summary>
    /// <param name="projectId">Project identifier.</param>
    /// <param name="category">Logical grouping for the fact.</param>
    /// <param name="key">Unique key within the category.</param>
    /// <param name="value">Fact value.</param>
    /// <param name="confidence">Confidence score between 0 and 1. Defaults to 1.0.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task RememberAsync(string projectId, string category, string key, string value, float confidence = 1.0f, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(category)) throw new ArgumentException("Category is required.", nameof(category));
        if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("Key is required.", nameof(key));

        _logger.LogInformation("Storing knowledge fact [{Category}/{Key}] for project {ProjectId}", category, key, projectId);

        await _knowledgeStore.UpsertFactAsync(new KnowledgeEntry
        {
            ProjectId = projectId,
            Category = category,
            Key = key,
            Value = value,
            Confidence = Math.Clamp(confidence, 0f, 1f),
        }, cancellationToken);
    }

    /// <summary>
    /// Searches knowledge facts by text query, optionally filtered by category.
    /// </summary>
    /// <param name="projectId">Project identifier.</param>
    /// <param name="category">Optional category filter.</param>
    /// <param name="query">Text search query.</param>
    /// <param name="limit">Maximum results. Defaults to 10.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Matching knowledge entries ordered by confidence descending.</returns>
    public Task<IReadOnlyList<KnowledgeEntry>> RecallAsync(string projectId, string? category, string query, int limit = 10, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query)) throw new ArgumentException("Query is required for recall.", nameof(query));

        _logger.LogDebug("Recalling knowledge for project {ProjectId} query='{Query}' category='{Category}'", projectId, query, category ?? "*");
        return _knowledgeStore.RecallAsync(projectId, category, query, limit, cancellationToken);
    }

    /// <summary>
    /// Gets a status summary: total fact count and category breakdown.
    /// </summary>
    /// <param name="projectId">Project identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Status payload including fact count and category list.</returns>
    public async Task<Dictionary<string, object?>> GetStatusAsync(string projectId, CancellationToken cancellationToken = default)
    {
        var factCount = await _knowledgeStore.GetFactCountAsync(projectId, cancellationToken);
        var categories = await _knowledgeStore.GetCategoriesAsync(projectId, cancellationToken);

        return new Dictionary<string, object?>
        {
            ["project_id"] = projectId,
            ["fact_count"] = factCount,
            ["categories"] = categories.Select(c => new { category = c.Category, count = c.Count }).ToList(),
        };
    }

    /// <summary>
    /// Lists all categories with their fact counts.
    /// </summary>
    /// <param name="projectId">Project identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Category name → fact count pairs.</returns>
    public Task<IReadOnlyList<(string Category, int Count)>> GetCategoriesAsync(string projectId, CancellationToken cancellationToken = default)
    {
        return _knowledgeStore.GetCategoriesAsync(projectId, cancellationToken);
    }

    /// <summary>
    /// Removes a specific knowledge fact.
    /// </summary>
    /// <param name="projectId">Project identifier.</param>
    /// <param name="category">Fact category.</param>
    /// <param name="key">Fact key.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if the fact was found and deleted.</returns>
    public async Task<bool> RemoveAsync(string projectId, string category, string key, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Removing knowledge fact [{Category}/{Key}] for project {ProjectId}", category, key, projectId);
        return await _knowledgeStore.RemoveFactAsync(projectId, category, key, cancellationToken);
    }
}
