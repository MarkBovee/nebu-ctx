namespace NebuCtx.Tools.Heatmap;

using System.Text;
using System.Text.Json;
using NebuCtx.Server.Core;

/// <summary>
/// MCP tool handler for ctx_heatmap — shows which files are accessed most frequently.
/// Reads file-access counts from TelemetryStore (tracked for ctx_read, ctx_edit, etc.).
/// Supports actions: status (default), directory, dirs, cold, json.
/// Optionally filters by project_id argument.
/// </summary>
public sealed class HeatmapToolHandler(TelemetryStore telemetry) : IToolHandler
{
    /// <inheritdoc/>
    public string Name => "ctx_heatmap";

    /// <inheritdoc/>
    public string Description =>
        "File-access heatmap: shows most-read files, hot directories, cold (unaccessed) paths. " +
        "Actions: status (default), directory, dirs, cold, json.";

    /// <inheritdoc/>
    public Dictionary<string, object?> InputSchema => new()
    {
        ["type"] = "object",
        ["properties"] = new Dictionary<string, object?>
        {
            ["action"] = new Dictionary<string, object?>
            {
                ["type"] = "string",
                ["description"] = "Report type: status | directory | dirs | cold | json",
                ["default"] = "status",
            },
            ["project_id"] = new Dictionary<string, object?>
            {
                ["type"] = "string",
                ["description"] = "Optional project filter. Omit for global stats.",
            },
            ["path"] = new Dictionary<string, object?>
            {
                ["type"] = "string",
                ["description"] = "Optional path prefix filter for directory/dirs actions.",
            },
        },
        ["required"] = new[] { "action" },
    };

    private static readonly JsonSerializerOptions IndentedJson = new() { WriteIndented = true };

    /// <inheritdoc/>
    public Task<object> ExecuteAsync(
        Dictionary<string, object?> arguments,
        ToolExecutionContext context,
        CancellationToken ct)
    {
        var action = arguments.TryGetValue("action", out var a) ? a?.ToString() ?? "status" : "status";
        var projectId = arguments.TryGetValue("project_id", out var p) ? p?.ToString() : null;
        var pathPrefix = arguments.TryGetValue("path", out var pp) ? pp?.ToString() : null;
        var snapshot = telemetry.GetSnapshot();
        var fileAccess = GetFileAccess(snapshot, projectId);

        var result = action switch
        {
            "directory" or "dirs" => BuildDirectory(fileAccess, pathPrefix, projectId),
            "cold" => BuildCold(fileAccess, projectId),
            "json" => BuildJson(fileAccess, projectId),
            _ => BuildStatus(fileAccess, projectId),
        };

        return Task.FromResult<object>(result);
    }

    /// <summary>
    /// Returns the file-access dictionary for the given project, or aggregated global access when no project specified.
    /// An unknown project_id returns an empty dictionary.
    /// </summary>
    /// <param name="snapshot">Current telemetry snapshot.</param>
    /// <param name="projectId">Optional project to filter by. Null means aggregate all projects.</param>
    /// <returns>Dictionary mapping file path to access count.</returns>
    private static IReadOnlyDictionary<string, int> GetFileAccess(
        TelemetryStore.Snapshot snapshot, string? projectId)
    {
        if (projectId is null)
        {
            // Aggregate across all projects
            var global = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var proj in snapshot.PerProject.Values)
            {
                foreach (var (path, count) in proj.FileAccess)
                    global[path] = global.TryGetValue(path, out var prev) ? prev + count : count;
            }
            return global;
        }

        return snapshot.GetFileAccess(projectId);
    }

    /// <summary>Returns a one-line status summary of total tracked files and hot-file count.</summary>
    /// <param name="fileAccess">File-access dictionary to summarise.</param>
    /// <param name="projectId">Project context for labelling the output, or null for global.</param>
    /// <returns>Human-readable single-line status string.</returns>
    private static string BuildStatus(IReadOnlyDictionary<string, int> fileAccess, string? projectId)
    {
        var totalFiles = fileAccess.Count;
        var totalAccesses = fileAccess.Values.Sum();
        var hotFiles = fileAccess.Count(f => f.Value >= 3);
        var projectSuffix = projectId is not null ? $" (project: {projectId})" : string.Empty;

        return $"File heatmap{projectSuffix}: {totalFiles} tracked files, {totalAccesses} total accesses, {hotFiles} hot files (≥3 accesses)";
    }

    /// <summary>Groups file accesses by directory and returns the hottest directories.</summary>
    /// <param name="fileAccess">File-access dictionary to group.</param>
    /// <param name="pathPrefix">Optional prefix to restrict results. Null means all paths.</param>
    /// <param name="projectId">Project context for the output heading, or null for global.</param>
    /// <returns>Markdown-formatted directory heatmap.</returns>
    private static string BuildDirectory(
        IReadOnlyDictionary<string, int> fileAccess, string? pathPrefix, string? projectId)
    {
        var filtered = pathPrefix is not null
            ? fileAccess.Where(f => f.Key.StartsWith(pathPrefix, StringComparison.OrdinalIgnoreCase))
            : fileAccess;

        var byDir = filtered
            .GroupBy(f => GetDirectory(f.Key), StringComparer.OrdinalIgnoreCase)
            .Select(g => (Directory: g.Key, Count: g.Sum(f => f.Value), Files: g.Count()))
            .OrderByDescending(d => d.Count)
            .Take(20);

        var sb = new StringBuilder();
        sb.AppendLine("## Hot Directories");
        sb.AppendLine();

        if (projectId is not null) sb.AppendLine($"> Project: `{projectId}`");
        if (pathPrefix is not null) sb.AppendLine($"> Path prefix: `{pathPrefix}`");

        foreach (var (dir, count, files) in byDir)
            sb.AppendLine($"- `{dir}` — {count} accesses across {files} file(s)");

        return sb.ToString();
    }

    /// <summary>Reports files with relatively low access counts compared to the median.</summary>
    /// <param name="fileAccess">File-access dictionary to analyse.</param>
    /// <param name="projectId">Project context for the output heading, or null for global.</param>
    /// <returns>Markdown-formatted cold-file report or informational message.</returns>
    private static string BuildCold(IReadOnlyDictionary<string, int> fileAccess, string? projectId)
    {
        var projectSuffix = projectId is not null ? $" (project: {projectId})" : string.Empty;

        if (fileAccess.Count == 0)
            return $"No file-access data recorded{projectSuffix}. Run ctx_read or ctx_edit on some files first.";

        // Cold = files accessed below the median (relatively cold compared to the rest)
        var median = fileAccess.Values.OrderBy(v => v).ElementAt(fileAccess.Count / 2);
        var cold = fileAccess.Where(f => f.Value < median).OrderBy(f => f.Value).Take(20).ToList();

        if (cold.Count == 0)
            return $"No cold files detected{projectSuffix} — all files have similar access frequency.";

        var sb = new StringBuilder();
        sb.AppendLine($"## Cold Files{projectSuffix}");
        sb.AppendLine($"> Files accessed below median ({median} accesses)");
        sb.AppendLine();
        foreach (var (path, count) in cold)
            sb.AppendLine($"- `{path}` ({count} access)");

        return sb.ToString();
    }

    /// <summary>Returns raw JSON heatmap data for programmatic consumption.</summary>
    /// <param name="fileAccess">File-access dictionary to serialise.</param>
    /// <param name="projectId">Project filter applied; included in the JSON payload so callers can confirm scope.</param>
    /// <returns>Indented JSON string with project_id, total_files, total_accesses, and files array.</returns>
    private static string BuildJson(IReadOnlyDictionary<string, int> fileAccess, string? projectId)
    {
        var payload = new
        {
            project_id = projectId,
            total_files = fileAccess.Count,
            total_accesses = fileAccess.Values.Sum(),
            files = fileAccess
                .OrderByDescending(f => f.Value)
                .Select(f => new { path = f.Key, count = f.Value }),
        };

        return JsonSerializer.Serialize(payload, IndentedJson);
    }

    /// <summary>Returns the parent directory path of a file path string.</summary>
    /// <param name="filePath">Absolute or relative file path.</param>
    /// <returns>Parent directory, or "/" for root-level paths, or the path itself if no separator found.</returns>
    private static string GetDirectory(string filePath)
    {
        var lastSep = filePath.LastIndexOfAny(['/', '\\']);
        if (lastSep < 0) return filePath;
        var dir = filePath[..lastSep];
        return dir.Length == 0 ? "/" : dir;
    }
}
