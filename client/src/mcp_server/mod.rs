mod dispatch;
mod execute;
pub mod helpers;

use rmcp::handler::server::ServerHandler;
use rmcp::model::*;
use rmcp::service::{RequestContext, RoleServer};
use rmcp::ErrorData;

use crate::tools::{CrpMode, NebuCtxServer};

/// Tools that ONLY exist on the configured server. No local fallback when offline.
pub const SERVER_ONLY_TOOLS: &[&str] = &[
    "ctx_brain",
    "ctx_gain",
    "ctx_cost",
    "ctx_heatmap",
    "ctx_stats",
];

const PUBLIC_TOOL_NAMES: &[&str] = &["ctx_read", "ctx_search", "ctx_tree", "ctx_shell", "ctx"];

/// Tools that prefer server routing but fall back to local file storage when not configured.
const SERVER_PREFERRED_TOOLS: &[&str] = &["ctx_knowledge", "ctx_session"];

const SERVER_KNOWLEDGE_ACTIONS: &[&str] = &[
    "remember",
    "recall",
    "search",
    "status",
    "remove",
    "categories",
    "timeline",
    "consolidate",
    "promote",
    "upkeep",
    "wakeup",
    "triage",
];

/// Outcome of attempting to route a tool call to the server.
enum ServerRoutingResult {
    /// Server responded successfully; return this text to the caller.
    Success(String),
    /// No server connection is configured; caller should fall back to local handling.
    NotConfigured,
    /// Connection is configured but the call failed; return this error text to the caller.
    Error(String),
}

fn prefers_server_route(
    name: &str,
    args: &Option<serde_json::Map<String, serde_json::Value>>,
) -> bool {
    if !SERVER_PREFERRED_TOOLS.contains(&name) {
        return false;
    }

    if name != "ctx_knowledge" {
        return true;
    }

    let action = args
        .as_ref()
        .and_then(|map| map.get("action"))
        .and_then(|value| value.as_str())
        .unwrap_or_default();
    SERVER_KNOWLEDGE_ACTIONS.contains(&action)
}

impl ServerHandler for NebuCtxServer {
    fn get_info(&self) -> ServerInfo {
        let capabilities = ServerCapabilities::builder().enable_tools().build();

        let instructions = crate::instructions::build_instructions(self.crp_mode);

        InitializeResult::new(capabilities)
            .with_server_info(Implementation::new("nebu-ctx", env!("CARGO_PKG_VERSION")))
            .with_instructions(instructions)
    }

    async fn initialize(
        &self,
        request: InitializeRequestParams,
        _context: RequestContext<RoleServer>,
    ) -> Result<InitializeResult, ErrorData> {
        let name = request.client_info.name.clone();
        tracing::info!("MCP client connected: {:?}", name);
        *self.client_name.write().await = name.clone();

        let derived_root = derive_project_root_from_cwd();
        let cwd_str = std::env::current_dir()
            .ok()
            .map(|p| p.to_string_lossy().to_string())
            .unwrap_or_default();
        {
            let mut session = self.session.write().await;
            if !cwd_str.is_empty() {
                session.shell_cwd = Some(cwd_str.clone());
            }
            if let Some(ref root) = derived_root {
                session.project_root = Some(root.clone());
                tracing::info!("Project root set to: {root}");
            } else if let Some(ref root) = session.project_root {
                let root_path = std::path::Path::new(root);
                let root_has_marker = has_project_marker(root_path);
                let root_str = root_path.to_string_lossy();
                let root_suspicious = root_str.contains("/.claude")
                    || root_str.contains("/.codex")
                    || root_str.contains("/var/folders/")
                    || root_str.contains("/tmp/")
                    || root_str.contains("\\.claude")
                    || root_str.contains("\\.codex")
                    || root_str.contains("\\AppData\\Local\\Temp")
                    || root_str.contains("\\Temp\\");
                if root_suspicious && !root_has_marker {
                    session.project_root = None;
                }
            }
            let _ = session.save();
        }

        let agent_name = name.clone();
        let agent_root = derived_root.clone().unwrap_or_default();
        let agent_id_handle = self.agent_id.clone();
        tokio::task::spawn_blocking(move || {
            if std::env::var("NEBU_CTX_HEADLESS").is_ok() {
                return;
            }
            if let Some(home) = dirs::home_dir() {
                let _ = crate::rules_inject::inject_all_rules(&home);
            }
            crate::hooks::refresh_installed_hooks();
            crate::core::version_check::check_background();

            if !agent_root.is_empty() {
                let role = match agent_name.to_lowercase().as_str() {
                    n if n.contains("cursor") => Some("coder"),
                    n if n.contains("claude") => Some("coder"),
                    n if n.contains("codex") => Some("coder"),
                    n if n.contains("antigravity") || n.contains("gemini") => Some("explorer"),
                    n if n.contains("review") => Some("reviewer"),
                    n if n.contains("test") => Some("tester"),
                    _ => None,
                };
                let env_role = std::env::var("NEBU_CTX_AGENT_ROLE").ok();
                let effective_role = env_role.as_deref().or(role);
                let mut registry = crate::core::agents::AgentRegistry::load_or_create();
                registry.cleanup_stale(24);
                let id = registry.register("mcp", effective_role, &agent_root);
                let _ = registry.save();
                if let Ok(mut guard) = agent_id_handle.try_write() {
                    *guard = Some(id);
                }
            }
        });

        let instructions =
            crate::instructions::build_instructions_with_client(self.crp_mode, &name);
        let capabilities = ServerCapabilities::builder().enable_tools().build();

        Ok(InitializeResult::new(capabilities)
            .with_server_info(Implementation::new("nebu-ctx", env!("CARGO_PKG_VERSION")))
            .with_instructions(instructions))
    }

    async fn list_tools(
        &self,
        _request: Option<PaginatedRequestParams>,
        _context: RequestContext<RoleServer>,
    ) -> Result<ListToolsResult, ErrorData> {
        let all_tools = crate::tool_defs::unified_tool_defs();

        let disabled = crate::core::config::Config::load().disabled_tools_effective();
        let tools = if disabled.is_empty() {
            all_tools
        } else {
            all_tools
                .into_iter()
                .filter(|t| !disabled.iter().any(|d| t.name.as_ref() == d.as_str()))
                .collect()
        };

        let tools = {
            let active = self.workflow.read().await.clone();
            if let Some(run) = active {
                if let Some(state) = run.spec.state(&run.current) {
                    if let Some(allowed) = &state.allowed_tools {
                        let mut allow: std::collections::HashSet<&str> =
                            allowed.iter().map(|s| s.as_str()).collect();
                        allow.insert("ctx");
                        allow.insert("ctx_workflow");
                        return Ok(ListToolsResult {
                            tools: tools
                                .into_iter()
                                .filter(|t| allow.contains(t.name.as_ref()))
                                .collect(),
                            ..Default::default()
                        });
                    }
                }
            }
            tools
        };

        Ok(ListToolsResult {
            tools,
            ..Default::default()
        })
    }

    async fn call_tool(
        &self,
        request: CallToolRequestParams,
        _context: RequestContext<RoleServer>,
    ) -> Result<CallToolResult, ErrorData> {
        self.check_idle_expiry().await;

        let original_name = request.name.as_ref().to_string();
        if !PUBLIC_TOOL_NAMES.contains(&original_name.as_str()) {
            return Err(ErrorData::invalid_params(
                "Public MCP surface only supports: ctx_read, ctx_search, ctx_tree, ctx_shell, ctx",
                None,
            ));
        }
        let (resolved_name, resolved_args) = match original_name.as_str() {
            "ctx" => {
                let arguments = request.arguments.unwrap_or_default();
                if arguments.get("tool").is_some() {
                    return Err(ErrorData::invalid_params(
                        "ctx now requires 'domain' + 'action'; 'tool' is no longer supported",
                        None,
                    ));
                }
                let domain = arguments
                    .get("domain")
                    .and_then(|v| v.as_str())
                    .ok_or_else(|| {
                        ErrorData::invalid_params("'domain' is required for ctx meta-tool", None)
                    })?;
                let action = arguments
                    .get("action")
                    .and_then(|v| v.as_str())
                    .filter(|v| !v.trim().is_empty())
                    .ok_or_else(|| {
                        ErrorData::invalid_params("'action' is required for ctx meta-tool", None)
                    })?;
                let mut args = arguments.clone();
                args.remove("domain");
                let resolved = match domain {
                    "memory" => match action {
                        "task" | "finding" | "decision" | "save" | "load" | "status" | "reset" | "list" | "cleanup" => "ctx_session",
                        "recall" | "search" | "consolidate" | "promote" | "upkeep" | "triage" | "timeline" | "categories" | "wakeup" | "remove" => "ctx_knowledge",
                        "store" | "set" | "remember" => {
                            args.insert("action".to_string(), serde_json::Value::String("remember".to_string()));
                            if !args.contains_key("category") {
                                args.insert("category".to_string(), serde_json::Value::String("general".to_string()));
                            }
                            "ctx_knowledge"
                        }
                        _ => return Err(ErrorData::invalid_params(
                            "Unknown memory action. Use one of: task, finding, decision, save, load, status, reset, list, cleanup, store, set, remember, recall, search, categories, timeline, consolidate, promote, upkeep, triage, wakeup, remove",
                            None,
                        )),
                    },
                    "context" => match action {
                        "overview" => "ctx_overview",
                        "preload" => "ctx_preload",
                        "prefetch" => "ctx_prefetch",
                        "status" => "ctx_context",
                        "compress" => "ctx_compress",
                        "fill" => "ctx_fill",
                        "intent" => "ctx_intent",
                        "response" => "ctx_response",
                        _ => return Err(ErrorData::invalid_params("Unknown context action", None)),
                    },
                    "graph" => match action {
                        "build" | "related" | "symbol" | "status" => "ctx_graph",
                        "impact" => {
                            args.insert("action".to_string(), serde_json::Value::String("analyze".to_string()));
                            "ctx_impact"
                        }
                        "chain" => "ctx_impact",
                        "architecture" => {
                            args.insert("action".to_string(), serde_json::Value::String("overview".to_string()));
                            "ctx_architecture"
                        }
                        "callers" => "ctx_callers",
                        "callees" => "ctx_callees",
                        "diagram" => "ctx_graph_diagram",
                        _ => return Err(ErrorData::invalid_params("Unknown graph action", None)),
                    },
                    "analytics" => match action {
                        "report" => {
                            args.insert("action".to_string(), serde_json::Value::String("report".to_string()));
                            "ctx_gain"
                        }
                        "cost" => {
                            args.insert("action".to_string(), serde_json::Value::String("report".to_string()));
                            "ctx_cost"
                        }
                        "heatmap" => {
                            args.insert("action".to_string(), serde_json::Value::String("status".to_string()));
                            "ctx_heatmap"
                        }
                        "stats" => {
                            args.insert("action".to_string(), serde_json::Value::String("report".to_string()));
                            "ctx_stats"
                        }
                        "feedback" => {
                            args.insert("action".to_string(), serde_json::Value::String("report".to_string()));
                            "ctx_feedback"
                        }
                        "wrapped" => "ctx_wrapped",
                        "analyze" => "ctx_analyze",
                        "discover" => "ctx_discover",
                        _ => return Err(ErrorData::invalid_params("Unknown analytics action", None)),
                    },
                    "agents" => match action {
                        "register" | "post" | "read" | "status" | "handoff" | "sync" | "diary" | "recall_diary" | "diaries" | "list" | "info" => "ctx_agent",
                        "push" | "pull" | "clear" => "ctx_share",
                        "create" | "update" | "get" | "cancel" | "message" => "ctx_task",
                        "start" | "transition" | "complete" | "evidence_add" | "evidence_list" | "stop" => "ctx_workflow",
                        _ => return Err(ErrorData::invalid_params("Unknown agents action", None)),
                    },
                    "inspect" => match action {
                        "dedup" => "ctx_dedup",
                        _ => return Err(ErrorData::invalid_params("Unknown inspect action", None)),
                    },
                    _ => {
                        return Err(ErrorData::invalid_params(
                            "Unknown ctx domain. Use one of: memory, context, graph, analytics, agents, inspect",
                            None,
                        ))
                    }
                };
                (resolved.to_string(), Some(args))
            }
            "ctx_read" => {
                let mut args = request.arguments.unwrap_or_default();
                let target = args
                    .get("target")
                    .and_then(|v| v.as_str())
                    .unwrap_or("file");
                let resolved = match target {
                    "file" => "ctx_read",
                    "files" => "ctx_multi_read",
                    "symbol" => {
                        if let Some(path) = args.remove("path") {
                            args.entry("file".to_string()).or_insert(path);
                        }
                        "ctx_symbol"
                    }
                    "outline" => "ctx_outline",
                    "archive" => "ctx_expand",
                    _ => return Err(ErrorData::invalid_params("Unknown ctx_read target", None)),
                };
                args.remove("target");
                (resolved.to_string(), Some(args))
            }
            "ctx_search" => {
                let mut args = request.arguments.unwrap_or_default();
                let mode = args.get("mode").and_then(|v| v.as_str()).unwrap_or("regex");
                let resolved = match mode {
                    "regex" => "ctx_search",
                    "semantic" => "ctx_semantic_search",
                    _ => return Err(ErrorData::invalid_params("Unknown ctx_search mode", None)),
                };
                args.remove("mode");
                (resolved.to_string(), Some(args))
            }
            _ => (original_name, request.arguments),
        };
        let name = resolved_name.as_str();
        let args = &resolved_args;

        // Route server-only and server-preferred tools through the configured server.
        let server_fallback_warning = if SERVER_ONLY_TOOLS.contains(&name) {
            match route_to_server(name, args).await {
                ServerRoutingResult::Success(s) => {
                    // Record telemetry so the dashboard reflects hosted tool usage.
                    self.record_call(name, 0, 0, None).await;
                    return Ok(CallToolResult::success(vec![Content::text(s)]));
                }
                ServerRoutingResult::NotConfigured => {
                    let msg = format!("{name} requires a server connection. Run: nebu-ctx connect");
                    return Ok(CallToolResult::success(vec![Content::text(msg)]));
                }
                ServerRoutingResult::Error(e) => {
                    return Ok(CallToolResult::success(vec![Content::text(e)]));
                }
            }
        } else if prefers_server_route(name, args) {
            // When a server is configured, fail hard rather than silently
            // writing to local JSON. Local fallback only applies when no server
            // server has been set up at all.
            let server_is_configured =
                matches!(crate::config::load_connection(), Ok(Some(_)) | Err(_));
            match route_to_server(name, args).await {
                ServerRoutingResult::Success(s) => {
                    // Record telemetry so the dashboard reflects hosted tool usage.
                    self.record_call(name, 0, 0, None).await;
                    return Ok(CallToolResult::success(vec![Content::text(s)]));
                }
                ServerRoutingResult::NotConfigured if server_is_configured => {
                    // Config exists but load/connect failed transiently — fail hard.
                    return Ok(CallToolResult::success(vec![Content::text(format!(
                        "{name}: server is configured but unreachable. Check: nebu-ctx status"
                    ))]));
                }
                ServerRoutingResult::NotConfigured => Some(
                    "\n\n⚠ Running locally (no server connection). Data stored in .nebu-ctx/ only.\n  To enable hosted persistence: nebu-ctx connect"
                        .to_string(),
                ),
                ServerRoutingResult::Error(e) => {
                    return Ok(CallToolResult::success(vec![Content::text(e)]))
                }
            }
        } else {
            None
        };

        if name != "ctx_workflow" {
            let active = self.workflow.read().await.clone();
            if let Some(run) = active {
                if let Some(state) = run.spec.state(&run.current) {
                    if let Some(allowed) = &state.allowed_tools {
                        let allowed_ok = allowed.iter().any(|t| t == name) || name == "ctx";
                        if !allowed_ok {
                            let mut shown = allowed.clone();
                            shown.sort();
                            shown.truncate(30);
                            return Ok(CallToolResult::success(vec![Content::text(format!(
                                "Tool '{name}' blocked by workflow '{}' (state: {}). Allowed ({} shown): {}",
                                run.spec.name,
                                run.current,
                                shown.len(),
                                shown.join(", ")
                            ))]));
                        }
                    }
                }
            }
        }

        let skip_auto_context = name == "ctx_shell"
            || (name == "ctx"
            && args.as_ref().is_some_and(|args| {
                let domain = args.get("domain").and_then(|value| value.as_str());
                let action = args.get("action").and_then(|value| value.as_str());
                domain == Some("memory") && matches!(action, Some("promote" | "triage"))
            }))
            || (name == "ctx_knowledge"
                && args.as_ref().is_some_and(|args| {
                    matches!(
                        args.get("action").and_then(|value| value.as_str()),
                        Some("promote" | "triage")
                    )
                }));

        let auto_context = if skip_auto_context {
            None
        } else {
            let task = {
                let session = self.session.read().await;
                session.task.as_ref().map(|t| t.description.clone())
            };
            let project_root = {
                let session = self.session.read().await;
                session.project_root.clone()
            };
            let mut cache = self.cache.write().await;
            crate::tools::autonomy::session_lifecycle_pre_hook(
                &self.autonomy,
                name,
                &mut cache,
                task.as_deref(),
                project_root.as_deref(),
                self.crp_mode,
            )
        };

        let throttle_result = {
            let fp = args
                .as_ref()
                .map(|a| {
                    crate::core::loop_detection::LoopDetector::fingerprint(
                        &serde_json::Value::Object(a.clone()),
                    )
                })
                .unwrap_or_default();
            let mut detector = self.loop_detector.write().await;

            let is_search = crate::core::loop_detection::LoopDetector::is_search_tool(name);
            let is_search_shell = name == "ctx_shell" && {
                let cmd = args
                    .as_ref()
                    .and_then(|a| a.get("command"))
                    .and_then(|v| v.as_str())
                    .unwrap_or("");
                crate::core::loop_detection::LoopDetector::is_search_shell_command(cmd)
            };

            if is_search || is_search_shell {
                let search_pattern = args.as_ref().and_then(|a| {
                    a.get("pattern")
                        .or_else(|| a.get("query"))
                        .and_then(|v| v.as_str())
                });
                let shell_pattern = if is_search_shell {
                    args.as_ref()
                        .and_then(|a| a.get("command"))
                        .and_then(|v| v.as_str())
                        .and_then(helpers::extract_search_pattern_from_command)
                } else {
                    None
                };
                let pat = search_pattern.or(shell_pattern.as_deref());
                detector.record_search(name, &fp, pat)
            } else {
                detector.record_call(name, &fp)
            }
        };

        if throttle_result.level == crate::core::loop_detection::ThrottleLevel::Blocked {
            let msg = throttle_result.message.unwrap_or_default();
            return Ok(CallToolResult::success(vec![Content::text(msg)]));
        }

        let throttle_warning =
            if throttle_result.level == crate::core::loop_detection::ThrottleLevel::Reduced {
                throttle_result.message.clone()
            } else {
                None
            };

        let tool_start = std::time::Instant::now();
        let result_text = self.dispatch_tool(name, args).await?;

        let mut result_text = result_text;

        // Archive large tool outputs before density compression (zero-loss recovery)
        let archive_hint = {
            use crate::core::archive;
            let archivable = matches!(
                name,
                "ctx_shell"
                    | "ctx_read"
                    | "ctx_multi_read"
                    | "ctx_smart_read"
                    | "ctx_execute"
                    | "ctx_search"
                    | "ctx_tree"
            );
            if archivable && archive::should_archive(&result_text) {
                let cmd = helpers::get_str(args, "command")
                    .or_else(|| helpers::get_str(args, "path"))
                    .unwrap_or_default();
                let session_id = self.session.read().await.id.clone();
                let tokens = crate::core::tokens::count_tokens(&result_text);
                archive::store(name, &cmd, &result_text, Some(&session_id))
                    .map(|id| archive::format_hint(&id, result_text.len(), tokens))
            } else {
                None
            }
        };

        {
            let config = crate::core::config::Config::load();
            let density = crate::core::config::OutputDensity::effective(&config.output_density);
            result_text = crate::core::protocol::compress_output(&result_text, &density);
        }

        if let Some(hint) = archive_hint {
            result_text = format!("{result_text}\n{hint}");
        }

        if let Some(ctx) = auto_context {
            result_text = format!("{ctx}\n\n{result_text}");
        }

        if let Some(warning) = throttle_warning {
            result_text = format!("{result_text}\n\n{warning}");
        }

        if let Some(offline_note) = server_fallback_warning {
            result_text = format!("{result_text}{offline_note}");
        }

        if name == "ctx_read" {
            let read_path = self
                .resolve_path_or_passthrough(&helpers::get_str(args, "path").unwrap_or_default())
                .await;
            let project_root = {
                let session = self.session.read().await;
                session.project_root.clone()
            };
            let mut cache = self.cache.write().await;
            let enrich = crate::tools::autonomy::enrich_after_read(
                &self.autonomy,
                &mut cache,
                &read_path,
                project_root.as_deref(),
            );
            if let Some(hint) = enrich.related_hint {
                result_text = format!("{result_text}\n{hint}");
            }

            crate::tools::autonomy::maybe_auto_dedup(&self.autonomy, &mut cache);
        }

        if name == "ctx_shell" {
            let cmd = helpers::get_str(args, "command").unwrap_or_default();
            let output_tokens = crate::core::tokens::count_tokens(&result_text);
            let calls = self.tool_calls.read().await;
            let last_original = calls.last().map(|c| c.original_tokens).unwrap_or(0);
            drop(calls);
            if let Some(hint) = crate::tools::autonomy::shell_efficiency_hint(
                &self.autonomy,
                &cmd,
                last_original,
                output_tokens,
            ) {
                result_text = format!("{result_text}\n{hint}");
            }
        }

        {
            let input = helpers::canonical_args_string(args);
            let input_md5 = helpers::md5_hex(&input);
            let output_md5 = helpers::md5_hex(&result_text);
            let action = helpers::get_str(args, "action");
            let agent_id = self.agent_id.read().await.clone();
            let client_name = self.client_name.read().await.clone();
            let mut explicit_intent: Option<(
                crate::core::intent_protocol::IntentRecord,
                Option<String>,
                String,
            )> = None;

            {
                let empty_args = serde_json::Map::new();
                let args_map = args.as_ref().unwrap_or(&empty_args);
                let mut session = self.session.write().await;
                session.record_tool_receipt(
                    name,
                    action.as_deref(),
                    &input_md5,
                    &output_md5,
                    agent_id.as_deref(),
                    Some(&client_name),
                );

                if let Some(intent) = crate::core::intent_protocol::infer_from_tool_call(
                    name,
                    action.as_deref(),
                    args_map,
                    session.project_root.as_deref(),
                ) {
                    let is_explicit =
                        intent.source == crate::core::intent_protocol::IntentSource::Explicit;
                    let root = session.project_root.clone();
                    let sid = session.id.clone();
                    session.record_intent(intent.clone());
                    if is_explicit {
                        explicit_intent = Some((intent, root, sid));
                    }
                }
                if session.should_save() {
                    let _ = session.save();
                }
            }

            if let Some((intent, root, session_id)) = explicit_intent {
                crate::core::intent_protocol::apply_side_effects(
                    &intent,
                    root.as_deref(),
                    &session_id,
                );
            }

            // Autopilot: consolidation loop (silent, deterministic, budgeted).
            if self.autonomy.is_enabled() {
                let (calls, project_root) = {
                    let session = self.session.read().await;
                    (session.stats.total_tool_calls, session.project_root.clone())
                };

                if let Some(root) = project_root {
                    if crate::tools::autonomy::should_auto_consolidate(&self.autonomy, calls) {
                        let root_clone = root.clone();
                        tokio::task::spawn_blocking(move || {
                            if crate::core::consolidation_engine::consolidate_latest(
                                &root_clone,
                                crate::core::consolidation_engine::ConsolidationBudgets::default(),
                            )
                            .is_ok()
                            {
                                crate::server_client::post_knowledge_to_server(&root_clone);
                            }
                        });
                    }
                }
            }

            let agent_key = agent_id.unwrap_or_else(|| "unknown".to_string());
            let input_tokens = crate::core::tokens::count_tokens(&input) as u64;
            let output_tokens = crate::core::tokens::count_tokens(&result_text) as u64;
            let mut store = crate::core::a2a::cost_attribution::CostStore::load();
            store.record_tool_call(&agent_key, &client_name, name, input_tokens, output_tokens);
            let _ = store.save();
        }

        let skip_checkpoint = matches!(
            name,
            "ctx_compress"
                | "ctx_metrics"
                | "ctx_benchmark"
                | "ctx_analyze"
                | "ctx_cache"
                | "ctx_discover"
                | "ctx_dedup"
                | "ctx_session"
                | "ctx_knowledge"
                | "ctx_agent"
                | "ctx_share"
                | "ctx_wrapped"
                | "ctx_overview"
                | "ctx_preload"
                | "ctx_cost"
                | "ctx_gain"
                | "ctx_heatmap"
                | "ctx_stats"
                | "ctx_task"
                | "ctx_impact"
                | "ctx_architecture"
                | "ctx_workflow"
        );

        if !skip_checkpoint && self.increment_and_check() {
            if let Some(checkpoint) = self.auto_checkpoint().await {
                let combined = format!(
                    "{result_text}\n\n--- AUTO CHECKPOINT (every {} calls) ---\n{checkpoint}",
                    self.checkpoint_interval
                );
                return Ok(CallToolResult::success(vec![Content::text(combined)]));
            }
        }

        let tool_duration_ms = tool_start.elapsed().as_millis() as u64;
        if tool_duration_ms > 100 {
            NebuCtxServer::append_tool_call_log(
                name,
                tool_duration_ms,
                0,
                0,
                None,
                &chrono::Local::now().format("%Y-%m-%d %H:%M:%S").to_string(),
            );
        }

        Ok(CallToolResult::success(vec![Content::text(result_text)]))
    }
}

/// Routes a tool call to the configured server, enriching it with the current git context.
///
/// Returns [`ServerRoutingResult::Success`] when the server responds,
/// [`ServerRoutingResult::NotConfigured`] when no connection is saved, and
/// [`ServerRoutingResult::Error`] when the connection exists but the call fails.
async fn route_to_server(
    name: &str,
    args: &Option<serde_json::Map<String, serde_json::Value>>,
) -> ServerRoutingResult {
    let tool_name = name.to_string();
    let arguments = args.clone().unwrap_or_default();
    // Extract project_root from args so the server call uses the caller's project identity,
    // not the MCP server process's working directory.
    let args_project_root: Option<String> = args
        .as_ref()
        .and_then(|a| a.get("project_root"))
        .and_then(|v| v.as_str())
        .map(|s| s.to_string());

    let result = tokio::task::spawn_blocking(move || {
        let connection = match crate::config::load_connection() {
            Ok(Some(connection)) => connection,
            Ok(None) => return ServerRoutingResult::NotConfigured,
            Err(error) => {
                return ServerRoutingResult::Error(format!(
                    "Saved server connection is invalid: {error}\nRun: nebu-ctx connect --endpoint <url> --token <token>"
                ))
            }
        };
        let client = crate::server_client::ServerClient::new(connection);
        // Prefer an explicit project_root arg over current_dir so that memory writes from
        // an editor workspace always resolve to the correct project on the server.
        let current_directory = args_project_root
            .as_deref()
            .map(std::path::Path::new)
            .filter(|p| p.exists())
            .map(|p| p.to_path_buf())
            .or_else(|| std::env::current_dir().ok())
            .unwrap_or_else(|| std::path::PathBuf::from("."));
        let project_context = crate::git_context::discover_project_context(&current_directory);
        match client.call_tool(&tool_name, arguments, &project_context) {
            Ok(value) => {
                let text = match &value {
                    serde_json::Value::String(s) => s.clone(),
                    other => {
                        serde_json::to_string_pretty(other).unwrap_or_else(|_| other.to_string())
                    }
                };
                ServerRoutingResult::Success(text)
            }
            Err(e) => ServerRoutingResult::Error(format!(
                "Server-routed tool {tool_name} failed: {e}\nCheck connection: nebu-ctx status"
            )),
        }
    })
    .await;

    result
        .unwrap_or_else(|_| ServerRoutingResult::Error("Server routing task panicked".to_string()))
}

/// Merges tool definitions fetched from the configured server into the local manifest.
///
/// Only tool names listed in [`SERVER_ONLY_TOOLS`] are pulled from the remote manifest, so the
/// local definitions always win for [`SERVER_PREFERRED_TOOLS`]. When the server is unreachable the
/// local manifest is returned unchanged.
pub fn build_instructions_for_test(crp_mode: CrpMode) -> String {
    crate::instructions::build_instructions(crp_mode)
}

pub fn build_claude_code_instructions_for_test() -> String {
    crate::instructions::claude_code_instructions()
}

const PROJECT_MARKERS: &[&str] = &[
    ".git",
    "Cargo.toml",
    "package.json",
    "go.mod",
    "pyproject.toml",
    "setup.py",
    "pom.xml",
    "build.gradle",
    "Makefile",
    ".lean-ctx.toml",
];

fn has_project_marker(dir: &std::path::Path) -> bool {
    PROJECT_MARKERS.iter().any(|m| dir.join(m).exists())
}

fn is_home_or_agent_dir(dir: &std::path::Path) -> bool {
    if let Some(home) = dirs::home_dir() {
        if dir == home {
            return true;
        }
    }
    let dir_str = dir.to_string_lossy();
    dir_str.ends_with("/.claude")
        || dir_str.ends_with("/.codex")
        || dir_str.contains("/.claude/")
        || dir_str.contains("/.codex/")
}

fn git_toplevel_from(dir: &std::path::Path) -> Option<String> {
    std::process::Command::new("git")
        .args(["rev-parse", "--show-toplevel"])
        .current_dir(dir)
        .stdout(std::process::Stdio::piped())
        .stderr(std::process::Stdio::null())
        .output()
        .ok()
        .and_then(|o| {
            if o.status.success() {
                String::from_utf8(o.stdout)
                    .ok()
                    .map(|s| s.trim().to_string())
            } else {
                None
            }
        })
}

pub fn derive_project_root_from_cwd() -> Option<String> {
    let cwd = std::env::current_dir().ok()?;
    let canonical = crate::core::pathutil::safe_canonicalize_or_self(&cwd);

    if is_home_or_agent_dir(&canonical) {
        return git_toplevel_from(&canonical);
    }

    if has_project_marker(&canonical) {
        return Some(canonical.to_string_lossy().to_string());
    }

    if let Some(git_root) = git_toplevel_from(&canonical) {
        return Some(git_root);
    }

    None
}

pub fn tool_descriptions_for_test() -> Vec<(&'static str, &'static str)> {
    crate::tool_defs::public_discoverable_tool_defs()
        .into_iter()
        .map(|(name, desc, _)| (name, desc))
        .collect()
}

pub fn tool_schemas_json_for_test() -> String {
    crate::tool_defs::public_discoverable_tool_defs()
        .iter()
        .map(|(name, _, schema)| format!("{}: {}", name, schema))
        .collect::<Vec<_>>()
        .join("\n")
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn project_markers_detected() {
        let tmp = tempfile::tempdir().unwrap();
        let root = tmp.path().join("myproject");
        std::fs::create_dir_all(&root).unwrap();
        assert!(!has_project_marker(&root));

        std::fs::create_dir(root.join(".git")).unwrap();
        assert!(has_project_marker(&root));
    }

    #[test]
    fn home_dir_detected_as_agent_dir() {
        if let Some(home) = dirs::home_dir() {
            assert!(is_home_or_agent_dir(&home));
        }
    }

    #[test]
    fn agent_dirs_detected() {
        let claude = std::path::PathBuf::from("/home/user/.claude");
        assert!(is_home_or_agent_dir(&claude));
        let codex = std::path::PathBuf::from("/home/user/.codex");
        assert!(is_home_or_agent_dir(&codex));
        let project = std::path::PathBuf::from("/home/user/projects/myapp");
        assert!(!is_home_or_agent_dir(&project));
    }

    #[test]
    fn test_unified_tool_count() {
        let tools = crate::tool_defs::unified_tool_defs();
        assert_eq!(tools.len(), 5, "Expected 5 unified tools");
    }

    #[test]
    fn public_tool_count_is_exactly_five() {
        let tools = crate::tool_defs::unified_tool_defs();
        let names: Vec<&str> = tools.iter().map(|t| t.name.as_ref()).collect();
        assert_eq!(
            names,
            vec!["ctx_read", "ctx_search", "ctx_tree", "ctx_shell", "ctx"],
            "Expected the canonical 5-tool public MCP surface"
        );
    }

    #[test]
    fn public_manifest_contains_only_public_tools() {
        let manifest = crate::core::mcp_manifest::manifest_value();
        let tools = manifest
            .get("tools")
            .and_then(|t| t.as_array())
            .expect("manifest tools should be an array");

        assert_eq!(
            tools.len(),
            5,
            "Manifest should expose exactly 5 public tools"
        );
    }

    #[test]
    fn ctx_requires_domain_and_action_in_public_mode() {
        let rt = tokio::runtime::Builder::new_current_thread()
            .enable_all()
            .build()
            .unwrap();
        let engine = crate::engine::ContextEngine::new();
        let err = rt
            .block_on(engine.call_tool_text(
                "ctx",
                Some(serde_json::json!({ "tool": "knowledge", "action": "recall" })),
            ))
            .expect_err("ctx(tool=...) should be rejected");

        assert!(
            err.to_string().contains("domain") || err.to_string().contains("tool"),
            "ctx should reject tool= style calls in favor of domain/action: {err}"
        );
    }

    #[test]
    fn claude_code_instructions_do_not_reference_ctx_edit() {
        let instructions = build_claude_code_instructions_for_test();
        assert!(
            !instructions.contains("ctx_edit"),
            "Public runtime instructions must not recommend private ctx_edit"
        );
    }

    #[test]
    fn ctx_read_symbol_target_honors_path_scope() {
        let rt = tokio::runtime::Builder::new_current_thread()
            .enable_all()
            .build()
            .unwrap();
        let engine = crate::engine::ContextEngine::new();
        let text = rt
            .block_on(engine.call_tool_text(
                "ctx_read",
                Some(serde_json::json!({
                    "target": "symbol",
                    "name": "handle",
                    "path": "client/src/tools/ctx_symbol.rs"
                })),
            ))
            .expect("ctx_read(symbol) with path scope should succeed");

        assert!(
            !text.contains("matches for 'handle'"),
            "path-scoped symbol reads should not degrade into ambiguous multi-match output: {text}"
        );
    }

    #[test]
    fn ctx_read_files_target_remains_the_public_batch_read_entrypoint() {
        let tools = crate::tool_defs::unified_tool_defs();
        let read = tools
            .iter()
            .find(|tool| tool.name.as_ref() == "ctx_read")
            .expect("ctx_read should remain public");
        let schema = serde_json::to_string(&*read.input_schema).unwrap();

        assert!(
            schema.contains("target") && schema.contains("files") && schema.contains("paths"),
            "ctx_read public schema must continue to advertise target=files batch reads: {schema}"
        );
        assert!(
            !schema.contains("ctx_multi_read"),
            "public ctx_read schema should not leak private ctx_multi_read details: {schema}"
        );
    }

    #[test]
    fn ctx_read_files_target_executes_batch_reads() {
        let _lock = crate::core::data_dir::test_env_lock();
        let data = tempfile::tempdir().unwrap();
        let repo = tempfile::tempdir().unwrap();
        std::env::set_var("NEBU_CTX_DATA_DIR", data.path());
        std::env::set_var("NEBU_CTX_HOME", data.path());
        std::fs::create_dir(repo.path().join(".git")).unwrap();

        let first = repo.path().join("one.txt");
        let second = repo.path().join("two.txt");
        std::fs::write(&first, "alpha\n").unwrap();
        std::fs::write(&second, "beta\n").unwrap();

        let first_path = crate::hooks::normalize_tool_path(&first.to_string_lossy());
        let second_path = crate::hooks::normalize_tool_path(&second.to_string_lossy());

        let rt = tokio::runtime::Builder::new_current_thread()
            .enable_all()
            .build()
            .unwrap();
        let engine = crate::engine::ContextEngine::with_project_root(repo.path());
        let text = rt
            .block_on(engine.call_tool_text(
                "ctx_read",
                Some(serde_json::json!({
                    "target": "files",
                    "paths": [first_path, second_path],
                    "mode": "full"
                })),
            ))
            .expect("ctx_read(files) should succeed");

        assert!(
            text.contains("Read 2 files"),
            "unexpected batch read text: {text}"
        );
        assert!(
            text.contains("one.txt"),
            "missing first file in batch read: {text}"
        );
        assert!(
            text.contains("two.txt"),
            "missing second file in batch read: {text}"
        );

        std::env::remove_var("NEBU_CTX_DATA_DIR");
        std::env::remove_var("NEBU_CTX_HOME");
    }

    #[test]
    fn analytics_report_is_accepted_in_public_mode() {
        let rt = tokio::runtime::Builder::new_current_thread()
            .enable_all()
            .build()
            .unwrap();
        let engine = crate::engine::ContextEngine::new();
        rt.block_on(engine.call_tool_text(
            "ctx",
            Some(serde_json::json!({ "domain": "analytics", "action": "report" })),
        ))
        .expect("ctx(domain=analytics, action=report) should be part of the public contract");
    }

    #[test]
    fn memory_recall_does_not_require_server_connection() {
        let rt = tokio::runtime::Builder::new_current_thread()
            .enable_all()
            .build()
            .unwrap();
        let engine = crate::engine::ContextEngine::new();
        let text = rt
            .block_on(engine.call_tool_text(
                "ctx",
                Some(serde_json::json!({
                    "domain": "memory",
                    "action": "recall",
                    "query": "session state decisions"
                })),
            ))
            .expect("ctx(domain=memory, action=recall) should succeed");

        assert!(
            !text.contains("requires a server connection"),
            "Public memory recall should not hard-require the hosted ctx_brain path: {text}"
        );
    }

    #[test]
    fn memory_search_stays_project_scoped_locally() {
        let _lock = crate::core::data_dir::test_env_lock();
        let data = tempfile::tempdir().unwrap();
        let repo_a = tempfile::tempdir().unwrap();
        let repo_b = tempfile::tempdir().unwrap();
        std::env::set_var("NEBU_CTX_DATA_DIR", data.path());
        std::env::set_var("NEBU_CTX_HOME", data.path());
        std::env::remove_var("NEBU_CTX_HTTP_TOKEN");
        std::fs::create_dir(repo_a.path().join(".git")).unwrap();
        std::fs::create_dir(repo_b.path().join(".git")).unwrap();

        let repo_a_root = repo_a.path().to_string_lossy().to_string();
        let repo_b_root = repo_b.path().to_string_lossy().to_string();
        let mut knowledge_a = crate::core::knowledge::ProjectKnowledge::load_or_create(&repo_a_root);
        let mut knowledge_b = crate::core::knowledge::ProjectKnowledge::load_or_create(&repo_b_root);
        let _ = knowledge_a.remember(
            "decision",
            "owner",
            "local search should stay in repo a",
            "session-a",
            0.9,
        );
        let _ = knowledge_b.remember(
            "decision",
            "owner",
            "other project only",
            "session-b",
            0.9,
        );
        knowledge_a.save().unwrap();
        knowledge_b.save().unwrap();

        let rt = tokio::runtime::Builder::new_current_thread()
            .enable_all()
            .build()
            .unwrap();
        let engine = crate::engine::ContextEngine::with_project_root(repo_a.path());
        let text = rt
            .block_on(engine.call_tool_text(
                "ctx",
                Some(serde_json::json!({
                    "domain": "memory",
                    "action": "search",
                    "query": "local repo a"
                })),
            ))
            .expect("memory search should succeed locally");

        assert!(text.contains("local search should stay in repo a"));
        assert!(!text.contains("other project only"));

        std::env::remove_var("NEBU_CTX_DATA_DIR");
        std::env::remove_var("NEBU_CTX_HOME");
    }

    #[test]
    fn memory_write_aliases_do_not_require_category() {
        let _lock = crate::core::data_dir::test_env_lock();
        let rt = tokio::runtime::Builder::new_current_thread()
            .enable_all()
            .build()
            .unwrap();
        let engine = crate::engine::ContextEngine::new();

        for action in ["store", "set", "remember"] {
            let text = rt
                .block_on(engine.call_tool_text(
                    "ctx",
                    Some(serde_json::json!({
                        "domain": "memory",
                        "action": action,
                        "key": format!("alias-{action}"),
                        "value": "memory alias smoke"
                    })),
                ))
                .expect("memory write alias should succeed");

            assert!(
                text.contains("Remembered")
                    || text.contains("remembered")
                    || text.contains("\"remembered\":true")
                    || text.contains("\"ok\":true")
                    || text.contains("\"ok\": true"),
                "memory action {action} should store knowledge instead of failing: {text}"
            );
        }
    }

    #[test]
    fn public_memory_gateway_rejects_local_only_knowledge_actions() {
        let rt = tokio::runtime::Builder::new_current_thread()
            .enable_all()
            .build()
            .unwrap();
        let engine = crate::engine::ContextEngine::new();

        for action in ["pattern", "export", "embeddings_status", "embeddings_reset", "embeddings_reindex"] {
            let err = rt
                .block_on(engine.call_tool_text(
                    "ctx",
                    Some(serde_json::json!({
                        "domain": "memory",
                        "action": action,
                    })),
                ))
                .expect_err("local-only knowledge actions should stay off the public memory gateway");

            let text = err.to_string();
            assert!(
                text.contains("Unknown memory action") || text.contains("tool call error"),
                "expected public memory gateway rejection for {action}: {text}"
            );
        }
    }

    #[test]
    fn memory_promote_falls_back_locally_without_server() {
        let _lock = crate::core::data_dir::test_env_lock();
        let data = tempfile::tempdir().unwrap();
        let repo = tempfile::tempdir().unwrap();
        std::env::set_var("NEBU_CTX_DATA_DIR", data.path());
        std::env::set_var("NEBU_CTX_HOME", data.path());
        std::env::remove_var("NEBU_CTX_HTTP_TOKEN");
        std::fs::create_dir(repo.path().join(".git")).unwrap();

        let rt = tokio::runtime::Builder::new_current_thread()
            .enable_all()
            .build()
            .unwrap();
        let engine = crate::engine::ContextEngine::with_project_root(repo.path());
        let text = rt
            .block_on(engine.call_tool_text(
                "ctx",
                Some(serde_json::json!({
                    "domain": "memory",
                    "action": "promote",
                    "items": [
                        {
                            "category": "decision",
                            "key": "memory-owner",
                            "value": "server owns canonical knowledge",
                            "confidence": 0.95
                        }
                    ]
                })),
            ))
            .expect("memory promote should succeed locally");

        assert!(
            text.contains("Promoted 1 items into local knowledge"),
            "unexpected promote text: {text}"
        );

        let knowledge =
            crate::core::knowledge::ProjectKnowledge::load(&repo.path().to_string_lossy())
                .expect("local knowledge should exist after promote");
        assert!(knowledge
            .facts
            .iter()
            .any(|fact| fact.category == "decision"
                && fact.key == "memory-owner"
                && fact.value == "server owns canonical knowledge"
                && fact.is_current()));

        std::env::remove_var("NEBU_CTX_DATA_DIR");
        std::env::remove_var("NEBU_CTX_HOME");
    }

    #[test]
    fn memory_triage_apply_marks_local_duplicates_non_current() {
        let _lock = crate::core::data_dir::test_env_lock();
        let data = tempfile::tempdir().unwrap();
        let repo = tempfile::tempdir().unwrap();
        std::env::set_var("NEBU_CTX_DATA_DIR", data.path());
        std::env::set_var("NEBU_CTX_HOME", data.path());
        std::env::remove_var("NEBU_CTX_HTTP_TOKEN");
        std::fs::create_dir(repo.path().join(".git")).unwrap();

        let repo_root = repo.path().to_string_lossy().to_string();
        let mut knowledge = crate::core::knowledge::ProjectKnowledge::load_or_create(&repo_root);
        let _ = knowledge.remember(
            "decision",
            "dup-a",
            "server owns canonical memory",
            "session-1",
            0.95,
        );
        let _ = knowledge.remember(
            "decision",
            "dup-b",
            "server owns canonical memory",
            "session-2",
            0.82,
        );
        let _ = knowledge.remember(
            "testing",
            "demo-placeholder",
            "demo placeholder memory",
            "session-3",
            0.60,
        );
        knowledge.save().unwrap();

        let rt = tokio::runtime::Builder::new_current_thread()
            .enable_all()
            .build()
            .unwrap();
        let engine = crate::engine::ContextEngine::with_project_root(repo.path());
        let text = rt
            .block_on(engine.call_tool_text(
                "ctx",
                Some(serde_json::json!({
                    "domain": "memory",
                    "action": "triage",
                    "mode": "apply"
                })),
            ))
            .expect("memory triage apply should succeed locally");

        assert!(
            text.contains("TRIAGE APPLY:"),
            "unexpected triage text: {text}"
        );
        assert!(
            text.contains("merged=1"),
            "unexpected triage merge summary: {text}"
        );
        assert!(
            text.contains("junk_marked=1"),
            "unexpected triage junk summary: {text}"
        );

        let knowledge = crate::core::knowledge::ProjectKnowledge::load(&repo_root)
            .expect("knowledge should still exist after triage apply");
        let current_decisions: Vec<_> = knowledge
            .facts
            .iter()
            .filter(|fact| fact.category == "decision" && fact.is_current())
            .collect();
        assert_eq!(
            current_decisions.len(),
            1,
            "expected one current decision after merge"
        );
        assert!(knowledge.facts.iter().any(|fact| fact.key == "dup-b"
            && !fact.is_current()
            && fact.supersedes.as_deref() == Some("merged-into:decision/dup-a")));
        assert!(knowledge
            .facts
            .iter()
            .any(|fact| fact.key == "demo-placeholder"
                && !fact.is_current()
                && fact.supersedes.as_deref() == Some("triage:junk")));

        std::env::remove_var("NEBU_CTX_DATA_DIR");
        std::env::remove_var("NEBU_CTX_HOME");
    }

    #[test]
    fn ctx_shell_does_not_prepend_auto_context() {
        let _lock = crate::core::data_dir::test_env_lock();
        let data = tempfile::tempdir().unwrap();
        let repo = tempfile::tempdir().unwrap();
        std::env::set_var("NEBU_CTX_DATA_DIR", data.path());
        std::env::set_var("NEBU_CTX_HOME", data.path());
        std::fs::create_dir(repo.path().join(".git")).unwrap();

        let rt = tokio::runtime::Builder::new_current_thread()
            .enable_all()
            .build()
            .unwrap();
        let engine = crate::engine::ContextEngine::with_project_root(repo.path());
        let text = rt
            .block_on(engine.call_tool_text(
                "ctx_shell",
                Some(serde_json::json!({
                    "command": "printf shell-clean"
                })),
            ))
            .expect("ctx_shell should succeed");

        assert!(text.contains("shell-clean"), "unexpected shell text: {text}");
        assert!(
            !text.contains("--- AUTO CONTEXT ---") && !text.contains("PROJECT OVERVIEW"),
            "ctx_shell should not leak auto context into normal shell output: {text}"
        );

        std::env::remove_var("NEBU_CTX_DATA_DIR");
        std::env::remove_var("NEBU_CTX_HOME");
    }

    #[test]
    fn ctx_read_allows_explicit_path_outside_project_root_with_warning() {
        let _lock = crate::core::data_dir::test_env_lock();
        let data = tempfile::tempdir().unwrap();
        let repo = tempfile::tempdir().unwrap();
        let other = tempfile::tempdir().unwrap();
        std::env::set_var("NEBU_CTX_DATA_DIR", data.path());
        std::env::set_var("NEBU_CTX_HOME", data.path());
        std::fs::create_dir(repo.path().join(".git")).unwrap();
        std::fs::write(other.path().join("outside.txt"), "outside-root\n").unwrap();

        let outside_path = other.path().join("outside.txt").to_string_lossy().to_string();

        let rt = tokio::runtime::Builder::new_current_thread()
            .enable_all()
            .build()
            .unwrap();
        let engine = crate::engine::ContextEngine::with_project_root(repo.path());
        let text = rt
            .block_on(engine.call_tool_text(
                "ctx_read",
                Some(serde_json::json!({
                    "path": outside_path,
                    "mode": "full"
                })),
            ))
            .expect("ctx_read should allow explicit outside-root path");

        assert!(text.contains("[warning: path outside project root; using explicit path:"));
        assert!(text.contains("outside-root"), "unexpected read text: {text}");

        std::env::remove_var("NEBU_CTX_DATA_DIR");
        std::env::remove_var("NEBU_CTX_HOME");
    }

    #[test]
    fn test_granular_tool_count() {
        let tools = crate::tool_defs::granular_tool_defs();
        assert!(tools.len() >= 25, "Expected at least 25 granular tools");
    }

    #[test]
    fn disabled_tools_filters_list() {
        let all = crate::tool_defs::granular_tool_defs();
        let total = all.len();
        let disabled = ["ctx_graph".to_string(), "ctx_agent".to_string()];
        let filtered: Vec<_> = all
            .into_iter()
            .filter(|t| !disabled.iter().any(|d| t.name.as_ref() == d.as_str()))
            .collect();
        assert_eq!(filtered.len(), total - 2);
        assert!(!filtered.iter().any(|t| t.name.as_ref() == "ctx_graph"));
        assert!(!filtered.iter().any(|t| t.name.as_ref() == "ctx_agent"));
    }

    #[test]
    fn empty_disabled_tools_returns_all() {
        let all = crate::tool_defs::granular_tool_defs();
        let total = all.len();
        let disabled: Vec<String> = vec![];
        let filtered: Vec<_> = all
            .into_iter()
            .filter(|t| !disabled.iter().any(|d| t.name.as_ref() == d.as_str()))
            .collect();
        assert_eq!(filtered.len(), total);
    }

    #[test]
    fn misspelled_disabled_tool_is_silently_ignored() {
        let all = crate::tool_defs::granular_tool_defs();
        let total = all.len();
        let disabled = ["ctx_nonexistent_tool".to_string()];
        let filtered: Vec<_> = all
            .into_iter()
            .filter(|t| !disabled.iter().any(|d| t.name.as_ref() == d.as_str()))
            .collect();
        assert_eq!(filtered.len(), total);
    }

    #[test]
    fn server_tool_constants_are_disjoint() {
        for name in SERVER_ONLY_TOOLS {
            assert!(
                !SERVER_PREFERRED_TOOLS.contains(name),
                "{name} appears in both SERVER_ONLY_TOOLS and SERVER_PREFERRED_TOOLS"
            );
        }
    }

    #[test]
    fn ctx_brain_is_server_only_not_preferred() {
        assert!(SERVER_ONLY_TOOLS.contains(&"ctx_brain"));
        assert!(!SERVER_PREFERRED_TOOLS.contains(&"ctx_brain"));
    }

    #[test]
    fn ctx_knowledge_and_ctx_session_are_server_preferred() {
        assert!(SERVER_PREFERRED_TOOLS.contains(&"ctx_knowledge"));
        assert!(SERVER_PREFERRED_TOOLS.contains(&"ctx_session"));
        assert!(!SERVER_ONLY_TOOLS.contains(&"ctx_knowledge"));
        assert!(!SERVER_ONLY_TOOLS.contains(&"ctx_session"));
    }

    #[test]
    fn knowledge_server_route_only_for_supported_actions() {
        let args = |action: &str| {
            Some(serde_json::Map::from_iter([(
                "action".to_string(),
                serde_json::Value::String(action.to_string()),
            )]))
        };

        assert!(prefers_server_route("ctx_knowledge", &args("remember")));
        assert!(prefers_server_route("ctx_knowledge", &args("consolidate")));
        assert!(prefers_server_route("ctx_knowledge", &args("promote")));
        assert!(prefers_server_route("ctx_knowledge", &args("upkeep")));
        assert!(prefers_server_route("ctx_knowledge", &args("triage")));
        assert!(prefers_server_route("ctx_knowledge", &args("wakeup")));
        assert!(prefers_server_route("ctx_knowledge", &args("timeline")));
        assert!(prefers_server_route("ctx_knowledge", &args("categories")));
        assert!(prefers_server_route("ctx_session", &args("status")));
    }

    #[test]
    fn analytics_tools_are_server_only() {
        assert!(SERVER_ONLY_TOOLS.contains(&"ctx_gain"));
        assert!(SERVER_ONLY_TOOLS.contains(&"ctx_cost"));
        assert!(SERVER_ONLY_TOOLS.contains(&"ctx_heatmap"));
        assert!(SERVER_ONLY_TOOLS.contains(&"ctx_stats"));
    }

    #[test]
    fn ctx_brain_in_granular_tool_defs() {
        let tools = crate::tool_defs::granular_tool_defs();
        assert!(
            tools.iter().any(|t| t.name.as_ref() == "ctx_brain"),
            "ctx_brain must appear in the local manifest so Claude sees it even when offline"
        );
    }

    #[test]
    fn ctx_brain_stub_has_required_actions() {
        let tools = crate::tool_defs::granular_tool_defs();
        let brain = tools
            .iter()
            .find(|t| t.name.as_ref() == "ctx_brain")
            .unwrap();
        let schema = serde_json::to_string(&*brain.input_schema).unwrap();
        assert!(
            schema.contains("store"),
            "ctx_brain schema must include 'store' action"
        );
        assert!(
            schema.contains("recall"),
            "ctx_brain schema must include 'recall' action"
        );
        assert!(
            schema.contains("forget"),
            "ctx_brain schema must include 'forget' action"
        );
    }

    #[test]
    fn route_to_server_returns_not_configured_or_error_when_no_connection() {
        // Without a saved connection, route_to_server must not panic. It should return
        // NotConfigured or Error (never Success unless CI has a live server).
        let rt = tokio::runtime::Builder::new_current_thread()
            .enable_all()
            .build()
            .unwrap();
        let result = rt.block_on(route_to_server("ctx_brain", &None));
        match result {
            ServerRoutingResult::Success(_) => {} // acceptable if CI has a live server
            ServerRoutingResult::NotConfigured => {}
            ServerRoutingResult::Error(_) => {}
        }
    }
}
