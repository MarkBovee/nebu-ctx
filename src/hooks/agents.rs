use std::path::PathBuf;

use super::{
    full_server_entry, generate_compact_rewrite_script, generate_rewrite_script,
    install_mcp_json_agent, install_project_rules, is_inside_git_repo, make_executable,
    mcp_server_quiet_mode, resolve_binary_path, resolve_binary_path_for_bash, write_file,
    KIRO_STEERING_TEMPLATE, REDIRECT_SCRIPT_CLAUDE, REDIRECT_SCRIPT_GENERIC,
};

pub(super) fn install_claude_hook(global: bool) {
    let home = match dirs::home_dir() {
        Some(h) => h,
        None => {
            eprintln!("Cannot resolve home directory");
            return;
        }
    };

    install_claude_hook_scripts(&home);
    install_claude_hook_config(&home);

    let scope = crate::core::config::Config::load().rules_scope_effective();
    if scope != crate::core::config::RulesScope::Project {
        install_claude_rules_file(&home);
        install_claude_global_claude_md(&home);
        install_claude_skill(&home);
    }

    let _ = global;
}

const CLAUDE_MD_BLOCK_START: &str = "<!-- lean-ctx -->";
const CLAUDE_MD_BLOCK_END: &str = "<!-- /lean-ctx -->";
const CLAUDE_MD_BLOCK_VERSION: &str = "lean-ctx-claude-v2";

const CLAUDE_MD_BLOCK_CONTENT: &str = "\
<!-- lean-ctx -->
<!-- lean-ctx-claude-v2 -->
## lean-ctx — Context Runtime

Always prefer lean-ctx MCP tools over native equivalents:
- `ctx_read` instead of `Read` / `cat` (cached, 10 modes, re-reads ~13 tokens)
- `ctx_shell` instead of `bash` / `Shell` (90+ compression patterns)
- `ctx_search` instead of `Grep` / `rg` (compact results)
- `ctx_tree` instead of `ls` / `find` (compact directory maps)
- Native Edit/StrReplace stay unchanged. If Edit requires Read and Read is unavailable, use `ctx_edit(path, old_string, new_string)` instead.
- Write, Delete, Glob — use normally.

Full rules: @rules/lean-ctx.md

Verify setup: run `/mcp` to check lean-ctx is connected, `/memory` to confirm this file loaded.
<!-- /lean-ctx -->";

fn install_claude_global_claude_md(home: &std::path::Path) {
    let claude_dir = crate::core::editor_registry::claude_state_dir(home);
    let _ = std::fs::create_dir_all(&claude_dir);
    let claude_md_path = claude_dir.join("CLAUDE.md");

    let existing = std::fs::read_to_string(&claude_md_path).unwrap_or_default();

    if existing.contains(CLAUDE_MD_BLOCK_START) {
        if existing.contains(CLAUDE_MD_BLOCK_VERSION) {
            return;
        }
        let cleaned = remove_block(&existing, CLAUDE_MD_BLOCK_START, CLAUDE_MD_BLOCK_END);
        let updated = format!("{}\n\n{}\n", cleaned.trim(), CLAUDE_MD_BLOCK_CONTENT);
        write_file(&claude_md_path, &updated);
        return;
    }

    if existing.trim().is_empty() {
        write_file(&claude_md_path, CLAUDE_MD_BLOCK_CONTENT);
    } else {
        let updated = format!("{}\n\n{}\n", existing.trim(), CLAUDE_MD_BLOCK_CONTENT);
        write_file(&claude_md_path, &updated);
    }
}

fn remove_block(content: &str, start: &str, end: &str) -> String {
    let s = content.find(start);
    let e = content.find(end);
    match (s, e) {
        (Some(si), Some(ei)) if ei >= si => {
            let after_end = ei + end.len();
            let before = content[..si].trim_end_matches('\n');
            let after = &content[after_end..];
            let mut out = before.to_string();
            out.push('\n');
            if !after.trim().is_empty() {
                out.push('\n');
                out.push_str(after.trim_start_matches('\n'));
            }
            out
        }
        _ => content.to_string(),
    }
}

fn install_claude_skill(home: &std::path::Path) {
    let skill_dir = home.join(".claude/skills/lean-ctx");
    let _ = std::fs::create_dir_all(skill_dir.join("scripts"));

    let skill_md = include_str!("../../skills/lean-ctx/SKILL.md");
    let install_sh = include_str!("../../skills/lean-ctx/scripts/install.sh");

    let skill_path = skill_dir.join("SKILL.md");
    let script_path = skill_dir.join("scripts/install.sh");

    write_file(&skill_path, skill_md);
    write_file(&script_path, install_sh);

    #[cfg(unix)]
    {
        use std::os::unix::fs::PermissionsExt;
        if let Ok(mut perms) = std::fs::metadata(&script_path).map(|m| m.permissions()) {
            perms.set_mode(0o755);
            let _ = std::fs::set_permissions(&script_path, perms);
        }
    }
}

fn install_claude_rules_file(home: &std::path::Path) {
    let rules_dir = crate::core::editor_registry::claude_rules_dir(home);
    let _ = std::fs::create_dir_all(&rules_dir);
    let rules_path = rules_dir.join("lean-ctx.md");

    let desired = crate::rules_inject::rules_dedicated_markdown();
    let existing = std::fs::read_to_string(&rules_path).unwrap_or_default();

    if existing.is_empty() {
        write_file(&rules_path, desired);
        return;
    }
    if existing.contains(crate::rules_inject::RULES_VERSION_STR) {
        return;
    }
    if existing.contains("<!-- lean-ctx-rules-") {
        write_file(&rules_path, desired);
    }
}

pub(super) fn install_claude_hook_scripts(home: &std::path::Path) {
    let hooks_dir = crate::core::editor_registry::claude_state_dir(home).join("hooks");
    let _ = std::fs::create_dir_all(&hooks_dir);

    let binary = resolve_binary_path();

    let rewrite_path = hooks_dir.join("lean-ctx-rewrite.sh");
    let rewrite_script = generate_rewrite_script(&resolve_binary_path_for_bash());
    write_file(&rewrite_path, &rewrite_script);
    make_executable(&rewrite_path);

    let redirect_path = hooks_dir.join("lean-ctx-redirect.sh");
    write_file(&redirect_path, REDIRECT_SCRIPT_CLAUDE);
    make_executable(&redirect_path);

    let wrapper = |subcommand: &str| -> String {
        if cfg!(windows) {
            format!("{binary} hook {subcommand}")
        } else {
            format!("{} hook {subcommand}", resolve_binary_path_for_bash())
        }
    };

    let rewrite_native = hooks_dir.join("lean-ctx-rewrite-native");
    write_file(
        &rewrite_native,
        &format!(
            "#!/bin/sh\nexec {} hook rewrite\n",
            resolve_binary_path_for_bash()
        ),
    );
    make_executable(&rewrite_native);

    let redirect_native = hooks_dir.join("lean-ctx-redirect-native");
    write_file(
        &redirect_native,
        &format!(
            "#!/bin/sh\nexec {} hook redirect\n",
            resolve_binary_path_for_bash()
        ),
    );
    make_executable(&redirect_native);

    let _ = wrapper; // suppress unused warning on unix
}

pub(super) fn install_claude_hook_config(home: &std::path::Path) {
    let hooks_dir = crate::core::editor_registry::claude_state_dir(home).join("hooks");
    let binary = resolve_binary_path();

    let rewrite_cmd = format!("{binary} hook rewrite");
    let redirect_cmd = format!("{binary} hook redirect");

    let settings_path = crate::core::editor_registry::claude_state_dir(home).join("settings.json");
    let settings_content = if settings_path.exists() {
        std::fs::read_to_string(&settings_path).unwrap_or_default()
    } else {
        String::new()
    };

    let needs_update =
        !settings_content.contains("hook rewrite") || !settings_content.contains("hook redirect");
    let has_old_hooks = settings_content.contains("lean-ctx-rewrite.sh")
        || settings_content.contains("lean-ctx-redirect.sh");

    if !needs_update && !has_old_hooks {
        return;
    }

    let hook_entry = serde_json::json!({
        "hooks": {
            "PreToolUse": [
                {
                    "matcher": "Bash|bash",
                    "hooks": [{
                        "type": "command",
                        "command": rewrite_cmd
                    }]
                },
                {
                    "matcher": "Read|read|ReadFile|read_file|View|view|Grep|grep|Search|search|ListFiles|list_files|ListDirectory|list_directory",
                    "hooks": [{
                        "type": "command",
                        "command": redirect_cmd
                    }]
                }
            ]
        }
    });

    if settings_content.is_empty() {
        write_file(
            &settings_path,
            &serde_json::to_string_pretty(&hook_entry).unwrap(),
        );
    } else if let Ok(mut existing) = serde_json::from_str::<serde_json::Value>(&settings_content) {
        if let Some(obj) = existing.as_object_mut() {
            obj.insert("hooks".to_string(), hook_entry["hooks"].clone());
            write_file(
                &settings_path,
                &serde_json::to_string_pretty(&existing).unwrap(),
            );
        }
    }
    if !mcp_server_quiet_mode() {
        println!("Installed Claude Code hooks at {}", hooks_dir.display());
    }
}

pub(super) fn install_claude_project_hooks(cwd: &std::path::Path) {
    let binary = resolve_binary_path();
    let rewrite_cmd = format!("{binary} hook rewrite");
    let redirect_cmd = format!("{binary} hook redirect");

    let settings_path = cwd.join(".claude").join("settings.local.json");
    let _ = std::fs::create_dir_all(cwd.join(".claude"));

    let existing = std::fs::read_to_string(&settings_path).unwrap_or_default();
    if existing.contains("hook rewrite") && existing.contains("hook redirect") {
        return;
    }

    let hook_entry = serde_json::json!({
        "hooks": {
            "PreToolUse": [
                {
                    "matcher": "Bash|bash",
                    "hooks": [{
                        "type": "command",
                        "command": rewrite_cmd
                    }]
                },
                {
                    "matcher": "Read|read|ReadFile|read_file|View|view|Grep|grep|Search|search|ListFiles|list_files|ListDirectory|list_directory",
                    "hooks": [{
                        "type": "command",
                        "command": redirect_cmd
                    }]
                }
            ]
        }
    });

    if existing.is_empty() {
        write_file(
            &settings_path,
            &serde_json::to_string_pretty(&hook_entry).unwrap(),
        );
    } else if let Ok(mut json) = serde_json::from_str::<serde_json::Value>(&existing) {
        if let Some(obj) = json.as_object_mut() {
            obj.insert("hooks".to_string(), hook_entry["hooks"].clone());
            write_file(
                &settings_path,
                &serde_json::to_string_pretty(&json).unwrap(),
            );
        }
    }
    println!("Created .claude/settings.local.json (project-local PreToolUse hooks).");
}

pub fn install_cursor_hook(global: bool) {
    let home = match dirs::home_dir() {
        Some(h) => h,
        None => {
            eprintln!("Cannot resolve home directory");
            return;
        }
    };

    install_cursor_hook_scripts(&home);
    install_cursor_hook_config(&home);

    let scope = crate::core::config::Config::load().rules_scope_effective();
    let skip_project = global || scope == crate::core::config::RulesScope::Global;

    if !skip_project {
        let rules_dir = PathBuf::from(".cursor").join("rules");
        let _ = std::fs::create_dir_all(&rules_dir);
        let rule_path = rules_dir.join("lean-ctx.mdc");
        if !rule_path.exists() {
            let rule_content = include_str!("../templates/lean-ctx.mdc");
            write_file(&rule_path, rule_content);
            println!("Created .cursor/rules/lean-ctx.mdc in current project.");
        } else {
            println!("Cursor rule already exists.");
        }
    } else {
        println!("Global mode: skipping project-local .cursor/rules/ (use without --global in a project).");
    }

    println!("Restart Cursor to activate.");
}

pub(super) fn install_cursor_hook_scripts(home: &std::path::Path) {
    let hooks_dir = home.join(".cursor").join("hooks");
    let _ = std::fs::create_dir_all(&hooks_dir);

    let binary = resolve_binary_path_for_bash();

    let rewrite_path = hooks_dir.join("lean-ctx-rewrite.sh");
    let rewrite_script = generate_compact_rewrite_script(&binary);
    write_file(&rewrite_path, &rewrite_script);
    make_executable(&rewrite_path);

    let redirect_path = hooks_dir.join("lean-ctx-redirect.sh");
    write_file(&redirect_path, REDIRECT_SCRIPT_GENERIC);
    make_executable(&redirect_path);

    let native_binary = resolve_binary_path();
    let rewrite_native = hooks_dir.join("lean-ctx-rewrite-native");
    write_file(
        &rewrite_native,
        &format!("#!/bin/sh\nexec {} hook rewrite\n", native_binary),
    );
    make_executable(&rewrite_native);

    let redirect_native = hooks_dir.join("lean-ctx-redirect-native");
    write_file(
        &redirect_native,
        &format!("#!/bin/sh\nexec {} hook redirect\n", native_binary),
    );
    make_executable(&redirect_native);
}

pub(super) fn install_cursor_hook_config(home: &std::path::Path) {
    let binary = resolve_binary_path();
    let rewrite_cmd = format!("{binary} hook rewrite");
    let redirect_cmd = format!("{binary} hook redirect");

    let hooks_json = home.join(".cursor").join("hooks.json");

    let hook_config = serde_json::json!({
        "version": 1,
        "hooks": {
            "preToolUse": [
                {
                    "matcher": "Shell",
                    "command": rewrite_cmd
                },
                {
                    "matcher": "Read|Grep",
                    "command": redirect_cmd
                }
            ]
        }
    });

    let content = if hooks_json.exists() {
        std::fs::read_to_string(&hooks_json).unwrap_or_default()
    } else {
        String::new()
    };

    let has_correct_matchers = content.contains("\"Shell\"")
        && (content.contains("\"Read|Grep\"") || content.contains("\"Read\""));
    let has_correct_format = content.contains("\"version\"") && content.contains("\"preToolUse\"");
    if has_correct_format
        && has_correct_matchers
        && content.contains("hook rewrite")
        && content.contains("hook redirect")
    {
        return;
    }

    if content.is_empty() || !content.contains("\"version\"") {
        write_file(
            &hooks_json,
            &serde_json::to_string_pretty(&hook_config).unwrap(),
        );
    } else if let Ok(mut existing) = serde_json::from_str::<serde_json::Value>(&content) {
        if let Some(obj) = existing.as_object_mut() {
            obj.insert("version".to_string(), serde_json::json!(1));
            obj.insert("hooks".to_string(), hook_config["hooks"].clone());
            write_file(
                &hooks_json,
                &serde_json::to_string_pretty(&existing).unwrap(),
            );
        }
    } else {
        write_file(
            &hooks_json,
            &serde_json::to_string_pretty(&hook_config).unwrap(),
        );
    }

    if !mcp_server_quiet_mode() {
        println!("Installed Cursor hooks at {}", hooks_json.display());
    }
}

pub(super) fn install_gemini_hook() {
    let home = match dirs::home_dir() {
        Some(h) => h,
        None => {
            eprintln!("Cannot resolve home directory");
            return;
        }
    };

    install_gemini_hook_scripts(&home);
    install_gemini_hook_config(&home);
}

pub(super) fn install_gemini_hook_scripts(home: &std::path::Path) {
    let hooks_dir = home.join(".gemini").join("hooks");
    let _ = std::fs::create_dir_all(&hooks_dir);

    let binary = resolve_binary_path_for_bash();

    let rewrite_path = hooks_dir.join("lean-ctx-rewrite-gemini.sh");
    let rewrite_script = generate_compact_rewrite_script(&binary);
    write_file(&rewrite_path, &rewrite_script);
    make_executable(&rewrite_path);

    let redirect_path = hooks_dir.join("lean-ctx-redirect-gemini.sh");
    write_file(&redirect_path, REDIRECT_SCRIPT_GENERIC);
    make_executable(&redirect_path);
}

pub(super) fn install_gemini_hook_config(home: &std::path::Path) {
    let binary = resolve_binary_path();
    let rewrite_cmd = format!("{binary} hook rewrite");
    let redirect_cmd = format!("{binary} hook redirect");

    let settings_path = home.join(".gemini").join("settings.json");
    let settings_content = if settings_path.exists() {
        std::fs::read_to_string(&settings_path).unwrap_or_default()
    } else {
        String::new()
    };

    let has_new_format = settings_content.contains("hook rewrite")
        && settings_content.contains("hook redirect")
        && settings_content.contains("\"type\"")
        && settings_content.contains("\"matcher\"");
    let has_old_hooks = settings_content.contains("lean-ctx-rewrite")
        || settings_content.contains("lean-ctx-redirect")
        || (settings_content.contains("hook rewrite") && !settings_content.contains("\"matcher\""));

    if has_new_format && !has_old_hooks {
        return;
    }

    let hook_config = serde_json::json!({
        "hooks": {
            "BeforeTool": [
                {
                    "matcher": "shell|execute_command|run_shell_command",
                    "hooks": [{
                        "type": "command",
                        "command": rewrite_cmd
                    }]
                },
                {
                    "matcher": "read_file|read_many_files|grep|search|list_dir",
                    "hooks": [{
                        "type": "command",
                        "command": redirect_cmd
                    }]
                }
            ]
        }
    });

    if settings_content.is_empty() {
        write_file(
            &settings_path,
            &serde_json::to_string_pretty(&hook_config).unwrap(),
        );
    } else if let Ok(mut existing) = serde_json::from_str::<serde_json::Value>(&settings_content) {
        if let Some(obj) = existing.as_object_mut() {
            obj.insert("hooks".to_string(), hook_config["hooks"].clone());
            write_file(
                &settings_path,
                &serde_json::to_string_pretty(&existing).unwrap(),
            );
        }
    }
    if !mcp_server_quiet_mode() {
        println!(
            "Installed Gemini CLI hooks at {}",
            settings_path.parent().unwrap_or(&settings_path).display()
        );
    }
}

pub fn install_codex_hook() {
    let home = match dirs::home_dir() {
        Some(h) => h,
        None => {
            eprintln!("Cannot resolve home directory");
            return;
        }
    };

    let codex_dir = home.join(".codex");
    let _ = std::fs::create_dir_all(&codex_dir);

    install_codex_hook_scripts(&home);
    install_codex_hook_config(&home);

    let agents_path = codex_dir.join("AGENTS.md");
    let agents_content = "# Global Agent Instructions\n\n@LEAN-CTX.md\n";

    let lean_ctx_md = codex_dir.join("LEAN-CTX.md");
    let binary = resolve_binary_path();
    let lean_ctx_content = format!(
        r#"# lean-ctx — Token Optimization

Prefix all shell commands with `{binary} -c` for compressed output:

```bash
{binary} -c git status    # instead of: git status
{binary} -c cargo test    # instead of: cargo test
{binary} -c ls src/       # instead of: ls src/
```

This saves 60-90% tokens per command. Works with: git, cargo, npm, pnpm, docker, kubectl, pip, ruff, go, curl, grep, find, ls, aws, helm, and 90+ more commands.
Use `{binary} -c --raw <cmd>` to skip compression and get full output.
"#
    );

    if agents_path.exists() {
        let content = std::fs::read_to_string(&agents_path).unwrap_or_default();
        if content.contains("lean-ctx") || content.contains("LEAN-CTX") {
            println!("Codex AGENTS.md already configured.");
            return;
        }
    }

    write_file(&agents_path, agents_content);
    write_file(&lean_ctx_md, &lean_ctx_content);
    println!("Installed Codex instructions at {}", codex_dir.display());
}

fn install_codex_hook_config(home: &std::path::Path) {
    let binary = resolve_binary_path();
    let rewrite_cmd = format!("{binary} hook rewrite");

    let codex_dir = home.join(".codex");

    let hooks_json_path = codex_dir.join("hooks.json");
    let hook_config = serde_json::json!({
        "hooks": {
            "PreToolUse": [
                {
                    "matcher": "Bash",
                    "hooks": [{
                        "type": "command",
                        "command": rewrite_cmd,
                        "timeout": 15
                    }]
                }
            ]
        }
    });

    let needs_write = if hooks_json_path.exists() {
        let content = std::fs::read_to_string(&hooks_json_path).unwrap_or_default();
        !content.contains("hook rewrite")
    } else {
        true
    };

    if needs_write {
        if hooks_json_path.exists() {
            if let Ok(mut existing) = serde_json::from_str::<serde_json::Value>(
                &std::fs::read_to_string(&hooks_json_path).unwrap_or_default(),
            ) {
                if let Some(obj) = existing.as_object_mut() {
                    obj.insert("hooks".to_string(), hook_config["hooks"].clone());
                    write_file(
                        &hooks_json_path,
                        &serde_json::to_string_pretty(&existing).unwrap(),
                    );
                    if !mcp_server_quiet_mode() {
                        println!("Updated Codex hooks.json at {}", hooks_json_path.display());
                    }
                    return;
                }
            }
        }
        write_file(
            &hooks_json_path,
            &serde_json::to_string_pretty(&hook_config).unwrap(),
        );
        if !mcp_server_quiet_mode() {
            println!(
                "Installed Codex hooks.json at {}",
                hooks_json_path.display()
            );
        }
    }

    let config_toml_path = codex_dir.join("config.toml");
    let config_content = std::fs::read_to_string(&config_toml_path).unwrap_or_default();
    if !config_content.contains("codex_hooks") {
        let mut out = config_content;
        if !out.is_empty() && !out.ends_with('\n') {
            out.push('\n');
        }
        if !out.contains("[features]") {
            out.push_str("\n[features]\ncodex_hooks = true\n");
        } else {
            out.push_str("codex_hooks = true\n");
        }
        write_file(&config_toml_path, &out);
        if !mcp_server_quiet_mode() {
            println!(
                "Enabled codex_hooks feature in {}",
                config_toml_path.display()
            );
        }
    }
}

pub(super) fn install_codex_hook_scripts(home: &std::path::Path) {
    let hooks_dir = home.join(".codex").join("hooks");
    let _ = std::fs::create_dir_all(&hooks_dir);

    let binary = resolve_binary_path_for_bash();
    let rewrite_path = hooks_dir.join("lean-ctx-rewrite-codex.sh");
    let rewrite_script = generate_compact_rewrite_script(&binary);
    write_file(&rewrite_path, &rewrite_script);
    make_executable(&rewrite_path);
    if !mcp_server_quiet_mode() {
        println!(
            "  \x1b[32m✓\x1b[0m Installed Codex hook scripts at {}",
            hooks_dir.display()
        );
    }
}

pub(super) fn install_windsurf_rules(global: bool) {
    let scope = crate::core::config::Config::load().rules_scope_effective();
    if global || scope == crate::core::config::RulesScope::Global {
        println!("Global mode: skipping project-local .windsurfrules (use without --global in a project).");
        return;
    }

    let cwd = std::env::current_dir().unwrap_or_default();
    if !is_inside_git_repo(&cwd) || cwd == dirs::home_dir().unwrap_or_default() {
        eprintln!("  Skipping .windsurfrules: not inside a git repository or in home directory.");
        return;
    }

    let rules_path = PathBuf::from(".windsurfrules");
    if rules_path.exists() {
        let content = std::fs::read_to_string(&rules_path).unwrap_or_default();
        if content.contains("lean-ctx") {
            println!(".windsurfrules already configured.");
            return;
        }
    }

    let rules = include_str!("../templates/windsurfrules.txt");
    write_file(&rules_path, rules);
    println!("Installed .windsurfrules in current project.");
}

pub(super) fn install_cline_rules(global: bool) {
    let scope = crate::core::config::Config::load().rules_scope_effective();
    if global || scope == crate::core::config::RulesScope::Global {
        println!(
            "Global mode: skipping project-local .clinerules (use without --global in a project)."
        );
        return;
    }

    let cwd = std::env::current_dir().unwrap_or_default();
    if !is_inside_git_repo(&cwd) || cwd == dirs::home_dir().unwrap_or_default() {
        eprintln!("  Skipping .clinerules: not inside a git repository or in home directory.");
        return;
    }

    let rules_path = PathBuf::from(".clinerules");
    if rules_path.exists() {
        let content = std::fs::read_to_string(&rules_path).unwrap_or_default();
        if content.contains("lean-ctx") {
            println!(".clinerules already configured.");
            return;
        }
    }

    let binary = resolve_binary_path();
    let rules = format!(
        r#"# lean-ctx Shell Optimization
# Prefix all shell commands with `{binary} -c` for 60-90% token savings.

When running shell commands, always prefix them with `{binary} -c` to compress output. For example:
- `{binary} -c git status` instead of `git status`
- `{binary} -c cargo test` instead of `cargo test`
- `{binary} -c ls src/` instead of `ls src/`

Supported commands: git, cargo, npm, pnpm, docker, kubectl, pip, ruff, go, curl, grep, find, ls, aws, helm, and 90+ more.
"#
    );

    write_file(&rules_path, &rules);
    println!("Installed .clinerules in current project.");
}

pub(super) fn install_pi_hook(global: bool) {
    let has_pi = std::process::Command::new("pi")
        .arg("--version")
        .output()
        .is_ok();

    if !has_pi {
        println!("Pi Coding Agent not found in PATH.");
        println!("Install Pi first: npm install -g @mariozechner/pi-coding-agent");
        println!();
    }

    println!("Installing pi-lean-ctx Pi Package...");
    println!();

    let install_result = std::process::Command::new("pi")
        .args(["install", "npm:pi-lean-ctx"])
        .status();

    match install_result {
        Ok(status) if status.success() => {
            println!("Installed pi-lean-ctx Pi Package.");
        }
        _ => {
            println!("Could not auto-install pi-lean-ctx. Install manually:");
            println!("  pi install npm:pi-lean-ctx");
            println!();
        }
    }

    write_pi_mcp_config();

    let scope = crate::core::config::Config::load().rules_scope_effective();
    let skip_project = global || scope == crate::core::config::RulesScope::Global;

    if !skip_project {
        let agents_md = PathBuf::from("AGENTS.md");
        if !agents_md.exists()
            || !std::fs::read_to_string(&agents_md)
                .unwrap_or_default()
                .contains("lean-ctx")
        {
            let content = include_str!("../templates/PI_AGENTS.md");
            write_file(&agents_md, content);
            println!("Created AGENTS.md in current project directory.");
        } else {
            println!("AGENTS.md already contains lean-ctx configuration.");
        }
    } else {
        println!(
            "Global mode: skipping project-local AGENTS.md (use without --global in a project)."
        );
    }

    println!();
    println!("Setup complete. All Pi tools (bash, read, grep, find, ls) route through lean-ctx.");
    println!("MCP tools (ctx_session, ctx_knowledge, ctx_semantic_search, ...) also available.");
    println!("Use /lean-ctx in Pi to verify the binary path and MCP status.");
}

fn write_pi_mcp_config() {
    let home = match dirs::home_dir() {
        Some(h) => h,
        None => return,
    };

    let mcp_config_path = home.join(".pi/agent/mcp.json");

    if !home.join(".pi/agent").exists() {
        println!("  \x1b[2m○ ~/.pi/agent/ not found — skipping MCP config\x1b[0m");
        return;
    }

    if mcp_config_path.exists() {
        let content = match std::fs::read_to_string(&mcp_config_path) {
            Ok(c) => c,
            Err(_) => return,
        };
        if content.contains("lean-ctx") {
            println!("  \x1b[32m✓\x1b[0m Pi MCP config already contains lean-ctx");
            return;
        }

        if let Ok(mut json) = serde_json::from_str::<serde_json::Value>(&content) {
            if let Some(obj) = json.as_object_mut() {
                let servers = obj
                    .entry("mcpServers")
                    .or_insert_with(|| serde_json::json!({}));
                if let Some(servers_obj) = servers.as_object_mut() {
                    servers_obj.insert("lean-ctx".to_string(), pi_mcp_server_entry());
                }
                if let Ok(formatted) = serde_json::to_string_pretty(&json) {
                    let _ = std::fs::write(&mcp_config_path, formatted);
                    println!(
                        "  \x1b[32m✓\x1b[0m Added lean-ctx to Pi MCP config (~/.pi/agent/mcp.json)"
                    );
                }
            }
        }
        return;
    }

    let content = serde_json::json!({
        "mcpServers": {
            "lean-ctx": pi_mcp_server_entry()
        }
    });
    if let Ok(formatted) = serde_json::to_string_pretty(&content) {
        let _ = std::fs::write(&mcp_config_path, formatted);
        println!("  \x1b[32m✓\x1b[0m Created Pi MCP config (~/.pi/agent/mcp.json)");
    }
}

fn pi_mcp_server_entry() -> serde_json::Value {
    let binary = resolve_binary_path();
    let mut entry = full_server_entry(&binary);
    if let Some(obj) = entry.as_object_mut() {
        obj.insert("lifecycle".to_string(), serde_json::json!("lazy"));
        obj.insert("directTools".to_string(), serde_json::json!(true));
    }
    entry
}

pub(super) fn install_copilot_hook(global: bool) {
    let binary = resolve_binary_path();

    if global {
        let mcp_path = copilot_global_mcp_path();
        if mcp_path.as_os_str() == "/nonexistent" {
            println!("  \x1b[2mVS Code not found — skipping global Copilot config\x1b[0m");
            return;
        }
        write_vscode_mcp_file(&mcp_path, &binary, "global VS Code User MCP");
        install_copilot_pretooluse_hook(true);
    } else {
        let vscode_dir = PathBuf::from(".vscode");
        let _ = std::fs::create_dir_all(&vscode_dir);
        let mcp_path = vscode_dir.join("mcp.json");
        write_vscode_mcp_file(&mcp_path, &binary, ".vscode/mcp.json");
        install_copilot_pretooluse_hook(false);
    }
}

fn install_copilot_pretooluse_hook(global: bool) {
    let binary = resolve_binary_path();
    let rewrite_cmd = format!("{binary} hook rewrite");
    let redirect_cmd = format!("{binary} hook redirect");

    let hook_config = serde_json::json!({
        "version": 1,
        "hooks": {
            "preToolUse": [
                {
                    "type": "command",
                    "bash": rewrite_cmd,
                    "timeoutSec": 15
                },
                {
                    "type": "command",
                    "bash": redirect_cmd,
                    "timeoutSec": 5
                }
            ]
        }
    });

    let hook_path = if global {
        let Some(home) = dirs::home_dir() else { return };
        let dir = home.join(".github").join("hooks");
        let _ = std::fs::create_dir_all(&dir);
        dir.join("hooks.json")
    } else {
        let dir = PathBuf::from(".github").join("hooks");
        let _ = std::fs::create_dir_all(&dir);
        dir.join("hooks.json")
    };

    let needs_write = if hook_path.exists() {
        let content = std::fs::read_to_string(&hook_path).unwrap_or_default();
        !content.contains("hook rewrite") || content.contains("\"PreToolUse\"")
    } else {
        true
    };

    if !needs_write {
        return;
    }

    if hook_path.exists() {
        if let Ok(mut existing) = serde_json::from_str::<serde_json::Value>(
            &std::fs::read_to_string(&hook_path).unwrap_or_default(),
        ) {
            if let Some(obj) = existing.as_object_mut() {
                obj.insert("version".to_string(), serde_json::json!(1));
                obj.insert("hooks".to_string(), hook_config["hooks"].clone());
                write_file(
                    &hook_path,
                    &serde_json::to_string_pretty(&existing).unwrap(),
                );
                if !mcp_server_quiet_mode() {
                    println!("Updated Copilot hooks at {}", hook_path.display());
                }
                return;
            }
        }
    }

    write_file(
        &hook_path,
        &serde_json::to_string_pretty(&hook_config).unwrap(),
    );
    if !mcp_server_quiet_mode() {
        println!("Installed Copilot hooks at {}", hook_path.display());
    }
}

fn copilot_global_mcp_path() -> PathBuf {
    if let Some(home) = dirs::home_dir() {
        #[cfg(target_os = "macos")]
        {
            return home.join("Library/Application Support/Code/User/mcp.json");
        }
        #[cfg(target_os = "linux")]
        {
            return home.join(".config/Code/User/mcp.json");
        }
        #[cfg(target_os = "windows")]
        {
            if let Ok(appdata) = std::env::var("APPDATA") {
                return PathBuf::from(appdata).join("Code/User/mcp.json");
            }
        }
        #[allow(unreachable_code)]
        home.join(".config/Code/User/mcp.json")
    } else {
        PathBuf::from("/nonexistent")
    }
}

fn write_vscode_mcp_file(mcp_path: &PathBuf, binary: &str, label: &str) {
    let data_dir = crate::core::data_dir::lean_ctx_data_dir()
        .map(|d| d.to_string_lossy().to_string())
        .unwrap_or_default();
    let desired = serde_json::json!({ "type": "stdio", "command": binary, "args": [], "env": { "LEAN_CTX_DATA_DIR": data_dir } });
    if mcp_path.exists() {
        let content = std::fs::read_to_string(mcp_path).unwrap_or_default();
        match serde_json::from_str::<serde_json::Value>(&content) {
            Ok(mut json) => {
                if let Some(obj) = json.as_object_mut() {
                    let servers = obj
                        .entry("servers")
                        .or_insert_with(|| serde_json::json!({}));
                    if let Some(servers_obj) = servers.as_object_mut() {
                        if servers_obj.get("lean-ctx") == Some(&desired) {
                            println!("  \x1b[32m✓\x1b[0m Copilot already configured in {label}");
                            return;
                        }
                        servers_obj.insert("lean-ctx".to_string(), desired);
                    }
                    write_file(
                        mcp_path,
                        &serde_json::to_string_pretty(&json).unwrap_or_default(),
                    );
                    println!("  \x1b[32m✓\x1b[0m Added lean-ctx to {label}");
                    return;
                }
            }
            Err(e) => {
                eprintln!(
                    "Could not parse VS Code MCP config at {}: {e}\nAdd to \"servers\": \"lean-ctx\": {{ \"command\": \"{}\", \"args\": [] }}",
                    mcp_path.display(),
                    binary
                );
                return;
            }
        };
    }

    if let Some(parent) = mcp_path.parent() {
        let _ = std::fs::create_dir_all(parent);
    }

    let data_dir = crate::core::data_dir::lean_ctx_data_dir()
        .map(|d| d.to_string_lossy().to_string())
        .unwrap_or_default();
    let config = serde_json::json!({
        "servers": {
            "lean-ctx": {
                "type": "stdio",
                "command": binary,
                "args": [],
                "env": { "LEAN_CTX_DATA_DIR": data_dir }
            }
        }
    });

    write_file(
        mcp_path,
        &serde_json::to_string_pretty(&config).unwrap_or_default(),
    );
    println!("  \x1b[32m✓\x1b[0m Created {label} with lean-ctx MCP server");
}

pub(super) fn install_amp_hook() {
    let binary = resolve_binary_path();
    let home = dirs::home_dir().unwrap_or_default();
    let config_path = home.join(".config/amp/settings.json");
    let display_path = "~/.config/amp/settings.json";

    if let Some(parent) = config_path.parent() {
        let _ = std::fs::create_dir_all(parent);
    }

    let data_dir = crate::core::data_dir::lean_ctx_data_dir()
        .map(|d| d.to_string_lossy().to_string())
        .unwrap_or_default();
    let entry = serde_json::json!({
        "command": binary,
        "env": { "LEAN_CTX_DATA_DIR": data_dir }
    });

    if config_path.exists() {
        let content = std::fs::read_to_string(&config_path).unwrap_or_default();
        if content.contains("lean-ctx") {
            println!("Amp MCP already configured at {display_path}");
            return;
        }

        if let Ok(mut json) = serde_json::from_str::<serde_json::Value>(&content) {
            if let Some(obj) = json.as_object_mut() {
                let servers = obj
                    .entry("amp.mcpServers")
                    .or_insert_with(|| serde_json::json!({}));
                if let Some(servers_obj) = servers.as_object_mut() {
                    servers_obj.insert("lean-ctx".to_string(), entry.clone());
                }
                if let Ok(formatted) = serde_json::to_string_pretty(&json) {
                    let _ = std::fs::write(&config_path, formatted);
                    println!("  \x1b[32m✓\x1b[0m Amp MCP configured at {display_path}");
                    return;
                }
            }
        }
    }

    let config = serde_json::json!({ "amp.mcpServers": { "lean-ctx": entry } });
    if let Ok(json_str) = serde_json::to_string_pretty(&config) {
        let _ = std::fs::write(&config_path, json_str);
        println!("  \x1b[32m✓\x1b[0m Amp MCP configured at {display_path}");
    } else {
        eprintln!("  \x1b[31m✗\x1b[0m Failed to configure Amp");
    }
}

pub(super) fn install_jetbrains_hook() {
    let binary = resolve_binary_path();
    let home = dirs::home_dir().unwrap_or_default();
    let config_path = home.join(".jb-mcp.json");
    let display_path = "~/.jb-mcp.json";

    let entry = serde_json::json!({
        "name": "lean-ctx",
        "command": binary,
        "args": [],
        "env": {
            "LEAN_CTX_DATA_DIR": crate::core::data_dir::lean_ctx_data_dir()
                .map(|d| d.to_string_lossy().to_string())
                .unwrap_or_default()
        }
    });

    if config_path.exists() {
        let content = std::fs::read_to_string(&config_path).unwrap_or_default();
        if content.contains("lean-ctx") {
            println!("JetBrains MCP already configured at {display_path}");
            return;
        }

        if let Ok(mut json) = serde_json::from_str::<serde_json::Value>(&content) {
            if let Some(obj) = json.as_object_mut() {
                let servers = obj
                    .entry("servers")
                    .or_insert_with(|| serde_json::json!([]));
                if let Some(arr) = servers.as_array_mut() {
                    arr.push(entry.clone());
                }
                if let Ok(formatted) = serde_json::to_string_pretty(&json) {
                    let _ = std::fs::write(&config_path, formatted);
                    println!("  \x1b[32m✓\x1b[0m JetBrains MCP configured at {display_path}");
                    return;
                }
            }
        }
    }

    let config = serde_json::json!({ "servers": [entry] });
    if let Ok(json_str) = serde_json::to_string_pretty(&config) {
        let _ = std::fs::write(&config_path, json_str);
        println!("  \x1b[32m✓\x1b[0m JetBrains MCP configured at {display_path}");
    } else {
        eprintln!("  \x1b[31m✗\x1b[0m Failed to configure JetBrains");
    }
}

pub(super) fn install_opencode_hook() {
    let binary = resolve_binary_path();
    let home = dirs::home_dir().unwrap_or_default();
    let config_path = home.join(".config/opencode/opencode.json");
    let display_path = "~/.config/opencode/opencode.json";

    if let Some(parent) = config_path.parent() {
        let _ = std::fs::create_dir_all(parent);
    }

    let data_dir = crate::core::data_dir::lean_ctx_data_dir()
        .map(|d| d.to_string_lossy().to_string())
        .unwrap_or_default();
    let desired = serde_json::json!({
        "type": "local",
        "command": [&binary],
        "enabled": true,
        "environment": { "LEAN_CTX_DATA_DIR": data_dir }
    });

    if config_path.exists() {
        let content = std::fs::read_to_string(&config_path).unwrap_or_default();
        if content.contains("lean-ctx") {
            println!("OpenCode MCP already configured at {display_path}");
        } else if let Ok(mut json) = serde_json::from_str::<serde_json::Value>(&content) {
            if let Some(obj) = json.as_object_mut() {
                let mcp = obj.entry("mcp").or_insert_with(|| serde_json::json!({}));
                if let Some(mcp_obj) = mcp.as_object_mut() {
                    mcp_obj.insert("lean-ctx".to_string(), desired.clone());
                }
                if let Ok(formatted) = serde_json::to_string_pretty(&json) {
                    let _ = std::fs::write(&config_path, formatted);
                    println!("  \x1b[32m✓\x1b[0m OpenCode MCP configured at {display_path}");
                }
            }
        }
    } else {
        let content = serde_json::to_string_pretty(&serde_json::json!({
            "$schema": "https://opencode.ai/config.json",
            "mcp": {
                "lean-ctx": desired
            }
        }));

        if let Ok(json_str) = content {
            let _ = std::fs::write(&config_path, json_str);
            println!("  \x1b[32m✓\x1b[0m OpenCode MCP configured at {display_path}");
        } else {
            eprintln!("  \x1b[31m✗\x1b[0m Failed to configure OpenCode");
        }
    }

    install_opencode_plugin(&home);
}

fn install_opencode_plugin(home: &std::path::Path) {
    let plugin_dir = home.join(".config/opencode/plugins");
    let _ = std::fs::create_dir_all(&plugin_dir);
    let plugin_path = plugin_dir.join("lean-ctx.ts");

    let plugin_content = include_str!("../templates/opencode-plugin.ts");
    let _ = std::fs::write(&plugin_path, plugin_content);

    if !mcp_server_quiet_mode() {
        println!(
            "  \x1b[32m✓\x1b[0m OpenCode plugin installed at {}",
            plugin_path.display()
        );
    }
}

pub(super) fn install_crush_hook() {
    let binary = resolve_binary_path();
    let home = dirs::home_dir().unwrap_or_default();
    let config_path = home.join(".config/crush/crush.json");
    let display_path = "~/.config/crush/crush.json";

    if let Some(parent) = config_path.parent() {
        let _ = std::fs::create_dir_all(parent);
    }

    if config_path.exists() {
        let content = std::fs::read_to_string(&config_path).unwrap_or_default();
        if content.contains("lean-ctx") {
            println!("Crush MCP already configured at {display_path}");
            return;
        }

        if let Ok(mut json) = serde_json::from_str::<serde_json::Value>(&content) {
            if let Some(obj) = json.as_object_mut() {
                let servers = obj.entry("mcp").or_insert_with(|| serde_json::json!({}));
                if let Some(servers_obj) = servers.as_object_mut() {
                    servers_obj.insert(
                        "lean-ctx".to_string(),
                        serde_json::json!({ "type": "stdio", "command": binary }),
                    );
                }
                if let Ok(formatted) = serde_json::to_string_pretty(&json) {
                    let _ = std::fs::write(&config_path, formatted);
                    println!("  \x1b[32m✓\x1b[0m Crush MCP configured at {display_path}");
                    return;
                }
            }
        }
    }

    let content = serde_json::to_string_pretty(&serde_json::json!({
        "mcp": {
            "lean-ctx": {
                "type": "stdio",
                "command": binary
            }
        }
    }));

    if let Ok(json_str) = content {
        let _ = std::fs::write(&config_path, json_str);
        println!("  \x1b[32m✓\x1b[0m Crush MCP configured at {display_path}");
    } else {
        eprintln!("  \x1b[31m✗\x1b[0m Failed to configure Crush");
    }
}

pub(super) fn install_kiro_hook() {
    let home = dirs::home_dir().unwrap_or_default();

    install_mcp_json_agent(
        "AWS Kiro",
        "~/.kiro/settings/mcp.json",
        &home.join(".kiro/settings/mcp.json"),
    );

    let cwd = std::env::current_dir().unwrap_or_default();
    let steering_dir = cwd.join(".kiro").join("steering");
    let steering_file = steering_dir.join("lean-ctx.md");

    if steering_file.exists()
        && std::fs::read_to_string(&steering_file)
            .unwrap_or_default()
            .contains("lean-ctx")
    {
        println!("  Kiro steering file already exists at .kiro/steering/lean-ctx.md");
    } else {
        let _ = std::fs::create_dir_all(&steering_dir);
        write_file(&steering_file, KIRO_STEERING_TEMPLATE);
        println!("  \x1b[32m✓\x1b[0m Created .kiro/steering/lean-ctx.md (Kiro will now prefer lean-ctx tools)");
    }
}

pub(super) fn install_hermes_hook(global: bool) {
    let home = match dirs::home_dir() {
        Some(h) => h,
        None => {
            eprintln!("Cannot resolve home directory");
            return;
        }
    };

    let binary = resolve_binary_path();
    let config_path = home.join(".hermes/config.yaml");
    let target = crate::core::editor_registry::EditorTarget {
        name: "Hermes Agent",
        agent_key: "hermes".to_string(),
        config_path: config_path.clone(),
        detect_path: home.join(".hermes"),
        config_type: crate::core::editor_registry::ConfigType::HermesYaml,
    };

    match crate::core::editor_registry::write_config_with_options(
        &target,
        &binary,
        crate::core::editor_registry::WriteOptions {
            overwrite_invalid: true,
        },
    ) {
        Ok(res) => match res.action {
            crate::core::editor_registry::WriteAction::Created => {
                println!("  \x1b[32m✓\x1b[0m Hermes Agent MCP configured at ~/.hermes/config.yaml");
            }
            crate::core::editor_registry::WriteAction::Updated => {
                println!("  \x1b[32m✓\x1b[0m Hermes Agent MCP updated at ~/.hermes/config.yaml");
            }
            crate::core::editor_registry::WriteAction::Already => {
                println!("  Hermes Agent MCP already configured at ~/.hermes/config.yaml");
            }
        },
        Err(e) => {
            eprintln!("  \x1b[31m✗\x1b[0m Failed to configure Hermes Agent MCP: {e}");
        }
    }

    let scope = crate::core::config::Config::load().rules_scope_effective();

    match scope {
        crate::core::config::RulesScope::Global => {
            install_hermes_rules(&home);
        }
        crate::core::config::RulesScope::Project => {
            if !global {
                install_project_hermes_rules();
                install_project_rules();
            }
        }
        crate::core::config::RulesScope::Both => {
            if global {
                install_hermes_rules(&home);
            } else {
                install_hermes_rules(&home);
                install_project_hermes_rules();
                install_project_rules();
            }
        }
    }
}

fn install_hermes_rules(home: &std::path::Path) {
    let rules_path = home.join(".hermes/HERMES.md");
    let content = HERMES_RULES_TEMPLATE;

    if rules_path.exists() {
        let existing = std::fs::read_to_string(&rules_path).unwrap_or_default();
        if existing.contains("lean-ctx") {
            println!("  Hermes rules already present in ~/.hermes/HERMES.md");
            return;
        }
        let mut updated = existing;
        if !updated.ends_with('\n') {
            updated.push('\n');
        }
        updated.push('\n');
        updated.push_str(content);
        let _ = std::fs::write(&rules_path, updated);
        println!("  \x1b[32m✓\x1b[0m Appended lean-ctx rules to ~/.hermes/HERMES.md");
    } else {
        let _ = std::fs::create_dir_all(rules_path.parent().unwrap());
        let _ = std::fs::write(&rules_path, content);
        println!("  \x1b[32m✓\x1b[0m Created ~/.hermes/HERMES.md with lean-ctx rules");
    }
}

fn install_project_hermes_rules() {
    let cwd = match std::env::current_dir() {
        Ok(d) => d,
        Err(_) => return,
    };
    let rules_path = cwd.join(".hermes.md");
    if rules_path.exists() {
        let existing = std::fs::read_to_string(&rules_path).unwrap_or_default();
        if existing.contains("lean-ctx") {
            println!("  .hermes.md already contains lean-ctx rules");
            return;
        }
        let mut updated = existing;
        if !updated.ends_with('\n') {
            updated.push('\n');
        }
        updated.push('\n');
        updated.push_str(HERMES_RULES_TEMPLATE);
        let _ = std::fs::write(&rules_path, updated);
        println!("  \x1b[32m✓\x1b[0m Appended lean-ctx rules to .hermes.md");
    } else {
        let _ = std::fs::write(&rules_path, HERMES_RULES_TEMPLATE);
        println!("  \x1b[32m✓\x1b[0m Created .hermes.md with lean-ctx rules");
    }
}

const HERMES_RULES_TEMPLATE: &str = "\
# lean-ctx — Context Engineering Layer

PREFER lean-ctx MCP tools over native equivalents for token savings:

| PREFER | OVER | Why |
|--------|------|-----|
| `ctx_read(path, mode)` | `Read` / `cat` | Cached, 10 read modes, re-reads ~13 tokens |
| `ctx_shell(command)` | `Shell` / `bash` | Pattern compression for git/npm/cargo output |
| `ctx_search(pattern, path)` | `Grep` / `rg` | Compact search results |
| `ctx_tree(path, depth)` | `ls` / `find` | Compact directory maps |

- Native Edit/StrReplace stay unchanged. If Edit requires Read and Read is unavailable, use `ctx_edit(path, old_string, new_string)`.
- Write, Delete, Glob — use normally.

ctx_read modes: full|map|signatures|diff|task|reference|aggressive|entropy|lines:N-M. Auto-selects optimal mode.
Re-reads cost ~13 tokens (cached).

Available tools: ctx_overview, ctx_preload, ctx_dedup, ctx_compress, ctx_session, ctx_knowledge, ctx_semantic_search.
Multi-agent: ctx_agent(action=handoff|sync). Diary: ctx_agent(action=diary, category=discovery|decision|blocker|progress|insight).
";
