use chrono::{DateTime, Utc};
use serde::{Deserialize, Serialize};
use std::collections::HashSet;
use std::path::PathBuf;

const MAX_EVENTS_PER_PROJECT: usize = 500;

#[derive(Debug, Clone, Serialize, Deserialize, PartialEq, Eq)]
#[serde(rename_all = "snake_case")]
pub enum LifecycleEventKind {
    SessionStart,
    UserTurn,
    AssistantTurn,
    ToolActivity,
    PreCompact,
    IdleFlush,
    SessionStop,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct JournalEvent {
    pub timestamp: DateTime<Utc>,
    pub session_id: String,
    pub source: String,
    pub kind: LifecycleEventKind,
    pub text: String,
    pub tool: Option<String>,
    pub command: Option<String>,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct BrainFactCandidate {
    pub key: String,
    pub value: String,
    pub kind: String,
    pub category: String,
    pub source_type: String,
    pub source_scope: String,
    pub promotion_identity: String,
    pub logical_key: String,
    pub confidence: f32,
    pub evidence: String,
}

#[derive(Debug, Default, Clone)]
pub struct FlushOutcome {
    pub journal_events: usize,
    pub derived_facts: usize,
}

pub fn append_event(project_root: &str, mut event: JournalEvent) -> Result<(), String> {
    if project_root.trim().is_empty() {
        return Ok(());
    }

    if event.timestamp == DateTime::<Utc>::default() {
        event.timestamp = Utc::now();
    }

    let mut events = load_events(project_root)?;
    events.push(event);
    if events.len() > MAX_EVENTS_PER_PROJECT {
        events.drain(0..events.len() - MAX_EVENTS_PER_PROJECT);
    }
    save_events(project_root, &events)
}

pub fn record_user_turn(
    project_root: &str,
    session_id: Option<&str>,
    source: &str,
    text: &str,
) -> Result<(), String> {
    append_event(
        project_root,
        JournalEvent {
            timestamp: Utc::now(),
            session_id: session_id.unwrap_or("sessionless").to_string(),
            source: source.to_string(),
            kind: LifecycleEventKind::UserTurn,
            text: truncate_text(text, 4000),
            tool: None,
            command: None,
        },
    )
}

pub fn record_assistant_turn(
    project_root: &str,
    session_id: Option<&str>,
    source: &str,
    text: &str,
) -> Result<(), String> {
    append_event(
        project_root,
        JournalEvent {
            timestamp: Utc::now(),
            session_id: session_id.unwrap_or("sessionless").to_string(),
            source: source.to_string(),
            kind: LifecycleEventKind::AssistantTurn,
            text: truncate_text(text, 4000),
            tool: None,
            command: None,
        },
    )
}

pub fn record_tool_activity(
    project_root: &str,
    session_id: Option<&str>,
    source: &str,
    tool: &str,
    command: Option<&str>,
    output_preview: Option<&str>,
) -> Result<(), String> {
    let mut text = String::new();
    if let Some(preview) = output_preview {
        text = truncate_text(preview, 600);
    }

    append_event(
        project_root,
        JournalEvent {
            timestamp: Utc::now(),
            session_id: session_id.unwrap_or("sessionless").to_string(),
            source: source.to_string(),
            kind: LifecycleEventKind::ToolActivity,
            text,
            tool: Some(tool.to_string()),
            command: command.map(|value| truncate_text(value, 400)),
        },
    )
}

pub fn record_lifecycle_marker(
    project_root: &str,
    session_id: Option<&str>,
    source: &str,
    kind: LifecycleEventKind,
    text: &str,
) -> Result<(), String> {
    append_event(
        project_root,
        JournalEvent {
            timestamp: Utc::now(),
            session_id: session_id.unwrap_or("sessionless").to_string(),
            source: source.to_string(),
            kind,
            text: truncate_text(text, 400),
            tool: None,
            command: None,
        },
    )
}

pub fn load_events(project_root: &str) -> Result<Vec<JournalEvent>, String> {
    let path = journal_file_path(project_root)?;
    if !path.exists() {
        return Ok(Vec::new());
    }

    let content = std::fs::read_to_string(&path).map_err(|e| e.to_string())?;
    let mut events = Vec::new();
    for line in content.lines() {
        let trimmed = line.trim();
        if trimmed.is_empty() {
            continue;
        }

        if let Ok(event) = serde_json::from_str::<JournalEvent>(trimmed) {
            events.push(event);
        }
    }

    Ok(events)
}

pub fn flush_to_brain(project_root: &str, source_type: &str) -> Result<FlushOutcome, String> {
    let events = load_events(project_root)?;
    let facts = derive_fact_candidates(project_root, source_type, &events);
    if !facts.is_empty() {
        crate::server_client::post_brain_facts_to_server(project_root, &facts);
    }

    Ok(FlushOutcome {
        journal_events: events.len(),
        derived_facts: facts.len(),
    })
}

pub fn derive_fact_candidates(
    project_root: &str,
    source_type: &str,
    events: &[JournalEvent],
) -> Vec<BrainFactCandidate> {
    let mut facts = Vec::new();
    let mut seen = HashSet::new();
    let session = crate::core::session::SessionState::load_latest_for_project_root(project_root);
    let session_scope = session
        .as_ref()
        .map(|value| value.id.clone())
        .unwrap_or_else(|| "sessionless".to_string());

    if let Some(session) = session {
        if let Some(task) = session.task {
            push_fact(
                &mut facts,
                &mut seen,
                build_fact_candidate(
                    source_type,
                    &session_scope,
                    "current-task",
                    &task.description,
                    "task",
                    "task",
                    0.95,
                    "derived from session task",
                ),
            );
        }

        for decision in session.decisions.iter().rev().take(5) {
            let key = format!("decision-{}", slug_key(&decision.summary, 48));
            push_fact(
                &mut facts,
                &mut seen,
                build_fact_candidate(
                    source_type,
                    &session_scope,
                    &key,
                    &decision.summary,
                    "decision",
                    "decision",
                    0.9,
                    "derived from session decision",
                ),
            );
        }

        for finding in session.findings.iter().rev().take(8) {
            let key = finding
                .file
                .as_ref()
                .map(|file| {
                    finding
                        .line
                        .map(|line| format!("finding-{}-{line}", slug_key(file, 36)))
                        .unwrap_or_else(|| format!("finding-{}", slug_key(file, 36)))
                })
                .unwrap_or_else(|| format!("finding-{}", slug_key(&finding.summary, 48)));
            push_fact(
                &mut facts,
                &mut seen,
                build_fact_candidate(
                    source_type,
                    &session_scope,
                    &key,
                    &finding.summary,
                    "fact",
                    "finding",
                    0.78,
                    "derived from session finding",
                ),
            );
        }
    }

    for event in events.iter().rev().take(40) {
        if event.text.is_empty() {
            continue;
        }

        for candidate in infer_facts_from_event(source_type, event) {
            push_fact(&mut facts, &mut seen, candidate);
        }
    }

    facts
}

fn infer_facts_from_event(source_type: &str, event: &JournalEvent) -> Vec<BrainFactCandidate> {
    let mut facts = Vec::new();
    let lowered = event.text.to_lowercase();

    if event.kind == LifecycleEventKind::UserTurn {
        if lowered.contains("opencode")
            && (lowered.contains("go to ide")
                || lowered.contains("primary ide")
                || lowered.contains("hoofd ide")
                || lowered.contains("nu onze")
                || lowered.contains("nu onze go to ide"))
        {
            facts.push(build_fact_candidate(
                source_type,
                &event.session_id,
                "primary-ide",
                "OpenCode",
                "preference",
                "workflow",
                0.98,
                "derived from user-stated primary IDE",
            ));
        }

        if lowered.contains("no shortcuts") || lowered.contains("geen shortcuts") {
            facts.push(build_fact_candidate(
                source_type,
                &event.session_id,
                "no-shortcuts",
                "No shortcuts; implement the full memory behavior.",
                "constraint",
                "quality",
                0.97,
                "derived from user quality constraint",
            ));
        }

        if lowered.contains("brain")
            && (lowered.contains("echte feiten")
                || lowered.contains("fact-only")
                || lowered.contains("geen log")
                || lowered.contains("not log")
                || lowered.contains("journal"))
        {
            facts.push(build_fact_candidate(
                source_type,
                &event.session_id,
                "brain-fact-only",
                "Brain stores derived facts only; raw journal remains local on the client.",
                "constraint",
                "memory",
                0.99,
                "derived from user memory-model constraint",
            ));
        }
    }

    facts
}

fn push_fact(
    facts: &mut Vec<BrainFactCandidate>,
    seen: &mut HashSet<String>,
    candidate: BrainFactCandidate,
) {
    if seen.insert(candidate.promotion_identity.clone()) {
        facts.push(candidate);
    }
}

fn build_fact_candidate(
    source_type: &str,
    source_scope: &str,
    key: &str,
    value: &str,
    kind: &str,
    category: &str,
    confidence: f32,
    evidence: &str,
) -> BrainFactCandidate {
    let logical_key = normalize_token(key);
    BrainFactCandidate {
        key: logical_key.clone(),
        value: value.trim().to_string(),
        kind: kind.to_string(),
        category: category.to_string(),
        source_type: source_type.to_string(),
        source_scope: source_scope.to_string(),
        promotion_identity: crate::server_client::deterministic_promotion_identity(
            source_type,
            source_scope,
            category,
            &logical_key,
        ),
        logical_key,
        confidence,
        evidence: evidence.to_string(),
    }
}

fn save_events(project_root: &str, events: &[JournalEvent]) -> Result<(), String> {
    let path = journal_file_path(project_root)?;
    if let Some(dir) = path.parent() {
        std::fs::create_dir_all(dir).map_err(|e| e.to_string())?;
    }

    let mut serialized = String::new();
    for event in events {
        let line = serde_json::to_string(event).map_err(|e| e.to_string())?;
        serialized.push_str(&line);
        serialized.push('\n');
    }

    std::fs::write(path, serialized).map_err(|e| e.to_string())
}

fn journal_file_path(project_root: &str) -> Result<PathBuf, String> {
    let project_hash = crate::core::project_hash::hash_project_root(project_root);
    Ok(crate::core::data_dir::nebu_ctx_data_dir()?
        .join("journal")
        .join(format!("{project_hash}.jsonl")))
}

fn truncate_text(text: &str, limit: usize) -> String {
    text.chars().take(limit).collect()
}

fn normalize_token(value: &str) -> String {
    let lowered = value.trim().to_lowercase();
    let mut normalized = lowered
        .chars()
        .map(|ch| if ch.is_ascii_alphanumeric() { ch } else { '-' })
        .collect::<String>();

    while normalized.contains("--") {
        normalized = normalized.replace("--", "-");
    }

    let trimmed = normalized.trim_matches('-');
    if trimmed.is_empty() {
        "unknown".to_string()
    } else {
        trimmed.to_string()
    }
}

fn slug_key(value: &str, limit: usize) -> String {
    let slug = normalize_token(value);
    if slug.len() <= limit {
        slug
    } else {
        slug[..limit].trim_matches('-').to_string()
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn journal_roundtrip_keeps_recent_events() {
        let _lock = crate::core::data_dir::test_env_lock();
        let tmp = tempfile::tempdir().unwrap();
        std::env::set_var("NEBU_CTX_DATA_DIR", tmp.path());
        let root = tmp.path().join("project");
        std::fs::create_dir_all(&root).unwrap();
        let root = root.to_string_lossy().to_string();

        record_user_turn(&root, Some("s1"), "test", "OpenCode is now our go to IDE").unwrap();
        let events = load_events(&root).unwrap();
        assert_eq!(events.len(), 1);
        assert_eq!(events[0].kind, LifecycleEventKind::UserTurn);
    }

    #[test]
    fn derive_facts_infers_primary_ide_and_brain_constraint() {
        let events = vec![JournalEvent {
            timestamp: Utc::now(),
            session_id: "s1".to_string(),
            source: "test".to_string(),
            kind: LifecycleEventKind::UserTurn,
            text: "OpenCode is now our go to IDE and brain should store echte feiten, geen log.".to_string(),
            tool: None,
            command: None,
        }];

        let facts = derive_fact_candidates("/tmp/project", "stop", &events);
        assert!(facts.iter().any(|fact| fact.key == "primary-ide" && fact.value == "OpenCode"));
        assert!(facts.iter().any(|fact| fact.key == "brain-fact-only"));
    }

    #[test]
    fn journal_retention_discards_oldest_events_over_limit() {
        let _lock = crate::core::data_dir::test_env_lock();
        let tmp = tempfile::tempdir().unwrap();
        std::env::set_var("NEBU_CTX_DATA_DIR", tmp.path());
        let root = tmp.path().join("project");
        std::fs::create_dir_all(&root).unwrap();
        let root = root.to_string_lossy().to_string();

        for index in 0..(MAX_EVENTS_PER_PROJECT + 5) {
            record_user_turn(&root, Some("s1"), "test", &format!("event-{index}")).unwrap();
        }

        let events = load_events(&root).unwrap();
        assert_eq!(events.len(), MAX_EVENTS_PER_PROJECT);
        assert_eq!(events.first().map(|event| event.text.as_str()), Some("event-5"));
        assert_eq!(
            events.last().map(|event| event.text.as_str()),
            Some("event-504")
        );
    }
}
