/// Brain activation service — warm-up new sessions with relevant memories.
///
/// On session start, recalls top-N memories by composite score,
/// loads open loops, and loads the latest checkpoint.

use crate::core::store::{ContextStore, BrainMemory};
use super::{ActivationPacket, ActivatedMemory, BrainScoringWeights, MemoryLayer};
use super::scoring::recency_decay;

/// Activate a brain session — recall relevant context for warm-up.
pub fn activate(
    store: &dyn ContextStore,
    brain_id: &str,
    _weights: &BrainScoringWeights,
    max_memories: usize,
) -> anyhow::Result<ActivationPacket> {
    // Recall top memories
    let short_term = store.brain_recall(brain_id, "", "short_term", max_memories)?;
    let long_term = store.brain_recall(brain_id, "", "long_term", max_memories)?;

    // Merge and re-score with recency
    let mut all_memories: Vec<BrainMemory> = short_term;
    all_memories.extend(long_term);

    // Score each memory and sort
    let mut scored: Vec<(f64, BrainMemory)> = all_memories.into_iter().map(|m| {
        let age_days = parse_age_days(m.created_at.as_deref());
        let layer = MemoryLayer::from_str_lossy(&m.layer);
        let recency = recency_decay(age_days, layer);
        // Use existing composite_score as base, boost by recency
        let score = m.composite_score * 0.6 + recency * 0.4;
        (score, m)
    }).collect();
    scored.sort_by(|a, b| b.0.partial_cmp(&a.0).unwrap_or(std::cmp::Ordering::Equal));

    // Increment recall count for accessed memories
    let activated: Vec<ActivatedMemory> = scored.into_iter().take(max_memories).map(|(score, m)| {
        if let Some(id) = m.id {
            let _ = store.brain_increment_recall(id);
        }
        ActivatedMemory {
            content: m.content,
            memory_type: m.memory_type,
            layer: m.layer,
            score,
            recall_count: m.recall_count + 1,
        }
    }).collect();

    // Load open loops
    let open_loops = store.open_loop_list(brain_id, "open")?;
    let loop_descriptions: Vec<String> = open_loops.into_iter().map(|l| l.description).collect();

    // Load latest session checkpoint
    let checkpoint = store.brain_session_latest(brain_id)?
        .and_then(|s| s.checkpoint_json);

    Ok(ActivationPacket {
        memories: activated,
        open_loops: loop_descriptions,
        checkpoint,
    })
}

/// Parse an ISO date string into age in days (approximate).
fn parse_age_days(created_at: Option<&str>) -> f64 {
    let Some(date_str) = created_at else { return 0.0 };
    // Try parsing common formats: "2026-04-20T16:33:20" or "2026-04-20 16:33:20"
    let cleaned = date_str.split('.').next().unwrap_or(date_str);
    if let Ok(dt) = chrono::NaiveDateTime::parse_from_str(cleaned, "%Y-%m-%d %H:%M:%S")
        .or_else(|_| chrono::NaiveDateTime::parse_from_str(cleaned, "%Y-%m-%dT%H:%M:%S"))
    {
        let now = chrono::Utc::now().naive_utc();
        (now - dt).num_seconds() as f64 / 86400.0
    } else {
        0.0
    }
}
