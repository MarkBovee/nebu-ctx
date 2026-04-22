/// Brain consolidation service — extract memories from session context.
///
/// At session end, analyzes the session context and extracts:
/// - New memories (semantic, episodic, procedural)
/// - Open loops (unresolved tasks/questions)
/// - Checkpoints (session state for resume)
///
/// Also handles auto-promotion: short_term → long_term after N recalls.

use crate::core::store::ContextStore;
use super::{ConsolidationResult, MemoryLayer, MemoryType, BrainScoringWeights};
use super::scoring::should_promote;

/// Promotion threshold: how many recalls before short_term → long_term.
const DEFAULT_PROMOTION_THRESHOLD: i32 = 3;

/// Consolidate a session — extract memories and promote eligible ones.
///
/// This is a lightweight version that works without an LLM call.
/// A future version can use an LLM to extract structured memories from
/// free-form session text.
pub fn consolidate(
    store: &dyn ContextStore,
    brain_id: &str,
    memories: &[(&str, MemoryType)],  // (content, type) pairs extracted from session
    open_loops: &[&str],
    weights: &BrainScoringWeights,
) -> anyhow::Result<ConsolidationResult> {
    let mut memories_extracted = 0;
    let mut open_loops_extracted = 0;
    let mut promoted = 0;
    let mut duplicates_skipped = 0;

    // Store new memories
    for (content, mem_type) in memories {
        // Check for duplicates (simple content match)
        let existing = store.brain_recall(brain_id, content, "", 5)?;
        let is_dup = existing.iter().any(|m| m.content == *content);
        if is_dup {
            duplicates_skipped += 1;
            continue;
        }

        let initial_score = calculate_initial_score(mem_type, weights);
        store.brain_store(&crate::core::store::BrainMemory {
            id: None,
            brain_id: brain_id.to_string(),
            layer: MemoryLayer::ShortTerm.as_str().to_string(),
            memory_type: mem_type.as_str().to_string(),
            content: content.to_string(),
            embedding: None,
            composite_score: initial_score,
            recall_count: 0,
            weights_json: None,
            created_at: None,
        })?;
        memories_extracted += 1;
    }

    // Store open loops
    for desc in open_loops {
        store.open_loop_store(&crate::core::store::OpenLoop {
            id: None,
            brain_id: brain_id.to_string(),
            description: desc.to_string(),
            priority: 0.5,
            status: "open".to_string(),
            created_at: None,
        })?;
        open_loops_extracted += 1;
    }

    // Auto-promote eligible short_term memories
    let short_term = store.brain_recall(brain_id, "", "short_term", 100)?;
    for mem in &short_term {
        if should_promote(mem.recall_count, DEFAULT_PROMOTION_THRESHOLD) {
            if let Some(id) = mem.id {
                store.brain_promote(id, MemoryLayer::LongTerm.as_str())?;
                promoted += 1;
            }
        }
    }

    Ok(ConsolidationResult {
        memories_extracted,
        open_loops_extracted,
        promoted,
        duplicates_skipped,
    })
}

/// Calculate initial score based on memory type.
fn calculate_initial_score(mem_type: &MemoryType, weights: &BrainScoringWeights) -> f64 {
    match mem_type {
        MemoryType::Semantic => weights.importance * 0.8 + weights.confidence * 0.5,
        MemoryType::Episodic => weights.importance * 0.6 + weights.confidence * 0.4,
        MemoryType::Procedural => weights.importance * 0.7 + weights.confidence * 0.6,
    }
}
