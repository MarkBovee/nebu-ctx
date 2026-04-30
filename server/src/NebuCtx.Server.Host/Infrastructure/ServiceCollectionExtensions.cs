namespace NebuCtx.Server.Host.Infrastructure;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NebuCtx.Contracts.Configuration;
using NebuCtx.Server.Core;
using NebuCtx.Server.Core.Configuration;
using NebuCtx.Server.Core.Services;
using NebuCtx.Server.Core.Validation;
using NebuCtx.Storage;
using NebuCtx.Storage.Postgres;
using NebuCtx.Tools;

/// <summary>
/// DI registration extensions for the nebu-ctx server host.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers all nebu-ctx services: storage, projects, application, tools.
    /// </summary>
    public static IServiceCollection AddNebuCtxServices(this IServiceCollection services, ServerOptions configuredOptions)
    {
        services.AddNebuCtxOptions(configuredOptions);
        services.AddStorage();
        services.AddApplicationServices();
        services.AddToolHandlers();

        return services;
    }

    private static IServiceCollection AddNebuCtxOptions(this IServiceCollection services, ServerOptions configuredOptions)
    {
        services.AddSingleton<IValidateOptions<ServerOptions>, ServerOptionsValidator>();
        services.AddOptions<ServerOptions>()
            .Configure(options => EnvironmentBinder.CopyTo(configuredOptions, options))
            .ValidateOnStart();

        return services;
    }

    private static IServiceCollection AddStorage(this IServiceCollection services)
    {
        services.AddSingleton<IProjectStore>(sp => StoreFactory.CreateProjectStore(sp.GetRequiredService<IOptions<ServerOptions>>().Value));
        services.AddSingleton<ICheckoutBindingStore>(sp => StoreFactory.CreateCheckoutBindingStore(sp.GetRequiredService<IOptions<ServerOptions>>().Value));
        services.AddSingleton<IBrainStore>(sp => StoreFactory.CreateBrainStore(sp.GetRequiredService<IOptions<ServerOptions>>().Value));
        services.AddSingleton<IKnowledgeStore>(sp => StoreFactory.CreateKnowledgeStore(sp.GetRequiredService<IOptions<ServerOptions>>().Value));
        services.AddSingleton<ISessionStore>(sp => StoreFactory.CreateSessionStore(sp.GetRequiredService<IOptions<ServerOptions>>().Value));
        services.AddSingleton<ICodeIndexStore>(sp => StoreFactory.CreateCodeIndexStore(sp.GetRequiredService<IOptions<ServerOptions>>().Value));
        services.AddSingleton<PostgresTelemetryStore>(sp => StoreFactory.CreateTelemetryStore(sp.GetRequiredService<IOptions<ServerOptions>>().Value));

        return services;
    }

    private static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        services.AddSingleton<ProjectRegistry>();
        services.AddSingleton<BrainService>();
        services.AddSingleton<KnowledgeService>();
        services.AddSingleton<SessionService>();
        services.AddSingleton<TelemetryStore>();
        services.AddSingleton<ToolRegistry>();
        services.AddHostedService<SchemaInitializationService>();
        services.AddHostedService<TelemetryHydrationService>();

        return services;
    }
}
