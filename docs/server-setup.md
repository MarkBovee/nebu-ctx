# Server Setup Guide

Nebula-ctx can run in three modes:

| Mode | Transport | Storage | Use case |
|------|-----------|---------|----------|
| **Local** | stdio | SQLite | CLI, IDE integration, single machine |
| **Server** | HTTP | SQLite or PostgreSQL | Remote access, multi-machine |
| **HA Addon** | HTTP | SQLite or PostgreSQL | Home Assistant integration |

## Local Mode (default)

No configuration needed. Just run setup:

```bash
nebula-ctx setup
```

This registers nebula-ctx as a stdio MCP server in Claude Code, Cursor, VS Code, etc.

## Server Mode

Run as an HTTP MCP server for remote access:

```bash
# SQLite backend (default)
nebula-ctx serve --port 8099

# PostgreSQL backend
DATABASE_URL="postgres://user:pass@localhost:5432/nebula" nebula-ctx serve --port 8099

# With authentication
NEBULA_CTX_HTTP_TOKEN="my-secret-token" nebula-ctx serve --port 8099
```

### Environment Variables

| Variable | Default | Description |
|----------|---------|-------------|
| `NEBULA_CTX_DATA_DIR` | `~/.nebula-ctx` | Data directory for SQLite DB, cache, sessions |
| `NEBULA_CTX_STORE` | `sqlite` | Storage backend: `sqlite` or `postgres` |
| `DATABASE_URL` | — | PostgreSQL connection string (required if store=postgres) |
| `NEBULA_CTX_HTTP_PORT` | `8099` | HTTP server port |
| `NEBULA_CTX_HTTP_TOKEN` | — | Bearer token for authentication |
| `RUST_LOG` | `info` | Log level: `debug`, `info`, `warn`, `error` |

### Register as MCP Client (remote)

To connect Claude Code to a remote nebula-ctx server, add to `~/.claude/mcp.json`:

```json
{
  "mcpServers": {
    "nebula-ctx": {
      "type": "http",
      "url": "http://your-server:8099/v1/tools/call",
      "headers": {
        "Authorization": "Bearer my-secret-token"
      }
    }
  }
}
```

## Docker

```bash
# Build
docker build -t nebula-ctx .

# Run with SQLite
docker run -d \
  -p 8099:8099 \
  -v nebula-ctx-data:/data \
  nebula-ctx

# Run with PostgreSQL
docker run -d \
  -p 8099:8099 \
  -e NEBULA_CTX_STORE=postgres \
  -e DATABASE_URL="postgres://user:pass@db:5432/nebula" \
  nebula-ctx
```

## Home Assistant Addon

1. Copy `homeassistant/` to your HA addons directory (or add as custom repository)
2. Install the addon
3. Configure in addon options:
   - `store`: `sqlite` or `postgres`
   - `database_url`: PostgreSQL connection string (if using postgres)
   - `auth_token`: optional Bearer token for authentication
4. Start the addon
5. Connect from Claude Code using the HA server URL

## Brain Memory

Brain memory is available in all modes. Data is stored in the configured backend:

- **SQLite**: `{data_dir}/nebula-ctx.db`
- **PostgreSQL**: Tables `brain_memories`, `brain_sessions`, `brain_checkpoints`, `open_loops`

### MCP Tool: `ctx_brain`

| Action | Parameters | Description |
|--------|-----------|-------------|
| `store` | content, brain_id?, layer?, memory_type?, importance? | Store a new memory |
| `recall` | brain_id?, query?, layer?, limit? | Recall memories by score |
| `consolidate` | brain_id?, session_text | Extract memories from session |
| `activate` | brain_id?, max_memories? | Warm-up with relevant memories |
| `checkpoint` | brain_id?, content, checkpoint_type? | Save session state |
| `status` | brain_id? | Show brain memory stats |
