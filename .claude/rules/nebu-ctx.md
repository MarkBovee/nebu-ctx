# nebu-ctx — Context Engineering Layer
<!-- nebu-ctx-rules-v12 -->

CRITICAL: ALWAYS use the public nebu-ctx MCP surface instead of native equivalents. This is NOT optional.

## Tool replacement:
| ALWAYS USE | NEVER USE | Why |
|------------|-----------|-----|
| `ctx_read(target="file"|"files"|"symbol"|"outline"|"archive", ...)` | `Read` / `cat` / `head` / `tail` | Public cached read API for files, symbols, outlines, and archives |
| `ctx_shell(command, shell?)` | `Shell` / `bash` / terminal | Pattern compression plus active shell visibility |
| `ctx_search(mode="regex"|"semantic", ...)` | `Grep` / `rg` | Compact regex and semantic search results |
| `ctx_tree(path, depth)` | `ls` / `find` | Compact directory maps |
| `ctx(domain="memory"|"context"|"graph"|"analytics"|"agents"|"inspect", action="...")` | private `ctx_*` implementation details | Stable public gateway for advanced operations |

Public `ctx_read` targets: `file`, `files`, `symbol`, `outline`, `archive`.
Public `ctx_search` modes: `regex`, `semantic`.
Public `ctx` domains: `memory`, `context`, `graph`, `analytics`, `agents`, `inspect`.
`ctx_shell` uses active shell semantics; output includes `[shell: ...]`. Use `shell="pwsh.exe"`, `shell="cmd.exe"`, or `shell="/bin/bash"` to force a shell per call.

Use private tool names only when documenting nebu-ctx internals. User-facing guidance should recommend only the 5 public tools.

If a public nebu-ctx tool (`ctx_read`, `ctx_search`, `ctx_tree`, `ctx_shell`, `ctx(...)`) fails reproducibly, retry once if the failure may be environmental. If it still fails, create a GitHub issue in `MarkBovee/nebu-ctx` with repro steps, expected vs actual behavior, shell/platform, and the failing tool call. Prefer `gh issue create --repo MarkBovee/nebu-ctx ...`; fall back to `nebu-ctx report-issue --title ... --description ...` if needed.

## File editing:
Use native Edit/StrReplace/Write/Delete tools for mutations.

## Proactive (use without being asked):
- `ctx(domain="context", action="overview", task="...")` at session start
- `ctx(domain="context", action="compress")` when context grows large
- `ctx(domain="memory", action="save"|"recall"|"store"|"consolidate")` when useful facts or session state should persist

Fallback only if a nebu-ctx tool is unavailable: use native equivalents.
<!-- /lean-ctx -->
