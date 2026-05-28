use serde_json::Value;

use super::types::{ConfigType, EditorTarget};

fn lookup_server<'a>(servers: &'a serde_json::Map<String, Value>, keys: &[&str]) -> Option<&'a Value> {
    keys.iter().find_map(|key| servers.get(*key))
}

fn remove_server_aliases(servers: &mut serde_json::Map<String, Value>, keys: &[&str]) -> bool {
    let mut modified = false;
    for key in keys {
        modified |= servers.remove(*key).is_some();
    }
    modified
}

fn uses_copilot_server_key(target: &EditorTarget) -> bool {
    target.config_type == ConfigType::VsCodeMcp || target.agent_key == "copilot"
}

fn preferred_server_key(target: &EditorTarget) -> &'static str {
    if uses_copilot_server_key(target) {
        super::paths::COPILOT_MCP_SERVER_KEY
    } else {
        "nebu-ctx"
    }
}

fn accepted_server_keys(target: &EditorTarget) -> Vec<&'static str> {
    let mut keys = vec![preferred_server_key(target)];
    if uses_copilot_server_key(target) {
        for key in super::paths::COPILOT_LEGACY_MCP_SERVER_KEYS {
            if !keys.contains(key) {
                keys.push(key);
            }
        }
    } else if !keys.contains(&"lean-ctx") {
        keys.push("lean-ctx");
    }
    keys
}

fn legacy_server_keys(target: &EditorTarget) -> Vec<&'static str> {
    accepted_server_keys(target)
        .into_iter()
        .filter(|key| *key != preferred_server_key(target))
        .collect()
}

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum WriteAction {
    Created,
    Updated,
    Already,
}

#[derive(Debug, Clone, Copy, Default)]
pub struct WriteOptions {
    pub overwrite_invalid: bool,
}

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct WriteResult {
    pub action: WriteAction,
    pub note: Option<String>,
}

pub fn write_config(target: &EditorTarget, binary: &str) -> Result<WriteResult, String> {
    write_config_with_options(target, binary, WriteOptions::default())
}

pub fn write_config_with_options(
    target: &EditorTarget,
    binary: &str,
    opts: WriteOptions,
) -> Result<WriteResult, String> {
    if let Some(parent) = target.config_path.parent() {
        std::fs::create_dir_all(parent).map_err(|e| e.to_string())?;
    }

    match target.config_type {
        ConfigType::McpJson => write_mcp_json(target, binary, opts),
        ConfigType::Zed => write_zed_config(target, binary, opts),
        ConfigType::Codex => write_codex_config(target, binary),
        ConfigType::VsCodeMcp => write_vscode_mcp(target, binary, opts),
        ConfigType::OpenCode => write_opencode_config(target, binary, opts),
        ConfigType::Crush => write_crush_config(target, binary, opts),
        ConfigType::JetBrains => write_jetbrains_config(target, binary, opts),
        ConfigType::Amp => write_amp_config(target, binary, opts),
        ConfigType::HermesYaml => write_hermes_yaml(target, binary, opts),
        ConfigType::GeminiSettings => write_gemini_settings(target, binary, opts),
    }
}

pub fn auto_approve_tools() -> Vec<&'static str> {
    vec!["ctx_read", "ctx_shell", "ctx_search", "ctx_tree", "ctx"]
}

fn server_entry(binary: &str, data_dir: &str, include_auto_approve: bool) -> Value {
    let mut entry = serde_json::json!({
        "command": binary,
        "args": [],
        "env": {
            "NEBU_CTX_DATA_DIR": data_dir
        }
    });
    if include_auto_approve {
        entry["autoApprove"] = serde_json::json!(auto_approve_tools());
    }
    entry
}

const NO_AUTO_APPROVE_EDITORS: &[&str] = &["Antigravity"];

fn default_data_dir() -> Result<String, String> {
    Ok(crate::core::data_dir::nebu_ctx_data_dir()?
        .to_string_lossy()
        .to_string())
}

fn write_mcp_json(
    target: &EditorTarget,
    binary: &str,
    opts: WriteOptions,
) -> Result<WriteResult, String> {
    let data_dir = default_data_dir()?;
    let include_aa = !NO_AUTO_APPROVE_EDITORS.contains(&target.name);
    let desired = server_entry(binary, &data_dir, include_aa);
    let preferred_key = preferred_server_key(target);
    let accepted_keys = accepted_server_keys(target);
    let legacy_keys = legacy_server_keys(target);

    // Claude Code manages ~/.claude.json and may overwrite it on first start.
    // Prefer the official CLI integration when available.
    if target.agent_key == "claude" || target.name == "Claude Code" {
        if let Ok(result) = try_claude_mcp_add(&desired) {
            return Ok(result);
        }
    }

    if target.config_path.exists() {
        let content = std::fs::read_to_string(&target.config_path).map_err(|e| e.to_string())?;
        let mut json = match serde_json::from_str::<Value>(&content) {
            Ok(v) => v,
            Err(e) => {
                if !opts.overwrite_invalid {
                    return Err(e.to_string());
                }
                backup_invalid_file(&target.config_path)?;
                return write_mcp_json_fresh(
                    &target.config_path,
                    preferred_key,
                    desired,
                    Some("overwrote invalid JSON".to_string()),
                );
            }
        };
        let obj = json
            .as_object_mut()
            .ok_or_else(|| "root JSON must be an object".to_string())?;

        let servers = obj
            .entry("mcpServers")
            .or_insert_with(|| serde_json::json!({}));
        let servers_obj = servers
            .as_object_mut()
            .ok_or_else(|| "\"mcpServers\" must be an object".to_string())?;

        let existing = lookup_server(servers_obj, &accepted_keys).cloned();
        let has_preferred = servers_obj.contains_key(preferred_key);
        let has_legacy = legacy_keys.iter().any(|key| servers_obj.contains_key(*key));
        if existing.as_ref() == Some(&desired) && has_preferred && !has_legacy {
            return Ok(WriteResult {
                action: WriteAction::Already,
                note: None,
            });
        }
        let removed_aliases = remove_server_aliases(servers_obj, &legacy_keys);
        let needs_write = existing.as_ref() != Some(&desired) || removed_aliases || !has_preferred;
        servers_obj.insert(preferred_key.to_string(), desired);

        let formatted = serde_json::to_string_pretty(&json).map_err(|e| e.to_string())?;
        crate::config_io::write_atomic_with_backup(&target.config_path, &formatted)?;
        return Ok(WriteResult {
            action: if needs_write {
                WriteAction::Updated
            } else {
                WriteAction::Already
            },
            note: None,
        });
    }

    write_mcp_json_fresh(&target.config_path, preferred_key, desired, None)
}

fn try_claude_mcp_add(desired: &Value) -> Result<WriteResult, String> {
    use std::io::Write;
    use std::path::PathBuf;
    use std::process::{Command, Stdio};
    use std::time::{Duration, Instant};

    fn split_search_paths(raw: &str) -> Vec<PathBuf> {
        if !cfg!(windows) {
            return raw
                .split(':')
                .filter(|segment| !segment.trim().is_empty())
                .map(PathBuf::from)
                .collect();
        }

        let mut paths = Vec::new();
        let mut current = String::new();
        let chars: Vec<char> = raw.chars().collect();

        for (index, ch) in chars.iter().enumerate() {
            let drive_colon = *ch == ':'
                && current.len() == 1
                && current
                    .chars()
                    .next()
                    .is_some_and(|value| value.is_ascii_alphabetic())
                && chars
                    .get(index + 1)
                    .is_some_and(|next| *next == '\\' || *next == '/');
            let is_separator = *ch == ';' || (*ch == ':' && !drive_colon);

            if is_separator {
                if !current.trim().is_empty() {
                    paths.push(PathBuf::from(current.trim()));
                }
                current.clear();
            } else {
                current.push(*ch);
            }
        }

        if !current.trim().is_empty() {
            paths.push(PathBuf::from(current.trim()));
        }

        paths
    }

    fn resolve_claude_command() -> PathBuf {
        let Some(raw_path) = std::env::var_os("PATH") else {
            return PathBuf::from("claude");
        };
        let raw_path = raw_path.to_string_lossy();

        for dir in split_search_paths(&raw_path) {
            let base = dir.join("claude");
            if base.exists() {
                return base;
            }
            if cfg!(windows) {
                for extension in ["cmd", "bat", "exe"] {
                    let candidate = dir.join(format!("claude.{extension}"));
                    if candidate.exists() {
                        return candidate;
                    }
                }
            }
        }

        PathBuf::from("claude")
    }

    let server_json = serde_json::to_string(desired).map_err(|e| e.to_string())?;

    let mut child = Command::new(resolve_claude_command())
        .args(["mcp", "add-json", "--scope", "user", "nebu-ctx"])
        .stdin(Stdio::piped())
        .stdout(Stdio::null())
        .stderr(Stdio::null())
        .spawn()
        .map_err(|e| e.to_string())?;

    if let Some(mut stdin) = child.stdin.take() {
        let _ = stdin.write_all(server_json.as_bytes());
    }

    let deadline = Duration::from_secs(3);
    let start = Instant::now();
    loop {
        match child.try_wait() {
            Ok(Some(status)) => {
                return if status.success() {
                    Ok(WriteResult {
                        action: WriteAction::Updated,
                        note: Some("via claude mcp add-json".to_string()),
                    })
                } else {
                    Err("claude mcp add-json failed".to_string())
                };
            }
            Ok(None) => {
                if start.elapsed() > deadline {
                    let _ = child.kill();
                    let _ = child.wait();
                    return Err("claude mcp add-json timed out".to_string());
                }
                std::thread::sleep(Duration::from_millis(20));
            }
            Err(e) => return Err(e.to_string()),
        }
    }
}

fn write_mcp_json_fresh(
    path: &std::path::Path,
    server_key: &str,
    desired: Value,
    note: Option<String>,
) -> Result<WriteResult, String> {
    let content = serde_json::to_string_pretty(&serde_json::json!({
        "mcpServers": { server_key: desired }
    }))
    .map_err(|e| e.to_string())?;
    crate::config_io::write_atomic_with_backup(path, &content)?;
    Ok(WriteResult {
        action: if note.is_some() {
            WriteAction::Updated
        } else {
            WriteAction::Created
        },
        note,
    })
}

fn write_zed_config(
    target: &EditorTarget,
    binary: &str,
    opts: WriteOptions,
) -> Result<WriteResult, String> {
    let desired = serde_json::json!({
        "source": "custom",
        "command": binary,
        "args": [],
        "env": {}
    });

    if target.config_path.exists() {
        let content = std::fs::read_to_string(&target.config_path).map_err(|e| e.to_string())?;
        let mut json = match serde_json::from_str::<Value>(&content) {
            Ok(v) => v,
            Err(e) => {
                if !opts.overwrite_invalid {
                    return Err(e.to_string());
                }
                backup_invalid_file(&target.config_path)?;
                return write_zed_config_fresh(
                    &target.config_path,
                    desired,
                    Some("overwrote invalid JSON".to_string()),
                );
            }
        };
        let obj = json
            .as_object_mut()
            .ok_or_else(|| "root JSON must be an object".to_string())?;

        let servers = obj
            .entry("context_servers")
            .or_insert_with(|| serde_json::json!({}));
        let servers_obj = servers
            .as_object_mut()
            .ok_or_else(|| "\"context_servers\" must be an object".to_string())?;

        let existing = servers_obj
            .get("nebu-ctx")
            .cloned()
            .or_else(|| servers_obj.get("lean-ctx").cloned());
        if existing.as_ref() == Some(&desired) {
            return Ok(WriteResult {
                action: WriteAction::Already,
                note: None,
            });
        }
        servers_obj.insert("nebu-ctx".to_string(), desired);

        let formatted = serde_json::to_string_pretty(&json).map_err(|e| e.to_string())?;
        crate::config_io::write_atomic_with_backup(&target.config_path, &formatted)?;
        return Ok(WriteResult {
            action: WriteAction::Updated,
            note: None,
        });
    }

    write_zed_config_fresh(&target.config_path, desired, None)
}

fn write_codex_config(target: &EditorTarget, binary: &str) -> Result<WriteResult, String> {
    if target.config_path.exists() {
        let content = std::fs::read_to_string(&target.config_path).map_err(|e| e.to_string())?;
        let updated = upsert_codex_toml(&content, binary);
        if updated == content {
            return Ok(WriteResult {
                action: WriteAction::Already,
                note: None,
            });
        }
        crate::config_io::write_atomic_with_backup(&target.config_path, &updated)?;
        return Ok(WriteResult {
            action: WriteAction::Updated,
            note: None,
        });
    }

    let content = format!(
        "[mcp_servers.nebu-ctx]\ncommand = \"{}\"\nargs = []\n",
        binary
    );
    crate::config_io::write_atomic_with_backup(&target.config_path, &content)?;
    Ok(WriteResult {
        action: WriteAction::Created,
        note: None,
    })
}

fn write_zed_config_fresh(
    path: &std::path::Path,
    desired: Value,
    note: Option<String>,
) -> Result<WriteResult, String> {
    let content = serde_json::to_string_pretty(&serde_json::json!({
        "context_servers": { "nebu-ctx": desired }
    }))
    .map_err(|e| e.to_string())?;
    crate::config_io::write_atomic_with_backup(path, &content)?;
    Ok(WriteResult {
        action: if note.is_some() {
            WriteAction::Updated
        } else {
            WriteAction::Created
        },
        note,
    })
}

fn write_vscode_mcp(
    target: &EditorTarget,
    binary: &str,
    opts: WriteOptions,
) -> Result<WriteResult, String> {
    let data_dir = crate::core::data_dir::nebu_ctx_data_dir()
        .map(|d| d.to_string_lossy().to_string())
        .unwrap_or_default();
    let desired = serde_json::json!({ "type": "stdio", "command": binary, "args": [], "env": { "NEBU_CTX_DATA_DIR": data_dir } });
    let preferred_key = preferred_server_key(target);
    let accepted_keys = accepted_server_keys(target);
    let legacy_keys = legacy_server_keys(target);

    if target.config_path.exists() {
        let content = std::fs::read_to_string(&target.config_path).map_err(|e| e.to_string())?;
        let mut json = match serde_json::from_str::<Value>(&content) {
            Ok(v) => v,
            Err(e) => {
                if !opts.overwrite_invalid {
                    return Err(e.to_string());
                }
                backup_invalid_file(&target.config_path)?;
                return write_vscode_mcp_fresh(
                    &target.config_path,
                    binary,
                    preferred_key,
                    Some("overwrote invalid JSON".to_string()),
                );
            }
        };
        let obj = json
            .as_object_mut()
            .ok_or_else(|| "root JSON must be an object".to_string())?;

        let servers = obj
            .entry("servers")
            .or_insert_with(|| serde_json::json!({}));
        let servers_obj = servers
            .as_object_mut()
            .ok_or_else(|| "\"servers\" must be an object".to_string())?;

        let existing = lookup_server(servers_obj, &accepted_keys).cloned();
        let has_preferred = servers_obj.contains_key(preferred_key);
        let has_legacy = legacy_keys.iter().any(|key| servers_obj.contains_key(*key));
        if existing.as_ref() == Some(&desired) && has_preferred && !has_legacy {
            return Ok(WriteResult {
                action: WriteAction::Already,
                note: None,
            });
        }
        let removed_aliases = remove_server_aliases(servers_obj, &legacy_keys);
        let needs_write = existing.as_ref() != Some(&desired) || removed_aliases || !has_preferred;
        servers_obj.insert(preferred_key.to_string(), desired);

        let formatted = serde_json::to_string_pretty(&json).map_err(|e| e.to_string())?;
        crate::config_io::write_atomic_with_backup(&target.config_path, &formatted)?;
        return Ok(WriteResult {
            action: if needs_write {
                WriteAction::Updated
            } else {
                WriteAction::Already
            },
            note: None,
        });
    }

    write_vscode_mcp_fresh(&target.config_path, binary, preferred_key, None)
}

fn write_vscode_mcp_fresh(
    path: &std::path::Path,
    binary: &str,
    server_key: &str,
    note: Option<String>,
) -> Result<WriteResult, String> {
    let data_dir = crate::core::data_dir::nebu_ctx_data_dir()
        .map(|d| d.to_string_lossy().to_string())
        .unwrap_or_default();
    let content = serde_json::to_string_pretty(&serde_json::json!({
        "servers": { server_key: { "type": "stdio", "command": binary, "args": [], "env": { "NEBU_CTX_DATA_DIR": data_dir } } }
    }))
    .map_err(|e| e.to_string())?;
    crate::config_io::write_atomic_with_backup(path, &content)?;
    Ok(WriteResult {
        action: if note.is_some() {
            WriteAction::Updated
        } else {
            WriteAction::Created
        },
        note,
    })
}

fn write_opencode_config(
    target: &EditorTarget,
    binary: &str,
    opts: WriteOptions,
) -> Result<WriteResult, String> {
    let data_dir = crate::core::data_dir::nebu_ctx_data_dir()
        .map(|d| d.to_string_lossy().to_string())
        .unwrap_or_default();
    let desired = serde_json::json!({
        "type": "local",
        "command": [binary],
        "enabled": true,
        "environment": { "NEBU_CTX_DATA_DIR": data_dir }
    });
    let desired_instruction = serde_json::json!("./rules/nebu-ctx.md");
    let desired_plugin = serde_json::json!("./plugins/nebu-ctx.ts");
    install_opencode_support_files(target)?;

    if target.config_path.exists() {
        let content = std::fs::read_to_string(&target.config_path).map_err(|e| e.to_string())?;
        let mut json = match serde_json::from_str::<Value>(&content) {
            Ok(v) => v,
            Err(e) => {
                if !opts.overwrite_invalid {
                    return Err(e.to_string());
                }
                backup_invalid_file(&target.config_path)?;
                return write_opencode_fresh(
                    &target.config_path,
                    binary,
                    Some("overwrote invalid JSON".to_string()),
                );
            }
        };
        let obj = json
            .as_object_mut()
            .ok_or_else(|| "root JSON must be an object".to_string())?;
        obj.entry("$schema")
            .or_insert_with(|| serde_json::json!("https://opencode.ai/config.json"));

        let instructions = obj
            .entry("instructions")
            .or_insert_with(|| serde_json::json!([]));
        let instructions_arr = instructions
            .as_array_mut()
            .ok_or_else(|| "\"instructions\" must be an array".to_string())?;
        let had_instruction = instructions_arr
            .iter()
            .any(|item| item == &desired_instruction);
        if !had_instruction {
            instructions_arr.push(desired_instruction.clone());
        }

        let plugins = obj.entry("plugin").or_insert_with(|| serde_json::json!([]));
        let plugins_arr = plugins
            .as_array_mut()
            .ok_or_else(|| "\"plugin\" must be an array".to_string())?;
        let had_plugin = plugins_arr.iter().any(|item| item == &desired_plugin);
        if !had_plugin {
            plugins_arr.push(desired_plugin.clone());
        }

        let mcp = obj.entry("mcp").or_insert_with(|| serde_json::json!({}));
        let mcp_obj = mcp
            .as_object_mut()
            .ok_or_else(|| "\"mcp\" must be an object".to_string())?;

        let existing = mcp_obj.get("nebu-ctx").cloned();
        if existing.as_ref() == Some(&desired) && had_instruction && had_plugin {
            return Ok(WriteResult {
                action: WriteAction::Already,
                note: None,
            });
        }
        let _ = mcp_obj.remove("lean-ctx");
        mcp_obj.insert("nebu-ctx".to_string(), desired);

        let formatted = serde_json::to_string_pretty(&json).map_err(|e| e.to_string())?;
        crate::config_io::write_atomic_with_backup(&target.config_path, &formatted)?;
        return Ok(WriteResult {
            action: WriteAction::Updated,
            note: None,
        });
    }

    write_opencode_fresh(&target.config_path, binary, None)
}

fn write_opencode_fresh(
    path: &std::path::Path,
    binary: &str,
    note: Option<String>,
) -> Result<WriteResult, String> {
    install_opencode_support_files_for_config(path)?;
    let data_dir = crate::core::data_dir::nebu_ctx_data_dir()
        .map(|d| d.to_string_lossy().to_string())
        .unwrap_or_default();
    let content = serde_json::to_string_pretty(&serde_json::json!({
        "$schema": "https://opencode.ai/config.json",
        "instructions": ["./rules/nebu-ctx.md"],
        "plugin": ["./plugins/nebu-ctx.ts"],
        "mcp": { "nebu-ctx": { "type": "local", "command": [binary], "enabled": true, "environment": { "NEBU_CTX_DATA_DIR": data_dir } } }
    }))
    .map_err(|e| e.to_string())?;
    crate::config_io::write_atomic_with_backup(path, &content)?;
    Ok(WriteResult {
        action: if note.is_some() {
            WriteAction::Updated
        } else {
            WriteAction::Created
        },
        note,
    })
}

fn install_opencode_support_files(target: &EditorTarget) -> Result<(), String> {
    install_opencode_support_files_for_config(&target.config_path)
}

fn install_opencode_support_files_for_config(config_path: &std::path::Path) -> Result<(), String> {
    let Some(config_dir) = config_path.parent() else {
        return Err("OpenCode config path has no parent directory".to_string());
    };

    let rules_dir = config_dir.join("rules");
    std::fs::create_dir_all(&rules_dir).map_err(|e| e.to_string())?;
    let rules_path = rules_dir.join("nebu-ctx.md");
    let desired_rules = crate::rules_inject::rules_dedicated_markdown();
    let existing_rules = std::fs::read_to_string(&rules_path).unwrap_or_default();
    if existing_rules.is_empty() || !existing_rules.contains(crate::rules_inject::RULES_VERSION_STR)
    {
        crate::config_io::write_atomic_with_backup(&rules_path, desired_rules)?;
    }

    let plugin_dir = config_dir.join("plugins");
    std::fs::create_dir_all(&plugin_dir).map_err(|e| e.to_string())?;
    let plugin_path = plugin_dir.join("nebu-ctx.ts");
    let plugin_content = include_str!("../../templates/opencode-plugin.ts");
    crate::config_io::write_atomic_with_backup(&plugin_path, plugin_content)?;

    let skill_dir = config_dir
        .join("skills")
        .join(crate::core::editor_registry::PROJECT_BOOTSTRAP_SKILL_NAME);
    install_embedded_skill(&skill_dir)?;

    Ok(())
}

fn install_embedded_skill(skill_dir: &std::path::Path) -> Result<(), String> {
    std::fs::create_dir_all(skill_dir.join("scripts")).map_err(|e| e.to_string())?;

    let skill_path = skill_dir.join("SKILL.md");
    let skill_md = include_str!("../../../assets/skills/project-bootstrap/SKILL.md");
    crate::config_io::write_atomic_with_backup(&skill_path, skill_md)?;

    let script_path = skill_dir.join("scripts/install.sh");
    let install_sh = include_str!("../../../assets/skills/project-bootstrap/scripts/install.sh");
    crate::config_io::write_atomic_with_backup(&script_path, install_sh)?;

    #[cfg(unix)]
    {
        use std::os::unix::fs::PermissionsExt;
        if let Ok(mut perms) = std::fs::metadata(&script_path).map(|meta| meta.permissions()) {
            perms.set_mode(0o755);
            let _ = std::fs::set_permissions(&script_path, perms);
        }
    }

    Ok(())
}

fn write_jetbrains_config(
    target: &EditorTarget,
    binary: &str,
    opts: WriteOptions,
) -> Result<WriteResult, String> {
    let data_dir = crate::core::data_dir::nebu_ctx_data_dir()
        .map(|d| d.to_string_lossy().to_string())
        .unwrap_or_default();
    let entry = serde_json::json!({
        "name": "nebu-ctx",
        "command": binary,
        "args": [],
        "env": { "NEBU_CTX_DATA_DIR": data_dir }
    });

    if target.config_path.exists() {
        let content = std::fs::read_to_string(&target.config_path).map_err(|e| e.to_string())?;
        let mut json = match serde_json::from_str::<Value>(&content) {
            Ok(v) => v,
            Err(e) => {
                if !opts.overwrite_invalid {
                    return Err(e.to_string());
                }
                backup_invalid_file(&target.config_path)?;
                let fresh = serde_json::json!({ "servers": [entry] });
                let formatted = serde_json::to_string_pretty(&fresh).map_err(|e| e.to_string())?;
                crate::config_io::write_atomic_with_backup(&target.config_path, &formatted)?;
                return Ok(WriteResult {
                    action: WriteAction::Updated,
                    note: Some("overwrote invalid JSON".to_string()),
                });
            }
        };
        let obj = json
            .as_object_mut()
            .ok_or_else(|| "root JSON must be an object".to_string())?;
        let servers = obj
            .entry("servers")
            .or_insert_with(|| serde_json::json!([]));
        if let Some(arr) = servers.as_array_mut() {
            let already = arr
                .iter()
                .any(|s| s.get("name").and_then(|n| n.as_str()) == Some("nebu-ctx"));
            if already {
                return Ok(WriteResult {
                    action: WriteAction::Already,
                    note: None,
                });
            }
            arr.push(entry);
        }
        let formatted = serde_json::to_string_pretty(&json).map_err(|e| e.to_string())?;
        crate::config_io::write_atomic_with_backup(&target.config_path, &formatted)?;
        return Ok(WriteResult {
            action: WriteAction::Updated,
            note: None,
        });
    }

    let config = serde_json::json!({ "servers": [entry] });
    let formatted = serde_json::to_string_pretty(&config).map_err(|e| e.to_string())?;
    crate::config_io::write_atomic_with_backup(&target.config_path, &formatted)?;
    Ok(WriteResult {
        action: WriteAction::Created,
        note: None,
    })
}

fn write_amp_config(
    target: &EditorTarget,
    binary: &str,
    opts: WriteOptions,
) -> Result<WriteResult, String> {
    let data_dir = crate::core::data_dir::nebu_ctx_data_dir()
        .map(|d| d.to_string_lossy().to_string())
        .unwrap_or_default();
    let entry = serde_json::json!({
        "command": binary,
        "args": [],
        "env": { "NEBU_CTX_DATA_DIR": data_dir }
    });

    if target.config_path.exists() {
        let content = std::fs::read_to_string(&target.config_path).map_err(|e| e.to_string())?;
        let mut json = match serde_json::from_str::<Value>(&content) {
            Ok(v) => v,
            Err(e) => {
                if !opts.overwrite_invalid {
                    return Err(e.to_string());
                }
                backup_invalid_file(&target.config_path)?;
                let fresh = serde_json::json!({ "amp.mcpServers": { "nebu-ctx": entry } });
                let formatted = serde_json::to_string_pretty(&fresh).map_err(|e| e.to_string())?;
                crate::config_io::write_atomic_with_backup(&target.config_path, &formatted)?;
                return Ok(WriteResult {
                    action: WriteAction::Updated,
                    note: Some("overwrote invalid JSON".to_string()),
                });
            }
        };
        let obj = json
            .as_object_mut()
            .ok_or_else(|| "root JSON must be an object".to_string())?;
        let servers = obj
            .entry("amp.mcpServers")
            .or_insert_with(|| serde_json::json!({}));
        let servers_obj = servers
            .as_object_mut()
            .ok_or_else(|| "\"amp.mcpServers\" must be an object".to_string())?;

        let existing = servers_obj
            .get("nebu-ctx")
            .cloned()
            .or_else(|| servers_obj.get("lean-ctx").cloned());
        if existing.as_ref() == Some(&entry) {
            return Ok(WriteResult {
                action: WriteAction::Already,
                note: None,
            });
        }
        servers_obj.insert("nebu-ctx".to_string(), entry);

        let formatted = serde_json::to_string_pretty(&json).map_err(|e| e.to_string())?;
        crate::config_io::write_atomic_with_backup(&target.config_path, &formatted)?;
        return Ok(WriteResult {
            action: WriteAction::Updated,
            note: None,
        });
    }

    let config = serde_json::json!({ "amp.mcpServers": { "nebu-ctx": entry } });
    let formatted = serde_json::to_string_pretty(&config).map_err(|e| e.to_string())?;
    crate::config_io::write_atomic_with_backup(&target.config_path, &formatted)?;
    Ok(WriteResult {
        action: WriteAction::Created,
        note: None,
    })
}

fn write_crush_config(
    target: &EditorTarget,
    binary: &str,
    opts: WriteOptions,
) -> Result<WriteResult, String> {
    let desired = serde_json::json!({ "type": "stdio", "command": binary, "args": [], "env": {} });

    if target.config_path.exists() {
        let content = std::fs::read_to_string(&target.config_path).map_err(|e| e.to_string())?;
        let mut json = match serde_json::from_str::<Value>(&content) {
            Ok(v) => v,
            Err(e) => {
                if !opts.overwrite_invalid {
                    return Err(e.to_string());
                }
                backup_invalid_file(&target.config_path)?;
                return write_crush_fresh(
                    &target.config_path,
                    desired,
                    Some("overwrote invalid JSON".to_string()),
                );
            }
        };
        let obj = json
            .as_object_mut()
            .ok_or_else(|| "root JSON must be an object".to_string())?;
        let mcp = obj.entry("mcp").or_insert_with(|| serde_json::json!({}));
        let mcp_obj = mcp
            .as_object_mut()
            .ok_or_else(|| "\"mcp\" must be an object".to_string())?;

        let existing = mcp_obj
            .get("nebu-ctx")
            .cloned()
            .or_else(|| mcp_obj.get("lean-ctx").cloned());
        if existing.as_ref() == Some(&desired) {
            return Ok(WriteResult {
                action: WriteAction::Already,
                note: None,
            });
        }
        mcp_obj.insert("nebu-ctx".to_string(), desired);

        let formatted = serde_json::to_string_pretty(&json).map_err(|e| e.to_string())?;
        crate::config_io::write_atomic_with_backup(&target.config_path, &formatted)?;
        return Ok(WriteResult {
            action: WriteAction::Updated,
            note: None,
        });
    }

    write_crush_fresh(&target.config_path, desired, None)
}

fn write_crush_fresh(
    path: &std::path::Path,
    desired: Value,
    note: Option<String>,
) -> Result<WriteResult, String> {
    let content = serde_json::to_string_pretty(&serde_json::json!({
        "mcp": { "nebu-ctx": desired }
    }))
    .map_err(|e| e.to_string())?;
    crate::config_io::write_atomic_with_backup(path, &content)?;
    Ok(WriteResult {
        action: if note.is_some() {
            WriteAction::Updated
        } else {
            WriteAction::Created
        },
        note,
    })
}

fn upsert_codex_toml(existing: &str, binary: &str) -> String {
    let mut out = String::with_capacity(existing.len() + 128);
    let mut in_section = false;
    let mut saw_section = false;
    let mut wrote_command = false;
    let mut wrote_args = false;

    for line in existing.lines() {
        let trimmed = line.trim();
        if trimmed == "[]" {
            continue;
        }
        if trimmed.starts_with('[') && trimmed.ends_with(']') {
            if in_section && !wrote_command {
                out.push_str(&format!("command = \"{}\"\n", binary));
                wrote_command = true;
            }
            if in_section && !wrote_args {
                out.push_str("args = []\n");
                wrote_args = true;
            }
            in_section = trimmed == "[mcp_servers.nebu-ctx]";
            if in_section {
                saw_section = true;
            }
            out.push_str(line);
            out.push('\n');
            continue;
        }

        if in_section {
            if trimmed.starts_with("command") && trimmed.contains('=') {
                out.push_str(&format!("command = \"{}\"\n", binary));
                wrote_command = true;
                continue;
            }
            if trimmed.starts_with("args") && trimmed.contains('=') {
                out.push_str("args = []\n");
                wrote_args = true;
                continue;
            }
        }

        out.push_str(line);
        out.push('\n');
    }

    if saw_section {
        if in_section && !wrote_command {
            out.push_str(&format!("command = \"{}\"\n", binary));
        }
        if in_section && !wrote_args {
            out.push_str("args = []\n");
        }
        return out;
    }

    if !out.ends_with('\n') {
        out.push('\n');
    }
    out.push_str("\n[mcp_servers.nebu-ctx]\n");
    out.push_str(&format!("command = \"{}\"\n", binary));
    out.push_str("args = []\n");
    out
}

fn write_gemini_settings(
    target: &EditorTarget,
    binary: &str,
    opts: WriteOptions,
) -> Result<WriteResult, String> {
    let data_dir = crate::core::data_dir::nebu_ctx_data_dir()
        .map(|d| d.to_string_lossy().to_string())
        .unwrap_or_default();
    let entry = serde_json::json!({
        "command": binary,
        "args": [],
        "env": { "NEBU_CTX_DATA_DIR": data_dir },
        "trust": true,
    });

    if target.config_path.exists() {
        let content = std::fs::read_to_string(&target.config_path).map_err(|e| e.to_string())?;
        let mut json = match serde_json::from_str::<Value>(&content) {
            Ok(v) => v,
            Err(e) => {
                if !opts.overwrite_invalid {
                    return Err(e.to_string());
                }
                backup_invalid_file(&target.config_path)?;
                let fresh = serde_json::json!({ "mcpServers": { "nebu-ctx": entry } });
                let formatted = serde_json::to_string_pretty(&fresh).map_err(|e| e.to_string())?;
                crate::config_io::write_atomic_with_backup(&target.config_path, &formatted)?;
                return Ok(WriteResult {
                    action: WriteAction::Updated,
                    note: Some("overwrote invalid JSON".to_string()),
                });
            }
        };
        let obj = json
            .as_object_mut()
            .ok_or_else(|| "root JSON must be an object".to_string())?;
        let servers = obj
            .entry("mcpServers")
            .or_insert_with(|| serde_json::json!({}));
        let servers_obj = servers
            .as_object_mut()
            .ok_or_else(|| "\"mcpServers\" must be an object".to_string())?;

        let existing = servers_obj
            .get("nebu-ctx")
            .cloned()
            .or_else(|| servers_obj.get("lean-ctx").cloned());
        if existing.as_ref() == Some(&entry) {
            return Ok(WriteResult {
                action: WriteAction::Already,
                note: None,
            });
        }
        servers_obj.insert("nebu-ctx".to_string(), entry);

        let formatted = serde_json::to_string_pretty(&json).map_err(|e| e.to_string())?;
        crate::config_io::write_atomic_with_backup(&target.config_path, &formatted)?;
        return Ok(WriteResult {
            action: WriteAction::Updated,
            note: None,
        });
    }

    let config = serde_json::json!({ "mcpServers": { "nebu-ctx": entry } });
    let formatted = serde_json::to_string_pretty(&config).map_err(|e| e.to_string())?;
    crate::config_io::write_atomic_with_backup(&target.config_path, &formatted)?;
    Ok(WriteResult {
        action: WriteAction::Created,
        note: None,
    })
}

fn write_hermes_yaml(
    target: &EditorTarget,
    binary: &str,
    _opts: WriteOptions,
) -> Result<WriteResult, String> {
    let data_dir = default_data_dir()?;

    let mcp_block = format!(
        "  nebu-ctx:\n    command: \"{binary}\"\n    args: []\n    env:\n      NEBU_CTX_DATA_DIR: \"{data_dir}\""
    );

    if target.config_path.exists() {
        let content = std::fs::read_to_string(&target.config_path).map_err(|e| e.to_string())?;

        if content.contains("nebu-ctx") || content.contains("lean-ctx") {
            return Ok(WriteResult {
                action: WriteAction::Already,
                note: None,
            });
        }

        let updated = upsert_hermes_yaml_mcp(&content, &mcp_block);
        crate::config_io::write_atomic_with_backup(&target.config_path, &updated)?;
        return Ok(WriteResult {
            action: WriteAction::Updated,
            note: None,
        });
    }

    let content = format!("mcp_servers:\n{mcp_block}\n");
    crate::config_io::write_atomic_with_backup(&target.config_path, &content)?;
    Ok(WriteResult {
        action: WriteAction::Created,
        note: None,
    })
}

fn upsert_hermes_yaml_mcp(existing: &str, mcp_block: &str) -> String {
    let mut out = String::with_capacity(existing.len() + mcp_block.len() + 32);
    let mut in_mcp_section = false;
    let mut saw_mcp_child = false;
    let mut inserted = false;
    let lines: Vec<&str> = existing.lines().collect();

    for line in &lines {
        if !inserted && line.trim_end() == "mcp_servers:" {
            in_mcp_section = true;
            out.push_str(line);
            out.push('\n');
            continue;
        }

        if in_mcp_section && !inserted {
            let is_child = line.starts_with("  ") && !line.trim().is_empty();
            let is_toplevel = !line.starts_with(' ') && !line.trim().is_empty();

            if is_child {
                saw_mcp_child = true;
                out.push_str(line);
                out.push('\n');
                continue;
            }

            if saw_mcp_child && (line.trim().is_empty() || is_toplevel) {
                out.push_str(mcp_block);
                out.push('\n');
                inserted = true;
                in_mcp_section = false;
            }
        }

        out.push_str(line);
        out.push('\n');
    }

    if in_mcp_section && !inserted {
        out.push_str(mcp_block);
        out.push('\n');
        inserted = true;
    }

    if !inserted {
        if !out.ends_with('\n') {
            out.push('\n');
        }
        out.push_str("\nmcp_servers:\n");
        out.push_str(mcp_block);
        out.push('\n');
    }

    out
}

fn backup_invalid_file(path: &std::path::Path) -> Result<(), String> {
    if !path.exists() {
        return Ok(());
    }
    let parent = path
        .parent()
        .ok_or_else(|| "invalid path (no parent directory)".to_string())?;
    let filename = path
        .file_name()
        .ok_or_else(|| "invalid path (no filename)".to_string())?
        .to_string_lossy();
    let pid = std::process::id();
    let nanos = std::time::SystemTime::now()
        .duration_since(std::time::UNIX_EPOCH)
        .map(|d| d.as_nanos())
        .unwrap_or(0);
    let bak = parent.join(format!("{filename}.lean-ctx.invalid.{pid}.{nanos}.bak"));
    std::fs::rename(path, bak).map_err(|e| e.to_string())?;
    Ok(())
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::path::PathBuf;

    fn target(path: PathBuf, ty: ConfigType) -> EditorTarget {
        EditorTarget {
            name: "test",
            agent_key: "test".to_string(),
            config_path: path,
            detect_path: PathBuf::from("/nonexistent"),
            config_type: ty,
        }
    }

    #[test]
    fn mcp_json_upserts_and_preserves_other_servers() {
        let dir = tempfile::tempdir().unwrap();
        let path = dir.path().join("mcp.json");
        std::fs::write(
            &path,
            r#"{ "mcpServers": { "other": { "command": "other-bin" }, "lean-ctx": { "command": "/old/path/lean-ctx", "autoApprove": [] } } }"#,
        )
        .unwrap();

        let t = target(path.clone(), ConfigType::McpJson);
        let res = write_mcp_json(&t, "/new/path/nebu-ctx", WriteOptions::default()).unwrap();
        assert_eq!(res.action, WriteAction::Updated);

        let json: Value = serde_json::from_str(&std::fs::read_to_string(&path).unwrap()).unwrap();
        assert_eq!(json["mcpServers"]["other"]["command"], "other-bin");
        assert_eq!(
            json["mcpServers"]["nebu-ctx"]["command"],
            "/new/path/nebu-ctx"
        );
        let approved = json["mcpServers"]["nebu-ctx"]["autoApprove"]
            .as_array()
            .expect("autoApprove should be an array");
        assert_eq!(approved.len(), 5);
    }

    #[test]
    fn crush_config_writes_mcp_root() {
        let dir = tempfile::tempdir().unwrap();
        let path = dir.path().join("crush.json");
        std::fs::write(
            &path,
            r#"{ "mcp": { "nebu-ctx": { "type": "stdio", "command": "old" } } }"#,
        )
        .unwrap();

        let t = target(path.clone(), ConfigType::Crush);
        let res = write_crush_config(&t, "new", WriteOptions::default()).unwrap();
        assert_eq!(res.action, WriteAction::Updated);

        let json: Value = serde_json::from_str(&std::fs::read_to_string(&path).unwrap()).unwrap();
        assert_eq!(json["mcp"]["nebu-ctx"]["type"], "stdio");
        assert_eq!(json["mcp"]["nebu-ctx"]["command"], "new");
    }

    #[test]
    fn codex_toml_upserts_existing_section() {
        let dir = tempfile::tempdir().unwrap();
        let path = dir.path().join("config.toml");
        std::fs::write(
            &path,
            r#"[mcp_servers.nebu-ctx]
command = "old"
args = ["x"]
"#,
        )
        .unwrap();

        let t = target(path.clone(), ConfigType::Codex);
        let res = write_codex_config(&t, "new").unwrap();
        assert_eq!(res.action, WriteAction::Updated);

        let content = std::fs::read_to_string(&path).unwrap();
        assert!(content.contains(r#"command = "new""#));
        assert!(content.contains("args = []"));
    }

    #[test]
    fn upsert_codex_toml_inserts_new_section_when_missing() {
        let updated = upsert_codex_toml("[other]\nx=1\n", "nebu-ctx");
        assert!(updated.contains("[mcp_servers.nebu-ctx]"));
        assert!(updated.contains("command = \"nebu-ctx\""));
        assert!(updated.contains("args = []"));
    }

    #[test]
    fn auto_approve_contains_core_tools() {
        let tools = auto_approve_tools();
        assert!(tools.contains(&"ctx_read"));
        assert!(tools.contains(&"ctx_shell"));
        assert!(tools.contains(&"ctx_search"));
        assert!(tools.contains(&"ctx_tree"));
        assert!(tools.contains(&"ctx"));
        assert_eq!(tools.len(), 5);
    }

    #[test]
    fn antigravity_config_omits_auto_approve() {
        let dir = tempfile::tempdir().unwrap();
        let path = dir.path().join("mcp_config.json");

        let t = EditorTarget {
            name: "Antigravity",
            agent_key: "gemini".to_string(),
            config_path: path.clone(),
            detect_path: PathBuf::from("/nonexistent"),
            config_type: ConfigType::McpJson,
        };
        let res = write_mcp_json(&t, "/usr/local/bin/nebu-ctx", WriteOptions::default()).unwrap();
        assert_eq!(res.action, WriteAction::Created);

        let json: Value = serde_json::from_str(&std::fs::read_to_string(&path).unwrap()).unwrap();
        assert!(json["mcpServers"]["nebu-ctx"]["autoApprove"].is_null());
        assert_eq!(
            json["mcpServers"]["nebu-ctx"]["command"],
            "/usr/local/bin/nebu-ctx"
        );
    }

    #[test]
    fn copilot_cli_config_migrates_to_camel_case_server_key() {
        let dir = tempfile::tempdir().unwrap();
        let path = dir.path().join("mcp-config.json");
        std::fs::write(
            &path,
            r#"{ "mcpServers": { "other": { "command": "other-bin" }, "nebu-ctx": { "command": "/old/path/nebu-ctx", "autoApprove": [] } } }"#,
        )
        .unwrap();

        let t = EditorTarget {
            name: "Copilot CLI",
            agent_key: "copilot".to_string(),
            config_path: path.clone(),
            detect_path: PathBuf::from("/nonexistent"),
            config_type: ConfigType::McpJson,
        };
        let res = write_mcp_json(&t, "/new/path/nebu-ctx", WriteOptions::default()).unwrap();
        assert_eq!(res.action, WriteAction::Updated);

        let json: Value = serde_json::from_str(&std::fs::read_to_string(&path).unwrap()).unwrap();
        assert_eq!(json["mcpServers"]["other"]["command"], "other-bin");
        assert!(json["mcpServers"].get("nebu-ctx").is_none());
        assert!(json["mcpServers"].get("lean-ctx").is_none());
        assert_eq!(
            json["mcpServers"][super::super::paths::COPILOT_MCP_SERVER_KEY]["command"],
            "/new/path/nebu-ctx"
        );
    }

    #[test]
    fn vscode_mcp_config_migrates_to_camel_case_server_key() {
        let dir = tempfile::tempdir().unwrap();
        let path = dir.path().join("mcp.json");
        std::fs::write(
            &path,
            r#"{ "servers": { "lean-ctx": { "type": "stdio", "command": "old" } } }"#,
        )
        .unwrap();

        let t = EditorTarget {
            name: "VS Code / Copilot",
            agent_key: "copilot".to_string(),
            config_path: path.clone(),
            detect_path: PathBuf::from("/nonexistent"),
            config_type: ConfigType::VsCodeMcp,
        };
        let res = write_vscode_mcp(&t, "/usr/local/bin/nebu-ctx", WriteOptions::default()).unwrap();
        assert_eq!(res.action, WriteAction::Updated);

        let json: Value = serde_json::from_str(&std::fs::read_to_string(&path).unwrap()).unwrap();
        assert!(json["servers"].get("nebu-ctx").is_none());
        assert!(json["servers"].get("lean-ctx").is_none());
        assert_eq!(
            json["servers"][super::super::paths::COPILOT_MCP_SERVER_KEY]["command"],
            "/usr/local/bin/nebu-ctx"
        );
    }

    #[test]
    fn opencode_config_uses_nebu_ctx_and_drops_legacy_key() {
        let dir = tempfile::tempdir().unwrap();
        let path = dir.path().join("opencode.json");
        std::fs::write(
            &path,
            r#"{ "mcp": { "other": { "type": "local", "command": ["foo"] }, "lean-ctx": { "type": "local", "command": ["old"], "enabled": true, "environment": { "NEBU_CTX_DATA_DIR": "/tmp" } } } }"#,
        )
        .unwrap();

        let t = target(path.clone(), ConfigType::OpenCode);
        let res =
            write_opencode_config(&t, "/usr/local/bin/nebu-ctx", WriteOptions::default()).unwrap();
        assert_eq!(res.action, WriteAction::Updated);

        let json: Value = serde_json::from_str(&std::fs::read_to_string(&path).unwrap()).unwrap();
        assert_eq!(json["mcp"]["other"]["command"], serde_json::json!(["foo"]));
        assert!(json["mcp"].get("lean-ctx").is_none());
        assert_eq!(
            json["mcp"]["nebu-ctx"]["command"],
            serde_json::json!(["/usr/local/bin/nebu-ctx"])
        );
        assert_eq!(json["mcp"]["nebu-ctx"]["enabled"], true);
        assert_eq!(
            json["instructions"],
            serde_json::json!(["./rules/nebu-ctx.md"])
        );
        assert_eq!(json["plugin"], serde_json::json!(["./plugins/nebu-ctx.ts"]));
        assert!(dir.path().join("rules/nebu-ctx.md").exists());
        assert!(dir.path().join("plugins/nebu-ctx.ts").exists());
        assert!(dir.path().join("skills/project-bootstrap/SKILL.md").exists());
        assert!(dir.path().join("skills/project-bootstrap/scripts/install.sh").exists());
    }

    #[test]
    fn opencode_config_adds_plugin_to_existing_array() {
        let dir = tempfile::tempdir().unwrap();
        let path = dir.path().join("opencode.json");
        std::fs::write(
            &path,
            r#"{ "plugin": [["./plugins/skill-router.js", { "maxHints": 3 }]], "instructions": [], "mcp": {} }"#,
        )
        .unwrap();

        let t = target(path.clone(), ConfigType::OpenCode);
        let res =
            write_opencode_config(&t, "/usr/local/bin/nebu-ctx", WriteOptions::default()).unwrap();
        assert_eq!(res.action, WriteAction::Updated);

        let json: Value = serde_json::from_str(&std::fs::read_to_string(&path).unwrap()).unwrap();
        let plugins = json["plugin"].as_array().expect("plugin should be array");
        assert_eq!(plugins.len(), 2);
        assert_eq!(plugins[0][0], "./plugins/skill-router.js");
        assert_eq!(plugins[1], serde_json::json!("./plugins/nebu-ctx.ts"));
        assert!(dir.path().join("skills/project-bootstrap/SKILL.md").exists());
    }

    #[test]
    fn opencode_plugin_template_uses_nebu_ctx() {
        let plugin = include_str!("../../templates/opencode-plugin.ts");
        assert!(plugin.contains("NebuCtxOpenCodePlugin"));
        assert!(plugin.contains("resolveConfiguredNebuBinary"));
        assert!(plugin.contains("config.mcp?.[NEBU]?.command"));
        assert!(plugin.contains("const dataDir = process.env[\"NEBU_CTX_DATA_DIR\"]"));
        assert!(plugin.contains("const result = await runNebu([\"--version\"])"));
        assert!(plugin.contains("NEBU_CTX_BIN: nebuBinary"));
        assert!(plugin.contains("runNebu([\"hook\", \"rewrite-inline\", command])"));
        assert!(plugin.contains("experimental.chat.system.transform"));
        assert!(plugin.contains("experimental.session.compacting"));
        assert!(plugin.contains("message.updated"));
        assert!(plugin.contains("message.part.updated"));
        assert!(plugin.contains("assistant-output-submit"));
        assert!(plugin.contains("tool-activity"));
        assert!(plugin.contains("idle-flush"));
        assert!(plugin.contains("session.compacted"));
        assert!(plugin.contains("session.idle"));
        assert!(!plugin.contains("lean-ctx hook rewrite-inline"));
    }

    #[test]
    fn hermes_yaml_inserts_into_existing_mcp_servers() {
        let existing = "model: anthropic/claude-sonnet-4\n\nmcp_servers:\n  github:\n    command: \"npx\"\n    args: [\"-y\", \"@modelcontextprotocol/server-github\"]\n\ntool_allowlist:\n  - terminal\n";
        let block = "  nebu-ctx:\n    command: \"nebu-ctx\"\n    env:\n      NEBU_CTX_DATA_DIR: \"/home/user/.nebu-ctx\"";
        let result = upsert_hermes_yaml_mcp(existing, block);
        assert!(result.contains("nebu-ctx"));
        assert!(result.contains("model: anthropic/claude-sonnet-4"));
        assert!(result.contains("tool_allowlist:"));
        assert!(result.contains("github:"));
    }

    #[test]
    fn hermes_yaml_creates_mcp_servers_section() {
        let existing = "model: openai/gpt-4o\n";
        let block = "  nebu-ctx:\n    command: \"nebu-ctx\"";
        let result = upsert_hermes_yaml_mcp(existing, block);
        assert!(result.contains("mcp_servers:"));
        assert!(result.contains("nebu-ctx"));
        assert!(result.contains("model: openai/gpt-4o"));
    }

    #[test]
    fn hermes_yaml_skips_if_already_present() {
        let dir = tempfile::tempdir().unwrap();
        let path = dir.path().join("config.yaml");
        std::fs::write(
            &path,
            "mcp_servers:\n  nebu-ctx:\n    command: \"nebu-ctx\"\n",
        )
        .unwrap();
        let t = target(path.clone(), ConfigType::HermesYaml);
        let res = write_hermes_yaml(&t, "nebu-ctx", WriteOptions::default()).unwrap();
        assert_eq!(res.action, WriteAction::Already);
    }
}
