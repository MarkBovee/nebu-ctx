namespace NebuCtx.Contracts.Configuration;

/// <summary>
/// Server configuration mirroring the current env-var contract.
/// Configuration sources: env vars first, optional config files second.
/// </summary>
public sealed class ServerOptions
{
    /// <summary>
    /// MCP HTTP server bind host. Env: NEBULA_CTX_HOST. Default: 127.0.0.1.
    /// </summary>
    public string McpHost { get; set; } = "127.0.0.1";

    /// <summary>
    /// MCP HTTP server port. Env: NEBULA_CTX_HTTP_PORT. Default: 4242.
    /// </summary>
    public int McpPort { get; set; } = 4242;

    /// <summary>
    /// Dashboard bind host. Default: 127.0.0.1.
    /// </summary>
    public string DashboardHost { get; set; } = "127.0.0.1";

    /// <summary>
    /// Dashboard port. Default: 3333.
    /// </summary>
    public int DashboardPort { get; set; } = 3333;

    /// <summary>
    /// Bearer auth token for MCP endpoints. Env: NEBULA_CTX_HTTP_TOKEN.
    /// When null and binding to non-loopback, the server refuses to start.
    /// </summary>
    public string? AuthToken { get; set; }

    /// <summary>
    /// Store backend selector. Only "postgres" is supported. Env: NEBULA_STORE. Default: postgres.
    /// </summary>
    public string Store { get; set; } = "postgres";

    /// <summary>
    /// Postgres connection string. Env: DATABASE_URL.
    /// </summary>
    public string? DatabaseUrl { get; set; }

    /// <summary>
    /// Maximum request body size in bytes. Default: 2 MB.
    /// </summary>
    public int MaxBodyBytes { get; set; } = 2_097_152;

    /// <summary>
    /// Maximum concurrent requests. Default: 32.
    /// </summary>
    public int MaxConcurrency { get; set; } = 32;

    /// <summary>
    /// Rate limit: requests per second. Default: 50.
    /// </summary>
    public int MaxRequestsPerSecond { get; set; } = 50;

    /// <summary>
    /// Token bucket burst capacity. Default: 100.
    /// </summary>
    public int RateBurst { get; set; } = 100;

    /// <summary>
    /// Tool execution timeout in milliseconds. Default: 30000 (30s).
    /// </summary>
    public int RequestTimeoutMs { get; set; } = 30_000;

    /// <summary>
    /// Path to file containing the auth token. Env: NEBU_CTX_TOKEN_FILE / NEBULA_CTX_TOKEN_FILE.
    /// </summary>
    public string? TokenFilePath { get; set; }

    /// <summary>
    /// Disable dashboard auth. Env: NEBULA_CTX_DASHBOARD_DISABLE_AUTH.
    /// </summary>
    public bool DashboardDisableAuth { get; set; }

    /// <summary>
    /// Log level. Default: info.
    /// </summary>
    public string LogLevel { get; set; } = "info";
}
