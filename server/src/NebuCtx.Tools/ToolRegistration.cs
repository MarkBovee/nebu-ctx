namespace NebuCtx.Tools;

using Microsoft.Extensions.DependencyInjection;
using NebuCtx.Application;
using NebuCtx.Tools.Brain;
using NebuCtx.Tools.Routes;

/// <summary>
/// Registers all tool handlers with the DI container.
/// </summary>
public static class ToolRegistration
{
    /// <summary>
    /// Adds all MCP tool handlers to the service collection.
    /// </summary>
    /// <param name="services">Service collection to register handlers in.</param>
    /// <returns>The same service collection for chaining.</returns>
    public static IServiceCollection AddToolHandlers(this IServiceCollection services)
    {
        // Brain tool — MVP critical path
        services.AddSingleton<IToolHandler, BrainToolHandler>();
        services.AddSingleton<IToolHandler, RoutesToolHandler>();

        return services;
    }
}
