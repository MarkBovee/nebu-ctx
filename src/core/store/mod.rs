/// ContextStore — storage abstraction for nebula-ctx.
///
/// Currently lean-ctx only uses SQLite for the property graph.
/// Everything else is in-memory. This trait defines what we persist,
/// with two backends:
/// - SqliteStore: local dev, wraps existing rusqlite code
/// - PostgresStore: server/HA deployment, persists across restarts

use anyhow::Result;

// ── Data types ──────────────────────────────────────────────

#[derive(Debug, Clone)]
pub struct GraphNode {
    pub id: Option<i64>,
    pub kind: String,        // "File", "Symbol", "Module"
    pub name: String,
    pub file_path: String,
    pub line_start: Option<i32>,
    pub line_end: Option<i32>,
    pub metadata: Option<String>,
}

#[derive(Debug, Clone)]
pub struct GraphEdge {
    pub id: Option<i64>,
    pub source_id: i64,
    pub target_id: i64,
    pub kind: String,        // "Imports", "Calls", "Defines", "Exports", "TypeRef"
    pub metadata: Option<String>,
}

#[derive(Debug, Clone)]
pub struct BrainMemory {
    pub id: Option<i64>,
    pub brain_id: String,
    pub layer: String,       // "short_term", "long_term"
    pub memory_type: String, // "episodic", "semantic", "procedural"
    pub content: String,
    pub embedding: Option<Vec<u8>>,   // Serialized f32 vector as raw bytes
    pub composite_score: f64,
    pub recall_count: i32,
    pub weights_json: Option<String>,
    pub created_at: Option<String>,
}

#[derive(Debug, Clone)]
pub struct BrainSession {
    pub id: Option<i64>,
    pub brain_id: String,
    pub started_at: Option<String>,
    pub status: String,
    pub checkpoint_json: Option<String>,
}

#[derive(Debug, Clone)]
pub struct BrainCheckpoint {
    pub id: Option<i64>,
    pub session_id: i64,
    pub checkpoint_type: String, // "manual", "auto", "consolidation"
    pub content_json: String,
    pub created_at: Option<String>,
}

#[derive(Debug, Clone)]
pub struct OpenLoop {
    pub id: Option<i64>,
    pub brain_id: String,
    pub description: String,
    pub priority: f64,
    pub status: String,      // "open", "closed"
    pub created_at: Option<String>,
}

#[derive(Debug, Clone)]
pub struct SearchResult {
    pub id: i64,
    pub content: String,
    pub score: f64,
    pub metadata: Option<String>,
}

#[derive(Debug, Clone)]
pub struct KnowledgeEntry {
    pub id: Option<i64>,
    pub category: String,
    pub key: String,
    pub value: String,
    pub confidence: f64,
    pub expires_at: Option<String>,
    pub updated_at: Option<String>,
}

// ── Trait ───────────────────────────────────────────────────

pub trait ContextStore: Send + Sync {
    // ── Property Graph ──

    fn upsert_node(&self, node: &GraphNode) -> Result<i64>;
    fn upsert_edge(&self, edge: &GraphEdge) -> Result<()>;
    fn get_node(&self, id: i64) -> Result<Option<GraphNode>>;
    fn get_nodes_by_file(&self, file_path: &str) -> Result<Vec<GraphNode>>;
    fn get_node_by_symbol(&self, name: &str, file_path: &str) -> Result<Option<GraphNode>>;
    fn remove_nodes_by_file(&self, file_path: &str) -> Result<()>;
    fn get_edges_from(&self, source_id: i64) -> Result<Vec<GraphEdge>>;
    fn get_edges_to(&self, target_id: i64) -> Result<Vec<GraphEdge>>;
    fn count_nodes(&self) -> Result<i64>;
    fn count_edges(&self) -> Result<i64>;

    // ── Brain Memory ──

    fn brain_store(&self, memory: &BrainMemory) -> Result<i64>;
    fn brain_recall(&self, brain_id: &str, query: &str, layer: &str, limit: usize) -> Result<Vec<BrainMemory>>;
    fn brain_get_by_id(&self, id: i64) -> Result<Option<BrainMemory>>;
    fn brain_update_score(&self, id: i64, score: f64) -> Result<()>;
    fn brain_increment_recall(&self, id: i64) -> Result<()>;
    fn brain_promote(&self, id: i64, new_layer: &str) -> Result<()>;
    fn brain_delete(&self, id: i64) -> Result<()>;
    fn brain_list(&self, brain_id: &str, layer: &str, limit: usize) -> Result<Vec<BrainMemory>>;

    // ── Brain Sessions ──

    fn brain_session_create(&self, brain_id: &str) -> Result<i64>;
    fn brain_session_get(&self, id: i64) -> Result<Option<BrainSession>>;
    fn brain_session_update_status(&self, id: i64, status: &str) -> Result<()>;
    fn brain_session_update_checkpoint(&self, id: i64, checkpoint_json: &str) -> Result<()>;
    fn brain_session_latest(&self, brain_id: &str) -> Result<Option<BrainSession>>;

    // ── Brain Checkpoints ──

    fn brain_checkpoint_store(&self, checkpoint: &BrainCheckpoint) -> Result<i64>;
    fn brain_checkpoint_latest(&self, session_id: i64) -> Result<Option<BrainCheckpoint>>;

    // ── Open Loops ──

    fn open_loop_store(&self, loop_item: &OpenLoop) -> Result<i64>;
    fn open_loop_list(&self, brain_id: &str, status: &str) -> Result<Vec<OpenLoop>>;
    fn open_loop_close(&self, id: i64) -> Result<()>;

    // ── Knowledge ──

    fn knowledge_remember(&self, entry: &KnowledgeEntry) -> Result<()>;
    fn knowledge_recall(&self, query: &str, limit: usize) -> Result<Vec<KnowledgeEntry>>;
    fn knowledge_get(&self, category: &str, key: &str) -> Result<Option<KnowledgeEntry>>;
    fn knowledge_remove(&self, category: &str, key: &str) -> Result<()>;

    // ── Initialization ──

    fn initialize(&self) -> Result<()>;
}

pub mod sqlite;
#[cfg(feature = "cloud-server")]
pub mod postgres;

// Blanket delegation: Box<dyn ContextStore> implements ContextStore
// so callers can use open_store() result directly.
impl ContextStore for Box<dyn ContextStore> {
    fn upsert_node(&self, node: &GraphNode) -> Result<i64> { (**self).upsert_node(node) }
    fn upsert_edge(&self, edge: &GraphEdge) -> Result<()> { (**self).upsert_edge(edge) }
    fn get_node(&self, id: i64) -> Result<Option<GraphNode>> { (**self).get_node(id) }
    fn get_nodes_by_file(&self, file_path: &str) -> Result<Vec<GraphNode>> { (**self).get_nodes_by_file(file_path) }
    fn get_node_by_symbol(&self, name: &str, file_path: &str) -> Result<Option<GraphNode>> { (**self).get_node_by_symbol(name, file_path) }
    fn remove_nodes_by_file(&self, file_path: &str) -> Result<()> { (**self).remove_nodes_by_file(file_path) }
    fn get_edges_from(&self, source_id: i64) -> Result<Vec<GraphEdge>> { (**self).get_edges_from(source_id) }
    fn get_edges_to(&self, target_id: i64) -> Result<Vec<GraphEdge>> { (**self).get_edges_to(target_id) }
    fn count_nodes(&self) -> Result<i64> { (**self).count_nodes() }
    fn count_edges(&self) -> Result<i64> { (**self).count_edges() }
    fn brain_store(&self, memory: &BrainMemory) -> Result<i64> { (**self).brain_store(memory) }
    fn brain_recall(&self, brain_id: &str, query: &str, layer: &str, limit: usize) -> Result<Vec<BrainMemory>> { (**self).brain_recall(brain_id, query, layer, limit) }
    fn brain_get_by_id(&self, id: i64) -> Result<Option<BrainMemory>> { (**self).brain_get_by_id(id) }
    fn brain_update_score(&self, id: i64, score: f64) -> Result<()> { (**self).brain_update_score(id, score) }
    fn brain_increment_recall(&self, id: i64) -> Result<()> { (**self).brain_increment_recall(id) }
    fn brain_promote(&self, id: i64, new_layer: &str) -> Result<()> { (**self).brain_promote(id, new_layer) }
    fn brain_delete(&self, id: i64) -> Result<()> { (**self).brain_delete(id) }
    fn brain_list(&self, brain_id: &str, layer: &str, limit: usize) -> Result<Vec<BrainMemory>> { (**self).brain_list(brain_id, layer, limit) }
    fn brain_session_create(&self, brain_id: &str) -> Result<i64> { (**self).brain_session_create(brain_id) }
    fn brain_session_get(&self, id: i64) -> Result<Option<BrainSession>> { (**self).brain_session_get(id) }
    fn brain_session_update_status(&self, id: i64, status: &str) -> Result<()> { (**self).brain_session_update_status(id, status) }
    fn brain_session_update_checkpoint(&self, id: i64, checkpoint_json: &str) -> Result<()> { (**self).brain_session_update_checkpoint(id, checkpoint_json) }
    fn brain_session_latest(&self, brain_id: &str) -> Result<Option<BrainSession>> { (**self).brain_session_latest(brain_id) }
    fn brain_checkpoint_store(&self, checkpoint: &BrainCheckpoint) -> Result<i64> { (**self).brain_checkpoint_store(checkpoint) }
    fn brain_checkpoint_latest(&self, session_id: i64) -> Result<Option<BrainCheckpoint>> { (**self).brain_checkpoint_latest(session_id) }
    fn open_loop_store(&self, loop_item: &OpenLoop) -> Result<i64> { (**self).open_loop_store(loop_item) }
    fn open_loop_list(&self, brain_id: &str, status: &str) -> Result<Vec<OpenLoop>> { (**self).open_loop_list(brain_id, status) }
    fn open_loop_close(&self, id: i64) -> Result<()> { (**self).open_loop_close(id) }
    fn knowledge_remember(&self, entry: &KnowledgeEntry) -> Result<()> { (**self).knowledge_remember(entry) }
    fn knowledge_recall(&self, query: &str, limit: usize) -> Result<Vec<KnowledgeEntry>> { (**self).knowledge_recall(query, limit) }
    fn knowledge_get(&self, category: &str, key: &str) -> Result<Option<KnowledgeEntry>> { (**self).knowledge_get(category, key) }
    fn knowledge_remove(&self, category: &str, key: &str) -> Result<()> { (**self).knowledge_remove(category, key) }
    fn initialize(&self) -> Result<()> { (**self).initialize() }
}

/// Open a store based on NEBULA_STORE env var.
/// - "postgres" → PostgresStore (requires cloud-server feature + DATABASE_URL)
/// - "sqlite" or unset → SqliteStore (default, local)
///
/// Returns a boxed trait object. Initializes schema on first connect.
pub fn open_store() -> Result<Box<dyn ContextStore>> {
    let store_type = std::env::var("NEBULA_STORE")
        .unwrap_or_else(|_| "sqlite".to_string())
        .to_lowercase();

    match store_type.as_str() {
        #[cfg(feature = "cloud-server")]
        "postgres" => {
            let url = std::env::var("DATABASE_URL")
                .or_else(|_| std::env::var("LEANCTX_CLOUD_DATABASE_URL"))
                .map_err(|_| anyhow::anyhow!(
                    "NEBULA_STORE=postgres but DATABASE_URL not set"
                ))?;
            // PostgresStore::open is async, use block_on
            let rt = tokio::runtime::Handle::current();
            let store = rt.block_on(async {
                crate::core::store::postgres::PostgresStore::open(&url).await
            })?;
            store.initialize()?;
            Ok(Box::new(store))
        }
        #[cfg(not(feature = "cloud-server"))]
        "postgres" => {
            Err(anyhow::anyhow!(
                "NEBULA_STORE=postgres but cloud-server feature not enabled. Build with --features cloud-server"
            ))
        }
        _ => {
            let data_dir = crate::core::data_dir::nebula_ctx_data_dir()
                .map_err(|e| anyhow::anyhow!("{e}"))?;
            let db_path = std::path::Path::new(&data_dir).join("nebula-ctx.db");
            let store = sqlite::SqliteStore::open(&db_path)?;
            store.initialize()?;
            Ok(Box::new(store))
        }
    }
}
