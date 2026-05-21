use std::sync::Arc;

use rmcp::model::*;
use serde_json::{Map, Value};

mod granular;
pub use granular::{granular_tool_defs, list_all_tool_defs, unified_tool_defs};

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
    "ctx_session",
    "ctx_knowledge",
];

pub fn lazy_tool_defs() -> Vec<Tool> {
    granular_tool_defs()
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
        return format!("No tools found matching '{query}'. Try broader terms like: graph, cost, session, search, compress, agent, workflow, gain.");
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
    list_all_tool_defs()
        .into_iter()
        .filter(|(name, _, _)| {
            matches!(
                *name,
                "ctx_read"
                    | "ctx_search"
                    | "ctx_tree"
                    | "ctx_shell"
                    | "ctx_session"
                    | "ctx_knowledge"
                    | "ctx_overview"
                    | "ctx_preload"
                    | "ctx_prefetch"
                    | "ctx_graph"
                    | "ctx_impact"
                    | "ctx_architecture"
                    | "ctx_symbol"
                    | "ctx_outline"
                    | "ctx_callers"
                    | "ctx_callees"
                    | "ctx_graph_diagram"
                    | "ctx_agent"
                    | "ctx_share"
                    | "ctx_task"
                    | "ctx_handoff"
                    | "ctx_workflow"
                    | "ctx_feedback"
                    | "ctx_wrapped"
                    | "ctx"
            )
        })
        .collect()
}

pub fn is_lazy_mode() -> bool {
    std::env::var("NEBU_CTX_LAZY_TOOLS").is_ok()
}
