namespace NebuCtx.ContractTests;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NebuCtx.Contracts.Configuration;
using NebuCtx.Server.Core.Auth;

/// <summary>
/// Tests dashboard-specific bearer auth behavior.
/// </summary>
public class BearerAuthMiddlewareTests
{
    /// <summary>
    /// Verifies that dashboard traffic can bypass auth when explicitly disabled for the dashboard port.
    /// </summary>
    [Fact]
    public async Task InvokeAsync_DashboardPortWithDisabledAuth_SkipsBearerValidation()
    {
        var nextCalled = false;
        var middleware = new BearerAuthMiddleware(
            async context =>
            {
                nextCalled = true;
                context.Response.StatusCode = StatusCodes.Status200OK;
                await Task.CompletedTask;
            },
            Options.Create(new ServerOptions
            {
                AuthToken = "secret-token",
                DashboardDisableAuth = true,
                DashboardPort = 3333,
            }),
            NullLogger<BearerAuthMiddleware>.Instance);

        var context = new DefaultHttpContext();
        context.Request.Path = "/";
        context.Connection.LocalPort = 3333;

        await middleware.InvokeAsync(context);

        Assert.True(nextCalled);
        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
    }

    /// <summary>
    /// Verifies that MCP traffic still requires a bearer token even when dashboard auth is disabled.
    /// </summary>
    [Fact]
    public async Task InvokeAsync_McpPortWithoutToken_ReturnsUnauthorized()
    {
        var nextCalled = false;
        var middleware = new BearerAuthMiddleware(
            async context =>
            {
                nextCalled = true;
                context.Response.StatusCode = StatusCodes.Status200OK;
                await Task.CompletedTask;
            },
            Options.Create(new ServerOptions
            {
                AuthToken = "secret-token",
                DashboardDisableAuth = true,
                DashboardPort = 3333,
                McpPort = 4242,
            }),
            NullLogger<BearerAuthMiddleware>.Instance);

        var context = new DefaultHttpContext();
        context.Request.Path = "/v1/tools";
        context.Connection.LocalPort = 4242;

        await middleware.InvokeAsync(context);

        Assert.False(nextCalled);
        Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
    }
}
