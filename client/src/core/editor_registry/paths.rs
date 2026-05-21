use std::path::{Path, PathBuf};

pub const PROJECT_BOOTSTRAP_SKILL_NAME: &str = "project-bootstrap";

pub fn zed_settings_path(home: &std::path::Path) -> PathBuf {
    if cfg!(target_os = "macos") {
        home.join("Library/Application Support/Zed/settings.json")
    } else {
        home.join(".config/zed/settings.json")
    }
}

pub fn zed_config_dir(home: &std::path::Path) -> PathBuf {
    if cfg!(target_os = "macos") {
        home.join("Library/Application Support/Zed")
    } else {
        home.join(".config/zed")
    }
}

pub fn vscode_mcp_path() -> PathBuf {
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

pub fn copilot_cli_mcp_path() -> PathBuf {
    dirs::home_dir()
        .map(|h| h.join(".copilot/mcp-config.json"))
        .unwrap_or_else(|| PathBuf::from("/nonexistent"))
}

pub fn detect_copilot_cli_path() -> PathBuf {
    dirs::home_dir()
        .map(|h| h.join(".copilot"))
        .unwrap_or_else(|| PathBuf::from("/nonexistent"))
}

#[allow(unreachable_code)]
pub fn cline_mcp_path() -> PathBuf {
    #[cfg(target_os = "windows")]
    {
        if let Ok(appdata) = std::env::var("APPDATA") {
            return PathBuf::from(appdata).join(
                "Code/User/globalStorage/saoudrizwan.claude-dev/settings/cline_mcp_settings.json",
            );
        }
        return PathBuf::from("/nonexistent");
    }

    let Some(home) = dirs::home_dir() else {
        return PathBuf::from("/nonexistent");
    };
    #[cfg(target_os = "macos")]
    {
        return home.join("Library/Application Support/Code/User/globalStorage/saoudrizwan.claude-dev/settings/cline_mcp_settings.json");
    }
    #[cfg(target_os = "linux")]
    {
        return home.join(".config/Code/User/globalStorage/saoudrizwan.claude-dev/settings/cline_mcp_settings.json");
    }
    PathBuf::from("/nonexistent")
}

#[allow(unreachable_code)]
pub fn roo_mcp_path() -> PathBuf {
    #[cfg(target_os = "windows")]
    {
        if let Ok(appdata) = std::env::var("APPDATA") {
            return PathBuf::from(appdata)
                .join("Code/User/globalStorage/rooveterinaryinc.roo-cline/settings/cline_mcp_settings.json");
        }
        return PathBuf::from("/nonexistent");
    }

    let Some(home) = dirs::home_dir() else {
        return PathBuf::from("/nonexistent");
    };
    #[cfg(target_os = "macos")]
    {
        return home.join("Library/Application Support/Code/User/globalStorage/rooveterinaryinc.roo-cline/settings/cline_mcp_settings.json");
    }
    #[cfg(target_os = "linux")]
    {
        return home.join(".config/Code/User/globalStorage/rooveterinaryinc.roo-cline/settings/cline_mcp_settings.json");
    }
    PathBuf::from("/nonexistent")
}

pub fn claude_mcp_json_path(home: &Path) -> PathBuf {
    if let Ok(dir) = std::env::var("CLAUDE_CONFIG_DIR") {
        let dir = dir.trim();
        if !dir.is_empty() {
            return PathBuf::from(dir).join(".claude.json");
        }
    }
    home.join(".claude.json")
}

pub fn claude_state_dir(home: &Path) -> PathBuf {
    if let Ok(dir) = std::env::var("CLAUDE_CONFIG_DIR") {
        let dir = dir.trim();
        if !dir.is_empty() {
            return PathBuf::from(dir);
        }
    }
    home.join(".claude")
}

pub fn claude_rules_dir(home: &Path) -> PathBuf {
    claude_state_dir(home).join("rules")
}

pub fn opencode_config_dir(home: &Path) -> PathBuf {
    home.join(".config").join("opencode")
}

pub fn opencode_config_path(home: &Path) -> PathBuf {
    opencode_config_dir(home).join("opencode.json")
}

pub fn opencode_rules_path(home: &Path) -> PathBuf {
    opencode_config_dir(home).join("rules").join("nebu-ctx.md")
}

pub fn opencode_plugin_path(home: &Path) -> PathBuf {
    opencode_config_dir(home)
        .join("plugins")
        .join("nebu-ctx.ts")
}

pub fn opencode_skill_dir(home: &Path) -> PathBuf {
    opencode_config_dir(home)
        .join("skills")
        .join(PROJECT_BOOTSTRAP_SKILL_NAME)
}

pub fn opencode_skill_path(home: &Path) -> PathBuf {
    opencode_skill_dir(home).join("SKILL.md")
}
