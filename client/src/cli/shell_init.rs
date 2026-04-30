macro_rules! qprintln {
    ($($t:tt)*) => {
        if !super::quiet_enabled() {
            println!($($t)*);
        }
    };
}

pub fn print_hook_stdout(shell: &str) {
    let binary = crate::core::portable_binary::resolve_portable_binary();
    let binary = crate::hooks::to_bash_compatible_path(&binary);

    let code = match shell {
        "bash" => generate_hook_posix(&binary),
        "zsh" => generate_hook_posix(&binary),
        "fish" => generate_hook_fish(&binary),
        "powershell" | "pwsh" => generate_hook_powershell(&binary),
        _ => {
            eprintln!("nebu-ctx: unsupported shell '{shell}'");
            eprintln!("Supported: bash, zsh, fish, powershell");
            std::process::exit(1);
        }
    };
    print!("{code}");
}

fn backup_shell_config(path: &std::path::Path) {
    if !path.exists() {
        return;
    }
    let bak = path.with_extension("nebu-ctx.bak");
    if std::fs::copy(path, &bak).is_ok() {
        qprintln!(
            "  Backup: {}",
            bak.file_name()
                .map(|n| format!("~/{}", n.to_string_lossy()))
                .unwrap_or_else(|| bak.display().to_string())
        );
    }
}

fn nebu_ctx_dir() -> Option<std::path::PathBuf> {
    dirs::home_dir().map(|h| h.join(".nebu-ctx"))
}

fn write_hook_file(filename: &str, content: &str) -> Option<std::path::PathBuf> {
    let dir = nebu_ctx_dir()?;
    let _ = std::fs::create_dir_all(&dir);
    let path = dir.join(filename);
    match std::fs::write(&path, content) {
        Ok(()) => Some(path),
        Err(e) => {
            eprintln!("Error writing {}: {e}", path.display());
            None
        }
    }
}

fn source_line_posix(shell_ext: &str) -> String {
    format!(
        r#"# nebu-ctx shell hook
[ -f "$HOME/.nebu-ctx/shell-hook.{shell_ext}" ] && . "$HOME/.nebu-ctx/shell-hook.{shell_ext}"
"#
    )
}

fn source_line_fish() -> String {
    r#"# nebu-ctx shell hook
fish_add_path "$HOME/.cargo/bin"
if test -f "$HOME/.nebu-ctx/shell-hook.fish"
    source "$HOME/.nebu-ctx/shell-hook.fish"
end
"#
    .to_string()
}

fn source_line_powershell() -> String {
    r#"# nebu-ctx shell hook
$nebuCtxHook = Join-Path $HOME ".nebu-ctx" "shell-hook.ps1"
if (Test-Path $nebuCtxHook) { . $nebuCtxHook }
"#
    .to_string()
}

fn upsert_source_line(rc_path: &std::path::Path, source_line: &str) {
    backup_shell_config(rc_path);

    if let Ok(existing) = std::fs::read_to_string(rc_path) {
        if existing.contains(".nebu-ctx/shell-hook.") {
            return;
        }

        let cleaned = if existing.contains("nebu-ctx shell hook") {
            remove_nebu_ctx_block(&existing)
        } else {
            existing
        };

        match std::fs::write(rc_path, format!("{cleaned}{source_line}")) {
            Ok(()) => {
                qprintln!("Updated nebu-ctx hook in {}", rc_path.display());
            }
            Err(e) => {
                eprintln!("Error updating {}: {e}", rc_path.display());
            }
        }
        return;
    }

    match std::fs::OpenOptions::new()
        .append(true)
        .create(true)
        .open(rc_path)
    {
        Ok(mut f) => {
            use std::io::Write;
            let _ = f.write_all(source_line.as_bytes());
            qprintln!("Added nebu-ctx hook to {}", rc_path.display());
        }
        Err(e) => eprintln!("Error writing {}: {e}", rc_path.display()),
    }
}

pub fn generate_hook_powershell(binary: &str) -> String {
    let binary_escaped = binary.replace('\\', "\\\\");
    format!(
        r#"# nebu-ctx shell hook — transparent CLI compression (90+ patterns)
if (-not $env:NEBU_CTX_ACTIVE -and -not $env:NEBU_CTX_DISABLED) {{
  $NebuCtxBin = "{binary_escaped}"
  function _lc {{
    if ($env:NEBU_CTX_DISABLED -or [Console]::IsOutputRedirected) {{ & @args; return }}
    & $NebuCtxBin -c @args
    if ($LASTEXITCODE -eq 127 -or $LASTEXITCODE -eq 126) {{
      & @args
    }}
  }}
    function nebu-ctx-raw {{ $env:NEBU_CTX_RAW = '1'; & @args; Remove-Item Env:NEBU_CTX_RAW -ErrorAction SilentlyContinue }}
    function git {{ _lc git @args }}
    function cargo {{ _lc cargo @args }}
    function docker {{ _lc docker @args }}
    function kubectl {{ _lc kubectl @args }}
    function gh {{ _lc gh @args }}
    function pip {{ _lc pip @args }}
    function pip3 {{ _lc pip3 @args }}
    function ruff {{ _lc ruff @args }}
    function go {{ _lc go @args }}
    function curl {{ _lc curl @args }}
    function wget {{ _lc wget @args }}
    foreach ($c in @('npm','pnpm','yarn','eslint','prettier','tsc')) {{
        if (Get-Command $c -CommandType Application -ErrorAction SilentlyContinue) {{
            New-Item -Path "function:$c" -Value ([scriptblock]::Create("_lc $c @args")) -Force | Out-Null
        }}
    }}
}}
"#
    )
}

pub fn init_powershell(binary: &str) {
    let profile_dir = dirs::home_dir().map(|h| h.join("Documents").join("PowerShell"));
    let profile_path = match profile_dir {
        Some(dir) => {
            let _ = std::fs::create_dir_all(&dir);
            dir.join("Microsoft.PowerShell_profile.ps1")
        }
        None => {
            eprintln!("Could not resolve PowerShell profile directory");
            return;
        }
    };

    let hook_content = generate_hook_powershell(binary);

    if write_hook_file("shell-hook.ps1", &hook_content).is_some() {
        upsert_source_line(&profile_path, &source_line_powershell());
        qprintln!("  Binary: {binary}");
    }
}

pub fn remove_nebu_ctx_block_ps(content: &str) -> String {
    let mut result = String::new();
    let mut in_block = false;
    let mut brace_depth = 0i32;

    for line in content.lines() {
        if line.contains("nebu-ctx shell hook") {
            in_block = true;
            continue;
        }
        if in_block {
            brace_depth += line.matches('{').count() as i32;
            brace_depth -= line.matches('}').count() as i32;
            if brace_depth <= 0 && (line.trim() == "}" || line.trim().is_empty()) {
                if line.trim() == "}" {
                    in_block = false;
                    brace_depth = 0;
                }
                continue;
            }
            continue;
        }
        result.push_str(line);
        result.push('\n');
    }
    result
}

pub fn generate_hook_fish(binary: &str) -> String {
    let alias_list = crate::rewrite_registry::shell_alias_list();
    format!(
        "# nebu-ctx shell hook — smart shell mode (track-by-default)\n\
        set -g _nebu_ctx_cmds {alias_list}\n\
        \n\
        function _lc\n\
        \tif set -q NEBU_CTX_DISABLED; or not isatty stdout\n\
        \t\tcommand $argv\n\
        \t\treturn\n\
        \tend\n\
        \t'{binary}' -t $argv\n\
        \tset -l _lc_rc $status\n\
        \tif test $_lc_rc -eq 127 -o $_lc_rc -eq 126\n\
        \t\tcommand $argv\n\
        \telse\n\
        \t\treturn $_lc_rc\n\
        \tend\n\
        end\n\
        \n\
        function _lc_compress\n\
        \tif set -q NEBU_CTX_DISABLED; or not isatty stdout\n\
        \t\tcommand $argv\n\
        \t\treturn\n\
        \tend\n\
        \t'{binary}' -c $argv\n\
        \tset -l _lc_rc $status\n\
        \tif test $_lc_rc -eq 127 -o $_lc_rc -eq 126\n\
        \t\tcommand $argv\n\
        \telse\n\
        \t\treturn $_lc_rc\n\
        \tend\n\
        end\n\
        \n\
        function nebu-ctx-on\n\
        \tfor _lc_cmd in $_nebu_ctx_cmds\n\
        \t\talias $_lc_cmd '_lc '$_lc_cmd\n\
        \tend\n\
        \talias k '_lc kubectl'\n\
        \tset -gx NEBU_CTX_ENABLED 1\n\
        \tisatty stdout; and echo 'nebu-ctx: ON (track mode — full output, stats recorded)'\n\
        end\n\
        \n\
        function nebu-ctx-off\n\
        \tfor _lc_cmd in $_nebu_ctx_cmds\n\
        \t\tfunctions --erase $_lc_cmd 2>/dev/null; true\n\
        \tend\n\
        \tfunctions --erase k 2>/dev/null; true\n\
        \tset -e NEBU_CTX_ENABLED\n\
        \tisatty stdout; and echo 'nebu-ctx: OFF'\n\
        end\n\
        \n\
        function nebu-ctx-mode\n\
        \tswitch $argv[1]\n\
        \t\tcase compress\n\
        \t\t\tfor _lc_cmd in $_nebu_ctx_cmds\n\
        \t\t\t\talias $_lc_cmd '_lc_compress '$_lc_cmd\n\
        \t\t\t\tend\n\
        \t\t\talias k '_lc_compress kubectl'\n\
        \t\t\tset -gx NEBU_CTX_ENABLED 1\n\
        \t\t\tisatty stdout; and echo 'nebu-ctx: COMPRESS mode (all output compressed)'\n\
        \t\tcase track\n\
        \t\t\tnebu-ctx-on\n\
        \t\tcase off\n\
        \t\t\tnebu-ctx-off\n\
        \t\tcase '*'\n\
        \t\t\techo 'Usage: nebu-ctx-mode <track|compress|off>'\n\
        \t\t\techo '  track    — Full output, stats recorded (default)'\n\
        \t\t\techo '  compress — Compressed output for all commands'\n\
        \t\t\techo '  off      — No aliases, raw shell'\n\
        \tend\n\
        end\n\
        \n\
        function nebu-ctx-raw\n\
        \tset -lx NEBU_CTX_RAW 1\n\
        \tcommand $argv\n\
        end\n\
        \n\
        function nebu-ctx-status\n\
        \tif set -q NEBU_CTX_DISABLED\n\
        \t\tisatty stdout; and echo 'nebu-ctx: DISABLED (NEBU_CTX_DISABLED is set)'\n\
        \telse if set -q NEBU_CTX_ENABLED\n\
        \t\tisatty stdout; and echo 'nebu-ctx: ON'\n\
        \telse\n\
        \t\tisatty stdout; and echo 'nebu-ctx: OFF'\n\
        \tend\n\
        end\n\
        \n\
        if not set -q NEBU_CTX_ACTIVE; and not set -q NEBU_CTX_DISABLED; and test (set -q NEBU_CTX_ENABLED; and echo $NEBU_CTX_ENABLED; or echo 1) != '0'\n\
    	nebu-ctx-on\n\
        end\n"
    )
}

pub fn init_fish(binary: &str) {
    let config = dirs::home_dir()
        .map(|h| h.join(".config/fish/config.fish"))
        .unwrap_or_default();

    let hook_content = generate_hook_fish(binary);

    if write_hook_file("shell-hook.fish", &hook_content).is_some() {
        upsert_source_line(&config, &source_line_fish());
        qprintln!("  Binary: {binary}");
    }
}

pub fn generate_hook_posix(binary: &str) -> String {
    let alias_list = crate::rewrite_registry::shell_alias_list();
    format!(
        r#"# nebu-ctx shell hook — smart shell mode (track-by-default)
_nebu_ctx_cmds=({alias_list})

_lc() {{
    if [ -n "${{NEBU_CTX_DISABLED:-}}" ] || [ ! -t 1 ]; then
        command "$@"
        return
    fi
    '{binary}' -t "$@"
    local _lc_rc=$?
    if [ "$_lc_rc" -eq 127 ] || [ "$_lc_rc" -eq 126 ]; then
        command "$@"
    else
        return "$_lc_rc"
    fi
}}

_lc_compress() {{
    if [ -n "${{NEBU_CTX_DISABLED:-}}" ] || [ ! -t 1 ]; then
        command "$@"
        return
    fi
    '{binary}' -c "$@"
    local _lc_rc=$?
    if [ "$_lc_rc" -eq 127 ] || [ "$_lc_rc" -eq 126 ]; then
        command "$@"
    else
        return "$_lc_rc"
    fi
}}

nebu-ctx-on() {{
    for _lc_cmd in "${{_nebu_ctx_cmds[@]}}"; do
        # shellcheck disable=SC2139
        alias "$_lc_cmd"='_lc '"$_lc_cmd"
    done
    alias k='_lc kubectl'
    export NEBU_CTX_ENABLED=1
    [ -t 1 ] && echo "nebu-ctx: ON (track mode — full output, stats recorded)"
}}

nebu-ctx-off() {{
    for _lc_cmd in "${{_nebu_ctx_cmds[@]}}"; do
        unalias "$_lc_cmd" 2>/dev/null || true
    done
    unalias k 2>/dev/null || true
    unset NEBU_CTX_ENABLED
    [ -t 1 ] && echo "nebu-ctx: OFF"
}}

nebu-ctx-mode() {{
    case "${{1:-}}" in
        compress)
            for _lc_cmd in "${{_nebu_ctx_cmds[@]}}"; do
                # shellcheck disable=SC2139
                alias "$_lc_cmd"='_lc_compress '"$_lc_cmd"
            done
            alias k='_lc_compress kubectl'
            export NEBU_CTX_ENABLED=1
            [ -t 1 ] && echo "nebu-ctx: COMPRESS mode (all output compressed)"
            ;;
        track)
            nebu-ctx-on
            ;;
        off)
            nebu-ctx-off
            ;;
        *)
            echo "Usage: nebu-ctx-mode <track|compress|off>"
            echo "  track    — Full output, stats recorded (default)"
            echo "  compress — Compressed output for all commands"
            echo "  off      — No aliases, raw shell"
            ;;
    esac
}}

nebu-ctx-raw() {{
    NEBU_CTX_RAW=1 command "$@"
}}

nebu-ctx-status() {{
    if [ -n "${{NEBU_CTX_DISABLED:-}}" ]; then
        [ -t 1 ] && echo "nebu-ctx: DISABLED (NEBU_CTX_DISABLED is set)"
    elif [ -n "${{NEBU_CTX_ENABLED:-}}" ]; then
        [ -t 1 ] && echo "nebu-ctx: ON"
    else
        [ -t 1 ] && echo "nebu-ctx: OFF"
    fi
}}

if [ -z "${{NEBU_CTX_ACTIVE:-}}" ] && [ -z "${{NEBU_CTX_DISABLED:-}}" ] && [ "${{NEBU_CTX_ENABLED:-1}}" != "0" ]; then
    nebu-ctx-on
fi
"#
    )
}

pub fn init_posix(is_zsh: bool, binary: &str) {
    let rc_file = if is_zsh {
        dirs::home_dir()
            .map(|h| h.join(".zshrc"))
            .unwrap_or_default()
    } else {
        dirs::home_dir()
            .map(|h| h.join(".bashrc"))
            .unwrap_or_default()
    };

    let shell_ext = if is_zsh { "zsh" } else { "bash" };
    let hook_content = generate_hook_posix(binary);

    if let Some(hook_path) = write_hook_file(&format!("shell-hook.{shell_ext}"), &hook_content) {
        upsert_source_line(&rc_file, &source_line_posix(shell_ext));
        qprintln!("  Binary: {binary}");

        write_env_sh_for_containers(&hook_content);
        print_docker_env_hints(is_zsh);

        let _ = hook_path;
    }
}

pub fn write_env_sh_for_containers(aliases: &str) {
    let env_sh = match crate::core::data_dir::nebu_ctx_data_dir() {
        Ok(d) => d.join("env.sh"),
        Err(_) => return,
    };
    if let Some(parent) = env_sh.parent() {
        let _ = std::fs::create_dir_all(parent);
    }
    let sanitized_aliases = crate::core::sanitize::neutralize_shell_content(aliases);
    let mut content = sanitized_aliases;
    content.push_str(
        r#"

# nebu-ctx docker self-heal: re-inject Claude MCP config if Claude overwrote ~/.claude.json
if command -v claude >/dev/null 2>&1 && command -v nebu-ctx >/dev/null 2>&1; then
    if ! claude mcp list 2>/dev/null | grep -Eq "nebu-ctx|nebu-ctx"; then
        NEBU_CTX_QUIET=1 nebu-ctx init --agent claude >/dev/null 2>&1
  fi
fi
"#,
    );
    match std::fs::write(&env_sh, content) {
        Ok(()) => {
            if !matches!(std::env::var("NEBU_CTX_QUIET"), Ok(value) if value.trim() == "1") {
                println!("  env.sh: {}", env_sh.display());
            }
        }
        Err(e) => eprintln!("  Warning: could not write {}: {e}", env_sh.display()),
    }
}

fn print_docker_env_hints(is_zsh: bool) {
    if is_zsh || !crate::shell::is_container() {
        return;
    }
    let env_sh = crate::core::data_dir::nebu_ctx_data_dir()
        .map(|d| d.join("env.sh").to_string_lossy().to_string())
        .unwrap_or_else(|_| "/root/.nebu-ctx/env.sh".to_string());

    let has_bash_env = std::env::var("BASH_ENV").is_ok();
    let has_claude_env = std::env::var("CLAUDE_ENV_FILE").is_ok();

    if has_bash_env && has_claude_env {
        return;
    }

    eprintln!();
    eprintln!("  \x1b[33m⚠  Docker detected — environment hints:\x1b[0m");

    if !has_bash_env {
        eprintln!("  For generic bash -c usage (non-interactive shells):");
        eprintln!("    \x1b[1mENV BASH_ENV=\"{env_sh}\"\x1b[0m");
    }
    if !has_claude_env {
        eprintln!("  For Claude Code (sources before each command):");
        eprintln!("    \x1b[1mENV CLAUDE_ENV_FILE=\"{env_sh}\"\x1b[0m");
    }
    eprintln!();
}

pub fn remove_nebu_ctx_block(content: &str) -> String {
    if content.contains("# nebu-ctx shell hook — end") {
        return remove_nebu_ctx_block_by_marker(content);
    }
    remove_nebu_ctx_block_legacy(content)
}

fn remove_nebu_ctx_block_by_marker(content: &str) -> String {
    let mut result = String::new();
    let mut in_block = false;

    for line in content.lines() {
        if !in_block && line.contains("nebu-ctx shell hook") && !line.contains("end") {
            in_block = true;
            continue;
        }
        if in_block {
            if line.trim() == "# nebu-ctx shell hook — end" {
                in_block = false;
            }
            continue;
        }
        result.push_str(line);
        result.push('\n');
    }
    result
}

fn remove_nebu_ctx_block_legacy(content: &str) -> String {
    let mut result = String::new();
    let mut in_block = false;

    for line in content.lines() {
        if line.contains("nebu-ctx shell hook") {
            in_block = true;
            continue;
        }
        if in_block {
            if line.trim() == "fi" || line.trim() == "end" || line.trim().is_empty() {
                if line.trim() == "fi" || line.trim() == "end" {
                    in_block = false;
                }
                continue;
            }
            if !line.starts_with("alias ") && !line.starts_with('\t') && !line.starts_with("if ") {
                in_block = false;
                result.push_str(line);
                result.push('\n');
            }
            continue;
        }
        result.push_str(line);
        result.push('\n');
    }
    result
}

#[cfg(test)]
mod tests {
    use super::*;

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
        assert!(!result.contains("nebu-ctx"), "block should be removed");
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
        assert!(!result.contains("nebu-ctx"), "block should be removed");
        assert!(result.contains("set -x FOO"), "other content preserved");
        assert!(result.contains("set -x BAZ"), "trailing content preserved");
    }

    #[test]
    fn test_remove_nebu_ctx_block_ps() {
        let input = "# PowerShell profile\n$env:FOO = 'bar'\n\n# nebu-ctx shell hook — transparent CLI compression (90+ patterns)\nif (-not $env:NEBU_CTX_ACTIVE) {\n  $NebuCtxBin = \"C:\\\\bin\\\\nebu-ctx.exe\"\n  function git { & $NebuCtxBin -c \"git $($args -join ' ')\" }\n}\n\n# other stuff\n$env:EDITOR = 'vim'\n";
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
        let input = "# PowerShell profile\n$env:FOO = 'bar'\n\n# nebu-ctx shell hook — transparent CLI compression (90+ patterns)\nif (-not $env:NEBU_CTX_ACTIVE) {\n  $NebuCtxBin = \"nebu-ctx\"\n  function _lc {\n    & $NebuCtxBin -c \"$($args -join ' ')\"\n  }\n  if (Get-Command nebu-ctx -ErrorAction SilentlyContinue) {\n    function git { _lc git @args }\n    foreach ($c in @('npm','pnpm')) {\n      if ($a) {\n        Set-Variable -Name \"_lc_$c\" -Value $a.Source -Scope Script\n      }\n    }\n  }\n}\n\n# other stuff\n$env:EDITOR = 'vim'\n";
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
        assert!(content.contains("nebu-ctx init --agent claude"));

        std::env::remove_var("NEBU_CTX_DATA_DIR");
    }

    #[test]
    fn test_source_line_posix() {
        let line = source_line_posix("zsh");
        assert!(line.contains("shell-hook.zsh"));
        assert!(line.contains("[ -f"));
    }

    #[test]
    fn test_source_line_fish() {
        let line = source_line_fish();
        assert!(line.contains("shell-hook.fish"));
        assert!(line.contains("source"));
    }

    #[test]
    fn test_source_line_powershell() {
        let line = source_line_powershell();
        assert!(line.contains("shell-hook.ps1"));
        assert!(line.contains("Test-Path"));
    }
}
