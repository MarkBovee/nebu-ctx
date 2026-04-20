# Nebula Server — Technical Architecture

> This document explains how the code works, module by module, for someone learning Rust. Based on the lean-ctx foundation.

## Table of Contents

- [How It Starts](#how-it-starts)
- [Module Structure](#module-structure)
- [MCP Tool System](#mcp-tool-system)
- [HTTP Server](#http-server)
- [Core Engine](#core-engine)
- [Storage Layer](#storage-layer)
- [Key Rust Patterns](#key-rust-patterns)
- [Data Flow Examples](#data-flow-examples)

---

## How It Starts

`main.rs` is the binary entry point. It decides what mode to run in based on CLI arguments.

```rust
fn main() {
    // 1. Install a custom panic hook for better crash messages
    std::panic::set_hook(Box::new(|info| {
        eprintln!("FATAL: {}", info);
    }));

    // 2. Read command-line arguments
    let args: Vec<String> = std::env::args().collect();

    // 3. Route to the right mode
    if args.len() > 1 {
        match args[1].as_str() {
            "serve" => { /* Start HTTP server */ },
            "mcp"   => { /* Fall through to stdio MCP */ },
            other   => { /* Handle CLI commands like "compress", "stats" */ },
        }
    }

    // 4. Default: start as MCP server over stdio
    run_mcp_server();
}
```

**Three modes:**
1. **Stdio MCP** — reads JSON-RPC from stdin, writes to stdout. Used by IDEs and Claude Code.
2. **HTTP MCP** (`serve`) — listens on a port, accepts JSON-RPC over HTTP. Used for remote/server deployment.
3. **CLI commands** — one-shot operations like `compress`, `stats`, `benchmark`.

### Rust concept: `std::env::args()`
Returns an iterator of command-line arguments. `args[0]` is the program name, `args[1]` is the first real argument. The `match` statement is Rust's pattern matching — like a switch statement but exhaustive.

---

## Module Structure

`lib.rs` exports the library. Each `pub mod` is a separate file or directory:

```
src/
├── main.rs              # Binary entry point
├── lib.rs               # Library exports
├── tools/               # MCP tool implementations
│   ├── ctx_read.rs      # File reading with compression
│   ├── ctx_search.rs    # Full-text search
│   ├── ctx_shell.rs     # Shell output compression
│   ├── ctx_knowledge.rs # Persistent knowledge store
│   ├── ctx_session.rs   # Cross-session state
│   ├── ctx_agent.rs     # Multi-agent coordination
│   ├── ctx_brain.rs     # Brain memory tool (store/recall/consolidate/activate)
│   └── ...              # More tools
├── core/                # Business logic
│   ├── cache.rs         # LRU cache with RRF eviction
│   ├── session.rs       # Session state management
│   ├── compressor.rs    # 10 compression modes
│   ├── knowledge.rs     # Knowledge base (SQLite FTS5)
│   ├── property_graph/  # Code dependency graph
│   ├── store/           # Storage abstraction layer
│   │   ├── mod.rs       # ContextStore trait + data models
│   │   ├── sqlite.rs    # SQLite implementation (local dev default)
│   │   └── postgres.rs  # PostgreSQL implementation (server/HA default)
│   ├── brain/           # Brain memory system
│   │   ├── mod.rs       # Module exports + data models
│   │   ├── scoring.rs   # Recency decay + composite scoring
│   │   ├── activation.rs # Session warm-up service
│   │   └── consolidation.rs # Memory extraction + promotion
│   └── ...
├── server/              # MCP server dispatch
│   ├── dispatch.rs      # Tool routing (match on tool name)
│   └── ...
├── tool_defs/           # Tool definitions (JSON schema)
│   └── granular.rs      # All tool definitions + descriptions
├── http_server/         # Axum HTTP server (optional feature)
├── cloud_server/        # PostgreSQL cloud sync (optional feature)
└── mcp_stdio/           # Stdio MCP transport
```

### Rust concept: `pub mod`
`mod` declares a module (file or directory). `pub` makes it public. The compiler looks for `foo.rs` or `foo/mod.rs`. Modules control visibility — private items can't be used outside their module.

---

## MCP Tool System

### What is MCP?

Model Context Protocol (MCP) is how AI agents talk to tools. It's JSON-RPC:
- Client sends: `{"method": "tools/call", "params": {"name": "ctx_read", "arguments": {"path": "/foo.rs"}}}`
- Server responds: `{"result": {"content": [{"type": "text", "text": "file contents here"}]}}`

### How Tools Are Registered

The `rmcp` crate provides the MCP server framework. Each tool is a function that the `LeanCtxServer` exposes:

```rust
// The main server struct holds all shared state
pub struct LeanCtxServer {
    pub cache: Arc<RwLock<SessionCache>>,
    pub session: Arc<RwLock<SessionState>>,
    pub knowledge: Arc<RwLock<KnowledgeStore>>,
    pub tool_calls: Arc<RwLock<Vec<ToolCallRecord>>>,
}

// The server implements rmcp's ServerHandler trait
impl ServerHandler for LeanCtxServer {
    fn list_tools(&self) -> Vec<Tool> {
        // Returns all 42 tool definitions (name, description, JSON schema)
    }

    async fn call_tool(&self, name: &str, args: Value) -> Result<CallToolResult> {
        // Routes tool name to the right handler function
        match name {
            "ctx_read"   => tools::ctx_read::handle(&mut cache, args).await,
            "ctx_search" => tools::ctx_search::handle(&mut cache, args).await,
            // ... 40 more
        }
    }
}
```

### Tool Example: `ctx_read` End-to-End

When Claude calls `ctx_read(path="/src/main.rs", mode="map")`:

```
1. MCP client sends JSON-RPC request
       │
2. HTTP server (or stdio) receives it
       │
3. LeanCtxServer.call_tool("ctx_read", {path, mode})
       │
4. Route to tools::ctx_read::handle()
       │
   ┌───▼──────────────────────────────┐
   │ a. Check SessionCache for hit     │  ← Cache avoids re-reading files
   │ b. If miss: read file from disk   │
   │ c. Store in cache                 │
   │ d. Apply compression mode         │
   │    - "full": return as-is         │
   │    - "map": extract deps/exports  │  ← Uses tree-sitter for AST
   │    - "signatures": fn signatures  │
   │    - "aggressive": strip syntax   │
   │ e. Track tokens saved             │
   └───┬──────────────────────────────┘
       │
5. Return compressed content as MCP result
```

The handler function looks roughly like:

```rust
pub async fn handle(
    cache: &Arc<RwLock<SessionCache>>,
    args: Value,
) -> Result<CallToolResult> {
    // Parse arguments from JSON
    let path = args["path"].as_str().ok_or("missing path")?;
    let mode = args["mode"].as_str().unwrap_or("full");

    // Check cache first (re-reads cost ~13 tokens instead of thousands)
    let mut cache = cache.write().await;  // ← RwLock: multiple readers OR one writer
    if let Some(entry) = cache.get(path) {
        return Ok(compress(&entry.content, mode));
    }

    // Read from filesystem
    let content = fs::read_to_string(path)
        .map_err(|e| format!("Cannot read {}: {}", path, e))?;

    // Cache it
    cache.store(path, content.clone());

    // Compress and return
    Ok(compress(&content, mode))
}
```

### Rust concepts used here

| Pattern | What it does | Why |
|---------|-------------|-----|
| `Arc<RwLock<T>>` | Shared mutable state across async tasks | Multiple tools read the cache concurrently; writes are exclusive |
| `Result<T, E>` | Success or error return | No exceptions. Every fallible operation returns Result. |
| `.await` | Wait for async operation | Rust async doesn't run implicitly — you must `.await` each step |
| `match` | Pattern matching | Routes tool names to handlers, handles variants of enums |
| `&str` vs `String` | Borrowed vs owned string | `&str` is a view into text (cheap). `String` owns its heap allocation. |

---

## HTTP Server

The HTTP server wraps the MCP server behind Axum (a Rust web framework):

```rust
// http_server/mod.rs
use axum::{Router, routing::post, middleware};

pub async fn serve(config: HttpServerConfig) -> Result<()> {
    // Shared state — the same LeanCtxServer handles all requests
    let state = AppState {
        server: Arc::new(LeanCtxServer::new()),
    };

    // Build the router with middleware stack
    let app = Router::new()
        .route("/health", get(health_check))
        .route("/v1/tools", get(list_tools))
        .route("/v1/tools/call", post(call_tool))
        // Middleware runs bottom-up:
        .layer(middleware::from_fn(rate_limiter))  // 3. Rate limiting
        .layer(middleware::from_fn(auth_check))     // 2. Authentication
        .layer(middleware::from_fn(request_log))    // 1. Request logging
        .with_state(state);

    // Bind to address and serve
    let addr = SocketAddr::from(([0, 0, 0, 0], config.port));
    axum::serve(tcp_listener, app).await?;
    Ok(())
}
```

### Request Flow

```
Client → HTTP Request
  → request_log middleware (logs method + path)
  → auth_check middleware (validates Bearer token)
  → rate_limiter middleware (checks RPS limit)
  → Route handler (list_tools or call_tool)
  → LeanCtxServer (shared via Arc)
  → Tool handler function
  → Result → HTTP Response
```

### Rust concept: Axum
Axum uses Rust's type system for routing. `.route("/path", get(handler))` maps GET requests to `handler`. The `State` extractor passes shared state to handlers. Middleware layers compose like a stack — last added runs first.

---

## Core Engine

### Cache (`core/cache.rs`)

The cache is the heart of the system. It stores file contents to avoid re-reading and re-processing:

```rust
pub struct CacheEntry {
    pub content: String,           // File content (or compressed version)
    pub hash: String,              // SHA-256 hash for change detection
    pub line_count: usize,         // For line-range operations
    pub original_tokens: usize,    // Tokens before compression
    pub compressed_tokens: usize,  // Tokens after compression
    pub read_count: u32,           // How many times accessed
    pub last_access: Instant,      // For eviction priority
}

pub struct SessionCache {
    entries: HashMap<String, CacheEntry>,  // path → content
    total_tokens: usize,                   // Current token budget used
    max_tokens: usize,                     // Budget limit (default 500K)
}
```

**Eviction**: When the token budget is exceeded, entries are ranked using Reciprocal Rank Fusion (RRF):

```rust
fn eviction_score(entry: &CacheEntry) -> f64 {
    let recency = 1.0 / (1.0 + entry.last_access.elapsed().as_secs() as f64);
    let frequency = entry.read_count as f64;
    let efficiency = entry.compressed_tokens as f64 / entry.original_tokens.max(1) as f64;
    // RRF combines these signals
    1.0 / (1.0 + recency_rank) + 1.0 / (1.0 + frequency_rank)
}
```

Lowest score gets evicted first.

### Compression (`core/compressor.rs`)

10 compression modes transform file content to save tokens:

| Mode | What it does | Use case |
|------|-------------|----------|
| `full` | Return content as-is | Files you plan to edit |
| `map` | Extract imports, exports, types | Context-only files |
| `signatures` | Function/class signatures only | API surface exploration |
| `diff` | Only changed lines | After editing |
| `aggressive` | Strip syntax, keep identifiers | Maximum token savings |
| `entropy` | High-information fragments | Dense code analysis |
| `task` | Filter by relevance to current task | Focused context |
| `reference` | Minimal cache keys | Cross-file references |
| `lines:N-M` | Specific line range | Targeted reading |
| `auto` | Thompson Sampling selects best | Default mode |

**Thompson Sampling** (`auto` mode): Tracks which mode produces the best compression ratio for each file type. Uses a probabilistic model to balance exploration (try new modes) vs exploitation (use known-good modes):

```rust
struct ModeStats {
    alpha: f64,  // Success count
    beta: f64,   // Failure count
}

fn thompson_sample(stats: &[(f64, f64); 10]) -> usize {
    // Sample from Beta distribution for each mode
    let samples: Vec<f64> = stats.iter()
        .map(|(a, b)| beta_sample(*a, *b))
        .collect();
    // Pick mode with highest sample
    samples.iter().enumerate().max_by(|(_, a), (_, b)| a.partial_cmp(b).unwrap()).0
}
```

### Knowledge Store (`core/knowledge.rs`)

Persistent facts stored in SQLite FTS5 (full-text search):

```rust
pub struct KnowledgeEntry {
    pub category: String,    // "architecture", "api", "conventions"
    pub key: String,         // "auth-method", "db-engine"
    pub value: String,       // The actual fact
    pub confidence: f64,     // 0.0-1.0
    pub updated_at: DateTime<Utc>,
}
```

Uses SQLite's FTS5 virtual table for fast text search:

```sql
CREATE VIRTUAL TABLE knowledge USING fts5(
    category, key, value,
    content='knowledge_entries',
    tokenize='porter unicode61'
);
```

### Rust concept: `HashMap`
`HashMap<K, V>` is an unordered key-value store. `cache.entries` maps file paths to cached content. Lookup is O(1) on average.

---

## Storage Layer

Nebula Server uses a trait-based storage abstraction so tools don't know (or care) whether data lives in SQLite or Postgres.

### ContextStore Trait (`core/store/mod.rs`)

```rust
pub trait ContextStore: Send + Sync {
    // Lifecycle
    fn initialize(&self) -> Result<()>;

    // Brain memory
    fn brain_store(&self, memory: &BrainMemory) -> Result<i64>;
    fn brain_recall(&self, brain_id: &str, query: &str, layer: &str, limit: usize) -> Result<Vec<BrainMemory>>;
    fn brain_update_score(&self, id: i64, score: f64) -> Result<()>;
    fn brain_increment_recall(&self, id: i64) -> Result<()>;

    // Sessions
    fn brain_session_create(&self, brain_id: &str) -> Result<i64>;
    fn brain_session_get(&self, id: i64) -> Result<Option<BrainSession>>;
    fn brain_session_update_status(&self, id: i64, status: &str) -> Result<()>;
    fn brain_session_update_checkpoint(&self, id: i64, checkpoint_json: &str) -> Result<()>;
    fn brain_session_latest(&self, brain_id: &str) -> Result<Option<BrainSession>>;

    // Checkpoints
    fn brain_checkpoint_store(&self, checkpoint: &BrainCheckpoint) -> Result<i64>;
    fn brain_checkpoint_latest(&self, session_id: i64) -> Result<Option<BrainCheckpoint>>;

    // Open loops
    fn open_loop_store(&self, item: &OpenLoop) -> Result<i64>;
    fn open_loop_list(&self, brain_id: &str, status: &str) -> Result<Vec<OpenLoop>>;
    fn open_loop_close(&self, id: i64) -> Result<()>;

    // Knowledge
    fn knowledge_remember(&self, entry: &KnowledgeEntry) -> Result<()>;
    fn knowledge_recall(&self, query: &str, limit: usize) -> Result<Vec<KnowledgeEntry>>;
    // ... more
}
```

### SqliteStore (`core/store/sqlite.rs`)

Default for local development. Wraps `rusqlite` behind a `Mutex<Connection>`:

```rust
pub struct SqliteStore {
    conn: Mutex<Connection>,
}

impl SqliteStore {
    pub fn open(path: &Path) -> Result<Self> {
        let conn = Connection::open(path)?;
        conn.execute_batch("PRAGMA journal_mode=WAL; PRAGMA foreign_keys=ON;")?;
        Ok(Self { conn: Mutex::new(conn) })
    }
}
```

Schema tables: `brain_memories`, `brain_sessions`, `brain_checkpoints`, `open_loops`, `knowledge_entries`.

### PostgresStore (`core/store/postgres.rs`)

Default for server/HA deployment. Uses `deadpool-postgres`:

```rust
pub struct PostgresStore {
    pool: Pool,
}

impl PostgresStore {
    pub async fn open(database_url: &str) -> Result<Self> {
        let pg_config = database_url.parse::<tokio_postgres::Config>()?;
        let mgr = Manager::new(pg_config, NoTls);
        let pool = Pool::builder(mgr).max_size(16).build()?;
        Ok(Self { pool })
    }
}
```

Note: PostgresStore methods use `async` internally (pool.get().await) but the trait is sync — each call spawns a blocking task via `tokio::task::block_in_place`.

### Data Models

```rust
// A single memory in the brain system
pub struct BrainMemory {
    pub id: Option<i64>,
    pub brain_id: String,         // Scope: "project-x", "user-mark"
    pub layer: String,            // "short_term" | "long_term"
    pub memory_type: String,      // "episodic" | "semantic" | "procedural"
    pub content: String,
    pub embedding: Option<Vec<f32>>,
    pub composite_score: f64,     // Weighted score for ranking
    pub recall_count: i32,
    pub weights_json: Option<String>,
    pub created_at: Option<String>,
}

// A brain session (starts when agent connects, ends on disconnect)
pub struct BrainSession {
    pub id: Option<i64>,
    pub brain_id: String,
    pub started_at: Option<String>,
    pub status: String,           // "active" | "ended"
    pub checkpoint_json: Option<String>,
}

// A saved checkpoint within a session
pub struct BrainCheckpoint {
    pub id: Option<i64>,
    pub session_id: i64,
    pub checkpoint_type: String,  // "manual" | "auto"
    pub content_json: String,
    pub created_at: Option<String>,
}

// An unresolved task or question
pub struct OpenLoop {
    pub id: Option<i64>,
    pub brain_id: String,
    pub description: String,
    pub priority: f64,
    pub status: String,           // "open" | "closed"
    pub created_at: Option<String>,
}
```

### Rust concept: Traits
Traits are like interfaces in C#/Java. `trait ContextStore` defines what methods a type must implement. Any type that implements `ContextStore` can be used wherever a store is needed. Both `SqliteStore` and `PostgresStore` implement the same trait, so tool code doesn't change when switching backends.

---

## Key Rust Patterns

### Error Handling: `Result<T, E>`

Rust has no exceptions. Every fallible operation returns `Result`:

```rust
// Ok(value) for success, Err(error) for failure
fn read_config(path: &str) -> Result<Config, io::Error> {
    let content = fs::read_to_string(path)?;  // ? operator: return early on error
    Ok(parse_config(&content))
}
```

The `?` operator is Rust's error propagation — if the expression returns `Err`, the function returns immediately with that error. No try/catch needed.

### Shared State: `Arc<RwLock<T>>`

- `Arc` (Atomic Reference Counted) — shared ownership across threads/tasks
- `RwLock` (Read-Write Lock) — multiple concurrent readers OR one exclusive writer
- Combined: safe shared mutable state in async code

```rust
let cache: Arc<RwLock<SessionCache>> = Arc::new(RwLock::new(SessionCache::new()));

// Reading (many tasks can read simultaneously)
let cache_read = cache.read().await;

// Writing (exclusive access)
let mut cache_write = cache.write().await;
cache_write.store(path, content);
```

### Enums and Pattern Matching

Rust enums can carry data:

```rust
enum CompressionMode {
    Full,
    Map,
    Signatures,
    Lines { start: usize, end: usize },  // Variant with data
}

match mode {
    CompressionMode::Full => content.clone(),
    CompressionMode::Map => extract_map(&content),
    CompressionMode::Lines { start, end } => extract_lines(&content, start, end),
    _ => compress_default(&content),
}
```

### Async/Await

Rust's async is zero-cost — no hidden heap allocations or runtime threads:

```rust
async fn fetch_url(url: &str) -> Result<String> {
    let response = reqwest::get(url).await?;       // .await pauses until done
    let body = response.text().await?;
    Ok(body)
}
```

The `tokio` runtime drives all async operations. It's a work-stealing scheduler similar to .NET's ThreadPool.

---

## Data Flow Examples

### Full request cycle: "Read this file and compress it"

```
Claude Code                     Nebula Server
    │                                │
    │  POST /v1/tools/call           │
    │  {name: "ctx_read",            │
    │   args: {path: "src/lib.rs",   │
    │          mode: "map"}}          │
    │───────────────────────────────►│
    │                                │ 1. auth_check middleware
    │                                │ 2. rate_limiter middleware
    │                                │ 3. call_tool handler
    │                                │ 4. LeanCtxServer.call_tool()
    │                                │ 5. cache.write().await
    │                                │ 6. cache.get("src/lib.rs")
    │                                │    → Miss: read from disk
    │                                │ 7. fs::read_to_string()
    │                                │ 8. cache.store()
    │                                │ 9. compress_mode("map")
    │                                │    → tree-sitter parses AST
    │                                │    → extract imports, exports, types
    │                                │ 10. Return compressed result
    │  {result: {content:            │
    │    "pub mod cache;             │
    │     pub mod session;           │
    │     use tokio;                 │
    │     exports: [SessionCache]    │
    │  "}}                           │
    │◄───────────────────────────────│
    │                                │
```

### Brain memory flow

```
Session Start:
    ctx_brain(action="activate", brain_id="my-app")
        │
        ├── store.brain_recall(brain_id, "", "short_term", N)
        ├── store.brain_recall(brain_id, "", "long_term", N)
        ├── Score each memory: composite_score*0.6 + recency*0.4
        │   recency = exp(-0.231 * days) for short-term
        │   recency = exp(-0.0077 * days) for long-term
        ├── store.brain_increment_recall(id) for each activated memory
        ├── store.open_loop_list(brain_id, "open")
        └── store.brain_session_latest(brain_id)
            │
            ▼
    Return ActivationPacket {
        memories: [...],
        open_loops: [...],
        checkpoint: {...}
    }

During Session:
    ctx_brain(action="store", content="Redis uses port 6380",
              memory_type="semantic", layer="short_term")
        │
        └── store.brain_store(&BrainMemory { content, layer, type, ... })

    ctx_brain(action="recall", query="redis port")
        │
        └── store.brain_recall(brain_id, query, "", limit)

Session End:
    ctx_brain(action="consolidate", session_text="...")
        │
        ├── Parse session text for extractable memories
        ├── store.brain_recall() to check for duplicates
        ├── store.brain_store() for new memories
        └── Auto-promote: if recall_count >= threshold → promote to long_term

    ctx_brain(action="checkpoint", checkpoint_type="manual")
        │
        └── store.brain_checkpoint_store(&BrainCheckpoint { ... })
```

---

## Key Takeaways for Learning Rust

1. **Ownership is central** — every value has exactly one owner. `Arc` shares ownership, `&` borrows it.
2. **No nulls** — use `Option<T>` (Some/None) instead. The compiler forces you to handle both cases.
3. **No exceptions** — use `Result<T, E>` and the `?` operator. Errors are explicit in function signatures.
4. **Pattern matching is everywhere** — `match`, `if let`, `while let`. It's how you work with enums and Options.
5. **Traits, not inheritance** — composition over inheritance. `impl Trait for Type` adds behavior.
6. **Async needs a runtime** — `tokio::main` or `#[tokio::main]` sets up the async executor.
7. **`Cargo.toml`** is your `.csproj` — dependencies, features, build profiles all live here.
8. **`cargo test`** runs tests inline in the same file (`#[cfg(test)] mod tests { }`).
9. **Zero-cost abstractions** — generics and traits compile away. You don't pay for what you don't use.
10. **The compiler is your friend** — Rust's error messages are famously helpful. Read them carefully.
