/// Brain scoring service — composite score with recency decay.
///
/// Ported from dot-claw's BrainScoringService.
/// Recency decay:
///   short_term: exp(-0.231 * days)
///   long_term:  exp(-0.0077 * days)

use super::{BrainScoringWeights, MemoryLayer};

/// Calculate composite score for a memory.
pub fn composite_score(
    semantic_sim: f64,
    age_days: f64,
    importance: f64,
    confidence: f64,
    is_open_loop: bool,
    layer: MemoryLayer,
    weights: &BrainScoringWeights,
) -> f64 {
    let recency = recency_decay(age_days, layer);
    let open_loop_boost = if is_open_loop { 1.0 } else { 0.0 };

    weights.semantic * semantic_sim
        + weights.recency * recency
        + weights.importance * importance
        + weights.confidence * confidence
        + weights.open_loop * open_loop_boost
}

/// Recency decay based on memory layer.
pub fn recency_decay(age_days: f64, layer: MemoryLayer) -> f64 {
    match layer {
        MemoryLayer::ShortTerm => (-0.231 * age_days).exp(),
        MemoryLayer::LongTerm => (-0.0077 * age_days).exp(),
    }
}

/// Should this memory be promoted from short_term to long_term?
pub fn should_promote(recall_count: i32, threshold: i32) -> bool {
    recall_count >= threshold
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn test_short_term_decay() {
        // Fresh memory: score ~1.0
        let score = recency_decay(0.0, MemoryLayer::ShortTerm);
        assert!((score - 1.0).abs() < 0.001);

        // 1 day old: ~0.79
        let score = recency_decay(1.0, MemoryLayer::ShortTerm);
        assert!((score - 0.79).abs() < 0.01);

        // 10 days old: ~0.10
        let score = recency_decay(10.0, MemoryLayer::ShortTerm);
        assert!(score < 0.15);
    }

    #[test]
    fn test_long_term_decay() {
        // Fresh: ~1.0
        let score = recency_decay(0.0, MemoryLayer::LongTerm);
        assert!((score - 1.0).abs() < 0.001);

        // 30 days old: ~0.79
        let score = recency_decay(30.0, MemoryLayer::LongTerm);
        assert!((score - 0.79).abs() < 0.01);

        // 365 days old: ~0.06
        let score = recency_decay(365.0, MemoryLayer::LongTerm);
        assert!(score < 0.1);
    }

    #[test]
    fn test_composite_score() {
        let weights = BrainScoringWeights::default();
        let score = composite_score(
            0.8,   // semantic similarity
            0.5,   // half day old
            0.7,   // importance
            0.9,   // confidence
            false, // not open loop
            MemoryLayer::ShortTerm,
            &weights,
        );
        assert!(score > 0.5 && score < 1.0);
    }

    #[test]
    fn test_should_promote() {
        assert!(should_promote(3, 3));
        assert!(!should_promote(2, 3));
    }
}
