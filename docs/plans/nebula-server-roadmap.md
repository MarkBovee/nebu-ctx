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
| 6: Data Import | **Done** | ctx_import MCP tool — bulk import from nebula-rag/lean-ctx/JSON, dedup, dry-run, 7 tests |

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

#### Phase 4a: HA Addon Polish + Dashboard Integration

**Current state**: Basic HA addon exists with 4 config options. Dashboard (`ctx_insight`) is a built-in web UI with compression demos, BM25 index, symbol browser, heatmap, and agents view. Runs as `nebula-ctx dashboard`.

**HA addon config expansion** (`homeassistant/config.yaml`):

```yaml
options:
  # Store backend
  store: "postgres"                          # sqlite or postgres
  database_url: ""                           # postgres://user:pass@host:5432/db
  auth_token: ""                             # MCP HTTP auth token

  # Server
  mcp_port: 8099                             # MCP HTTP endpoint port
  dashboard_port: 4747                       # Web dashboard port (ctx_insight)
  dashboard_enabled: true                    # Enable web dashboard
  log_level: "info"                          # debug|info|warn|error

  # Brain memory
  brain_auto_consolidate: true               # Auto-consolidate at session end
  brain_default_layer: "short_term"          # Default memory layer
  brain_max_memories: 1000                   # Max memories per brain

  # Ingress
  ingress_enabled: true                      # Enable HA Ingress for dashboard
  ingress_port: 4747                         # HA Ingress target port
  ingress_stream: true                       # Stream responses
  ingress_entry: "index.html"                # Dashboard entry point

schema:
  store: "list(sqlite|postgres)"
  database_url: "password?"
  auth_token: "password?"
  mcp_port: "int"
  dashboard_port: "int"
  dashboard_enabled: "bool"
  log_level: "list(debug|info|warn|error)"
  brain_auto_consolidate: "bool"
  brain_default_layer: "list(short_term|long_term)"
  brain_max_memories: "int"
  ingress_enabled: "bool"
  ingress_port: "int"
  ingress_stream: "bool"
  ingress_entry: "str"
```

**Dashboard integration with HA**:

The existing `ctx_insight` dashboard (single-page web app in `src/dashboard/`) provides:
- Compression mode comparison (10 modes)
- BM25 index browser
- Symbol search (tree-sitter based)
- File heatmap visualization
- Multi-agent status view
- Token savings metrics

New panels to add:
- **Brain Memory**: list/recall/search memories, show layers, scores, recall counts
- **Knowledge Browser**: category/key/value grid, timeline view, contradiction alerts
- **Import Status**: show imported data sources, counts, last import timestamp
- **Store Backend**: show current store (sqlite/postgres), connection status, table stats

**HA Ingress**: Dashboard served at `{ha_url}/api/hassio_ingress/nebula-ctx/` — requires:
1. `ingress: true` in config.yaml
2. Dashboard listens on configured port (default 4747)
3. `run.sh` starts both MCP server AND dashboard:
   ```bash
   nebula-ctx serve &          # MCP HTTP on 8099
   nebula-ctx dashboard        # Web UI on 4747
   ```
4. Both share the same store backend

**`run.sh` updates**:
- Start MCP server + dashboard concurrently
- Pass `NEBULA_STORE` env to both processes
- Health check on both ports
- Graceful shutdown via trap

**Files to modify**:
```
homeassistant/config.yaml           — expanded options + ingress
homeassistant/run.sh                 — dual-process, HA ingress support
src/dashboard/mod.rs                 — add brain/knowledge/import API routes
src/dashboard/dashboard.html         — add brain/knowledge/import panels
Dockerfile                           — expose both ports (8099 + 4747)
```

**Verify**: HA addon install → Ingress opens dashboard → Brain memory panel shows data → MCP tools work from Claude Code → Both use same Postgres backend.

### Phase 6: Data Import & Migration

Unified import system to migrate data from nebula-rag (.NET/Postgres) and lean-ctx (local/SQLite) into nebula-ctx cloud (Postgres).

**Import sources:**

| Source | Data | Format |
|--------|------|--------|
| nebula-rag memories | Episodic/semantic/procedural memories with tags, project, tier | PostgreSQL → JSON export via MCP `memory list` |
| nebula-rag RAG chunks | Indexed source documents with embeddings | PostgreSQL → JSON export via MCP `rag_sources list` + `rag_query` |
| lean-ctx knowledge | Category/key/value facts with confidence | SQLite → JSON export via MCP `ctx_knowledge status` |
| lean-ctx sessions | Session snapshots, diaries, handoffs | SQLite → JSON export via MCP `ctx_session list` |
| lean-ctx cache | Cached file reads, search index | SQLite → direct DB copy or rebuild |

**Implementation — CLI subcommand `nebula-ctx import`:**

```
nebula-ctx import --from <source> --to <target> [options]

Sources:
  --from nebula-rag     Import from nebula-rag Postgres (via MCP or direct DB)
  --from lean-ctx       Import from local lean-ctx SQLite

Targets:
  --to postgres         Write to nebula-ctx Postgres (cloud)
  --to sqlite           Write to nebula-ctx SQLite (local)

Options:
  --scope memories      Import memories/knowledge only
  --scope rag           Import RAG sources/chunks only
  --scope all           Import everything (default)
  --project <id>        Filter to specific project
  --dry-run             Show what would be imported without writing
  --batch-size <n>      Rows per batch (default: 100)
```

**Data mapping:**

| nebula-rag field | nebula-ctx field | Transform |
|------------------|------------------|-----------|
| memory.type (semantic/episodic/procedural) | knowledge.category | Direct map or tag-based categorization |
| memory.content | knowledge.value | Direct |
| memory.tags | knowledge.tags | Direct |
| memory.projectId | knowledge.key prefix | `{projectId}-{slug}` |
| memory.tier (short_term/long_term) | knowledge.confidence | short_term=60%, long_term=80% |
| rag chunk + embedding | search_chunks table | Direct if same pgvector dims, re-embed if not |

| lean-ctx field | nebula-ctx field | Transform |
|----------------|------------------|-----------|
| knowledge.category | knowledge.category | Direct |
| knowledge.key | knowledge.key | Direct |
| knowledge.value | knowledge.value | Direct |
| knowledge.confidence | knowledge.confidence | Direct |
| session data | brain_sessions | Map to brain session format |
| cache entries | context_cache | Direct key/value copy |

**Import flow:**

1. **Extract**: Read from source (MCP tools or direct DB query)
2. **Validate**: Check schema compatibility, embedding dimensions, required fields
3. **Transform**: Map fields, normalize categories, generate keys for entries without them
4. **Deduplicate**: Check existing entries by key/content hash, skip or merge
5. **Load**: Batch insert into target store via ContextStore trait methods
6. **Report**: Print counts (imported/skipped/merged per category)

**MCP tool — `ctx_import`:**

For remote/cloud imports where CLI isn't available:

```json
{
  "name": "ctx_import",
  "description": "Import data from external sources into nebula-ctx",
  "inputSchema": {
    "source": { "enum": ["nebula-rag", "lean-ctx-local", "json-file"] },
    "scope": { "enum": ["memories", "rag", "sessions", "all"] },
    "projectFilter": { "type": "string" },
    "data": { "type": "string", "description": "JSON payload (for json-file source)" },
    "dryRun": { "type": "boolean" }
  }
}
```

**JSON interchange format** (for file-based or MCP-based import):

```json
{
  "version": 1,
  "exportedAt": "2026-04-20T16:00:00Z",
  "source": "nebula-rag",
  "memories": [
    {
      "key": "arc-runner-dind-setup",
      "value": "...",
      "category": "architecture",
      "tags": ["arc", "dind", "kubernetes"],
      "project": null,
      "confidence": 0.8,
      "createdAt": "2026-04-14T14:23:53Z"
    }
  ],
  "knowledge": [...],
  "ragSources": [
    {
      "path": "accentry-miep/ci-cd-pipeline",
      "chunks": ["..."],
      "indexedAt": "2026-04-14T14:25:02Z"
    }
  ]
}
```

**Verify**: Import test data from nebula-rag (48 memories + 3 RAG sources) → verify recall returns same results from both SQLite and Postgres backends.

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

### New Files (Phase 6: Import)
```
src/tools/ctx_import.rs             — MCP tool for data import
src/core/import/mod.rs              — Import orchestration module
src/core/import/nebula_rag.rs       — nebula-rag source adapter
src/core/import/lean_ctx_local.rs   — lean-ctx SQLite source adapter
src/core/import/json_file.rs        — JSON file source adapter
src/core/import/transforms.rs       — Field mapping + dedup logic
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
5. **Phase 6**: Import nebula-rag 48 memories + 3 RAG sources → verify recall returns same results from Postgres backend
6. **End-to-end**: Agent session with brain activation, context caching, cross-session memory recall
