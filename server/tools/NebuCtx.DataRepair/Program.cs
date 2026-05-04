using System.Diagnostics;
using System.Text.Json;
using NebuCtx.Contracts.Projects;
using NebuCtx.Server.Core;
using NebuCtx.Storage;
using Npgsql;

var databaseUrl = Environment.GetEnvironmentVariable("DATABASE_URL");
if (string.IsNullOrWhiteSpace(databaseUrl))
{
    Console.Error.WriteLine("DATABASE_URL is required.");
    return 1;
}

var connectionString = StoreFactory.NormalizePostgresConnectionString(databaseUrl);
var deleteUnresolved = string.Equals(
    Environment.GetEnvironmentVariable("NEBU_REPAIR_DELETE_UNRESOLVED"),
    "1",
    StringComparison.Ordinal);
await using var connection = new NpgsqlConnection(connectionString);
await connection.OpenAsync();

var candidates = await LoadCandidateProjectsAsync(connection);
var beforeCounts = await LoadTableCountsAsync(connection, candidates.Select(project => project.ProjectId).ToArray());
var inspections = await InspectProjectsAsync(candidates);

WriteJson(new
{
    phase = "inspect-before",
    candidate_count = candidates.Count,
    candidates = candidates.Select(project => BuildProjectInspection(project, inspections)).ToArray(),
    table_counts = beforeCounts,
});

await using var transaction = await connection.BeginTransactionAsync();

var deletedProjects = new List<object>();
foreach (var project in candidates.Where(LegacyProjectCleanupRules.IsSafeToDelete))
{
    var deletions = await DeleteProjectEverywhereAsync(connection, transaction, project.ProjectId);
    deletedProjects.Add(new
    {
        project.ProjectId,
        project.Slug,
        deletions,
        reason = "stale-legacy",
    });
}

if (deleteUnresolved)
{
    foreach (var project in candidates.Where(project =>
                 inspections.TryGetValue(project.ProjectId, out var inspection)
                 && LegacyProjectCleanupRules.IsUnresolvedLegacyProject(project, inspection.Fingerprint)))
    {
        var deletions = await DeleteProjectEverywhereAsync(connection, transaction, project.ProjectId);
        deletedProjects.Add(new
        {
            project.ProjectId,
            project.Slug,
            deletions,
            reason = "unresolved-legacy-no-repo",
        });
    }
}

var migratedProjects = new List<object>();
foreach (var project in candidates.Where(project => !LegacyProjectCleanupRules.IsSafeToDelete(project)))
{
    if (!inspections.TryGetValue(project.ProjectId, out var inspection)
        || inspection.Fingerprint is null
        || string.IsNullOrWhiteSpace(inspection.CanonicalSlug))
    {
        continue;
    }

    var targetProject = await FindOrCreateCanonicalProjectAsync(connection, transaction, inspection, project);
    if (string.Equals(targetProject.ProjectId, project.ProjectId, StringComparison.OrdinalIgnoreCase))
    {
        continue;
    }

    var reassigned = await ReassignProjectScopedDataAsync(connection, transaction, project.ProjectId, targetProject.ProjectId);
    await DeleteByProjectIdAsync(connection, transaction, "projects", project.ProjectId);

    migratedProjects.Add(new
    {
        from_project_id = project.ProjectId,
        from_slug = project.Slug,
        to_project_id = targetProject.ProjectId,
        to_slug = targetProject.Slug,
        local_root = inspection.LocalRoot,
        remote_url = inspection.Fingerprint.RemoteUrl,
        repo_name = inspection.Fingerprint.RepoName,
        reassigned,
    });
}

await transaction.CommitAsync();

var touchedProjectIds = deletedProjects.Select(item => (string)item.GetType().GetProperty("ProjectId")!.GetValue(item)!).ToList();
touchedProjectIds.AddRange(migratedProjects.Select(item => (string)item.GetType().GetProperty("from_project_id")!.GetValue(item)!).ToList());

var afterCounts = touchedProjectIds.Count == 0
    ? new Dictionary<string, Dictionary<string, int>>(StringComparer.OrdinalIgnoreCase)
    : await LoadTableCountsAsync(connection, touchedProjectIds.ToArray());

WriteJson(new
{
    phase = "cleanup",
    deleted_project_count = deletedProjects.Count,
    deleted_projects = deletedProjects,
    migrated_project_count = migratedProjects.Count,
    migrated_projects = migratedProjects,
    table_counts_after = afterCounts,
});

return 0;

static async Task<List<ProjectRecord>> LoadCandidateProjectsAsync(NpgsqlConnection connection)
{
    const string sql =
        """
        SELECT project_id, slug, remote_url, host, owner, repo_name, default_branch, project_metadata_json::text, created_at, updated_at
        FROM projects
        WHERE slug ILIKE '%mark%'
        ORDER BY created_at
        """;

    await using var command = new NpgsqlCommand(sql, connection);
    await using var reader = await command.ExecuteReaderAsync();

    var projects = new List<ProjectRecord>();
    while (await reader.ReadAsync())
    {
        projects.Add(MapProject(reader));
    }

    return projects;
}

static async Task<Dictionary<string, ProjectInspection>> InspectProjectsAsync(IEnumerable<ProjectRecord> projects)
{
    var inspections = new Dictionary<string, ProjectInspection>(StringComparer.OrdinalIgnoreCase);
    foreach (var project in projects)
    {
        var inspection = new ProjectInspection
        {
            SafeToDelete = LegacyProjectCleanupRules.IsSafeToDelete(project),
        };

        var localRoot = await DiscoverLocalRootAsync(project.ProjectId);
        inspection.LocalRoot = localRoot;
        if (!string.IsNullOrWhiteSpace(localRoot))
        {
            var remoteUrl = await GitOutputAsync(localRoot, "config", "--get", "remote.origin.url");
            var defaultBranch = await GitOutputAsync(localRoot, "symbolic-ref", "refs/remotes/origin/HEAD");
            var parsed = ParseRemoteUrl(remoteUrl);
            if (parsed is not null)
            {
                inspection.Fingerprint = new RepositoryFingerprint
                {
                    RemoteUrl = remoteUrl,
                    Host = parsed.Value.Host,
                    Owner = parsed.Value.Owner,
                    RepoName = parsed.Value.RepoName,
                    DefaultBranch = defaultBranch?.Split('/').LastOrDefault(),
                };
                inspection.CanonicalSlug = LegacyProjectCleanupRules.CanonicalSlugFromRepoName(parsed.Value.RepoName);
            }
        }

        inspection.BindingDetails = await LoadBindingDetailsAsync(project.ProjectId);
        inspection.KnowledgeSample = await LoadSingleTextAsync(
            "SELECT category || ':' || key || '=' || value FROM knowledge_entries WHERE project_id = @project_id ORDER BY updated_at DESC LIMIT 1",
            project.ProjectId);
        inspection.BrainSample = await LoadSingleTextAsync(
            "SELECT key || '=' || value FROM brain_entries WHERE project_id = @project_id ORDER BY created_at DESC LIMIT 1",
            project.ProjectId);
        inspection.SessionSample = await LoadSingleTextAsync(
            "SELECT LEFT(state_json::text, 400) FROM session_state WHERE project_id = @project_id ORDER BY updated_at DESC LIMIT 1",
            project.ProjectId);

        inspections[project.ProjectId] = inspection;
    }

    return inspections;
}

static object BuildProjectInspection(ProjectRecord project, IReadOnlyDictionary<string, ProjectInspection> inspections)
{
    inspections.TryGetValue(project.ProjectId, out var inspection);
    return new
    {
        project.ProjectId,
        project.Slug,
        fingerprint = project.Fingerprint,
        metadata = new
        {
            total_file_count = project.ProjectMetadata?.Summary.TotalFileCount ?? 0,
            source_file_count = project.ProjectMetadata?.Summary.SourceFileCount ?? 0,
        },
        safe_to_delete = inspection?.SafeToDelete ?? LegacyProjectCleanupRules.IsSafeToDelete(project),
        local_root = inspection?.LocalRoot,
        binding = inspection?.BindingDetails,
        inferred_fingerprint = inspection?.Fingerprint,
        canonical_slug = inspection?.CanonicalSlug,
        knowledge_sample = inspection?.KnowledgeSample,
        brain_sample = inspection?.BrainSample,
        session_sample = inspection?.SessionSample,
    };
}

static async Task<string?> DiscoverLocalRootAsync(string projectId)
{
    var databaseUrl = Environment.GetEnvironmentVariable("DATABASE_URL");
    var connectionString = StoreFactory.NormalizePostgresConnectionString(databaseUrl!);
    await using var connection = new NpgsqlConnection(connectionString);
    await connection.OpenAsync();

    await using var command = new NpgsqlCommand(
        "SELECT local_root FROM checkout_bindings WHERE project_id = @project_id AND local_root IS NOT NULL ORDER BY last_sync DESC NULLS LAST LIMIT 1",
        connection);
    command.Parameters.AddWithValue("project_id", projectId);
    var value = await command.ExecuteScalarAsync();
    return value as string;
}

static async Task<object?> LoadBindingDetailsAsync(string projectId)
{
    var databaseUrl = Environment.GetEnvironmentVariable("DATABASE_URL");
    var connectionString = StoreFactory.NormalizePostgresConnectionString(databaseUrl!);
    await using var connection = new NpgsqlConnection(connectionString);
    await connection.OpenAsync();

    await using var command = new NpgsqlCommand(
        "SELECT local_root, branch, last_commit, client_label, last_sync FROM checkout_bindings WHERE project_id = @project_id ORDER BY last_sync DESC NULLS LAST LIMIT 1",
        connection);
    command.Parameters.AddWithValue("project_id", projectId);
    await using var reader = await command.ExecuteReaderAsync();
    if (!await reader.ReadAsync())
    {
        return null;
    }

    return new
    {
        local_root = reader.IsDBNull(0) ? null : reader.GetString(0),
        branch = reader.IsDBNull(1) ? null : reader.GetString(1),
        last_commit = reader.IsDBNull(2) ? null : reader.GetString(2),
        client_label = reader.IsDBNull(3) ? null : reader.GetString(3),
        last_sync = reader.IsDBNull(4) ? (DateTime?)null : reader.GetDateTime(4),
    };
}

static async Task<string?> LoadSingleTextAsync(string sql, string projectId)
{
    var databaseUrl = Environment.GetEnvironmentVariable("DATABASE_URL");
    var connectionString = StoreFactory.NormalizePostgresConnectionString(databaseUrl!);
    await using var connection = new NpgsqlConnection(connectionString);
    await connection.OpenAsync();

    await using var command = new NpgsqlCommand(sql, connection);
    command.Parameters.AddWithValue("project_id", projectId);
    var value = await command.ExecuteScalarAsync();
    return value as string;
}

static async Task<ProjectRecord> FindOrCreateCanonicalProjectAsync(
    NpgsqlConnection connection,
    NpgsqlTransaction transaction,
    ProjectInspection inspection,
    ProjectRecord sourceProject)
{
    var fingerprint = inspection.Fingerprint!;

    var existing = await FindProjectByFingerprintAsync(connection, transaction, fingerprint);
    if (existing is not null)
    {
        return existing;
    }

    var now = DateTimeOffset.UtcNow;
    var project = new ProjectRecord
    {
        ProjectId = $"proj_{Guid.NewGuid():N}",
        Slug = inspection.CanonicalSlug!,
        Fingerprint = fingerprint,
        ProjectMetadata = sourceProject.ProjectMetadata,
        CreatedAt = now,
        UpdatedAt = now,
    };

    await using var command = new NpgsqlCommand(
        """
        INSERT INTO projects (project_id, slug, remote_url, host, owner, repo_name, default_branch, project_metadata_json, created_at, updated_at)
        VALUES (@project_id, @slug, @remote_url, @host, @owner, @repo_name, @default_branch, CAST(@project_metadata_json AS jsonb), @created_at, @updated_at)
        """,
        connection,
        transaction);

    command.Parameters.AddWithValue("project_id", project.ProjectId);
    command.Parameters.AddWithValue("slug", project.Slug);
    command.Parameters.AddWithValue("remote_url", (object?)fingerprint.RemoteUrl ?? DBNull.Value);
    command.Parameters.AddWithValue("host", (object?)fingerprint.Host ?? DBNull.Value);
    command.Parameters.AddWithValue("owner", (object?)fingerprint.Owner ?? DBNull.Value);
    command.Parameters.AddWithValue("repo_name", (object?)fingerprint.RepoName ?? DBNull.Value);
    command.Parameters.AddWithValue("default_branch", (object?)fingerprint.DefaultBranch ?? DBNull.Value);
    command.Parameters.AddWithValue("project_metadata_json", (object?)SerializeMetadata(project.ProjectMetadata) ?? DBNull.Value);
    command.Parameters.AddWithValue("created_at", project.CreatedAt);
    command.Parameters.AddWithValue("updated_at", project.UpdatedAt);
    await command.ExecuteNonQueryAsync();

    return project;
}

static async Task<ProjectRecord?> FindProjectByFingerprintAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, RepositoryFingerprint fingerprint)
{
    await using var command = new NpgsqlCommand(
        """
        SELECT project_id, slug, remote_url, host, owner, repo_name, default_branch, project_metadata_json::text, created_at, updated_at
        FROM projects
        WHERE remote_url = @remote_url
           OR (host = @host AND owner = @owner AND repo_name = @repo_name)
        ORDER BY created_at
        LIMIT 1
        """,
        connection,
        transaction);

    command.Parameters.AddWithValue("remote_url", (object?)fingerprint.RemoteUrl ?? DBNull.Value);
    command.Parameters.AddWithValue("host", (object?)fingerprint.Host ?? DBNull.Value);
    command.Parameters.AddWithValue("owner", (object?)fingerprint.Owner ?? DBNull.Value);
    command.Parameters.AddWithValue("repo_name", (object?)fingerprint.RepoName ?? DBNull.Value);

    await using var reader = await command.ExecuteReaderAsync();
    if (!await reader.ReadAsync())
    {
        return null;
    }

    var project = MapProject(reader);
    await reader.CloseAsync();
    return project;
}

static async Task<Dictionary<string, int>> ReassignProjectScopedDataAsync(
    NpgsqlConnection connection,
    NpgsqlTransaction transaction,
    string fromProjectId,
    string toProjectId)
{
    var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
    {
        ["knowledge_entries"] = await MoveUniqueTriplesAsync(connection, transaction, fromProjectId, toProjectId),
        ["brain_entries"] = await MoveUniqueKeysAsync(connection, transaction, fromProjectId, toProjectId),
        ["session_state"] = await MoveSessionStateAsync(connection, transaction, fromProjectId, toProjectId),
        ["telemetry_events"] = await ReassignProjectIdAsync(connection, transaction, "telemetry_events", fromProjectId, toProjectId),
        ["project_files"] = await MoveProjectFilesAsync(connection, transaction, fromProjectId, toProjectId),
        ["project_symbols"] = await MoveProjectSymbolsAsync(connection, transaction, fromProjectId, toProjectId),
        ["project_call_edges"] = await MoveProjectCallEdgesAsync(connection, transaction, fromProjectId, toProjectId),
        ["checkout_bindings"] = await MoveCheckoutBindingsAsync(connection, transaction, fromProjectId, toProjectId),
    };

    return result;
}

static async Task<int> MoveUniqueTriplesAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, string fromProjectId, string toProjectId)
{
    await using var insert = new NpgsqlCommand(
        """
        INSERT INTO knowledge_entries (project_id, category, key, value, confidence, updated_at)
        SELECT @to_project_id, category, key, value, confidence, updated_at
        FROM knowledge_entries
        WHERE project_id = @from_project_id
        ON CONFLICT (project_id, category, key) DO UPDATE SET
            value = EXCLUDED.value,
            confidence = GREATEST(knowledge_entries.confidence, EXCLUDED.confidence),
            updated_at = GREATEST(knowledge_entries.updated_at, EXCLUDED.updated_at)
        """,
        connection,
        transaction);
    insert.Parameters.AddWithValue("from_project_id", fromProjectId);
    insert.Parameters.AddWithValue("to_project_id", toProjectId);
    var moved = await insert.ExecuteNonQueryAsync();
    await DeleteByProjectIdAsync(connection, transaction, "knowledge_entries", fromProjectId);
    return moved;
}

static async Task<int> MoveUniqueKeysAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, string fromProjectId, string toProjectId)
{
    await using var insert = new NpgsqlCommand(
        """
        INSERT INTO brain_entries (project_id, key, value, created_at)
        SELECT @to_project_id, key, value, created_at
        FROM brain_entries
        WHERE project_id = @from_project_id
        ON CONFLICT (project_id, key) DO UPDATE SET
            value = EXCLUDED.value,
            created_at = LEAST(brain_entries.created_at, EXCLUDED.created_at)
        """,
        connection,
        transaction);
    insert.Parameters.AddWithValue("from_project_id", fromProjectId);
    insert.Parameters.AddWithValue("to_project_id", toProjectId);
    var moved = await insert.ExecuteNonQueryAsync();
    await DeleteByProjectIdAsync(connection, transaction, "brain_entries", fromProjectId);
    return moved;
}

static async Task<int> MoveSessionStateAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, string fromProjectId, string toProjectId)
{
    await using var insert = new NpgsqlCommand(
        """
        INSERT INTO session_state (project_id, session_id, state_json, created_at, updated_at)
        SELECT @to_project_id, session_id, state_json, created_at, updated_at
        FROM session_state
        WHERE project_id = @from_project_id
        ON CONFLICT (project_id, session_id) DO UPDATE SET
            state_json = EXCLUDED.state_json,
            updated_at = GREATEST(session_state.updated_at, EXCLUDED.updated_at)
        """,
        connection,
        transaction);
    insert.Parameters.AddWithValue("from_project_id", fromProjectId);
    insert.Parameters.AddWithValue("to_project_id", toProjectId);
    var moved = await insert.ExecuteNonQueryAsync();
    await DeleteByProjectIdAsync(connection, transaction, "session_state", fromProjectId);
    return moved;
}

static async Task<int> MoveProjectFilesAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, string fromProjectId, string toProjectId)
{
    await using var insert = new NpgsqlCommand(
        """
        INSERT INTO project_files (project_id, path, hash, language, line_count, token_count, exports_json, summary, indexed_at)
        SELECT @to_project_id, path, hash, language, line_count, token_count, exports_json, summary, indexed_at
        FROM project_files
        WHERE project_id = @from_project_id
        ON CONFLICT (project_id, path) DO UPDATE SET
            hash = EXCLUDED.hash,
            language = EXCLUDED.language,
            line_count = EXCLUDED.line_count,
            token_count = EXCLUDED.token_count,
            exports_json = EXCLUDED.exports_json,
            summary = EXCLUDED.summary,
            indexed_at = GREATEST(project_files.indexed_at, EXCLUDED.indexed_at)
        """,
        connection,
        transaction);
    insert.Parameters.AddWithValue("from_project_id", fromProjectId);
    insert.Parameters.AddWithValue("to_project_id", toProjectId);
    var moved = await insert.ExecuteNonQueryAsync();
    await DeleteByProjectIdAsync(connection, transaction, "project_files", fromProjectId);
    return moved;
}

static async Task<int> MoveProjectSymbolsAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, string fromProjectId, string toProjectId)
{
    await using var insert = new NpgsqlCommand(
        """
        INSERT INTO project_symbols (project_id, file_path, name, kind, start_line, end_line, is_exported, indexed_at)
        SELECT @to_project_id, file_path, name, kind, start_line, end_line, is_exported, indexed_at
        FROM project_symbols
        WHERE project_id = @from_project_id
        ON CONFLICT (project_id, file_path, name) DO UPDATE SET
            kind = EXCLUDED.kind,
            start_line = EXCLUDED.start_line,
            end_line = EXCLUDED.end_line,
            is_exported = EXCLUDED.is_exported,
            indexed_at = GREATEST(project_symbols.indexed_at, EXCLUDED.indexed_at)
        """,
        connection,
        transaction);
    insert.Parameters.AddWithValue("from_project_id", fromProjectId);
    insert.Parameters.AddWithValue("to_project_id", toProjectId);
    var moved = await insert.ExecuteNonQueryAsync();
    await DeleteByProjectIdAsync(connection, transaction, "project_symbols", fromProjectId);
    return moved;
}

static async Task<int> MoveProjectCallEdgesAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, string fromProjectId, string toProjectId)
{
    await using var insert = new NpgsqlCommand(
        """
        INSERT INTO project_call_edges (project_id, from_symbol, to_symbol, kind, indexed_at)
        SELECT @to_project_id, from_symbol, to_symbol, kind, indexed_at
        FROM project_call_edges
        WHERE project_id = @from_project_id
        ON CONFLICT (project_id, from_symbol, to_symbol) DO UPDATE SET
            kind = EXCLUDED.kind,
            indexed_at = GREATEST(project_call_edges.indexed_at, EXCLUDED.indexed_at)
        """,
        connection,
        transaction);
    insert.Parameters.AddWithValue("from_project_id", fromProjectId);
    insert.Parameters.AddWithValue("to_project_id", toProjectId);
    var moved = await insert.ExecuteNonQueryAsync();
    await DeleteByProjectIdAsync(connection, transaction, "project_call_edges", fromProjectId);
    return moved;
}

static async Task<int> MoveCheckoutBindingsAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, string fromProjectId, string toProjectId)
{
    await using var insert = new NpgsqlCommand(
        """
        INSERT INTO checkout_bindings (project_id, local_root, branch, last_commit, client_label, last_sync)
        SELECT @to_project_id, local_root, branch, last_commit, client_label, last_sync
        FROM checkout_bindings
        WHERE project_id = @from_project_id
        ON CONFLICT (project_id, local_root) DO UPDATE SET
            branch = EXCLUDED.branch,
            last_commit = EXCLUDED.last_commit,
            client_label = EXCLUDED.client_label,
            last_sync = GREATEST(checkout_bindings.last_sync, EXCLUDED.last_sync)
        """,
        connection,
        transaction);
    insert.Parameters.AddWithValue("from_project_id", fromProjectId);
    insert.Parameters.AddWithValue("to_project_id", toProjectId);
    var moved = await insert.ExecuteNonQueryAsync();
    await DeleteByProjectIdAsync(connection, transaction, "checkout_bindings", fromProjectId);
    return moved;
}

static async Task<int> ReassignProjectIdAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, string tableName, string fromProjectId, string toProjectId)
{
    await using var command = new NpgsqlCommand($"UPDATE {tableName} SET project_id = @to_project_id WHERE project_id = @from_project_id", connection, transaction);
    command.Parameters.AddWithValue("from_project_id", fromProjectId);
    command.Parameters.AddWithValue("to_project_id", toProjectId);
    return await command.ExecuteNonQueryAsync();
}

static async Task<Dictionary<string, int>> DeleteProjectEverywhereAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, string projectId)
{
    return new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
    {
        ["brain_entries"] = await DeleteByProjectIdAsync(connection, transaction, "brain_entries", projectId),
        ["knowledge_entries"] = await DeleteByProjectIdAsync(connection, transaction, "knowledge_entries", projectId),
        ["session_state"] = await DeleteByProjectIdAsync(connection, transaction, "session_state", projectId),
        ["telemetry_events"] = await DeleteByProjectIdAsync(connection, transaction, "telemetry_events", projectId),
        ["project_files"] = await DeleteByProjectIdAsync(connection, transaction, "project_files", projectId),
        ["project_symbols"] = await DeleteByProjectIdAsync(connection, transaction, "project_symbols", projectId),
        ["project_call_edges"] = await DeleteByProjectIdAsync(connection, transaction, "project_call_edges", projectId),
        ["checkout_bindings"] = await DeleteByProjectIdAsync(connection, transaction, "checkout_bindings", projectId),
        ["projects"] = await DeleteByProjectIdAsync(connection, transaction, "projects", projectId),
    };
}

static async Task<Dictionary<string, Dictionary<string, int>>> LoadTableCountsAsync(NpgsqlConnection connection, string[] projectIds)
{
    var result = new Dictionary<string, Dictionary<string, int>>(StringComparer.OrdinalIgnoreCase);
    foreach (var projectId in projectIds.Distinct(StringComparer.OrdinalIgnoreCase))
    {
        result[projectId] = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["projects"] = await CountByProjectIdAsync(connection, "projects", projectId),
            ["checkout_bindings"] = await CountByProjectIdAsync(connection, "checkout_bindings", projectId),
            ["brain_entries"] = await CountByProjectIdAsync(connection, "brain_entries", projectId),
            ["knowledge_entries"] = await CountByProjectIdAsync(connection, "knowledge_entries", projectId),
            ["session_state"] = await CountByProjectIdAsync(connection, "session_state", projectId),
            ["telemetry_events"] = await CountByProjectIdAsync(connection, "telemetry_events", projectId),
            ["project_files"] = await CountByProjectIdAsync(connection, "project_files", projectId),
            ["project_symbols"] = await CountByProjectIdAsync(connection, "project_symbols", projectId),
            ["project_call_edges"] = await CountByProjectIdAsync(connection, "project_call_edges", projectId),
        };
    }

    return result;
}

static async Task<int> CountByProjectIdAsync(NpgsqlConnection connection, string tableName, string projectId)
{
    await using var command = new NpgsqlCommand($"SELECT COUNT(*) FROM {tableName} WHERE project_id = @project_id", connection);
    command.Parameters.AddWithValue("project_id", projectId);
    return Convert.ToInt32(await command.ExecuteScalarAsync());
}

static async Task<int> DeleteByProjectIdAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, string tableName, string projectId)
{
    await using var command = new NpgsqlCommand($"DELETE FROM {tableName} WHERE project_id = @project_id", connection, transaction);
    command.Parameters.AddWithValue("project_id", projectId);
    return await command.ExecuteNonQueryAsync();
}

static ProjectRecord MapProject(NpgsqlDataReader reader)
{
    return new ProjectRecord
    {
        ProjectId = reader.GetString(0),
        Slug = reader.GetString(1),
        Fingerprint = new RepositoryFingerprint
        {
            RemoteUrl = reader.IsDBNull(2) ? null : reader.GetString(2),
            Host = reader.IsDBNull(3) ? null : reader.GetString(3),
            Owner = reader.IsDBNull(4) ? null : reader.GetString(4),
            RepoName = reader.IsDBNull(5) ? null : reader.GetString(5),
            DefaultBranch = reader.IsDBNull(6) ? null : reader.GetString(6),
        },
        ProjectMetadata = DeserializeMetadata(reader.IsDBNull(7) ? null : reader.GetString(7)),
        CreatedAt = reader.GetFieldValue<DateTimeOffset>(8),
        UpdatedAt = reader.GetFieldValue<DateTimeOffset>(9),
    };
}

static ProjectMetadataEnvelope? DeserializeMetadata(string? value)
{
    return string.IsNullOrWhiteSpace(value) ? null : JsonSerializer.Deserialize<ProjectMetadataEnvelope>(value);
}

static string? SerializeMetadata(ProjectMetadataEnvelope? value)
{
    return value is null ? null : JsonSerializer.Serialize(value);
}

static async Task<string?> GitOutputAsync(string workingDirectory, params string[] args)
{
    if (!Directory.Exists(workingDirectory))
    {
        return null;
    }

    var startInfo = new ProcessStartInfo("git")
    {
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        CreateNoWindow = true,
        WorkingDirectory = workingDirectory,
    };

    foreach (var arg in args)
    {
        startInfo.ArgumentList.Add(arg);
    }

    using var process = Process.Start(startInfo);
    if (process is null)
    {
        return null;
    }

    var output = await process.StandardOutput.ReadToEndAsync();
    await process.WaitForExitAsync();
    if (process.ExitCode != 0)
    {
        return null;
    }

    var trimmed = output.Trim();
    return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
}

static (string Host, string Owner, string RepoName)? ParseRemoteUrl(string? remoteUrl)
{
    if (string.IsNullOrWhiteSpace(remoteUrl))
    {
        return null;
    }

    var trimmed = remoteUrl.Trim().TrimEnd('/');
    if (trimmed.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
    {
        trimmed = trimmed[..^4];
    }

    if (trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
    {
        return ParseHostPath(trimmed[8..]);
    }

    if (trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
    {
        return ParseHostPath(trimmed[7..]);
    }

    if (trimmed.StartsWith("ssh://git@", StringComparison.OrdinalIgnoreCase))
    {
        return ParseHostPath(trimmed[10..]);
    }

    if (trimmed.StartsWith("git@", StringComparison.OrdinalIgnoreCase))
    {
        var remainder = trimmed[4..];
        var split = remainder.Split(':', 2);
        if (split.Length != 2)
        {
            return null;
        }

        return ParsePathSegments(split[0], split[1]);
    }

    return null;
}

static (string Host, string Owner, string RepoName)? ParseHostPath(string value)
{
    var split = value.Split('/', 2);
    if (split.Length != 2)
    {
        return null;
    }

    return ParsePathSegments(split[0], split[1]);
}

static (string Host, string Owner, string RepoName)? ParsePathSegments(string host, string path)
{
    var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
    if (segments.Length < 2)
    {
        return null;
    }

    return (host, segments[0], segments[1]);
}

static void WriteJson(object value)
{
    Console.WriteLine(JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }));
}

file sealed class ProjectInspection
{
    public bool SafeToDelete { get; set; }
    public string? LocalRoot { get; set; }
    public object? BindingDetails { get; set; }
    public RepositoryFingerprint? Fingerprint { get; set; }
    public string? CanonicalSlug { get; set; }
    public string? KnowledgeSample { get; set; }
    public string? BrainSample { get; set; }
    public string? SessionSample { get; set; }
}
