namespace NebuCtx.IntegrationTests;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NebuCtx.Server.Host;
using NebuCtx.Storage;
using NebuCtx.Storage.Postgres;

/// <summary>
/// Custom <see cref="WebApplicationFactory{TProgram}"/> for integration tests.
/// Bypasses PostgreSQL by replacing all Postgres-backed stores with in-memory stubs
/// and skipping schema initialization via ASPNETCORE_ENVIRONMENT=Test.
/// </summary>
public sealed class NebuCtxTestFactory : WebApplicationFactory<Program>
{
    static NebuCtxTestFactory()
    {
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Test");
        Environment.SetEnvironmentVariable("NEBULA_STORE", "postgres");
        Environment.SetEnvironmentVariable("DATABASE_URL", "Host=localhost;Database=nebu_test;Username=test;Password=test");
    }

    /// <inheritdoc />
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Test");

        builder.ConfigureTestServices(services =>
        {
            Replace<IProjectStore>(services, new InMemoryProjectStore());
            Replace<ICheckoutBindingStore>(services, new InMemoryCheckoutBindingStore());
            Replace<IBrainStore>(services, new InMemoryBrainStore());
            Replace<IKnowledgeStore>(services, new InMemoryKnowledgeStore());
            Replace<ISessionStore>(services, new InMemorySessionStore());
            Replace<ICodeIndexStore>(services, new InMemoryCodeIndexStore());

            // Replace PostgresTelemetryStore with a disconnected instance.
            Replace<PostgresTelemetryStore>(services,
                new PostgresTelemetryStore("Host=localhost;Database=nebu_test;Username=test;Password=test"));

            // Remove TelemetryHydrationService — it would query Postgres on startup.
            var hydration = services.FirstOrDefault(d =>
                d.ServiceType == typeof(IHostedService) &&
                d.ImplementationType == typeof(TelemetryHydrationService));
            if (hydration is not null)
                services.Remove(hydration);
        });
    }

    private static void Replace<T>(IServiceCollection services, T instance) where T : class
    {
        var existing = services.Where(d => d.ServiceType == typeof(T)).ToList();
        foreach (var descriptor in existing)
            services.Remove(descriptor);
        services.AddSingleton(instance);
    }
}
