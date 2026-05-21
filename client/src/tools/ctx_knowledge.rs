use chrono::Utc;
use serde_json::Value;

#[cfg(feature = "embeddings")]
use crate::core::embeddings::EmbeddingEngine;

use crate::core::knowledge::ProjectKnowledge;
use crate::core::session::SessionState;

#[allow(clippy::too_many_arguments)]
pub fn handle(
    project_root: &str,
    action: &str,
    category: Option<&str>,
    key: Option<&str>,
    value: Option<&str>,
    query: Option<&str>,
    session_id: &str,
    confidence: Option<f32>,
    mode: Option<&str>,
    raw_items: Option<&Value>,
) -> String {
    match action {
        "remember" => handle_remember(project_root, category, key, value, session_id, confidence),
        "recall" => handle_recall(project_root, category, query, session_id),
        "status" => handle_status(project_root),
        "remove" => handle_remove(project_root, category, key),
        "consolidate" => handle_consolidate(project_root),
        "promote" => handle_promote_batch(project_root, raw_items, session_id),
        "upkeep" => handle_upkeep(project_root),
        "triage" => handle_triage(project_root, mode),
        "timeline" => handle_timeline(project_root, category),
        "categories" => handle_categories(project_root),
        "search" => handle_search(project_root, query, session_id),
        "wakeup" => handle_wakeup(project_root),
        "embeddings_status" => handle_embeddings_status(project_root),
        "embeddings_reset" => handle_embeddings_reset(project_root),
        "embeddings_reindex" => handle_embeddings_reindex(project_root),
        _ => format!(
            "Unknown action: {action}. Use: remember, recall, status, remove, consolidate, promote, upkeep, triage, timeline, categories, search, wakeup, embeddings_status, embeddings_reset, embeddings_reindex"
        ),
    }
}

pub fn handle_promote_batch(
    project_root: &str,
    raw_items: Option<&Value>,
    session_id: &str,
) -> String {
    let Some(Value::Array(items)) = raw_items else {
        return "No promotion items supplied.".to_string();
    };

    let mut knowledge = ProjectKnowledge::load_or_create(project_root);
    let mut promoted = 0usize;
    let mut skipped = 0usize;

    for item in items {
        let Some(obj) = item.as_object() else {
            skipped += 1;
            continue;
        };

        let category = obj
            .get("category")
            .and_then(|v| v.as_str())
            .unwrap_or("")
            .trim();
        let key = obj.get("key").and_then(|v| v.as_str()).unwrap_or("").trim();
        let value = obj
            .get("value")
            .and_then(|v| v.as_str())
            .unwrap_or("")
            .trim();
        let confidence = obj
            .get("confidence")
            .and_then(|v| v.as_f64())
            .unwrap_or(0.8) as f32;

        if category.is_empty() || key.is_empty() || value.is_empty() {
            skipped += 1;
            continue;
        }

        let _ = knowledge.remember(category, key, value, session_id, confidence);
        promoted += 1;
    }

    let _ = knowledge.run_memory_lifecycle();
    let _ = knowledge.save();

    format!("Promoted {promoted} items into local knowledge ({skipped} skipped).")
}

#[cfg(feature = "embeddings")]
fn embeddings_auto_download_allowed() -> bool {
    std::env::var("NEBU_CTX_EMBEDDINGS_AUTO_DOWNLOAD")
        .ok()
        .map(|v| {
            matches!(
                v.trim().to_lowercase().as_str(),
                "1" | "true" | "yes" | "on"
            )
        })
        .unwrap_or(false)
}

#[cfg(feature = "embeddings")]
fn embedding_engine() -> Option<&'static EmbeddingEngine> {
    use std::sync::OnceLock;

    if !EmbeddingEngine::is_available() && !embeddings_auto_download_allowed() {
        return None;
    }

    static ENGINE: OnceLock<anyhow::Result<EmbeddingEngine>> = OnceLock::new();
    ENGINE
        .get_or_init(EmbeddingEngine::load_default)
        .as_ref()
        .ok()
}

fn handle_embeddings_status(project_root: &str) -> String {
    #[cfg(feature = "embeddings")]
    {
        let knowledge = ProjectKnowledge::load_or_create(project_root);
        let model_available = EmbeddingEngine::is_available();
        let auto = embeddings_auto_download_allowed();

        let entries = crate::core::knowledge_embedding::KnowledgeEmbeddingIndex::load(
            &knowledge.project_hash,
        )
        .map(|i| i.entries.len())
        .unwrap_or(0);

        let path = crate::core::data_dir::nebu_ctx_data_dir()
            .ok()
            .map(|d| {
                d.join("knowledge")
                    .join(&knowledge.project_hash)
                    .join("embeddings.json")
            })
            .map(|p| p.display().to_string())
            .unwrap_or_else(|| "<unknown>".to_string());

        format!(
            "Knowledge embeddings: model={}, auto_download={}, index_entries={}, path={path}",
            if model_available {
                "present"
            } else {
                "missing"
            },
            if auto { "on" } else { "off" },
            entries
        )
    }
    #[cfg(not(feature = "embeddings"))]
    {
        let _ = project_root;
        "ERR: embeddings feature not enabled".to_string()
    }
}

fn handle_embeddings_reset(project_root: &str) -> String {
    #[cfg(feature = "embeddings")]
    {
        let knowledge = ProjectKnowledge::load_or_create(project_root);
        match crate::core::knowledge_embedding::reset(&knowledge.project_hash) {
            Ok(()) => "Embeddings index reset.".to_string(),
            Err(e) => format!("Embeddings reset failed: {e}"),
        }
    }
    #[cfg(not(feature = "embeddings"))]
    {
        let _ = project_root;
        "ERR: embeddings feature not enabled".to_string()
    }
}

fn handle_embeddings_reindex(project_root: &str) -> String {
    #[cfg(feature = "embeddings")]
    {
        let knowledge = match ProjectKnowledge::load(project_root) {
            Some(k) => k,
            None => return "No knowledge stored for this project yet.".to_string(),
        };

        let engine = match embedding_engine() {
            Some(e) => e,
            None => {
                return "Embeddings model not available. Set NEBU_CTX_EMBEDDINGS_AUTO_DOWNLOAD=1 to allow auto-download, then re-run."
                    .to_string()
            }
        };

        let mut idx =
            crate::core::knowledge_embedding::KnowledgeEmbeddingIndex::new(&knowledge.project_hash);

        let mut facts: Vec<&crate::core::knowledge::KnowledgeFact> =
            knowledge.facts.iter().filter(|f| f.is_current()).collect();
        facts.sort_by(|a, b| {
            b.confidence
                .partial_cmp(&a.confidence)
                .unwrap_or(std::cmp::Ordering::Equal)
                .then_with(|| b.last_confirmed.cmp(&a.last_confirmed))
                .then_with(|| a.category.cmp(&b.category))
                .then_with(|| a.key.cmp(&b.key))
        });

        let max = crate::core::budgets::KNOWLEDGE_EMBEDDINGS_MAX_FACTS;
        let mut embedded = 0usize;
        for f in facts.into_iter().take(max) {
            if crate::core::knowledge_embedding::embed_and_store(
                &mut idx,
                engine,
                &f.category,
                &f.key,
                &f.value,
            )
            .is_ok()
            {
                embedded += 1;
            }
        }

        crate::core::knowledge_embedding::compact_against_knowledge(&mut idx, &knowledge);
        match idx.save() {
            Ok(()) => format!("Embeddings reindex ok (embedded {embedded} facts)."),
            Err(e) => format!("Embeddings reindex failed: {e}"),
        }
    }
    #[cfg(not(feature = "embeddings"))]
    {
        let _ = project_root;
        "ERR: embeddings feature not enabled".to_string()
    }
}

fn handle_remember(
    project_root: &str,
    category: Option<&str>,
    key: Option<&str>,
    value: Option<&str>,
    session_id: &str,
    confidence: Option<f32>,
) -> String {
    let cat = match category {
        Some(c) => c,
        None => return "Error: category is required for remember".to_string(),
    };
    let k = match key {
        Some(k) => k,
        None => return "Error: key is required for remember".to_string(),
    };
    let v = match value {
        Some(v) => v,
        None => return "Error: value is required for remember".to_string(),
    };
    let conf = confidence.unwrap_or(0.8);
    let mut knowledge = ProjectKnowledge::load_or_create(project_root);
    let contradiction = knowledge.remember(cat, k, v, session_id, conf);
    let _ = knowledge.run_memory_lifecycle();

    let mut result = format!(
        "Remembered [{cat}] {k}: {v} (confidence: {:.0}%)",
        conf * 100.0
    );

    if let Some(c) = contradiction {
        result.push_str(&format!("\n⚠ CONTRADICTION DETECTED: {}", c.resolution));
    }

    #[cfg(feature = "embeddings")]
    {
        if let Some(engine) = embedding_engine() {
            let mut idx = crate::core::knowledge_embedding::KnowledgeEmbeddingIndex::load(
                &knowledge.project_hash,
            )
            .unwrap_or_else(|| {
                crate::core::knowledge_embedding::KnowledgeEmbeddingIndex::new(
                    &knowledge.project_hash,
                )
            });

            match crate::core::knowledge_embedding::embed_and_store(&mut idx, engine, cat, k, v) {
                Ok(()) => {
                    crate::core::knowledge_embedding::compact_against_knowledge(
                        &mut idx, &knowledge,
                    );
                    if let Err(e) = idx.save() {
                        result.push_str(&format!("\n(warn: embeddings save failed: {e})"));
                    }
                }
                Err(e) => {
                    result.push_str(&format!("\n(warn: embeddings update failed: {e})"));
                }
            }
        }
    }

    match knowledge.save() {
        Ok(()) => result,
        Err(e) => format!("{result}\n(save failed: {e})"),
    }
}

fn handle_recall(
    project_root: &str,
    category: Option<&str>,
    query: Option<&str>,
    session_id: &str,
) -> String {
    let mut knowledge = match ProjectKnowledge::load(project_root) {
        Some(k) => k,
        None => return "No knowledge stored for this project yet.".to_string(),
    };

    if let Some(cat) = category {
        let limit = crate::core::budgets::KNOWLEDGE_RECALL_FACTS_LIMIT;
        let (facts, total) = knowledge.recall_by_category_for_output(cat, limit);
        if facts.is_empty() || total == 0 {
            // System 2: archive rehydrate (category-only)
            let rehydrated = rehydrate_from_archives(&mut knowledge, Some(cat), None, session_id);
            if rehydrated {
                let (facts2, total2) = knowledge.recall_by_category_for_output(cat, limit);
                if !facts2.is_empty() && total2 > 0 {
                    let mut out2 = format_facts(&facts2, total2, Some(cat));
                    if let Err(e) = knowledge.save() {
                        out2.push_str(&format!(
                            "\n(warn: failed to persist retrieval signals: {e})"
                        ));
                    }
                    return out2;
                }
            }
            return format!("No facts in category '{cat}'.");
        }
        let mut out = format_facts(&facts, total, Some(cat));
        if let Err(e) = knowledge.save() {
            out.push_str(&format!(
                "\n(warn: failed to persist retrieval signals: {e})"
            ));
        }
        return out;
    }

    if let Some(q) = query {
        #[cfg(feature = "embeddings")]
        {
            if let Some(engine) = embedding_engine() {
                if let Some(idx) = crate::core::knowledge_embedding::KnowledgeEmbeddingIndex::load(
                    &knowledge.project_hash,
                ) {
                    let limit = crate::core::budgets::KNOWLEDGE_RECALL_FACTS_LIMIT;
                    let scored = crate::core::knowledge_embedding::semantic_recall(
                        &knowledge, &idx, engine, q, limit,
                    );
                    if !scored.is_empty() {
                        let hits: Vec<SemanticHit> = scored
                            .iter()
                            .map(|s| SemanticHit {
                                category: s.fact.category.clone(),
                                key: s.fact.key.clone(),
                                value: s.fact.value.clone(),
                                score: s.score,
                                semantic_score: s.semantic_score,
                                confidence_score: s.confidence_score,
                            })
                            .collect();
                        apply_retrieval_signals_from_hits(&mut knowledge, &hits);
                        let mut out = format_semantic_facts(q, &hits);
                        if let Err(e) = knowledge.save() {
                            out.push_str(&format!(
                                "\n(warn: failed to persist retrieval signals: {e})"
                            ));
                        }
                        return out;
                    }
                }
            }
        }

        let limit = crate::core::budgets::KNOWLEDGE_RECALL_FACTS_LIMIT;
        let (facts, total) = knowledge.recall_for_output(q, limit);
        if facts.is_empty() || total == 0 {
            // System 2: archive rehydrate (query)
            let rehydrated = rehydrate_from_archives(&mut knowledge, None, Some(q), session_id);
            if rehydrated {
                let (facts2, total2) = knowledge.recall_for_output(q, limit);
                if !facts2.is_empty() && total2 > 0 {
                    let mut out2 = format_facts(&facts2, total2, None);
                    if let Err(e) = knowledge.save() {
                        out2.push_str(&format!(
                            "\n(warn: failed to persist retrieval signals: {e})"
                        ));
                    }
                    return out2;
                }
            }
            return format!("No facts matching '{q}'.");
        }
        let mut out = format_facts(&facts, total, None);
        if let Err(e) = knowledge.save() {
            out.push_str(&format!(
                "\n(warn: failed to persist retrieval signals: {e})"
            ));
        }
        return out;
    }

    "Error: provide query or category for recall".to_string()
}

fn rehydrate_from_archives(
    knowledge: &mut ProjectKnowledge,
    category: Option<&str>,
    query: Option<&str>,
    session_id: &str,
) -> bool {
    let mut archives = crate::core::memory_lifecycle::list_archives();
    if archives.is_empty() {
        return false;
    }
    archives.sort();
    let max_archives = crate::core::budgets::KNOWLEDGE_REHYDRATE_MAX_ARCHIVES;
    if archives.len() > max_archives {
        archives = archives[archives.len() - max_archives..].to_vec();
    }

    let terms: Vec<String> = query
        .unwrap_or("")
        .to_lowercase()
        .split_whitespace()
        .filter(|t| !t.is_empty())
        .map(|s| s.to_string())
        .collect();

    #[derive(Clone)]
    struct Cand {
        category: String,
        key: String,
        value: String,
        confidence: f32,
        score: f32,
    }

    let mut cands: Vec<Cand> = Vec::new();

    for p in &archives {
        let p_str = p.to_string_lossy().to_string();
        let facts = match crate::core::memory_lifecycle::restore_archive(&p_str) {
            Ok(f) => f,
            Err(_) => continue,
        };
        for f in facts {
            if let Some(cat) = category {
                if f.category != cat {
                    continue;
                }
            }
            if !terms.is_empty() {
                let searchable = format!(
                    "{} {} {} {}",
                    f.category.to_lowercase(),
                    f.key.to_lowercase(),
                    f.value.to_lowercase(),
                    f.source_session.to_lowercase()
                );
                let match_count = terms.iter().filter(|t| searchable.contains(*t)).count();
                if match_count == 0 {
                    continue;
                }
                let rel = match_count as f32 / terms.len() as f32;
                let score = rel * f.confidence;
                cands.push(Cand {
                    category: f.category,
                    key: f.key,
                    value: f.value,
                    confidence: f.confidence,
                    score,
                });
            } else {
                cands.push(Cand {
                    category: f.category,
                    key: f.key,
                    value: f.value,
                    confidence: f.confidence,
                    score: f.confidence,
                });
            }
        }
    }

    if cands.is_empty() {
        return false;
    }

    cands.sort_by(|a, b| {
        b.score
            .partial_cmp(&a.score)
            .unwrap_or(std::cmp::Ordering::Equal)
            .then_with(|| {
                b.confidence
                    .partial_cmp(&a.confidence)
                    .unwrap_or(std::cmp::Ordering::Equal)
            })
            .then_with(|| a.category.cmp(&b.category))
            .then_with(|| a.key.cmp(&b.key))
            .then_with(|| a.value.cmp(&b.value))
    });
    cands.truncate(crate::core::budgets::KNOWLEDGE_REHYDRATE_LIMIT);

    let mut any = false;
    for c in &cands {
        knowledge.remember(
            &c.category,
            &c.key,
            &c.value,
            session_id,
            c.confidence.max(0.6),
        );
        any = true;
    }
    if any {
        let _ = knowledge.run_memory_lifecycle();
    }
    any
}

fn handle_status(project_root: &str) -> String {
    let knowledge = match ProjectKnowledge::load(project_root) {
        Some(k) => k,
        None => {
            return "No knowledge stored for this project yet. Use ctx_knowledge(action=\"remember\") to start.".to_string();
        }
    };

    let current_facts = knowledge.facts.iter().filter(|f| f.is_current()).count();
    let archived_facts = knowledge.facts.len() - current_facts;

    let mut out = format!(
        "Project Knowledge: {} active facts ({} archived), {} history entries\n",
        current_facts,
        archived_facts,
        knowledge.history.len()
    );
    out.push_str(&format!(
        "Last updated: {}\n",
        knowledge.updated_at.format("%Y-%m-%d %H:%M UTC")
    ));

    let categories = knowledge.list_categories();
    if !categories.is_empty() {
        out.push_str("Categories: ");
        let category_strs: Vec<String> = categories.iter().map(|(c, n)| format!("{c}({n})")).collect();
        out.push_str(&category_strs.join(", "));
        out.push('\n');
    }

    out.push_str(&knowledge.format_summary());
    out
}

fn handle_remove(project_root: &str, category: Option<&str>, key: Option<&str>) -> String {
    let cat = match category {
        Some(c) => c,
        None => return "Error: category is required for remove".to_string(),
    };
    let k = match key {
        Some(k) => k,
        None => return "Error: key is required for remove".to_string(),
    };
    let mut knowledge = ProjectKnowledge::load_or_create(project_root);
    if knowledge.remove_fact(cat, k) {
        let _ = knowledge.run_memory_lifecycle();

        #[cfg(feature = "embeddings")]
        {
            if let Some(mut idx) = crate::core::knowledge_embedding::KnowledgeEmbeddingIndex::load(
                &knowledge.project_hash,
            ) {
                idx.remove(cat, k);
                crate::core::knowledge_embedding::compact_against_knowledge(&mut idx, &knowledge);
                let _ = idx.save();
            }
        }

        match knowledge.save() {
            Ok(()) => format!("Removed [{cat}] {k}"),
            Err(e) => format!("Removed but save failed: {e}"),
        }
    } else {
        format!("No fact found: [{cat}] {k}")
    }
}

fn handle_consolidate(project_root: &str) -> String {
    let session = match SessionState::load_latest_for_project_root(project_root) {
        Some(s) => s,
        None => return "No active session to consolidate.".to_string(),
    };

    let mut knowledge = ProjectKnowledge::load_or_create(project_root);
    let mut consolidated = 0u32;

    for finding in &session.findings {
        let key_text = if let Some(ref file) = finding.file {
            if let Some(line) = finding.line {
                format!("{file}:{line}")
            } else {
                file.clone()
            }
        } else {
            format!("finding-{consolidated}")
        };

        knowledge.remember("finding", &key_text, &finding.summary, &session.id, 0.7);
        consolidated += 1;
    }

    for decision in &session.decisions {
        let key_text = decision
            .summary
            .chars()
            .take(50)
            .collect::<String>()
            .replace(' ', "-")
            .to_lowercase();

        knowledge.remember("decision", &key_text, &decision.summary, &session.id, 0.85);
        consolidated += 1;
    }

    let task_desc = session
        .task
        .as_ref()
        .map(|t| t.description.clone())
        .unwrap_or_else(|| "(no task)".into());

    let summary = format!(
        "Session {}: {} — {} findings, {} decisions consolidated",
        session.id,
        task_desc,
        session.findings.len(),
        session.decisions.len()
    );
    knowledge.consolidate(&summary, vec![session.id.clone()]);
    let _ = knowledge.run_memory_lifecycle();

    match knowledge.save() {
        Ok(()) => format!(
            "Consolidated {consolidated} items from session {} into project knowledge.\n\
             Facts: {}, History: {}",
            session.id,
            knowledge.facts.len(),
            knowledge.history.len()
        ),
        Err(e) => format!("Consolidation done but save failed: {e}"),
    }
}

fn handle_timeline(project_root: &str, category: Option<&str>) -> String {
    let knowledge = match ProjectKnowledge::load(project_root) {
        Some(k) => k,
        None => return "No knowledge stored yet.".to_string(),
    };

    let cat = match category {
        Some(c) => c,
        None => return "Error: category is required for timeline".to_string(),
    };

    let facts = knowledge.timeline(cat);
    if facts.is_empty() {
        return format!("No history for category '{cat}'.");
    }

    let mut ordered: Vec<&crate::core::knowledge::KnowledgeFact> = facts;
    ordered.sort_by(|a, b| {
        let a_start = a.valid_from.unwrap_or(a.created_at);
        let b_start = b.valid_from.unwrap_or(b.created_at);
        a_start
            .cmp(&b_start)
            .then_with(|| a.last_confirmed.cmp(&b.last_confirmed))
            .then_with(|| a.key.cmp(&b.key))
            .then_with(|| a.value.cmp(&b.value))
    });

    let total = ordered.len();
    let limit = crate::core::budgets::KNOWLEDGE_TIMELINE_LIMIT;
    if ordered.len() > limit {
        ordered = ordered[ordered.len() - limit..].to_vec();
    }

    let mut out = format!(
        "Timeline [{cat}] (showing {}/{} entries):\n",
        ordered.len(),
        total
    );
    for f in &ordered {
        let status = if f.is_current() {
            "CURRENT"
        } else {
            "archived"
        };
        let valid_range = match (f.valid_from, f.valid_until) {
            (Some(from), Some(until)) => format!(
                "{} → {}",
                from.format("%Y-%m-%d %H:%M"),
                until.format("%Y-%m-%d %H:%M")
            ),
            (Some(from), None) => format!("{} → now", from.format("%Y-%m-%d %H:%M")),
            _ => "unknown".to_string(),
        };
        out.push_str(&format!(
            "  {} = {} [{status}] ({valid_range}) conf={:.0}% x{}\n",
            f.key,
            f.value,
            f.confidence * 100.0,
            f.confirmation_count
        ));
    }
    out
}

fn handle_categories(project_root: &str) -> String {
    let knowledge = match ProjectKnowledge::load(project_root) {
        Some(k) => k,
        None => return "No knowledge stored yet.".to_string(),
    };

    let categories = knowledge.list_categories();
    if categories.is_empty() {
        return "No knowledge categories yet. Use ctx_knowledge(action=\"remember\", category=\"...\") to create categories.".to_string();
    }

    let mut categories = categories;
    categories.sort_by(|a, b| b.1.cmp(&a.1).then_with(|| a.0.cmp(&b.0)));
    let total = categories.len();
    categories.truncate(crate::core::budgets::KNOWLEDGE_ROOMS_LIMIT);

    let mut out = format!(
        "Knowledge Categories (showing {}/{} categories, project: {}):\n",
        categories.len(),
        total,
        short_hash(&knowledge.project_hash)
    );
    for (cat, count) in &categories {
        out.push_str(&format!("  [{cat}] {count} fact(s)\n"));
    }
    out
}

fn handle_search(project_root: &str, query: Option<&str>, session_id: &str) -> String {
    handle_recall(project_root, None, query, session_id)
}

fn handle_wakeup(project_root: &str) -> String {
    let knowledge = match ProjectKnowledge::load(project_root) {
        Some(k) => k,
        None => return "No knowledge for wake-up briefing.".to_string(),
    };
    let aaak = knowledge.format_aaak();
    if aaak.is_empty() {
        return "No knowledge yet. Start using ctx_knowledge(action=\"remember\") to build project memory.".to_string();
    }
    format!("WAKE-UP BRIEFING:\n{aaak}")
}

fn handle_upkeep(project_root: &str) -> String {
    let mut knowledge = match ProjectKnowledge::load(project_root) {
        Some(k) => k,
        None => return "No knowledge available for upkeep.".to_string(),
    };

    let report = knowledge.run_memory_lifecycle();
    let _ = knowledge.save();

    format!(
        "Lifecycle upkeep complete: decayed={}, consolidated={}, archived={}, compacted={}, remaining={}",
        report.decayed_count,
        report.consolidated_count,
        report.archived_count,
        report.compacted_count,
        report.remaining_facts
    )
}

fn handle_triage(project_root: &str, mode: Option<&str>) -> String {
    let mut knowledge = match ProjectKnowledge::load(project_root) {
        Some(k) => k,
        None => return "No knowledge available for triage.".to_string(),
    };

    let current_indices: Vec<usize> = knowledge
        .facts
        .iter()
        .enumerate()
        .filter_map(|(idx, fact)| fact.is_current().then_some(idx))
        .collect();

    let mut duplicate_like = Vec::new();
    for (idx, fact_idx) in current_indices.iter().enumerate() {
        for other_idx in current_indices.iter().skip(idx + 1) {
            let fact = &knowledge.facts[*fact_idx];
            let other = &knowledge.facts[*other_idx];
            if fact.category != other.category {
                continue;
            }

            let similarity = triage_similarity(&fact.value, &other.value);
            if similarity >= 0.8 {
                duplicate_like.push((*fact_idx, *other_idx, similarity));
            }
        }
    }

    let junk_like = current_indices
        .iter()
        .copied()
        .filter(|idx| is_triage_junk_candidate(&knowledge.facts[*idx]))
        .collect::<Vec<_>>();

    if matches!(mode, Some(mode) if mode.eq_ignore_ascii_case("apply")) {
        let now = Utc::now();
        let mut merged = 0usize;
        let mut junk_marked = 0usize;
        let mut applied = Vec::new();
        let mut touched = false;
        let mut retired = std::collections::HashSet::new();

        for (keep_idx, duplicate_idx, similarity) in &duplicate_like {
            if retired.contains(duplicate_idx) || !knowledge.facts[*duplicate_idx].is_current() {
                continue;
            }

            let keep = &knowledge.facts[*keep_idx];
            let keep_ref = format!("{}/{}", keep.category, keep.key);
            let duplicate_category = knowledge.facts[*duplicate_idx].category.clone();
            let duplicate_key = knowledge.facts[*duplicate_idx].key.clone();
            let duplicate = &mut knowledge.facts[*duplicate_idx];
            duplicate.valid_until = Some(now);
            duplicate.supersedes = Some(format!("merged-into:{keep_ref}"));
            retired.insert(*duplicate_idx);
            touched = true;
            merged += 1;
            applied.push(format!(
                "merge [{duplicate_category}/{duplicate_key}] -> [{keep_ref}] ({:.0}%)",
                similarity * 100.0
            ));
        }

        for idx in &junk_like {
            if retired.contains(idx) || !knowledge.facts[*idx].is_current() {
                continue;
            }

            let category = knowledge.facts[*idx].category.clone();
            let key = knowledge.facts[*idx].key.clone();
            let fact = &mut knowledge.facts[*idx];
            fact.valid_until = Some(now);
            fact.supersedes = Some("triage:junk".to_string());
            retired.insert(*idx);
            touched = true;
            junk_marked += 1;
            applied.push(format!("mark_junk [{category}/{key}]"));
        }

        if touched {
            knowledge.updated_at = now;
            let _ = knowledge.run_memory_lifecycle();
            let _ = knowledge.save();
        }

        let mut lines = vec![format!(
            "TRIAGE APPLY: current_facts={}, merged={}, junk_marked={}",
            current_indices.len(),
            merged,
            junk_marked
        )];
        if applied.is_empty() {
            lines.push("applied_actions: none".to_string());
        } else {
            lines.push(format!("applied_actions: {}", applied.join(" | ")));
        }
        return lines.join("\n");
    }

    let duplicate_lines = duplicate_like
        .iter()
        .map(|(fact_idx, other_idx, similarity)| {
            let fact = &knowledge.facts[*fact_idx];
            let other = &knowledge.facts[*other_idx];
            format!(
                "[{}/{}] ~ [{}/{}] ({:.0}%)",
                fact.category,
                fact.key,
                other.category,
                other.key,
                similarity * 100.0
            )
        })
        .collect::<Vec<_>>();

    let junk_lines = junk_like
        .iter()
        .map(|idx| {
            let fact = &knowledge.facts[*idx];
            format!("[{}/{}] {}", fact.category, fact.key, fact.value)
        })
        .collect::<Vec<_>>();

    let mut lines = vec![format!(
        "TRIAGE PREVIEW: current_facts={}",
        current_indices.len()
    )];
    if duplicate_lines.is_empty() {
        lines.push("duplicates: none".to_string());
    } else {
        lines.push(format!("duplicates: {}", duplicate_lines.join(" | ")));
    }
    if junk_lines.is_empty() {
        lines.push("junk_candidates: none".to_string());
    } else {
        lines.push(format!("junk_candidates: {}", junk_lines.join(" | ")));
    }
    lines.join("\n")
}

fn is_triage_junk_candidate(fact: &crate::core::knowledge::KnowledgeFact) -> bool {
    let key = fact.key.to_lowercase();
    let value = fact.value.to_lowercase();
    key.contains("demo")
        || key.contains("test")
        || value.contains("demo")
        || value.contains("placeholder")
}

fn triage_similarity(a: &str, b: &str) -> f32 {
    let a_lower = a.to_lowercase();
    let b_lower = b.to_lowercase();
    let a_words: std::collections::HashSet<&str> = a_lower.split_whitespace().collect();
    let b_words: std::collections::HashSet<&str> = b_lower.split_whitespace().collect();

    if a_words.is_empty() && b_words.is_empty() {
        return 1.0;
    }

    let intersection = a_words.intersection(&b_words).count();
    let union = a_words.union(&b_words).count();
    if union == 0 {
        return 0.0;
    }

    intersection as f32 / union as f32
}

#[cfg(feature = "embeddings")]
struct SemanticHit {
    category: String,
    key: String,
    value: String,
    score: f32,
    semantic_score: f32,
    confidence_score: f32,
}

#[cfg(feature = "embeddings")]
fn apply_retrieval_signals_from_hits(knowledge: &mut ProjectKnowledge, hits: &[SemanticHit]) {
    let now = Utc::now();
    for s in hits {
        for f in &mut knowledge.facts {
            if !f.is_current() {
                continue;
            }
            if f.category == s.category && f.key == s.key {
                f.retrieval_count = f.retrieval_count.saturating_add(1);
                f.last_retrieved = Some(now);
                break;
            }
        }
    }
}

#[cfg(feature = "embeddings")]
fn format_semantic_facts(query: &str, hits: &[SemanticHit]) -> String {
    if hits.is_empty() {
        return format!("No facts matching '{query}'.");
    }
    let mut out = format!("Semantic recall '{query}' (showing {}):\n", hits.len());
    for s in hits {
        out.push_str(&format!(
            "  [{}/{}]: {} (score: {:.0}%, sem: {:.0}%, conf: {:.0}%)\n",
            s.category,
            s.key,
            s.value,
            s.score * 100.0,
            s.semantic_score * 100.0,
            s.confidence_score * 100.0
        ));
    }
    out
}

fn format_facts(
    facts: &[crate::core::knowledge::KnowledgeFact],
    total: usize,
    category: Option<&str>,
) -> String {
    let mut facts: Vec<&crate::core::knowledge::KnowledgeFact> = facts.iter().collect();
    facts.sort_by(|a, b| sort_fact_for_output(a, b));

    let mut out = String::new();
    if let Some(cat) = category {
        out.push_str(&format!(
            "Facts [{cat}] (showing {}/{}):\n",
            facts.len(),
            total
        ));
    } else {
        out.push_str(&format!(
            "Matching facts (showing {}/{}):\n",
            facts.len(),
            total
        ));
    }
    for f in facts {
        let temporal = if !f.is_current() { " [archived]" } else { "" };
        out.push_str(&format!(
            "  [{}/{}]: {} (confidence: {:.0}%, confirmed: {} x{}){temporal}\n",
            f.category,
            f.key,
            f.value,
            f.confidence * 100.0,
            f.last_confirmed.format("%Y-%m-%d"),
            f.confirmation_count
        ));
    }
    out
}

fn short_hash(hash: &str) -> &str {
    if hash.len() > 8 {
        &hash[..8]
    } else {
        hash
    }
}

fn sort_fact_for_output(
    a: &crate::core::knowledge::KnowledgeFact,
    b: &crate::core::knowledge::KnowledgeFact,
) -> std::cmp::Ordering {
    salience_score(b)
        .cmp(&salience_score(a))
        .then_with(|| {
            b.confidence
                .partial_cmp(&a.confidence)
                .unwrap_or(std::cmp::Ordering::Equal)
        })
        .then_with(|| b.confirmation_count.cmp(&a.confirmation_count))
        .then_with(|| b.retrieval_count.cmp(&a.retrieval_count))
        .then_with(|| b.last_retrieved.cmp(&a.last_retrieved))
        .then_with(|| b.last_confirmed.cmp(&a.last_confirmed))
        .then_with(|| a.category.cmp(&b.category))
        .then_with(|| a.key.cmp(&b.key))
        .then_with(|| a.value.cmp(&b.value))
}

fn salience_score(f: &crate::core::knowledge::KnowledgeFact) -> u32 {
    let cat = f.category.to_lowercase();
    let base: u32 = match cat.as_str() {
        "decision" => 70,
        "gotcha" => 75,
        "architecture" | "arch" => 60,
        "security" => 65,
        "testing" | "tests" => 55,
        "deployment" | "deploy" => 55,
        "conventions" | "convention" => 45,
        "finding" => 40,
        _ => 30,
    };

    let confidence_bonus = (f.confidence.clamp(0.0, 1.0) * 30.0) as u32;
    let confirmation_bonus = f.confirmation_count.min(15);
    let retrieval_bonus = ((f.retrieval_count as f32).ln_1p() * 8.0).min(20.0) as u32;
    let recency_bonus = f
        .last_retrieved
        .map(|t| {
            let days = chrono::Utc::now().signed_duration_since(t).num_days();
            if days <= 7 {
                10u32
            } else if days <= 30 {
                5u32
            } else {
                0u32
            }
        })
        .unwrap_or(0u32);

    base + confidence_bonus + confirmation_bonus + retrieval_bonus + recency_bonus
}
