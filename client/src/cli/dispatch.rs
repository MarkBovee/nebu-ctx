use crate::{
    core, doctor, hook_handlers, mcp_stdio, setup, shell, status, sync_cli, tools, uninstall,
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

fn exit_unsupported(command: &str, replacement: &str) -> ! {
    eprintln!("nebu-ctx: `{command}` is no longer a supported thin-client CLI surface.");
    if !replacement.is_empty() {
        eprintln!("  Use: {replacement}");
    }
    std::process::exit(1);
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
            "gain" => exit_unsupported("gain", "ctx(domain=analytics, action=report)"),
            "token-report" | "report-tokens" => {
                exit_unsupported("token-report", "nebu-ctx status --json")
            }
            "cep" => exit_unsupported("cep", "ctx(domain=analytics, action=report)"),
            "serve" => exit_unsupported("serve", "use server/src/NebuCtx.Server.Host instead"),
            "proxy" => exit_unsupported("proxy", "no thin-client replacement"),
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
                exit_unsupported("bootstrap", "nebu-ctx setup --non-interactive --fix --yes")
            }
            "project-bootstrap" => {
                exit_unsupported("project-bootstrap", "no thin-client replacement")
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
            "read" => exit_unsupported("read", "ctx_read(path=..., mode=...)"),
            "diff" => exit_unsupported("diff", "ctx_read(...) via MCP"),
            "grep" => exit_unsupported("grep", "ctx_search(pattern=...)"),
            "find" | "ls" => exit_unsupported("ls", "ctx_tree(path=...)"),
            "deps" | "discover" | "filter" => exit_unsupported(command, "ctx(...) via MCP"),
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
            "session" | "wrapped" | "sessions" | "benchmark" | "cache" | "tee" | "terse"
            | "slow-log" | "cheat" | "cheatsheet" | "cheat-sheet" => {
                exit_unsupported(command, "no thin-client replacement")
            }
            "config" => exit_unsupported("config", "edit ~/.nebu-ctx/config.toml manually"),
            "theme" => exit_unsupported("theme", "edit ~/.nebu-ctx/config.toml manually"),

            "doctor" => {
                let code = doctor::run_cli(&rest);
                if code != 0 {
                    std::process::exit(code);
                }
                return;
            }
            "memory" => {
                super::memory::cmd_memory(&rest);
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
                    "idle-flush" => hook_handlers::handle_idle_flush(),
                    "post-tool-use" => hook_handlers::handle_post_tool_use(),
                    "tool-activity" => hook_handlers::handle_tool_activity(),
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
                        eprintln!("Usage: nebu-ctx hook <rewrite|redirect|copilot|codex-pretooluse|codex-session-start|rewrite-inline|stop|idle-flush|post-tool-use|tool-activity|pre-compact|session-start|user-prompt-submit|assistant-output-submit|telemetry>");
                        eprintln!("  Internal commands used by agent hooks (Claude, Cursor, Copilot, etc.)");
                        std::process::exit(1);
                    }
                }
                return;
            }
            "report-issue" | "report" => std::process::exit(crate::report_issue::run(&rest)),
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
        "nebu-ctx {version} — Thin Context Runtime for AI Agents

Public MCP surface: ctx_read, ctx_search, ctx_tree, ctx

USAGE:
    nebu-ctx                          Start MCP server (stdio)
    nebu-ctx -t \"command\"             Track command (full output + stats, no compression)
    nebu-ctx -c \"command\"             Execute shell command with compressed output
    nebu-ctx -c <exe> [args...]        Execute argv directly to avoid re-quoting hazards
    nebu-ctx -c --raw \"command\"       Execute without compression (full output)
    nebu-ctx exec \"command\"           Same as -c

COMMANDS:
    setup                             Install shell/editor integration
    setup <shell>                     Print shell hook to stdout
    setup --global                    Install shell aliases to rc/profile file
    setup --agent <name>              Configure supported agent/editor integration
    connect [--endpoint URL --token TOKEN]
                                      Save and validate host connection
    disconnect                        Remove saved host connection
    status [--json]                   Show setup and host connection status
    sync [status|flush] [--json]      Inspect or replay queued server-bound sync items
    doctor [--fix] [--json]           Run diagnostics (and optionally repair)
    memory list [...]                 Browse canonical memories (filters: --category, --since, --source-type, --limit, --offset, --sort-field, --sort-direction, --promoted-from-session, --promoted-from-brain-key)
    report-issue [options]            Create/update bug issue with local draft fallback
    hook <...>                        Internal hook entrypoints
    on-brief                          Print shell startup brief
    uninstall                         Remove shell hook, MCP configs, and data directory

NOTES:
    Memory, analytics, and workflow state route through ctx(domain=...).
    Memory actions require a configured host connection via `nebu-ctx connect`.
    Legacy local helper commands now fail fast outside thin-client surface.

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
    nebu-ctx -c \"git status --short --untracked-files=all\"
    nebu-ctx -c gh pr list
    nebu-ctx -c --raw \"cargo test --manifest-path client/Cargo.toml\"
    nebu-ctx setup
    nebu-ctx connect --endpoint http://127.0.0.1:4242 --token <token>
    nebu-ctx status --json
    nebu-ctx sync status --json
    nebu-ctx sync flush
    nebu-ctx doctor --fix --json
    nebu-ctx report-issue --submit --search-duplicates --title \"ctx_search invoke error\" --tool ctx_search --actual \"Cannot read properties of undefined (reading 'invoke')\"

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
    nebu-ctx doctor                Check PATH, config, MCP, and local edge health
    nebu-ctx doctor --fix --json   Repair + machine-readable report
    nebu-ctx status --json         Machine-readable current status
    nebu-ctx sync status --json    Inspect queued offline sync items
    nebu-ctx sync flush            Replay queued offline sync items once

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
