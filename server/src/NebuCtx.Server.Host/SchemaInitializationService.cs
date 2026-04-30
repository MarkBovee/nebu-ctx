namespace NebuCtx.Server.Host;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NebuCtx.Contracts.Configuration;
using NebuCtx.Storage;

/// <summary>
/// Ensures the Postgres schema exists before the rest of the host starts using it.
/// </summary>
public sealed class SchemaInitializationService : IHostedService
{
    private readonly IHostEnvironment _environment;
    private readonly IOptions<ServerOptions> _options;
    private readonly ILogger<SchemaInitializationService> _logger;

    /// <summary>
    /// Initializes the schema bootstrapper.
    /// </summary>
    public SchemaInitializationService(IHostEnvironment environment, IOptions<ServerOptions> options, ILogger<SchemaInitializationService> logger)
    {
        _environment = environment;
        _options = options;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (_environment.IsEnvironment("Test"))
        {
            return;
        }

        _logger.LogInformation("Schema: ensuring PostgreSQL schema is initialized");
        await StoreFactory.InitializeSchemaAsync(_options.Value, cancellationToken);
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
