/// ctx_brain — MCP tool for brain memory operations.
///
/// Actions: store, recall, consolidate, activate, checkpoint, status

use serde_json;
use crate::core::store::ContextStore;

/// Get the brain store (Sqlite or Postgres based on NEBULA_STORE env).
/// Returns error string if unavailable.
fn get_store() -> Result<Box<dyn ContextStore>, String> {
    crate::core::store::open_store().map_err(|e| format!("{e}"))
}

pub fn handle(action: &str, args: &serde_json::Value) -> String {
    match action {
        "store" => handle_store(args),
        "recall" => handle_recall(args),
        "consolidate" => handle_consolidate(args),
        "activate" => handle_activate(args),
        "checkpoint" => handle_checkpoint(args),
        "status" => handle_status(args),
        _ => format!("Unknown brain action: {action}. Use: store, recall, consolidate, activate, checkpoint, status"),
    }
}

fn handle_store(args: &serde_json::Value) -> String {
    let content = match args.get("content").and_then(|v| v.as_str()) {
        Some(c) => c,
        None => return "ERROR: 'content' parameter required".to_string(),
    };
    let brain_id = args.get("brain_id").and_then(|v| v.as_str()).unwrap_or("default");
    let layer = args.get("layer").and_then(|v| v.as_str()).unwrap_or("short_term");
    let memory_type = args.get("memory_type").and_then(|v| v.as_str()).unwrap_or("semantic");
    let importance: f64 = args.get("importance").and_then(|v| v.as_f64()).unwrap_or(0.5);

    let store = match get_store() {
        Ok(s) => s,
        Err(e) => return format!("ERROR: {e}"),
    };

    let memory = crate::core::store::BrainMemory {
        id: None,
        brain_id: brain_id.to_string(),
        layer: layer.to_string(),
        memory_type: memory_type.to_string(),
        content: content.to_string(),
        embedding: None,
        composite_score: importance,
        recall_count: 0,
        weights_json: None,
        created_at: None,
    };

    match store.brain_store(&memory) {
        Ok(id) => format!("Stored memory #{id} in brain '{brain_id}' ({layer}/{memory_type})"),
        Err(e) => format!("ERROR: Failed to store: {e}"),
    }
}

fn handle_recall(args: &serde_json::Value) -> String {
    let brain_id = args.get("brain_id").and_then(|v| v.as_str()).unwrap_or("default");
    let query = args.get("query").and_then(|v| v.as_str()).unwrap_or("");
    let layer = args.get("layer").and_then(|v| v.as_str()).unwrap_or("");
    let limit = args.get("limit").and_then(|v| v.as_u64()).unwrap_or(10) as usize;

    let store = match get_store() {
        Ok(s) => s,
        Err(e) => return format!("ERROR: {e}"),
    };

    match store.brain_recall(brain_id, query, layer, limit) {
        Ok(memories) => {
            if memories.is_empty() {
                return format!("No memories found for brain '{brain_id}'");
            }
            let lines: Vec<String> = memories.iter().map(|m| {
                format!("[#{}] ({}/{}) score={:.2} recalls={} | {}",
                    m.id.unwrap_or(0), m.layer, m.memory_type,
                    m.composite_score, m.recall_count,
                    if m.content.len() > 200 { &m.content[..200] } else { &m.content })
            }).collect();
            format!("Recalled {} memories from brain '{}':\n{}", memories.len(), brain_id, lines.join("\n"))
        }
        Err(e) => format!("ERROR: Recall failed: {e}"),
    }
}

fn handle_consolidate(args: &serde_json::Value) -> String {
    let brain_id = args.get("brain_id").and_then(|v| v.as_str()).unwrap_or("default");

    // Extract memories from provided session text
    let session_text = match args.get("session_text").and_then(|v| v.as_str()) {
        Some(t) => t,
        None => return "ERROR: 'session_text' parameter required for consolidation".to_string(),
    };

    // Simple extraction: split by newlines, treat each non-empty line as a potential memory
    let memories: Vec<(&str, crate::core::brain::MemoryType)> = session_text
        .lines()
        .filter(|l| !l.trim().is_empty() && l.len() > 20)
        .take(20)
        .map(|l| (l, crate::core::brain::MemoryType::Semantic))
        .collect();

    // Extract open loops (lines containing "?")
    let open_loops: Vec<&str> = session_text
        .lines()
        .filter(|l| l.contains('?') && l.len() > 10)
        .take(5)
        .collect();

    let store = match get_store() {
        Ok(s) => s,
        Err(e) => return format!("ERROR: {e}"),
    };

    let weights = crate::core::brain::BrainScoringWeights::default();
    match crate::core::brain::consolidation::consolidate(&store, brain_id, &memories, &open_loops, &weights) {
        Ok(result) => format!(
            "Consolidation complete: {} memories stored, {} open loops, {} promoted, {} duplicates skipped",
            result.memories_extracted, result.open_loops_extracted, result.promoted, result.duplicates_skipped
        ),
        Err(e) => format!("ERROR: Consolidation failed: {e}"),
    }
}

fn handle_activate(args: &serde_json::Value) -> String {
    let brain_id = args.get("brain_id").and_then(|v| v.as_str()).unwrap_or("default");
    let max_memories = args.get("max_memories").and_then(|v| v.as_u64()).unwrap_or(10) as usize;

    let store = match get_store() {
        Ok(s) => s,
        Err(e) => return format!("ERROR: {e}"),
    };

    let weights = crate::core::brain::BrainScoringWeights::default();
    match crate::core::brain::activation::activate(&store, brain_id, &weights, max_memories) {
        Ok(packet) => {
            let mut lines = vec![format!("Activation packet for brain '{}':", brain_id)];

            if !packet.memories.is_empty() {
                lines.push(format!("\nMemories ({}):", packet.memories.len()));
                for m in &packet.memories {
                    lines.push(format!("  [{}/{}] score={:.2} | {}",
                        m.layer, m.memory_type, m.score,
                        if m.content.len() > 150 { &m.content[..150] } else { &m.content }));
                }
            }

            if !packet.open_loops.is_empty() {
                lines.push(format!("\nOpen loops ({}):", packet.open_loops.len()));
                for l in &packet.open_loops {
                    lines.push(format!("  - {}", l));
                }
            }

            if let Some(cp) = &packet.checkpoint {
                lines.push(format!("\nCheckpoint: {} bytes", cp.len()));
            }

            lines.join("\n")
        }
        Err(e) => format!("ERROR: Activation failed: {e}"),
    }
}

fn handle_checkpoint(args: &serde_json::Value) -> String {
    let brain_id = args.get("brain_id").and_then(|v| v.as_str()).unwrap_or("default");
    let content = match args.get("content").and_then(|v| v.as_str()) {
        Some(c) => c,
        None => return "ERROR: 'content' parameter required for checkpoint".to_string(),
    };
    let checkpoint_type = args.get("checkpoint_type").and_then(|v| v.as_str()).unwrap_or("manual");

    let store = match get_store() {
        Ok(s) => s,
        Err(e) => return format!("ERROR: {e}"),
    };

    // Get or create session
    let session_id = match store.brain_session_latest(brain_id) {
        Ok(Some(session)) => session.id.unwrap_or(0),
        _ => {
            match store.brain_session_create(brain_id) {
                Ok(id) => id,
                Err(e) => return format!("ERROR: Cannot create session: {e}"),
            }
        }
    };

    let checkpoint = crate::core::store::BrainCheckpoint {
        id: None,
        session_id,
        checkpoint_type: checkpoint_type.to_string(),
        content_json: content.to_string(),
        created_at: None,
    };

    match store.brain_checkpoint_store(&checkpoint) {
        Ok(id) => format!("Checkpoint #{id} saved for brain '{brain_id}' session #{session_id}"),
        Err(e) => format!("ERROR: Checkpoint failed: {e}"),
    }
}

fn handle_status(args: &serde_json::Value) -> String {
    let brain_id = args.get("brain_id").and_then(|v| v.as_str()).unwrap_or("default");

    let store = match get_store() {
        Ok(s) => s,
        Err(e) => return format!("ERROR: {e}"),
    };

    let short_term = store.brain_recall(brain_id, "", "short_term", 1000).unwrap_or_default();
    let long_term = store.brain_recall(brain_id, "", "long_term", 1000).unwrap_or_default();
    let open_loops = store.open_loop_list(brain_id, "open").unwrap_or_default();
    let sessions = store.brain_session_latest(brain_id).ok().flatten();

    format!(
        "Brain '{}' status:\n\
         Short-term memories: {}\n\
         Long-term memories: {}\n\
         Open loops: {}\n\
         Latest session: {}",
        brain_id,
        short_term.len(),
        long_term.len(),
        open_loops.len(),
        sessions.map(|s| format!("#{} ({})", s.id.unwrap_or(0), s.status)).unwrap_or("none".to_string()),
    )
}
