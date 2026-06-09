use std::collections::HashSet;
use std::sync::Mutex;
use std::time::{Duration, Instant};

use chrono::Utc;
use serde::Serialize;

use super::knowledge::{KnowledgeFact, ProjectKnowledge};
use super::sanitize::telemetry_command_preview;

const DEFAULT_THRESHOLD: f32 = 0.30;
const DEFAULT_MAX_SUGGESTIONS: usize = 3;
const DEFAULT_COOLDOWN: Duration = Duration::from_secs(30);
const MAX_CONTEXT_BYTES: usize = 1024;

/// Mode controlling whether contextual surfacing runs at all.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum SurfacingMode {
    Off,
    On,
}

impl SurfacingMode {
    pub fn from_env() -> Self {
        match std::env::var("NEBU_CTX_CONTEXTUAL_SURFACING") {
            Ok(value) if value.trim().eq_ignore_ascii_case("off") => Self::Off,
            _ => Self::On,
        }
    }
}

impl Default for SurfacingMode {
    fn default() -> Self {
        Self::On
    }
}

/// Configuration that gates how aggressive contextual surfacing is.
#[derive(Debug, Clone)]
pub struct SurfacingConfig {
    pub mode: SurfacingMode,
    pub threshold: f32,
    pub max_suggestions: usize,
    pub cooldown: Duration,
}

impl Default for SurfacingConfig {
    fn default() -> Self {
        Self {
            mode: SurfacingMode::On,
            threshold: DEFAULT_THRESHOLD,
            max_suggestions: DEFAULT_MAX_SUGGESTIONS,
            cooldown: DEFAULT_COOLDOWN,
        }
    }
}

impl SurfacingConfig {
    pub fn from_env() -> Self {
        let mut config = Self::default();
        config.mode = SurfacingMode::from_env();
        if let Ok(value) = std::env::var("NEBU_CTX_CONTEXTUAL_THRESHOLD") {
            if let Ok(parsed) = value.trim().parse::<f32>() {
                if (0.0..=1.0).contains(&parsed) {
                    config.threshold = parsed;
                }
            }
        }
        if let Ok(value) = std::env::var("NEBU_CTX_CONTEXTUAL_MAX") {
            if let Ok(parsed) = value.trim().parse::<usize>() {
                if parsed > 0 {
                    config.max_suggestions = parsed.min(10);
                }
            }
        }
        if let Ok(value) = std::env::var("NEBU_CTX_CONTEXTUAL_COOLDOWN_S") {
            if let Ok(parsed) = value.trim().parse::<u64>() {
                config.cooldown = Duration::from_secs(parsed);
            }
        }
        config
    }
}

/// A single ranked memory suggestion ready for display.
#[derive(Debug, Clone, Serialize)]
pub struct ContextualSuggestion {
    pub key: String,
    pub category: String,
    pub preview: String,
    pub relevance: f32,
    pub confidence: f32,
    pub reason: String,
    pub recall_command: String,
}

/// Stateful suggester that owns config, cache, and cooldown bookkeeping.
/// Hook handlers should construct one per call and use the shared `suggest` API.
pub struct ContextualSuggester {
    config: SurfacingConfig,
    cache: Mutex<Option<(String, Instant, Vec<ContextualSuggestion>)>>,
}

impl Default for ContextualSuggester {
    fn default() -> Self {
        Self::new(SurfacingConfig::default())
    }
}

impl ContextualSuggester {
    pub fn new(config: SurfacingConfig) -> Self {
        Self {
            config,
            cache: Mutex::new(None),
        }
    }

    pub fn from_env() -> Self {
        Self::new(SurfacingConfig::from_env())
    }

    pub fn config(&self) -> &SurfacingConfig {
        &self.config
    }

    /// Returns the top suggestions for `query` derived from `knowledge`.
    /// The query is intentionally generic so callers can use the same
    /// entry point for tool outputs, prompts, and shell commands.
    pub fn suggest(&self, query: &str, knowledge: &ProjectKnowledge) -> Vec<ContextualSuggestion> {
        if matches!(self.config.mode, SurfacingMode::Off) {
            return Vec::new();
        }
        let query = query.trim();
        if query.is_empty() {
            return Vec::new();
        }

        let cache_key = build_cache_key(query, knowledge);
        if let Some(cached) = self.cache_hit(&cache_key) {
            return cached;
        }

        let ranked = rank_facts(
            query,
            knowledge,
            self.config.threshold,
            self.config.max_suggestions,
        );
        if let Ok(mut guard) = self.cache.lock() {
            *guard = Some((
                cache_key,
                Instant::now() + self.config.cooldown,
                ranked.clone(),
            ));
        }
        ranked
    }

    fn cache_hit(&self, key: &str) -> Option<Vec<ContextualSuggestion>> {
        let guard = self.cache.lock().ok()?;
        let (stored_key, expires_at, suggestions) = guard.as_ref()?;
        if stored_key != key {
            return None;
        }
        if Instant::now() >= *expires_at {
            return None;
        }
        Some(suggestions.clone())
    }
}

fn build_cache_key(query: &str, knowledge: &ProjectKnowledge) -> String {
    use std::fmt::Write;
    let mut key = String::with_capacity(query.len() + 32);
    key.push_str(&query.len().to_string());
    key.push('|');
    key.push_str(&knowledge.facts.len().to_string());
    key.push('|');
    let _ = write!(&mut key, "{}", query);
    key
}

fn rank_facts(
    query: &str,
    knowledge: &ProjectKnowledge,
    threshold: f32,
    max_suggestions: usize,
) -> Vec<ContextualSuggestion> {
    let query_tokens = tokenize(query);
    if query_tokens.is_empty() {
        return Vec::new();
    }

    let now = Utc::now();
    let mut scored: Vec<(f32, ContextualSuggestion)> = Vec::new();
    for fact in &knowledge.facts {
        if !is_current(fact) {
            continue;
        }
        let score = score_fact(fact, &query_tokens, query, now);
        if score < threshold {
            continue;
        }
        scored.push((score, to_suggestion(fact, score, query)));
    }
    scored.sort_by(|a, b| b.0.partial_cmp(&a.0).unwrap_or(std::cmp::Ordering::Equal));
    scored
        .into_iter()
        .take(max_suggestions.max(1))
        .map(|(_, s)| s)
        .collect()
}

fn is_current(fact: &KnowledgeFact) -> bool {
    fact.valid_until.is_none()
}

fn tokenize(input: &str) -> HashSet<String> {
    input
        .split(|ch: char| !ch.is_alphanumeric())
        .filter(|s| !s.is_empty())
        .map(|s| s.to_ascii_lowercase())
        .filter(|s| s.len() >= 3)
        .collect()
}

fn score_fact(
    fact: &KnowledgeFact,
    query_tokens: &HashSet<String>,
    query: &str,
    now: chrono::DateTime<Utc>,
) -> f32 {
    let fact_text = format!("{} {} {}", fact.category, fact.key, fact.value).to_ascii_lowercase();
    let fact_tokens: HashSet<String> = fact_text
        .split(|ch: char| !ch.is_alphanumeric())
        .filter(|s| !s.is_empty())
        .map(|s| s.to_string())
        .collect();

    if fact_tokens.is_empty() {
        return 0.0;
    }

    let exact_hits = query_tokens
        .iter()
        .filter(|token| fact_tokens.contains(*token))
        .count();
    let partial_hits = query_tokens
        .iter()
        .filter(|token| fact_text.contains(token.as_str()))
        .count();
    let phrase_hit =
        (!query.trim().is_empty() && fact_text.contains(&query.to_ascii_lowercase())) as u8 as f32;

    if exact_hits == 0 && partial_hits == 0 && phrase_hit == 0.0 {
        return 0.0;
    }

    let total = query_tokens.len().max(1) as f32;
    let coverage = exact_hits as f32 / total;
    let partial = partial_hits as f32 / total;
    let mut score = coverage * 0.55 + partial * 0.20 + phrase_hit * 0.25;
    score *= 0.6 + fact.confidence.clamp(0.0, 1.0) * 0.4;

    // Temporal boost: facts touched in the last 14 days get a small lift.
    let last_activity = fact.last_retrieved.unwrap_or(fact.last_confirmed);
    let age_days = (now - last_activity).num_days().max(0) as f32;
    let recency_boost = if age_days <= 14.0 {
        0.10 * (1.0 - age_days / 14.0)
    } else {
        0.0
    };
    score += recency_boost;
    score.clamp(0.0, 1.0)
}

fn to_suggestion(fact: &KnowledgeFact, score: f32, query: &str) -> ContextualSuggestion {
    let preview = build_preview(&fact.value, 80);
    let reason = match query.len() {
        0..=32 => "prompt keyword match",
        33..=120 => "context phrase overlap",
        _ => "context block match",
    };
    let recall_command = format!(
        "ctx(domain=\"memory\", action=\"recall\", query=\"{}\")",
        escape_for_command(&fact.key)
    );
    ContextualSuggestion {
        key: fact.key.clone(),
        category: fact.category.clone(),
        preview,
        relevance: (score * 100.0).round() / 100.0,
        confidence: fact.confidence,
        reason: reason.to_string(),
        recall_command,
    }
}

fn build_preview(value: &str, max: usize) -> String {
    let sanitized = telemetry_command_preview(value).unwrap_or_else(|| value.to_string());
    if sanitized.len() <= max {
        return sanitized;
    }
    let mut end = max;
    while !sanitized.is_char_boundary(end) && end > 0 {
        end -= 1;
    }
    format!("{}…", &sanitized[..end])
}

fn escape_for_command(input: &str) -> String {
    input.replace('"', "\\\"").replace('\n', " ")
}

/// Renders a `<context_suggestions>` XML block ready to embed inside
/// `<session_state>`. Returns an empty string when `suggestions` is empty
/// or the projected payload would exceed the byte budget.
pub fn render_suggestions_block(suggestions: &[ContextualSuggestion]) -> String {
    if suggestions.is_empty() {
        return String::new();
    }
    let mut lines: Vec<String> = Vec::with_capacity(suggestions.len());
    for s in suggestions {
        lines.push(format!(
            "- [{}] {} (rel={:.2}, conf={:.2}) — {}",
            s.category, s.key, s.relevance, s.confidence, s.preview
        ));
    }
    let body = lines.join("\n");
    let block = format!(
        "<context_suggestions reason=\"{}\">\n{}\n</context_suggestions>",
        "hook_surfacing", body
    );
    if block.len() <= MAX_CONTEXT_BYTES {
        return block;
    }
    // Drop the lowest-ranked suggestions until we fit.
    let mut kept = suggestions.to_vec();
    while kept.len() > 1 {
        kept.pop();
        let lines: Vec<String> = kept
            .iter()
            .map(|s| {
                format!(
                    "- [{}] {} (rel={:.2}, conf={:.2}) — {}",
                    s.category, s.key, s.relevance, s.confidence, s.preview
                )
            })
            .collect();
        let candidate = format!(
            "<context_suggestions reason=\"hook_surfacing\">\n{}\n</context_suggestions>",
            lines.join("\n")
        );
        if candidate.len() <= MAX_CONTEXT_BYTES {
            return candidate;
        }
    }
    String::new()
}

/// Builds the JSON payload Claude Code / Copilot CLI accept in
/// `additionalContext`. Returns `None` when there is nothing to surface.
pub fn render_additional_context_json(suggestions: &[ContextualSuggestion]) -> Option<String> {
    if suggestions.is_empty() {
        return None;
    }
    let mut lines: Vec<String> = Vec::with_capacity(suggestions.len());
    for s in suggestions {
        lines.push(format!(
            "- [{}] {} (rel={:.2}) → {}",
            s.category, s.key, s.relevance, s.preview
        ));
    }
    let body = lines.join("\n");
    if body.len() > MAX_CONTEXT_BYTES {
        return None;
    }
    let escaped = body
        .replace('\\', "\\\\")
        .replace('"', "\\\"")
        .replace('\n', "\\n");
    Some(format!(
        "{{\"additionalContext\":\"<context_suggestions>\\n{}\\n</context_suggestions>\"}}",
        escaped
    ))
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::core::knowledge::{KnowledgeFact, ProjectKnowledge};

    fn fact(category: &str, key: &str, value: &str, confidence: f32) -> KnowledgeFact {
        let now = Utc::now();
        KnowledgeFact {
            category: category.to_string(),
            key: key.to_string(),
            value: value.to_string(),
            source_session: "test".to_string(),
            confidence,
            created_at: now,
            last_confirmed: now,
            retrieval_count: 0,
            last_retrieved: None,
            valid_from: None,
            valid_until: None,
            supersedes: None,
            confirmation_count: 0,
        }
    }

    fn knowledge_with(facts: Vec<KnowledgeFact>) -> ProjectKnowledge {
        ProjectKnowledge {
            project_root: "/tmp".to_string(),
            project_hash: "hash".to_string(),
            facts,
            history: Vec::new(),
            updated_at: Utc::now(),
        }
    }

    #[test]
    fn suggest_returns_empty_when_mode_off() {
        let config = SurfacingConfig {
            mode: SurfacingMode::Off,
            ..SurfacingConfig::default()
        };
        let suggester = ContextualSuggester::new(config);
        let knowledge = knowledge_with(vec![fact("build", "pnpm-test", "use pnpm test", 0.9)]);
        let result = suggester.suggest("how do I run pnpm tests", &knowledge);
        assert!(result.is_empty());
    }

    #[test]
    fn suggest_routes_high_relevance_facts_to_top() {
        let suggester = ContextualSuggester::default();
        let knowledge = knowledge_with(vec![
            fact("build", "pnpm-test", "use pnpm test for unit tests", 0.95),
            fact("deploy", "k8s-context", "use staging cluster", 0.9),
        ]);
        let result = suggester.suggest("pnpm unit test command", &knowledge);
        assert!(!result.is_empty());
        assert_eq!(result[0].key, "pnpm-test");
        assert!(result[0].relevance >= result.iter().map(|s| s.relevance).fold(0.0, f32::min));
    }

    #[test]
    fn suggest_skips_expired_facts() {
        let suggester = ContextualSuggester::default();
        let now = Utc::now();
        let mut expired = fact("build", "old-build", "legacy make build", 0.95);
        expired.valid_until = Some(now - chrono::Duration::days(1));
        let knowledge = knowledge_with(vec![
            expired,
            fact("build", "pnpm-test", "pnpm test runner", 0.95),
        ]);
        let result = suggester.suggest("how do I run pnpm tests", &knowledge);
        assert!(result.iter().all(|s| s.key != "old-build"));
    }

    #[test]
    fn suggest_caches_results_within_cooldown() {
        let suggester = ContextualSuggester::new(SurfacingConfig {
            cooldown: Duration::from_secs(60),
            ..SurfacingConfig::default()
        });
        let knowledge = knowledge_with(vec![fact("build", "pnpm-test", "pnpm test runner", 0.9)]);
        let first = suggester.suggest("pnpm tests", &knowledge);
        let second = suggester.suggest("pnpm tests", &knowledge);
        assert_eq!(first.len(), second.len());
    }

    #[test]
    fn suggest_respects_threshold() {
        let suggester = ContextualSuggester::new(SurfacingConfig {
            threshold: 0.99,
            ..SurfacingConfig::default()
        });
        let knowledge = knowledge_with(vec![fact("misc", "k1", "low overlap content", 0.5)]);
        let result = suggester.suggest("completely unrelated prompt", &knowledge);
        assert!(result.is_empty());
    }

    #[test]
    fn suggest_respects_max_suggestions() {
        let suggester = ContextualSuggester::new(SurfacingConfig {
            max_suggestions: 1,
            ..SurfacingConfig::default()
        });
        let knowledge = knowledge_with(vec![
            fact("build", "pnpm-test", "pnpm test runner", 0.95),
            fact("build", "pnpm-lint", "pnpm lint runner", 0.95),
        ]);
        let result = suggester.suggest("pnpm test runner", &knowledge);
        assert_eq!(result.len(), 1);
    }

    #[test]
    fn render_suggestions_block_truncates_to_byte_budget() {
        let suggestions: Vec<ContextualSuggestion> = (0..50)
            .map(|i| ContextualSuggestion {
                key: format!("k-{i}"),
                category: "cat".to_string(),
                preview: "x".repeat(80),
                relevance: 0.5,
                confidence: 0.5,
                reason: "test".to_string(),
                recall_command: "ctx".to_string(),
            })
            .collect();
        let block = render_suggestions_block(&suggestions);
        assert!(block.len() <= MAX_CONTEXT_BYTES);
    }

    #[test]
    fn render_additional_context_json_emits_escaped_payload() {
        let suggestions = vec![ContextualSuggestion {
            key: "k".to_string(),
            category: "build".to_string(),
            preview: "preview".to_string(),
            relevance: 0.5,
            confidence: 0.5,
            reason: "test".to_string(),
            recall_command: "ctx".to_string(),
        }];
        let json = render_additional_context_json(&suggestions).unwrap();
        assert!(json.starts_with("{\"additionalContext\":\""));
        assert!(json.ends_with("\"}"));
    }

    #[test]
    fn render_additional_context_json_returns_none_when_empty() {
        assert!(render_additional_context_json(&[]).is_none());
    }
}
