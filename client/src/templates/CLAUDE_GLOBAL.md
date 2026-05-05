# nebu-ctx — Context Engineering Layer (Global)

You have the nebu-ctx MCP server available. Use the public 5-tool surface in guidance: `ctx_read`, `ctx_search`, `ctx_tree`, `ctx_shell`, and `ctx`.

## Tool Replacement Rules

| NEVER use | ALWAYS use instead |
|-----------|-------------------|
| `Read` / `View` / `cat` / `head` / `tail` | `ctx_read(target="file"|"files"|"symbol"|"outline"|"archive", ...)` |
| `Bash` (any shell command) | `ctx_shell(command)` |
| `Grep` / `Search` / `rg` | `ctx_search(mode="regex"|"semantic", ...)` |
| `ListFiles` / `ListDirectory` / `ls` / `find` | `ctx_tree(path, depth)` |
| private `ctx_*` implementation details | `ctx(domain="memory"|"context"|"graph"|"analytics"|"agents"|"inspect", action="...")` |

## How to Use

```
ctx_read(target="file", path="src/main.rs")
ctx_read(target="symbol", name="main", file="src/main.rs")
ctx_read(target="outline", path="src/lib.rs")
ctx_shell("git status")
ctx_search(mode="regex", pattern="pub fn", path="src/")
ctx_search(mode="semantic", query="session state persistence", path="src/")
ctx_tree(".", 2)
ctx(domain="memory", action="recall", query="session state decisions")
```

Write, Edit, and other mutation tools have no public nebu-ctx equivalent — use them normally.

CRITICAL: Every time you reach for Read, Bash, Grep, or ListFiles in guidance, stop and use the public nebu-ctx equivalent instead.

## Memory policy

- Use `ctx(domain="memory", action="save"|"recall")` for task state and working memory.
- Use `ctx(domain="memory", action="store"|"recall"|"wakeup"|"consolidate")` for durable project facts.
- The stop/compact hooks already persist session state into the nebu-ctx server; rely on that instead of chat history for reusable memory.

Use `ctx(domain="context", action="overview"|"compress")`, `ctx(domain="graph", action="...")`, and the other public `ctx` domains for advanced operations.
