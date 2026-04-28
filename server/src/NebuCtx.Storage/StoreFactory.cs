namespace NebuCtx.Storage;

using Npgsql;
using NebuCtx.Contracts.Configuration;
using NebuCtx.Storage.Postgres;

/// <summary>
/// Factory for creating Postgres-backed store instances.
/// </summary>
public static class StoreFactory
{
    /// <summary>
    /// Creates an <see cref="IProjectStore"/> based on the server options.
    /// </summary>
    /// <param name="options">Server configuration containing store selection and connection details.</param>
    /// <returns>A Postgres-backed project store.</returns>
    public static IProjectStore CreateProjectStore(ServerOptions options)
    {
        return new PostgresProjectStore(BuildConfiguredPostgresConnectionString(options));
    }

    /// <summary>
    /// Creates an <see cref="ICheckoutBindingStore"/> backed by Postgres.
    /// </summary>
    /// <param name="options">Server configuration containing store selection and connection details.</param>
    /// <returns>A Postgres-backed checkout binding store.</returns>
    public static ICheckoutBindingStore CreateCheckoutBindingStore(ServerOptions options)
    {
        return new PostgresCheckoutBindingStore(BuildConfiguredPostgresConnectionString(options));
    }

    /// <summary>
    /// Creates an <see cref="IBrainStore"/> based on the server options.
    /// </summary>
    /// <param name="options">Server configuration containing store selection and connection details.</param>
    /// <returns>A Postgres-backed brain store.</returns>
    public static IBrainStore CreateBrainStore(ServerOptions options)
    {
        return new PostgresBrainStore(BuildConfiguredPostgresConnectionString(options));
    }

    /// <summary>
    /// Creates an <see cref="IKnowledgeStore"/> backed by Postgres.
    /// </summary>
    /// <param name="options">Server configuration containing store selection and connection details.</param>
    /// <returns>A Postgres-backed knowledge store.</returns>
    public static IKnowledgeStore CreateKnowledgeStore(ServerOptions options)
    {
        return new PostgresKnowledgeStore(BuildConfiguredPostgresConnectionString(options));
    }

    /// <summary>
    /// Creates an <see cref="ISessionStore"/> backed by Postgres.
    /// </summary>
    /// <param name="options">Server configuration containing store selection and connection details.</param>
    /// <returns>A Postgres-backed session store.</returns>
    public static ISessionStore CreateSessionStore(ServerOptions options)
    {
        return new PostgresSessionStore(BuildConfiguredPostgresConnectionString(options));
    }

    /// <summary>
    /// Creates a <see cref="PostgresTelemetryStore"/> for persisting and hydrating telemetry events.
    /// </summary>
    /// <param name="options">Server configuration containing store selection and connection details.</param>
    /// <returns>A Postgres-backed telemetry store.</returns>
    public static PostgresTelemetryStore CreateTelemetryStore(ServerOptions options)
    {
        return new PostgresTelemetryStore(BuildConfiguredPostgresConnectionString(options));
    }

    /// <summary>
    /// Runs additive schema initialization for the supported Postgres backend.
    /// </summary>
    /// <param name="options">Server configuration.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public static async Task InitializeSchemaAsync(ServerOptions options, CancellationToken cancellationToken = default)
    {
        await PostgresSchemaInitializer.EnsureSchemaAsync(BuildConfiguredPostgresConnectionString(options), cancellationToken);
    }

    /// <summary>
    /// Validates that Postgres is the active store and returns a normalized connection string.
    /// </summary>
    /// <param name="options">Server configuration.</param>
    /// <returns>Npgsql-compatible connection string.</returns>
    private static string BuildConfiguredPostgresConnectionString(ServerOptions options)
    {
        EnsurePostgresStoreConfigured(options);
        return BuildPostgresConnectionString(options);
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
    /// Ensures the configured store is the only supported Postgres backend.
    /// </summary>
    /// <param name="options">Server configuration.</param>
    private static void EnsurePostgresStoreConfigured(ServerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (!string.Equals(options.Store, "postgres", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Only the 'postgres' store is supported. Received '{options.Store}'.");
        }
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
