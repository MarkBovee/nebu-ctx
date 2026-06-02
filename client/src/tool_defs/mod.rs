use std::sync::Arc;

use rmcp::model::*;
use serde_json::{Map, Value};

mod granular;
pub use granular::unified_tool_defs;

pub fn tool_def(name: &'static str, description: &'static str, schema_value: Value) -> Tool {
    let schema: Map<String, Value> = match schema_value {
        Value::Object(map) => map,
        _ => Map::new(),
    };
    Tool::new(name, description, Arc::new(schema))
}

const CORE_TOOL_NAMES: &[&str] = &[
    "ctx_read",
    "ctx_shell",
    "ctx_search",
    "ctx_tree",
    "ctx",
];

pub fn lazy_tool_defs() -> Vec<Tool> {
    unified_tool_defs()
        .into_iter()
        .filter(|t| CORE_TOOL_NAMES.contains(&t.name.as_ref()))
        .collect()
}

pub fn discover_tools(query: &str) -> String {
    let all = public_discoverable_tool_defs();
    let query_lower = query.to_lowercase();
    let matches: Vec<(&str, &str)> = all
        .iter()
        .filter(|(name, desc, _)| {
            name.to_lowercase().contains(&query_lower) || desc.to_lowercase().contains(&query_lower)
        })
        .map(|(name, desc, _)| (*name, *desc))
        .collect();

    if matches.is_empty() {
        return format!(
            "No tools found matching '{query}'. Try broader terms like: read, search, shell, tree, memory, graph, analytics."
        );
    }

    let mut out = format!("{} tools matching '{query}':\n", matches.len());
    for (name, desc) in &matches {
        let short = if desc.len() > 80 { &desc[..80] } else { desc };
        out.push_str(&format!("  {name} — {short}\n"));
    }
    out.push_str("\nCall the tool directly by name to use it.");
    out
}

pub fn public_discoverable_tool_defs() -> Vec<(&'static str, &'static str, Value)> {
    vec![
        (
            "ctx_read",
            "Read code and archived output. target=file|files|symbol|outline|archive. mode=auto|full|map|signatures|diff|aggressive|entropy|task|reference|lines:N-M.",
            serde_json::json!({
                "type": "object",
                "properties": {
                    "target": { "type": "string", "description": "file|files|symbol|outline|archive" },
                    "path": { "type": "string", "description": "File path" },
                    "paths": { "type": "array", "items": { "type": "string" }, "description": "Multiple file paths when target=files" },
                    "name": { "type": "string", "description": "Symbol name when target=symbol" },
                    "kind": { "type": "string", "description": "Symbol kind or outline filter" },
                    "mode": { "type": "string" },
                    "id": { "type": "string", "description": "Archive id when target=archive" },
                    "action": { "type": "string", "description": "Archive retrieval action when target=archive" },
                    "start_line": { "type": "integer" },
                    "fresh": { "type": "boolean" }
                },
                "required": []
            }),
        ),
        (
            "ctx_search",
            "Search code by regex or semantics. mode=regex|semantic.",
            serde_json::json!({
                "type": "object",
                "properties": {
                    "mode": { "type": "string", "description": "regex|semantic" },
                    "pattern": { "type": "string", "description": "Regex pattern when mode=regex" },
                    "query": { "type": "string", "description": "Natural language query when mode=semantic" },
                    "path": { "type": "string" },
                    "ext": { "type": "string" },
                    "top_k": { "type": "integer" },
                    "path_glob": { "type": "string" },
                    "ignore_gitignore": { "type": "boolean" }
                },
                "required": []
            }),
        ),
        (
            "ctx_tree",
            "Directory listing with file counts.",
            serde_json::json!({
                "type": "object",
                "properties": {
                    "path": { "type": "string" },
                    "depth": { "type": "integer" },
                    "show_hidden": { "type": "boolean" }
                }
            }),
        ),
        (
            "ctx_shell",
            "Run shell command (compressed output). Output includes active shell. raw=true skips compression. cwd sets working directory. shell_path overrides executable per call.",
            serde_json::json!({
                "type": "object",
                "properties": {
                    "command": { "type": "string", "description": "Shell command" },
                    "raw": { "type": "boolean", "description": "Skip compression for full output" },
                    "cwd": { "type": "string", "description": "Working directory (defaults to last cd or project root)" },
                    "shell_path": { "type": "string", "description": "Optional shell executable or path for this call. Legacy alias: shell." }
                },
                "required": ["command"]
            }),
        ),
        (
            "ctx",
            "High-level meta-tool. domain=memory|context|graph|analytics|agents|inspect with action selecting the operation inside that domain.",
            serde_json::json!({
                "type": "object",
                "properties": {
                    "domain": { "type": "string", "description": "memory|context|graph|analytics|agents|inspect" },
                    "action": { "type": "string" },
                    "view": { "type": "string" },
                    "path": { "type": "string" },
                    "paths": { "type": "array", "items": { "type": "string" } },
                    "query": { "type": "string" },
                    "pattern": { "type": "string" },
                    "value": { "type": "string" },
                    "category": { "type": "string" },
                    "key": { "type": "string" },
                    "to": { "type": "string" },
                    "spec": { "type": "string" },
                    "budget": { "type": "integer" },
                    "task": { "type": "string" },
                    "mode": { "type": "string" },
                    "text": { "type": "string" },
                    "message": { "type": "string" },
                    "session_id": { "type": "string" },
                    "period": { "type": "string" },
                    "format": { "type": "string" },
                    "agent_type": { "type": "string" },
                    "role": { "type": "string" },
                    "status": { "type": "string" },
                    "pattern_type": { "type": "string" },
                    "examples": { "type": "array", "items": { "type": "string" } },
                    "confidence": { "type": "number" },
                    "project_root": { "type": "string" },
                    "include_signatures": { "type": "boolean" },
                    "limit": { "type": "integer" },
                    "to_agent": { "type": "string" },
                    "task_id": { "type": "string" },
                    "agent_id": { "type": "string" },
                    "description": { "type": "string" },
                    "state": { "type": "string" },
                    "root": { "type": "string" },
                    "depth": { "type": "integer" },
                    "show_hidden": { "type": "boolean" }
                },
                "required": ["domain", "action"]
            }),
        ),
    ]
}

pub fn is_lazy_mode() -> bool {
    std::env::var("NEBU_CTX_LAZY_TOOLS").is_ok()
}
