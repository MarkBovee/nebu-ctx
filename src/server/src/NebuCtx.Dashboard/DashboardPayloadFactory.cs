namespace NebuCtx.Dashboard;

using NebuCtx.Application;
using NebuCtx.Projects;

/// <summary>
/// Builds dashboard payloads from the current .NET server state.
/// The data is intentionally lightweight but contract-compatible with the legacy dashboard UI.
/// </summary>
public static class DashboardPayloadFactory
{
    /// <summary>
    /// Builds the version payload expected by the dashboard UI.
    /// </summary>
    /// <returns>Version payload with compatibility fields.</returns>
    public static object BuildVersionPayload()
    {
        return new
        {
            name = "nebu-ctx",
            version = ServerVersion.Current,
            current = ServerVersion.Current,
            latest = ServerVersion.Current,
            update_available = false,
        };
    }

    /// <summary>
    /// Builds a stats payload using the current known server metadata.
    /// </summary>
    /// <param name="toolRegistry">Tool registry.</param>
    /// <param name="projects">Registered projects.</param>
    /// <returns>Stats payload compatible with the legacy dashboard.</returns>
    public static object BuildStatsPayload(ToolRegistry toolRegistry, IReadOnlyList<NebuCtx.Contracts.Projects.ProjectRecord> projects)
    {
        var tools = toolRegistry.GetTools().Tools;
        var commands = tools.ToDictionary(
            tool => tool.Name,
            tool => new
            {
                count = 0,
                input_tokens = 0,
                output_tokens = 0,
            },
            StringComparer.OrdinalIgnoreCase);

        return new
        {
            total_tokens_saved = 0,
            total_tokens_input = 0,
            total_input_tokens = 0,
            total_output_tokens = 0,
            cache_hits = 0,
            total_tool_calls = 0,
            total_commands = 0,
            first_use = projects.Count > 0 ? projects.Min(project => project.CreatedAt).ToString("O") : null,
            daily = Array.Empty<object>(),
            commands,
            project_count = projects.Count,
            registered_tool_count = tools.Count,
        };
    }

    /// <summary>
    /// Builds a lightweight gain payload from current server capabilities.
    /// </summary>
    /// <param name="toolRegistry">Tool registry.</param>
    /// <returns>Gain payload expected by the overview view.</returns>
    public static object BuildGainPayload(ToolRegistry toolRegistry)
    {
        return new
        {
            summary = new
            {
                score = new
                {
                    total = 0,
                    compression = 0,
                    cost_efficiency = 0,
                    quality = 0,
                    consistency = 0,
                },
                model = new
                {
                    cost = new
                    {
                        input_per_m = 0.00,
                        output_per_m = 0.00,
                    },
                },
            },
            tasks = toolRegistry.GetTools().Tools
                .Select(tool => new
                {
                    category = tool.Name,
                    tokens_saved = 0,
                    tool_spend_usd = 0.0,
                })
                .ToArray(),
        };
    }

    /// <summary>
    /// Builds the MCP live-session payload.
    /// </summary>
    /// <returns>Live MCP payload.</returns>
    public static object BuildMcpPayload()
    {
        return new
        {
            started_at = DateTimeOffset.UtcNow.ToString("O"),
            tool_calls = 0,
            tokens_saved = 0,
            tokens_original = 0,
            sessions = Array.Empty<object>(),
        };
    }

    /// <summary>
    /// Builds the agents payload.
    /// </summary>
    /// <returns>Agents payload.</returns>
    public static object BuildAgentsPayload()
    {
        return new
        {
            total_active = 0,
            pending_messages = 0,
            shared_contexts = 0,
            agents = Array.Empty<object>(),
        };
    }

    /// <summary>
    /// Builds the gotchas payload.
    /// </summary>
    /// <returns>Gotchas payload.</returns>
    public static object BuildGotchasPayload()
    {
        return new
        {
            gotchas = Array.Empty<object>(),
            stats = new
            {
                total_errors_detected = 0,
                total_prevented = 0,
                total_fixes_correlated = 0,
            },
        };
    }

    /// <summary>
    /// Builds the feedback payload.
    /// </summary>
    /// <returns>Feedback payload.</returns>
    public static object BuildFeedbackPayload()
    {
        return new
        {
            learned_thresholds = new Dictionary<string, object>(),
            outcomes = Array.Empty<object>(),
            metrics = Array.Empty<object>(),
        };
    }

    /// <summary>
    /// Builds the routes payload from the known .NET host routes.
    /// </summary>
    /// <returns>Route payload expected by the dashboard route view.</returns>
    public static object BuildRoutesPayload()
    {
        var routes = GetRouteDescriptors();
        return new
        {
            routes,
            indexed_file_count = routes.Length,
            route_candidate_count = routes.Length,
        };
    }

    /// <summary>
    /// Builds a symbol payload from registered tools and known route handlers.
    /// </summary>
    /// <param name="toolRegistry">Tool registry.</param>
    /// <returns>Symbol list.</returns>
    public static object[] BuildSymbolsPayload(ToolRegistry toolRegistry)
    {
        var routeSymbols = GetRouteDescriptors().Select(route => new
        {
            name = route.handler,
            kind = "route",
            file = route.file,
            start_line = route.line,
            end_line = route.line,
            is_exported = true,
        });

        var toolSymbols = toolRegistry.GetTools().Tools.Select(tool => new
        {
            name = tool.Name,
            kind = "tool",
            file = "NebuCtx.Tools",
            start_line = 1,
            end_line = 1,
            is_exported = true,
        });

        return routeSymbols.Concat(toolSymbols).Cast<object>().ToArray();
    }

    /// <summary>
    /// Builds a search index payload from the known routes and tool definitions.
    /// </summary>
    /// <param name="toolRegistry">Tool registry.</param>
    /// <returns>Search index payload.</returns>
    public static object BuildSearchIndexPayload(ToolRegistry toolRegistry)
    {
        var tools = toolRegistry.GetTools().Tools;
        var routes = GetRouteDescriptors();
        var topChunks = tools.Select(tool => new
        {
            symbol_name = tool.Name,
            file_path = "NebuCtx.Tools",
            kind = "tool",
            token_count = EstimateTokenCount(tool.Description),
            start_line = 1,
            end_line = 1,
        }).ToArray();

        return new
        {
            doc_count = routes.Length + tools.Count,
            chunk_count = topChunks.Length,
            language_distribution = new Dictionary<string, int>
            {
                ["route"] = routes.Length,
                ["tool"] = tools.Count,
            },
            top_chunks_by_token_count = topChunks,
        };
    }

    /// <summary>
    /// Builds dashboard search results by matching against route paths and tool names.
    /// </summary>
    /// <param name="query">Search query.</param>
    /// <param name="limit">Maximum result count.</param>
    /// <param name="toolRegistry">Tool registry.</param>
    /// <returns>Search result payload.</returns>
    public static object BuildSearchPayload(string? query, int? limit, ToolRegistry toolRegistry)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return new { results = Array.Empty<object>() };
        }

        var normalizedQuery = query.Trim();
        var maxResults = limit is > 0 ? limit.Value : 20;

        var routeResults = GetRouteDescriptors()
            .Where(route => route.path.Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase)
                || route.handler.Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase))
            .Select(route => new
            {
                score = ScoreMatch(route.path, normalizedQuery),
                symbol_name = route.handler,
                kind = "route",
                file_path = route.file,
                start_line = route.line,
                end_line = route.line,
                snippet = $"{route.method} {route.path} handled by {route.handler}",
            });

        var toolResults = toolRegistry.GetTools().Tools
            .Where(tool => tool.Name.Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase)
                || tool.Description.Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase))
            .Select(tool => new
            {
                score = ScoreMatch(tool.Name, normalizedQuery),
                symbol_name = tool.Name,
                kind = "tool",
                file_path = "NebuCtx.Tools",
                start_line = 1,
                end_line = 1,
                snippet = tool.Description,
            });

        var results = routeResults.Concat(toolResults)
            .OrderByDescending(result => result.score)
            .Take(maxResults)
            .Cast<object>()
            .ToArray();

        return new { results };
    }

    /// <summary>
    /// Builds the call graph payload from the current tool and route metadata.
    /// </summary>
    /// <param name="toolRegistry">Tool registry.</param>
    /// <returns>Call graph payload.</returns>
    public static object BuildCallGraphPayload(ToolRegistry toolRegistry)
    {
        return new
        {
            edges = Array.Empty<object>(),
            indexed_file_count = GetRouteDescriptors().Length,
            indexed_symbol_count = BuildSymbolsPayload(toolRegistry).Length,
            analyzed_file_count = GetRouteDescriptors().Length,
        };
    }

    /// <summary>
    /// Builds the context-layer pipeline payload.
    /// </summary>
    /// <returns>Pipeline payload.</returns>
    public static object BuildPipelineStatsPayload()
    {
        return new
        {
            runs = 0,
            per_layer = new Dictionary<string, object>(),
        };
    }

    /// <summary>
    /// Builds the context-ledger payload.
    /// </summary>
    /// <returns>Context-ledger payload.</returns>
    public static object BuildContextLedgerPayload()
    {
        return new
        {
            entries_count = 0,
            total_tokens_sent = 0,
            total_tokens_saved = 0,
            compression_ratio = 0,
            pressure = new
            {
                utilization = 0,
                recommendation = "NoAction",
            },
            mode_distribution = new Dictionary<string, int>(),
            entries = Array.Empty<object>(),
        };
    }

    /// <summary>
    /// Builds the intent payload.
    /// </summary>
    /// <returns>Intent payload.</returns>
    public static object BuildIntentPayload()
    {
        return new
        {
            active = false,
            intent = new { },
        };
    }

    /// <summary>
    /// Estimates a pseudo-token count for dashboard display based on text length.
    /// </summary>
    /// <param name="text">Source text.</param>
    /// <returns>Approximate token count.</returns>
    private static int EstimateTokenCount(string text)
    {
        return Math.Max(1, text.Length / 4);
    }

    /// <summary>
    /// Scores a search match using a simple containment heuristic.
    /// </summary>
    /// <param name="source">Source text.</param>
    /// <param name="query">Query text.</param>
    /// <returns>Relative score for sorting.</returns>
    private static double ScoreMatch(string source, string query)
    {
        if (source.Equals(query, StringComparison.OrdinalIgnoreCase))
        {
            return 100;
        }

        if (source.Contains(query, StringComparison.OrdinalIgnoreCase))
        {
            return 50;
        }

        return 10;
    }

    /// <summary>
    /// Returns the known route descriptors exposed by the .NET host.
    /// </summary>
    /// <returns>Route descriptors.</returns>
    private static RouteDescriptor[] GetRouteDescriptors()
    {
        return
        [
            new("GET", "/health", "Health", "NebuCtx.Server.Host/Program.cs", 76),
            new("GET", "/v1/manifest", "Manifest", "NebuCtx.Server.Host/Program.cs", 79),
            new("GET", "/v1/tools", "Tools", "NebuCtx.Server.Host/Program.cs", 82),
            new("POST", "/v1/tools/call", "CallTool", "NebuCtx.Server.Host/Program.cs", 85),
            new("POST", "/v1/projects/resolve", "ResolveProject", "NebuCtx.Server.Host/Projects/ProjectApiEndpoints.cs", 23),
            new("GET", "/v1/projects", "ListProjects", "NebuCtx.Server.Host/Projects/ProjectApiEndpoints.cs", 24),
            new("GET", "/v1/projects/{projectId}/bindings", "GetBindings", "NebuCtx.Server.Host/Projects/ProjectApiEndpoints.cs", 25),
            new("POST", "/v1/projects/{projectId}/bindings", "BindWorkspace", "NebuCtx.Server.Host/Projects/ProjectApiEndpoints.cs", 26),
            new("GET", "/api/version", "DashboardVersion", "NebuCtx.Dashboard/DashboardEndpoints.cs", 25),
            new("GET", "/api/stats", "DashboardStats", "NebuCtx.Dashboard/DashboardEndpoints.cs", 39),
            new("GET", "/api/search-index", "DashboardSearchIndex", "NebuCtx.Dashboard/DashboardEndpoints.cs", 82),
            new("GET", "/api/search", "DashboardSearch", "NebuCtx.Dashboard/DashboardEndpoints.cs", 83),
            new("GET", "/api/routes", "DashboardRoutes", "NebuCtx.Dashboard/DashboardEndpoints.cs", 81),
            new("GET", "/api/symbols", "DashboardSymbols", "NebuCtx.Dashboard/DashboardEndpoints.cs", 80),
        ];
    }

    /// <summary>
    /// Immutable dashboard route descriptor.
    /// </summary>
    /// <param name="method">HTTP method.</param>
    /// <param name="path">Route path.</param>
    /// <param name="handler">Logical handler name.</param>
    /// <param name="file">Owning file path.</param>
    /// <param name="line">Representative line number.</param>
    private sealed record RouteDescriptor(string method, string path, string handler, string file, int line);
}