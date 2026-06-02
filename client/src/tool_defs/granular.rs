use rmcp::model::Tool;
use serde_json::json;

use super::tool_def;

pub fn unified_tool_defs() -> Vec<Tool> {
    vec![
        tool_def(
            "ctx_read",
            "Read code and archived output. target=file|files|symbol|outline|archive. mode=auto|full|map|signatures|diff|aggressive|entropy|task|reference|lines:N-M.",
            json!({
                "type": "object",
                "properties": {
                    "target": {
                        "type": "string",
                        "description": "file|files|symbol|outline|archive"
                    },
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
        tool_def(
            "ctx_search",
            "Search code by regex or semantics. mode=regex|semantic.",
            json!({
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
        tool_def(
            "ctx_tree",
            "Directory listing with file counts.",
            json!({
                "type": "object",
                "properties": {
                    "path": { "type": "string" },
                    "depth": { "type": "integer" },
                    "show_hidden": { "type": "boolean" }
                }
            }),
        ),
        tool_def(
            "ctx_shell",
            "Run shell command (compressed output). Output includes active shell. raw=true skips compression. cwd sets working directory. shell_path overrides executable per call.",
            json!({
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
        tool_def(
            "ctx",
            "High-level meta-tool. domain=memory|context|graph|analytics|agents|inspect with action selecting the operation inside that domain.",
            json!({
                "type": "object",
                "properties": {
                    "domain": {
                        "type": "string",
                        "description": "memory|context|graph|analytics|agents|inspect"
                    },
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
