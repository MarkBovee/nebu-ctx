# nebu-ctx — Context Engineering Layer

CRITICAL: ALWAYS use nebu-ctx MCP tools instead of native equivalents. This is NOT optional.

## Tool replacement:

| ALWAYS USE | NEVER USE | Why |
|------------|-----------|-----|
| `ctx_read(target=file|files|symbol|outline|archive, ...)` | `Read` / `cat` / `head` / `tail` | Public cached read API for files, symbols, outlines, and archives |
| `ctx_shell(command)` | `Shell` / `bash` / terminal | Pattern compression for git/npm/cargo output |
| `ctx_search(mode=regex|semantic, ...)` | `Grep` / `rg` | Compact regex and semantic search results |
| `ctx_tree(path, depth)` | `ls` / `find` | Compact directory maps |
| `ctx(domain=memory|context|graph|analytics|agents|inspect, action="...")` | private `ctx_*` implementation details | Stable public gateway for advanced operations |

Public `ctx_read` targets: `file`, `files`, `symbol`, `outline`, `archive`.
Public `ctx_search` modes: `regex`, `semantic`.
Public `ctx` domains: `memory`, `context`, `graph`, `analytics`, `agents`, `inspect`.

Use private tool names only when documenting nebu-ctx internals. User-facing guidance should recommend only the 5 public tools.

## File editing:

Use native Edit/StrReplace/Write/Delete tools for mutations.

## Proactive (use without being asked):

- `ctx(domain="context", action="overview", task="...")` at session start
- `ctx(domain="context", action="compress")` when context grows large
- `ctx(domain="memory", action="save"|"recall"|"store"|"consolidate")` when useful facts or session state should persist

## Memory policy:

- Use `ctx(domain="memory", action="save"|"load"|"task"|"finding"|"decision")` for task state and working memory.
- Use `ctx(domain="memory", action="store"|"recall"|"wakeup"|"consolidate")` for durable project facts.
- Let the stop/compact hooks consolidate session context into the nebu-ctx server instead of relying on chat history.

Fallback only if a nebu-ctx tool is unavailable: use native equivalents.
