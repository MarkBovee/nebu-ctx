# Nebula Server

Context engineering layer for AI agents — forked from [lean-ctx](https://github.com/your-org/lean-ctx) with PostgreSQL persistence, brain memory, and Home Assistant deployment.

## What It Does

Nebula Server is an MCP (Model Context Protocol) server that gives AI agents:

- **Context management** — 42 tools for reading, caching, compressing, and searching code context
- **Token efficiency** — 10 compression modes, 90+ shell output patterns, adaptive mode selection
- **Brain memory** — persistent memory with scoring, consolidation, and session activation (ported from dot-claw)
- **Multi-agent coordination** — agent registry, task orchestration, context handoffs
- **Dual storage** — SQLite for local dev, PostgreSQL for server/HA deployment

## Architecture

```
┌─────────────────────────────────────────┐
│           MCP Client (Claude Code)      │
│           or any MCP-compatible agent    │
└──────────────┬──────────────────────────┘
               │ JSON-RPC over HTTP/stdio
┌──────────────▼──────────────────────────┐
│          Nebula Server (Rust)            │
│  ┌──────────────────────────────────┐   │
│  │  42+ MCP Tools                   │   │
│  │  ctx_read, ctx_search, cache,    │   │
│  │  knowledge, agents, brain_*      │   │
│  └──────────────┬───────────────────┘   │
│  ┌──────────────▼───────────────────┐   │
│  │  ContextStore Trait              │   │
│  │  ├─ SqliteStore (local)          │   │
│  │  └─ PostgresStore (server/HA)    │   │
│  └──────────────┬───────────────────┘   │
│  ┌──────────────▼───────────────────┐   │
│  │  Brain Memory System             │   │
│  │  scoring → consolidation →       │   │
│  │  activation → checkpoint         │   │
│  └──────────────────────────────────┘   │
└─────────────────────────────────────────┘
               │
    ┌──────────▼──────────┐
    │   SQLite (local)    │
    │   PostgreSQL (srv)  │
    └─────────────────────┘
```

## Features from Parent Projects

### From lean-ctx (foundation)
- 42 MCP tools for context management
- FTS5 full-text search with BM25 + hybrid embeddings
- Token-aware compression (tiktoken)
- Property graph for dependency analysis
- Multi-agent coordination (registry, tasks, ledger, diaries)
- Adaptive compression via Thompson Sampling

### From dot-claw (brain memory — ported)
- **Scoring**: composite weights (semantic, recency, importance, confidence, open loops)
- **Consolidation**: LLM-powered extraction of memories from sessions
- **Activation**: warm-up new sessions with relevant memories + open loops
- **Tiers**: short-term → long-term auto-promotion after N recalls
- **Checkpoints**: save/restore session state

## Quick Start

### Local (SQLite)

```bash
cargo build --release
./target/release/nebula-server --store sqlite
```

### Server (PostgreSQL)

```bash
export DATABASE_URL="postgres://user:pass@localhost:5432/nebula"
./target/release/nebula-server --store postgres --port 8099
```

### Home Assistant Addon

Copy `homeassistant/` to your HA addons directory, configure Postgres URL in addon options.

### MCP Client Config

```json
{
  "mcpServers": {
    "nebula": {
      "url": "http://localhost:8099/v1/tools",
      "transport": "http"
    }
  }
}
```

## Brain Memory Tools

| Tool | Description |
|------|-------------|
| `brain_store` | Store a memory with auto-scoring |
| `brain_recall` | Recall memories with scoring + decay |
| `brain_consolidate` | Extract memories from session text |
| `brain_activate` | Warm-up a new session with relevant context |
| `brain_checkpoint` | Save/restore session state |
| `brain_status` | Show memory stats, open loops, tiers |

## Storage Backends

| Feature | SQLite | PostgreSQL |
|---------|--------|------------|
| Cache | FTS5 | pgvector + tsvector |
| Search | BM25 + hybrid | pgvector + BM25 |
| Knowledge | Local tables | Persistent |
| Brain memory | Local | Persistent + sync |
| Graph | rusqlite | Postgres tables |
| Best for | Local dev, CLI | Server, HA addon, multi-device |

## Development

Built with Rust. See [docs/plans/nebula-server-roadmap.md](docs/plans/nebula-server-roadmap.md) for the full roadmap.

**Learning Rust?** Read [docs/technical-architecture.md](docs/technical-architecture.md) — explains every module, how MCP tools work end-to-end, the cache/compression engine, shared state patterns, and key Rust concepts with code examples.

```bash
# Build
cargo build

# Test
cargo test

# Run with logging
RUST_LOG=debug cargo run -- --store sqlite
```

## Upstream Sync

Nebula Server is forked from lean-ctx. Upstream changes are pulled regularly:

```bash
git remote add upstream https://github.com/your-org/lean-ctx.git
git fetch upstream
git rebase upstream/main
```

Modifications are kept behind the `ContextStore` trait boundary to minimize merge conflicts.

## License

MIT (matching lean-ctx)
