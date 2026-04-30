namespace NebuCtx.Server.Core.Middleware;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NebuCtx.Contracts.Configuration;

/// <summary>
/// Request timeout middleware. Cancels tool execution if it exceeds the configured timeout.
/// Returns 408 Gateway Timeout on expiration.
/// </summary>
public sealed class RequestTimeoutMiddleware
{
    private readonly RequestDelegate _next;
    private readonly TimeSpan _timeout;
    private readonly ILogger<RequestTimeoutMiddleware> _logger;

    /// <summary>
    /// Initializes the timeout middleware from server options resolved via DI.
    /// </summary>
    /// <param name="next">Next middleware in the pipeline.</param>
    /// <param name="options">Server configuration options.</param>
    /// <param name="logger">Logger for timeout events.</param>
    public RequestTimeoutMiddleware(RequestDelegate next, IOptions<ServerOptions> options, ILogger<RequestTimeoutMiddleware> logger)
    {
        _next = next;
        _timeout = TimeSpan.FromMilliseconds(options.Value.RequestTimeoutMs);
        _logger = logger;
    }

    /// <summary>
    /// Wraps the downstream pipeline with a cancellation token that fires after the timeout.
    /// </summary>
    public async Task InvokeAsync(HttpContext context)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
        timeoutCts.CancelAfter(_timeout);

        // Replace the request abort token with our timeout-aware token
        var originalToken = context.RequestAborted;
        context.RequestAborted = timeoutCts.Token;

        try
        {
            await _next(context);
        }
        catch (OperationCanceledException) when (!originalToken.IsCancellationRequested)
        {
            // Timeout fired, not client disconnect
            _logger.LogWarning("Request timeout on {Method} {Path}", context.Request.Method, context.Request.Path);
            context.Response.StatusCode = StatusCodes.Status408RequestTimeout;
            await context.Response.WriteAsJsonAsync(new { error = "Request timeout" });
        }
    }
}
