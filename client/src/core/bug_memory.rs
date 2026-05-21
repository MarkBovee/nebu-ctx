use chrono::{DateTime, Utc};
use serde::{Deserialize, Serialize};
use std::collections::hash_map::DefaultHasher;
use std::hash::{Hash, Hasher};
use std::path::PathBuf;

const MAX_PATTERNS: usize = 100;
const MAX_RECENT_FAILURES: usize = 30;
const REMINDER_LIMIT: usize = crate::core::budgets::PROSPECTIVE_REMINDERS_LIMIT;

#[derive(Debug, Clone, Serialize, Deserialize, PartialEq, Eq)]
pub enum BugMemorySource {
    Shell,
    McpShell,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct FailureRecord {
    pub command: String,
    pub exit_code: i32,
    pub signature: String,
    pub snippet: String,
    pub first_seen: DateTime<Utc>,
    pub last_seen: DateTime<Utc>,
    pub occurrences: u32,
    pub source: BugMemorySource,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct RecentFailure {
    pub command: String,
    pub exit_code: i32,
    pub signature: String,
    pub snippet: String,
    pub timestamp: DateTime<Utc>,
    pub source: BugMemorySource,
}

#[derive(Debug, Clone, Serialize, Deserialize, Default)]
pub struct BugMemoryStats {
    pub total_failures: u64,
    pub distinct_signatures: u64,
    pub shell_failures: u64,
    pub mcp_shell_failures: u64,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct BugMemoryStore {
    pub project_hash: String,
    #[serde(default)]
    pub failures: Vec<FailureRecord>,
    #[serde(default)]
    pub recent: Vec<RecentFailure>,
    #[serde(default)]
    pub stats: BugMemoryStats,
    pub updated_at: DateTime<Utc>,
}

impl BugMemoryStore {
    pub fn new(project_hash: &str) -> Self {
        Self {
            project_hash: project_hash.to_string(),
            failures: Vec::new(),
            recent: Vec::new(),
            stats: BugMemoryStats::default(),
            updated_at: Utc::now(),
        }
    }

    pub fn load(project_root: &str) -> Self {
        let hash = crate::core::project_hash::hash_project_root(project_root);
        let path = bug_memory_path(&hash);
        if let Ok(content) = std::fs::read_to_string(&path) {
            if let Ok(store) = serde_json::from_str::<BugMemoryStore>(&content) {
                return store;
            }
        }

        let legacy = legacy_gotcha_path(&hash);
        if let Ok(content) = std::fs::read_to_string(&legacy) {
            if !content.trim().is_empty() && content.trim() != "{}" {
                let _ = std::fs::remove_file(&legacy);
            }
        }

        Self::new(&hash)
    }

    pub fn save(&self, project_root: &str) -> Result<(), String> {
        let hash = crate::core::project_hash::hash_project_root(project_root);
        let path = bug_memory_path(&hash);
        if let Some(parent) = path.parent() {
            std::fs::create_dir_all(parent).map_err(|e| e.to_string())?;
        }
        let tmp = path.with_extension("tmp");
        let json = serde_json::to_string_pretty(self).map_err(|e| e.to_string())?;
        std::fs::write(&tmp, &json).map_err(|e| e.to_string())?;
        std::fs::rename(&tmp, &path).map_err(|e| e.to_string())?;
        Ok(())
    }

    pub fn clear(&mut self) {
        self.failures.clear();
        self.recent.clear();
        self.stats = BugMemoryStats::default();
        self.updated_at = Utc::now();
    }

    pub fn record_failure(
        &mut self,
        command: &str,
        exit_code: i32,
        output: &str,
        source: BugMemorySource,
    ) {
        if exit_code == 0 {
            return;
        }

        let signature = normalize_signature(output);
        if signature.is_empty() {
            return;
        }

        let snippet = build_snippet(output);
        let now = Utc::now();
        let id = failure_id(command, &signature);

        if let Some(existing) = self
            .failures
            .iter_mut()
            .find(|failure| failure_id(&failure.command, &failure.signature) == id)
        {
            existing.occurrences = existing.occurrences.saturating_add(1);
            existing.last_seen = now;
            existing.exit_code = exit_code;
            existing.snippet = snippet.clone();
            existing.source = source.clone();
        } else {
            self.failures.push(FailureRecord {
                command: command.to_string(),
                exit_code,
                signature: signature.clone(),
                snippet: snippet.clone(),
                first_seen: now,
                last_seen: now,
                occurrences: 1,
                source: source.clone(),
            });
        }

        self.failures.sort_by(|a, b| {
            b.occurrences
                .cmp(&a.occurrences)
                .then_with(|| b.last_seen.cmp(&a.last_seen))
        });
        if self.failures.len() > MAX_PATTERNS {
            self.failures.truncate(MAX_PATTERNS);
        }

        self.recent.push(RecentFailure {
            command: command.to_string(),
            exit_code,
            signature,
            snippet,
            timestamp: now,
            source: source.clone(),
        });
        if self.recent.len() > MAX_RECENT_FAILURES {
            let drain = self.recent.len() - MAX_RECENT_FAILURES;
            self.recent.drain(0..drain);
        }

        self.stats.total_failures = self.stats.total_failures.saturating_add(1);
        self.stats.distinct_signatures = self.failures.len() as u64;
        match source {
            BugMemorySource::Shell => {
                self.stats.shell_failures = self.stats.shell_failures.saturating_add(1)
            }
            BugMemorySource::McpShell => {
                self.stats.mcp_shell_failures = self.stats.mcp_shell_failures.saturating_add(1)
            }
        }
        self.updated_at = now;
    }

    pub fn format_list(&self) -> String {
        if self.failures.is_empty() {
            return "No client-side failures recorded for this project.".to_string();
        }

        let mut out = Vec::new();
        out.push(format!(
            "  {} recorded failure patterns\n",
            self.failures.len()
        ));
        for failure in &self.failures {
            out.push(format!(
                "  [{}x] {} (exit {}, last seen {})",
                failure.occurrences,
                truncate_chars(&failure.signature, 90),
                failure.exit_code,
                format_age(failure.last_seen),
            ));
            out.push(format!(
                "       cmd: {}",
                truncate_chars(&failure.command, 90)
            ));
            out.push(format!(
                "       out: {}",
                truncate_chars(&failure.snippet, 120)
            ));
            out.push(String::new());
        }
        out.pop();
        out.join("\n")
    }

    pub fn reminders_for_task(&self, task: &str) -> Vec<String> {
        let terms = tokenize(task);
        if terms.is_empty() || self.failures.is_empty() {
            return Vec::new();
        }

        let mut scored: Vec<(String, f32)> = self
            .failures
            .iter()
            .filter_map(|failure| {
                let searchable = format!(
                    "{} {} {}",
                    failure.command.to_lowercase(),
                    failure.signature.to_lowercase(),
                    failure.snippet.to_lowercase()
                );
                let matches = terms
                    .iter()
                    .filter(|term| searchable.contains(*term))
                    .count();
                if matches == 0 {
                    return None;
                }

                let score = matches as f32 * (1.0 + failure.occurrences as f32 * 0.1);
                Some((
                    format!(
                        "[remember] recent client failure: {} -> {}",
                        sanitize_one_line(&failure.command),
                        truncate_chars(&sanitize_one_line(&failure.signature), 90),
                    ),
                    score,
                ))
            })
            .collect();

        scored.sort_by(|a, b| {
            b.1.partial_cmp(&a.1)
                .unwrap_or(std::cmp::Ordering::Equal)
                .then_with(|| a.0.cmp(&b.0))
        });
        scored
            .into_iter()
            .take(REMINDER_LIMIT)
            .map(|(line, _)| line)
            .collect()
    }

    pub fn format_injection_block(&self) -> String {
        if self.failures.is_empty() {
            return String::new();
        }

        let mut lines = vec!["--- CLIENT FAILURE MEMORY (recent failure patterns) ---".to_string()];
        for failure in self.failures.iter().take(5) {
            lines.push(format!(
                "- {} | cmd: {} | seen {}x | last {}",
                crate::core::sanitize::neutralize_metadata(&failure.signature),
                crate::core::sanitize::neutralize_metadata(&truncate_chars(&failure.command, 70)),
                failure.occurrences,
                format_age(failure.last_seen),
            ));
        }
        lines.push("---".to_string());
        crate::core::sanitize::fence_content("client_bug_memory", &lines.join("\n"))
    }
}

pub fn record_shell_failure(project_root: &str, command: &str, exit_code: i32, output: &str) {
    record_failure(
        project_root,
        command,
        exit_code,
        output,
        BugMemorySource::Shell,
    );
}

pub fn record_mcp_shell_failure(project_root: &str, command: &str, exit_code: i32, output: &str) {
    record_failure(
        project_root,
        command,
        exit_code,
        output,
        BugMemorySource::McpShell,
    );
}

fn record_failure(
    project_root: &str,
    command: &str,
    exit_code: i32,
    output: &str,
    source: BugMemorySource,
) {
    if project_root.trim().is_empty() || exit_code == 0 {
        return;
    }

    let mut store = BugMemoryStore::load(project_root);
    store.record_failure(command, exit_code, output, source);
    let _ = store.save(project_root);
}

fn bug_memory_path(project_hash: &str) -> PathBuf {
    crate::core::data_dir::nebu_ctx_data_dir()
        .unwrap_or_else(|_| PathBuf::from("."))
        .join("knowledge")
        .join(project_hash)
        .join("bug-memory.json")
}

fn legacy_gotcha_path(project_hash: &str) -> PathBuf {
    crate::core::data_dir::nebu_ctx_data_dir()
        .unwrap_or_else(|_| PathBuf::from("."))
        .join("knowledge")
        .join(project_hash)
        .join("gotchas.json")
}

fn failure_id(command: &str, signature: &str) -> u64 {
    let mut hasher = DefaultHasher::new();
    command_base(command).hash(&mut hasher);
    signature.hash(&mut hasher);
    hasher.finish()
}

fn command_base(command: &str) -> String {
    let parts: Vec<&str> = command.split_whitespace().take(2).collect();
    parts.join(" ")
}

fn build_snippet(output: &str) -> String {
    sanitize_one_line(&truncate_chars(output.trim(), 200))
}

fn normalize_signature(output: &str) -> String {
    let preferred = output
        .lines()
        .map(str::trim)
        .find(|line| {
            let lower = line.to_ascii_lowercase();
            !line.is_empty()
                && (lower.contains("error")
                    || lower.contains("failed")
                    || lower.contains("panic")
                    || lower.contains("exception")
                    || lower.contains("traceback"))
        })
        .or_else(|| output.lines().map(str::trim).find(|line| !line.is_empty()))
        .unwrap_or_default();

    let mut signature = preferred.to_string();
    signature = regex_replace(&signature, r"(/[A-Za-z0-9._-]+)+", "<path>");
    signature = regex_replace(&signature, r"[A-Z]:\\[^\s:]+", "<path>");
    signature = regex_replace(&signature, r":\d+:\d+", ":_:_");
    signature = regex_replace(&signature, r"\b\d+\b", "#");
    signature = regex_replace(&signature, r"\s+", " ");
    truncate_chars(signature.trim(), 160)
}

fn regex_replace(text: &str, pattern: &str, replacement: &str) -> String {
    match regex::Regex::new(pattern) {
        Ok(re) => re.replace_all(text, replacement).to_string(),
        Err(_) => text.to_string(),
    }
}

fn tokenize(s: &str) -> Vec<String> {
    let mut out = Vec::new();
    let mut cur = String::new();
    for ch in s.chars() {
        if ch.is_ascii_alphanumeric() {
            cur.push(ch.to_ascii_lowercase());
        } else if !cur.is_empty() {
            if cur.len() >= 3 {
                out.push(cur.clone());
            }
            cur.clear();
        }
    }
    if !cur.is_empty() && cur.len() >= 3 {
        out.push(cur);
    }
    out.sort();
    out.dedup();
    out
}

fn sanitize_one_line(s: &str) -> String {
    let mut t = s.replace(['\n', '\r'], " ");
    t = t.replace('`', "");
    while t.contains("  ") {
        t = t.replace("  ", " ");
    }
    t.trim().to_string()
}

fn truncate_chars<S: AsRef<str>>(s: S, max: usize) -> String {
    let s = s.as_ref();
    if s.chars().count() <= max {
        return s.to_string();
    }
    let mut out = String::new();
    for (i, ch) in s.chars().enumerate() {
        if i + 1 >= max {
            break;
        }
        out.push(ch);
    }
    out.push('…');
    out
}

fn format_age(dt: DateTime<Utc>) -> String {
    let diff = Utc::now() - dt;
    let hours = diff.num_hours();
    if hours < 1 {
        return format!("{}m ago", diff.num_minutes().max(1));
    }
    if hours < 24 {
        return format!("{}h ago", hours);
    }
    format!("{}d ago", diff.num_days())
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn records_and_aggregates_failures() {
        let mut store = BugMemoryStore::new("test");
        store.record_failure(
            "cargo test",
            101,
            "error[E0502]: cannot borrow self as mutable",
            BugMemorySource::Shell,
        );
        store.record_failure(
            "cargo test --lib",
            101,
            "error[E0502]: cannot borrow self as mutable",
            BugMemorySource::Shell,
        );

        assert_eq!(store.failures.len(), 1);
        assert_eq!(store.failures[0].occurrences, 2);
        assert_eq!(store.stats.total_failures, 2);
    }

    #[test]
    fn builds_task_reminders_from_failures() {
        let mut store = BugMemoryStore::new("test");
        store.record_failure(
            "cargo test",
            101,
            "error[E0502]: cannot borrow self as mutable",
            BugMemorySource::Shell,
        );

        let reminders = store.reminders_for_task("fix cargo borrow error in tests");
        assert!(!reminders.is_empty());
    }

    #[test]
    fn injection_block_hidden_when_empty() {
        let store = BugMemoryStore::new("test");
        assert!(store.format_injection_block().is_empty());
    }
}
