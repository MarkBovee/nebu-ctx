using System.Net;
using NebuCtx.Contracts.Configuration;
using NebuCtx.Server.Core;
using NebuCtx.Server.Core.Configuration;
using NebuCtx.Server.Host.Infrastructure;
using Microsoft.Extensions.Options;

static void PrintServerBanner(string version)
{
    if (Console.IsOutputRedirected)
    {
        return;
    }

    const string cyan = "\x1b[36m";
    const string violet = "\x1b[38;5;141m";
    const string dim = "\x1b[2m";
    const string bold = "\x1b[1m";
    const string reset = "\x1b[0m";

    Console.WriteLine();
    Console.WriteLine($"  {cyan}███╗   ██╗███████╗██████╗ ██╗   ██╗{reset}  {violet}server{reset}");
    Console.WriteLine($"  {cyan}████╗  ██║██╔════╝██╔══██╗██║   ██║{reset}  {dim}dashboard + mcp host{reset}");
    Console.WriteLine($"  {cyan}██╔██╗ ██║█████╗  ██████╔╝██║   ██║{reset}  {bold}v{version}{reset}");
    Console.WriteLine($"  {cyan}██║╚██╗██║██╔══╝  ██╔══██╗██║   ██║{reset}");
    Console.WriteLine($"  {cyan}██║ ╚████║███████╗██████╔╝╚██████╔╝{reset}");
    Console.WriteLine($"  {cyan}╚═╝  ╚═══╝╚══════╝╚═════╝  ╚═════╝ {reset}");
    Console.WriteLine();
}

// --- Configuration ---
var startupOptions = EnvironmentBinder.CreateConfiguredOptions();

var validationErrors = NebuCtx.Server.Core.Validation.StartupValidator.Validate(startupOptions);
if (validationErrors.Count > 0)
{
    foreach (var error in validationErrors)
        Console.Error.WriteLine($"Configuration error: {error}");
    return 1;
}

// --- Host setup ---
var builder = WebApplication.CreateBuilder(args);

builder.WebHost.ConfigureKestrel(kestrel =>
{
    kestrel.Listen(IPAddress.Parse(startupOptions.McpHost), startupOptions.McpPort);
    if (startupOptions.DashboardPort != startupOptions.McpPort)
        kestrel.Listen(IPAddress.Parse(startupOptions.DashboardHost), startupOptions.DashboardPort);

    kestrel.Limits.MaxRequestBodySize = startupOptions.MaxBodyBytes;
});

// --- DI ---
builder.Services.AddNebuCtxServices(startupOptions);

var app = builder.Build();

var serverOptions = app.Services.GetRequiredService<IOptions<ServerOptions>>().Value;

// --- Pipeline + endpoints ---
app.UseNebuCtxMiddleware();
app.MapNebuCtxEndpoints();

// --- Startup log ---
var logger = app.Services.GetRequiredService<ILogger<Program>>();
PrintServerBanner(ServerVersion.Current);
logger.LogInformation("nebu-ctx server v{Version} starting", ServerVersion.Current);
logger.LogInformation("MCP HTTP on {Host}:{Port}", serverOptions.McpHost, serverOptions.McpPort);
logger.LogInformation("Dashboard on {Host}:{Port}", serverOptions.DashboardHost, serverOptions.DashboardPort);
logger.LogInformation("Store: {Store}", serverOptions.Store);

if (!string.IsNullOrEmpty(serverOptions.AuthToken))
    logger.LogInformation("Auth: Bearer token required");
else
    logger.LogWarning("Auth: No token configured (loopback only)");

await app.RunAsync();
return 0;
