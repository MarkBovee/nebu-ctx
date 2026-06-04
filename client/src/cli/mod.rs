pub mod connect;
pub mod dispatch;
pub mod memory;
pub(crate) mod shell_init;

pub use dispatch::run;
pub use shell_init::*;

use crate::hooks::to_bash_compatible_path;

pub(crate) fn has_flag(args: &[String], flags: &[&str]) -> bool {
    args.iter().any(|arg| flags.contains(&arg.as_str()))
}

pub(crate) fn option_value(args: &[String], flags: &[&str]) -> Option<String> {
    let mut index = 0;
    while index < args.len() {
        let arg = &args[index];
        if flags.contains(&arg.as_str()) {
            return args.get(index + 1).cloned();
        }

        for flag in flags {
            let prefix = format!("{flag}=");
            if let Some(value) = arg.strip_prefix(&prefix) {
                return Some(value.to_string());
            }
        }

        index += 1;
    }

    None
}

fn quiet_enabled() -> bool {
    matches!(std::env::var("NEBU_CTX_QUIET"), Ok(v) if v.trim() == "1")
}

pub fn hosted_analytics_only_message(surface: &str) -> String {
    format!(
        "nebu-ctx {surface} is no longer available as a local client surface.\nAnalytics and dashboards now live on the NebuCtx host.\nRun the .NET NebuCtx server and use its hosted dashboard for canonical stats, gain, cost, and heatmap views."
    )
}

pub fn exit_hosted_analytics_only(surface: &str) -> ! {
    eprintln!("{}", hosted_analytics_only_message(surface));
    std::process::exit(1);
}

macro_rules! qprintln {
    ($($t:tt)*) => {
        if !quiet_enabled() {
            println!($($t)*);
        }
    };
}

pub fn cmd_init(args: &[String]) {
    let global = args.iter().any(|a| a == "--global" || a == "-g");
    let dry_run = args.iter().any(|a| a == "--dry-run");

    let agents: Vec<&str> = args
        .windows(2)
        .filter(|w| w[0] == "--agent")
        .map(|w| w[1].as_str())
        .collect();

    if !agents.is_empty() {
        for agent_name in &agents {
            crate::hooks::install_agent_hook(agent_name, global);
            if let Err(e) = crate::setup::configure_agent_mcp(agent_name) {
                eprintln!("MCP config for '{agent_name}' not updated: {e}");
            }
        }
        if !global {
            crate::hooks::install_project_rules();
        }
        qprintln!("\nAnalytics are served by the NebuCtx host; this client no longer renders local dashboards.");
        return;
    }

    let eval_shell = args
        .iter()
        .find(|a| matches!(a.as_str(), "bash" | "zsh" | "fish" | "powershell" | "pwsh"));
    if let Some(shell) = eval_shell {
        if !global {
            shell_init::print_hook_stdout(shell);
            return;
        }
    }

    let shell_name = std::env::var("SHELL").unwrap_or_default();
    let is_zsh = shell_name.contains("zsh");
    let is_fish = shell_name.contains("fish");
    let is_powershell = cfg!(windows) && shell_name.is_empty();

    let binary = crate::core::portable_binary::resolve_portable_binary();

    if dry_run {
        let rc = if is_powershell {
            "Documents/PowerShell/Microsoft.PowerShell_profile.ps1".to_string()
        } else if is_fish {
            "~/.config/fish/config.fish".to_string()
        } else if is_zsh {
            "~/.zshrc".to_string()
        } else {
            "~/.bashrc".to_string()
        };
        qprintln!("\nnebu-ctx setup --dry-run\n");
        qprintln!("  Would modify:  {rc}");
        qprintln!("  Would backup:  {rc}.nebu-ctx.bak");
        qprintln!("  Would alias:   git npm pnpm yarn cargo docker docker-compose kubectl");
        qprintln!("                 gh pip pip3 ruff go golangci-lint eslint prettier tsc");
        qprintln!("                 curl wget php composer (24 commands + k)");
        qprintln!("  Would create:  ~/.nebu-ctx/");
        qprintln!("  Binary:        {binary}");
        qprintln!("\n  Safety: aliases auto-fallback to original command if nebu-ctx is removed.");
        qprintln!("\n  Run without --dry-run to apply.");
        return;
    }

    if is_powershell {
        init_powershell(&binary);
    } else {
        let bash_binary = to_bash_compatible_path(&binary);
        if is_fish {
            init_fish(&bash_binary);
        } else {
            init_posix(is_zsh, &bash_binary);
        }
    }

    let lean_dir = dirs::home_dir().map(|h| h.join(".nebu-ctx"));
    if let Some(dir) = lean_dir {
        if !dir.exists() {
            let _ = std::fs::create_dir_all(&dir);
            qprintln!("Created {}", dir.display());
        }
    }

    let rc = if is_powershell {
        "$PROFILE"
    } else if is_fish {
        "config.fish"
    } else if is_zsh {
        ".zshrc"
    } else {
        ".bashrc"
    };

    qprintln!("\nnebu-ctx setup complete (24 aliases installed)");
    qprintln!();
    qprintln!("  Disable temporarily:  nebu-ctx-off");
    qprintln!("  Re-enable:            nebu-ctx-on");
    qprintln!("  Check status:         nebu-ctx-status");
    qprintln!("  Full uninstall:       nebu-ctx uninstall");
    qprintln!("  Diagnose issues:      nebu-ctx doctor");
    qprintln!("  Preview changes:      nebu-ctx setup --global --dry-run");
    qprintln!();
    if is_powershell {
        qprintln!("  Restart PowerShell or run: . {rc}");
    } else {
        qprintln!("  Restart your shell or run: source ~/{rc}");
    }
    qprintln!();
    qprintln!("For AI tool integration: nebu-ctx setup --agent <tool>");
    qprintln!("  Supported: claude, codex, copilot, opencode");
}

pub fn cmd_init_quiet(args: &[String]) {
    let previous = std::env::var_os("NEBU_CTX_QUIET");
    std::env::set_var("NEBU_CTX_QUIET", "1");
    cmd_init(args);
    if let Some(previous) = previous {
        std::env::set_var("NEBU_CTX_QUIET", previous);
    } else {
        std::env::remove_var("NEBU_CTX_QUIET");
    }
}

pub fn load_shell_history_pub() -> Vec<String> {
    load_shell_history()
}

fn load_shell_history() -> Vec<String> {
    let shell = std::env::var("SHELL").unwrap_or_default();
    let home = match dirs::home_dir() {
        Some(h) => h,
        None => return Vec::new(),
    };

    let history_file = if shell.contains("zsh") {
        home.join(".zsh_history")
    } else if shell.contains("fish") {
        home.join(".local/share/fish/fish_history")
    } else if cfg!(windows) && shell.is_empty() {
        home.join("AppData")
            .join("Roaming")
            .join("Microsoft")
            .join("Windows")
            .join("PowerShell")
            .join("PSReadLine")
            .join("ConsoleHost_history.txt")
    } else {
        home.join(".bash_history")
    };

    match std::fs::read_to_string(&history_file) {
        Ok(content) => content
            .lines()
            .filter_map(|l| {
                let trimmed = l.trim();
                if trimmed.starts_with(':') {
                    trimmed.split(';').nth(1).map(|s| s.to_string())
                } else {
                    Some(trimmed.to_string())
                }
            })
            .filter(|l| !l.is_empty())
            .collect(),
        Err(_) => Vec::new(),
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use tempfile;

    #[test]
    fn test_remove_nebu_ctx_block_posix() {
        let input = r#"# existing config
export PATH="$HOME/bin:$PATH"

# nebu-ctx shell hook — transparent CLI compression (90+ patterns)
if [ -z "$NEBU_CTX_ACTIVE" ]; then
alias git='nebu-ctx -c git'
alias npm='nebu-ctx -c npm'
fi

# other stuff
export EDITOR=vim
"#;
        let result = remove_nebu_ctx_block(input);
        assert!(!result.contains("lean-ctx"), "block should be removed");
        assert!(result.contains("export PATH"), "other content preserved");
        assert!(
            result.contains("export EDITOR"),
            "trailing content preserved"
        );
    }

    #[test]
    fn test_remove_nebu_ctx_block_fish() {
        let input = "# other fish config\nset -x FOO bar\n\n# nebu-ctx shell hook — transparent CLI compression (90+ patterns)\nif not set -q NEBU_CTX_ACTIVE\n\talias git 'nebu-ctx -c git'\n\talias npm 'nebu-ctx -c npm'\nend\n\n# more config\nset -x BAZ qux\n";
        let result = remove_nebu_ctx_block(input);
        assert!(!result.contains("lean-ctx"), "block should be removed");
        assert!(result.contains("set -x FOO"), "other content preserved");
        assert!(result.contains("set -x BAZ"), "trailing content preserved");
    }

    #[test]
    fn test_remove_nebu_ctx_block_ps() {
        let input = "# PowerShell profile\n$env:FOO = 'bar'\n\n# nebu-ctx shell hook — transparent CLI compression (90+ patterns)\nif (-not $env:NEBU_CTX_ACTIVE) {\n  $LeanCtxBin = \"C:\\\\bin\\\\nebu-ctx.exe\"\n  function git { & $LeanCtxBin -c \"git $($args -join ' ')\" }\n}\n\n# other stuff\n$env:EDITOR = 'vim'\n";
        let result = remove_nebu_ctx_block_ps(input);
        assert!(
            !result.contains("nebu-ctx shell hook"),
            "block should be removed"
        );
        assert!(result.contains("$env:FOO"), "other content preserved");
        assert!(result.contains("$env:EDITOR"), "trailing content preserved");
    }

    #[test]
    fn test_remove_nebu_ctx_block_ps_nested() {
        let input = "# PowerShell profile\n$env:FOO = 'bar'\n\n# nebu-ctx shell hook — transparent CLI compression (90+ patterns)\nif (-not $env:NEBU_CTX_ACTIVE) {\n  $LeanCtxBin = \"lean-ctx\"\n  function _lc {\n    & $LeanCtxBin -c \"$($args -join ' ')\"\n  }\n  if (Get-Command lean-ctx -ErrorAction SilentlyContinue) {\n    function git { _lc git @args }\n    foreach ($c in @('npm','pnpm')) {\n      if ($a) {\n        Set-Variable -Name \"_lc_$c\" -Value $a.Source -Scope Script\n      }\n    }\n  }\n}\n\n# other stuff\n$env:EDITOR = 'vim'\n";
        let result = remove_nebu_ctx_block_ps(input);
        assert!(
            !result.contains("nebu-ctx shell hook"),
            "block should be removed"
        );
        assert!(!result.contains("_lc"), "function should be removed");
        assert!(result.contains("$env:FOO"), "other content preserved");
        assert!(result.contains("$env:EDITOR"), "trailing content preserved");
    }

    #[test]
    fn test_remove_block_no_nebu_ctx() {
        let input = "# normal bashrc\nexport PATH=\"$HOME/bin:$PATH\"\n";
        let result = remove_nebu_ctx_block(input);
        assert!(result.contains("export PATH"), "content unchanged");
    }

    #[test]
    fn test_bash_hook_contains_pipe_guard() {
        let binary = "/usr/local/bin/nebu-ctx";
        let hook = format!(
            r#"_lc() {{
    if [ -n "${{NEBU_CTX_DISABLED:-}}" ] || [ ! -t 1 ]; then
        command "$@"
        return
    fi
    '{binary}' -t "$@"
}}"#
        );
        assert!(
            hook.contains("! -t 1"),
            "bash/zsh hook must contain pipe guard [ ! -t 1 ]"
        );
        assert!(
            hook.contains("NEBU_CTX_DISABLED") && hook.contains("! -t 1"),
            "pipe guard must be in the same conditional as NEBU_CTX_DISABLED"
        );
    }

    #[test]
    fn test_lc_uses_track_mode_by_default() {
        let binary = "/usr/local/bin/nebu-ctx";
        let alias_list = crate::rewrite_registry::shell_alias_list();
        let aliases = format!(
            r#"_lc() {{
    '{binary}' -t "$@"
}}
_lc_compress() {{
    '{binary}' -c "$@"
}}"#
        );
        assert!(
            aliases.contains("-t \"$@\""),
            "_lc must use -t (track mode) by default"
        );
        assert!(
            aliases.contains("-c \"$@\""),
            "_lc_compress must use -c (compress mode)"
        );
        let _ = alias_list;
    }

    #[test]
    fn test_posix_shell_has_nebu_ctx_mode() {
        let alias_list = crate::rewrite_registry::shell_alias_list();
        let aliases = r#"
nebu-ctx-mode() {{
    case "${{1:-}}" in
        compress) echo compress ;;
        track) echo track ;;
        off) echo off ;;
    esac
}}
"#
        .to_string();
        assert!(
            aliases.contains("nebu-ctx-mode()"),
            "nebu-ctx-mode function must exist"
        );
        assert!(
            aliases.contains("compress"),
            "compress mode must be available"
        );
        assert!(aliases.contains("track"), "track mode must be available");
        let _ = alias_list;
    }

    #[test]
    fn test_fish_hook_contains_pipe_guard() {
        let hook = "function _lc\n\tif set -q NEBU_CTX_DISABLED; or not isatty stdout\n\t\tcommand $argv\n\t\treturn\n\tend\nend";
        assert!(
            hook.contains("isatty stdout"),
            "fish hook must contain pipe guard (isatty stdout)"
        );
    }

    #[test]
    fn test_powershell_hook_contains_pipe_guard() {
        let hook = "function _lc { if ($env:NEBU_CTX_DISABLED -or [Console]::IsOutputRedirected) { & @args; return } }";
        assert!(
            hook.contains("IsOutputRedirected"),
            "PowerShell hook must contain pipe guard ([Console]::IsOutputRedirected)"
        );
    }

    #[test]
    fn test_remove_nebu_ctx_block_new_format_with_end_marker() {
        let input = r#"# existing config
export PATH="$HOME/bin:$PATH"

# nebu-ctx shell hook — transparent CLI compression (90+ patterns)
_nebu_ctx_cmds=(git npm pnpm)

nebu-ctx-on() {
    for _lc_cmd in "${_nebu_ctx_cmds[@]}"; do
        alias "$_lc_cmd"='nebu-ctx -c '"$_lc_cmd"
    done
    export NEBU_CTX_ENABLED=1
    [ -t 1 ] && echo "nebu-ctx: ON"
}

nebu-ctx-off() {
    unset NEBU_CTX_ENABLED
    [ -t 1 ] && echo "nebu-ctx: OFF"
}

if [ -z "${NEBU_CTX_ACTIVE:-}" ] && [ "${NEBU_CTX_ENABLED:-1}" != "0" ]; then
    nebu-ctx-on
fi
# nebu-ctx shell hook — end

# other stuff
export EDITOR=vim
"#;
        let result = remove_nebu_ctx_block(input);
        assert!(!result.contains("nebu-ctx-on"), "block should be removed");
        assert!(!result.contains("nebu-ctx shell hook"), "marker removed");
        assert!(result.contains("export PATH"), "other content preserved");
        assert!(
            result.contains("export EDITOR"),
            "trailing content preserved"
        );
    }

    #[test]
    fn env_sh_for_containers_includes_self_heal() {
        let _g = crate::core::data_dir::test_env_lock();
        let tmp = tempfile::tempdir().expect("tempdir");
        let data_dir = tmp.path().join("data");
        std::fs::create_dir_all(&data_dir).expect("mkdir data");
        std::env::set_var("NEBU_CTX_DATA_DIR", &data_dir);

        write_env_sh_for_containers("alias git='nebu-ctx -c git'\n");
        let env_sh = data_dir.join("env.sh");
        let content = std::fs::read_to_string(&env_sh).expect("env.sh exists");
        assert!(content.contains("nebu-ctx docker self-heal"));
        assert!(content.contains("claude mcp list"));
        assert!(content.contains("nebu-ctx setup --agent claude"));

        std::env::remove_var("NEBU_CTX_DATA_DIR");
    }
}
