namespace NebuCtx.Tools;

using Microsoft.Extensions.DependencyInjection;
using NebuCtx.Server.Core;
using NebuCtx.Tools.Brain;
using NebuCtx.Tools.Cost;
using NebuCtx.Tools.Gain;
using NebuCtx.Tools.Heatmap;
using NebuCtx.Tools.Knowledge;
using NebuCtx.Tools.Routes;
using NebuCtx.Tools.Session;
using NebuCtx.Tools.Stats;

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
        services.AddSingleton<IToolHandler, BrainToolHandler>();
        services.AddSingleton<IToolHandler, CostToolHandler>();
        services.AddSingleton<IToolHandler, GainToolHandler>();
        services.AddSingleton<IToolHandler, HeatmapToolHandler>();
        services.AddSingleton<IToolHandler, KnowledgeToolHandler>();
        services.AddSingleton<IToolHandler, RoutesToolHandler>();
        services.AddSingleton<IToolHandler, SessionToolHandler>();
        services.AddSingleton<IToolHandler, StatsToolHandler>();

        return services;
    }
}
