use chrono::Utc;
use serde::Deserialize;

use crate::core::knowledge::{KnowledgeFact, ProjectKnowledge};

#[derive(Deserialize)]
struct ImportPayload {
    #[serde(default)]
    version: u32,
    #[serde(default)]
    source: String,
    #[serde(default)]
    memories: Vec<ImportMemory>,
}

#[derive(Deserialize)]
struct ImportMemory {
    #[serde(default)]
    key: String,
    #[serde(default)]
    value: String,
    #[serde(default)]
    category: String,
    #[serde(default)]
    tags: Vec<String>,
    #[serde(default)]
    #[allow(dead_code)]
    project: Option<String>,
    #[serde(default)]
    confidence: Option<f32>,
    #[serde(default)]
    memory_type: Option<String>,
    #[serde(default)]
    #[allow(dead_code)]
    created_at: Option<String>,
}

#[allow(clippy::too_many_arguments)]
pub fn handle(
    project_root: &str,
    action: &str,
    data: Option<&str>,
    dry_run: bool,
) -> String {
    match action {
        "import" => handle_import(project_root, data, dry_run),
        "validate" => handle_validate(data),
        "status" => handle_status(project_root),
        _ => "Unknown action. Use: import, validate, status".to_string(),
    }
}

fn handle_import(project_root: &str, data: Option<&str>, dry_run: bool) -> String {
    let raw = match data {
        Some(d) => d,
        None => return "ERROR: data parameter required for import action".to_string(),
    };

    let payload: ImportPayload = match serde_json::from_str(raw) {
        Ok(p) => p,
        Err(e) => return format!("ERROR: invalid JSON payload: {e}"),
    };

    if payload.memories.is_empty() {
        return "No memories to import (empty payload)".to_string();
    }

    if dry_run {
        return format!(
            "DRY RUN: Would import {} memories from {} (skipped dedup check)",
            payload.memories.len(),
            payload.source
        );
    }

    let mut knowledge = ProjectKnowledge::load_or_create(project_root);
    let existing_keys: std::collections::HashSet<String> =
        knowledge.facts.iter().map(|f| f.key.clone()).collect();

    let session_id = format!("import-{}-{}", payload.source, Utc::now().format("%Y%m%d%H%M%S"));
    let mut imported = 0usize;
    let mut skipped = 0usize;
    let mut failed = 0usize;

    for mem in &payload.memories {
        let key = if mem.key.is_empty() {
            failed += 1;
            continue;
        } else {
            mem.key.clone()
        };

        if existing_keys.contains(&key) {
            skipped += 1;
            continue;
        }

        let category = resolve_category(&mem.category, mem.memory_type.as_deref());
        let value = mem.value.clone();
        if value.is_empty() {
            failed += 1;
            continue;
        }

        let confidence = mem.confidence.unwrap_or(0.8);
        let tag_prefix = if mem.tags.is_empty() {
            String::new()
        } else {
            format!("[{}] ", mem.tags.join(","))
        };

        let fact = KnowledgeFact {
            category,
            key,
            value: format!("{tag_prefix}{value}"),
            source_session: session_id.clone(),
            confidence,
            created_at: Utc::now(),
            last_confirmed: Utc::now(),
            retrieval_count: 0,
            last_retrieved: None,
            valid_from: None,
            valid_until: None,
            supersedes: None,
            confirmation_count: 0,
        };

        knowledge.facts.push(fact);
        imported += 1;
    }

    if imported > 0 {
        if let Err(e) = knowledge.save() {
            return format!("ERROR: failed to save: {e} (imported {imported} before save)");
        }
    }

    format!(
        "Import complete from '{}': {} imported, {} skipped (existing key), {} failed (missing key/value)",
        payload.source, imported, skipped, failed
    )
}

fn handle_validate(data: Option<&str>) -> String {
    let raw = match data {
        Some(d) => d,
        None => return "ERROR: data parameter required for validate action".to_string(),
    };

    let payload: ImportPayload = match serde_json::from_str(raw) {
        Ok(p) => p,
        Err(e) => return format!("ERROR: invalid JSON payload: {e}"),
    };

    let mut by_category: std::collections::HashMap<String, usize> = std::collections::HashMap::new();
    let mut missing_keys = 0usize;
    let mut missing_values = 0usize;

    for mem in &payload.memories {
        let cat = resolve_category(&mem.category, mem.memory_type.as_deref());
        *by_category.entry(cat).or_insert(0) += 1;
        if mem.key.is_empty() {
            missing_keys += 1;
        }
        if mem.value.is_empty() {
            missing_values += 1;
        }
    }

    let mut lines = vec![
        format!("Payload valid (v{}, source: {})", payload.version, payload.source),
        format!("Total memories: {}", payload.memories.len()),
    ];

    for (cat, count) in by_category {
        lines.push(format!("  {cat}: {count}"));
    }

    if missing_keys > 0 || missing_values > 0 {
        lines.push(format!(
            "Warnings: {} missing keys, {} missing values",
            missing_keys, missing_values
        ));
    }

    lines.join("\n")
}

fn handle_status(project_root: &str) -> String {
    let knowledge = ProjectKnowledge::load_or_create(project_root);

    let imported: Vec<&KnowledgeFact> = knowledge
        .facts
        .iter()
        .filter(|f| f.source_session.starts_with("import-"))
        .collect();

    if imported.is_empty() {
        return "No imported memories found. Use 'import' action to load data.".to_string();
    }

    let sources: std::collections::HashSet<&str> = imported
        .iter()
        .map(|f| {
            f.source_session
                .strip_prefix("import-")
                .and_then(|s| s.split('-').next())
                .unwrap_or("unknown")
        })
        .collect();

    format!(
        "Imported memories: {} total from {} source(s) ({})",
        imported.len(),
        sources.len(),
        sources.into_iter().collect::<Vec<_>>().join(", ")
    )
}

fn resolve_category(explicit: &str, memory_type: Option<&str>) -> String {
    if !explicit.is_empty() {
        return explicit.to_string();
    }
    match memory_type.unwrap_or("") {
        "semantic" => "architecture".to_string(),
        "episodic" => "episodic".to_string(),
        "procedural" => "procedural".to_string(),
        other if !other.is_empty() => other.to_string(),
        _ => "imported".to_string(),
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn test_validate_valid_payload() {
        let json = r#"{"version":1,"source":"nebula-rag","memories":[{"key":"test-1","value":"test value","category":"architecture","confidence":0.8}]}"#;
        let result = handle("/tmp/test-project", "validate", Some(json), false);
        assert!(result.contains("Total memories: 1"));
        assert!(result.contains("architecture: 1"));
    }

    #[test]
    fn test_validate_empty_payload() {
        let json = r#"{"version":1,"source":"test","memories":[]}"#;
        let result = handle("/tmp/test-project", "validate", Some(json), false);
        assert!(result.contains("Total memories: 0"));
    }

    #[test]
    fn test_validate_invalid_json() {
        let result = handle("/tmp/test-project", "validate", Some("not json"), false);
        assert!(result.contains("ERROR"));
    }

    #[test]
    fn test_import_no_data() {
        let result = handle("/tmp/test-project", "import", None, false);
        assert!(result.contains("ERROR"));
    }

    #[test]
    fn test_import_dry_run() {
        let json = r#"{"version":1,"source":"test","memories":[{"key":"dry-run-test","value":"test","category":"test"}]}"#;
        let result = handle("/tmp/test-project", "import", Some(json), true);
        assert!(result.contains("DRY RUN"));
    }

    #[test]
    fn test_resolve_category() {
        assert_eq!(resolve_category("", Some("semantic")), "architecture");
        assert_eq!(resolve_category("", Some("episodic")), "episodic");
        assert_eq!(resolve_category("", Some("procedural")), "procedural");
        assert_eq!(resolve_category("api", None), "api");
        assert_eq!(resolve_category("", None), "imported");
    }

    #[test]
    fn test_unknown_action() {
        let result = handle("/tmp/test-project", "foobar", None, false);
        assert!(result.contains("Unknown action"));
    }
}
