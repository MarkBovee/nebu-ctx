namespace NebuCtx.Server.Core.Middleware;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NebuCtx.Contracts.Configuration;

/// <summary>
/// Concurrency limiter middleware using a semaphore.
/// Returns 429 Too Many Requests when max concurrent requests are exceeded.
/// </summary>
public sealed class ConcurrencyLimitMiddleware
{
    private readonly RequestDelegate _next;
    private readonly SemaphoreSlim _semaphore;
    private readonly ILogger<ConcurrencyLimitMiddleware> _logger;

    /// <summary>
    /// Initializes the concurrency limiter from server options resolved via DI.
    /// </summary>
    /// <param name="next">Next middleware in the pipeline.</param>
    /// <param name="options">Server configuration options.</param>
    /// <param name="logger">Logger for concurrency limit events.</param>
    public ConcurrencyLimitMiddleware(RequestDelegate next, IOptions<ServerOptions> options, ILogger<ConcurrencyLimitMiddleware> logger)
    {
        _next = next;
        var serverOptions = options.Value;
        _semaphore = new SemaphoreSlim(serverOptions.MaxConcurrency, serverOptions.MaxConcurrency);
        _logger = logger;
    }

    /// <summary>
    /// Attempts to acquire a concurrency permit before passing the request through.
    /// </summary>
    public async Task InvokeAsync(HttpContext context)
    {
        if (!_semaphore.Wait(0))
        {
            _logger.LogWarning("Concurrency limit reached for {Method} {Path}", context.Request.Method, context.Request.Path);
            context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            await context.Response.WriteAsJsonAsync(new { error = "Too many concurrent requests" });
            return;
        }

        try
        {
            await _next(context);
        }
        finally
        {
            _semaphore.Release();
        }
    }
}
