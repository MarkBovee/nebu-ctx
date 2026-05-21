use crate::{
    core, doctor, hook_handlers, mcp_stdio, report, setup, shell, status, sync_cli, token_report,
    tools, uninstall,
};
use anyhow::Result;

fn fire_shell_telemetry(tool_name: String, command_preview: Option<String>) {
    let project_context = std::env::current_dir()
        .ok()
        .map(|dir| crate::git_context::discover_project_context(&dir));

    core::telemetry_queue::fire_sync(crate::models::TelemetryIngestRequest {
        tool_name,
        tokens_original: 0,
        tokens_saved: 0,
        duration_ms: 0,
        mode: Some("shell".to_string()),
        repository_fingerprint: project_context.as_ref().and_then(|context| {
            context
                .fingerprint
                .has_safe_identity()
                .then(|| context.fingerprint.clone())
        }),
        checkout_binding: project_context
            .as_ref()
            .map(|context| context.checkout_binding.clone()),
        project_slug: project_context
            .as_ref()
            .map(|context| context.project_slug.clone())
            .filter(|slug| !slug.is_empty()),
        command_preview,
    });
}

pub fn run() {
    let args: Vec<String> = std::env::args().collect();

    if args.len() > 1 {
        let rest = args[2..].to_vec();
        let command = args[1].as_str();

        if matches!(command, "heatmap" | "stats") {
            super::exit_hosted_analytics_only(command);
        }

        match command {
            "-c" | "exec" => {
                let raw = rest.first().map(|a| a == "--raw").unwrap_or(false);
                let cmd_args = if raw { &args[3..] } else { &args[2..] };
                if cmd_args.is_empty() {
                    eprintln!("Usage: nebu-ctx -c [--raw] \"command\" or nebu-ctx -c [--raw] <exe> [args...]");
                    std::process::exit(1);
                }
                let command = if cmd_args.len() == 1 {
                    cmd_args[0].clone()
                } else {
                    shell::join_command(cmd_args)
                };
                if std::env::var("NEBU_CTX_ACTIVE").is_ok()
                    || std::env::var("NEBU_CTX_DISABLED").is_ok()
                {
                    passthrough(&command);
                }
                if raw {
                    std::env::set_var("NEBU_CTX_RAW", "1");
                } else {
                    std::env::set_var("NEBU_CTX_COMPRESS", "1");
                }
                let code = if cmd_args.len() > 1 {
                    shell::exec_argv_compressed(cmd_args)
                } else {
                    shell::exec(&command)
                };
                core::stats::flush();
                fire_shell_telemetry(
                    core::stats::normalize_command(&command),
                    core::sanitize::telemetry_command_preview(&command),
                );
                std::process::exit(code);
            }
            "-t" | "--track" => {
                let cmd_args = &args[2..];
                if cmd_args.is_empty() {
                    eprintln!("Usage: nebu-ctx -t \"command\" or nebu-ctx -t <exe> [args...]");
                    std::process::exit(1);
                }
                let tracked_name = cmd_args
                    .first()
                    .map(|s| core::stats::normalize_command(s))
                    .unwrap_or_else(|| "shell".to_string());
                let code = if cmd_args.len() > 1 {
                    shell::exec_argv(cmd_args)
                } else {
                    let command = cmd_args[0].clone();
                    if std::env::var("NEBU_CTX_ACTIVE").is_ok()
                        || std::env::var("NEBU_CTX_DISABLED").is_ok()
                    {
                        passthrough(&command);
                    }
                    shell::exec(&command)
                };
                core::stats::flush();
                let command_preview = if cmd_args.len() > 1 {
                    core::sanitize::telemetry_command_preview(&shell::join_command(cmd_args))
                } else {
                    cmd_args
                        .first()
                        .and_then(|command| core::sanitize::telemetry_command_preview(command))
                };
                fire_shell_telemetry(tracked_name, command_preview);
                std::process::exit(code);
            }
            "init" => {
                eprintln!("nebu-ctx: `init` is deprecated; use `setup` instead.");
                super::cmd_init(&rest);
                return;
            }
            "shell" | "--shell" => {
                shell::interactive();
                return;
            }
            "gain" => {
                if rest.iter().any(|a| a == "--reset") {
                    core::stats::reset_all();
                    println!("Stats reset. All token savings data cleared.");
                    return;
                }
                if super::has_flag(&rest, &["--live", "--watch"]) {
                    core::stats::gain_live();
                    return;
                }
                let model = super::option_value(&rest, &["--model"]);
                let period =
                    super::option_value(&rest, &["--period"]).unwrap_or_else(|| "all".to_string());
                let limit = super::option_value_parsed::<usize>(&rest, &["--limit"]).unwrap_or(10);

                if rest.iter().any(|a| a == "--graph") {
                    println!("{}", core::stats::format_gain_graph());
                } else if rest.iter().any(|a| a == "--daily") {
                    println!("{}", core::stats::format_gain_daily());
                } else if rest.iter().any(|a| a == "--json") {
                    println!(
                        "{}",
                        tools::ctx_gain::handle(
                            "json",
                            Some(&period),
                            model.as_deref(),
                            Some(limit)
                        )
                    );
                } else if rest.iter().any(|a| a == "--score") {
                    println!(
                        "{}",
                        tools::ctx_gain::handle("score", None, model.as_deref(), Some(limit))
                    );
                } else if rest.iter().any(|a| a == "--cost") {
                    println!(
                        "{}",
                        tools::ctx_gain::handle("cost", None, model.as_deref(), Some(limit))
                    );
                } else if rest.iter().any(|a| a == "--tasks") {
                    println!(
                        "{}",
                        tools::ctx_gain::handle("tasks", None, None, Some(limit))
                    );
                } else if rest.iter().any(|a| a == "--agents") {
                    println!(
                        "{}",
                        tools::ctx_gain::handle("agents", None, None, Some(limit))
                    );
                } else if rest.iter().any(|a| a == "--heatmap") {
                    println!(
                        "{}",
                        tools::ctx_gain::handle("heatmap", None, None, Some(limit))
                    );
                } else if rest.iter().any(|a| a == "--wrapped") {
                    println!(
                        "{}",
                        tools::ctx_gain::handle(
                            "wrapped",
                            Some(&period),
                            model.as_deref(),
                            Some(limit)
                        )
                    );
                } else if rest.iter().any(|a| a == "--pipeline") {
                    let stats_path = dirs::home_dir()
                        .unwrap_or_default()
                        .join(".nebu-ctx")
                        .join("pipeline_stats.json");
                    if let Ok(data) = std::fs::read_to_string(&stats_path) {
                        if let Ok(stats) =
                            serde_json::from_str::<core::pipeline::PipelineStats>(&data)
                        {
                            println!("{}", stats.format_summary());
                        } else {
                            println!("No pipeline stats available yet (corrupt data).");
                        }
                    } else {
                        println!(
                            "No pipeline stats available yet. Use MCP tools to generate data."
                        );
                    }
                } else if rest.iter().any(|a| a == "--deep") {
                    println!(
                        "{}\n{}\n{}\n{}\n{}",
                        tools::ctx_gain::handle("report", None, model.as_deref(), Some(limit)),
                        tools::ctx_gain::handle("tasks", None, None, Some(limit)),
                        tools::ctx_gain::handle("cost", None, model.as_deref(), Some(limit)),
                        tools::ctx_gain::handle("agents", None, None, Some(limit)),
                        tools::ctx_gain::handle("heatmap", None, None, Some(limit))
                    );
                } else {
                    println!("{}", core::stats::format_gain());
                }
                return;
            }
            "token-report" | "report-tokens" => {
                let code = token_report::run_cli(&rest);
                if code != 0 {
                    std::process::exit(code);
                }
                return;
            }
            "cep" => {
                println!("{}", tools::ctx_gain::handle("score", None, None, Some(10)));
                return;
            }
            "serve" => {
                #[cfg(feature = "http-server")]
                {
                    let mut cfg = crate::mcp_http::HttpServerConfig::default();
                    let mut i = 0;
                    while i < rest.len() {
                        match rest[i].as_str() {
                            "--host" | "-H" => {
                                i += 1;
                                if i < rest.len() {
                                    cfg.host = rest[i].clone();
                                }
                            }
                            arg if arg.starts_with("--host=") => {
                                cfg.host = arg["--host=".len()..].to_string();
                            }
                            "--port" | "-p" => {
                                i += 1;
                                if i < rest.len() {
                                    if let Ok(p) = rest[i].parse::<u16>() {
                                        cfg.port = p;
                                    }
                                }
                            }
                            arg if arg.starts_with("--port=") => {
                                if let Ok(p) = arg["--port=".len()..].parse::<u16>() {
                                    cfg.port = p;
                                }
                            }
                            "--project-root" => {
                                i += 1;
                                if i < rest.len() {
                                    cfg.project_root = std::path::PathBuf::from(&rest[i]);
                                }
                            }
                            arg if arg.starts_with("--project-root=") => {
                                cfg.project_root =
                                    std::path::PathBuf::from(&arg["--project-root=".len()..]);
                            }
                            "--auth-token" => {
                                i += 1;
                                if i < rest.len() {
                                    cfg.auth_token = Some(rest[i].clone());
                                }
                            }
                            arg if arg.starts_with("--auth-token=") => {
                                cfg.auth_token = Some(arg["--auth-token=".len()..].to_string());
                            }
                            "--stateful" => cfg.stateful_mode = true,
                            "--stateless" => cfg.stateful_mode = false,
                            "--json" => cfg.json_response = true,
                            "--sse" => cfg.json_response = false,
                            "--disable-host-check" => cfg.disable_host_check = true,
                            "--allowed-host" => {
                                i += 1;
                                if i < rest.len() {
                                    cfg.allowed_hosts.push(rest[i].clone());
                                }
                            }
                            arg if arg.starts_with("--allowed-host=") => {
                                cfg.allowed_hosts
                                    .push(arg["--allowed-host=".len()..].to_string());
                            }
                            "--max-body-bytes" => {
                                i += 1;
                                if i < rest.len() {
                                    if let Ok(n) = rest[i].parse::<usize>() {
                                        cfg.max_body_bytes = n;
                                    }
                                }
                            }
                            arg if arg.starts_with("--max-body-bytes=") => {
                                if let Ok(n) = arg["--max-body-bytes=".len()..].parse::<usize>() {
                                    cfg.max_body_bytes = n;
                                }
                            }
                            "--max-concurrency" => {
                                i += 1;
                                if i < rest.len() {
                                    if let Ok(n) = rest[i].parse::<usize>() {
                                        cfg.max_concurrency = n;
                                    }
                                }
                            }
                            arg if arg.starts_with("--max-concurrency=") => {
                                if let Ok(n) = arg["--max-concurrency=".len()..].parse::<usize>() {
                                    cfg.max_concurrency = n;
                                }
                            }
                            "--max-rps" => {
                                i += 1;
                                if i < rest.len() {
                                    if let Ok(n) = rest[i].parse::<u32>() {
                                        cfg.max_rps = n;
                                    }
                                }
                            }
                            arg if arg.starts_with("--max-rps=") => {
                                if let Ok(n) = arg["--max-rps=".len()..].parse::<u32>() {
                                    cfg.max_rps = n;
                                }
                            }
                            "--rate-burst" => {
                                i += 1;
                                if i < rest.len() {
                                    if let Ok(n) = rest[i].parse::<u32>() {
                                        cfg.rate_burst = n;
                                    }
                                }
                            }
                            arg if arg.starts_with("--rate-burst=") => {
                                if let Ok(n) = arg["--rate-burst=".len()..].parse::<u32>() {
                                    cfg.rate_burst = n;
                                }
                            }
                            "--request-timeout-ms" => {
                                i += 1;
                                if i < rest.len() {
                                    if let Ok(n) = rest[i].parse::<u64>() {
                                        cfg.request_timeout_ms = n;
                                    }
                                }
                            }
                            arg if arg.starts_with("--request-timeout-ms=") => {
                                if let Ok(n) = arg["--request-timeout-ms=".len()..].parse::<u64>() {
                                    cfg.request_timeout_ms = n;
                                }
                            }
                            "--help" | "-h" => {
                                eprintln!(
                                                                        "Usage: nebu-ctx serve [--host H] [--port N] [--project-root DIR]\\n\\
                                     \\n\\
                                     Options:\\n\\
                                       --host, -H            Bind host (default: 127.0.0.1)\\n\\
                                       --port, -p            Bind port (default: 8080)\\n\\
                                       --project-root        Resolve relative paths against this root (default: cwd)\\n\\
                                       --auth-token          Require Authorization: Bearer <token> (required for non-loopback hosts)\\n\\
                                       --stateful/--stateless  Streamable HTTP session mode (default: stateless)\\n\\
                                       --json/--sse          Response framing in stateless mode (default: json)\\n\\
                                       --max-body-bytes      Max request body size in bytes (default: 2097152)\\n\\
                                       --max-concurrency     Max concurrent requests (default: 32)\\n\\
                                       --max-rps             Max requests/sec (global, default: 50)\\n\\
                                       --rate-burst          Rate limiter burst (global, default: 100)\\n\\
                                       --request-timeout-ms  REST tool-call timeout (default: 30000)\\n\\
                                       --allowed-host        Add allowed Host header (repeatable)\\n\\
                                       --disable-host-check  Disable Host header validation (unsafe)"
                                );
                                return;
                            }
                            _ => {}
                        }
                        i += 1;
                    }

                    if cfg.auth_token.is_none() {
                        if let Ok(v) = std::env::var("NEBU_CTX_HTTP_TOKEN") {
                            if !v.trim().is_empty() {
                                cfg.auth_token = Some(v);
                            }
                        }
                    }

                    if let Err(e) = run_async(crate::mcp_http::serve(cfg)) {
                        eprintln!("HTTP server error: {e}");
                        std::process::exit(1);
                    }
                    return;
                }
                #[cfg(not(feature = "http-server"))]
                {
                    eprintln!("nebu-ctx serve is not available in this build");
                    std::process::exit(1);
                }
            }
            "proxy" => {
                #[cfg(feature = "http-server")]
                {
                    let sub = rest.first().map(|s| s.as_str()).unwrap_or("help");
                    match sub {
                        "start" => {
                            let port = super::option_value_parsed::<u16>(&rest, &["--port", "-p"])
                                .unwrap_or(4444);
                            let autostart = rest.iter().any(|a| a == "--autostart");
                            if autostart {
                                crate::proxy_autostart::install(port, false);
                                return;
                            }
                            if let Err(e) = run_async(crate::llm_proxy::start_proxy(port)) {
                                eprintln!("Proxy error: {e}");
                                std::process::exit(1);
                            }
                        }
                        "stop" => {
                            let port = super::option_value_parsed::<u16>(&rest, &["--port", "-p"])
                                .unwrap_or(4444);
                            match ureq::get(&format!("http://127.0.0.1:{port}/health")).call() {
                                Ok(_) => {
                                    println!("Proxy is running. Use Ctrl+C or kill the process.");
                                }
                                Err(_) => {
                                    println!("No proxy running on that port.");
                                }
                            }
                        }
                        "status" => {
                            let port = super::option_value_parsed::<u16>(&rest, &["--port", "-p"])
                                .unwrap_or(4444);
                            match ureq::get(&format!("http://127.0.0.1:{port}/status")).call() {
                                Ok(resp) => {
                                    let body =
                                        resp.into_body().read_to_string().unwrap_or_default();
                                    if let Ok(v) = serde_json::from_str::<serde_json::Value>(&body)
                                    {
                                        println!("nebu-ctx proxy status:");
                                        println!("  Requests:    {}", v["requests_total"]);
                                        println!("  Compressed:  {}", v["requests_compressed"]);
                                        println!("  Tokens saved: {}", v["tokens_saved"]);
                                        println!(
                                            "  Compression: {}%",
                                            v["compression_ratio_pct"].as_str().unwrap_or("0.0")
                                        );
                                    } else {
                                        println!("{body}");
                                    }
                                }
                                Err(_) => {
                                    println!("No proxy running on port {port}.");
                                    println!("Start with: nebu-ctx proxy start");
                                }
                            }
                        }
                        _ => {
                            println!("Usage: nebu-ctx proxy <start|stop|status> [--port=4444]");
                        }
                    }
                    return;
                }
                #[cfg(not(feature = "http-server"))]
                {
                    eprintln!("nebu-ctx proxy is not available in this build");
                    std::process::exit(1);
                }
            }
            "setup" => {
                if let Some(mode) = rest.first().map(|s| s.as_str()) {
                    if matches!(mode, "bash" | "zsh" | "fish" | "powershell" | "pwsh") {
                        super::cmd_init(&rest);
                        return;
                    }
                }
                if rest.iter().any(|a| a == "--agent")
                    || rest.iter().any(|a| a == "--dry-run")
                    || rest.iter().any(|a| a == "--global" || a == "-g")
                {
                    super::cmd_init(&rest);
                    return;
                }
                let non_interactive = rest.iter().any(|a| a == "--non-interactive");
                let yes = rest.iter().any(|a| a == "--yes" || a == "-y");
                let fix = rest.iter().any(|a| a == "--fix");
                let json = rest.iter().any(|a| a == "--json");

                if non_interactive || fix || json || yes {
                    let opts = setup::SetupOptions {
                        non_interactive,
                        yes,
                        fix,
                        json,
                    };
                    match setup::run_setup_with_options(opts) {
                        Ok(report) => {
                            if json {
                                println!(
                                    "{}",
                                    serde_json::to_string_pretty(&report)
                                        .unwrap_or_else(|_| "{}".to_string())
                                );
                            }
                            if !report.success {
                                std::process::exit(1);
                            }
                        }
                        Err(e) => {
                            eprintln!("{e}");
                            std::process::exit(1);
                        }
                    }
                } else {
                    setup::run_setup();
                }
                return;
            }
            "bootstrap" => {
                let json = rest.iter().any(|a| a == "--json");
                let opts = setup::SetupOptions {
                    non_interactive: true,
                    yes: true,
                    fix: true,
                    json,
                };
                match setup::run_setup_with_options(opts) {
                    Ok(report) => {
                        if json {
                            println!(
                                "{}",
                                serde_json::to_string_pretty(&report)
                                    .unwrap_or_else(|_| "{}".to_string())
                            );
                        }
                        if !report.success {
                            std::process::exit(1);
                        }
                    }
                    Err(e) => {
                        eprintln!("{e}");
                        std::process::exit(1);
                    }
                }
                return;
            }
            "status" => {
                let code = status::run_cli(&rest);
                if code != 0 {
                    std::process::exit(code);
                }
                return;
            }
            "sync" => {
                let code = sync_cli::run_cli(&rest);
                if code != 0 {
                    std::process::exit(code);
                }
                return;
            }
            "read" => {
                super::cmd_read(&rest);
                return;
            }
            "diff" => {
                super::cmd_diff(&rest);
                return;
            }
            "grep" => {
                super::cmd_grep(&rest);
                return;
            }
            "find" => {
                super::cmd_find(&rest);
                return;
            }
            "ls" => {
                super::cmd_ls(&rest);
                return;
            }
            "deps" => {
                super::cmd_deps(&rest);
                return;
            }
            "discover" => {
                super::cmd_discover(&rest);
                return;
            }
            "filter" => {
                super::cmd_filter(&rest);
                return;
            }
            "graph" => {
                let mut action = "build";
                let mut path_arg: Option<&str> = None;
                for arg in &rest {
                    if arg == "build" {
                        action = "build";
                    } else {
                        path_arg = Some(arg.as_str());
                    }
                }
                let root = path_arg
                    .map(String::from)
                    .or_else(|| {
                        std::env::current_dir()
                            .ok()
                            .map(|p| p.to_string_lossy().to_string())
                    })
                    .unwrap_or_else(|| ".".to_string());
                match action {
                    "build" => {
                        let index = core::graph_index::load_or_build(&root);
                        println!(
                            "Graph built: {} files, {} edges",
                            index.files.len(),
                            index.edges.len()
                        );
                    }
                    _ => {
                        eprintln!("Usage: nebu-ctx graph [build] [path]");
                    }
                }
                return;
            }
            "session" => {
                super::cmd_session();
                return;
            }
            "wrapped" => {
                super::cmd_wrapped(&rest);
                return;
            }
            "sessions" => {
                super::cmd_sessions(&rest);
                return;
            }
            "benchmark" => {
                super::cmd_benchmark(&rest);
                return;
            }
            "config" => {
                super::cmd_config(&rest);
                return;
            }
            "cache" => {
                super::cmd_cache(&rest);
                return;
            }
            "theme" => {
                super::cmd_theme(&rest);
                return;
            }
            "tee" => {
                super::cmd_tee(&rest);
                return;
            }
            "terse" => {
                super::cmd_terse(&rest);
                return;
            }
            "slow-log" => {
                super::cmd_slow_log(&rest);
                return;
            }

            "doctor" => {
                let code = doctor::run_cli(&rest);
                if code != 0 {
                    std::process::exit(code);
                }
                return;
            }
            "failures" | "failure-memory" | "bugs" | "bug-memory" => {
                super::connect::cmd_bug_memory(&rest);
                return;
            }
            "buddy" | "pet" => {
                super::connect::cmd_buddy(&rest);
                return;
            }
            "hook" => {
                let action = rest.first().map(|s| s.as_str()).unwrap_or("help");
                match action {
                    "rewrite" => hook_handlers::handle_rewrite(),
                    "redirect" => hook_handlers::handle_redirect(),
                    "copilot" => hook_handlers::handle_copilot(),
                    "codex-pretooluse" => hook_handlers::handle_codex_pretooluse(),
                    "codex-session-start" => hook_handlers::handle_codex_session_start(),
                    "rewrite-inline" => hook_handlers::handle_rewrite_inline(),
                    "stop" => hook_handlers::handle_stop(),
                    "post-tool-use" => hook_handlers::handle_post_tool_use(),
                    "pre-compact" => hook_handlers::handle_pre_compact(),
                    "session-start" => hook_handlers::handle_session_start(),
                    "user-prompt-submit" => hook_handlers::handle_user_prompt_submit(),
                    "assistant-output-submit" => hook_handlers::handle_assistant_output_submit(),
                    "telemetry" => {
                        let tool_name = rest
                            .get(1)
                            .cloned()
                            .unwrap_or_else(|| "unknown".to_string());
                        let tokens_original: i64 =
                            rest.get(2).and_then(|s| s.parse().ok()).unwrap_or(0);
                        let tokens_saved: i64 =
                            rest.get(3).and_then(|s| s.parse().ok()).unwrap_or(0);
                        let project_context = std::env::current_dir()
                            .ok()
                            .map(|dir| crate::git_context::discover_project_context(&dir));
                        core::telemetry_queue::fire_sync(crate::models::TelemetryIngestRequest {
                            tool_name: core::stats::normalize_command(&tool_name),
                            tokens_original,
                            tokens_saved,
                            duration_ms: 0,
                            mode: Some("plugin".to_string()),
                            repository_fingerprint: project_context.as_ref().and_then(|context| {
                                context
                                    .fingerprint
                                    .has_safe_identity()
                                    .then(|| context.fingerprint.clone())
                            }),
                            checkout_binding: project_context
                                .as_ref()
                                .map(|context| context.checkout_binding.clone()),
                            project_slug: project_context
                                .as_ref()
                                .map(|context| context.project_slug.clone())
                                .filter(|slug| !slug.is_empty()),
                            command_preview: None,
                        });
                    }
                    _ => {
                        eprintln!("Usage: nebu-ctx hook <rewrite|redirect|copilot|codex-pretooluse|codex-session-start|rewrite-inline|stop|post-tool-use|pre-compact|session-start|user-prompt-submit|assistant-output-submit|telemetry>");
                        eprintln!("  Internal commands used by agent hooks (Claude, Cursor, Copilot, etc.)");
                        std::process::exit(1);
                    }
                }
                return;
            }
            "report-issue" | "report" => {
                report::run(&rest);
                return;
            }
            "uninstall" => {
                uninstall::run();
                return;
            }
            "bypass" => {
                if rest.is_empty() {
                    eprintln!("Usage: nebu-ctx bypass \"command\"");
                    eprintln!("Runs the command with zero compression (raw passthrough).");
                    std::process::exit(1);
                }
                let command = if rest.len() == 1 {
                    rest[0].clone()
                } else {
                    shell::join_command(&args[2..])
                };
                std::env::set_var("NEBU_CTX_RAW", "1");
                let code = shell::exec(&command);
                std::process::exit(code);
            }
            "safety-levels" | "safety" => {
                println!("{}", core::compression_safety::format_safety_table());
                return;
            }
            "cheat" | "cheatsheet" | "cheat-sheet" => {
                super::cmd_cheatsheet();
                return;
            }
            "connect" => {
                super::connect::cmd_connect(&rest);
                return;
            }
            "disconnect" => {
                super::connect::cmd_disconnect();
                return;
            }
            "--version" | "-V" => {
                println!("nebu-ctx {}", env!("CARGO_PKG_VERSION"));
                return;
            }
            "--help" | "-h" => {
                print_help();
                return;
            }
            "mcp" => {}
            "on" => {
                eprintln!("nebu-ctx: `nebu-ctx on` is a shell function, not a binary command.");
                eprintln!("  Run: source ~/.nebu-ctx/shell-hook.fish  (fish)");
                eprintln!("  Or add the shell hook to your shell profile via: nebu-ctx setup");
                std::process::exit(1);
            }
            "off" => {
                eprintln!("nebu-ctx: `nebu-ctx off` is a shell function, not a binary command.");
                eprintln!("  Run: source ~/.nebu-ctx/shell-hook.fish  (fish)");
                eprintln!("  Or add the shell hook to your shell profile via: nebu-ctx setup");
                std::process::exit(1);
            }
            "on-brief" => {
                status::print_on_brief();
                return;
            }
            _ => {
                eprintln!("nebu-ctx: unknown command '{}'\n", args[1]);
                print_help();
                std::process::exit(1);
            }
        }
    }

    if let Err(e) = run_mcp_server() {
        eprintln!("nebu-ctx: {e}");
        std::process::exit(1);
    }
}

fn passthrough(command: &str) -> ! {
    let (shell, flag) = shell::shell_and_flag();
    let status = shell::spawn_shell_command(&shell, &flag, command, false)
        .env("NEBU_CTX_ACTIVE", "1")
        .status()
        .map(|s| s.code().unwrap_or(1))
        .unwrap_or(127);
    std::process::exit(status);
}

#[cfg(feature = "http-server")]
fn run_async<F: std::future::Future>(future: F) -> F::Output {
    tokio::runtime::Runtime::new()
        .expect("failed to create async runtime")
        .block_on(future)
}

fn run_mcp_server() -> Result<()> {
    use rmcp::ServiceExt;
    use tracing_subscriber::EnvFilter;

    std::env::set_var("NEBU_CTX_MCP_SERVER", "1");
    ensure_mcp_host_connection_configured()?;

    let rt = tokio::runtime::Runtime::new()?;
    rt.block_on(async {
        tracing_subscriber::fmt()
            .with_env_filter(EnvFilter::from_default_env())
            .with_writer(std::io::stderr)
            .init();

        tracing::info!(
            "nebu-ctx v{} MCP server starting",
            env!("CARGO_PKG_VERSION")
        );

        let server = tools::create_server();
        core::telemetry_queue::start_drain_task();
        let transport =
            mcp_stdio::HybridStdioTransport::new_server(tokio::io::stdin(), tokio::io::stdout());
        let service = server.serve(transport).await?;
        service.waiting().await?;

        core::stats::flush();
        core::mode_predictor::ModePredictor::flush();
        core::feedback::FeedbackStore::flush();

        Ok(())
    })
}

fn ensure_mcp_host_connection_configured() -> Result<()> {
    if crate::config::load_connection()?.is_some() {
        return Ok(());
    }

    anyhow::bail!(
        "nebu-ctx host connection is not configured yet.\n\
Run: nebu-ctx status\n\
Connect to a local host:   nebu-ctx connect --endpoint http://127.0.0.1:4242 --token <token>\n\
Connect to a network host: nebu-ctx connect --endpoint http://192.168.1.50:4242 --token <token>\n\
Port 4242 is the MCP/host port. After connecting, retry your editor or agent."
    );
}

fn print_help() {
    println!(
        "nebu-ctx {version} — Context Runtime for AI Agents

90+ compression patterns | 46 MCP tools | Context Continuity Protocol

USAGE:
    nebu-ctx                       Start MCP server (stdio)
    nebu-ctx serve                 Start MCP server (Streamable HTTP)
    nebu-ctx -t \"command\"          Track command (full output + stats, no compression)
    nebu-ctx -c \"command\"          Execute shell command with compressed output
    nebu-ctx -c <exe> [args...]     Execute argv directly to avoid re-quoting hazards
    nebu-ctx -c --raw \"command\"    Execute without compression (full output)
    nebu-ctx exec \"command\"        Same as -c
    nebu-ctx bypass \"command\"      Run command with zero compression (raw passthrough)
    nebu-ctx shell                 Interactive shell with compression

COMMANDS:
         token-report [--json]          Token + memory report (project + session + CEP)
    gain [action]              Fetch analytics from server (report, score, cost, tasks, etc.)
    serve [--host H] [--port N]    MCP over HTTP (Streamable HTTP, local-first)
    proxy start [--port=4444]      API proxy: compress tool_results before LLM API
    proxy status                   Show proxy statistics
    cache [list|clear|stats]       Show/manage file read cache
    wrapped [--week|--month|--all] Savings report card (shareable)
    sessions [list|show|cleanup]   Manage CCP sessions (~/.nebu-ctx/sessions/)
    benchmark run [path] [--json]  Run real benchmark on project files
    benchmark report [path]        Generate shareable Markdown report
    cheatsheet                     Command cheat sheet & workflow quick reference
    setup                          One-command setup: shell + editors + rules
    bootstrap                      Non-interactive setup + fix (zero-config)
    status [--json]                Show setup + MCP + rules status
    setup <shell>                  Print shell hook to stdout (eval pattern, like starship)
                                   Supported: bash, zsh, fish, powershell
    setup --global                 Install shell aliases to rc file (file-based)
    setup --agent <name>           Configure MCP for specific editor/agent
    read <file> [-m mode]          Read file with compression
    diff <file1> <file2>           Compressed file diff
    grep <pattern> [path]          Search with compressed output
    find <pattern> [path]          Find files with compressed output
    ls [path]                      Directory listing with compression
    deps [path]                    Show project dependencies
    discover                       Find uncompressed commands in shell history
    filter [list|validate|init]    Manage custom compression filters (~/.nebu-ctx/filters/)
    session                        Show adoption statistics
    config                         Show/edit configuration (~/.nebu-ctx/config.toml)
    theme [list|set|export|import] Customize terminal colors and themes
    tee [list|clear|show <file>|last] Manage output tee files (~/.nebu-ctx/tee/)
    terse [off|lite|full|ultra]    Set agent output verbosity (saves 25-65% output tokens)
    slow-log [list|clear]          Show/clear slow command log (~/.nebu-ctx/slow-commands.log)
    failures [list|clear|export|stats] Failure Memory: view/manage recorded client-side failures
    buddy [show|stats|ascii|json]  Token Guardian: your data-driven coding companion
    doctor [--fix] [--json]        Run diagnostics (and optionally repair)
    safety-levels                  Show compression safety levels per command
    bypass \"command\"               Run command with zero compression (raw passthrough)
    uninstall                      Remove shell hook, MCP configs, and data directory

SHELL HOOK PATTERNS (90+):
    git       status, log, diff, add, commit, push, pull, fetch, clone,
              branch, checkout, switch, merge, stash, tag, reset, remote
    docker    build, ps, images, logs, compose, exec, network
    npm/pnpm  install, test, run, list, outdated, audit
    cargo     build, test, check, clippy
    gh        pr list/view/create, issue list/view, run list/view
    kubectl   get pods/services/deployments, logs, describe, apply
    python    pip install/list/outdated, ruff check/format, poetry, uv
    linters   eslint, biome, prettier, golangci-lint
    builds    tsc, next build, vite build
    ruby      rubocop, bundle install/update, rake test, rails test
    tests     jest, vitest, pytest, go test, playwright, rspec, minitest
    iac       terraform, make, maven, gradle, dotnet, flutter, dart
    utils     curl, grep/rg, find, ls, wget, env
    data      JSON schema extraction, log deduplication

READ MODES:
    auto                           Auto-select optimal mode (default)
    full                           Full content (cached re-reads = 13 tokens)
    map                            Dependency graph + API signatures
    signatures                     tree-sitter AST extraction (18 languages)
    task                           Task-relevant filtering (requires ctx_session task)
    reference                      One-line reference stub (cheap cache key)
    aggressive                     Syntax-stripped content
    entropy                        Shannon entropy filtered
    diff                           Changed lines only
    lines:N-M                      Specific line ranges (e.g. lines:10-50,80)

ENVIRONMENT:
    NEBU_CTX_DISABLED=1            Bypass ALL compression + prevent shell hook from loading
    NEBU_CTX_ENABLED=0             Prevent shell hook auto-start (nebu-ctx-on still works)
    NEBU_CTX_RAW=1                 Same as --raw for current command
    NEBU_CTX_AUTONOMY=false        Disable autonomous features
    NEBU_CTX_COMPRESS=1            Force compression (even for excluded commands)

OPTIONS:
    --version, -V                  Show version
    --help, -h                     Show this help

EXAMPLES:
        nebu-ctx -c \"git status\"       Compressed shell command
        nebu-ctx -c gh pr list          Direct argv mode for quoting-sensitive CLIs
        nebu-ctx -c \"kubectl get pods\" Compressed k8s output
            nebu-ctx token-report --json   Machine-readable token + memory report
        nebu-ctx wrapped               Weekly savings report card
        nebu-ctx wrapped --month       Monthly savings report card
        nebu-ctx sessions list         List all CCP sessions
        nebu-ctx sessions show         Show latest session state
        nebu-ctx discover              Find missed savings in shell history
        nebu-ctx setup                 One-command setup (shell + editors + rules)
        nebu-ctx bootstrap             Non-interactive setup + fix (zero-config)
        nebu-ctx bootstrap --json      Machine-readable bootstrap report
        nebu-ctx setup --global        Install shell aliases (file-based, includes nebu-ctx-on/off)

EVAL SETUP (starship/zoxide style — always in sync with binary version):
    # bash: add to ~/.bashrc
    eval \"$(nebu-ctx setup bash)\"
    # zsh: add to ~/.zshrc
    eval \"$(nebu-ctx setup zsh)\"
    # fish: add to ~/.config/fish/config.fish
    nebu-ctx setup fish | source
    # powershell: add to $PROFILE
    nebu-ctx setup powershell | Invoke-Expression
    nebu-ctx-on                    Enable shell aliases in track mode (full output + stats)
    nebu-ctx-off                   Disable all shell aliases
    nebu-ctx-mode track            Track mode: full output, stats recorded (default)
    nebu-ctx-mode compress         Compress mode: all output compressed (power users)
    nebu-ctx-mode off              Same as nebu-ctx-off
    nebu-ctx-status                Show whether compression is active
    nebu-ctx setup --agent pi      Install Pi Coding Agent extension
    nebu-ctx doctor                Check PATH, config, MCP, and local edge health
        nebu-ctx doctor --fix --json   Repair + machine-readable report
        nebu-ctx status --json         Machine-readable current status
        nebu-ctx sync status --json    Inspect queued offline sync items
        nebu-ctx sync flush            Replay queued offline sync items once
        nebu-ctx read src/main.rs -m map
    nebu-ctx grep \"pub fn\" src/
    nebu-ctx deps .

HOST CONNECTION:
    connect [--endpoint <url>] [--token <token>]  Save and validate a server connection
    status                         Includes host connection status
    disconnect                     Remove the saved server connection

TROUBLESHOOTING:
    Commands broken?     nebu-ctx-off             (fixes current session)
    Permanent fix?       nebu-ctx uninstall       (removes all hooks)
    Manual fix?          Edit ~/.zshrc, remove the \"nebu-ctx shell hook\" block
    Binary missing?      Aliases auto-fallback to original commands (safe)
    Preview setup?       nebu-ctx setup --global --dry-run

WEBSITE: https://nebu-ctx.com
GITHUB:  https://github.com/MarkBovee/nebu-ctx
",
        version = env!("CARGO_PKG_VERSION"),
    );
}
