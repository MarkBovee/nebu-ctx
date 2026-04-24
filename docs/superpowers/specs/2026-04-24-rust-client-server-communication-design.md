# Design: Rust Client ↔ .NET Server Communication

Date: 2026-04-24  
Status: Approved  
Author: brainstorming session

---

## Problem

The Rust client (`nebu-ctx`) runs as the MCP server that Claude connects to via stdio. It serves ~46 local tools. The .NET server hosts cloud-backed tools (`ctx_brain`, `ctx_knowledge`, `ctx_session`) over HTTP, but these are unreachable from Claude unless it connects to the .NET server directly as a second MCP endpoint.

That two-endpoint setup is fragile: it requires extra config per machine, loses automatic git context enrichment, and splits the tool manifest across two places.

---

## Goal

Claude connects to exactly one MCP endpoint — the Rust client — and sees all tools (local and cloud) in a single manifest. The Rust client transparently proxies cloud tool calls to the .NET server, automatically enriching each call with the current git context (project fingerprint, branch, commit, local root). No project ID management required from the AI side.

---

## Architecture

```
Claude (or any AI agent)
        │
        │  stdio  (or streamable HTTP: nebu-ctx serve)
        ▼
┌─────────────────────────────────────────────────────┐
│  Rust Client  (nebu-ctx)                            │
│                                                     │
│  MCP manifest = local tools + cloud tool stubs      │
│                                                     │
│  On tool call:                                      │
│    local tool? ──► handle locally (unchanged)       │
│    cloud tool? ──► enrich with git context          │
│                    ──► POST /v1/tools/call          │
└───────────────────────────┬─────────────────────────┘
                            │ HTTP + Bearer token
                            ▼
              ┌─────────────────────────┐
              │  .NET Server            │
              │  ctx_brain (Postgres)   │
              │  ctx_knowledge (PG)     │
              │  ctx_session (PG)       │
              │  /v1/projects/resolve   │
              │  Dashboard :3333        │
              └─────────────────────────┘
```

---

## Components

### CloudToolRouter (`client/src/tools/cloud_router.rs`)

Centralises all cloud proxy logic. Responsibilities:
- Accept a tool name and arguments from the MCP handler
- Load the saved `ServerClient` (endpoint + token from config)
- Discover git context from the current working directory using the existing `git_context::discover_project_context`
- Call `ServerClient::call_tool(tool_name, args, &project_context)`
- Return the server's `result` value to the MCP layer

### Cloud tool stubs

| Tool | File | Behaviour |
|---|---|---|
| `ctx_brain` | `client/src/tools/ctx_brain.rs` (new) | Always delegates to `CloudToolRouter` |
| `ctx_knowledge` | `client/src/tools/ctx_knowledge.rs` (extend) | Routes to `CloudToolRouter` if cloud connected; falls back to local file store if not |
| `ctx_session` | `client/src/tools/ctx_session.rs` (extend) | Same as `ctx_knowledge` |

### Tool registration (`client/src/tools/mod.rs`)

Cloud tool stubs are registered in the MCP server manifest unconditionally — they appear whether or not a server connection is saved. Attempting to call them without a connection returns a clear, actionable error.

---

## Data Flow

**Cloud tool call (ctx_brain example):**

```
Claude: ctx_brain(action="store", key="x", value="y")
  │
  ▼ rmcp dispatch
CloudToolRouter::handle("ctx_brain", args)
  1. Load ServerClient — fail clearly if not configured
  2. git_context::discover_project_context(cwd)
  3. ServerClient::call_tool("ctx_brain", args, &project_context)
     POST /v1/tools/call {
       name: "ctx_brain",
       arguments: { action, key, value },
       project_slug: "nebu-ctx",
       repository_fingerprint: { remote_url, host, owner, repo_name, default_branch },
       checkout_binding: { branch, last_commit, local_root }
     }
  4. Receive ToolCallResponse { result }
  5. Return result to Claude
```

**Offline / ctx_knowledge fallback:**

If `ServerClient::load()` fails (no connection saved), `ctx_knowledge` and `ctx_session` fall back to the existing local file-based implementation with a warning appended to the result:

```
⚠ Running locally (no cloud connection). Data is stored in .nebu-ctx/ and not synced.
  To enable cloud persistence: nebu-ctx cloud connect
```

`ctx_brain` has no local fallback — it returns an error immediately.

---

## Error Handling

| Condition | Behaviour |
|---|---|
| No connection saved | Return structured error: `"ctx_brain requires a cloud connection. Run: nebu-ctx cloud connect"` |
| Server unreachable | Return structured error with cause: `"Cloud tool ctx_brain failed: <error>. Check: nebu-ctx cloud status"` |
| Server returns non-200 | Surface the server's error message directly |
| cwd is not a git repo | Proxy call proceeds without fingerprint/binding (server assigns to default project) |

Errors are returned as MCP tool results (not panics or process exits), so Claude can read and react to them.

---

## Testing

**Unit tests (Rust client):**
- `CloudToolRouter` routes local tools locally and cloud tools via `ServerClient`
- Returns a structured "not connected" error when no connection is saved
- Enriches the outgoing request with fingerprint and binding when in a git repo
- Enriches correctly when cwd is not a git repo (graceful degradation)

**Integration (existing):**  
`tests/local-addon-test.sh` already validates `ctx_brain store + recall` end-to-end over HTTP against the .NET server. No new integration test is needed for this work.

**Manual validation:**
```bash
nebu-ctx cloud connect --endpoint http://server:4242 --token <token>
# In a git repo:
ctx_brain(action="store", key="test", value="hello from client")
ctx_brain(action="recall", query="test")
# Verify data in Postgres dashboard at :3333
```

---

## Out of Scope

- Network retries and timeout tuning (deferred to WP6 — hybrid sync pipeline)
- Full e2e test through Claude's MCP protocol layer
- Adding new cloud tools beyond `ctx_brain`, `ctx_knowledge`, `ctx_session`
- Streaming responses from the server (current `/v1/tools/call` is request/response)

---

## Implementation Notes

- `ServerClient::call_tool` in `cloud_client.rs` already accepts a `ProjectContext` and builds the enriched request body — the `CloudToolRouter` just needs to call it
- `git_context::discover_project_context` already handles the non-git-repo case gracefully
- Cloud tool stubs should reuse the existing `ToolDef` / `inputSchema` definitions already present in `tool_defs/granular.rs` — no duplication needed
- `ctx_knowledge` and `ctx_session` local fallback paths are unchanged; only the routing decision at the top of the handler changes
