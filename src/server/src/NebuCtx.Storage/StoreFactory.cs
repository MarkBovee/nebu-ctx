namespace NebuCtx.Storage;

using Npgsql;
using NebuCtx.Contracts.Configuration;
using NebuCtx.Storage.Postgres;
using NebuCtx.Storage.Sqlite;

/// <summary>
/// Factory for creating store instances based on the configured backend (SQLite or Postgres).
/// </summary>
public static class StoreFactory
{
    /// <summary>
    /// Creates an <see cref="IProjectStore"/> based on the server options.
    /// </summary>
    /// <param name="options">Server configuration containing store selection and connection details.</param>
    /// <returns>A project store implementation for the configured backend.</returns>
    public static IProjectStore CreateProjectStore(ServerOptions options)
    {
        return options.Store.ToLowerInvariant() switch
        {
            "postgres" => new PostgresProjectStore(BuildPostgresConnectionString(options)),
            _ => new SqliteProjectStore(BuildSqliteConnectionString(options)),
        };
    }

    /// <summary>
    /// Creates an <see cref="IWorkspaceBindingStore"/> based on the server options.
    /// </summary>
    /// <param name="options">Server configuration containing store selection and connection details.</param>
    /// <returns>A workspace binding store implementation for the configured backend.</returns>
    public static IWorkspaceBindingStore CreateWorkspaceBindingStore(ServerOptions options)
    {
        return options.Store.ToLowerInvariant() switch
        {
            "postgres" => new PostgresWorkspaceBindingStore(BuildPostgresConnectionString(options)),
            _ => new Sqlite.SqliteWorkspaceBindingStore(BuildSqliteConnectionString(options)),
        };
    }

    /// <summary>
    /// Creates an <see cref="IBrainStore"/> based on the server options.
    /// </summary>
    /// <param name="options">Server configuration containing store selection and connection details.</param>
    /// <returns>A brain store implementation for the configured backend.</returns>
    public static IBrainStore CreateBrainStore(ServerOptions options)
    {
        return options.Store.ToLowerInvariant() switch
        {
            "postgres" => new PostgresBrainStore(BuildPostgresConnectionString(options)),
            _ => new Sqlite.SqliteBrainStore(BuildSqliteConnectionString(options)),
        };
    }

    /// <summary>
    /// Runs additive schema initialization for the configured backend.
    /// </summary>
    /// <param name="options">Server configuration.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public static async Task InitializeSchemaAsync(ServerOptions options, CancellationToken cancellationToken = default)
    {
        switch (options.Store.ToLowerInvariant())
        {
            case "postgres":
                await PostgresSchemaInitializer.EnsureSchemaAsync(BuildPostgresConnectionString(options), cancellationToken);
                break;
            default:
                await SqliteSchemaInitializer.EnsureSchemaAsync(BuildSqliteConnectionString(options), cancellationToken);
                break;
        }
    }

    /// <summary>
    /// Builds a SQLite connection string using the default data directory.
    /// </summary>
    private static string BuildSqliteConnectionString(ServerOptions options)
    {
        var dataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".nebu-ctx");
        Directory.CreateDirectory(dataDir);

        return $"Data Source={Path.Combine(dataDir, "nebu-ctx.db")}";
    }

    /// <summary>
    /// Normalizes a Postgres DATABASE_URL so Npgsql receives a standard connection string.
    /// </summary>
    /// <param name="options">Server options containing the configured database URL.</param>
    /// <returns>Npgsql-compatible connection string.</returns>
    public static string BuildPostgresConnectionString(ServerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return NormalizePostgresConnectionString(options.DatabaseUrl!);
    }

    /// <summary>
    /// Converts postgres:// and postgresql:// URIs into Npgsql key/value connection strings.
    /// Existing key/value connection strings are returned unchanged.
    /// </summary>
    /// <param name="configuredValue">Configured database value.</param>
    /// <returns>Npgsql-compatible connection string.</returns>
    public static string NormalizePostgresConnectionString(string configuredValue)
    {
        if (string.IsNullOrWhiteSpace(configuredValue))
        {
            throw new ArgumentException("Postgres connection string is required.", nameof(configuredValue));
        }

        if (!Uri.TryCreate(configuredValue, UriKind.Absolute, out var uri)
            || (uri.Scheme != "postgres" && uri.Scheme != "postgresql"))
        {
            return configuredValue;
        }

        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = uri.Host,
            Port = uri.IsDefaultPort ? 5432 : uri.Port,
            Database = uri.AbsolutePath.Trim('/'),
        };

        if (!string.IsNullOrWhiteSpace(uri.UserInfo))
        {
            var userInfo = uri.UserInfo.Split(':', 2);
            builder.Username = Uri.UnescapeDataString(userInfo[0]);
            if (userInfo.Length > 1)
            {
                builder.Password = Uri.UnescapeDataString(userInfo[1]);
            }
        }

        if (!string.IsNullOrWhiteSpace(uri.Query))
        {
            foreach (var part in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var keyValue = part.Split('=', 2);
                if (keyValue.Length != 2)
                {
                    continue;
                }

                var key = Uri.UnescapeDataString(keyValue[0]);
                var value = Uri.UnescapeDataString(keyValue[1]);
                builder[key] = value;
            }
        }

        return builder.ConnectionString;
    }
}
