namespace NebuCtx.Tools.Routes;

using NebuCtx.Application;
using NebuCtx.Application.Routing;

/// <summary>
/// Tool handler for ctx_routes — lists known HTTP routes from the .NET host.
/// </summary>
public sealed class RoutesToolHandler : IToolHandler
{
    /// <inheritdoc />
    public string Name => "ctx_routes";

    /// <inheritdoc />
    public string Description => "List HTTP routes exposed by the .NET host. Optional filters: method, path.";

    /// <inheritdoc />
    public Dictionary<string, object?> InputSchema => new()
    {
        ["type"] = "object",
        ["properties"] = new Dictionary<string, object?>
        {
            ["method"] = new Dictionary<string, object?>
            {
                ["type"] = "string",
                ["description"] = "Optional HTTP method filter, for example GET or POST.",
            },
            ["path"] = new Dictionary<string, object?>
            {
                ["type"] = "string",
                ["description"] = "Optional case-insensitive route path substring filter.",
            },
        },
    };

    /// <inheritdoc />
    public Task<object> ExecuteAsync(Dictionary<string, object?> arguments, ToolExecutionContext context, CancellationToken cancellationToken = default)
    {
        var method = GetStringArg(arguments, "method");
        var path = GetStringArg(arguments, "path");
        var routes = RouteCatalog.Search(method, path)
            .Select(route => new
            {
                method = route.Method,
                path = route.Path,
                handler = route.Handler,
                file = route.File,
                line = route.Line,
            })
            .ToArray();

        return Task.FromResult<object>(new
        {
            count = routes.Length,
            routes,
        });
    }

    /// <summary>
    /// Extracts a string argument from the arguments dictionary.
    /// </summary>
    /// <param name="arguments">Tool arguments.</param>
    /// <param name="key">Argument key.</param>
    /// <returns>String value when present; otherwise null.</returns>
    private static string? GetStringArg(Dictionary<string, object?> arguments, string key)
    {
        return arguments.TryGetValue(key, out var value) ? value?.ToString() : null;
    }
}