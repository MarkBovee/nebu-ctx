namespace NebuCtx.Hosting.Configuration;

using NebuCtx.Contracts.Configuration;

/// <summary>
/// Binds <see cref="ServerOptions"/> from environment variables,
/// preserving the current operator contract.
/// </summary>
public static class EnvironmentBinder
{
    /// <summary>
    /// Reads known environment variables and applies them to a <see cref="ServerOptions"/> instance.
    /// Env vars take precedence over any defaults already set on the options object.
    /// </summary>
    /// <param name="options">The options instance to populate.</param>
    /// <returns>The same options instance, mutated in-place for convenience.</returns>
    public static ServerOptions BindFromEnvironment(ServerOptions options)
    {
        ApplyString("NEBULA_CTX_HOST", value =>
        {
            options.McpHost = value;
            options.DashboardHost = value;
        });

        ApplyInt("NEBULA_CTX_HTTP_PORT", value => options.McpPort = value);
        ApplyInt("NEBULA_CTX_PORT", value => options.DashboardPort = value);

        ApplyString("NEBULA_CTX_HTTP_TOKEN", value => options.AuthToken = value);
        ApplyString("NEBULA_STORE", value => options.Store = value);
        ApplyString("DATABASE_URL", value => options.DatabaseUrl = value);

        // Token file path — try both naming conventions
        ApplyString("NEBU_CTX_TOKEN_FILE", value => options.TokenFilePath = value);
        ApplyString("NEBULA_CTX_TOKEN_FILE", value => options.TokenFilePath = value);

        ApplyBool("NEBULA_CTX_DASHBOARD_DISABLE_AUTH", value => options.DashboardDisableAuth = value);
        ApplyString("LOG_LEVEL", value => options.LogLevel = value);

        return options;
    }

    /// <summary>
    /// Applies an environment variable string value if present.
    /// </summary>
    private static void ApplyString(string key, Action<string> setter)
    {
        var value = Environment.GetEnvironmentVariable(key);
        if (!string.IsNullOrEmpty(value))
        {
            setter(value);
        }
    }

    /// <summary>
    /// Applies an environment variable as an integer if present and parseable.
    /// </summary>
    private static void ApplyInt(string key, Action<int> setter)
    {
        var value = Environment.GetEnvironmentVariable(key);
        if (!string.IsNullOrEmpty(value) && int.TryParse(value, out var parsed))
        {
            setter(parsed);
        }
    }

    /// <summary>
    /// Applies an environment variable as a boolean if present.
    /// Truthy values: "1", "true", "yes", "on" (case-insensitive).
    /// </summary>
    private static void ApplyBool(string key, Action<bool> setter)
    {
        var value = Environment.GetEnvironmentVariable(key);
        if (!string.IsNullOrEmpty(value))
        {
            setter(value is "1" or "true" or "yes" or "on");
        }
    }
}
