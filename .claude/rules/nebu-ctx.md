# nebu-ctx — Context Engineering Layer
<!-- nebu-ctx-rules-v15 -->

CRITICAL: ALWAYS use the public nebu-ctx MCP surface instead of native equivalents. This is NOT optional.

## Tool replacement:
| ALWAYS USE | NEVER USE | Why |
|------------|-----------|-----|
| `ctx_read(target="file"|"files"|"symbol"|"outline"|"archive", ...)` | `Read` / `cat` / `head` / `tail` | Public cached read API for files, symbols, outlines, and archives |
| `ctx_search(mode="regex"|"semantic", ...)` | `Grep` / `rg` | Compact regex and semantic search results |
| `ctx_tree(path, depth)` | `ls` / `find` | Compact directory maps |
| `ctx(domain="memory"|"context"|"graph"|"analytics"|"agents"|"inspect", action="...")` | private `ctx_*` implementation details | Stable public gateway for advanced operations |

Public `ctx_read` targets: `file`, `files`, `symbol`, `outline`, `archive`.
Public `ctx_search` modes: `regex`, `semantic`.
Public `ctx` domains: `memory`, `context`, `graph`, `analytics`, `agents`, `inspect`.
Shell commands stay on native `Shell` / `bash`; the nebu-ctx shell hook compresses output automatically.

Use private tool names only when documenting nebu-ctx internals. User-facing guidance should recommend only the 4 public tools.

When a host exposes deferred `mcp_nebuctx_*` tools, do not pass them through `multi_tool_use.parallel`; call the public `ctx_*` tool directly, use `ctx_read(target="files", paths=[...])` for multi-file reads, and run repeated `ctx_search` calls separately until that host wrapper bug is fixed.

If a public nebu-ctx tool (`ctx_read`, `ctx_search`, `ctx_tree`, `ctx(...)`) fails reproducibly, retry once if the failure may be environmental. If it still fails, do not bypass to the native equivalent. Use supported raw mode or the repo-built nebu-ctx client, then automatically create or update a GitHub issue in `MarkBovee/nebu-ctx` before final handoff. Include repro steps, expected vs actual behavior, shell/platform, and the failing tool call. Use `gh issue create --repo MarkBovee/nebu-ctx ...` or `gh issue comment` when needed.

## File editing:
Use native Edit/StrReplace/Write/Delete tools for mutations.

## Proactive (use without being asked):
- `ctx(domain="context", action="overview", task="...")` at session start
- `ctx(domain="context", action="compress")` when context grows large
- `ctx(domain="memory", action="save"|"recall"|"store"|"consolidate")` when useful facts or session state should persist

fallback only if a nebu-ctx tool is unavailable: use native equivalents.

<!-- /lean-ctx -->