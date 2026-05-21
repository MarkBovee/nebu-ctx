use std::collections::{BTreeMap, BTreeSet};
use std::path::{Path, PathBuf};

use serde::{Deserialize, Serialize};

use crate::core::graph_index::ProjectIndex;
use crate::core::knowledge::ProjectKnowledge;
use crate::project_metadata;
use crate::server_client;

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct BootstrapFact {
    pub category: String,
    pub key: String,
    pub value: String,
    pub confidence: f32,
    pub evidence: String,
    pub source_type: String,
    pub source_scope: String,
    pub promotion_identity: String,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct BootstrapPreview {
    pub project_root: String,
    pub stack: Vec<String>,
    pub entrypoints: Vec<String>,
    pub tests: Vec<String>,
    pub infra: Vec<String>,
    pub modules: Vec<String>,
    pub workflow_signals: Vec<String>,
    pub facts: Vec<BootstrapFact>,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct BootstrapApplyReport {
    pub project_root: String,
    pub stored: usize,
    pub queued_server_promotions: bool,
    pub facts: Vec<BootstrapFact>,
}

pub fn resolve_project_root(path: Option<&str>) -> Result<String, String> {
    let candidate = match path {
        Some(value) => PathBuf::from(value),
        None => std::env::current_dir().map_err(|e| format!("cannot resolve current directory: {e}"))?,
    };

    candidate
        .canonicalize()
        .map(|path| path.to_string_lossy().to_string())
        .map_err(|e| format!("cannot resolve project root {}: {e}", candidate.display()))
}

pub fn build_preview(project_root: &str) -> Result<BootstrapPreview, String> {
    let root = Path::new(project_root);
    let metadata = project_metadata::build_project_metadata(root).map_err(|e| e.to_string())?;
    let index = Some(crate::core::graph_index::scan(project_root));

    let stack = detect_stack(&metadata.summary.markers, &metadata.summary.languages);
    let entrypoints = detect_entrypoints(root, index.as_ref());
    let tests = detect_tests(root, index.as_ref());
    let infra = detect_infra(&metadata.summary.markers, root);
    let modules = detect_modules(index.as_ref(), root);
    let workflow_signals = detect_workflow_signals(&metadata.summary.markers, root);
    let facts = build_fact_candidates(
        project_root,
        &stack,
        &entrypoints,
        &tests,
        &infra,
        &modules,
        &workflow_signals,
        &metadata.summary.markers,
    );

    Ok(BootstrapPreview {
        project_root: project_root.to_string(),
        stack,
        entrypoints,
        tests,
        infra,
        modules,
        workflow_signals,
        facts,
    })
}

pub fn apply_preview(
    project_root: &str,
    preview: &BootstrapPreview,
) -> Result<BootstrapApplyReport, String> {
    let session_id = crate::core::session::SessionState::load_latest_for_project_root(project_root)
        .map(|session| session.id)
        .unwrap_or_else(|| "project-bootstrap".to_string());

    let mut knowledge = ProjectKnowledge::load_or_create(project_root);
    for fact in &preview.facts {
        let _ = knowledge.remember(
            &fact.category,
            &fact.key,
            &fact.value,
            &session_id,
            fact.confidence,
        );
    }
    let _ = knowledge.run_memory_lifecycle();
    knowledge.save()?;

    let items: Vec<serde_json::Value> = preview
        .facts
        .iter()
        .map(|fact| {
            serde_json::json!({
                "category": fact.category,
                "key": fact.key,
                "value": fact.value,
                "confidence": fact.confidence,
                "source_type": fact.source_type,
                "source_scope": fact.source_scope,
                "promotion_identity": fact.promotion_identity,
            })
        })
        .collect();

    let mut queued_server_promotions = false;
    if !items.is_empty() {
        let ctx = crate::git_context::discover_project_context(Path::new(project_root));
        let mut args = serde_json::Map::new();
        args.insert("action".to_string(), serde_json::Value::String("promote".to_string()));
        args.insert("items".to_string(), serde_json::Value::Array(items));
        queued_server_promotions =
            server_client::queue_or_call_tool("ctx_knowledge", args, &ctx).is_ok();
    }

    Ok(BootstrapApplyReport {
        project_root: project_root.to_string(),
        stored: preview.facts.len(),
        queued_server_promotions,
        facts: preview.facts.clone(),
    })
}

pub fn format_preview(preview: &BootstrapPreview) -> String {
    let mut lines = vec![
        format!("PROJECT BOOTSTRAP PREVIEW  {}", preview.project_root),
        String::new(),
    ];

    push_section(&mut lines, "STACK", &preview.stack);
    push_section(&mut lines, "ENTRYPOINTS", &preview.entrypoints);
    push_section(&mut lines, "TESTS", &preview.tests);
    push_section(&mut lines, "INFRA", &preview.infra);
    push_section(&mut lines, "MODULES", &preview.modules);
    push_section(&mut lines, "WORKFLOW SIGNALS", &preview.workflow_signals);

    lines.push("CANDIDATE FACTS:".to_string());
    for fact in &preview.facts {
        lines.push(format!(
            "  - [{}/{}] {}  ({:.0}% | {})",
            fact.category,
            fact.key,
            fact.value,
            fact.confidence * 100.0,
            fact.evidence
        ));
    }
    lines.push(String::new());
    lines.push(
        "Preview only. Nothing stored yet. Apply with: nebu-ctx project-bootstrap apply"
            .to_string(),
    );

    lines.join("\n")
}

pub fn format_apply(report: &BootstrapApplyReport) -> String {
    format!(
        "Stored {} bootstrap facts for {}. Server promotion queued: {}.",
        report.stored,
        report.project_root,
        if report.queued_server_promotions {
            "yes"
        } else {
            "no"
        }
    )
}

fn push_section(lines: &mut Vec<String>, title: &str, items: &[String]) {
    lines.push(format!("{title}:"));
    if items.is_empty() {
        lines.push("  - none detected".to_string());
    } else {
        for item in items {
            lines.push(format!("  - {item}"));
        }
    }
    lines.push(String::new());
}

fn detect_stack(
    markers: &[String],
    languages: &[crate::models::ProjectLanguageStat],
) -> Vec<String> {
    let mut stack = Vec::new();
    let language_names: Vec<String> = languages
        .iter()
        .map(|language| format!("{}({})", language.language, language.file_count))
        .collect();
    if !language_names.is_empty() {
        stack.push(format!("languages: {}", language_names.join(", ")));
    }
    if markers.iter().any(|marker| marker == "Cargo.toml") {
        stack.push("build: cargo / rust".to_string());
    }
    if markers.iter().any(|marker| marker == "package.json") {
        stack.push("app: node package.json".to_string());
    }
    if markers
        .iter()
        .any(|marker| marker == "pyproject.toml" || marker == "requirements.txt")
    {
        stack.push("app: python project".to_string());
    }
    if markers.iter().any(|marker| marker.ends_with(".slnx")) {
        stack.push("app: .NET solution".to_string());
    }
    stack
}

fn detect_entrypoints(root: &Path, index: Option<&ProjectIndex>) -> Vec<String> {
    let mut entrypoints = BTreeSet::new();
    for rel in [
        "src/main.rs",
        "src/lib.rs",
        "src/index.ts",
        "src/main.ts",
        "src/main.tsx",
        "src/index.js",
        "src/main.py",
        "main.py",
        "Program.cs",
        "server/src/NebuCtx.Server.Host/Program.cs",
    ] {
        if root.join(rel).exists() {
            entrypoints.insert(rel.to_string());
        }
    }

    if let Some(index) = index {
        for file in index.files.values() {
            if file.exports.iter().any(|export| export == "main") {
                entrypoints.insert(relativize(root, &file.path));
            }
        }
    }

    entrypoints.into_iter().collect()
}

fn detect_tests(root: &Path, index: Option<&ProjectIndex>) -> Vec<String> {
    let mut tests = BTreeSet::new();
    for rel in ["tests", "server/tests", "client/tests", ".github/workflows"] {
        if root.join(rel).exists() {
            tests.insert(rel.to_string());
        }
    }
    if let Some(index) = index {
        for file in index.files.values() {
            let rel = relativize(root, &file.path);
            if rel.contains("test") || rel.contains("spec") {
                tests.insert(rel);
            }
        }
    }
    tests.into_iter().take(8).collect()
}

fn detect_infra(markers: &[String], root: &Path) -> Vec<String> {
    let mut infra = BTreeSet::new();
    for marker in markers {
        if matches!(marker.as_str(), "compose.yaml" | "docker-compose.yml") {
            infra.insert(format!("container: {marker}"));
        }
    }
    for rel in ["Dockerfile", "docker-entrypoint.sh", ".github/workflows", "homeassistant"] {
        if root.join(rel).exists() {
            infra.insert(rel.to_string());
        }
    }
    infra.into_iter().collect()
}

fn detect_modules(index: Option<&ProjectIndex>, root: &Path) -> Vec<String> {
    if let Some(index) = index {
        let mut by_dir = BTreeMap::<String, usize>::new();
        for file in index.files.values() {
            let rel = relativize(root, &file.path);
            let dir = Path::new(&rel)
                .parent()
                .map(|path| path.to_string_lossy().to_string())
                .unwrap_or_else(|| ".".to_string());
            *by_dir.entry(dir).or_default() += 1;
        }

        return by_dir
            .into_iter()
            .filter(|(dir, count)| dir != "." && *count >= 2)
            .map(|(dir, count)| format!("{dir} ({count} files)"))
            .take(8)
            .collect();
    }

    let mut modules = Vec::new();
    for rel in ["client/src", "server/src", "tests", "docs", "homeassistant"] {
        if root.join(rel).exists() {
            modules.push(rel.to_string());
        }
    }
    modules
}

fn detect_workflow_signals(markers: &[String], root: &Path) -> Vec<String> {
    let mut signals = BTreeSet::new();
    if markers.iter().any(|marker| marker == "Cargo.toml") {
        signals.insert("rust workflow present".to_string());
    }
    if markers.iter().any(|marker| marker.ends_with(".slnx")) {
        signals.insert("dotnet workflow present".to_string());
    }
    if root.join("tests").exists() {
        signals.insert("repo-level tests directory".to_string());
    }
    if root.join(".github/workflows").exists() {
        signals.insert("github actions configured".to_string());
    }
    if root.join("client/assets/skills").exists() {
        signals.insert("agent skill assets in repo".to_string());
    }
    signals.into_iter().collect()
}

fn build_fact_candidates(
    project_root: &str,
    stack: &[String],
    entrypoints: &[String],
    tests: &[String],
    infra: &[String],
    modules: &[String],
    workflow_signals: &[String],
    markers: &[String],
) -> Vec<BootstrapFact> {
    let source_scope = format!("project-bootstrap:{project_root}");
    let mut facts = Vec::new();

    if !stack.is_empty() {
        facts.push(make_fact(
            "architecture",
            "stack",
            &stack.join("; "),
            0.95,
            "derived from project markers and languages",
            &source_scope,
        ));
    }
    if !entrypoints.is_empty() {
        facts.push(make_fact(
            "architecture",
            "entrypoints",
            &entrypoints.join(", "),
            0.9,
            "derived from common entrypoint files and index exports",
            &source_scope,
        ));
    }
    if !tests.is_empty() {
        facts.push(make_fact(
            "testing",
            "test-surfaces",
            &tests.join(", "),
            0.85,
            "derived from test directories and indexed file names",
            &source_scope,
        ));
    }
    if !infra.is_empty() {
        facts.push(make_fact(
            "deployment",
            "infra-surfaces",
            &infra.join(", "),
            0.85,
            "derived from infrastructure markers",
            &source_scope,
        ));
    }
    if !modules.is_empty() {
        facts.push(make_fact(
            "architecture",
            "modules",
            &modules.join(", "),
            0.8,
            "derived from indexed directories and module roots",
            &source_scope,
        ));
    }
    if !workflow_signals.is_empty() {
        facts.push(make_fact(
            "conventions",
            "workflow-signals",
            &workflow_signals.join(", "),
            0.8,
            "derived from repository automation and workflow files",
            &source_scope,
        ));
    }
    if !markers.is_empty() {
        facts.push(make_fact(
            "conventions",
            "project-markers",
            &markers.join(", "),
            0.75,
            "derived from marker files at repository root",
            &source_scope,
        ));
    }

    facts
}

fn make_fact(
    category: &str,
    key: &str,
    value: &str,
    confidence: f32,
    evidence: &str,
    source_scope: &str,
) -> BootstrapFact {
    BootstrapFact {
        category: category.to_string(),
        key: key.to_string(),
        value: value.to_string(),
        confidence,
        evidence: evidence.to_string(),
        source_type: "project_bootstrap".to_string(),
        source_scope: source_scope.to_string(),
        promotion_identity: server_client::deterministic_promotion_identity(
            "project_bootstrap",
            source_scope,
            category,
            key,
        ),
    }
}

fn relativize(root: &Path, path: &str) -> String {
    let path = Path::new(path);
    path.strip_prefix(root)
        .map(|rel| rel.to_string_lossy().to_string())
        .unwrap_or_else(|_| path.to_string_lossy().to_string())
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn preview_detects_basic_stack_and_candidates() {
        let _lock = crate::core::data_dir::test_env_lock();
        let tmp = tempfile::tempdir().unwrap();
        let root = tmp.path();
        std::fs::write(root.join("Cargo.toml"), "[package]\nname='demo'\n").unwrap();
        std::fs::create_dir_all(root.join("src")).unwrap();
        std::fs::create_dir_all(root.join("tests")).unwrap();
        std::fs::write(root.join("src/main.rs"), "fn main() {}\n").unwrap();
        std::fs::write(root.join("Dockerfile"), "FROM scratch\n").unwrap();

        let preview = build_preview(&root.to_string_lossy()).unwrap();
        assert!(preview.stack.iter().any(|line| line.contains("cargo / rust")));
        assert!(preview.entrypoints.iter().any(|line| line == "src/main.rs"));
        assert!(preview.tests.iter().any(|line| line.contains("tests")));
        assert!(preview.infra.iter().any(|line| line.contains("Dockerfile")));
        assert!(preview.facts.iter().any(|fact| fact.key == "stack"));
    }

    #[test]
    fn apply_preview_stores_local_knowledge_and_queues_server_call() {
        let _lock = crate::core::data_dir::test_env_lock();
        let tmp = tempfile::tempdir().unwrap();
        std::env::set_var("NEBU_CTX_DATA_DIR", tmp.path().join("data"));
        std::env::set_var("NEBU_CTX_HOME", tmp.path().join("home"));
        let root = tmp.path().join("project");
        std::fs::create_dir_all(&root).unwrap();
        std::fs::write(root.join("Cargo.toml"), "[package]\nname='demo'\n").unwrap();

        let preview = BootstrapPreview {
            project_root: root.to_string_lossy().to_string(),
            stack: vec!["build: cargo / rust".to_string()],
            entrypoints: vec!["src/main.rs".to_string()],
            tests: vec![],
            infra: vec![],
            modules: vec![],
            workflow_signals: vec![],
            facts: vec![make_fact(
                "architecture",
                "stack",
                "build: cargo / rust",
                0.95,
                "derived from markers",
                &format!("project-bootstrap:{}", root.to_string_lossy()),
            )],
        };

        let report = apply_preview(&root.to_string_lossy(), &preview).unwrap();
        assert_eq!(report.stored, 1);

        let stored = ProjectKnowledge::load(&root.to_string_lossy()).unwrap();
        assert!(stored
            .facts
            .iter()
            .any(|fact| fact.key == "stack" && fact.value.contains("cargo")));

        let entry = crate::core::sync_outbox::load_entries()
            .unwrap()
            .into_iter()
            .find(|item| item.kind == crate::core::sync_outbox::OutboxOperationKind::ServerToolCall)
            .unwrap();
        assert_eq!(entry.payload["tool_name"], "ctx_knowledge");
        assert_eq!(entry.payload["arguments"]["action"], "promote");
    }

    #[test]
    fn preview_refreshes_stale_index_paths() {
        let _lock = crate::core::data_dir::test_env_lock();
        let tmp = tempfile::tempdir().unwrap();
        let data_dir = tmp.path().join("data");
        std::fs::create_dir_all(&data_dir).unwrap();
        std::env::set_var("NEBU_CTX_DATA_DIR", &data_dir);

        let root = tmp.path().join("project");
        std::fs::create_dir_all(root.join("client/assets/skills/project-bootstrap/scripts")).unwrap();
        std::fs::create_dir_all(root.join("skills/project-bootstrap/scripts")).unwrap();
        std::fs::create_dir_all(root.join("src")).unwrap();
        std::fs::write(root.join("Cargo.toml"), "[package]\nname='demo'\n").unwrap();
        std::fs::write(root.join("src/main.rs"), "fn main() {}\n").unwrap();
        std::fs::write(root.join("client/assets/skills/project-bootstrap/scripts/install.sh"), "#!/bin/sh\n").unwrap();
        std::fs::write(root.join("skills/project-bootstrap/scripts/install.sh"), "#!/bin/sh\n").unwrap();

        let project_root = root.to_string_lossy().to_string();
        let index_dir = crate::core::graph_index::ProjectIndex::index_dir(&project_root).unwrap();
        std::fs::create_dir_all(&index_dir).unwrap();
        let stale_index = serde_json::json!({
            "version": 6,
            "project_root": project_root,
            "last_scan": "now",
            "files": {
                "client/assets/skills/nebu-ctx/scripts/install.sh": {
                    "path": "client/assets/skills/nebu-ctx/scripts/install.sh",
                    "hash": "stale",
                    "language": "sh",
                    "line_count": 1,
                    "token_count": 1,
                    "exports": ["main"],
                    "summary": "stale"
                },
                "skills/nebu-ctx/scripts/install.sh": {
                    "path": "skills/nebu-ctx/scripts/install.sh",
                    "hash": "stale",
                    "language": "sh",
                    "line_count": 1,
                    "token_count": 1,
                    "exports": ["main"],
                    "summary": "stale"
                }
            },
            "edges": [],
            "symbols": {}
        });
        std::fs::write(index_dir.join("index.json"), serde_json::to_string(&stale_index).unwrap()).unwrap();

        let preview = build_preview(&root.to_string_lossy()).unwrap();
        let joined_entrypoints = preview.entrypoints.join("\n");
        assert!(joined_entrypoints.contains("src/main.rs"));
        assert!(!joined_entrypoints.contains("client/assets/skills/nebu-ctx/scripts/install.sh"));
        assert!(!joined_entrypoints.contains("skills/nebu-ctx/scripts/install.sh"));

        std::env::remove_var("NEBU_CTX_DATA_DIR");
    }
}
