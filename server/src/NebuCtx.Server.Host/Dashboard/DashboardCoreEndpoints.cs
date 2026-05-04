namespace NebuCtx.Server.Host.Dashboard;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using NebuCtx.Contracts.Dashboard;

/// <summary>
/// Core dashboard endpoints: HTML shell, health, version, pulse, auth-token, favicon, and logo.
/// </summary>
public static class DashboardCoreEndpoints
{
    /// <summary>
    /// Maps core dashboard routes on the provided endpoint builder.
    /// </summary>
    public static IEndpointRouteBuilder MapDashboardCore(this IEndpointRouteBuilder app)
    {
        app.MapGet("/", () => Results.Content(DashboardHtmlProvider.LoadHtml(), "text/html"));
        app.MapGet("/index.html", () => Results.Content(DashboardHtmlProvider.LoadHtml(), "text/html"));
        app.MapGet("/dashboard", () => Results.Content(DashboardHtmlProvider.LoadHtml(), "text/html"));

        app.MapGet("/health", () => Results.Ok(new HealthResponse { Status = "ok" }));

        app.MapGet("/api/version", () => Results.Ok(DashboardPayloadFactory.BuildVersionPayload()));

        app.MapGet("/api/pulse", () => Results.Ok(new PulseResponse
        {
            Hash = null,
            Mtime = DateTimeOffset.UtcNow,
        }));

        app.MapGet("/api/auth-token", () =>
        {
            var tokenPath = Environment.GetEnvironmentVariable("NEBULA_CTX_TOKEN_FILE")
                ?? Environment.GetEnvironmentVariable("NEBU_CTX_TOKEN_FILE");

            string? tokenValue = null;
            if (!string.IsNullOrEmpty(tokenPath) && File.Exists(tokenPath))
                tokenValue = File.ReadAllText(tokenPath).Trim();

            return Results.Ok(new AuthTokenResponse { Token = tokenValue });
        });

        app.MapGet("/logo.png", () =>
        {
            var logoPath = DashboardHtmlProvider.ResolveLogoPath();
            return logoPath is not null
                ? Results.File(logoPath, "image/png")
                : Results.NotFound();
        });

        app.MapGet("/favicon.ico", () =>
        {
            var faviconPath = DashboardHtmlProvider.ResolveFaviconPath();
            return faviconPath is not null
                ? Results.File(faviconPath, "image/x-icon")
                : Results.NotFound();
        });

        return app;
    }
}
