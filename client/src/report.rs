//! `nebu-ctx report-issue` — collects diagnostics and creates or updates a GitHub issue.

use std::collections::HashSet;
use std::path::PathBuf;

const VERSION: &str = env!("CARGO_PKG_VERSION");
const REPO: &str = "MarkBovee/nebu-ctx";
const BOLD: &str = "\x1b[1m";
const RST: &str = "\x1b[0m";
const DIM: &str = "\x1b[2m";
const GREEN: &str = "\x1b[32m";
const YELLOW: &str = "\x1b[33m";

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
enum RunMode {
    Interactive,
    Automated,
}

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
enum SubmissionState {
    Created,
    Updated,
    Blocked,
    SavedLocally,
}

#[derive(Debug, Clone)]
struct ReportOptions {
    mode: RunMode,
    title: Option<String>,
    description: Option<String>,
    expected: Option<String>,
    actual: Option<String>,
    repro: Option<String>,
    tool: Option<String>,
    dry_run: bool,
    include_tee: bool,
    search_duplicates: bool,
    allow_sensitive: bool,
}

#[derive(Debug, Clone)]
struct DuplicateIssue {
    number: u64,
    title: String,
    url: String,
}

#[derive(Debug, Clone)]
struct SubmissionOutcome {
    state: SubmissionState,
}

pub fn run(args: &[String]) {
    let options = parse_options(args);

    println!("{BOLD}nebu-ctx report-issue{RST}\n");

    let title = options.title.clone().unwrap_or_else(|| prompt_input("Issue title"));
    if title.trim().is_empty() {
        eprintln!("Title is required. Aborting.");
        std::process::exit(1);
    }

    let description = options
        .description
        .clone()
        .unwrap_or_else(|| prompt_input("Describe the problem"));

    println!("\n{DIM}Collecting diagnostics...{RST}");
    let body = build_report_body(&description, &options);

    if options.mode == RunMode::Interactive || options.dry_run {
        println!("\n{BOLD}=== Preview ==={RST}\n");
        let preview: String = body.chars().take(2000).collect();
        println!("{preview}");
        if body.len() > 2000 {
            println!("{DIM}... ({} more characters){RST}", body.len() - 2000);
        }
    }

    if options.dry_run {
        println!("\n{YELLOW}--dry-run: not submitting.{RST}");
        let path = save_report_locally(&body);
        println!("Report saved to {}", path.display());
        return;
    }

    if options.mode == RunMode::Interactive {
        println!("\n{BOLD}Submit this as a GitHub issue to {REPO}?{RST} [y/N]");
        let mut answer = String::new();
        let _ = std::io::stdin().read_line(&mut answer);
        if !answer.trim().eq_ignore_ascii_case("y") {
            println!("Aborted.");
            let path = save_report_locally(&body);
            println!("Report saved to {}", path.display());
            return;
        }
    }

    let outcome = submit_report(&title, &body, &options);
    match outcome.state {
        SubmissionState::Created | SubmissionState::Updated => std::process::exit(0),
        SubmissionState::Blocked => std::process::exit(2),
        SubmissionState::SavedLocally => std::process::exit(1),
    }
}

fn parse_options(args: &[String]) -> ReportOptions {
    let automated = has_flag(args, "--submit") || has_flag(args, "--yes");
    ReportOptions {
        mode: if automated {
            RunMode::Automated
        } else {
            RunMode::Interactive
        },
        title: extract_flag(args, "--title"),
        description: extract_flag(args, "--description"),
        expected: extract_flag(args, "--expected"),
        actual: extract_flag(args, "--actual"),
        repro: extract_flag(args, "--repro"),
        tool: extract_flag(args, "--tool"),
        dry_run: has_flag(args, "--dry-run"),
        include_tee: has_flag(args, "--include-tee"),
        search_duplicates: automated || has_flag(args, "--search-duplicates"),
        allow_sensitive: has_flag(args, "--allow-sensitive"),
    }
}

fn build_report_body(description: &str, options: &ReportOptions) -> String {
    let mut sections = Vec::new();

    sections.push(format!("## Description\n\n{description}"));
    if let Some(tool) = options.tool.as_deref() {
        sections.push(format!("## Tool\n\n- Public tool: `{tool}`"));
    }
    if options.expected.is_some() || options.actual.is_some() {
        let expected = options.expected.as_deref().unwrap_or("Not provided");
        let actual = options.actual.as_deref().unwrap_or("Not provided");
        sections.push(format!("## Expected vs Actual\n\n- Expected: {expected}\n- Actual: {actual}"));
    }
    if let Some(repro) = options.repro.as_deref() {
        sections.push(format!("## Repro\n\n{repro}"));
    }
    sections.push(section_environment());
    sections.push(section_configuration());
    sections.push(section_mcp_status());
    sections.push(section_performance());
    if should_include_sensitive_sections(options) {
        sections.push(section_tool_calls());
        sections.push(section_session());
        sections.push(section_slow_commands());
        sections.push(section_tee_logs(options.include_tee));
        sections.push(section_project_context());
    } else {
        sections.push(String::from(
            "## Privacy\n\nSensitive local diagnostics were omitted from automatic submission. Use `--allow-sensitive` to include recent tool history, tee logs, and project-local context.",
        ));
    }

    anonymize_report(&sections.join("\n\n---\n\n"))
}

fn submit_report(title: &str, body: &str, options: &ReportOptions) -> SubmissionOutcome {
    if let Some(reason) = privacy_block_reason(options) {
        let path = save_report_locally(body);
        eprintln!(
            "{YELLOW}Automatic submission blocked: {reason}. Saved report to {}{RST}",
            path.display()
        );
        return SubmissionOutcome {
            state: SubmissionState::Blocked,
        };
    }

    let duplicate = if options.search_duplicates {
        find_duplicate_issue(title, options.tool.as_deref())
    } else {
        None
    };

    if let Some(issue) = duplicate {
        if let Some(url) = try_update_duplicate_issue(&issue, body) {
            println!("\n{GREEN}Issue updated:{RST} {url}");
            return SubmissionOutcome {
                state: SubmissionState::Updated,
            };
        }
    }

    if let Some(url) = try_gh_issue_create(title, body) {
        println!("\n{GREEN}Issue created:{RST} {url}");
        return SubmissionOutcome {
            state: SubmissionState::Created,
        };
    }

    let path = save_report_locally(body);
    eprintln!("{YELLOW}Issue submission did not complete. Saved report to {}{RST}", path.display());
    SubmissionOutcome {
        state: SubmissionState::SavedLocally,
    }
}

fn find_duplicate_issue(title: &str, tool: Option<&str>) -> Option<DuplicateIssue> {
    let gh = find_gh_binary()?;
    let mut query = title.to_string();
    if let Some(tool) = tool {
        query.push(' ');
        query.push_str(tool);
    }

    let output = std::process::Command::new(&gh)
        .args([
            "issue",
            "list",
            "--repo",
            REPO,
            "--state",
            "open",
            "--search",
            &query,
            "--json",
            "number,title,url",
            "--limit",
            "5",
        ])
        .output()
        .ok()?;

    if !output.status.success() {
        return None;
    }

    let json = serde_json::from_slice::<serde_json::Value>(&output.stdout).ok()?;
    let items = json.as_array()?;
    items
        .iter()
        .filter_map(|item| {
            let issue = DuplicateIssue {
                number: item.get("number")?.as_u64()?,
                title: item.get("title")?.as_str()?.to_string(),
                url: item.get("url")?.as_str()?.to_string(),
            };
            let score = duplicate_match_score(title, &issue.title, tool);
            (score >= 0.6).then_some((score, issue))
        })
        .max_by(|left, right| left.0.partial_cmp(&right.0).unwrap_or(std::cmp::Ordering::Equal))
        .map(|(_, issue)| issue)
}

fn should_include_sensitive_sections(options: &ReportOptions) -> bool {
    options.mode == RunMode::Interactive || options.allow_sensitive
}

fn privacy_block_reason(options: &ReportOptions) -> Option<&'static str> {
    if options.mode == RunMode::Automated && options.include_tee && !options.allow_sensitive {
        return Some("`--include-tee` requires `--allow-sensitive` in automation mode");
    }

    None
}

fn duplicate_match_score(requested_title: &str, candidate_title: &str, tool: Option<&str>) -> f32 {
    let requested_norm = normalize_title(requested_title);
    let candidate_norm = normalize_title(candidate_title);
    if requested_norm.is_empty() || candidate_norm.is_empty() {
        return 0.0;
    }

    if requested_norm == candidate_norm
        || requested_norm.contains(&candidate_norm)
        || candidate_norm.contains(&requested_norm)
    {
        return 1.0;
    }

    let requested_tokens = title_tokens(&requested_norm);
    let candidate_tokens = title_tokens(&candidate_norm);
    if requested_tokens.is_empty() || candidate_tokens.is_empty() {
        return 0.0;
    }

    let overlap = requested_tokens.intersection(&candidate_tokens).count() as f32 / requested_tokens.len() as f32;
    let tool_bonus = tool
        .map(normalize_title)
        .filter(|value| !value.is_empty() && candidate_norm.contains(value))
        .map(|_| 0.15)
        .unwrap_or(0.0);

    (overlap + tool_bonus).min(1.0)
}

fn normalize_title(value: &str) -> String {
    value
        .chars()
        .map(|ch| if ch.is_ascii_alphanumeric() { ch.to_ascii_lowercase() } else { ' ' })
        .collect::<String>()
        .split_whitespace()
        .collect::<Vec<_>>()
        .join(" ")
}

fn title_tokens(value: &str) -> HashSet<String> {
    value
        .split_whitespace()
        .filter(|token| token.len() >= 3)
        .map(|token| token.to_string())
        .collect()
}

fn try_update_duplicate_issue(issue: &DuplicateIssue, body: &str) -> Option<String> {
    let gh = find_gh_binary()?;
    let comment = format!(
        "Automatic duplicate report for `{}`:\n\n{}",
        issue.title, body
    );
    let output = std::process::Command::new(&gh)
        .args([
            "issue",
            "comment",
            &issue.number.to_string(),
            "--repo",
            REPO,
            "--body",
            &comment,
        ])
        .output()
        .ok()?;
    if output.status.success() {
        Some(issue.url.clone())
    } else {
        None
    }
}

fn try_gh_issue_create(title: &str, body: &str) -> Option<String> {
    let gh = find_gh_binary()?;
    let tmp = std::env::temp_dir().join("nebu-ctx-report.md");
    std::fs::write(&tmp, body).ok()?;

    let result = std::process::Command::new(&gh)
        .args([
            "issue",
            "create",
            "--repo",
            REPO,
            "--title",
            title,
            "--body-file",
            &tmp.to_string_lossy(),
            "--label",
            "bug,auto-report",
        ])
        .output()
        .ok();

    let _ = std::fs::remove_file(&tmp);

    match result {
        Some(output) if output.status.success() => Some(String::from_utf8_lossy(&output.stdout).trim().to_string()),
        Some(output) => {
            let stderr = String::from_utf8_lossy(&output.stderr);
            if stderr.contains("not logged") || stderr.contains("auth login") {
                eprintln!("{YELLOW}gh CLI found but not authenticated. Run: gh auth login{RST}");
            } else {
                eprintln!("{YELLOW}gh issue create failed: {}{RST}", stderr.trim());
            }
            None
        }
        None => None,
    }
}

fn section_environment() -> String {
    let os = std::env::consts::OS;
    let arch = std::env::consts::ARCH;
    let shell = std::env::var("SHELL").unwrap_or_else(|_| "unknown".into());
    let ide = detect_ide();

    format!(
        "## Environment\n\n| Field | Value |\n|---|---|\n| nebu-ctx | {VERSION} |\n| OS | {os} {arch} |\n| Shell | {shell} |\n| IDE | {ide} |"
    )
}

fn section_configuration() -> String {
    let mut out = String::from("## Configuration\n\n```toml\n");
    if let Some(dir) = nebu_ctx_dir() {
        let config_path = dir.join("config.toml");
        if let Ok(content) = std::fs::read_to_string(&config_path) {
            out.push_str(&mask_secrets(&content));
        } else {
            out.push_str("# config.toml not found — using defaults");
        }
    }
    out.push_str("\n```");
    out
}

fn section_mcp_status() -> String {
    let mut lines = vec!["## MCP Integration Status\n".to_string()];
    let binary_ok = which_nebu_ctx().is_some();
    lines.push(format!("- Binary on PATH: {}", if binary_ok { "yes" } else { "no" }));
    lines.push(format!("- Shell hooks: {}", check_shell_hooks()));
    lines.push(format!("- MCP configured for: {}", check_mcp_configs()));
    lines.join("\n")
}

fn section_tool_calls() -> String {
    let mut out = String::from("## Recent Tool Calls\n\n```\n");
    if let Some(dir) = nebu_ctx_dir() {
        let log_path = dir.join("tool-calls.log");
        if let Ok(content) = std::fs::read_to_string(&log_path) {
            let lines: Vec<&str> = content.lines().collect();
            let start = lines.len().saturating_sub(20);
            for line in &lines[start..] {
                out.push_str(line);
                out.push('\n');
            }
        } else {
            out.push_str("# No tool call log found\n");
        }
    }
    out.push_str("```");
    out
}

fn section_session() -> String {
    let mut out = String::from("## Session State\n\n");
    if let Some(dir) = nebu_ctx_dir() {
        let latest = dir.join("sessions").join("latest.json");
        if let Ok(content) = std::fs::read_to_string(&latest) {
            if let Ok(val) = serde_json::from_str::<serde_json::Value>(&content) {
                if let Some(task) = val.get("task") {
                    out.push_str(&format!(
                        "- Task: {}\n",
                        task.get("description").and_then(|d| d.as_str()).unwrap_or("-")
                    ));
                }
                if let Some(stats) = val.get("stats") {
                    out.push_str(&format!("- Stats: {}\n", stats));
                }
                if let Some(files) = val.get("files_touched").and_then(|f| f.as_object()) {
                    out.push_str(&format!("- Files touched: {}\n", files.len()));
                }
            }
        } else {
            out.push_str("No active session found.\n");
        }
    }
    out
}

fn section_performance() -> String {
    let mut out = String::from("## Performance Metrics\n\n");
    if let Some(dir) = nebu_ctx_dir() {
        let mcp_live = dir.join("mcp-live.json");
        if let Ok(content) = std::fs::read_to_string(&mcp_live) {
            if let Ok(val) = serde_json::from_str::<serde_json::Value>(&content) {
                let fields = [
                    "cep_score",
                    "cache_utilization",
                    "compression_rate",
                    "tokens_saved",
                    "tokens_original",
                    "tool_calls",
                ];
                out.push_str("| Metric | Value |\n|---|---|\n");
                for field in fields {
                    if let Some(v) = val.get(field) {
                        out.push_str(&format!("| {field} | {v} |\n"));
                    }
                }
            }
        }

        let stats_path = dir.join("stats.json");
        if let Ok(content) = std::fs::read_to_string(&stats_path) {
            if let Ok(val) = serde_json::from_str::<serde_json::Value>(&content) {
                if let Some(cmds) = val.get("commands").and_then(|c| c.as_object()) {
                    let mut top: Vec<_> = cmds
                        .iter()
                        .filter_map(|(k, v)| v.get("count").and_then(|c| c.as_u64()).map(|c| (k, c)))
                        .collect();
                    top.sort_by_key(|x| std::cmp::Reverse(x.1));
                    top.truncate(5);
                    out.push_str("\n**Top 5 tools:**\n");
                    for (name, count) in top {
                        out.push_str(&format!("- {name}: {count} calls\n"));
                    }
                }
            }
        }
    }
    out
}

fn section_slow_commands() -> String {
    let mut out = String::from("## Slow Commands\n\n```\n");
    if let Some(dir) = nebu_ctx_dir() {
        let log_path = dir.join("slow-commands.log");
        if let Ok(content) = std::fs::read_to_string(&log_path) {
            let lines: Vec<&str> = content.lines().collect();
            let start = lines.len().saturating_sub(10);
            for line in &lines[start..] {
                out.push_str(line);
                out.push('\n');
            }
        } else {
            out.push_str("# No slow commands logged\n");
        }
    }
    out.push_str("```");
    out
}

fn section_tee_logs(include_content: bool) -> String {
    let mut out = String::from("## Tee Logs (last 24h)\n\n");
    if let Some(dir) = nebu_ctx_dir() {
        let tee_dir = dir.join("tee");
        if tee_dir.is_dir() {
            let cutoff = std::time::SystemTime::now() - std::time::Duration::from_secs(24 * 3600);
            let mut entries: Vec<_> = std::fs::read_dir(&tee_dir)
                .into_iter()
                .flatten()
                .filter_map(|entry| entry.ok())
                .filter(|entry| {
                    entry
                        .metadata()
                        .ok()
                        .and_then(|meta| meta.modified().ok())
                        .is_some_and(|modified| modified > cutoff)
                })
                .collect();
            entries.sort_by_key(|entry| {
                std::cmp::Reverse(
                    entry
                        .metadata()
                        .ok()
                        .and_then(|meta| meta.modified().ok())
                        .unwrap_or(std::time::SystemTime::UNIX_EPOCH),
                )
            });
            if entries.is_empty() {
                out.push_str("No tee logs in the last 24h.\n");
            } else {
                for entry in entries.iter().take(10) {
                    let size = entry.metadata().map(|meta| meta.len()).unwrap_or(0);
                    out.push_str(&format!("- `{}` ({size} bytes)\n", entry.file_name().to_string_lossy()));
                }
                if include_content {
                    if let Some(latest) = entries.first() {
                        if let Ok(content) = std::fs::read_to_string(latest.path()) {
                            let truncated: String = content.chars().take(3000).collect();
                            out.push_str(&format!(
                                "\n**Latest tee content (`{}`):**\n```\n{truncated}\n```",
                                latest.file_name().to_string_lossy()
                            ));
                        }
                    }
                }
            }
        } else {
            out.push_str("No tee directory found.\n");
        }
    }
    out
}

fn section_project_context() -> String {
    let mut out = String::from("## Project Context\n\n");
    let cwd = std::env::current_dir()
        .map(|path| path.to_string_lossy().to_string())
        .unwrap_or_else(|_| "unknown".into());
    out.push_str(&format!("- Working directory: {cwd}\n"));
    if let Ok(entries) = std::fs::read_dir(".") {
        out.push_str(&format!("- Files in root: {}\n", entries.filter_map(|entry| entry.ok()).count()));
    }
    out
}

fn anonymize_report(text: &str) -> String {
    let home = dirs::home_dir()
        .map(|path| path.to_string_lossy().to_string())
        .unwrap_or_default();
    let mut result = text.to_string();
    if !home.is_empty() {
        result = result.replace(&home, "~");
    }
    let user = std::env::var("USER").or_else(|_| std::env::var("USERNAME")).unwrap_or_default();
    if user.len() > 2 {
        result = result.replace(&user, "<user>");
    }
    result
}

fn mask_secrets(text: &str) -> String {
    let mut out = String::new();
    for line in text.lines() {
        if line.contains("token")
            || line.contains("key")
            || line.contains("secret")
            || line.contains("password")
            || line.contains("api_key")
        {
            if let Some(eq) = line.find('=') {
                out.push_str(&line[..=eq]);
                out.push_str(" \"[REDACTED]\"");
            } else {
                out.push_str(line);
            }
        } else {
            out.push_str(line);
        }
        out.push('\n');
    }
    out
}

fn find_gh_binary() -> Option<PathBuf> {
    let candidates = [
        "/opt/homebrew/bin/gh",
        "/usr/local/bin/gh",
        "/usr/bin/gh",
        "/home/linuxbrew/.linuxbrew/bin/gh",
    ];
    for candidate in &candidates {
        let path = std::path::Path::new(candidate);
        if path.exists() {
            return Some(path.to_path_buf());
        }
    }
    if let Ok(output) = std::process::Command::new("which").arg("gh").output() {
        if output.status.success() {
            let path = String::from_utf8_lossy(&output.stdout).trim().to_string();
            if !path.is_empty() {
                return Some(PathBuf::from(path));
            }
        }
    }
    None
}

fn save_report_locally(body: &str) -> PathBuf {
    let dir = nebu_ctx_dir().unwrap_or_else(std::env::temp_dir);
    let _ = std::fs::create_dir_all(&dir);
    let path = dir.join("last-report.md");
    let _ = std::fs::write(&path, body);
    path
}

fn nebu_ctx_dir() -> Option<PathBuf> {
    crate::core::data_dir::nebu_ctx_data_dir().ok()
}

fn which_nebu_ctx() -> Option<PathBuf> {
    let cmd = if cfg!(windows) { "where" } else { "which" };
    std::process::Command::new(cmd)
        .arg("nebu-ctx")
        .output()
        .ok()
        .filter(|output| output.status.success())
        .map(|output| PathBuf::from(String::from_utf8_lossy(&output.stdout).trim().to_string()))
}

fn check_shell_hooks() -> String {
    let Some(home) = dirs::home_dir() else {
        return "unknown".into();
    };
    let mut found = Vec::new();
    for (file, name) in [
        (".zshrc", "zsh"),
        (".bashrc", "bash"),
        (".config/fish/config.fish", "fish"),
    ] {
        let path = home.join(file);
        if let Ok(content) = std::fs::read_to_string(&path) {
            if content.contains("lean-ctx") || content.contains("nebu-ctx") {
                found.push(name);
            }
        }
    }
    if found.is_empty() { "none detected".into() } else { found.join(", ") }
}

fn check_mcp_configs() -> String {
    let Some(home) = dirs::home_dir() else {
        return "unknown".into();
    };
    let mut found = Vec::new();
    let claude_cfg = crate::setup::claude_config_json_path(&home);
    let configs = vec![
        (home.join(".cursor/mcp.json"), "Cursor"),
        (claude_cfg, "Claude Code"),
        (home.join(".codeium/windsurf/mcp_config.json"), "Windsurf"),
    ];
    for (path, name) in &configs {
        if let Ok(content) = std::fs::read_to_string(path) {
            if content.contains("lean-ctx") || content.contains("nebu-ctx") {
                found.push(*name);
            }
        }
    }
    if found.is_empty() { "none".into() } else { found.join(", ") }
}

fn detect_ide() -> String {
    if std::env::var("CURSOR_SESSION").is_ok() || std::env::var("CURSOR_TRACE_DIR").is_ok() {
        return "Cursor".into();
    }
    if std::env::var("VSCODE_PID").is_ok() {
        return "VS Code".into();
    }
    "unknown".into()
}

fn extract_flag(args: &[String], flag: &str) -> Option<String> {
    args.windows(2).find(|window| window[0] == flag).map(|window| window[1].clone())
}

fn has_flag(args: &[String], flag: &str) -> bool {
    args.iter().any(|arg| arg == flag)
}

fn prompt_input(label: &str) -> String {
    eprint!("{BOLD}{label}:{RST} ");
    let mut input = String::new();
    let _ = std::io::stdin().read_line(&mut input);
    input.trim().to_string()
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn parse_options_detects_automation_mode() {
        let args = vec![
            "--submit".to_string(),
            "--title".to_string(),
            "bug".to_string(),
        ];
        let options = parse_options(&args);
        assert_eq!(options.mode, RunMode::Automated);
        assert!(options.search_duplicates);
        assert_eq!(options.title.as_deref(), Some("bug"));
    }

    #[test]
    fn build_report_body_includes_structured_bug_details() {
        let options = ReportOptions {
            mode: RunMode::Automated,
            title: Some("bug".to_string()),
            description: Some("desc".to_string()),
            expected: Some("exp".to_string()),
            actual: Some("act".to_string()),
            repro: Some("step 1".to_string()),
            tool: Some("ctx_search".to_string()),
            dry_run: false,
            include_tee: false,
            search_duplicates: true,
            allow_sensitive: false,
        };
        let body = build_report_body("desc", &options);
        assert!(body.contains("Public tool: `ctx_search`"));
        assert!(body.contains("Expected: exp"));
        assert!(body.contains("Actual: act"));
        assert!(body.contains("## Repro"));
        assert!(body.contains("Sensitive local diagnostics were omitted"));
        assert!(!body.contains("## Recent Tool Calls"));
    }

    #[test]
    fn privacy_block_requires_sensitive_opt_in_for_tee_uploads() {
        let options = ReportOptions {
            mode: RunMode::Automated,
            title: None,
            description: None,
            expected: None,
            actual: None,
            repro: None,
            tool: None,
            dry_run: false,
            include_tee: true,
            search_duplicates: true,
            allow_sensitive: false,
        };

        assert_eq!(
            privacy_block_reason(&options),
            Some("`--include-tee` requires `--allow-sensitive` in automation mode")
        );
    }

    #[test]
    fn duplicate_match_requires_confident_overlap() {
        assert!(
            duplicate_match_score(
                "ctx_search false zero matches when ripgrep finds results",
                "ctx_search false zero matches when ripgrep finds matches",
                Some("ctx_search")
            ) >= 0.6
        );
        assert!(
            duplicate_match_score(
                "ctx_search false zero matches when ripgrep finds results",
                "dashboard theme colors look wrong on mobile",
                Some("ctx_search")
            ) < 0.6
        );
    }
}
