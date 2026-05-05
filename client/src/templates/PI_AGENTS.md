# nebu-ctx — Public MCP Guidance for Pi

nebu-ctx is installed as a Pi Package with first-class MCP support. User-facing guidance should recommend only the public 5-tool MCP surface: `ctx_read`, `ctx_search`, `ctx_tree`, `ctx_shell`, and `ctx`.

## Public MCP Surface

- `ctx_read(target="file"|"files"|"symbol"|"outline"|"archive", ...)`
- `ctx_search(mode="regex"|"semantic", ...)`
- `ctx_tree(path, depth)`
- `ctx_shell(command)`
- `ctx(domain="memory"|"context"|"graph"|"analytics"|"agents"|"inspect", action="...")`

## Guidance Rules

- Prefer the 5 public tools above in all visible instructions and examples.
- Do not recommend direct private `ctx_*` tool names unless you are explicitly documenting internal implementation details.
- Use native edit/write/delete tools for mutations; nebu-ctx's public surface is read/search/tree/shell/ctx.

## Public Examples

```text
ctx_read(target="file", path="src/main.rs")
ctx_read(target="symbol", name="main", file="src/main.rs")
ctx_search(mode="regex", pattern="pub fn", path="src/")
ctx_search(mode="semantic", query="session state persistence", path="src/")
ctx_tree(".", 2)
ctx_shell("git status")
ctx(domain="memory", action="recall", query="session state decisions")
```

## Memory policy

- Use `ctx(domain="memory", action="save"|"recall")` for task state, findings, and decisions that should survive across chats.
- Use `ctx(domain="memory", action="store")` for durable project facts.
- The stop/compact hooks already persist session state into the nebu-ctx server; rely on that instead of chat history for reusable memory.

## No manual prefixing needed

The Pi extension handles routing automatically. Use the public nebu-ctx surface directly in guidance and examples:

```text
ctx_shell("git status")
ctx_shell("cargo test")
ctx_shell("kubectl get pods")
```

## Checking status

Use `/nebu-ctx` in Pi to verify which binary is active and see MCP bridge status.

## Dashboard

Use the NebuCtx server-hosted dashboard for real-time token savings, cost, and heatmap views.
