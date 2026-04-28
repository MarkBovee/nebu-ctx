namespace NebuCtx.Hosting.Validation;

using System.Net;
using NebuCtx.Contracts.Configuration;

/// <summary>
/// Validates server configuration before startup.
/// Enforces security constraints like requiring auth on non-loopback bindings.
/// </summary>
public static class StartupValidator
{
    /// <summary>
    /// Validates the server options and returns a list of errors.
    /// An empty list means configuration is valid.
    /// </summary>
    /// <param name="options">Server options to validate.</param>
    /// <returns>List of validation error messages. Empty if valid.</returns>
    public static List<string> Validate(ServerOptions options)
    {
        var errors = new List<string>();

        if (!string.Equals(options.Store, "postgres", StringComparison.OrdinalIgnoreCase))
        {
            errors.Add($"NEBULA_STORE must be 'postgres'. '{options.Store}' is no longer supported.");
        }

        // Non-loopback binding requires auth token
        if (!IsLoopback(options.McpHost) && string.IsNullOrEmpty(options.AuthToken))
        {
            errors.Add($"Auth token is required when binding MCP to non-loopback address '{options.McpHost}'. Set NEBULA_CTX_HTTP_TOKEN.");
        }

        if (options.McpPort is < 1 or > 65535)
        {
            errors.Add($"MCP port {options.McpPort} is out of valid range (1-65535).");
        }

        if (options.DashboardPort is < 1 or > 65535)
        {
            errors.Add($"Dashboard port {options.DashboardPort} is out of valid range (1-65535).");
        }

        if (string.IsNullOrWhiteSpace(options.DatabaseUrl))
        {
            errors.Add("DATABASE_URL is required because Postgres is the only supported store.");
        }

        return errors;
    }

    /// <summary>
    /// Determines whether the given host string resolves to a loopback address.
    /// </summary>
    /// <param name="host">Host to check (e.g. "127.0.0.1", "localhost", "::1", "0.0.0.0").</param>
    /// <returns>True if the host is a loopback address.</returns>
    public static bool IsLoopback(string host)
    {
        if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (IPAddress.TryParse(host, out var address))
        {
            return IPAddress.IsLoopback(address);
        }

        return false;
    }
}
