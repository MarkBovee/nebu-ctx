namespace NebuCtx.Storage.Postgres;

using Npgsql;
using NebuCtx.Contracts.Projects;

/// <summary>
/// Postgres implementation of <see cref="ICheckoutBindingStore"/>.
/// </summary>
public sealed class PostgresCheckoutBindingStore : ICheckoutBindingStore
{
    private readonly string _connectionString;

    /// <summary>
    /// Initializes the Postgres checkout binding store.
    /// </summary>
    /// <param name="connectionString">Postgres connection string.</param>
    public PostgresCheckoutBindingStore(string connectionString)
    {
        _connectionString = connectionString;
    }

    /// <inheritdoc />
    public async Task UpsertBindingAsync(CheckoutBinding binding, CancellationToken cancellationToken = default)
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);

        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO checkout_bindings (project_id, local_root, branch, last_commit, client_label, last_sync)
            VALUES (@project_id, @local_root, @branch, @last_commit, @client_label, @last_sync)
            ON CONFLICT (project_id, local_root) DO UPDATE SET
                branch = EXCLUDED.branch,
                last_commit = EXCLUDED.last_commit,
                client_label = EXCLUDED.client_label,
                last_sync = EXCLUDED.last_sync
            """,
            conn);

        cmd.Parameters.AddWithValue("project_id", binding.ProjectId);
        cmd.Parameters.AddWithValue("local_root", (object?)binding.LocalRoot ?? DBNull.Value);
        cmd.Parameters.AddWithValue("branch", (object?)binding.Branch ?? DBNull.Value);
        cmd.Parameters.AddWithValue("last_commit", (object?)binding.LastCommit ?? DBNull.Value);
        cmd.Parameters.AddWithValue("client_label", (object?)binding.ClientLabel ?? DBNull.Value);
        cmd.Parameters.AddWithValue("last_sync", (object?)binding.LastSync ?? DBNull.Value);

        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<CheckoutBinding>> GetBindingsAsync(string projectId, CancellationToken cancellationToken = default)
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);

        await using var cmd = new NpgsqlCommand(
            "SELECT project_id, local_root, branch, last_commit, client_label, last_sync FROM checkout_bindings WHERE project_id = @project_id ORDER BY last_sync DESC",
            conn);
        cmd.Parameters.AddWithValue("project_id", projectId);

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        var bindings = new List<CheckoutBinding>();

        while (await reader.ReadAsync(cancellationToken))
        {
            bindings.Add(new CheckoutBinding
            {
                ProjectId = reader.GetString(0),
                LocalRoot = reader.IsDBNull(1) ? null : reader.GetString(1),
                Branch = reader.IsDBNull(2) ? null : reader.GetString(2),
                LastCommit = reader.IsDBNull(3) ? null : reader.GetString(3),
                ClientLabel = reader.IsDBNull(4) ? null : reader.GetString(4),
                LastSync = reader.IsDBNull(5) ? null : reader.GetDateTime(5),
            });
        }

        return bindings;
    }

    /// <inheritdoc />
    public async Task<int> ClearProjectAsync(string projectId, CancellationToken cancellationToken = default)
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);

        await using var cmd = new NpgsqlCommand(
            "DELETE FROM checkout_bindings WHERE project_id = @project_id",
            conn);
        cmd.Parameters.AddWithValue("project_id", projectId);

        return await cmd.ExecuteNonQueryAsync(cancellationToken);
    }
}
