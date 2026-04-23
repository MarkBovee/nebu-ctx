namespace NebuCtx.Storage.Sqlite;

using Microsoft.Data.Sqlite;
using NebuCtx.Contracts.Projects;

/// <summary>
/// SQLite implementation of <see cref="IWorkspaceBindingStore"/>.
/// </summary>
public sealed class SqliteWorkspaceBindingStore : IWorkspaceBindingStore
{
    private readonly string _connectionString;

    /// <summary>
    /// Initializes the SQLite workspace binding store.
    /// </summary>
    /// <param name="connectionString">SQLite connection string.</param>
    public SqliteWorkspaceBindingStore(string connectionString)
    {
        _connectionString = connectionString;
    }

    /// <inheritdoc />
    public async Task UpsertBindingAsync(WorkspaceBinding binding, CancellationToken cancellationToken = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            """
            INSERT INTO workspace_bindings (project_id, local_root, branch, last_commit, client_label, last_sync)
            VALUES (@project_id, @local_root, @branch, @last_commit, @client_label, @last_sync)
            ON CONFLICT (project_id, local_root) DO UPDATE SET
                branch = excluded.branch,
                last_commit = excluded.last_commit,
                client_label = excluded.client_label,
                last_sync = excluded.last_sync
            """;

        cmd.Parameters.AddWithValue("@project_id", binding.ProjectId);
        cmd.Parameters.AddWithValue("@local_root", (object?)binding.LocalRoot ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@branch", (object?)binding.Branch ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@last_commit", (object?)binding.LastCommit ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@client_label", (object?)binding.ClientLabel ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@last_sync", (object?)binding.LastSync?.ToString("O") ?? DBNull.Value);

        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<WorkspaceBinding>> GetBindingsAsync(string projectId, CancellationToken cancellationToken = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT project_id, local_root, branch, last_commit, client_label, last_sync FROM workspace_bindings WHERE project_id = @project_id ORDER BY last_sync DESC";
        cmd.Parameters.AddWithValue("@project_id", projectId);

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        var bindings = new List<WorkspaceBinding>();

        while (await reader.ReadAsync(cancellationToken))
        {
            bindings.Add(new WorkspaceBinding
            {
                ProjectId = reader.GetString(0),
                LocalRoot = reader.IsDBNull(1) ? null : reader.GetString(1),
                Branch = reader.IsDBNull(2) ? null : reader.GetString(2),
                LastCommit = reader.IsDBNull(3) ? null : reader.GetString(3),
                ClientLabel = reader.IsDBNull(4) ? null : reader.GetString(4),
                LastSync = reader.IsDBNull(5) ? null : DateTimeOffset.Parse(reader.GetString(5)),
            });
        }

        return bindings;
    }
}
