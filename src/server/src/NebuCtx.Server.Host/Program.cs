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
using NebuCtx.Server.Host.Projects;
using NebuCtx.Storage;
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
builder.Services.AddSingleton<IWorkspaceBindingStore>(sp => StoreFactory.CreateWorkspaceBindingStore(sp.GetRequiredService<ServerOptions>()));
builder.Services.AddSingleton<IBrainStore>(sp => StoreFactory.CreateBrainStore(sp.GetRequiredService<ServerOptions>()));

// Projects
builder.Services.AddSingleton<ProjectRegistry>();

// Application services
builder.Services.AddSingleton<BrainService>();
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
