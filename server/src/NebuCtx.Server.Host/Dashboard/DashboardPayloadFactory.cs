namespace NebuCtx.Server.Host.Dashboard;

using System.Globalization;
using NebuCtx.Contracts.Dashboard;
using NebuCtx.Contracts.Projects;
using NebuCtx.Server.Core;
using NebuCtx.Server.Core.Routing;
using NebuCtx.Storage;

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
    public static DashboardVersionPayload BuildVersionPayload()
    {
        return new DashboardVersionPayload
        {
            Name = "nebu-ctx",
            Version = ServerVersion.Current,
            Current = ServerVersion.Current,
            Latest = ServerVersion.Current,
            UpdateAvailable = false,
        };
    }

    /// <summary>
    /// Builds a consolidated overview payload for the simplified dashboard overview.
    /// </summary>
    /// <param name="toolRegistry">Tool registry.</param>
    /// <param name="projects">Registered projects.</param>
    /// <param name="telemetryStore">Telemetry aggregation store.</param>
    /// <param name="authToken">Optional auth token for local admin workflows.</param>
    /// <returns>Aggregated overview payload.</returns>
    public static DashboardOverviewResponse BuildDashboardOverviewPayload(ToolRegistry toolRegistry, IReadOnlyList<ProjectRecord> projects, TelemetryStore telemetryStore, string? authToken)
    {
        return new DashboardOverviewResponse
        {
            Version = BuildVersionPayload(),
            Stats = BuildStatsPayload(toolRegistry, projects, telemetryStore),
            Gain = BuildGainPayload(telemetryStore),
            AuthToken = authToken,
        };
    }

    /// <summary>
    /// Builds the dashboard domain map used to consolidate detailed screens into operator areas.
    /// </summary>
    /// <returns>Dashboard domain payload.</returns>
    public static DashboardDomainsResponse BuildDashboardDomainsPayload()
    {
        return new DashboardDomainsResponse
        {
            Domains =
            [
                CreateDomain("overview", "Overview", "System summary, live sessions, and token access.",
                    ("overview", "Overview"), ("live", "Live Observatory"), ("token", "MCP Token")),
                CreateDomain("memory", "Memory", "Knowledge, brain, and bug memory surfaces.",
                    ("knowledge", "Knowledge Graph"), ("brain", "Brain Memory"), ("bugs", "Bug Memory")),
                CreateDomain("code", "Code Intelligence", "Search, symbols, dependencies, call graphs, and routes.",
                    ("search", "Search Explorer"), ("symbols", "Symbol Explorer"), ("deps", "Dependency Map"), ("callgraph", "Call Graph"), ("routes", "Route Map")),
                CreateDomain("context", "Context", "Compression and context-layer pressure diagnostics.",
                    ("compression", "Compression Lab"), ("contextlayer", "Context Layer")),
                CreateDomain("agents", "Agents", "Agent coordination and multi-actor activity.",
                    ("agents", "Agent World")),
                CreateDomain("learning", "Learning", "Feedback loops and learned operating curves.",
                    ("learning", "Learning Curves")),
            ],
        };
    }

    /// <summary>
    /// Builds a per-project memory payload for dashboard and admin workflows.
    /// </summary>
    /// <param name="project">Resolved project record.</param>
    /// <param name="knowledgeEntries">Knowledge entries for the project.</param>
    /// <param name="brainEntries">Brain entries for the project.</param>
    /// <returns>Project memory payload.</returns>
    public static ProjectMemoryResponse BuildProjectMemoryPayload(ProjectRecord project, IReadOnlyList<KnowledgeEntry> knowledgeEntries, IReadOnlyList<BrainEntry> brainEntries)
    {
        return new ProjectMemoryResponse
        {
            ProjectId = project.ProjectId,
            ProjectName = project.Slug,
            Knowledge = knowledgeEntries
                .OrderBy(entry => entry.Category, StringComparer.OrdinalIgnoreCase)
                .ThenBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase)
                .Select(entry => new ProjectKnowledgeFactResponse
                {
                    Category = entry.Category,
                    Key = entry.Key,
                    Value = entry.Value,
                    Confidence = entry.Confidence,
                    UpdatedAt = entry.UpdatedAt,
                })
                .ToArray(),
            Brain = brainEntries
                .OrderByDescending(entry => entry.CreatedAt)
                .Select(entry => new ProjectBrainEntryResponse
                {
                    Key = entry.Key,
                    Value = entry.Value,
                    CreatedAt = entry.CreatedAt,
                })
                .ToArray(),
        };
    }

    /// <summary>
    /// Builds a stats payload using the current known server metadata.
    /// </summary>
    /// <param name="toolRegistry">Tool registry.</param>
    /// <param name="projects">Registered projects.</param>
    /// <returns>Stats payload compatible with the legacy dashboard.</returns>
    public static DashboardStatsPayload BuildStatsPayload(ToolRegistry toolRegistry, IReadOnlyList<ProjectRecord> projects, TelemetryStore telemetryStore)
    {
        var tools = toolRegistry.GetRegisteredTools().Tools;
        var telemetry = telemetryStore.GetSnapshot();
        var aggregatedLanguageCounts = AggregateLanguageCounts(projects);
        var totalSourceFiles = projects.Sum(project => project.ProjectMetadata?.Summary.SourceFileCount ?? 0);
        var totalFiles = projects.Sum(project => project.ProjectMetadata?.Summary.TotalFileCount ?? 0);
        var commands = BuildCommandPayloads(tools, telemetry);
        var daily = telemetry.Daily.Select(item => new DashboardDailyPayload
        {
            Date = item.Date,
            InputTokens = item.InputTokens,
            OutputTokens = item.OutputTokens,
            Commands = item.Commands,
        }).ToArray();

        return new DashboardStatsPayload
        {
            TotalTokensSaved = Math.Max(0, telemetry.TotalInputTokens - telemetry.TotalOutputTokens),
            TotalTokensInput = telemetry.TotalInputTokens,
            TotalInputTokensLegacy = telemetry.TotalInputTokens,
            TotalOutputTokens = telemetry.TotalOutputTokens,
            CacheHits = telemetry.CacheHits,
            TotalToolCalls = telemetry.TotalToolCalls,
            TotalCommands = telemetry.TotalToolCalls,
            FirstUse = telemetry.FirstUse?.ToString("O") ?? (projects.Count > 0 ? projects.Min(project => project.CreatedAt).ToString("O") : null),
            Daily = daily,
            Commands = commands,
            ProjectCount = projects.Count,
            RegisteredToolCount = tools.Count,
            IndexedFileCount = totalSourceFiles,
            TotalFileCount = totalFiles,
            LanguageDistribution = aggregatedLanguageCounts.Select(item => new DashboardLanguagePayload { Language = item.Key, FileCount = item.Value }).ToArray(),
        };
    }

    /// <summary>
    /// Builds the knowledge payload from persisted project metadata.
    /// </summary>
    /// <param name="projects">Registered projects.</param>
    /// <returns>Knowledge payload compatible with the legacy dashboard UI.</returns>
    public static object BuildKnowledgePayload(IReadOnlyList<ProjectRecord> projects, IReadOnlyList<KnowledgeEntry>? postgresEntries = null)
    {
        // Merge synthetic project facts with real Postgres-backed facts
        var pgFacts = postgresEntries?
            .Select(e => new Dictionary<string, object?>
            {
                ["project_id"] = e.ProjectId,
                ["project_name"] = projects.FirstOrDefault(p => p.ProjectId == e.ProjectId)?.Slug ?? e.ProjectId,
                ["category"] = e.Category,
                ["fact_name"] = e.Key,
                ["key"] = e.Key,
                ["value"] = e.Value,
                ["confidence"] = (double)e.Confidence,
                ["source"] = "postgres",
            })
            ?? Enumerable.Empty<Dictionary<string, object?>>();

        var facts = DeduplicateKnowledgeFacts(projects
            .SelectMany(BuildProjectFacts)
            .Concat(pgFacts))
            .OrderByDescending(fact => Convert.ToDouble(fact["confidence"], CultureInfo.InvariantCulture))
            .ThenBy(fact => fact["project_name"]?.ToString(), StringComparer.OrdinalIgnoreCase)
            .ThenBy(fact => fact["category"]?.ToString(), StringComparer.OrdinalIgnoreCase)
            .Cast<object>()
            .ToArray();

        var projectSummaries = facts
            .Cast<Dictionary<string, object?>>()
            .GroupBy(fact => new
            {
                ProjectId = fact["project_id"]?.ToString() ?? string.Empty,
                ProjectName = fact["project_name"]?.ToString() ?? string.Empty,
            })
            .Select(group => new
            {
                project_id = group.Key.ProjectId,
                project_name = group.Key.ProjectName,
                fact_count = group.Count(),
            })
            .OrderBy(summary => summary.project_name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new
        {
            facts,
            projects = projectSummaries,
            project_count = projects.Count,
        };
    }

    /// <summary>
    /// Builds the brain dashboard payload from real Postgres brain entries.
    /// Groups entries by project for the dashboard brain view.
    /// </summary>
    /// <param name="projects">All registered projects (for name lookup).</param>
    /// <param name="entries">Brain entries from Postgres keyed by project ID.</param>
    /// <returns>Brain payload with entries and project summaries.</returns>
    public static object BuildBrainPayload(IReadOnlyList<ProjectRecord> projects, IReadOnlyDictionary<string, IReadOnlyList<BrainEntry>> entriesByProject)
    {
        var allEntries = entriesByProject
            .SelectMany(kvp => kvp.Value.Select(e => new
            {
                project_id = kvp.Key,
                project_name = projects.FirstOrDefault(p => p.ProjectId == kvp.Key)?.Slug ?? kvp.Key,
                key = e.Key,
                value = e.Value,
                created_at = e.CreatedAt,
            }))
            .OrderByDescending(e => e.created_at)
            .Cast<object>()
            .ToArray();

        var projectSummaries = entriesByProject
            .Select(kvp => new
            {
                project_id = kvp.Key,
                project_name = projects.FirstOrDefault(p => p.ProjectId == kvp.Key)?.Slug ?? kvp.Key,
                entry_count = kvp.Value.Count,
            })
            .OrderBy(s => s.project_name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new
        {
            entries = allEntries,
            projects = projectSummaries,
            total_count = allEntries.Length,
        };
    }

    /// <summary>
    /// Builds a lightweight gain payload from current server capabilities.
    /// </summary>
    /// <param name="toolRegistry">Tool registry.</param>
    /// <returns>Gain payload expected by the overview view.</returns>
    public static DashboardGainPayload BuildGainPayload(TelemetryStore telemetryStore)
    {
        var telemetry = telemetryStore.GetSnapshot();
        var totalSaved = Math.Max(0, telemetry.TotalInputTokens - telemetry.TotalOutputTokens);
        var compressionRate = telemetry.TotalInputTokens > 0 ? (double)totalSaved / telemetry.TotalInputTokens : 0;
        var score = Math.Min(100, (int)Math.Round(compressionRate * 100));

        return new DashboardGainPayload
        {
            Summary = new DashboardGainSummaryPayload
            {
                Score = new DashboardGainScorePayload
                {
                    Total = score,
                    Compression = score,
                    CostEfficiency = score,
                    Quality = telemetry.TotalToolCalls > 0 ? 60 : 0,
                    Consistency = telemetry.Sessions.Count > 1 ? 75 : telemetry.TotalToolCalls > 0 ? 50 : 0,
                },
                Model = new DashboardGainModelPayload
                {
                    Cost = new DashboardGainCostPayload
                    {
                        InputPerMillion = 0.00m,
                        OutputPerMillion = 0.00m,
                    },
                },
            },
            Tasks = telemetry.Commands.Values
                .OrderByDescending(command => command.InputTokens - command.OutputTokens)
                .Select(command => new DashboardGainTaskPayload
                {
                    Category = command.Name,
                    TokensSaved = Math.Max(0, command.InputTokens - command.OutputTokens),
                    ToolSpendUsd = EstimateSavedCost(command.InputTokens, command.OutputTokens),
                })
                .ToArray(),
        };
    }

    /// <summary>
    /// Builds the MCP live-session payload.
    /// </summary>
    /// <returns>Live MCP payload.</returns>
    public static object BuildMcpPayload(TelemetryStore telemetryStore)
    {
        var telemetry = telemetryStore.GetSnapshot();
        var latestSession = telemetry.Sessions.FirstOrDefault();

        return new
        {
            started_at = latestSession?.StartedAt.ToString("O"),
            tool_calls = latestSession?.ToolCalls ?? 0,
            tokens_saved = latestSession?.TokensSaved ?? 0,
            tokens_original = latestSession?.TokensOriginal ?? 0,
            sessions = telemetry.Sessions.Select(session => new
            {
                id = session.SessionKey,
                project_id = session.ProjectId,
                actor_label = session.ActorLabel,
                started_at = session.StartedAt.ToString("O"),
                updated_at = session.UpdatedAt.ToString("O"),
                tool_calls = session.ToolCalls,
                tokens_saved = session.TokensSaved,
                tokens_original = session.TokensOriginal,
            }).ToArray(),
        };
    }

    /// <summary>
    /// Builds the current session payload from the latest observed telemetry session.
    /// </summary>
    /// <param name="telemetryStore">Telemetry store.</param>
    /// <returns>Current session payload.</returns>
    public static object BuildSessionPayload(TelemetryStore telemetryStore)
    {
        var telemetry = telemetryStore.GetSnapshot();
        var latestSession = telemetry.Sessions.FirstOrDefault();
        return new
        {
            id = latestSession?.SessionKey ?? "server",
            version = telemetry.TotalToolCalls,
            started_at = latestSession?.StartedAt.ToString("O") ?? DateTimeOffset.UtcNow.ToString("O"),
            updated_at = latestSession?.UpdatedAt.ToString("O") ?? DateTimeOffset.UtcNow.ToString("O"),
            project_root = latestSession?.ProjectRoot,
            stats = new
            {
                total_tokens_saved = latestSession?.TokensSaved ?? 0,
                total_tokens_input = latestSession?.TokensOriginal ?? 0,
                cache_hits = 0,
                total_tool_calls = latestSession?.ToolCalls ?? 0,
            },
        };
    }

    /// <summary>
    /// Builds the agents payload.
    /// </summary>
    /// <returns>Agents payload.</returns>
    public static object BuildAgentsPayload(TelemetryStore telemetryStore, IReadOnlyList<ProjectRecord> projects)
    {
        var telemetry = telemetryStore.GetSnapshot();
        var latestEventsBySession = telemetry.Events
            .GroupBy(item => (item.ProjectId, item.ActorLabel))
            .ToDictionary(group => group.Key, group => group.OrderByDescending(item => item.Timestamp).First());

        var agents = telemetry.Sessions.Select(session =>
        {
            var lastUpdatedMinutes = (int)Math.Max(0, Math.Round((DateTimeOffset.UtcNow - session.UpdatedAt).TotalMinutes));
            var status = lastUpdatedMinutes <= 10 ? "active" : lastUpdatedMinutes <= 60 ? "idle" : "offline";
            latestEventsBySession.TryGetValue((session.ProjectId, session.ActorLabel), out var latestEvent);
            return new
            {
                id = session.ActorLabel,
                type = "thin-client",
                role = ResolveProjectLabel(projects, session.ProjectId),
                status,
                status_message = latestEvent is null ? null : $"last tool {latestEvent.ToolName}",
                last_active_minutes_ago = lastUpdatedMinutes,
            };
        }).ToArray();

        var sharedContexts = telemetry.Sessions
            .GroupBy(session => session.ProjectId, StringComparer.OrdinalIgnoreCase)
            .Count(group => group.Select(session => session.ActorLabel).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1);

        return new
        {
            total_active = agents.Count(agent => string.Equals(agent.status, "active", StringComparison.OrdinalIgnoreCase)),
            pending_messages = telemetry.Events.Count(item => item.Timestamp >= DateTimeOffset.UtcNow.AddMinutes(-5)),
            shared_contexts = sharedContexts,
            agents,
        };
    }

    /// <summary>
    /// Builds the gotchas payload.
    /// </summary>
    /// <returns>Gotchas payload.</returns>
    public static object BuildGotchasPayload(TelemetryStore telemetryStore)
    {
        var telemetry = telemetryStore.GetSnapshot();
        var gotchas = new List<object>();

        foreach (var command in telemetry.Commands.Values.OrderByDescending(item => item.Count))
        {
            var savingsRate = command.InputTokens > 0 ? (double)Math.Max(0, command.InputTokens - command.OutputTokens) / command.InputTokens : 0;
            if (command.Count == 0 || savingsRate >= 0.2)
            {
                continue;
            }

            gotchas.Add(new
            {
                severity = savingsRate < 0.05 ? "Warning" : "Info",
                category = "compression",
                trigger = $"{command.Name} returns much more data than it receives",
                resolution = $"Prefer narrower filters or a lighter view before calling {command.Name} repeatedly.",
                occurrences = command.Count,
                confidence = Math.Min(0.95, 0.35 + (command.Count * 0.12)),
                prevented_count = Math.Max(0, command.Count - 1),
            });
        }

        var pressureUtilization = CalculatePressureUtilization(telemetry.TotalOutputTokens);
        if (pressureUtilization >= 0.5)
        {
            gotchas.Add(new
            {
                severity = pressureUtilization >= 0.8 ? "Critical" : "Warning",
                category = "context",
                trigger = "Context window pressure is rising during live sessions",
                resolution = "Use map/reference compression modes or split large reads into smaller scoped requests.",
                occurrences = telemetry.Events.Count,
                confidence = Math.Min(0.97, 0.5 + pressureUtilization / 2),
                prevented_count = telemetry.Sessions.Count(session => session.TokensSaved > 0),
            });
        }

        var gotchaArray = gotchas.ToArray();
        return new
        {
            gotchas = gotchaArray,
            stats = new
            {
                total_errors_detected = gotchaArray.Sum(gotcha => (int)gotcha.GetType().GetProperty("occurrences")!.GetValue(gotcha)!),
                total_prevented = gotchaArray.Sum(gotcha => (int)gotcha.GetType().GetProperty("prevented_count")!.GetValue(gotcha)!),
                total_fixes_correlated = gotchaArray.Count(gotcha => (double)gotcha.GetType().GetProperty("confidence")!.GetValue(gotcha)! >= 0.6),
            },
        };
    }

    /// <summary>
    /// Builds the feedback payload.
    /// </summary>
    /// <returns>Feedback payload.</returns>
    public static object BuildFeedbackPayload(TelemetryStore telemetryStore, IReadOnlyList<ProjectRecord> projects)
    {
        var telemetry = telemetryStore.GetSnapshot();
        var learnedThresholds = BuildLearnedThresholds(projects, telemetry);
        var outcomes = telemetry.Events.Select(item => new
        {
            tool = item.ToolName,
            actor_label = item.ActorLabel,
            project_id = item.ProjectId,
            compression_ratio = item.TokensOriginal > 0 ? Math.Round((double)item.TokensSaved / item.TokensOriginal * 100, 2) : 0,
            savings_pct = item.TokensOriginal > 0 ? Math.Round((double)item.TokensSaved / item.TokensOriginal * 100, 2) : 0,
            tokens_original = item.TokensOriginal,
            tokens_saved = item.TokensSaved,
            task_completed = item.TokensOutput <= item.TokensOriginal || item.TokensSaved > 0,
            timestamp = item.Timestamp.ToString("O"),
        }).ToArray();

        return new
        {
            learned_thresholds = learnedThresholds,
            outcomes,
            metrics = Array.Empty<object>(),
        };
    }

    /// <summary>
    /// Builds the buddy payload used on the overview screen.
    /// </summary>
    /// <param name="telemetryStore">Telemetry store.</param>
    /// <param name="projects">Known projects.</param>
    /// <returns>Buddy payload or an empty object when there is no activity yet.</returns>
    public static object BuildBuddyPayload(TelemetryStore telemetryStore, IReadOnlyList<ProjectRecord> projects)
    {
        var telemetry = telemetryStore.GetSnapshot();
        if (telemetry.TotalToolCalls == 0)
        {
            return new { };
        }

        var totalSaved = Math.Max(0, telemetry.TotalInputTokens - telemetry.TotalOutputTokens);
        var compressionRate = telemetry.TotalInputTokens > 0 ? (double)totalSaved / telemetry.TotalInputTokens : 0;
        var topLanguage = AggregateLanguageCounts(projects).OrderByDescending(item => item.Value).Select(item => item.Key).FirstOrDefault() ?? "mixed";
        var mood = ResolveBuddyMood(compressionRate, telemetry.TotalToolCalls);
        var rarity = ResolveBuddyRarity(totalSaved, telemetry.TotalToolCalls);

        // XP drives level via a sqrt curve: level 100 requires ~5M XP.
        // This prevents hitting the cap after only a handful of sessions.
        var xp = totalSaved + (telemetry.TotalToolCalls * 25L);
        var level = (int)Math.Clamp(Math.Floor(Math.Sqrt((double)xp / 500.0)) + 1, 1, 100);
        // xp_next_level is always ahead of current xp so the bar never overflows.
        var xpNextLevel = level < 100 ? (long)(level * level * 500L) : (long)(100 * 100 * 500L);

        // Log-scaled stat helper: value maps to 0-100 where maxValue → 100.
        static int LogStat(long value, long maxValue) =>
            value <= 0 ? 1 : (int)Math.Clamp(Math.Log10(value + 1.0) / Math.Log10(maxValue + 1.0) * 100.0, 1, 100);

        return new
        {
            name = "Nebby",
            species = $"{topLanguage} familiar",
            level,
            mood,
            streak_days = Math.Max(1, telemetry.Daily.Count),
            speech = ResolveBuddySpeech(mood, topLanguage, telemetry),
            rarity,
            ascii_frames = new[]
            {
                new[] { "  /\\_/\\", " ( o.o )", "  > ^ <" },
                new[] { "  /\\_/\\", " ( -.- )", "  > ^ <" },
            },
            anim_ms = 900,
            xp,
            xp_next_level = xpNextLevel,
            stats = new
            {
                // Compression: actual rate → already naturally 0-100 %.
                compression = Math.Min(100, (int)Math.Round(compressionRate * 100)),
                // Vigilance: log scale, 2 000 events → 100.
                vigilance = LogStat(telemetry.Events.Count, 2000),
                // Endurance: active days (Daily), 500 days → 100.
                endurance = LogStat(telemetry.Daily.Count, 500),
                // Wisdom: distinct languages, 5 → 100.
                wisdom = Math.Min(100, AggregateLanguageCounts(projects).Count * 20),
                // Experience: based on total XP, 5 M XP → 100.
                experience = LogStat(xp, 5_000_000L),
            },
        };
    }

    /// <summary>
    /// Builds the routes payload from the known .NET host routes.
    /// </summary>
    /// <returns>Route payload expected by the dashboard route view.</returns>
    public static object BuildRoutesPayload()
    {
        var routes = RouteCatalog.GetAll()
            .Select(route => new
            {
                method = route.Method,
                path = route.Path,
                handler = route.Handler,
                file = route.File,
                line = route.Line,
            })
            .ToArray();

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
        var routeSymbols = RouteCatalog.GetAll().Select(route => new
        {
            name = route.Handler,
            kind = "route",
            file = route.File,
            start_line = route.Line,
            end_line = route.Line,
            is_exported = true,
        });

        var toolSymbols = toolRegistry.GetRegisteredTools().Tools.Select(tool => new
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
        var tools = toolRegistry.GetRegisteredTools().Tools;
        var routes = RouteCatalog.GetAll();
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
            doc_count = routes.Count + tools.Count,
            chunk_count = topChunks.Length,
            language_distribution = new Dictionary<string, int>
            {
                ["route"] = routes.Count,
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

        var routeResults = RouteCatalog.GetAll()
            .Where(route => route.Path.Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase)
                || route.Handler.Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase))
            .Select(route => new
            {
                score = ScoreMatch(route.Path, normalizedQuery),
                symbol_name = route.Handler,
                kind = "route",
                file_path = route.File,
                start_line = route.Line,
                end_line = route.Line,
                snippet = $"{route.Method} {route.Path} handled by {route.Handler}",
            });

        var toolResults = toolRegistry.GetRegisteredTools().Tools
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
    /// Builds a lightweight graph payload that exposes known source files.
    /// </summary>
    /// <returns>Graph payload compatible with the legacy dashboard.</returns>
    public static object BuildGraphPayload(IReadOnlyList<ProjectRecord> projects)
    {
        var routeFiles = RouteCatalog.GetAll()
            .GroupBy(route => route.File, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => (object)new Dictionary<string, object?>
                {
                    ["path"] = group.Key,
                    ["language"] = InferLanguage(group.Key),
                    ["route_count"] = group.Count(),
                    ["token_count"] = group.Count() * 32,
                    ["primary_handler"] = group.First().Handler,
                },
                StringComparer.OrdinalIgnoreCase);

        // Keep the bootstrap file visible in the graph so existing operator views
        // still show the host entrypoint alongside the routed endpoint modules.
        routeFiles.TryAdd(
            "NebuCtx.Server.Host/Program.cs",
            new Dictionary<string, object?>
            {
                ["path"] = "NebuCtx.Server.Host/Program.cs",
                ["language"] = "csharp",
                ["route_count"] = 0,
                ["token_count"] = 96,
                ["primary_handler"] = "Program",
            });

        var projectFiles = projects
            .Where(project => project.ProjectMetadata is not null)
            .ToDictionary(
                project => $"project/{project.ProjectId}",
                project => (object)new Dictionary<string, object?>
                {
                    ["path"] = $"project/{project.Slug}",
                    ["language"] = project.ProjectMetadata!.Summary.Languages.FirstOrDefault()?.Language ?? "unknown",
                    ["route_count"] = 0,
                    ["token_count"] = EstimateProjectTokenCount(project),
                    ["primary_handler"] = project.ProjectId,
                    ["project_id"] = project.ProjectId,
                    ["source_file_count"] = project.ProjectMetadata!.Summary.SourceFileCount,
                    ["total_file_count"] = project.ProjectMetadata!.Summary.TotalFileCount,
                },
                StringComparer.OrdinalIgnoreCase);

        foreach (var pair in projectFiles)
        {
            routeFiles[pair.Key] = pair.Value;
        }

        var edges = projects
            .Where(project => project.ProjectMetadata is not null)
            .SelectMany(project => project.ProjectMetadata!.Summary.Languages.Select(language => new
            {
                source = $"project/{project.ProjectId}",
                target = $"language/{language.Language}",
                weight = language.FileCount,
                kind = "project-language",
            }))
            .Cast<object>()
            .ToArray();

        return new
        {
            // nodes mirrors files for API consumers that pre-date the files field.
            nodes = routeFiles.Values.ToArray(),
            edges,
            files = routeFiles,
            indexed_file_count = routeFiles.Count,
        };
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
            indexed_file_count = RouteCatalog.GetAll().Count,
            indexed_symbol_count = BuildSymbolsPayload(toolRegistry).Length,
            analyzed_file_count = RouteCatalog.GetAll().Count,
        };
    }

    /// <summary>
    /// Builds the context-layer pipeline payload.
    /// </summary>
    /// <returns>Pipeline payload.</returns>
    public static object BuildPipelineStatsPayload(TelemetryStore telemetryStore)
    {
        var telemetry = telemetryStore.GetSnapshot();
        return new
        {
            runs = telemetry.TotalToolCalls,
            per_layer = new Dictionary<string, object>
            {
                ["mcp"] = new
                {
                    runs = telemetry.TotalToolCalls,
                    total_input_tokens = telemetry.TotalInputTokens,
                    total_output_tokens = telemetry.TotalOutputTokens,
                },
            },
        };
    }

    /// <summary>
    /// Builds the context-ledger payload.
    /// </summary>
    /// <returns>Context-ledger payload.</returns>
    public static object BuildContextLedgerPayload(TelemetryStore telemetryStore)
    {
        var telemetry = telemetryStore.GetSnapshot();
        var totalSaved = Math.Max(0, telemetry.TotalInputTokens - telemetry.TotalOutputTokens);
        var modeDistribution = telemetry.Events
            .GroupBy(item => item.Mode, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);

        return new
        {
            entries_count = telemetry.Events.Count,
            total_tokens_sent = telemetry.TotalOutputTokens,
            total_tokens_saved = totalSaved,
            compression_ratio = telemetry.TotalInputTokens > 0 ? Math.Round((double)totalSaved / telemetry.TotalInputTokens, 4) : 0,
            pressure = new
            {
                utilization = CalculatePressureUtilization(telemetry.TotalOutputTokens),
                recommendation = RecommendPressureMode(telemetry.TotalOutputTokens),
            },
            mode_distribution = modeDistribution,
            entries = telemetry.Events.Select(item => new
            {
                timestamp = item.Timestamp.ToString("O"),
                type = item.Type,
                tool = item.ToolName,
                project_id = item.ProjectId,
                actor_label = item.ActorLabel,
                tokens_saved = item.TokensSaved,
                tokens_original = item.TokensOriginal,
            }).ToArray(),
        };
    }

    /// <summary>
    /// Builds the recent event feed payload for the live dashboard view.
    /// </summary>
    /// <param name="telemetryStore">Telemetry store.</param>
    /// <returns>Recent event feed payload.</returns>
    public static object BuildEventsPayload(TelemetryStore telemetryStore)
    {
        return telemetryStore.GetSnapshot().Events.Select(item => new
        {
            timestamp = item.Timestamp.ToString("O"),
            kind = new
            {
                type = item.Type,
                tool = item.ToolName,
                mode = item.Mode,
                path = item.Path,
                actor_label = item.ActorLabel,
                project_id = item.ProjectId,
                tokens_saved = item.TokensSaved,
                tokens_original = item.TokensOriginal,
                tokens_output = item.TokensOutput,
            },
        }).ToArray();
    }

    /// <summary>
    /// Builds the intent payload.
    /// </summary>
    /// <returns>Intent payload.</returns>
    public static object BuildIntentPayload(TelemetryStore telemetryStore, IReadOnlyList<ProjectRecord> projects)
    {
        var telemetry = telemetryStore.GetSnapshot();
        var latestEvent = telemetry.Events.OrderByDescending(item => item.Timestamp).FirstOrDefault();
        if (latestEvent is null)
        {
            return new
            {
                active = false,
                intent = new { },
            };
        }

        var project = projects.FirstOrDefault(item => string.Equals(item.ProjectId, latestEvent.ProjectId, StringComparison.OrdinalIgnoreCase));
        var recentTools = telemetry.Events
            .Where(item => string.Equals(item.ProjectId, latestEvent.ProjectId, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(item => item.Timestamp)
            .Select(item => item.ToolName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(3)
            .ToArray();

        return new
        {
            active = latestEvent.Timestamp >= DateTimeOffset.UtcNow.AddHours(-6),
            intent = new
            {
                task_type = InferIntentTaskType(latestEvent.ToolName),
                confidence = 0.72,
                scope = project?.Slug ?? latestEvent.ProjectId,
                targets = recentTools,
                language_hint = project?.ProjectMetadata?.Summary.Languages.OrderByDescending(item => item.FileCount).Select(item => item.Language).FirstOrDefault(),
            },
        };
    }

    /// <summary>
    /// Builds the compression demo payload from route, tool, or project metadata summaries.
    /// </summary>
    /// <param name="path">Selected file or synthetic path.</param>
    /// <param name="task">Optional task hint.</param>
    /// <param name="toolRegistry">Tool registry.</param>
    /// <param name="projects">Known projects.</param>
    /// <returns>Compression demo payload.</returns>
    public static object BuildCompressionDemoPayload(string? path, string? task, ToolRegistry toolRegistry, IReadOnlyList<ProjectRecord> projects)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return new { error = "Select a file first." };
        }

        var source = ResolveCompressionSource(path, toolRegistry, projects);
        if (source is null)
        {
            return new { error = $"No compression demo source is available for {path}." };
        }

        var original = source.Value.Original;
        var originalLines = CountLines(original);
        var originalTokens = EstimateTokenCount(original);
        var aggressiveOutput = CompressAggressively(original);
        var entropyOutput = CompressByEntropy(original);
        var signaturesOutput = BuildSignaturesView(source.Value);
        var mapOutput = BuildMapView(source.Value);
        var referenceOutput = BuildReferenceView(source.Value, originalTokens, originalLines);

        var modes = new Dictionary<string, object>
        {
            ["map"] = BuildCompressionModePayload(mapOutput, originalTokens),
            ["signatures"] = BuildCompressionModePayload(signaturesOutput, originalTokens),
            ["reference"] = BuildCompressionModePayload(referenceOutput, originalTokens),
            ["aggressive"] = BuildCompressionModePayload(aggressiveOutput, originalTokens),
            ["entropy"] = BuildCompressionModePayload(entropyOutput, originalTokens),
        };

        if (!string.IsNullOrWhiteSpace(task))
        {
            modes["task"] = BuildCompressionModePayload(BuildTaskAwareView(source.Value, task), originalTokens);
        }

        return new
        {
            path = source.Value.Path,
            task,
            original,
            original_tokens = originalTokens,
            original_lines = originalLines,
            modes,
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
    /// Builds learned threshold suggestions from observed telemetry and language distribution.
    /// </summary>
    /// <param name="projects">Known projects.</param>
    /// <param name="telemetry">Current telemetry snapshot.</param>
    /// <returns>Language-keyed threshold payload.</returns>
    private static Dictionary<string, object> BuildLearnedThresholds(IReadOnlyList<ProjectRecord> projects, TelemetryStore.Snapshot telemetry)
    {
        var compressionRate = telemetry.TotalInputTokens > 0
            ? (double)Math.Max(0, telemetry.TotalInputTokens - telemetry.TotalOutputTokens) / telemetry.TotalInputTokens
            : 0;

        return AggregateLanguageCounts(projects)
            .ToDictionary(
                language => language.Key,
                language => (object)new
                {
                    entropy_threshold = Math.Round(Math.Clamp(0.58 + (compressionRate * 0.18), 0.45, 0.92), 3),
                    jaccard_threshold = Math.Round(Math.Clamp(0.32 + (compressionRate * 0.12), 0.20, 0.78), 3),
                },
                StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Builds the command statistics payload used by overview and live dashboard charts.
    /// </summary>
    /// <param name="tools">Registered tool definitions.</param>
    /// <param name="telemetry">Current telemetry snapshot.</param>
    /// <returns>Command payload dictionary keyed by tool name.</returns>
    private static Dictionary<string, DashboardCommandPayload> BuildCommandPayloads(IReadOnlyList<NebuCtx.Contracts.Mcp.ToolDefinition> tools, TelemetryStore.Snapshot telemetry)
    {
        var commands = tools.ToDictionary(
            tool => tool.Name,
            tool => new DashboardCommandPayload
            {
                Source = tool.Name.StartsWith("ctx_", StringComparison.OrdinalIgnoreCase) ? "mcp" : "tool",
                Count = 0,
                InputTokens = 0L,
                OutputTokens = 0L,
            },
            StringComparer.OrdinalIgnoreCase);

        foreach (var command in telemetry.Commands.Values)
        {
            commands[command.Name] = new DashboardCommandPayload
            {
                Source = command.Source,
                Count = command.Count,
                InputTokens = command.InputTokens,
                OutputTokens = command.OutputTokens,
            };
        }

        return commands;
    }

    /// <summary>
    /// Creates one dashboard domain group.
    /// </summary>
    /// <param name="id">Stable domain identifier.</param>
    /// <param name="label">Domain display label.</param>
    /// <param name="description">Domain description.</param>
    /// <param name="views">Views assigned to the domain.</param>
    /// <returns>Domain payload.</returns>
    private static DashboardDomainPayload CreateDomain(string id, string label, string description, params (string Id, string Label)[] views)
    {
        return new DashboardDomainPayload
        {
            Id = id,
            Label = label,
            Description = description,
            Views = views.Select(view => new DashboardDomainViewPayload { Id = view.Id, Label = view.Label }).ToArray(),
        };
    }

    /// <summary>
    /// Estimates saved cost from input and output token counts using the dashboard cost constants.
    /// </summary>
    /// <param name="inputTokens">Estimated input tokens.</param>
    /// <param name="outputTokens">Estimated output tokens.</param>
    /// <returns>Approximate USD saved.</returns>
    private static decimal EstimateSavedCost(long inputTokens, long outputTokens)
    {
        var savedTokens = Math.Max(0, inputTokens - outputTokens);
        return Math.Round(savedTokens / 1_000_000m * 2.50m, 4);
    }

    /// <summary>
    /// Estimates context-pressure utilization from total output volume.
    /// </summary>
    /// <param name="totalOutputTokens">Total output tokens observed.</param>
    /// <returns>Normalized utilization in the 0-1 range.</returns>
    private static double CalculatePressureUtilization(long totalOutputTokens)
    {
        return Math.Clamp(totalOutputTokens / 200_000d, 0, 1);
    }

    /// <summary>
    /// Maps output volume to a coarse context-pressure recommendation.
    /// </summary>
    /// <param name="totalOutputTokens">Total output tokens observed.</param>
    /// <returns>Recommendation label used by the dashboard.</returns>
    private static string RecommendPressureMode(long totalOutputTokens)
    {
        if (totalOutputTokens >= 160_000)
        {
            return "ForceCompression";
        }

        if (totalOutputTokens >= 100_000)
        {
            return "SuggestCompression";
        }

        return "NoAction";
    }

    /// <summary>
    /// Estimates a pseudo-token count for a project summary node.
    /// </summary>
    /// <param name="project">Project record.</param>
    /// <returns>Approximate token count for dashboard sizing.</returns>
    private static long EstimateProjectTokenCount(ProjectRecord project)
    {
        var metadata = project.ProjectMetadata?.Summary;
        if (metadata is null)
        {
            return 0;
        }

        return Math.Max(1, metadata.SourceFileCount) * 64;
    }

    /// <summary>
    /// Aggregates persisted language counts across all known projects.
    /// </summary>
    /// <param name="projects">Registered projects.</param>
    /// <returns>Language distribution for dashboard charts.</returns>
    private static Dictionary<string, long> AggregateLanguageCounts(IReadOnlyList<ProjectRecord> projects)
    {
        return projects
            .Where(project => project.ProjectMetadata is not null)
            .SelectMany(project => project.ProjectMetadata!.Summary.Languages)
            .GroupBy(language => language.Language, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Sum(item => item.FileCount), StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Expands persisted project metadata into dashboard knowledge facts.
    /// </summary>
    /// <param name="project">Project record.</param>
    /// <returns>Knowledge facts derived from compact project metadata.</returns>
    private static IEnumerable<Dictionary<string, object?>> BuildProjectFacts(ProjectRecord project)
    {
        if (project.ProjectMetadata?.Summary is not { } summary)
        {
            yield break;
        }

        yield return CreateKnowledgeFact(project, "architecture:project", "Project Name", $"project:{project.ProjectId}:slug", project.Slug, 0.98);

        if (project.Fingerprint?.RepoName is { Length: > 0 } repoName)
        {
            yield return CreateKnowledgeFact(project, "architecture:repository", "Repository Name", $"project:{project.ProjectId}:repository", repoName, 0.96);
        }

        if (project.Fingerprint?.Host is { Length: > 0 } host)
        {
            yield return CreateKnowledgeFact(project, "architecture:repository", "Repository Host", $"project:{project.ProjectId}:host", host, 0.94);
        }

        if (project.Fingerprint?.DefaultBranch is { Length: > 0 } defaultBranch)
        {
            yield return CreateKnowledgeFact(project, "workflow:branch", "Default Branch", $"project:{project.ProjectId}:default-branch", defaultBranch, 0.9);
        }

        yield return CreateKnowledgeFact(project, "architecture:files", "File Inventory", $"project:{project.ProjectId}:source-files", $"{summary.SourceFileCount} source files across {summary.TotalFileCount} total files", 0.93);

        yield return CreateKnowledgeFact(project, "architecture:files", "Marker Count", $"project:{project.ProjectId}:marker-count", $"{summary.Markers.Count} repository markers detected", 0.87);

        foreach (var marker in summary.Markers)
        {
            yield return CreateKnowledgeFact(project, "architecture:marker", $"Marker {marker}", $"project:{project.ProjectId}:marker:{marker}", marker, 0.9);
        }

        foreach (var language in summary.Languages)
        {
            yield return CreateKnowledgeFact(project, "architecture:language", $"Language {language.Language}", $"project:{project.ProjectId}:language:{language.Language}", $"{language.FileCount} files", 0.88);
        }

        if (summary.Languages.OrderByDescending(language => language.FileCount).FirstOrDefault() is { } dominantLanguage)
        {
            yield return CreateKnowledgeFact(project, "architecture:language", "Primary Language", $"project:{project.ProjectId}:primary-language", dominantLanguage.Language, 0.91);
        }
    }

    /// <summary>
    /// Creates a dashboard knowledge fact with stable project and human-readable labels.
    /// </summary>
    /// <param name="project">Owning project.</param>
    /// <param name="category">Knowledge category.</param>
    /// <param name="factName">Human-readable fact label.</param>
    /// <param name="key">Stable fact key.</param>
    /// <param name="value">Fact value.</param>
    /// <param name="confidence">Fact confidence score.</param>
    /// <returns>Dashboard fact dictionary.</returns>
    private static Dictionary<string, object?> CreateKnowledgeFact(ProjectRecord project, string category, string factName, string key, object? value, double confidence)
    {
        return new Dictionary<string, object?>
        {
            ["category"] = category,
            ["fact_name"] = factName,
            ["key"] = key,
            ["value"] = value,
            ["confidence"] = confidence,
            ["project_id"] = project.ProjectId,
            ["project_name"] = project.Slug,
        };
    }

    /// <summary>
    /// Builds stable demo facts so the dashboard graph has enough shape during local iteration.
    /// </summary>
    /// <param name="projects">Registered projects.</param>
    /// <returns>Demo facts grouped by project.</returns>
    private static IEnumerable<Dictionary<string, object?>> BuildDemoKnowledgeFacts(IReadOnlyList<ProjectRecord> projects)
    {
        foreach (var project in projects.Where(project => project.ProjectMetadata?.Summary is not null))
        {
            foreach (var fact in CreateDemoFacts(project))
            {
                yield return fact;
            }
        }
    }

    /// <summary>
    /// Creates a small deterministic demo fact pack for one project.
    /// </summary>
    /// <param name="project">Owning project.</param>
    /// <returns>Demo facts used to shape the graph visually.</returns>
    private static IEnumerable<Dictionary<string, object?>> CreateDemoFacts(ProjectRecord project)
    {
        yield return CreateDemoKnowledgeFact(project, "workflow:demo", "Demo Fact Layout", "Project facts are grouped under a visible project hub so the graph reads by product area first.", 0.74);
        yield return CreateDemoKnowledgeFact(project, "testing:demo", "Knowledge Graph Review", "Use these seeded facts to tune spacing, labels, and detail panel readability before wiring richer sources.", 0.71);
        yield return CreateDemoKnowledgeFact(project, "architecture:demo", "Fact Labels", "Readable fact names should be short, scannable, and distinct from raw storage keys.", 0.77);
    }

    /// <summary>
    /// Creates a single demo fact dictionary.
    /// </summary>
    /// <param name="project">Owning project.</param>
    /// <param name="category">Fact category.</param>
    /// <param name="factName">Fact name shown in the dashboard.</param>
    /// <param name="value">Fact value text.</param>
    /// <param name="confidence">Demo confidence value.</param>
    /// <returns>Dashboard fact dictionary.</returns>
    private static Dictionary<string, object?> CreateDemoKnowledgeFact(ProjectRecord project, string category, string factName, string value, double confidence)
    {
        var fact = CreateKnowledgeFact(project, category, factName, $"project:{project.ProjectId}:demo:{factName.ToLowerInvariant().Replace(' ', '-')}", value, confidence);
        fact["source"] = "demo";
        fact["is_demo"] = true;
        return fact;
    }

    /// <summary>
    /// Removes repeated knowledge facts for the same project and readable fact label.
    /// </summary>
    /// <param name="facts">Knowledge facts to normalize.</param>
    /// <returns>Deduplicated fact dictionaries.</returns>
    private static IEnumerable<Dictionary<string, object?>> DeduplicateKnowledgeFacts(IEnumerable<Dictionary<string, object?>> facts)
    {
        return facts
            .GroupBy(fact => string.Join(
                '|',
                fact.GetValueOrDefault("project_id")?.ToString() ?? string.Empty,
                fact.GetValueOrDefault("category")?.ToString() ?? string.Empty,
                fact.GetValueOrDefault("fact_name")?.ToString() ?? string.Empty,
                fact.GetValueOrDefault("value")?.ToString() ?? string.Empty), StringComparer.OrdinalIgnoreCase)
            .Select(group => group
                .OrderByDescending(fact => Convert.ToDouble(fact["confidence"], CultureInfo.InvariantCulture))
                .First());
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
    /// Infers a lightweight language label from the source file path.
    /// </summary>
    /// <param name="filePath">Source file path.</param>
    /// <returns>Language label for dashboard grouping.</returns>
    private static string InferLanguage(string filePath)
    {
        if (filePath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
        {
            return "csharp";
        }

        if (filePath.EndsWith(".html", StringComparison.OrdinalIgnoreCase))
        {
            return "html";
        }

        if (filePath.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
            || filePath.EndsWith(".md", StringComparison.OrdinalIgnoreCase)
            || filePath.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
        {
            return "text";
        }

        return "unknown";
    }

    /// <summary>
    /// Resolves a human-friendly project label from the known project list.
    /// </summary>
    /// <param name="projects">Known projects.</param>
    /// <param name="projectId">Project identifier.</param>
    /// <returns>Project slug when found, otherwise the raw project identifier.</returns>
    private static string ResolveProjectLabel(IReadOnlyList<ProjectRecord> projects, string projectId)
    {
        return projects.FirstOrDefault(project => string.Equals(project.ProjectId, projectId, StringComparison.OrdinalIgnoreCase))?.Slug ?? projectId;
    }

    /// <summary>
    /// Resolves the buddy rarity tier from telemetry volume.
    /// </summary>
    /// <param name="totalSaved">Total saved tokens.</param>
    /// <param name="totalToolCalls">Total tool calls.</param>
    /// <returns>Dashboard rarity label.</returns>
    private static string ResolveBuddyRarity(long totalSaved, int totalToolCalls)
    {
        if (totalSaved >= 10_000 || totalToolCalls >= 50)
        {
            return "Legendary";
        }

        if (totalSaved >= 5_000 || totalToolCalls >= 25)
        {
            return "Epic";
        }

        if (totalSaved >= 1_000 || totalToolCalls >= 10)
        {
            return "Rare";
        }

        return "Uncommon";
    }

    /// <summary>
    /// Resolves the buddy mood from compression effectiveness and activity level.
    /// </summary>
    /// <param name="compressionRate">Current compression rate.</param>
    /// <param name="totalToolCalls">Total tool calls.</param>
    /// <returns>Dashboard mood label.</returns>
    private static string ResolveBuddyMood(double compressionRate, int totalToolCalls)
    {
        if (compressionRate >= 0.65)
        {
            return "Ecstatic";
        }

        if (compressionRate >= 0.4)
        {
            return "Happy";
        }

        if (totalToolCalls >= 1)
        {
            return "Content";
        }

        return "Sleeping";
    }

    /// <summary>
    /// Builds the buddy speech string from telemetry state.
    /// </summary>
    /// <param name="mood">Resolved mood label.</param>
    /// <param name="topLanguage">Most common language in the project set.</param>
    /// <param name="telemetry">Current telemetry snapshot.</param>
    /// <returns>Buddy speech line.</returns>
    private static string ResolveBuddySpeech(string mood, string topLanguage, TelemetryStore.Snapshot telemetry)
    {
        return mood switch
        {
            "Ecstatic" => $"Compression is flying. Keep the {topLanguage} context tight and I will keep saving tokens.",
            "Happy" => $"Good rhythm so far. {telemetry.TotalToolCalls} calls in and the dashboard is staying lean.",
            "Content" => $"I am watching the live flow across {telemetry.Sessions.Count} active client sessions.",
            _ => "Wake me up with a few tool calls and I will start learning your project habits.",
        };
    }

    /// <summary>
    /// Infers a high-level task type from the latest tool name.
    /// </summary>
    /// <param name="toolName">Latest tool name.</param>
    /// <returns>Intent task type label.</returns>
    private static string InferIntentTaskType(string toolName)
    {
        if (toolName.Contains("search", StringComparison.OrdinalIgnoreCase))
        {
            return "search";
        }

        if (toolName.Contains("read", StringComparison.OrdinalIgnoreCase)
            || toolName.Contains("tree", StringComparison.OrdinalIgnoreCase)
            || toolName.Contains("outline", StringComparison.OrdinalIgnoreCase)
            || toolName.Contains("symbol", StringComparison.OrdinalIgnoreCase))
        {
            return "code-navigation";
        }

        if (toolName.Contains("brain", StringComparison.OrdinalIgnoreCase))
        {
            return "memory";
        }

        if (toolName.Contains("route", StringComparison.OrdinalIgnoreCase))
        {
            return "routing";
        }

        return "tool-execution";
    }

    /// <summary>
    /// Resolves a synthetic compression source from dashboard-available route, tool, or project metadata.
    /// </summary>
    /// <param name="path">Selected path.</param>
    /// <param name="toolRegistry">Tool registry.</param>
    /// <param name="projects">Known projects.</param>
    /// <returns>Compression source tuple or null.</returns>
    private static (string Path, string Language, string Original, string[] Highlights)? ResolveCompressionSource(string path, ToolRegistry toolRegistry, IReadOnlyList<ProjectRecord> projects)
    {
        if (string.Equals(path, "NebuCtx.Tools", StringComparison.OrdinalIgnoreCase))
        {
        var lines = toolRegistry.GetRegisteredTools().Tools.Select(tool => $"tool {tool.Name} => {tool.Description}").ToArray();
        return (path, "csharp", string.Join(Environment.NewLine, lines), toolRegistry.GetRegisteredTools().Tools.Select(tool => tool.Name).Take(8).ToArray());
        }

        var matchingRoutes = RouteCatalog.GetAll()
            .Where(route => string.Equals(route.File, path, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (matchingRoutes.Length > 0)
        {
            var original = string.Join(Environment.NewLine, matchingRoutes.Select(route => $"{route.Method} {route.Path} handled by {route.Handler} ({route.File}:{route.Line})"));
            return (path, InferLanguage(path), original, matchingRoutes.Select(route => route.Handler).Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
        }

        if (path.StartsWith("project/", StringComparison.OrdinalIgnoreCase))
        {
            var projectKey = path["project/".Length..];
            var project = projects.FirstOrDefault(item => string.Equals(item.Slug, projectKey, StringComparison.OrdinalIgnoreCase)
                || string.Equals(item.ProjectId, projectKey, StringComparison.OrdinalIgnoreCase));
            if (project?.ProjectMetadata?.Summary is { } summary)
            {
                var original = string.Join(Environment.NewLine, new[]
                {
                    $"project {project.Slug} ({project.ProjectId})",
                    $"source files: {summary.SourceFileCount}",
                    $"total files: {summary.TotalFileCount}",
                    $"markers: {string.Join(", ", summary.Markers)}",
                    $"languages: {string.Join(", ", summary.Languages.Select(language => $"{language.Language}={language.FileCount}"))}",
                });
                return (path, summary.Languages.FirstOrDefault()?.Language ?? "text", original, summary.Markers.Concat(summary.Languages.Select(language => language.Language)).ToArray());
            }
        }

        return null;
    }

    /// <summary>
    /// Builds the map-mode compression view.
    /// </summary>
    /// <param name="source">Compression source.</param>
    /// <returns>Map-mode output.</returns>
    private static string BuildMapView((string Path, string Language, string Original, string[] Highlights) source)
    {
        return string.Join(Environment.NewLine, new[]
        {
            $"path: {source.Path}",
            $"language: {source.Language}",
            "highlights:",
        }.Concat(source.Highlights.Select(highlight => $"- {highlight}")));
    }

    /// <summary>
    /// Builds the signatures-mode compression view.
    /// </summary>
    /// <param name="source">Compression source.</param>
    /// <returns>Signature-oriented output.</returns>
    private static string BuildSignaturesView((string Path, string Language, string Original, string[] Highlights) source)
    {
        return string.Join(Environment.NewLine, source.Original
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Where(line => line.Contains("=>", StringComparison.OrdinalIgnoreCase)
                || line.Contains("handled by", StringComparison.OrdinalIgnoreCase)
                || line.Contains("tool ", StringComparison.OrdinalIgnoreCase)
                || line.Contains("project ", StringComparison.OrdinalIgnoreCase))
            .Take(12));
    }

    /// <summary>
    /// Builds the reference-mode compression view.
    /// </summary>
    /// <param name="source">Compression source.</param>
    /// <param name="originalTokens">Original token count.</param>
    /// <param name="originalLines">Original line count.</param>
    /// <returns>Reference-oriented output.</returns>
    private static string BuildReferenceView((string Path, string Language, string Original, string[] Highlights) source, int originalTokens, int originalLines)
    {
        return string.Join(Environment.NewLine, new[]
        {
            $"ref: {source.Path}",
            $"language: {source.Language}",
            $"tokens: {originalTokens}",
            $"lines: {originalLines}",
            $"top: {string.Join(", ", source.Highlights.Take(4))}",
        });
    }

    /// <summary>
    /// Builds the task-aware compression view.
    /// </summary>
    /// <param name="source">Compression source.</param>
    /// <param name="task">Task hint.</param>
    /// <returns>Task-filtered output.</returns>
    private static string BuildTaskAwareView((string Path, string Language, string Original, string[] Highlights) source, string task)
    {
        var taskTerms = task.Split([' ', ',', ';', ':'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var selectedLines = source.Original
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Where(line => taskTerms.Any(term => line.Contains(term, StringComparison.OrdinalIgnoreCase)))
            .Take(10)
            .ToArray();

        if (selectedLines.Length == 0)
        {
            selectedLines = source.Original.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).Take(8).ToArray();
        }

        return string.Join(Environment.NewLine, new[] { $"task: {task}" }.Concat(selectedLines));
    }

    /// <summary>
    /// Builds a compact compression-mode payload.
    /// </summary>
    /// <param name="output">Compressed output.</param>
    /// <param name="originalTokens">Original token count.</param>
    /// <returns>Mode payload.</returns>
    private static object BuildCompressionModePayload(string output, int originalTokens)
    {
        var tokens = EstimateTokenCount(output);
        var saved = Math.Max(0, originalTokens - tokens);
        var savingsPercentage = originalTokens > 0 ? (int)Math.Round((double)saved / originalTokens * 100) : 0;
        return new
        {
            tokens,
            savings_pct = savingsPercentage,
            output,
        };
    }

    /// <summary>
    /// Performs a whitespace- and blank-line-stripping compression pass.
    /// </summary>
    /// <param name="original">Original source text.</param>
    /// <returns>Aggressively compacted output.</returns>
    private static string CompressAggressively(string original)
    {
        return string.Join(Environment.NewLine, original
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .Where(line => !string.IsNullOrWhiteSpace(line)));
    }

    /// <summary>
    /// Performs a simple entropy-style reduction by keeping the most information-dense lines.
    /// </summary>
    /// <param name="original">Original source text.</param>
    /// <returns>Reduced output.</returns>
    private static string CompressByEntropy(string original)
    {
        var candidateLines = original
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .Where(line => line.Length >= 16 || line.Contains("=>", StringComparison.OrdinalIgnoreCase) || line.Contains(':'))
            .Distinct(StringComparer.Ordinal)
            .Take(10)
            .ToArray();

        return string.Join(Environment.NewLine, candidateLines);
    }

    /// <summary>
    /// Counts the number of logical lines in a payload.
    /// </summary>
    /// <param name="text">Payload text.</param>
    /// <returns>Line count.</returns>
    private static int CountLines(string text)
    {
        return string.IsNullOrEmpty(text)
            ? 0
            : text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).Length;
    }

}
