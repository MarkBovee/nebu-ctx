# nebu-ctx — Context Engineering Layer (Global)

You have the nebu-ctx MCP server available. Use the public 4-tool surface in guidance: `ctx_read`, `ctx_search`, `ctx_tree`, and `ctx`.

## Tool Replacement Rules

| NEVER use | ALWAYS use instead |
|-----------|-------------------|
| `Read` / `View` / `cat` / `head` / `tail` | `ctx_read(target="file"|"files"|"symbol"|"outline"|"archive", ...)` |
| `Bash` / `Shell` (any shell command) | Native shell; nebu-ctx shell hook compresses output automatically |
| `Grep` / `Search` / `rg` | `ctx_search(mode="regex"|"semantic", ...)` |
| `ListFiles` / `ListDirectory` / `ls` / `find` | `ctx_tree(path, depth)` |
| private `ctx_*` implementation details | `ctx(domain="memory"|"context"|"graph"|"analytics"|"agents"|"inspect", action="...")` |

## How to Use

```
ctx_read(target="file", path="src/main.rs")
ctx_read(target="symbol", name="main", file="src/main.rs")
ctx_read(target="outline", path="src/lib.rs")
ctx_search(mode="regex", pattern="pub fn", path="src/")
ctx_search(mode="semantic", query="session state persistence", path="src/")
ctx_tree(".", 2)
ctx(domain="memory", action="recall", query="session state decisions")
```

Write, Edit, and other mutation tools have no public nebu-ctx equivalent — use them normally.

CRITICAL: Every time you reach for Read, Grep, or ListFiles in guidance, stop and use the public nebu-ctx equivalent instead. For shell commands, use native Bash/Shell directly.

## Memory policy

- Use `ctx(domain="memory", action="save"|"recall")` for task state and working memory.
- Use `ctx(domain="memory", action="store"|"recall"|"wakeup"|"consolidate")` for durable project facts.
- The stop/compact hooks already persist session state into the nebu-ctx server; rely on that instead of chat history for reusable memory.
- If a public nebu-ctx tool fails reproducibly, retry once if it may be environmental. If it still fails, do not bypass to the native equivalent. Use supported raw mode or the repo-built nebu-ctx client, then create a GitHub issue in `MarkBovee/nebu-ctx` with repro, expected vs actual, shell/platform, and the failing tool call. Prefer `gh issue create --repo MarkBovee/nebu-ctx ...`; fall back to `nebu-ctx report-issue` if needed.

Use `ctx(domain="context", action="overview"|"compress")`, `ctx(domain="graph", action="...")`, and the other public `ctx` domains for advanced operations.
