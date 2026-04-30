namespace NebuCtx.Server.Host.Infrastructure;

using Microsoft.AspNetCore.Builder;
using NebuCtx.Server.Core.Auth;
using NebuCtx.Server.Core.Middleware;
using NebuCtx.Server.Host.Dashboard;
using NebuCtx.Server.Host.Endpoints;
using NebuCtx.Server.Host.Projects;

/// <summary>
/// Pipeline and endpoint mapping extensions for the nebu-ctx server host.
/// </summary>
public static class ApplicationBuilderExtensions
{
    /// <summary>
    /// Adds the nebu-ctx middleware pipeline: rate limiting, concurrency, timeout, bearer auth.
    /// </summary>
    public static IApplicationBuilder UseNebuCtxMiddleware(this IApplicationBuilder app)
    {
        app.UseMiddleware<RateLimitMiddleware>();
        app.UseMiddleware<ConcurrencyLimitMiddleware>();
        app.UseMiddleware<RequestTimeoutMiddleware>();
        app.UseMiddleware<BearerAuthMiddleware>();

        return app;
    }

    /// <summary>
    /// Maps all nebu-ctx endpoint groups: MCP, project API, and dashboard.
    /// </summary>
    public static WebApplication MapNebuCtxEndpoints(this WebApplication app)
    {
        app.MapProjectApi();
        app.MapMcpApi();
        app.MapDashboardApi();

        return app;
    }
}
