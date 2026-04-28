namespace NebuCtx.Server.Host;

using NebuCtx.Application;
using NebuCtx.Storage.Postgres;

/// <summary>
/// Hosted service that hydrates the in-memory <see cref="TelemetryStore"/> from PostgreSQL on startup,
/// then wires up the persistence callback so all subsequent events are written to the database.
/// This ensures the dashboard shows consistent data across server restarts and across both
/// the local dev container and the HA addon (both share the same PostgreSQL instance).
/// </summary>
public sealed class TelemetryHydrationService : IHostedService
{
    private readonly TelemetryStore _telemetryStore;
    private readonly PostgresTelemetryStore _pgTelemetryStore;
    private readonly ILogger<TelemetryHydrationService> _logger;

    /// <summary>
    /// Initializes the telemetry hydration service.
    /// </summary>
    /// <param name="telemetryStore">The singleton in-memory telemetry store to hydrate.</param>
    /// <param name="pgTelemetryStore">The Postgres telemetry store used for load and persist operations.</param>
    /// <param name="logger">Logger instance.</param>
    public TelemetryHydrationService(TelemetryStore telemetryStore, PostgresTelemetryStore pgTelemetryStore, ILogger<TelemetryHydrationService> logger)
    {
        _telemetryStore = telemetryStore;
        _pgTelemetryStore = pgTelemetryStore;
        _logger = logger;
    }

    /// <summary>
    /// Loads persisted telemetry events from PostgreSQL into the in-memory store,
    /// then registers the persistence callback so future events are written to the database.
    /// Failures are logged as warnings so the server always starts even if the DB is unreachable.
    /// </summary>
    /// <param name="cancellationToken">Startup cancellation token.</param>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            var events = await _pgTelemetryStore.LoadAllEventsAsync(cancellationToken);

            // Hydrate first — before wiring the callback — so replayed events are never double-written.
            _telemetryStore.Hydrate(events);

            _telemetryStore.SetPersistCallback(evt =>
                _pgTelemetryStore.PersistEventAsync(evt, CancellationToken.None));

            _logger.LogInformation("Telemetry: hydrated {Count} events from PostgreSQL", events.Count);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Telemetry: failed to hydrate from PostgreSQL — starting with empty in-memory state");
        }
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
