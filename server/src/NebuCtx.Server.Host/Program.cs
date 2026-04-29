using NebuCtx.Application;
using NebuCtx.Application.Services;
using NebuCtx.Contracts.Configuration;
using NebuCtx.Contracts.Mcp;
using NebuCtx.Dashboard;
using NebuCtx.Hosting.Auth;
using NebuCtx.Hosting.Configuration;
using NebuCtx.Hosting.Middleware;
using NebuCtx.Hosting.Validation;
using NebuCtx.Projects;
using NebuCtx.Server.Host;
using NebuCtx.Server.Host.Projects;
using NebuCtx.Storage;
using NebuCtx.Storage.Postgres;
using NebuCtx.Tools;

// --- Configuration ---
var serverOptions = EnvironmentBinder.BindFromEnvironment(new ServerOptions());

// Validate startup configuration
var validationErrors = StartupValidator.Validate(serverOptions);
if (validationErrors.Count > 0)
{
    foreach (var error in validationErrors)
    {
        Console.Error.WriteLine($"Configuration error: {error}");
    }
    return 1;
}

// --- Host setup ---
var builder = WebApplication.CreateBuilder(args);

// Kestrel: bind MCP port (4242) and dashboard port (3333)
builder.WebHost.ConfigureKestrel(kestrel =>
{
    kestrel.Listen(System.Net.IPAddress.Parse(serverOptions.McpHost), serverOptions.McpPort);
    if (serverOptions.DashboardPort != serverOptions.McpPort)
    {
        kestrel.Listen(System.Net.IPAddress.Parse(serverOptions.DashboardHost), serverOptions.DashboardPort);
    }
});

// Request body size limit
builder.WebHost.ConfigureKestrel(kestrel =>
{
    kestrel.Limits.MaxRequestBodySize = serverOptions.MaxBodyBytes;
});

// --- DI registration ---
builder.Services.AddSingleton(serverOptions);

// Storage
builder.Services.AddSingleton<IProjectStore>(sp => StoreFactory.CreateProjectStore(sp.GetRequiredService<ServerOptions>()));
builder.Services.AddSingleton<ICheckoutBindingStore>(sp => StoreFactory.CreateCheckoutBindingStore(sp.GetRequiredService<ServerOptions>()));
builder.Services.AddSingleton<IBrainStore>(sp => StoreFactory.CreateBrainStore(sp.GetRequiredService<ServerOptions>()));
builder.Services.AddSingleton<IKnowledgeStore>(sp => StoreFactory.CreateKnowledgeStore(sp.GetRequiredService<ServerOptions>()));
builder.Services.AddSingleton<ISessionStore>(sp => StoreFactory.CreateSessionStore(sp.GetRequiredService<ServerOptions>()));
builder.Services.AddSingleton<ICodeIndexStore>(sp => StoreFactory.CreateCodeIndexStore(sp.GetRequiredService<ServerOptions>()));

// Projects
builder.Services.AddSingleton<ProjectRegistry>();

// Application services
builder.Services.AddSingleton<BrainService>();
builder.Services.AddSingleton<KnowledgeService>();
builder.Services.AddSingleton<SessionService>();
builder.Services.AddSingleton<TelemetryStore>();
builder.Services.AddSingleton<PostgresTelemetryStore>(sp =>
    StoreFactory.CreateTelemetryStore(sp.GetRequiredService<ServerOptions>()));
builder.Services.AddHostedService<TelemetryHydrationService>();
builder.Services.AddSingleton<ToolRegistry>();

// Tool handlers
builder.Services.AddToolHandlers();

var app = builder.Build();

// --- Schema initialization ---
await StoreFactory.InitializeSchemaAsync(serverOptions);

// --- Middleware pipeline ---
// Rate limiting
app.UseMiddleware<RateLimitMiddleware>();

// Concurrency limiting
app.UseMiddleware<ConcurrencyLimitMiddleware>();

// Request timeout
app.UseMiddleware<RequestTimeoutMiddleware>();

// Bearer auth (health is auth-exempt)
app.UseMiddleware<BearerAuthMiddleware>();

// --- MCP HTTP endpoints ---
var toolRegistry = app.Services.GetRequiredService<ToolRegistry>();
var projectRegistry = app.Services.GetRequiredService<ProjectRegistry>();

// Project identity endpoints used by the client binding flow.
app.MapProjectApi();

// GET /v1/manifest — full tool manifest
app.MapGet("/v1/manifest", () => Results.Ok(toolRegistry.GetManifest()));

// GET /v1/tools — paginated tool list
app.MapGet("/v1/tools", (int? offset, int? limit) =>
    Results.Ok(toolRegistry.GetTools(offset ?? 0, limit ?? 200)));

// POST /v1/tools/call — execute a tool
app.MapPost("/v1/tools/call", async (ToolCallRequest request, CancellationToken cancellationToken) =>
{
    try
    {
        var context = await ProjectApiEndpoints.ResolveToolExecutionContextAsync(request, projectRegistry, cancellationToken);
        var result = await toolRegistry.ExecuteToolAsync(request.Name, request.Arguments, context, cancellationToken);
        return Results.Ok(new ToolCallResponse { Result = result });
    }
    catch (KeyNotFoundException ex)
    {
        return Results.BadRequest(new ToolCallErrorResponse { Error = ex.Message });
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new ToolCallErrorResponse { Error = ex.Message });
    }
    catch (InvalidOperationException ex)
    {
        return Results.Conflict(new ToolCallErrorResponse { Error = ex.Message });
    }
});

// POST /v1/telemetry/ingest — accept a single tool-call event from the Rust client.
// Only token counts and metadata are accepted; no raw content is stored.
app.MapPost("/v1/telemetry/ingest", async (TelemetryIngestRequest request, TelemetryStore telemetryStore, CancellationToken cancellationToken) =>
{
    var projectId = string.Empty;
    if (request.RepositoryFingerprint is not null)
    {
        var project = await projectRegistry.ResolveOrCreateAsync(
            request.RepositoryFingerprint,
            request.ProjectSlug ?? "unknown",
            cancellationToken: cancellationToken);
        projectId = project?.ProjectId ?? string.Empty;
    }

    telemetryStore.IngestEvent(request, projectId);
    return Results.Ok(new { ingested = true });
});

// POST /v1/index/sync — receive the full project source-code index from the Rust client.
// Stores file metadata, symbols, and call edges in Postgres so the dashboard can display
// real project-level data instead of the server's internal tool/route registry.
app.MapPost("/v1/index/sync", async (IndexSyncRequest request, ICodeIndexStore codeIndexStore, CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(request.ProjectId))
        return Results.BadRequest(new { error = "project_id is required" });

    var files = (request.Files ?? []).Select(f => new IndexedFile
    {
        Path = f.Path,
        Hash = f.Hash ?? "",
        Language = f.Language ?? "",
        LineCount = f.LineCount,
        TokenCount = f.TokenCount,
        Exports = f.Exports ?? [],
        Summary = f.Summary ?? "",
    }).ToList();

    var symbols = (request.Symbols ?? []).Select(s => new IndexedSymbol
    {
        FilePath = s.FilePath,
        Name = s.Name,
        Kind = s.Kind ?? "",
        StartLine = s.StartLine,
        EndLine = s.EndLine,
        IsExported = s.IsExported,
    }).ToList();

    var edges = (request.Edges ?? []).Select(e => new IndexedCallEdge
    {
        FromSymbol = e.FromSymbol,
        ToSymbol = e.ToSymbol,
        Kind = e.Kind ?? "call",
    }).ToList();

    await codeIndexStore.SyncIndexAsync(request.ProjectId, files, symbols, edges, cancellationToken);

    return Results.Ok(new
    {
        synced = true,
        project_id = request.ProjectId,
        files = files.Count,
        symbols = symbols.Count,
        edges = edges.Count,
    });
});

// --- Dashboard endpoints ---
app.MapDashboardApi();

// --- Startup log ---
var logger = app.Services.GetRequiredService<ILogger<Program>>();
logger.LogInformation("nebu-ctx server v{Version} starting", ServerVersion.Current);
logger.LogInformation("MCP HTTP on {Host}:{Port}", serverOptions.McpHost, serverOptions.McpPort);
logger.LogInformation("Dashboard on {Host}:{Port}", serverOptions.DashboardHost, serverOptions.DashboardPort);
logger.LogInformation("Store: {Store}", serverOptions.Store);

if (!string.IsNullOrEmpty(serverOptions.AuthToken))
{
    logger.LogInformation("Auth: Bearer token required");
}
else
{
    logger.LogWarning("Auth: No token configured (loopback only)");
}

await app.RunAsync();
return 0;
