use std::fs;
use std::path::{Path, PathBuf};

pub fn run() {
    let home = match dirs::home_dir() {
        Some(h) => h,
        None => {
            eprintln!("  ✗ Could not determine home directory");
            return;
        }
    };

    println!("\n  nebu-ctx uninstall\n  ──────────────────────────────────\n");

    let mut removed_any = false;

    removed_any |= remove_shell_hook(&home);
    crate::proxy_setup::uninstall_proxy_env(&home, false);
    removed_any |= remove_mcp_configs(&home);
    removed_any |= remove_rules_files(&home);
    removed_any |= remove_hook_files(&home);
    removed_any |= remove_project_agent_files();
    removed_any |= remove_data_dir(&home);

    println!();

    if removed_any {
        println!("  ──────────────────────────────────");
        println!("  nebu-ctx configuration removed.\n");
    } else {
        println!("  Nothing to remove — nebu-ctx was not configured.\n");
    }

    print_binary_removal_instructions();
}

fn remove_project_agent_files() -> bool {
    let cwd = std::env::current_dir().unwrap_or_default();
    let agents = cwd.join("AGENTS.md");
    let nebu_ctx_md = cwd.join("NEBU-CTX.md");

    const START: &str = "<!-- nebu-ctx -->";
    const END: &str = "<!-- /nebu-ctx -->";
    const OWNED: &str = "<!-- nebu-ctx-owned: PROJECT-NEBU-CTX.md v1 -->";

    let mut removed = false;

    if agents.exists() {
        if let Ok(content) = fs::read_to_string(&agents) {
            if content.contains(START) {
                let cleaned = remove_marked_block(&content, START, END);
                if cleaned != content {
                    if let Err(e) = fs::write(&agents, cleaned) {
                        eprintln!("  ✗ Failed to update project AGENTS.md: {e}");
                    } else {
                        println!("  ✓ Project: removed nebu-ctx block from AGENTS.md");
                        removed = true;
                    }
                }
            }
        }
    }

    if nebu_ctx_md.exists() {
        if let Ok(content) = fs::read_to_string(&nebu_ctx_md) {
            if content.contains(OWNED) {
                if let Err(e) = fs::remove_file(&nebu_ctx_md) {
                    eprintln!("  ✗ Failed to remove project NEBU-CTX.md: {e}");
                } else {
                    println!("  ✓ Project: removed NEBU-CTX.md");
                    removed = true;
                }
            }
        }
    }

    let project_files = [
        ".windsurfrules",
        ".clinerules",
        ".cursorrules",
        ".kiro/steering/nebu-ctx.md",
        ".cursor/rules/nebu-ctx.mdc",
    ];
    for rel in &project_files {
        let path = cwd.join(rel);
        if path.exists() {
            if let Ok(content) = fs::read_to_string(&path) {
                if content.contains("nebu-ctx") {
                    let _ = fs::remove_file(&path);
                    println!("  ✓ Project: removed {rel}");
                    removed = true;
                }
            }
        }
    }

    removed
}

fn remove_marked_block(content: &str, start: &str, end: &str) -> String {
    let s = content.find(start);
    let e = content.find(end);
    match (s, e) {
        (Some(si), Some(ei)) if ei >= si => {
            let after_end = ei + end.len();
            let before = &content[..si];
            let after = &content[after_end..];
            let mut out = String::new();
            out.push_str(before.trim_end_matches('\n'));
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

fn remove_shell_hook(home: &Path) -> bool {
    let shell = std::env::var("SHELL").unwrap_or_default();
    let mut removed = false;

    crate::shell_hook::uninstall_all(false);

    let rc_files: Vec<PathBuf> = vec![
        home.join(".zshrc"),
        home.join(".bashrc"),
        home.join(".config/fish/config.fish"),
        #[cfg(windows)]
        home.join("Documents/PowerShell/Microsoft.PowerShell_profile.ps1"),
    ];

    for rc in &rc_files {
        if !rc.exists() {
            continue;
        }
        let content = match fs::read_to_string(rc) {
            Ok(c) => c,
            Err(_) => continue,
        };
        if !content.contains("nebu-ctx") {
            continue;
        }

        let mut cleaned = remove_nebu_ctx_block(&content);
        cleaned = remove_source_lines(&cleaned);
        if cleaned.trim() != content.trim() {
            let bak = rc.with_extension("nebu-ctx.bak");
            let _ = fs::copy(rc, &bak);
            if let Err(e) = fs::write(rc, &cleaned) {
                eprintln!("  ✗ Failed to update {}: {}", rc.display(), e);
            } else {
                let short = shorten(rc, home);
                println!("  ✓ Shell hook removed from {short}");
                println!("    Backup: {}", shorten(&bak, home));
                removed = true;
            }
        }
    }

    let hook_files = [
        "shell-hook.zsh",
        "shell-hook.bash",
        "shell-hook.fish",
        "shell-hook.ps1",
    ];
    let lc_dir = home.join(".nebu-ctx");
    for f in &hook_files {
        let path = lc_dir.join(f);
        if path.exists() {
            let _ = fs::remove_file(&path);
            println!("  ✓ Removed ~/.nebu-ctx/{f}");
            removed = true;
        }
    }

    if !removed && !shell.is_empty() {
        println!("  · No shell hook found");
    }

    removed
}

fn remove_source_lines(content: &str) -> String {
    let mut result = String::new();
    let mut lines = content.lines().peekable();

    while let Some(line) = lines.next() {
        if line.contains("# nebu-ctx shell hook") {
            while let Some(next) = lines.peek() {
                let trimmed = next.trim();
                let is_source_line = next.contains(".nebu-ctx/shell-hook.")
                    || trimmed == "fish_add_path \"$HOME/.cargo/bin\""
                    || trimmed.starts_with("if test -f \"$HOME/.nebu-ctx/shell-hook.fish\"")
                    || trimmed == "source \"$HOME/.nebu-ctx/shell-hook.fish\""
                    || trimmed == "end"
                    || trimmed.starts_with("$nebuCtxHook = Join-Path $HOME \".nebu-ctx\"")
                    || trimmed.starts_with("if (Test-Path $nebuCtxHook)");
                if !is_source_line {
                    break;
                }
                lines.next();
            }
            continue;
        }

        if line.contains(".nebu-ctx/shell-hook.") {
            continue;
        }

        result.push_str(line);
        result.push('\n');
    }

    result
}

fn remove_mcp_configs(home: &Path) -> bool {
    let claude_cfg_dir_json = std::env::var("CLAUDE_CONFIG_DIR")
        .ok()
        .map(|d| PathBuf::from(d).join(".claude.json"))
        .unwrap_or_else(|| PathBuf::from("/nonexistent"));
    let configs: Vec<(&str, PathBuf)> = vec![
        ("Cursor", home.join(".cursor/mcp.json")),
        ("Claude Code (config dir)", claude_cfg_dir_json),
        ("Claude Code (home)", home.join(".claude.json")),
        ("Windsurf", home.join(".codeium/windsurf/mcp_config.json")),
        ("Gemini CLI", home.join(".gemini/settings.json")),
        (
            "Gemini CLI (legacy)",
            home.join(".gemini/settings/mcp.json"),
        ),
        (
            "Antigravity",
            home.join(".gemini/antigravity/mcp_config.json"),
        ),
        ("Codex CLI", home.join(".codex/config.toml")),
        ("OpenCode", home.join(".config/opencode/opencode.json")),
        ("Qwen Code", home.join(".qwen/mcp.json")),
        ("Trae", home.join(".trae/mcp.json")),
        ("Amazon Q Developer", home.join(".aws/amazonq/mcp.json")),
        ("JetBrains IDEs", home.join(".jb-mcp.json")),
        ("AWS Kiro", home.join(".kiro/settings/mcp.json")),
        ("Verdent", home.join(".verdent/mcp.json")),
        ("Aider", home.join(".aider/mcp.json")),
        ("Amp", home.join(".config/amp/settings.json")),
        ("Crush", home.join(".config/crush/crush.json")),
        ("Pi Coding Agent", home.join(".pi/agent/mcp.json")),
        ("Cline", crate::core::editor_registry::cline_mcp_path()),
        ("Roo Code", crate::core::editor_registry::roo_mcp_path()),
        ("Hermes Agent", home.join(".hermes/config.yaml")),
    ];

    let mut removed = false;

    for (name, path) in &configs {
        if !path.exists() {
            continue;
        }
        let content = match fs::read_to_string(path) {
            Ok(c) => c,
            Err(_) => continue,
        };
        if !content.contains("nebu-ctx") {
            continue;
        }

        let ext = path.extension().and_then(|e| e.to_str()).unwrap_or("");
        let is_yaml = ext == "yaml" || ext == "yml";
        let is_toml = ext == "toml";

        let cleaned = if is_yaml {
            Some(remove_nebu_ctx_from_yaml(&content))
        } else if is_toml {
            Some(remove_nebu_ctx_from_toml(&content))
        } else {
            remove_nebu_ctx_from_json(&content)
        };

        if let Some(cleaned) = cleaned {
            if let Err(e) = fs::write(path, &cleaned) {
                eprintln!("  ✗ Failed to update {} config: {}", name, e);
            } else {
                println!("  ✓ MCP config removed from {name}");
                removed = true;
            }
        }
    }

    let zed_path = crate::core::editor_registry::zed_settings_path(home);
    if zed_path.exists() {
        if let Ok(content) = fs::read_to_string(&zed_path) {
            if content.contains("nebu-ctx") {
                println!(
                    "  ⚠ Zed: manually remove nebu-ctx from {}",
                    shorten(&zed_path, home)
                );
            }
        }
    }

    let vscode_path = crate::core::editor_registry::vscode_mcp_path();
    if vscode_path.exists() {
        if let Ok(content) = fs::read_to_string(&vscode_path) {
            if content.contains("nebu-ctx") {
                if let Some(cleaned) = remove_nebu_ctx_from_json(&content) {
                    if let Err(e) = fs::write(&vscode_path, &cleaned) {
                        eprintln!("  ✗ Failed to update VS Code config: {e}");
                    } else {
                        println!("  ✓ MCP config removed from VS Code / Copilot");
                        removed = true;
                    }
                }
            }
        }
    }

    removed
}

fn remove_rules_files(home: &Path) -> bool {
    let claude_skill_files = [
        home.join(".claude/skills/project-bootstrap/SKILL.md"),
        home.join(".claude/skills/project-bootstrap/scripts/install.sh"),
    ];
    let rules_files: Vec<(&str, PathBuf)> = vec![
        (
            "Claude Code",
            crate::core::editor_registry::claude_rules_dir(home).join("nebu-ctx.md"),
        ),
        // Legacy: shared CLAUDE.md (older releases).
        (
            "Claude Code (legacy)",
            crate::core::editor_registry::claude_state_dir(home).join("CLAUDE.md"),
        ),
        // Legacy: hardcoded home path (very old releases).
        ("Claude Code (legacy home)", home.join(".claude/CLAUDE.md")),
        ("Cursor", home.join(".cursor/rules/nebu-ctx.mdc")),
        ("Gemini CLI", home.join(".gemini/GEMINI.md")),
        (
            "Gemini CLI (legacy)",
            home.join(".gemini/rules/nebu-ctx.md"),
        ),
        ("Codex CLI", home.join(".codex/NEBU-CTX.md")),
        ("Codex CLI", home.join(".codex/instructions.md")),
        ("Windsurf", home.join(".codeium/windsurf/rules/nebu-ctx.md")),
        ("Zed", home.join(".config/zed/rules/nebu-ctx.md")),
        ("Cline", home.join(".cline/rules/nebu-ctx.md")),
        ("Roo Code", home.join(".roo/rules/nebu-ctx.md")),
        ("OpenCode", home.join(".config/opencode/rules/nebu-ctx.md")),
        (
            "OpenCode plugin",
            crate::core::editor_registry::opencode_plugin_path(home),
        ),
        (
            "OpenCode skill",
            crate::core::editor_registry::opencode_skill_path(home),
        ),
        (
            "OpenCode skill install script",
            crate::core::editor_registry::opencode_skill_dir(home)
                .join("scripts")
                .join("install.sh"),
        ),
        ("Continue", home.join(".continue/rules/nebu-ctx.md")),
        ("Aider", home.join(".aider/rules/nebu-ctx.md")),
        ("Amp", home.join(".ampcoder/rules/nebu-ctx.md")),
        ("Qwen Code", home.join(".qwen/rules/nebu-ctx.md")),
        ("Trae", home.join(".trae/rules/nebu-ctx.md")),
        (
            "Amazon Q Developer",
            home.join(".aws/amazonq/rules/nebu-ctx.md"),
        ),
        ("JetBrains IDEs", home.join(".jb-rules/nebu-ctx.md")),
        (
            "Antigravity",
            home.join(".gemini/antigravity/rules/nebu-ctx.md"),
        ),
        ("Pi Coding Agent", home.join(".pi/rules/nebu-ctx.md")),
        ("AWS Kiro", home.join(".kiro/steering/nebu-ctx.md")),
        ("Verdent", home.join(".verdent/rules/nebu-ctx.md")),
        ("Crush", home.join(".config/crush/rules/nebu-ctx.md")),
    ];

    let mut removed = false;
    for path in &claude_skill_files {
        if path.exists() {
            if let Err(e) = fs::remove_file(path) {
                eprintln!("  ✗ Failed to remove {}: {e}", path.display());
            } else {
                println!("  ✓ Removed {}", shorten(path, home));
                removed = true;
            }
        }
    }

    for (name, path) in &rules_files {
        if !path.exists() {
            continue;
        }
        if let Ok(content) = fs::read_to_string(path) {
            if content.contains("nebu-ctx") {
                if let Err(e) = fs::remove_file(path) {
                    eprintln!("  ✗ Failed to remove {name} rules: {e}");
                } else {
                    println!("  ✓ Rules removed from {name}");
                    removed = true;
                }
            }
        }
    }

    let hermes_md = home.join(".hermes/HERMES.md");
    if hermes_md.exists() {
        if let Ok(content) = fs::read_to_string(&hermes_md) {
            if content.contains("nebu-ctx") {
                let cleaned = remove_nebu_ctx_block_from_md(&content);
                if cleaned.trim().is_empty() {
                    let _ = fs::remove_file(&hermes_md);
                } else {
                    let _ = fs::write(&hermes_md, &cleaned);
                }
                println!("  ✓ Rules removed from Hermes Agent");
                removed = true;
            }
        }
    }

    if let Ok(cwd) = std::env::current_dir() {
        let project_hermes = cwd.join(".hermes.md");
        if project_hermes.exists() {
            if let Ok(content) = fs::read_to_string(&project_hermes) {
                if content.contains("nebu-ctx") {
                    let cleaned = remove_nebu_ctx_block_from_md(&content);
                    if cleaned.trim().is_empty() {
                        let _ = fs::remove_file(&project_hermes);
                    } else {
                        let _ = fs::write(&project_hermes, &cleaned);
                    }
                    println!("  ✓ Rules removed from .hermes.md");
                    removed = true;
                }
            }
        }
    }

    if !removed {
        println!("  · No rules files found");
    }
    removed
}

fn remove_nebu_ctx_block_from_md(content: &str) -> String {
    let mut out = String::with_capacity(content.len());
    let mut in_block = false;

    for line in content.lines() {
        if !in_block && line.contains("nebu-ctx") && line.starts_with('#') {
            in_block = true;
            continue;
        }
        if in_block {
            if line.starts_with('#') && !line.contains("nebu-ctx") {
                in_block = false;
                out.push_str(line);
                out.push('\n');
            }
            continue;
        }
        out.push_str(line);
        out.push('\n');
    }

    while out.starts_with('\n') {
        out.remove(0);
    }
    while out.ends_with("\n\n") {
        out.pop();
    }
    out
}

fn remove_hook_files(home: &Path) -> bool {
    let claude_hooks_dir = crate::core::editor_registry::claude_state_dir(home).join("hooks");
    let hook_files: Vec<PathBuf> = vec![
        claude_hooks_dir.join("nebu-ctx-rewrite.sh"),
        claude_hooks_dir.join("nebu-ctx-redirect.sh"),
        claude_hooks_dir.join("nebu-ctx-rewrite-native"),
        claude_hooks_dir.join("nebu-ctx-redirect-native"),
        home.join(".cursor/hooks/nebu-ctx-rewrite.sh"),
        home.join(".cursor/hooks/nebu-ctx-redirect.sh"),
        home.join(".cursor/hooks/nebu-ctx-rewrite-native"),
        home.join(".cursor/hooks/nebu-ctx-redirect-native"),
        home.join(".gemini/hooks/nebu-ctx-rewrite-gemini.sh"),
        home.join(".gemini/hooks/nebu-ctx-redirect-gemini.sh"),
        home.join(".gemini/hooks/nebu-ctx-hook-gemini.sh"),
        home.join(".codex/hooks/nebu-ctx-rewrite-codex.sh"),
    ];

    let mut removed = false;
    for path in &hook_files {
        if path.exists() {
            if let Err(e) = fs::remove_file(path) {
                eprintln!("  ✗ Failed to remove hook {}: {e}", path.display());
            } else {
                removed = true;
            }
        }
    }

    if removed {
        println!("  ✓ Hook scripts removed");
    }

    for (label, hj_path) in [
        ("Cursor", home.join(".cursor/hooks.json")),
        ("Codex", home.join(".codex/hooks.json")),
    ] {
        if hj_path.exists() {
            if let Ok(content) = fs::read_to_string(&hj_path) {
                if content.contains("nebu-ctx") {
                    if let Err(e) = fs::remove_file(&hj_path) {
                        eprintln!("  ✗ Failed to remove {label} hooks.json: {e}");
                    } else {
                        println!("  ✓ {label} hooks.json removed");
                        removed = true;
                    }
                }
            }
        }
    }

    removed
}

fn remove_data_dir(home: &Path) -> bool {
    let data_dir = home.join(".nebu-ctx");
    if !data_dir.exists() {
        println!("  · No data directory found");
        return false;
    }

    match fs::remove_dir_all(&data_dir) {
        Ok(_) => {
            println!("  ✓ Data directory removed (~/.nebu-ctx/)");
            true
        }
        Err(e) => {
            eprintln!("  ✗ Failed to remove ~/.nebu-ctx/: {e}");
            false
        }
    }
}

fn print_binary_removal_instructions() {
    let binary_path = std::env::current_exe()
        .map(|p| p.display().to_string())
        .unwrap_or_else(|_| "nebu-ctx".to_string());

    println!("  To complete uninstallation, remove the binary:\n");

    if binary_path.contains(".cargo") {
        println!("    cargo uninstall nebu-ctx\n");
    } else if binary_path.contains("homebrew") || binary_path.contains("Cellar") {
        println!("    brew uninstall nebu-ctx\n");
    } else {
        println!("    rm {binary_path}\n");
    }

    println!("  Then restart your shell.\n");
}

fn remove_nebu_ctx_block(content: &str) -> String {
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

fn remove_nebu_ctx_from_json(content: &str) -> Option<String> {
    let mut parsed: serde_json::Value = serde_json::from_str(content).ok()?;
    let mut modified = false;

    if let Some(servers) = parsed.get_mut("mcpServers").and_then(|s| s.as_object_mut()) {
        modified |= servers.remove("nebu-ctx").is_some();
        modified |= servers.remove("lean-ctx").is_some();
        modified |= servers.remove(crate::core::editor_registry::COPILOT_MCP_SERVER_KEY).is_some();
    }

    if let Some(servers) = parsed.get_mut("servers").and_then(|s| s.as_object_mut()) {
        modified |= servers.remove("nebu-ctx").is_some();
        modified |= servers.remove("lean-ctx").is_some();
        modified |= servers.remove(crate::core::editor_registry::COPILOT_MCP_SERVER_KEY).is_some();
    }

    if let Some(servers) = parsed.get_mut("servers").and_then(|s| s.as_array_mut()) {
        let before = servers.len();
        servers.retain(|entry| {
            let name = entry.get("name").and_then(|n| n.as_str());
            name != Some("nebu-ctx")
                && name != Some("lean-ctx")
                && name != Some(crate::core::editor_registry::COPILOT_MCP_SERVER_KEY)
        });
        modified |= servers.len() < before;
    }

    if let Some(mcp) = parsed.get_mut("mcp").and_then(|s| s.as_object_mut()) {
        modified |= mcp.remove("nebu-ctx").is_some();
        modified |= mcp.remove("lean-ctx").is_some();
        modified |= mcp.remove(crate::core::editor_registry::COPILOT_MCP_SERVER_KEY).is_some();
    }

    if let Some(amp) = parsed
        .get_mut("amp.mcpServers")
        .and_then(|s| s.as_object_mut())
    {
        modified |= amp.remove("nebu-ctx").is_some();
        modified |= amp.remove("lean-ctx").is_some();
        modified |= amp.remove(crate::core::editor_registry::COPILOT_MCP_SERVER_KEY).is_some();
    }

    if modified {
        Some(serde_json::to_string_pretty(&parsed).ok()? + "\n")
    } else {
        None
    }
}

fn remove_nebu_ctx_from_yaml(content: &str) -> String {
    let mut out = String::with_capacity(content.len());
    let mut skip_depth: Option<usize> = None;

    for line in content.lines() {
        if let Some(depth) = skip_depth {
            let indent = line.len() - line.trim_start().len();
            if indent > depth || line.trim().is_empty() {
                continue;
            }
            skip_depth = None;
        }

        let trimmed = line.trim();
        if trimmed == "nebu-ctx:" || trimmed.starts_with("nebu-ctx:") {
            let indent = line.len() - line.trim_start().len();
            skip_depth = Some(indent);
            continue;
        }

        out.push_str(line);
        out.push('\n');
    }

    out
}

fn remove_nebu_ctx_from_toml(content: &str) -> String {
    let mut out = String::with_capacity(content.len());
    let mut skip = false;

    for line in content.lines() {
        let trimmed = line.trim();

        if trimmed.starts_with('[') && trimmed.ends_with(']') {
            let section = trimmed.trim_start_matches('[').trim_end_matches(']').trim();
            if section == "mcp_servers.nebu-ctx"
                || section == "mcp_servers.\"nebu-ctx\""
                || section.starts_with("mcp_servers.nebu-ctx.")
                || section.starts_with("mcp_servers.\"nebu-ctx\".")
            {
                skip = true;
                continue;
            }
            skip = false;
        }

        if skip {
            continue;
        }

        if trimmed.contains("codex_hooks") && trimmed.contains("true") {
            out.push_str(&line.replace("true", "false"));
            out.push('\n');
            continue;
        }

        out.push_str(line);
        out.push('\n');
    }

    let cleaned: String = out
        .lines()
        .filter(|l| l.trim() != "[]")
        .collect::<Vec<_>>()
        .join("\n");
    if cleaned.is_empty() {
        cleaned
    } else {
        cleaned + "\n"
    }
}

fn shorten(path: &Path, home: &Path) -> String {
    match path.strip_prefix(home) {
        Ok(rel) => format!("~/{}", rel.display()),
        Err(_) => path.display().to_string(),
    }
}

// moved to core/editor_registry/paths.rs

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn remove_toml_mcp_server_section() {
        let input = "\
[features]
codex_hooks = true

[mcp_servers.nebu-ctx]
command = \"/usr/local/bin/nebu-ctx\"
args = []

[mcp_servers.other-tool]
command = \"/usr/bin/other\"
";
        let result = remove_nebu_ctx_from_toml(input);
        assert!(
            !result.contains("nebu-ctx"),
            "nebu-ctx section should be removed"
        );
        assert!(
            result.contains("[mcp_servers.other-tool]"),
            "other sections should be preserved"
        );
        assert!(
            result.contains("codex_hooks = false"),
            "codex_hooks should be set to false"
        );
    }

    #[test]
    fn remove_toml_only_nebu_ctx() {
        let input = "\
[mcp_servers.nebu-ctx]
command = \"nebu-ctx\"
";
        let result = remove_nebu_ctx_from_toml(input);
        assert!(
            result.trim().is_empty(),
            "should produce empty output: {result}"
        );
    }

    #[test]
    fn remove_toml_no_nebu_ctx() {
        let input = "\
[mcp_servers.other]
command = \"other\"
";
        let result = remove_nebu_ctx_from_toml(input);
        assert!(
            result.contains("[mcp_servers.other]"),
            "other content should be preserved"
        );
    }

    #[test]
    fn remove_source_lines_cleans_current_fish_source_block() {
        let input = "# shell\n# nebu-ctx shell hook\nfish_add_path \"$HOME/.cargo/bin\"\nif test -f \"$HOME/.nebu-ctx/shell-hook.fish\"\n    source \"$HOME/.nebu-ctx/shell-hook.fish\"\nend\nset -gx EDITOR vim\n";
        let cleaned = remove_source_lines(input);
        assert!(!cleaned.contains("shell-hook.fish"));
        assert!(cleaned.contains("set -gx EDITOR vim"));
    }

    #[test]
    fn remove_json_also_cleans_copilot_server_aliases() {
        let input = r#"{
  "servers": {
    "nebuCtx": { "command": "nebu-ctx" },
    "nebu-ctx": { "command": "nebu-ctx" },
    "other": { "command": "other" }
  }
}"#;
        let cleaned = remove_nebu_ctx_from_json(input).expect("aliases should be removed");
        assert!(cleaned.contains("other"));
        assert!(!cleaned.contains("nebuCtx"));
        assert!(!cleaned.contains("nebu-ctx"));
    }
}
