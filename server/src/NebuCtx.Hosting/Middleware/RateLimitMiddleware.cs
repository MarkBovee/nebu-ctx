namespace NebuCtx.Hosting.Middleware;

using System.Collections.Concurrent;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using NebuCtx.Contracts.Configuration;

/// <summary>
/// Token bucket rate limiter middleware.
/// Limits requests per second with burst capacity.
/// Returns 429 Too Many Requests when the bucket is empty.
/// </summary>
public sealed class RateLimitMiddleware
{
    private readonly RequestDelegate _next;
    private readonly int _maxRequestsPerSecond;
    private readonly int _burstCapacity;
    private readonly ILogger<RateLimitMiddleware> _logger;

    private double _tokens;
    private DateTime _lastRefill;
    private readonly object _lock = new();

    /// <summary>
    /// Initializes the rate limiter from server options resolved via DI.
    /// </summary>
    /// <param name="next">Next middleware in the pipeline.</param>
    /// <param name="options">Server configuration options.</param>
    /// <param name="logger">Logger for rate limit events.</param>
    public RateLimitMiddleware(RequestDelegate next, ServerOptions options, ILogger<RateLimitMiddleware> logger)
    {
        _next = next;
        _maxRequestsPerSecond = options.MaxRequestsPerSecond;
        _burstCapacity = options.RateBurst;
        _logger = logger;
        _tokens = options.RateBurst;
        _lastRefill = DateTime.UtcNow;
    }

    /// <summary>
    /// Checks the token bucket and either passes the request through or returns 429.
    /// </summary>
    public async Task InvokeAsync(HttpContext context)
    {
        if (!TryAcquireToken())
        {
            _logger.LogWarning("Rate limit exceeded for {Method} {Path}", context.Request.Method, context.Request.Path);
            context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            await context.Response.WriteAsJsonAsync(new { error = "Rate limit exceeded" });
            return;
        }

        await _next(context);
    }

    /// <summary>
    /// Attempts to consume one token from the bucket, refilling elapsed tokens first.
    /// </summary>
    /// <returns>True if a token was available and consumed; false if the bucket is empty.</returns>
    private bool TryAcquireToken()
    {
        lock (_lock)
        {
            var now = DateTime.UtcNow;
            var elapsed = (now - _lastRefill).TotalSeconds;
            _lastRefill = now;

            // Refill tokens based on elapsed time
            _tokens = Math.Min(_burstCapacity, _tokens + elapsed * _maxRequestsPerSecond);

            if (_tokens < 1.0)
            {
                return false;
            }

            _tokens -= 1.0;
            return true;
        }
    }
}
