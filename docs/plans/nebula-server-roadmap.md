# Nebula Ctx — lean-ctx Fork with Brain Memory + Postgres

## Status: Phases 0-4 Complete

| Phase | Status | Description |
|-------|--------|-------------|
| 0: Project Init | **Done** | GitHub repo, README, docs, roadmap |
| 1: Fork lean-ctx | **Done** | Source copied, renamed to nebula-ctx, Rust 1.95, build passes |
| 2: Storage Abstraction | **Done** | ContextStore trait, SqliteStore, PostgresStore (cloud-server feature) |
| 3: Brain Memory | **Done** | Scoring, activation, consolidation, ctx_brain MCP tool |
| 4: Deployment | **Done** | Dockerfile, HA addon, server setup docs |
| 5: Polish | **Pending** | Tests, postgres type fixes, upstream merge strategy |

## Context

Three existing projects with complementary strengths:
- **lean-ctx** (Rust): 42 MCP tools, token-efficient context management, SQLite FTS5, multi-agent coordination. Solid foundation.
- **Nebula RAG** (.NET): PostgreSQL + pgvector, MCP server, HA addon, hash embeddings, memory tiers.
- **dot-claw** (.NET): Brain memory system — scoring (semantic/recency/importance), consolidation via LLM, activation lifecycle, session checkpoints.

**Goal**: Fork lean-ctx into a new "Nebula Server" Rust project. Extend with Postgres persistence, server/HA-addon deployment, and dot-claw's brain memory features. Pull upstream changes from lean-ctx regularly.

**Decisions**:
- **Rust** — fork lean-ctx directly. Learning Rust is part of the goal.
- **Brain memory only** from dot-claw — scoring, consolidation, activation, tiers.
- **Extend lean-ctx's dashboard** — TUI + ctx_insight web dashboard, add brain memory views when needed.

## Feasibility: HIGH

lean-ctx already has 80% of what's needed:

| Need | Status in lean-ctx | Work Required |
|------|-------------------|---------------|
| MCP HTTP server | **Done** — Axum, rate limiting, auth, `/v1/tools/call` | None |
| PostgreSQL pool | **Done** — `cloud_server/db.rs`, Deadpool Postgres | Extend schema for context data |
| 42 MCP tools | **Done** — all tools work via stdio and HTTP | None |
| Token counting | **Done** — tiktoken-rs | None |
| Multi-agent coordination | **Done** — registry, tasks, ledger, diaries | None |
| Full-text search | **Done** — SQLite FTS5 + BM25 + hybrid | Abstract for Postgres |
| Compression | **Done** — 10 modes, Thompson Sampling | None |
| Dependency graph | **Done** — tree-sitter, property graph | Abstract for Postgres |
| TUI/Dashboard | **Done** — terminal UI + ctx_insight web | Extend with brain views |
| Context data in Postgres | **Missing** — only cloud sync data in PG | New: trait + Postgres impl |
| Brain memory system | **Missing** | New: port from dot-claw |
| HA addon packaging | **Missing** | New: Dockerfile + config |

## Architecture: Storage Abstraction

lean-ctx uses SQLite directly (no abstraction). Need a trait layer to support both backends.

```rust
// rust/src/core/store/mod.rs
#[async_trait]
trait ContextStore: Send + Sync {
    // Cache
    async fn cache_get(&self, key: &str) -> Result<Option<String>>;
    async fn cache_set(&self, key: &str, value: &str, ttl: Option<Duration>) -> Result<()>;

    // Search
    async fn search(&self, query: &str, limit: usize) -> Result<Vec<SearchResult>>;
    async fn index_content(&self, id: &str, content: &str, metadata: &str) -> Result<()>;

    // Property Graph
    async fn graph_upsert_node(&self, node: &Node) -> Result<()>;
    async fn graph_query(&self, path: &str, depth: usize) -> Result<Vec<Node>>;

    // Knowledge
    async fn knowledge_remember(&self, entry: &KnowledgeEntry) -> Result<()>;
    async fn knowledge_recall(&self, query: &str, limit: usize) -> Result<Vec<KnowledgeEntry>>;

    // Sessions
    async fn session_save(&self, session: &Session) -> Result<()>;
    async fn session_load(&self, id: &str) -> Result<Option<Session>>;

    // Brain Memory (new)
    async fn brain_store(&self, memory: &BrainMemory) -> Result<()>;
    async fn brain_recall(&self, query: &str, layer: MemoryLayer, limit: usize) -> Result<Vec<BrainMemory>>;
    async fn brain_update_score(&self, id: &str, score: f64) -> Result<()>;
    async fn brain_checkpoint(&self, checkpoint: &BrainCheckpoint) -> Result<()>;
}
```

Two implementations:
- `SqliteStore` — wraps existing rusqlite code. Default for local dev.
- `PostgresStore` — new, uses Deadpool. Default for server/HA.

Config: `--store sqlite|postgres` or env `NEBULA_STORE=postgres`.

## Implementation Phases

### Phase 0: Project Init
- Create `/home/mark/projects/personal/nebula-server` directory
- `git init` + initial commit
- Create GitHub repo + push
- Copy plan to `docs/plans/nebula-server-roadmap.md`
- Create `README.md` with project overview, goals, architecture summary
- Add `.gitignore` for Rust project

### Phase 1: Fork & Wire Up
- Fork lean-ctx repo → `nebula-server`
- Add upstream remote for pulling lean-ctx updates
- Rename crate in Cargo.toml
- Ensure HTTP server builds and all 42 tools respond
- **Verify**: `cargo build` + `curl localhost:8099/v1/tools`

### Phase 2: Storage Trait + Postgres Backend
- Create `ContextStore` trait in `rust/src/core/store/`
- Wrap existing SQLite code into `SqliteStore` impl
- Build `PostgresStore` with migration system:
  - `context_cache` — key/value with TTL
  - `search_chunks` — content + embeddings (pgvector)
  - `graph_nodes`, `graph_edges` — property graph
  - `knowledge_entries` — category/key/value with confidence + expiry
  - `brain_memories` — content, embedding, layer, type, composite_score, recall_count, weights_json
  - `brain_sessions` — brain_id, started_at, status, checkpoint_json
  - `brain_checkpoints` — session_id, type, content_json, created_at
  - `open_loops` — brain_id, description, priority, status, created_at
- Add CLI flag + env var for store selection
- **Verify**: Run with both `--store sqlite` and `--store postgres`; all 42 tools work identically

### Phase 3: Brain Memory System (from dot-claw)
Port dot-claw's brain memory to Rust idioms:

**Data models** (`rust/src/core/brain/models.rs`):
- `MemoryLayer` enum: ShortTerm, LongTerm
- `MemoryType` enum: Episodic, Semantic, Procedural
- `BrainScoringWeights`: semantic/recency/importance/confidence/open_loop weights
- `BrainMemory`: id, brain_id, layer, type, content, embedding, composite_score, recall_count, created_at
- `BrainSession`: id, brain_id, started_at, checkpoint
- `ActivationPacket`: memories + open_loops + checkpoint for session warm-up

**Scoring service** (`rust/src/core/brain/scoring.rs`):
- Recency decay: `exp(-0.231 * days)` short-term, `exp(-0.0077 * days)` long-term
- Composite: `semantic * w1 + recency * w2 + importance * w3 + confidence * w4 + open_loop * w5`
- Configurable weights per brain (stored in knowledge)

**Consolidation service** (`rust/src/core/brain/consolidation.rs`):
- At session end: LLM call extracts memories + open loops from session context
- Auto-promotion: short_term → long_term after N recalls (threshold configurable)
- Deduplication: semantic similarity check before storing

**Activation service** (`rust/src/core/brain/activation.rs`):
- On session start: score-weighted recall of relevant memories
- Recall open loops + latest checkpoint
- Return `ActivationPacket` as MCP tool result

**New MCP tools** (`rust/src/tools/brain_*.rs`):
- `brain_store` — store a memory with auto-scoring
- `brain_recall` — recall memories with scoring + decay
- `brain_consolidate` — extract memories from session text
- `brain_activate` — warm-up a new session
- `brain_checkpoint` — save/restore session state
- `brain_status` — show memory stats, open loops, tiers

**Verify**: Store → recall → consolidate → activate cycle works via MCP HTTP

### Phase 4: Deployment
- Multi-stage Dockerfile (build → scratch/alpine runtime)
- Home Assistant addon:
  - `config.yaml` with options for Postgres URL, MCP port, auth token
  - `run.sh` entrypoint
- Extend `ctx_insight` dashboard with brain memory panel
- **Verify**: Docker build + HA addon install + connect from Claude Code

### Phase 5: Polish
- Integration tests for both store backends
- Upstream merge strategy documented (rebase from lean-ctx)
- README: setup, config, brain memory usage

## Critical Files

### New Files
```
rust/src/core/store/mod.rs          — ContextStore trait
rust/src/core/store/sqlite.rs       — SQLite impl (wraps existing)
rust/src/core/store/postgres.rs     — Postgres impl
rust/src/core/store/migrations/     — SQL migration files (.sql)
rust/src/core/brain/mod.rs          — Brain memory module
rust/src/core/brain/models.rs       — Data structures
rust/src/core/brain/scoring.rs      — Scoring service
rust/src/core/brain/consolidation.rs — Consolidation service
rust/src/core/brain/activation.rs   — Activation service
rust/src/tools/brain_store.rs       — MCP tool
rust/src/tools/brain_recall.rs      — MCP tool
rust/src/tools/brain_consolidate.rs — MCP tool
rust/src/tools/brain_activate.rs    — MCP tool
rust/src/tools/brain_checkpoint.rs  — MCP tool
rust/src/tools/brain_status.rs      — MCP tool
Dockerfile
homeassistant/config.yaml
homeassistant/run.sh
```

### Modified Files (from lean-ctx)
```
rust/Cargo.toml                      — pgvector, deadpool-postgres deps
rust/src/core/mod.rs                  — Wire store + brain modules
rust/src/core/cache.rs                — Use ContextStore trait
rust/src/core/knowledge.rs            — Use ContextStore trait
rust/src/core/session.rs              — Use ContextStore trait
rust/src/core/property_graph/*.rs     — Use ContextStore trait
rust/src/http_server/mod.rs           — Register brain tools
rust/src/lib.rs                       — Register new tools + store init
```

## Risks

| Risk | Mitigation |
|------|-----------|
| SQLite→Postgres abstraction leaks | Trait is narrow; test both backends per tool |
| Rust learning curve | lean-ctx code is working reference; adapt its patterns |
| lean-ctx upstream merge conflicts | Keep modifications behind trait boundary; upstream changes stay in core |
| Embedding quality (hash-based) | Start with hash; add pgvector/ONNX later as enhancement |
| dot-claw→Rust port fidelity | Port concepts + formulas, not .NET idioms |

## Verification Plan

1. **Phase 1**: `cargo build` + `curl /v1/tools` returns all 42 tools
2. **Phase 2**: Switch between `--store sqlite` and `--store postgres`; identical results for read/search/cache/knowledge
3. **Phase 3**: Full brain cycle — store 10 memories → recall top 3 → consolidate session → activate → verify warm-up
4. **Phase 4**: `docker build` + HA addon install + Claude Code MCP connection
5. **End-to-end**: Agent session with brain activation, context caching, cross-session memory recall
