## Public MCP Surface

`nebu-ctx` exposes exactly 5 public MCP tools.

This is intentional:

- lower token cost in manifests and instructions
- less tool-selection noise for agents
- one stable mental model across local and server-backed behavior

## The 5 Tools

| Tool | Purpose |
|:---|:---|
| `ctx_read` | Read files, multi-file batches, symbols, outlines, and archived output |
| `ctx_search` | Regex and semantic code search |
| `ctx_tree` | Directory listings with file counts |
| `ctx_shell` | Shell commands with compressed output |
| `ctx` | Higher-level memory, context, graph, analytics, agent, and inspect workflows |

## `ctx_read`

Use `ctx_read` whenever you need code or archived output back into context.

### Targets

| Target | What it does |
|:---|:---|
| `file` | Read one file |
| `files` | Read multiple files in one call |
| `symbol` | Read one symbol by name |
| `outline` | Read the symbol/signature outline of a file |
| `archive` | Retrieve archived large tool output |

### Modes

| Mode | When to use |
|:---|:---|
| `auto` | Let the client pick the best mode |
| `full` | Full file contents |
| `map` | Quick structure and exports |
| `signatures` | API surface |
| `diff` | Changed lines |
| `task` | Task-focused filtering |
| `reference` | Very compact reference form |
| `aggressive` | Large repetitive files |
| `entropy` | Entropy-aware compression |
| `lines:N-M` | Exact ranges |

### Example

```json
{
  "name": "ctx_read",
  "arguments": {
    "target": "symbol",
    "name": "dispatch_tool",
    "path": "client/src/mcp_server/dispatch.rs"
  }
}
```

## `ctx_search`

Use `ctx_search` for both traditional grep-like search and semantic search.

### Modes

| Mode | What it does |
|:---|:---|
| `regex` | Regex search across files |
| `semantic` | Meaning-based search across the project |

### Examples

Regex:

```json
{
  "name": "ctx_search",
  "arguments": {
    "mode": "regex",
    "pattern": "ctx_read",
    "path": "client/src"
  }
}
```

Semantic:

```json
{
  "name": "ctx_search",
  "arguments": {
    "mode": "semantic",
    "query": "where does the client route MCP tools",
    "path": "client/src",
    "top_k": 10
  }
}
```

## `ctx_tree`

Use `ctx_tree` to orient quickly in a project.

### Example

```json
{
  "name": "ctx_tree",
  "arguments": {
    "path": "client/src",
    "depth": 2
  }
}
```

## `ctx_shell`

Use `ctx_shell` for shell commands. Output is compressed by default and begins with `[shell: ...]` so agents can see the active shell semantics. Use `shell` to force a specific executable for one call. Git inspection commands such as `git status --short` and `git diff --name-only/--stat` are preserved verbatim so commit workflows can trust the exact file list.

### Example

```json
{
  "name": "ctx_shell",
  "arguments": {
    "command": "cargo test --manifest-path client/Cargo.toml",
    "cwd": "/repo",
    "shell": "/bin/bash"
  }
}
```

## `ctx`

Use `ctx` for higher-level workflows with `domain` + `action`.

### Domains

| Domain | Purpose |
|:---|:---|
| `memory` | Project memory, session state, durable facts |
| `context` | Orientation, preload, compression, task-focused context shaping |
| `graph` | Related files, impact, architecture, call relationships |
| `analytics` | Cost, gain, heatmap, stats, benchmark-style reporting |
| `agents` | Handoffs, tasks, workflow, coordination |
| `inspect` | Cache, routes, execution, admin-style inspection |

### Examples

Memory:

```json
{
  "name": "ctx",
  "arguments": {
    "domain": "memory",
    "action": "recall",
    "query": "session state decisions"
  }
}
```

### Memory Actions

For hosted memory over HTTP, call the public `ctx` tool and set `domain` to `memory`.

Working-memory actions:

- `task`
- `finding`
- `decision`
- `save`
- `load`
- `status`
- `reset`
- `list`
- `cleanup`

Durable knowledge actions:

- `store`
- `set`
- `remember`
- `recall`
- `consolidate`
- `promote`
- `upkeep`
- `wakeup`
- `triage`
- `remove`

Internally, the Rust client or hosted server routes those public memory actions onto private handlers such as `ctx_session` and `ctx_knowledge`.

HTTP example:

```json
{
  "name": "ctx",
  "arguments": {
    "domain": "memory",
    "action": "remember",
    "category": "decision",
    "key": "memory-owner",
    "value": "server owns canonical memory"
  }
}
```

Context:

```json
{
  "name": "ctx",
  "arguments": {
    "domain": "context",
    "action": "overview",
    "task": "reduce MCP tool surface"
  }
}
```

Graph:

```json
{
  "name": "ctx",
  "arguments": {
    "domain": "graph",
    "action": "impact",
    "path": "client/src/mcp_server/mod.rs"
  }
}
```

Analytics:

```json
{
  "name": "ctx",
  "arguments": {
    "domain": "analytics",
    "action": "cost"
  }
}
```

Agents:

```json
{
  "name": "ctx",
  "arguments": {
    "domain": "agents",
    "action": "handoff",
    "to": "review-agent",
    "message": "review the routing changes"
  }
}
```

Inspect:

```json
{
  "name": "ctx",
  "arguments": {
    "domain": "inspect",
    "action": "cache_status"
  }
}
```

## Design Notes

- The public MCP contract is small by design.
- The Rust client may still route public calls to many internal handlers.
- Those internal handlers are implementation details, not part of the public MCP surface.
