/// PostgresStore — PostgreSQL backend for server/HA deployment.
///
/// Uses deadpool-postgres for connection pooling.
/// Persists all data across restarts.

use anyhow::{anyhow, Result};
use deadpool_postgres::{Manager, Pool};
use tokio_postgres::{Config as PgConfig, NoTls};

use super::*;

pub struct PostgresStore {
    pool: Pool,
}

impl PostgresStore {
    pub async fn open(database_url: &str) -> Result<Self> {
        let pg_config = database_url.parse::<PgConfig>()
            .map_err(|e| anyhow!("Invalid DATABASE_URL: {}", e))?;
        let mgr = Manager::new(pg_config, NoTls);
        let pool = Pool::builder(mgr)
            .max_size(16)
            .build()
            .map_err(|e| anyhow!("Failed to create Postgres pool: {}", e))?;

        // Verify connection
        let client = pool.get().await
            .map_err(|e| anyhow!("Cannot connect to Postgres: {}", e))?;
        client.simple_query("SELECT 1").await
            .map_err(|e| anyhow!("Postgres ping failed: {}", e))?;

        Ok(Self { pool })
    }
}

impl ContextStore for PostgresStore {
    // ── Property Graph ──

    fn upsert_node(&self, node: &GraphNode) -> Result<i64> {
        // PostgresStore needs async but trait is sync.
        // We block_on here — property graph operations are fast.
        let rt = tokio::runtime::Handle::current();
        rt.block_on(async {
            let client = self.pool.get().await.map_err(|e| anyhow!("{}", e))?;
            let row = client.query_one(
                "INSERT INTO nodes (kind, name, file_path, line_start, line_end, metadata)
                 VALUES ($1, $2, $3, $4, $5, $6)
                 ON CONFLICT (kind, name, file_path) DO UPDATE SET
                   line_start = EXCLUDED.line_start,
                   line_end = EXCLUDED.line_end,
                   metadata = EXCLUDED.metadata
                 RETURNING id",
                &[&node.kind, &node.name, &node.file_path, &node.line_start, &node.line_end, &node.metadata],
            ).await?;
            Ok(row.get::<_, i64>(0))
        })
    }

    fn upsert_edge(&self, edge: &GraphEdge) -> Result<()> {
        let rt = tokio::runtime::Handle::current();
        rt.block_on(async {
            let client = self.pool.get().await.map_err(|e| anyhow!("{}", e))?;
            client.execute(
                "INSERT INTO edges (source_id, target_id, kind, metadata)
                 VALUES ($1, $2, $3, $4)
                 ON CONFLICT (source_id, target_id, kind) DO UPDATE SET
                   metadata = EXCLUDED.metadata",
                &[&edge.source_id, &edge.target_id, &edge.kind, &edge.metadata],
            ).await?;
            Ok(())
        })
    }

    fn get_node(&self, id: i64) -> Result<Option<GraphNode>> {
        let rt = tokio::runtime::Handle::current();
        rt.block_on(async {
            let client = self.pool.get().await.map_err(|e| anyhow!("{}", e))?;
            let row = client.query_opt(
                "SELECT id, kind, name, file_path, line_start, line_end, metadata FROM nodes WHERE id = $1",
                &[&id],
            ).await?;
            Ok(row.map(|r| GraphNode {
                id: Some(r.get(0)),
                kind: r.get(1),
                name: r.get(2),
                file_path: r.get(3),
                line_start: r.get(4),
                line_end: r.get(5),
                metadata: r.get(6),
            }))
        })
    }

    fn get_nodes_by_file(&self, file_path: &str) -> Result<Vec<GraphNode>> {
        let rt = tokio::runtime::Handle::current();
        rt.block_on(async {
            let client = self.pool.get().await.map_err(|e| anyhow!("{}", e))?;
            let rows = client.query(
                "SELECT id, kind, name, file_path, line_start, line_end, metadata FROM nodes WHERE file_path = $1",
                &[&file_path],
            ).await?;
            Ok(rows.into_iter().map(|r| GraphNode {
                id: Some(r.get(0)),
                kind: r.get(1),
                name: r.get(2),
                file_path: r.get(3),
                line_start: r.get(4),
                line_end: r.get(5),
                metadata: r.get(6),
            }).collect())
        })
    }

    fn get_node_by_symbol(&self, name: &str, file_path: &str) -> Result<Option<GraphNode>> {
        let rt = tokio::runtime::Handle::current();
        rt.block_on(async {
            let client = self.pool.get().await.map_err(|e| anyhow!("{}", e))?;
            let row = client.query_opt(
                "SELECT id, kind, name, file_path, line_start, line_end, metadata FROM nodes WHERE name = $1 AND file_path = $2",
                &[&name, &file_path],
            ).await?;
            Ok(row.map(|r| GraphNode {
                id: Some(r.get(0)),
                kind: r.get(1),
                name: r.get(2),
                file_path: r.get(3),
                line_start: r.get(4),
                line_end: r.get(5),
                metadata: r.get(6),
            }))
        })
    }

    fn remove_nodes_by_file(&self, file_path: &str) -> Result<()> {
        let rt = tokio::runtime::Handle::current();
        rt.block_on(async {
            let client = self.pool.get().await.map_err(|e| anyhow!("{}", e))?;
            client.execute("DELETE FROM nodes WHERE file_path = $1", &[&file_path]).await?;
            Ok(())
        })
    }

    fn get_edges_from(&self, source_id: i64) -> Result<Vec<GraphEdge>> {
        let rt = tokio::runtime::Handle::current();
        rt.block_on(async {
            let client = self.pool.get().await.map_err(|e| anyhow!("{}", e))?;
            let rows = client.query(
                "SELECT id, source_id, target_id, kind, metadata FROM edges WHERE source_id = $1",
                &[&source_id],
            ).await?;
            Ok(rows.into_iter().map(|r| GraphEdge {
                id: Some(r.get(0)),
                source_id: r.get(1),
                target_id: r.get(2),
                kind: r.get(3),
                metadata: r.get(4),
            }).collect())
        })
    }

    fn get_edges_to(&self, target_id: i64) -> Result<Vec<GraphEdge>> {
        let rt = tokio::runtime::Handle::current();
        rt.block_on(async {
            let client = self.pool.get().await.map_err(|e| anyhow!("{}", e))?;
            let rows = client.query(
                "SELECT id, source_id, target_id, kind, metadata FROM edges WHERE target_id = $1",
                &[&target_id],
            ).await?;
            Ok(rows.into_iter().map(|r| GraphEdge {
                id: Some(r.get(0)),
                source_id: r.get(1),
                target_id: r.get(2),
                kind: r.get(3),
                metadata: r.get(4),
            }).collect())
        })
    }

    fn count_nodes(&self) -> Result<i64> {
        let rt = tokio::runtime::Handle::current();
        rt.block_on(async {
            let client = self.pool.get().await.map_err(|e| anyhow!("{}", e))?;
            let row = client.query_one("SELECT COUNT(*) FROM nodes", &[]).await?;
            Ok(row.get(0))
        })
    }

    fn count_edges(&self) -> Result<i64> {
        let rt = tokio::runtime::Handle::current();
        rt.block_on(async {
            let client = self.pool.get().await.map_err(|e| anyhow!("{}", e))?;
            let row = client.query_one("SELECT COUNT(*) FROM edges", &[]).await?;
            Ok(row.get(0))
        })
    }

    // ── Brain Memory ──

    fn brain_store(&self, memory: &BrainMemory) -> Result<i64> {
        let rt = tokio::runtime::Handle::current();
        rt.block_on(async {
            let client = self.pool.get().await.map_err(|e| anyhow!("{}", e))?;
            let row = client.query_one(
                "INSERT INTO brain_memories (brain_id, layer, memory_type, content, embedding, composite_score, recall_count, weights_json, created_at)
                 VALUES ($1, $2, $3, $4, $5, $6, $7, $8, NOW())
                 RETURNING id",
                &[&memory.brain_id, &memory.layer, &memory.memory_type, &memory.content,
                  &memory.embedding, &memory.composite_score, &memory.recall_count, &memory.weights_json],
            ).await?;
            Ok(row.get::<_, i64>(0))
        })
    }

    fn brain_recall(&self, brain_id: &str, _query: &str, layer: &str, limit: usize) -> Result<Vec<BrainMemory>> {
        let rt = tokio::runtime::Handle::current();
        rt.block_on(async {
            let client = self.pool.get().await.map_err(|e| anyhow!("{}", e))?;
            let rows = if layer.is_empty() {
                client.query(
                    "SELECT id, brain_id, layer, memory_type, content, embedding, composite_score, recall_count, weights_json, created_at::TEXT
                     FROM brain_memories WHERE brain_id = $1 ORDER BY composite_score DESC LIMIT $2",
                    &[&brain_id, &(limit as i64)],
                ).await?
            } else {
                client.query(
                    "SELECT id, brain_id, layer, memory_type, content, embedding, composite_score, recall_count, weights_json, created_at::TEXT
                     FROM brain_memories WHERE brain_id = $1 AND layer = $2 ORDER BY composite_score DESC LIMIT $3",
                    &[&brain_id, &layer, &(limit as i64)],
                ).await?
            };
            Ok(rows.into_iter().map(|r| BrainMemory {
                id: Some(r.get(0)),
                brain_id: r.get(1),
                layer: r.get(2),
                memory_type: r.get(3),
                content: r.get(4),
                embedding: r.get(5),
                composite_score: r.get(6),
                recall_count: r.get(7),
                weights_json: r.get(8),
                created_at: r.get(9),
            }).collect())
        })
    }

    fn brain_get_by_id(&self, id: i64) -> Result<Option<BrainMemory>> {
        let rt = tokio::runtime::Handle::current();
        rt.block_on(async {
            let client = self.pool.get().await.map_err(|e| anyhow!("{}", e))?;
            let row = client.query_opt(
                "SELECT id, brain_id, layer, memory_type, content, embedding, composite_score, recall_count, weights_json, created_at::TEXT
                 FROM brain_memories WHERE id = $1", &[&id],
            ).await?;
            Ok(row.map(|r| BrainMemory {
                id: Some(r.get(0)), brain_id: r.get(1), layer: r.get(2), memory_type: r.get(3),
                content: r.get(4), embedding: r.get(5), composite_score: r.get(6),
                recall_count: r.get(7), weights_json: r.get(8), created_at: r.get(9),
            }))
        })
    }

    fn brain_update_score(&self, id: i64, score: f64) -> Result<()> {
        let rt = tokio::runtime::Handle::current();
        rt.block_on(async {
            let client = self.pool.get().await.map_err(|e| anyhow!("{}", e))?;
            client.execute("UPDATE brain_memories SET composite_score = $1 WHERE id = $2", &[&score, &id]).await?;
            Ok(())
        })
    }

    fn brain_increment_recall(&self, id: i64) -> Result<()> {
        let rt = tokio::runtime::Handle::current();
        rt.block_on(async {
            let client = self.pool.get().await.map_err(|e| anyhow!("{}", e))?;
            client.execute("UPDATE brain_memories SET recall_count = recall_count + 1 WHERE id = $1", &[&id]).await?;
            Ok(())
        })
    }

    fn brain_promote(&self, id: i64, new_layer: &str) -> Result<()> {
        let rt = tokio::runtime::Handle::current();
        rt.block_on(async {
            let client = self.pool.get().await.map_err(|e| anyhow!("{}", e))?;
            client.execute("UPDATE brain_memories SET layer = $1 WHERE id = $2", &[&new_layer, &id]).await?;
            Ok(())
        })
    }

    fn brain_delete(&self, id: i64) -> Result<()> {
        let rt = tokio::runtime::Handle::current();
        rt.block_on(async {
            let client = self.pool.get().await.map_err(|e| anyhow!("{}", e))?;
            client.execute("DELETE FROM brain_memories WHERE id = $1", &[&id]).await?;
            Ok(())
        })
    }

    fn brain_list(&self, brain_id: &str, layer: &str, limit: usize) -> Result<Vec<BrainMemory>> {
        self.brain_recall(brain_id, "", layer, limit)
    }

    // ── Brain Sessions ──

    fn brain_session_create(&self, brain_id: &str) -> Result<i64> {
        let rt = tokio::runtime::Handle::current();
        rt.block_on(async {
            let client = self.pool.get().await.map_err(|e| anyhow!("{}", e))?;
            let row = client.query_one(
                "INSERT INTO brain_sessions (brain_id, status, started_at) VALUES ($1, 'active', NOW()) RETURNING id",
                &[&brain_id],
            ).await?;
            Ok(row.get::<_, i64>(0))
        })
    }

    fn brain_session_get(&self, id: i64) -> Result<Option<BrainSession>> {
        let rt = tokio::runtime::Handle::current();
        rt.block_on(async {
            let client = self.pool.get().await.map_err(|e| anyhow!("{}", e))?;
            let row = client.query_opt(
                "SELECT id, brain_id, started_at::TEXT, status, checkpoint_json FROM brain_sessions WHERE id = $1",
                &[&id],
            ).await?;
            Ok(row.map(|r| BrainSession {
                id: Some(r.get(0)), brain_id: r.get(1), started_at: r.get(2),
                status: r.get(3), checkpoint_json: r.get(4),
            }))
        })
    }

    fn brain_session_update_status(&self, id: i64, status: &str) -> Result<()> {
        let rt = tokio::runtime::Handle::current();
        rt.block_on(async {
            let client = self.pool.get().await.map_err(|e| anyhow!("{}", e))?;
            client.execute("UPDATE brain_sessions SET status = $1 WHERE id = $2", &[&status, &id]).await?;
            Ok(())
        })
    }

    fn brain_session_update_checkpoint(&self, id: i64, checkpoint_json: &str) -> Result<()> {
        let rt = tokio::runtime::Handle::current();
        rt.block_on(async {
            let client = self.pool.get().await.map_err(|e| anyhow!("{}", e))?;
            client.execute("UPDATE brain_sessions SET checkpoint_json = $1 WHERE id = $2", &[&checkpoint_json, &id]).await?;
            Ok(())
        })
    }

    fn brain_session_latest(&self, brain_id: &str) -> Result<Option<BrainSession>> {
        let rt = tokio::runtime::Handle::current();
        rt.block_on(async {
            let client = self.pool.get().await.map_err(|e| anyhow!("{}", e))?;
            let row = client.query_opt(
                "SELECT id, brain_id, started_at::TEXT, status, checkpoint_json FROM brain_sessions
                 WHERE brain_id = $1 ORDER BY started_at DESC LIMIT 1",
                &[&brain_id],
            ).await?;
            Ok(row.map(|r| BrainSession {
                id: Some(r.get(0)), brain_id: r.get(1), started_at: r.get(2),
                status: r.get(3), checkpoint_json: r.get(4),
            }))
        })
    }

    // ── Brain Checkpoints ──

    fn brain_checkpoint_store(&self, checkpoint: &BrainCheckpoint) -> Result<i64> {
        let rt = tokio::runtime::Handle::current();
        rt.block_on(async {
            let client = self.pool.get().await.map_err(|e| anyhow!("{}", e))?;
            let row = client.query_one(
                "INSERT INTO brain_checkpoints (session_id, checkpoint_type, content_json, created_at)
                 VALUES ($1, $2, $3, NOW()) RETURNING id",
                &[&checkpoint.session_id, &checkpoint.checkpoint_type, &checkpoint.content_json],
            ).await?;
            Ok(row.get::<_, i64>(0))
        })
    }

    fn brain_checkpoint_latest(&self, session_id: i64) -> Result<Option<BrainCheckpoint>> {
        let rt = tokio::runtime::Handle::current();
        rt.block_on(async {
            let client = self.pool.get().await.map_err(|e| anyhow!("{}", e))?;
            let row = client.query_opt(
                "SELECT id, session_id, checkpoint_type, content_json, created_at::TEXT FROM brain_checkpoints
                 WHERE session_id = $1 ORDER BY created_at DESC LIMIT 1",
                &[&session_id],
            ).await?;
            Ok(row.map(|r| BrainCheckpoint {
                id: Some(r.get(0)), session_id: r.get(1), checkpoint_type: r.get(2),
                content_json: r.get(3), created_at: r.get(4),
            }))
        })
    }

    // ── Open Loops ──

    fn open_loop_store(&self, loop_item: &OpenLoop) -> Result<i64> {
        let rt = tokio::runtime::Handle::current();
        rt.block_on(async {
            let client = self.pool.get().await.map_err(|e| anyhow!("{}", e))?;
            let row = client.query_one(
                "INSERT INTO open_loops (brain_id, description, priority, status, created_at)
                 VALUES ($1, $2, $3, 'open', NOW()) RETURNING id",
                &[&loop_item.brain_id, &loop_item.description, &loop_item.priority],
            ).await?;
            Ok(row.get::<_, i64>(0))
        })
    }

    fn open_loop_list(&self, brain_id: &str, status: &str) -> Result<Vec<OpenLoop>> {
        let rt = tokio::runtime::Handle::current();
        rt.block_on(async {
            let client = self.pool.get().await.map_err(|e| anyhow!("{}", e))?;
            let rows = client.query(
                "SELECT id, brain_id, description, priority, status, created_at::TEXT FROM open_loops
                 WHERE brain_id = $1 AND status = $2 ORDER BY priority DESC",
                &[&brain_id, &status],
            ).await?;
            Ok(rows.into_iter().map(|r| OpenLoop {
                id: Some(r.get(0)), brain_id: r.get(1), description: r.get(2),
                priority: r.get(3), status: r.get(4), created_at: r.get(5),
            }).collect())
        })
    }

    fn open_loop_close(&self, id: i64) -> Result<()> {
        let rt = tokio::runtime::Handle::current();
        rt.block_on(async {
            let client = self.pool.get().await.map_err(|e| anyhow!("{}", e))?;
            client.execute("UPDATE open_loops SET status = 'closed' WHERE id = $1", &[&id]).await?;
            Ok(())
        })
    }

    // ── Knowledge ──

    fn knowledge_remember(&self, entry: &KnowledgeEntry) -> Result<()> {
        let rt = tokio::runtime::Handle::current();
        rt.block_on(async {
            let client = self.pool.get().await.map_err(|e| anyhow!("{}", e))?;
            client.execute(
                "INSERT INTO knowledge (category, key, value, confidence, expires_at, updated_at)
                 VALUES ($1, $2, $3, $4, $5, NOW())
                 ON CONFLICT (category, key) DO UPDATE SET
                   value = EXCLUDED.value,
                   confidence = EXCLUDED.confidence,
                   expires_at = EXCLUDED.expires_at,
                   updated_at = NOW()",
                &[&entry.category, &entry.key, &entry.value, &entry.confidence, &entry.expires_at],
            ).await?;
            Ok(())
        })
    }

    fn knowledge_recall(&self, query: &str, limit: usize) -> Result<Vec<KnowledgeEntry>> {
        let rt = tokio::runtime::Handle::current();
        rt.block_on(async {
            let client = self.pool.get().await.map_err(|e| anyhow!("{}", e))?;
            let pattern = format!("%{}%", query);
            let rows = client.query(
                "SELECT id, category, key, value, confidence, expires_at::TEXT, updated_at::TEXT FROM knowledge
                 WHERE value ILIKE $1 OR key ILIKE $1 ORDER BY updated_at DESC LIMIT $2",
                &[&pattern, &(limit as i64)],
            ).await?;
            Ok(rows.into_iter().map(|r| KnowledgeEntry {
                id: Some(r.get(0)), category: r.get(1), key: r.get(2),
                value: r.get(3), confidence: r.get(4), expires_at: r.get(5), updated_at: r.get(6),
            }).collect())
        })
    }

    fn knowledge_get(&self, category: &str, key: &str) -> Result<Option<KnowledgeEntry>> {
        let rt = tokio::runtime::Handle::current();
        rt.block_on(async {
            let client = self.pool.get().await.map_err(|e| anyhow!("{}", e))?;
            let row = client.query_opt(
                "SELECT id, category, key, value, confidence, expires_at::TEXT, updated_at::TEXT FROM knowledge
                 WHERE category = $1 AND key = $2",
                &[&category, &key],
            ).await?;
            Ok(row.map(|r| KnowledgeEntry {
                id: Some(r.get(0)), category: r.get(1), key: r.get(2),
                value: r.get(3), confidence: r.get(4), expires_at: r.get(5), updated_at: r.get(6),
            }))
        })
    }

    fn knowledge_remove(&self, category: &str, key: &str) -> Result<()> {
        let rt = tokio::runtime::Handle::current();
        rt.block_on(async {
            let client = self.pool.get().await.map_err(|e| anyhow!("{}", e))?;
            client.execute("DELETE FROM knowledge WHERE category = $1 AND key = $2", &[&category, &key]).await?;
            Ok(())
        })
    }

    // ── Initialization ──

    fn initialize(&self) -> Result<()> {
        let rt = tokio::runtime::Handle::current();
        rt.block_on(async {
            let client = self.pool.get().await.map_err(|e| anyhow!("{}", e))?;
            client.batch_execute(
                "CREATE TABLE IF NOT EXISTS nodes (
                    id BIGSERIAL PRIMARY KEY,
                    kind TEXT NOT NULL,
                    name TEXT NOT NULL,
                    file_path TEXT NOT NULL,
                    line_start INTEGER,
                    line_end INTEGER,
                    metadata TEXT,
                    UNIQUE(kind, name, file_path)
                );
                CREATE INDEX IF NOT EXISTS idx_nodes_file ON nodes(file_path);
                CREATE INDEX IF NOT EXISTS idx_nodes_name ON nodes(name);

                CREATE TABLE IF NOT EXISTS edges (
                    id BIGSERIAL PRIMARY KEY,
                    source_id BIGINT NOT NULL REFERENCES nodes(id) ON DELETE CASCADE,
                    target_id BIGINT NOT NULL REFERENCES nodes(id) ON DELETE CASCADE,
                    kind TEXT NOT NULL,
                    metadata TEXT,
                    UNIQUE(source_id, target_id, kind)
                );
                CREATE INDEX IF NOT EXISTS idx_edges_source ON edges(source_id);
                CREATE INDEX IF NOT EXISTS idx_edges_target ON edges(target_id);

                CREATE TABLE IF NOT EXISTS brain_memories (
                    id BIGSERIAL PRIMARY KEY,
                    brain_id TEXT NOT NULL,
                    layer TEXT NOT NULL DEFAULT 'short_term',
                    memory_type TEXT NOT NULL DEFAULT 'semantic',
                    content TEXT NOT NULL,
                    embedding BYTEA,
                    composite_score DOUBLE PRECISION NOT NULL DEFAULT 0.5,
                    recall_count INTEGER NOT NULL DEFAULT 0,
                    weights_json TEXT,
                    created_at TIMESTAMPTZ DEFAULT NOW()
                );
                CREATE INDEX IF NOT EXISTS idx_brain_memories_brain ON brain_memories(brain_id);
                CREATE INDEX IF NOT EXISTS idx_brain_memories_layer ON brain_memories(brain_id, layer);

                CREATE TABLE IF NOT EXISTS brain_sessions (
                    id BIGSERIAL PRIMARY KEY,
                    brain_id TEXT NOT NULL,
                    started_at TIMESTAMPTZ DEFAULT NOW(),
                    status TEXT NOT NULL DEFAULT 'active',
                    checkpoint_json TEXT
                );
                CREATE INDEX IF NOT EXISTS idx_brain_sessions_brain ON brain_sessions(brain_id);

                CREATE TABLE IF NOT EXISTS brain_checkpoints (
                    id BIGSERIAL PRIMARY KEY,
                    session_id BIGINT NOT NULL REFERENCES brain_sessions(id) ON DELETE CASCADE,
                    checkpoint_type TEXT NOT NULL,
                    content_json TEXT NOT NULL,
                    created_at TIMESTAMPTZ DEFAULT NOW()
                );
                CREATE INDEX IF NOT EXISTS idx_brain_checkpoints_session ON brain_checkpoints(session_id);

                CREATE TABLE IF NOT EXISTS open_loops (
                    id BIGSERIAL PRIMARY KEY,
                    brain_id TEXT NOT NULL,
                    description TEXT NOT NULL,
                    priority DOUBLE PRECISION NOT NULL DEFAULT 0.5,
                    status TEXT NOT NULL DEFAULT 'open',
                    created_at TIMESTAMPTZ DEFAULT NOW()
                );
                CREATE INDEX IF NOT EXISTS idx_open_loops_brain ON open_loops(brain_id);

                CREATE TABLE IF NOT EXISTS knowledge (
                    id BIGSERIAL PRIMARY KEY,
                    category TEXT NOT NULL,
                    key TEXT NOT NULL,
                    value TEXT NOT NULL,
                    confidence DOUBLE PRECISION NOT NULL DEFAULT 0.8,
                    expires_at TIMESTAMPTZ,
                    updated_at TIMESTAMPTZ DEFAULT NOW(),
                    UNIQUE(category, key)
                );
                CREATE INDEX IF NOT EXISTS idx_knowledge_cat ON knowledge(category);
                "
            ).await?;
            Ok(())
        })
    }
}
