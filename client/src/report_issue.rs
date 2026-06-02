use anyhow::{anyhow, Context, Result};
use chrono::Utc;
use serde::Deserialize;
use std::io::{self, Write};
use std::path::PathBuf;
use std::process::Command;

const DEFAULT_REPO: &str = "MarkBovee/nebu-ctx";

#[derive(Debug, Clone, Default)]
pub struct ReportIssueArgs {
    pub submit: bool,
    pub search_duplicates: bool,
    pub title: Option<String>,
    pub summary: Option<String>,
    pub tool: Option<String>,
    pub repro: Option<String>,
    pub expected: Option<String>,
    pub actual: Option<String>,
    pub environment: Option<String>,
    pub notes: Option<String>,
    pub command: Option<String>,
    pub repo: String,
}

#[derive(Debug, Clone)]
pub struct ReportIssueResult {
    pub status: &'static str,
    pub issue_number: Option<u64>,
    pub issue_url: Option<String>,
    pub report_path: PathBuf,
    pub message: String,
}

#[derive(Debug, Clone)]
struct IssueReport {
    title: String,
    body: String,
    search_query: String,
    tool: Option<String>,
}

#[derive(Debug, Clone, Deserialize)]
struct GhIssue {
    number: u64,
    title: String,
    url: String,
    #[serde(default)]
    body: String,
}

pub fn run(raw_args: &[String]) -> i32 {
    match parse_args(raw_args).and_then(execute) {
        Ok(result) => {
            print_result(&result);
            if matches!(result.status, "saved" | "blocked") {
                1
            } else {
                0
            }
        }
        Err(err) => {
            eprintln!("nebu-ctx report-issue: {err}");
            1
        }
    }
}

fn parse_args(raw_args: &[String]) -> Result<ReportIssueArgs> {
    let mut args = ReportIssueArgs {
        repo: DEFAULT_REPO.to_string(),
        ..ReportIssueArgs::default()
    };
    let mut index = 0usize;

    while index < raw_args.len() {
        let flag = raw_args[index].as_str();
        match flag {
            "--submit" => args.submit = true,
            "--search-duplicates" => args.search_duplicates = true,
            "--title" => {
                index += 1;
                args.title = Some(required_value(raw_args, index, flag)?);
            }
            "--summary" => {
                index += 1;
                args.summary = Some(required_value(raw_args, index, flag)?);
            }
            "--tool" => {
                index += 1;
                args.tool = Some(required_value(raw_args, index, flag)?);
            }
            "--repro" => {
                index += 1;
                args.repro = Some(required_value(raw_args, index, flag)?);
            }
            "--expected" => {
                index += 1;
                args.expected = Some(required_value(raw_args, index, flag)?);
            }
            "--actual" => {
                index += 1;
                args.actual = Some(required_value(raw_args, index, flag)?);
            }
            "--environment" => {
                index += 1;
                args.environment = Some(required_value(raw_args, index, flag)?);
            }
            "--notes" => {
                index += 1;
                args.notes = Some(required_value(raw_args, index, flag)?);
            }
            "--command" => {
                index += 1;
                args.command = Some(required_value(raw_args, index, flag)?);
            }
            "--repo" => {
                index += 1;
                args.repo = required_value(raw_args, index, flag)?;
            }
            "--help" | "-h" => return Err(anyhow!(help_text())),
            other => return Err(anyhow!("unknown flag: {other}\n\n{}", help_text())),
        }
        index += 1;
    }

    Ok(args)
}

fn required_value(raw_args: &[String], index: usize, flag: &str) -> Result<String> {
    raw_args
        .get(index)
        .cloned()
        .ok_or_else(|| anyhow!("missing value for {flag}"))
}

fn execute(args: ReportIssueArgs) -> Result<ReportIssueResult> {
    let report = build_report(&args)?;
    let report_path = save_report(&report)?;

    if !args.submit {
        print_preview(&report, &report_path)?;
        if confirm_submit()? {
            return submit_report(&args, report, report_path);
        }

        return Ok(ReportIssueResult {
            status: "saved",
            issue_number: None,
            issue_url: None,
            report_path,
            message: "Issue report saved locally; submission skipped.".to_string(),
        });
    }

    submit_report(&args, report, report_path)
}

fn build_report(args: &ReportIssueArgs) -> Result<IssueReport> {
    let title = args
        .title
        .clone()
        .or_else(|| args.summary.clone())
        .ok_or_else(|| anyhow!("--title or --summary is required"))?;

    let environment = args.environment.clone().unwrap_or_else(default_environment);
    let summary = args.summary.clone().unwrap_or_else(|| title.clone());
    let body = format_issue_body(args, &summary, &environment);
    let search_query = build_search_query(args, &title, &summary);

    Ok(IssueReport {
        title,
        body,
        search_query,
        tool: args.tool.clone(),
    })
}

fn format_issue_body(args: &ReportIssueArgs, summary: &str, environment: &str) -> String {
    let mut sections = Vec::new();
    sections.push(format!("## Summary\n{}", sanitize(summary)));

    if let Some(tool) = args.tool.as_deref() {
        sections.push(format!("## Tool\n- `{}`", sanitize(tool)));
    }
    if let Some(command) = args.command.as_deref() {
        sections.push(format!(
            "## Failing Call\n```text\n{}\n```",
            sanitize(command)
        ));
    }
    if let Some(repro) = args.repro.as_deref() {
        sections.push(format!("## Repro\n{}", sanitize(repro)));
    }
    if let Some(expected) = args.expected.as_deref() {
        sections.push(format!("## Expected\n{}", sanitize(expected)));
    }
    if let Some(actual) = args.actual.as_deref() {
        sections.push(format!("## Actual\n{}", sanitize(actual)));
    }

    sections.push(format!("## Environment\n{}", sanitize(environment)));

    if let Some(notes) = args.notes.as_deref() {
        sections.push(format!("## Notes\n{}", sanitize(notes)));
    }

    sections.join("\n\n")
}

fn build_search_query(args: &ReportIssueArgs, title: &str, summary: &str) -> String {
    let mut parts = Vec::new();
    if let Some(tool) = args.tool.as_deref() {
        parts.push(tool.to_string());
    }
    parts.extend(tokenize_for_search(title));
    parts.extend(tokenize_for_search(summary));
    parts.sort();
    parts.dedup();
    parts.into_iter().take(8).collect::<Vec<_>>().join(" ")
}

fn tokenize_for_search(input: &str) -> Vec<String> {
    input
        .split(|c: char| !c.is_ascii_alphanumeric() && c != '_' && c != '-')
        .filter(|part| part.len() >= 4)
        .map(|part| part.to_ascii_lowercase())
        .collect()
}

fn default_environment() -> String {
    let cwd = std::env::current_dir()
        .map(|path| path.to_string_lossy().to_string())
        .unwrap_or_else(|_| ".".to_string());
    format!(
        "- date: {}\n- os: {}\n- cwd: {}\n- nebu-ctx: {}",
        Utc::now().to_rfc3339(),
        std::env::consts::OS,
        sanitize(&cwd),
        env!("CARGO_PKG_VERSION")
    )
}

fn sanitize(input: &str) -> String {
    crate::shell::mask_sensitive_data(input).trim().to_string()
}

fn save_report(report: &IssueReport) -> Result<PathBuf> {
    let data_dir = crate::core::data_dir::nebu_ctx_data_dir()
        .map_err(|err| anyhow!(err))?
        .join("issue-reports");
    std::fs::create_dir_all(&data_dir).context("failed to create issue report directory")?;

    let slug = slugify(&report.title);
    let filename = format!("{}_{}.md", Utc::now().format("%Y%m%d-%H%M%S"), slug);
    let path = data_dir.join(filename);
    std::fs::write(&path, &report.body).context("failed to save issue report")?;
    Ok(path)
}

fn slugify(value: &str) -> String {
    let mut slug = String::new();
    for ch in value.chars() {
        if ch.is_ascii_alphanumeric() {
            slug.push(ch.to_ascii_lowercase());
        } else if (ch.is_ascii_whitespace() || ch == '-' || ch == '_') && !slug.ends_with('-') {
            slug.push('-');
        }
        if slug.len() >= 48 {
            break;
        }
    }
    slug.trim_matches('-')
        .to_string()
        .chars()
        .take(48)
        .collect::<String>()
}

fn print_preview(report: &IssueReport, report_path: &PathBuf) -> Result<()> {
    println!("Title: {}", report.title);
    println!();
    println!("{}", report.body);
    println!();
    println!("Saved draft: {}", report_path.to_string_lossy());
    io::stdout().flush().context("failed to flush preview")
}

fn confirm_submit() -> Result<bool> {
    print!("Submit to GitHub now? [y/N] ");
    io::stdout().flush().context("failed to flush prompt")?;
    let mut answer = String::new();
    io::stdin()
        .read_line(&mut answer)
        .context("failed to read confirmation")?;
    Ok(matches!(answer.trim(), "y" | "Y" | "yes" | "YES"))
}

fn submit_report(
    args: &ReportIssueArgs,
    report: IssueReport,
    report_path: PathBuf,
) -> Result<ReportIssueResult> {
    if let Err(err) = ensure_gh_available() {
        return Ok(ReportIssueResult {
            status: "saved",
            issue_number: None,
            issue_url: None,
            report_path,
            message: format!("{err}"),
        });
    }

    if args.search_duplicates {
        if let Some(existing) = match find_duplicate_issue(&args.repo, &report) {
            Ok(existing) => existing,
            Err(err) => {
                return Ok(ReportIssueResult {
                    status: "saved",
                    issue_number: None,
                    issue_url: None,
                    report_path,
                    message: format!("{err}"),
                });
            }
        } {
            if let Err(err) = add_duplicate_comment(&args.repo, existing.number, &report.body) {
                return Ok(ReportIssueResult {
                    status: "saved",
                    issue_number: Some(existing.number),
                    issue_url: Some(existing.url.clone()),
                    report_path,
                    message: format!("{err}"),
                });
            }
            return Ok(ReportIssueResult {
                status: "updated",
                issue_number: Some(existing.number),
                issue_url: Some(existing.url.clone()),
                report_path,
                message: format!(
                    "Updated existing issue #{}: {}",
                    existing.number, existing.title
                ),
            });
        }
    }

    let created = match create_issue(&args.repo, &report) {
        Ok(created) => created,
        Err(err) => {
            return Ok(ReportIssueResult {
                status: "saved",
                issue_number: None,
                issue_url: None,
                report_path,
                message: format!("{err}"),
            });
        }
    };
    Ok(ReportIssueResult {
        status: "created",
        issue_number: Some(created.number),
        issue_url: Some(created.url.clone()),
        report_path,
        message: format!("Created issue #{}: {}", created.number, created.title),
    })
}

fn ensure_gh_available() -> Result<()> {
    let status = Command::new("gh")
        .args(["auth", "status"])
        .status()
        .context("failed to run gh auth status")?;
    if status.success() {
        Ok(())
    } else {
        Err(anyhow!(
            "gh authentication unavailable. Draft saved locally for later submission."
        ))
    }
}

fn find_duplicate_issue(repo: &str, report: &IssueReport) -> Result<Option<GhIssue>> {
    if report.search_query.trim().is_empty() {
        return Ok(None);
    }

    let output = Command::new("gh")
        .args([
            "issue",
            "list",
            "--repo",
            repo,
            "--state",
            "open",
            "--search",
            &report.search_query,
            "--limit",
            "20",
            "--json",
            "number,title,url,body",
        ])
        .output()
        .context("failed to search GitHub issues")?;

    if !output.status.success() {
        return Err(anyhow!(
            "gh issue list failed: {}",
            String::from_utf8_lossy(&output.stderr).trim()
        ));
    }

    let issues: Vec<GhIssue> = serde_json::from_slice(&output.stdout)
        .context("failed to parse GitHub issue search response")?;
    let target_tool = report.tool.as_deref().map(str::to_ascii_lowercase);
    let summary_lower = report.title.to_ascii_lowercase();

    Ok(issues.into_iter().find(|issue| {
        let haystack = format!("{}\n{}", issue.title, issue.body).to_ascii_lowercase();
        let tool_match = target_tool
            .as_deref()
            .is_none_or(|tool| haystack.contains(tool));
        let title_overlap = tokenize_for_search(&summary_lower)
            .into_iter()
            .filter(|token| haystack.contains(token))
            .count();
        tool_match && title_overlap >= 2
    }))
}

fn add_duplicate_comment(repo: &str, issue_number: u64, body: &str) -> Result<()> {
    let comment = format!("Duplicate-aware automation update:\n\n{}", body);
    let output = Command::new("gh")
        .args([
            "issue",
            "comment",
            &issue_number.to_string(),
            "--repo",
            repo,
            "--body",
            &comment,
        ])
        .output()
        .context("failed to comment on existing GitHub issue")?;

    if output.status.success() {
        Ok(())
    } else {
        Err(anyhow!(
            "gh issue comment failed: {}",
            String::from_utf8_lossy(&output.stderr).trim()
        ))
    }
}

fn create_issue(repo: &str, report: &IssueReport) -> Result<GhIssue> {
    let output = Command::new("gh")
        .args([
            "issue",
            "create",
            "--repo",
            repo,
            "--title",
            &report.title,
            "--body",
            &report.body,
        ])
        .output()
        .context("failed to create GitHub issue")?;

    if !output.status.success() {
        return Err(anyhow!(
            "gh issue create failed: {}",
            String::from_utf8_lossy(&output.stderr).trim()
        ));
    }

    let url = String::from_utf8_lossy(&output.stdout).trim().to_string();
    if url.is_empty() {
        return Err(anyhow!("gh issue create returned no issue URL"));
    }

    let view = Command::new("gh")
        .args([
            "issue",
            "view",
            &url,
            "--repo",
            repo,
            "--json",
            "number,title,url",
        ])
        .output()
        .context("failed to read created GitHub issue")?;
    if !view.status.success() {
        return Err(anyhow!(
            "gh issue view failed: {}",
            String::from_utf8_lossy(&view.stderr).trim()
        ));
    }

    serde_json::from_slice(&view.stdout).context("failed to parse issue view response")
}

fn print_result(result: &ReportIssueResult) {
    println!("status: {}", result.status);
    if let Some(number) = result.issue_number {
        println!("issue: #{number}");
    }
    if let Some(url) = result.issue_url.as_deref() {
        println!("url: {url}");
    }
    println!("report: {}", result.report_path.to_string_lossy());
    println!("message: {}", result.message);
}

fn help_text() -> &'static str {
    "Usage: nebu-ctx report-issue [--submit] [--search-duplicates] --title <text> [options]\n\nOptions:\n  --summary <text>\n  --tool <name>\n  --command <text>\n  --repro <text>\n  --expected <text>\n  --actual <text>\n  --environment <text>\n  --notes <text>\n  --repo <owner/name>\n  --submit\n  --search-duplicates"
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn build_search_query_prefers_tool_and_keywords() {
        let args = ReportIssueArgs {
            tool: Some("ctx_search".to_string()),
            ..ReportIssueArgs::default()
        };
        let query = build_search_query(
            &args,
            "ctx_search invoke error",
            "cross workspace pollution",
        );
        assert!(query.contains("ctx_search"));
        assert!(query.contains("invoke"));
        assert!(query.contains("workspace"));
    }

    #[test]
    fn sanitize_redacts_tokens() {
        let text = sanitize("Authorization: Bearer ghp_123456789012345678901234567890123456");
        assert!(text.contains("[REDACTED"));
    }

    #[test]
    fn slugify_stays_ascii() {
        let slug = slugify("Shell output compression replaces JSON stdout");
        assert!(slug.contains("shell-output-compression"));
        assert!(!slug.contains(' '));
    }
}
