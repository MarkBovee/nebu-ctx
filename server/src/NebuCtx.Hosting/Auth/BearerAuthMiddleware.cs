namespace NebuCtx.Hosting.Auth;

using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using NebuCtx.Contracts.Configuration;

/// <summary>
/// Bearer token authentication middleware.
/// Validates Authorization header against the configured auth token.
/// Health endpoint is always public. Uses constant-time comparison to prevent timing attacks.
/// </summary>
public sealed class BearerAuthMiddleware
{
    private readonly RequestDelegate _next;
    private readonly byte[]? _expectedTokenBytes;
    private readonly bool _dashboardDisableAuth;
    private readonly int _dashboardPort;
    private readonly ILogger<BearerAuthMiddleware> _logger;

    /// <summary>
    /// Initializes the auth middleware from server options resolved via DI.
    /// </summary>
    /// <param name="next">Next middleware in the pipeline.</param>
    /// <param name="options">Server configuration options.</param>
    /// <param name="logger">Logger for auth events.</param>
    public BearerAuthMiddleware(RequestDelegate next, ServerOptions options, ILogger<BearerAuthMiddleware> logger)
    {
        _next = next;
        _expectedTokenBytes = options.AuthToken is not null
            ? Encoding.UTF8.GetBytes(options.AuthToken)
            : null;
        _dashboardDisableAuth = options.DashboardDisableAuth;
        _dashboardPort = options.DashboardPort;
        _logger = logger;
    }

    /// <summary>
    /// Validates the Bearer token on each request, except for auth-exempt paths.
    /// </summary>
    public async Task InvokeAsync(HttpContext context)
    {
        // Health is always public, and the add-on dashboard can opt out of auth on its own port.
        if (IsAuthExempt(context))
        {
            await _next(context);
            return;
        }

        // If no token configured, skip auth (only valid for loopback)
        if (_expectedTokenBytes is null)
        {
            await _next(context);
            return;
        }

        var token = ExtractBearerToken(context.Request);
        if (token is null)
        {
            _logger.LogWarning("Missing Authorization header on {Method} {Path}", context.Request.Method, context.Request.Path);
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new { error = "Missing or invalid Authorization header" });
            return;
        }

        var presentedBytes = Encoding.UTF8.GetBytes(token);
        if (!CryptographicOperations.FixedTimeEquals(presentedBytes, _expectedTokenBytes))
        {
            _logger.LogWarning("Invalid token on {Method} {Path}", context.Request.Method, context.Request.Path);
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new { error = "Invalid token" });
            return;
        }

        await _next(context);
    }

    /// <summary>
    /// Checks whether the given path is exempt from auth.
    /// </summary>
    private bool IsAuthExempt(HttpContext context)
    {
        return context.Request.Path.StartsWithSegments("/health", StringComparison.OrdinalIgnoreCase)
            || (_dashboardDisableAuth && context.Connection.LocalPort == _dashboardPort);
    }

    /// <summary>
    /// Extracts the Bearer token from the Authorization header.
    /// Supports "Bearer &lt;token&gt;" format (case-insensitive prefix).
    /// </summary>
    private static string? ExtractBearerToken(HttpRequest request)
    {
        var header = request.Headers.Authorization.ToString();
        if (string.IsNullOrEmpty(header))
        {
            return null;
        }

        // Also check query parameter for dashboard compatibility
        if (!header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return request.Query.TryGetValue("token", out var queryToken) ? queryToken.ToString() : null;
        }

        return header["Bearer ".Length..].Trim();
    }
}
