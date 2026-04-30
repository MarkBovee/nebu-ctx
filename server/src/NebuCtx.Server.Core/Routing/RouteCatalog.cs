namespace NebuCtx.Server.Core.Routing;

/// <summary>
/// Provides the known route descriptors exposed by the .NET host.
/// </summary>
public static class RouteCatalog
{
    private static readonly IReadOnlyList<RouteDescriptor> Routes =
    [
        new("GET", "/health", "Health", "NebuCtx.Server.Host/Dashboard/DashboardCoreEndpoints.cs", 22),
        new("GET", "/v1/manifest", "Manifest", "NebuCtx.Server.Host/Endpoints/McpEndpoints.cs", 23),
        new("GET", "/v1/tools", "Tools", "NebuCtx.Server.Host/Endpoints/McpEndpoints.cs", 25),
        new("POST", "/v1/tools/call", "CallTool", "NebuCtx.Server.Host/Endpoints/McpEndpoints.cs", 28),
        new("POST", "/v1/projects/resolve", "ResolveProject", "NebuCtx.Server.Host/Projects/ProjectApiEndpoints.cs", 24),
        new("GET", "/v1/projects", "ListProjects", "NebuCtx.Server.Host/Projects/ProjectApiEndpoints.cs", 25),
        new("GET", "/v1/projects/{projectId}/bindings", "GetBindings", "NebuCtx.Server.Host/Projects/ProjectApiEndpoints.cs", 26),
        new("POST", "/v1/projects/{projectId}/bindings", "BindWorkspace", "NebuCtx.Server.Host/Projects/ProjectApiEndpoints.cs", 27),
        new("GET", "/api/version", "DashboardVersion", "NebuCtx.Server.Host/Dashboard/DashboardCoreEndpoints.cs", 24),
        new("GET", "/api/stats", "DashboardStats", "NebuCtx.Server.Host/Dashboard/DashboardAnalyticsEndpoints.cs", 19),
        new("GET", "/api/search-index", "DashboardSearchIndex", "NebuCtx.Server.Host/Dashboard/DashboardDataEndpoints.cs", 157),
        new("GET", "/api/search", "DashboardSearch", "NebuCtx.Server.Host/Dashboard/DashboardDataEndpoints.cs", 187),
        new("GET", "/api/routes", "DashboardRoutes", "NebuCtx.Server.Host/Dashboard/DashboardDataEndpoints.cs", 154),
        new("GET", "/api/symbols", "DashboardSymbols", "NebuCtx.Server.Host/Dashboard/DashboardDataEndpoints.cs", 126),
    ];

    /// <summary>
    /// Returns all known routes.
    /// </summary>
    /// <returns>Known route descriptors.</returns>
    public static IReadOnlyList<RouteDescriptor> GetAll()
    {
        return Routes;
    }

    /// <summary>
    /// Returns known routes filtered by method and path substring.
    /// </summary>
    /// <param name="method">Optional HTTP method filter.</param>
    /// <param name="path">Optional case-insensitive path substring filter.</param>
    /// <returns>Filtered route descriptors.</returns>
    public static IReadOnlyList<RouteDescriptor> Search(string? method = null, string? path = null)
    {
        IEnumerable<RouteDescriptor> query = Routes;

        if (!string.IsNullOrWhiteSpace(method))
        {
            query = query.Where(route => route.Method.Equals(method.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(path))
        {
            query = query.Where(route => route.Path.Contains(path.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        return query.ToArray();
    }
}
