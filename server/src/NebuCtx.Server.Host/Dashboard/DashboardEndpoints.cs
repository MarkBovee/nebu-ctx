namespace NebuCtx.Server.Host.Dashboard;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;

/// <summary>
/// Maps all dashboard API endpoints by delegating to focused endpoint modules.
/// </summary>
public static class DashboardEndpoints
{
    /// <summary>
    /// Maps all dashboard routes: core, analytics, and data endpoints.
    /// </summary>
    public static IEndpointRouteBuilder MapDashboardApi(this IEndpointRouteBuilder app)
    {
        app.MapDashboardCore();

        var api = app.MapGroup("/api");
        api.MapDashboardAnalytics();
        api.MapDashboardData();

        return app;
    }
}
