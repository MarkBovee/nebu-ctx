/// SqliteStore — SQLite backend for local dev.
///
/// Uses rusqlite with bundled SQLite. Stores everything in a single file
/// at `{data_dir}/nebula-ctx.db`.

use anyhow::{anyhow, Result};
use rusqlite::{params, Connection, OptionalExtension};
use std::path::Path;
use std::sync::Mutex;

use super::*;

pub struct SqliteStore {
    conn: Mutex<Connection>,
}

impl SqliteStore {
    pub fn open(db_path: &Path) -> Result<Self> {
        let conn = Connection::open(db_path)
            .map_err(|e| anyhow!("Cannot open SQLite at {:?}: {}", db_path, e))?;

        conn.execute_batch("PRAGMA journal_mode = WAL; PRAGMA synchronous = NORMAL; PRAGMA foreign_keys = ON;")?;

        Ok(Self {
            conn: Mutex::new(conn),
        })
    }

    pub fn open_in_memory() -> Result<Self> {
        let conn = Connection::open_in_memory()?;
        Ok(Self {
            conn: Mutex::new(conn),
        })
    }

    fn node_from_row(row: &rusqlite::Row) -> rusqlite::Result<GraphNode> {
        Ok(GraphNode {
            id: Some(row.get(0)?),
            kind: row.get(1)?,
            name: row.get(2)?,
            file_path: row.get(3)?,
            line_start: row.get(4)?,
            line_end: row.get(5)?,
            metadata: row.get(6)?,
        })
    }

    fn edge_from_row(row: &rusqlite::Row) -> rusqlite::Result<GraphEdge> {
        Ok(GraphEdge {
            id: Some(row.get(0)?),
            source_id: row.get(1)?,
            target_id: row.get(2)?,
            kind: row.get(3)?,
            metadata: row.get(4)?,
        })
    }

    fn memory_from_row(row: &rusqlite::Row) -> rusqlite::Result<BrainMemory> {
        Ok(BrainMemory {
            id: Some(row.get(0)?),
            brain_id: row.get(1)?,
            layer: row.get(2)?,
            memory_type: row.get(3)?,
            content: row.get(4)?,
            embedding: row.get(5)?,
            composite_score: row.get(6)?,
            recall_count: row.get(7)?,
            weights_json: row.get(8)?,
            created_at: row.get(9)?,
        })
    }
}

impl ContextStore for SqliteStore {
    // ── Property Graph ──

    fn upsert_node(&self, node: &GraphNode) -> Result<i64> {
        let conn = self.conn.lock().map_err(|e| anyhow!("{}", e))?;
        conn.execute(
            "INSERT INTO nodes (kind, name, file_path, line_start, line_end, metadata)
             VALUES (?1, ?2, ?3, ?4, ?5, ?6)
             ON CONFLICT(kind, name, file_path) DO UPDATE SET
               line_start = excluded.line_start,
               line_end = excluded.line_end,
               metadata = excluded.metadata",
            params![node.kind, node.name, node.file_path, node.line_start, node.line_end, node.metadata],
        )?;
        Ok(conn.last_insert_rowid())
    }

    fn upsert_edge(&self, edge: &GraphEdge) -> Result<()> {
        let conn = self.conn.lock().map_err(|e| anyhow!("{}", e))?;
        conn.execute(
            "INSERT INTO edges (source_id, target_id, kind, metadata)
             VALUES (?1, ?2, ?3, ?4)
             ON CONFLICT(source_id, target_id, kind) DO UPDATE SET
               metadata = excluded.metadata",
            params![edge.source_id, edge.target_id, edge.kind, edge.metadata],
        )?;
        Ok(())
    }

    fn get_node(&self, id: i64) -> Result<Option<GraphNode>> {
        let conn = self.conn.lock().map_err(|e| anyhow!("{}", e))?;
        conn.query_row(
            "SELECT id, kind, name, file_path, line_start, line_end, metadata FROM nodes WHERE id = ?1",
            params![id],
            Self::node_from_row,
        ).optional().map_err(|e| anyhow!("{}", e))
    }

    fn get_nodes_by_file(&self, file_path: &str) -> Result<Vec<GraphNode>> {
        let conn = self.conn.lock().map_err(|e| anyhow!("{}", e))?;
        let mut stmt = conn.prepare(
            "SELECT id, kind, name, file_path, line_start, line_end, metadata FROM nodes WHERE file_path = ?1",
        )?;
        let rows = stmt.query_map(params![file_path], Self::node_from_row)?;
        rows.collect::<std::result::Result<Vec<_>, _>>().map_err(|e| anyhow!("{}", e))
    }

    fn get_node_by_symbol(&self, name: &str, file_path: &str) -> Result<Option<GraphNode>> {
        let conn = self.conn.lock().map_err(|e| anyhow!("{}", e))?;
        conn.query_row(
            "SELECT id, kind, name, file_path, line_start, line_end, metadata FROM nodes WHERE name = ?1 AND file_path = ?2",
            params![name, file_path],
            Self::node_from_row,
        ).optional().map_err(|e| anyhow!("{}", e))
    }

    fn remove_nodes_by_file(&self, file_path: &str) -> Result<()> {
        let conn = self.conn.lock().map_err(|e| anyhow!("{}", e))?;
        conn.execute("DELETE FROM nodes WHERE file_path = ?1", params![file_path])?;
        Ok(())
    }

    fn get_edges_from(&self, source_id: i64) -> Result<Vec<GraphEdge>> {
        let conn = self.conn.lock().map_err(|e| anyhow!("{}", e))?;
        let mut stmt = conn.prepare(
            "SELECT id, source_id, target_id, kind, metadata FROM edges WHERE source_id = ?1",
        )?;
        let rows = stmt.query_map(params![source_id], Self::edge_from_row)?;
        rows.collect::<std::result::Result<Vec<_>, _>>().map_err(|e| anyhow!("{}", e))
    }

    fn get_edges_to(&self, target_id: i64) -> Result<Vec<GraphEdge>> {
        let conn = self.conn.lock().map_err(|e| anyhow!("{}", e))?;
        let mut stmt = conn.prepare(
            "SELECT id, source_id, target_id, kind, metadata FROM edges WHERE target_id = ?1",
        )?;
        let rows = stmt.query_map(params![target_id], Self::edge_from_row)?;
        rows.collect::<std::result::Result<Vec<_>, _>>().map_err(|e| anyhow!("{}", e))
    }

    fn count_nodes(&self) -> Result<i64> {
        let conn = self.conn.lock().map_err(|e| anyhow!("{}", e))?;
        Ok(conn.query_row("SELECT COUNT(*) FROM nodes", [], |r| r.get(0))?)
    }

    fn count_edges(&self) -> Result<i64> {
        let conn = self.conn.lock().map_err(|e| anyhow!("{}", e))?;
        Ok(conn.query_row("SELECT COUNT(*) FROM edges", [], |r| r.get(0))?)
    }

    // ── Brain Memory ──

    fn brain_store(&self, memory: &BrainMemory) -> Result<i64> {
        let conn = self.conn.lock().map_err(|e| anyhow!("{}", e))?;
        conn.execute(
            "INSERT INTO brain_memories (brain_id, layer, memory_type, content, embedding, composite_score, recall_count, weights_json, created_at)
             VALUES (?1, ?2, ?3, ?4, ?5, ?6, ?7, ?8, datetime('now'))",
            params![memory.brain_id, memory.layer, memory.memory_type, memory.content,
                    memory.embedding, memory.composite_score, memory.recall_count, memory.weights_json],
        )?;
        Ok(conn.last_insert_rowid())
    }

    fn brain_recall(&self, brain_id: &str, _query: &str, layer: &str, limit: usize) -> Result<Vec<BrainMemory>> {
        let conn = self.conn.lock().map_err(|e| anyhow!("{}", e))?;
        let sql = if layer.is_empty() {
            "SELECT id, brain_id, layer, memory_type, content, embedding, composite_score, recall_count, weights_json, created_at
             FROM brain_memories WHERE brain_id = ?1 ORDER BY composite_score DESC LIMIT ?2"
        } else {
            "SELECT id, brain_id, layer, memory_type, content, embedding, composite_score, recall_count, weights_json, created_at
             FROM brain_memories WHERE brain_id = ?1 AND layer = ?2 ORDER BY composite_score DESC LIMIT ?3"
        };
        let mut stmt = conn.prepare(sql)?;
        let rows = if layer.is_empty() {
            stmt.query_map(params![brain_id, limit as i64], Self::memory_from_row)?
        } else {
            stmt.query_map(params![brain_id, layer, limit as i64], Self::memory_from_row)?
        };
        rows.collect::<std::result::Result<Vec<_>, _>>().map_err(|e| anyhow!("{}", e))
    }

    fn brain_get_by_id(&self, id: i64) -> Result<Option<BrainMemory>> {
        let conn = self.conn.lock().map_err(|e| anyhow!("{}", e))?;
        conn.query_row(
            "SELECT id, brain_id, layer, memory_type, content, embedding, composite_score, recall_count, weights_json, created_at
             FROM brain_memories WHERE id = ?1",
            params![id],
            Self::memory_from_row,
        ).optional().map_err(|e| anyhow!("{}", e))
    }

    fn brain_update_score(&self, id: i64, score: f64) -> Result<()> {
        let conn = self.conn.lock().map_err(|e| anyhow!("{}", e))?;
        conn.execute("UPDATE brain_memories SET composite_score = ?1 WHERE id = ?2", params![score, id])?;
        Ok(())
    }

    fn brain_increment_recall(&self, id: i64) -> Result<()> {
        let conn = self.conn.lock().map_err(|e| anyhow!("{}", e))?;
        conn.execute("UPDATE brain_memories SET recall_count = recall_count + 1 WHERE id = ?1", params![id])?;
        Ok(())
    }

    fn brain_promote(&self, id: i64, new_layer: &str) -> Result<()> {
        let conn = self.conn.lock().map_err(|e| anyhow!("{}", e))?;
        conn.execute("UPDATE brain_memories SET layer = ?1 WHERE id = ?2", params![new_layer, id])?;
        Ok(())
    }

    fn brain_delete(&self, id: i64) -> Result<()> {
        let conn = self.conn.lock().map_err(|e| anyhow!("{}", e))?;
        conn.execute("DELETE FROM brain_memories WHERE id = ?1", params![id])?;
        Ok(())
    }

    fn brain_list(&self, brain_id: &str, layer: &str, limit: usize) -> Result<Vec<BrainMemory>> {
        self.brain_recall(brain_id, "", layer, limit)
    }

    // ── Brain Sessions ──

    fn brain_session_create(&self, brain_id: &str) -> Result<i64> {
        let conn = self.conn.lock().map_err(|e| anyhow!("{}", e))?;
        conn.execute(
            "INSERT INTO brain_sessions (brain_id, status, started_at) VALUES (?1, 'active', datetime('now'))",
            params![brain_id],
        )?;
        Ok(conn.last_insert_rowid())
    }

    fn brain_session_get(&self, id: i64) -> Result<Option<BrainSession>> {
        let conn = self.conn.lock().map_err(|e| anyhow!("{}", e))?;
        conn.query_row(
            "SELECT id, brain_id, started_at, status, checkpoint_json FROM brain_sessions WHERE id = ?1",
            params![id],
            |row| Ok(BrainSession {
                id: Some(row.get(0)?),
                brain_id: row.get(1)?,
                started_at: row.get(2)?,
                status: row.get(3)?,
                checkpoint_json: row.get(4)?,
            }),
        ).optional().map_err(|e| anyhow!("{}", e))
    }

    fn brain_session_update_status(&self, id: i64, status: &str) -> Result<()> {
        let conn = self.conn.lock().map_err(|e| anyhow!("{}", e))?;
        conn.execute("UPDATE brain_sessions SET status = ?1 WHERE id = ?2", params![status, id])?;
        Ok(())
    }

    fn brain_session_update_checkpoint(&self, id: i64, checkpoint_json: &str) -> Result<()> {
        let conn = self.conn.lock().map_err(|e| anyhow!("{}", e))?;
        conn.execute("UPDATE brain_sessions SET checkpoint_json = ?1 WHERE id = ?2", params![checkpoint_json, id])?;
        Ok(())
    }

    fn brain_session_latest(&self, brain_id: &str) -> Result<Option<BrainSession>> {
        let conn = self.conn.lock().map_err(|e| anyhow!("{}", e))?;
        conn.query_row(
            "SELECT id, brain_id, started_at, status, checkpoint_json FROM brain_sessions
             WHERE brain_id = ?1 ORDER BY started_at DESC LIMIT 1",
            params![brain_id],
            |row| Ok(BrainSession {
                id: Some(row.get(0)?),
                brain_id: row.get(1)?,
                started_at: row.get(2)?,
                status: row.get(3)?,
                checkpoint_json: row.get(4)?,
            }),
        ).optional().map_err(|e| anyhow!("{}", e))
    }

    // ── Brain Checkpoints ──

    fn brain_checkpoint_store(&self, checkpoint: &BrainCheckpoint) -> Result<i64> {
        let conn = self.conn.lock().map_err(|e| anyhow!("{}", e))?;
        conn.execute(
            "INSERT INTO brain_checkpoints (session_id, checkpoint_type, content_json, created_at)
             VALUES (?1, ?2, ?3, datetime('now'))",
            params![checkpoint.session_id, checkpoint.checkpoint_type, checkpoint.content_json],
        )?;
        Ok(conn.last_insert_rowid())
    }

    fn brain_checkpoint_latest(&self, session_id: i64) -> Result<Option<BrainCheckpoint>> {
        let conn = self.conn.lock().map_err(|e| anyhow!("{}", e))?;
        conn.query_row(
            "SELECT id, session_id, checkpoint_type, content_json, created_at FROM brain_checkpoints
             WHERE session_id = ?1 ORDER BY created_at DESC LIMIT 1",
            params![session_id],
            |row| Ok(BrainCheckpoint {
                id: Some(row.get(0)?),
                session_id: row.get(1)?,
                checkpoint_type: row.get(2)?,
                content_json: row.get(3)?,
                created_at: row.get(4)?,
            }),
        ).optional().map_err(|e| anyhow!("{}", e))
    }

    // ── Open Loops ──

    fn open_loop_store(&self, loop_item: &OpenLoop) -> Result<i64> {
        let conn = self.conn.lock().map_err(|e| anyhow!("{}", e))?;
        conn.execute(
            "INSERT INTO open_loops (brain_id, description, priority, status, created_at)
             VALUES (?1, ?2, ?3, 'open', datetime('now'))",
            params![loop_item.brain_id, loop_item.description, loop_item.priority],
        )?;
        Ok(conn.last_insert_rowid())
    }

    fn open_loop_list(&self, brain_id: &str, status: &str) -> Result<Vec<OpenLoop>> {
        let conn = self.conn.lock().map_err(|e| anyhow!("{}", e))?;
        let mut stmt = conn.prepare(
            "SELECT id, brain_id, description, priority, status, created_at FROM open_loops
             WHERE brain_id = ?1 AND status = ?2 ORDER BY priority DESC",
        )?;
        let rows = stmt.query_map(params![brain_id, status], |row| {
            Ok(OpenLoop {
                id: Some(row.get(0)?),
                brain_id: row.get(1)?,
                description: row.get(2)?,
                priority: row.get(3)?,
                status: row.get(4)?,
                created_at: row.get(5)?,
            })
        })?;
        rows.collect::<std::result::Result<Vec<_>, _>>().map_err(|e| anyhow!("{}", e))
    }

    fn open_loop_close(&self, id: i64) -> Result<()> {
        let conn = self.conn.lock().map_err(|e| anyhow!("{}", e))?;
        conn.execute("UPDATE open_loops SET status = 'closed' WHERE id = ?1", params![id])?;
        Ok(())
    }

    // ── Knowledge ──

    fn knowledge_remember(&self, entry: &KnowledgeEntry) -> Result<()> {
        let conn = self.conn.lock().map_err(|e| anyhow!("{}", e))?;
        conn.execute(
            "INSERT INTO knowledge (category, key, value, confidence, expires_at, updated_at)
             VALUES (?1, ?2, ?3, ?4, ?5, datetime('now'))
             ON CONFLICT(category, key) DO UPDATE SET
               value = excluded.value,
               confidence = excluded.confidence,
               expires_at = excluded.expires_at,
               updated_at = datetime('now')",
            params![entry.category, entry.key, entry.value, entry.confidence, entry.expires_at],
        )?;
        Ok(())
    }

    fn knowledge_recall(&self, query: &str, limit: usize) -> Result<Vec<KnowledgeEntry>> {
        let conn = self.conn.lock().map_err(|e| anyhow!("{}", e))?;
        let mut stmt = conn.prepare(
            "SELECT id, category, key, value, confidence, expires_at, updated_at FROM knowledge
             WHERE value LIKE ?1 OR key LIKE ?1 ORDER BY updated_at DESC LIMIT ?2",
        )?;
        let pattern = format!("%{}%", query);
        let rows = stmt.query_map(params![pattern, limit as i64], |row| {
            Ok(KnowledgeEntry {
                id: Some(row.get(0)?),
                category: row.get(1)?,
                key: row.get(2)?,
                value: row.get(3)?,
                confidence: row.get(4)?,
                expires_at: row.get(5)?,
                updated_at: row.get(6)?,
            })
        })?;
        rows.collect::<std::result::Result<Vec<_>, _>>().map_err(|e| anyhow!("{}", e))
    }

    fn knowledge_get(&self, category: &str, key: &str) -> Result<Option<KnowledgeEntry>> {
        let conn = self.conn.lock().map_err(|e| anyhow!("{}", e))?;
        conn.query_row(
            "SELECT id, category, key, value, confidence, expires_at, updated_at FROM knowledge
             WHERE category = ?1 AND key = ?2",
            params![category, key],
            |row| Ok(KnowledgeEntry {
                id: Some(row.get(0)?),
                category: row.get(1)?,
                key: row.get(2)?,
                value: row.get(3)?,
                confidence: row.get(4)?,
                expires_at: row.get(5)?,
                updated_at: row.get(6)?,
            }),
        ).optional().map_err(|e| anyhow!("{}", e))
    }

    fn knowledge_remove(&self, category: &str, key: &str) -> Result<()> {
        let conn = self.conn.lock().map_err(|e| anyhow!("{}", e))?;
        conn.execute("DELETE FROM knowledge WHERE category = ?1 AND key = ?2", params![category, key])?;
        Ok(())
    }

    // ── Initialization ──

    fn initialize(&self) -> Result<()> {
        let conn = self.conn.lock().map_err(|e| anyhow!("{}", e))?;
        conn.execute_batch(
            "CREATE TABLE IF NOT EXISTS nodes (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
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
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                source_id INTEGER NOT NULL REFERENCES nodes(id) ON DELETE CASCADE,
                target_id INTEGER NOT NULL REFERENCES nodes(id) ON DELETE CASCADE,
                kind TEXT NOT NULL,
                metadata TEXT,
                UNIQUE(source_id, target_id, kind)
            );
            CREATE INDEX IF NOT EXISTS idx_edges_source ON edges(source_id);
            CREATE INDEX IF NOT EXISTS idx_edges_target ON edges(target_id);

            CREATE TABLE IF NOT EXISTS brain_memories (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                brain_id TEXT NOT NULL,
                layer TEXT NOT NULL DEFAULT 'short_term',
                memory_type TEXT NOT NULL DEFAULT 'semantic',
                content TEXT NOT NULL,
                embedding BLOB,
                composite_score REAL NOT NULL DEFAULT 0.5,
                recall_count INTEGER NOT NULL DEFAULT 0,
                weights_json TEXT,
                created_at TEXT
            );
            CREATE INDEX IF NOT EXISTS idx_brain_memories_brain ON brain_memories(brain_id);
            CREATE INDEX IF NOT EXISTS idx_brain_memories_layer ON brain_memories(brain_id, layer);

            CREATE TABLE IF NOT EXISTS brain_sessions (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                brain_id TEXT NOT NULL,
                started_at TEXT,
                status TEXT NOT NULL DEFAULT 'active',
                checkpoint_json TEXT
            );
            CREATE INDEX IF NOT EXISTS idx_brain_sessions_brain ON brain_sessions(brain_id);

            CREATE TABLE IF NOT EXISTS brain_checkpoints (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                session_id INTEGER NOT NULL REFERENCES brain_sessions(id) ON DELETE CASCADE,
                checkpoint_type TEXT NOT NULL,
                content_json TEXT NOT NULL,
                created_at TEXT
            );
            CREATE INDEX IF NOT EXISTS idx_brain_checkpoints_session ON brain_checkpoints(session_id);

            CREATE TABLE IF NOT EXISTS open_loops (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                brain_id TEXT NOT NULL,
                description TEXT NOT NULL,
                priority REAL NOT NULL DEFAULT 0.5,
                status TEXT NOT NULL DEFAULT 'open',
                created_at TEXT
            );
            CREATE INDEX IF NOT EXISTS idx_open_loops_brain ON open_loops(brain_id);

            CREATE TABLE IF NOT EXISTS knowledge (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                category TEXT NOT NULL,
                key TEXT NOT NULL,
                value TEXT NOT NULL,
                confidence REAL NOT NULL DEFAULT 0.8,
                expires_at TEXT,
                updated_at TEXT,
                UNIQUE(category, key)
            );
            CREATE INDEX IF NOT EXISTS idx_knowledge_cat ON knowledge(category);
            "
        )?;
        Ok(())
    }
}
