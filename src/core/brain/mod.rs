/// Brain Memory System — persistent memory with scoring, consolidation, activation.
///
/// Ported from dot-claw's brain/memory system to Rust idioms.

pub mod scoring;
pub mod activation;
pub mod consolidation;

use serde::{Deserialize, Serialize};

// ── Enums ───────────────────────────────────────────────────

#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
pub enum MemoryLayer {
    ShortTerm,
    LongTerm,
}

impl MemoryLayer {
    pub fn as_str(&self) -> &'static str {
        match self {
            Self::ShortTerm => "short_term",
            Self::LongTerm => "long_term",
        }
    }

    pub fn from_str_lossy(s: &str) -> Self {
        match s {
            "long_term" => Self::LongTerm,
            _ => Self::ShortTerm,
        }
    }
}

#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
pub enum MemoryType {
    Episodic,
    Semantic,
    Procedural,
}

impl MemoryType {
    pub fn as_str(&self) -> &'static str {
        match self {
            Self::Episodic => "episodic",
            Self::Semantic => "semantic",
            Self::Procedural => "procedural",
        }
    }

    pub fn from_str_lossy(s: &str) -> Self {
        match s {
            "episodic" => Self::Episodic,
            "procedural" => Self::Procedural,
            _ => Self::Semantic,
        }
    }
}

// ── Scoring weights ─────────────────────────────────────────

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct BrainScoringWeights {
    pub semantic: f64,
    pub recency: f64,
    pub importance: f64,
    pub confidence: f64,
    pub open_loop: f64,
}

impl Default for BrainScoringWeights {
    fn default() -> Self {
        Self {
            semantic: 0.3,
            recency: 0.25,
            importance: 0.2,
            confidence: 0.15,
            open_loop: 0.1,
        }
    }
}

// ── Activation packet ───────────────────────────────────────

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct ActivationPacket {
    pub memories: Vec<ActivatedMemory>,
    pub open_loops: Vec<String>,
    pub checkpoint: Option<String>,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct ActivatedMemory {
    pub content: String,
    pub memory_type: String,
    pub layer: String,
    pub score: f64,
    pub recall_count: i32,
}

// ── Consolidation result ────────────────────────────────────

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct ConsolidationResult {
    pub memories_extracted: usize,
    pub open_loops_extracted: usize,
    pub promoted: usize,
    pub duplicates_skipped: usize,
}
