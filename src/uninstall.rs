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
    let nebula_ctx_md = cwd.join("LEAN-CTX.md");

    const START: &str = "<!-- nebula-ctx -->";
    const END: &str = "<!-- /nebula-ctx -->";
    const OWNED: &str = "<!-- nebula-ctx-owned: PROJECT-LEAN-CTX.md v1 -->";

    let mut removed = false;

    if agents.exists() {
        if let Ok(content) = fs::read_to_string(&agents) {
            if content.contains(START) {
                let cleaned = remove_marked_block(&content, START, END);
                if cleaned != content {
                    if let Err(e) = fs::write(&agents, cleaned) {
                        eprintln!("  ✗ Failed to update project AGENTS.md: {e}");
                    } else {
                        println!("  ✓ Project: removed nebula-ctx block from AGENTS.md");
                        removed = true;
                    }
                }
            }
        }
    }

    if nebula_ctx_md.exists() {
        if let Ok(content) = fs::read_to_string(&nebula_ctx_md) {
            if content.contains(OWNED) {
                if let Err(e) = fs::remove_file(&nebula_ctx_md) {
                    eprintln!("  ✗ Failed to remove project LEAN-CTX.md: {e}");
                } else {
                    println!("  ✓ Project: removed LEAN-CTX.md");
                    removed = true;
                }
            }
        }
    }

    let project_files = [
        ".windsurfrules",
        ".clinerules",
        ".cursorrules",
        ".kiro/steering/nebula-ctx.md",
        ".cursor/rules/nebula-ctx.mdc",
    ];
    for rel in &project_files {
        let path = cwd.join(rel);
        if path.exists() {
            if let Ok(content) = fs::read_to_string(&path) {
                if content.contains("nebula-ctx") {
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
        if !content.contains("nebula-ctx") {
            continue;
        }

        let cleaned = remove_nebula_ctx_block(&content);
        if cleaned.trim() != content.trim() {
            let bak = rc.with_extension("nebula-ctx.bak");
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

    if !removed && !shell.is_empty() {
        println!("  · No shell hook found");
    }

    removed
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
        if !content.contains("nebula-ctx") {
            continue;
        }

        let ext = path.extension().and_then(|e| e.to_str()).unwrap_or("");
        let is_yaml = ext == "yaml" || ext == "yml";
        let is_toml = ext == "toml";

        let cleaned = if is_yaml {
            Some(remove_nebula_ctx_from_yaml(&content))
        } else if is_toml {
            Some(remove_nebula_ctx_from_toml(&content))
        } else {
            remove_nebula_ctx_from_json(&content)
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
            if content.contains("nebula-ctx") {
                println!(
                    "  ⚠ Zed: manually remove nebula-ctx from {}",
                    shorten(&zed_path, home)
                );
            }
        }
    }

    let vscode_path = crate::core::editor_registry::vscode_mcp_path();
    if vscode_path.exists() {
        if let Ok(content) = fs::read_to_string(&vscode_path) {
            if content.contains("nebula-ctx") {
                if let Some(cleaned) = remove_nebula_ctx_from_json(&content) {
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
    let rules_files: Vec<(&str, PathBuf)> = vec![
        (
            "Claude Code",
            crate::core::editor_registry::claude_rules_dir(home).join("nebula-ctx.md"),
        ),
        // Legacy: shared CLAUDE.md (older releases).
        (
            "Claude Code (legacy)",
            crate::core::editor_registry::claude_state_dir(home).join("CLAUDE.md"),
        ),
        // Legacy: hardcoded home path (very old releases).
        ("Claude Code (legacy home)", home.join(".claude/CLAUDE.md")),
        ("Cursor", home.join(".cursor/rules/nebula-ctx.mdc")),
        ("Gemini CLI", home.join(".gemini/GEMINI.md")),
        (
            "Gemini CLI (legacy)",
            home.join(".gemini/rules/nebula-ctx.md"),
        ),
        ("Codex CLI", home.join(".codex/LEAN-CTX.md")),
        ("Codex CLI", home.join(".codex/instructions.md")),
        ("Windsurf", home.join(".codeium/windsurf/rules/nebula-ctx.md")),
        ("Zed", home.join(".config/zed/rules/nebula-ctx.md")),
        ("Cline", home.join(".cline/rules/nebula-ctx.md")),
        ("Roo Code", home.join(".roo/rules/nebula-ctx.md")),
        ("OpenCode", home.join(".config/opencode/rules/nebula-ctx.md")),
        ("Continue", home.join(".continue/rules/nebula-ctx.md")),
        ("Aider", home.join(".aider/rules/nebula-ctx.md")),
        ("Amp", home.join(".ampcoder/rules/nebula-ctx.md")),
        ("Qwen Code", home.join(".qwen/rules/nebula-ctx.md")),
        ("Trae", home.join(".trae/rules/nebula-ctx.md")),
        (
            "Amazon Q Developer",
            home.join(".aws/amazonq/rules/nebula-ctx.md"),
        ),
        ("JetBrains IDEs", home.join(".jb-rules/nebula-ctx.md")),
        (
            "Antigravity",
            home.join(".gemini/antigravity/rules/nebula-ctx.md"),
        ),
        ("Pi Coding Agent", home.join(".pi/rules/nebula-ctx.md")),
        ("AWS Kiro", home.join(".kiro/steering/nebula-ctx.md")),
        ("Verdent", home.join(".verdent/rules/nebula-ctx.md")),
        ("Crush", home.join(".config/crush/rules/nebula-ctx.md")),
    ];

    let mut removed = false;
    for (name, path) in &rules_files {
        if !path.exists() {
            continue;
        }
        if let Ok(content) = fs::read_to_string(path) {
            if content.contains("nebula-ctx") {
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
            if content.contains("nebula-ctx") {
                let cleaned = remove_nebula_ctx_block_from_md(&content);
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
                if content.contains("nebula-ctx") {
                    let cleaned = remove_nebula_ctx_block_from_md(&content);
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

fn remove_nebula_ctx_block_from_md(content: &str) -> String {
    let mut out = String::with_capacity(content.len());
    let mut in_block = false;

    for line in content.lines() {
        if !in_block && line.contains("nebula-ctx") && line.starts_with('#') {
            in_block = true;
            continue;
        }
        if in_block {
            if line.starts_with('#') && !line.contains("nebula-ctx") {
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
        claude_hooks_dir.join("nebula-ctx-rewrite.sh"),
        claude_hooks_dir.join("nebula-ctx-redirect.sh"),
        claude_hooks_dir.join("nebula-ctx-rewrite-native"),
        claude_hooks_dir.join("nebula-ctx-redirect-native"),
        home.join(".cursor/hooks/nebula-ctx-rewrite.sh"),
        home.join(".cursor/hooks/nebula-ctx-redirect.sh"),
        home.join(".cursor/hooks/nebula-ctx-rewrite-native"),
        home.join(".cursor/hooks/nebula-ctx-redirect-native"),
        home.join(".gemini/hooks/nebula-ctx-rewrite-gemini.sh"),
        home.join(".gemini/hooks/nebula-ctx-redirect-gemini.sh"),
        home.join(".gemini/hooks/nebula-ctx-hook-gemini.sh"),
        home.join(".codex/hooks/nebula-ctx-rewrite-codex.sh"),
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
                if content.contains("nebula-ctx") {
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
    let data_dir = home.join(".nebula-ctx");
    if !data_dir.exists() {
        println!("  · No data directory found");
        return false;
    }

    match fs::remove_dir_all(&data_dir) {
        Ok(_) => {
            println!("  ✓ Data directory removed (~/.nebula-ctx/)");
            true
        }
        Err(e) => {
            eprintln!("  ✗ Failed to remove ~/.nebula-ctx/: {e}");
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

fn remove_nebula_ctx_block(content: &str) -> String {
    if content.contains("# nebula-ctx shell hook — end") {
        return remove_nebula_ctx_block_by_marker(content);
    }
    remove_nebula_ctx_block_legacy(content)
}

fn remove_nebula_ctx_block_by_marker(content: &str) -> String {
    let mut result = String::new();
    let mut in_block = false;

    for line in content.lines() {
        if !in_block && line.contains("nebula-ctx shell hook") && !line.contains("end") {
            in_block = true;
            continue;
        }
        if in_block {
            if line.trim() == "# nebula-ctx shell hook — end" {
                in_block = false;
            }
            continue;
        }
        result.push_str(line);
        result.push('\n');
    }
    result
}

fn remove_nebula_ctx_block_legacy(content: &str) -> String {
    let mut result = String::new();
    let mut in_block = false;

    for line in content.lines() {
        if line.contains("nebula-ctx shell hook") {
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

fn remove_nebula_ctx_from_json(content: &str) -> Option<String> {
    let mut parsed: serde_json::Value = serde_json::from_str(content).ok()?;
    let mut modified = false;

    if let Some(servers) = parsed.get_mut("mcpServers").and_then(|s| s.as_object_mut()) {
        modified |= servers.remove("nebula-ctx").is_some();
    }

    if let Some(servers) = parsed.get_mut("servers").and_then(|s| s.as_object_mut()) {
        modified |= servers.remove("nebula-ctx").is_some();
    }

    if let Some(servers) = parsed.get_mut("servers").and_then(|s| s.as_array_mut()) {
        let before = servers.len();
        servers.retain(|entry| entry.get("name").and_then(|n| n.as_str()) != Some("nebula-ctx"));
        modified |= servers.len() < before;
    }

    if let Some(mcp) = parsed.get_mut("mcp").and_then(|s| s.as_object_mut()) {
        modified |= mcp.remove("nebula-ctx").is_some();
    }

    if let Some(amp) = parsed
        .get_mut("amp.mcpServers")
        .and_then(|s| s.as_object_mut())
    {
        modified |= amp.remove("nebula-ctx").is_some();
    }

    if modified {
        Some(serde_json::to_string_pretty(&parsed).ok()? + "\n")
    } else {
        None
    }
}

fn remove_nebula_ctx_from_yaml(content: &str) -> String {
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
        if trimmed == "nebula-ctx:" || trimmed.starts_with("nebula-ctx:") {
            let indent = line.len() - line.trim_start().len();
            skip_depth = Some(indent);
            continue;
        }

        out.push_str(line);
        out.push('\n');
    }

    out
}

fn remove_nebula_ctx_from_toml(content: &str) -> String {
    let mut out = String::with_capacity(content.len());
    let mut skip = false;

    for line in content.lines() {
        let trimmed = line.trim();

        if trimmed.starts_with('[') && trimmed.ends_with(']') {
            let section = trimmed.trim_start_matches('[').trim_end_matches(']').trim();
            if section == "mcp_servers.nebula-ctx"
                || section == "mcp_servers.\"nebula-ctx\""
                || section.starts_with("mcp_servers.nebula-ctx.")
                || section.starts_with("mcp_servers.\"nebula-ctx\".")
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

[mcp_servers.nebula-ctx]
command = \"/usr/local/bin/nebula-ctx\"
args = []

[mcp_servers.other-tool]
command = \"/usr/bin/other\"
";
        let result = remove_nebula_ctx_from_toml(input);
        assert!(
            !result.contains("nebula-ctx"),
            "nebula-ctx section should be removed"
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
    fn remove_toml_only_nebula_ctx() {
        let input = "\
[mcp_servers.nebula-ctx]
command = \"nebula-ctx\"
";
        let result = remove_nebula_ctx_from_toml(input);
        assert!(
            result.trim().is_empty(),
            "should produce empty output: {result}"
        );
    }

    #[test]
    fn remove_toml_no_nebula_ctx() {
        let input = "\
[mcp_servers.other]
command = \"other\"
";
        let result = remove_nebula_ctx_from_toml(input);
        assert!(
            result.contains("[mcp_servers.other]"),
            "other content should be preserved"
        );
    }
}
