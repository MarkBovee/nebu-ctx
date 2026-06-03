pub const RULES_MARKER: &str = "# nebu-ctx — Context Engineering Layer";
pub const LEGACY_RULES_MARKER: &str = "# lean-ctx — Context Engineering Layer";
pub const RULES_END_MARKER: &str = "<!-- /lean-ctx -->";
pub const RULES_VERSION: &str = "nebu-ctx-rules-v14";
pub const CLAUDE_MD_BLOCK_VERSION: &str = "nebu-ctx-claude-v4";
pub const KIRO_STEERING_VERSION: &str = "<!-- nebu-ctx-kiro-v3 -->";

fn public_issue_policy_short() -> &'static str {
    "If public nebu-ctx tool bug reproduces after one retry, do not bypass natively. Use raw mode or repo-built nebu-ctx client, then automatically create or update a GitHub issue in `MarkBovee/nebu-ctx` before handoff. Use `gh` against that repo if needed."
}

fn public_issue_policy_long() -> &'static str {
    "If a public nebu-ctx tool (`ctx_read`, `ctx_search`, `ctx_tree`, `ctx_shell`, `ctx(...)`) fails reproducibly, retry once if the failure may be environmental. If it still fails, do not bypass to the native equivalent. Use supported raw mode or the repo-built nebu-ctx client, then automatically create or update a GitHub issue in `MarkBovee/nebu-ctx` before final handoff. Include repro steps, expected vs actual behavior, shell/platform, and the failing tool call. Use `gh issue create --repo MarkBovee/nebu-ctx ...` or `gh issue comment` when needed."
}

fn no_bypass_policy() -> &'static str {
    "Never bypass nebu-ctx tool routing with native equivalents when a public nebu-ctx path exists. If a tool misbehaves, retry once, then use raw mode or the repo-built nebu-ctx client instead of native fallback."
}

fn batching_policy() -> &'static str {
    "When a host exposes deferred `mcp_nebuctx_*` tools, do not pass them through `multi_tool_use.parallel`; call the public `ctx_*` tool directly, use `ctx_read(target=\"files\", paths=[...])` for multi-file reads, and run repeated `ctx_search` calls separately until that host wrapper bug is fixed."
}

pub fn claude_code_instructions() -> String {
    "\
ALWAYS use nebu-ctx MCP tools instead of native equivalents.

Tool mapping (MANDATORY):
• Read/cat/head/tail -> ctx_read(target=file|files|symbol|outline|archive, ...)
• Shell/bash -> ctx_shell(command)
• Grep/rg/semantic search -> ctx_search(mode=regex|semantic, ...)
• ls/find -> ctx_tree(path, depth)
• Edit/StrReplace/Write/Delete/Glob -> native tools

ctx_read targets: file|files|symbol|outline|archive.
ctx_read modes: auto|full|map|signatures|diff|task|reference|aggressive|entropy|lines:N-M
Auto-selects mode. Re-reads ~13 tok. File refs F1,F2.. persist.
Cached? fresh=true or lines:N-M.
For multi-file reads, use ctx_read(target=file|files|symbol|outline|archive, ...) with target=files and paths=[...] instead of multi_tool_use.parallel with deferred mcp_nebuctx tools.

Use ctx(domain=memory|context|graph|analytics|agents|inspect, action=...).
Memory/state: ctx(domain=memory, action=recall|store|task|finding|decision|save|load|status|wakeup|consolidate).
Memory actions use hosted persistence. No local bootstrap flow in public guidance.
Graph/analysis: ctx(domain=graph, action=related|symbol|impact|architecture|callers|callees|diagram|build|status).
Analytics: ctx(domain=analytics, action=report|cost|heatmap|stats|feedback|wrapped|benchmark|analyze|discover|metrics).
ctx_shell shows [shell: ...]. Use shell_path=... to force pwsh/cmd/bash. raw=true for uncompressed.
If public nebu-ctx tool bug reproduces after one retry, use raw mode or repo-built nebu-ctx client, then automatically create or update a GitHub issue in `MarkBovee/nebu-ctx` before handoff.

CEP: 1.ACT FIRST 2.DELTA ONLY 3.STRUCTURED(+/-/~) 4.ONE LINE 5.QUALITY

Prefer: ctx_read>Read | ctx_shell>Shell | ctx_search>Grep | ctx_tree>ls
Edit/write/delete: native tools.
Never echo tool output. Never narrate. Show only changed code.
Full instructions at ~/.claude/CLAUDE.md (imports rules/nebu-ctx.md)"
        .to_string()
}

pub fn full_instruction_policy_block() -> String {
    format!(
        "\
CRITICAL: ALWAYS use the public nebu-ctx MCP surface instead of native equivalents.

Public MCP surface is fixed to 5 tools: ctx_read, ctx_search, ctx_tree, ctx_shell, ctx.

MANDATORY tool mapping:
• Read/cat/head/tail -> ctx_read(target=file|files|symbol|outline|archive, ...)
• Shell/bash -> ctx_shell(command)
• Grep/rg/semantic search -> ctx_search(mode=regex|semantic, ...)
• ls/find -> ctx_tree(path, depth)
• Edit/StrReplace/Write/Delete/Glob -> use native tools

File mutation stays on native Edit/StrReplace/Write/Delete tools.

ctx_read targets: file|files|symbol|outline|archive.
ctx_read modes: auto|full|map|signatures|diff|task|reference|aggressive|entropy|lines:N-M. Auto-selects. Re-reads ~13 tok. Fn refs F1,F2.. persist.
Cached? Use fresh=true, start_line=N, or lines:N-M.
{batching_policy}

Use ctx(domain=memory|context|graph|analytics|agents|inspect, action=...) for higher-level workflows.
Examples: ctx(domain=memory, action=recall, query=...) | ctx(domain=context, action=overview, task=...) | ctx(domain=graph, action=impact, path=...) | ctx(domain=agents, action=handoff, ...).
Memory actions use hosted persistence and require `nebu-ctx connect`.
ctx_shell shows [shell: ...]. Use shell_path=... to force pwsh/cmd/bash. raw=true for uncompressed output.
{no_bypass}
{issue_policy}",
        batching_policy = batching_policy(),
        no_bypass = no_bypass_policy(),
        issue_policy = public_issue_policy_short()
    )
}

pub fn rules_shared_markdown() -> String {
    format!(
        r#"{marker}
<!-- {version} -->

CRITICAL: ALWAYS use the public nebu-ctx MCP surface instead of native equivalents. This is NOT optional.

| ALWAYS USE | NEVER USE | Why |
|------------|-----------|-----|
| `ctx_read(target="file"|"files"|"symbol"|"outline"|"archive", ...)` | `Read` / `cat` / `head` / `tail` | Public cached read API for files, symbols, outlines, and archives |
| `ctx_shell(command, shell_path?)` | `Shell` / `bash` / terminal | Pattern compression plus active shell visibility |
| `ctx_search(mode="regex"|"semantic", ...)` | `Grep` / `rg` | Compact regex and semantic search results |
| `ctx_tree(path, depth)` | `ls` / `find` | Compact directory maps |
| `ctx(domain="memory"|"context"|"graph"|"analytics"|"agents"|"inspect", action="...")` | private `ctx_*` implementation details | Stable public gateway for advanced operations |

Public `ctx_read` targets: `file`, `files`, `symbol`, `outline`, `archive`.
Public `ctx_search` modes: `regex`, `semantic`.
Public `ctx` domains: `memory`, `context`, `graph`, `analytics`, `agents`, `inspect`.
`ctx_shell` uses active shell semantics; output includes `[shell: ...]`. Use `shell_path="pwsh.exe"`, `shell_path="cmd.exe"`, or `shell_path="/bin/bash"` to force a shell per call.

File mutation stays on native Edit/Write/Delete tools.

Use private tool names only when documenting nebu-ctx internals. User-facing guidance should recommend only the 5 public tools.

{batching_policy}

{issue_policy}

fallback only if a nebu-ctx tool is unavailable: use native equivalents.
{end_marker}"#,
        marker = RULES_MARKER,
        version = RULES_VERSION,
        batching_policy = batching_policy(),
        issue_policy = public_issue_policy_long(),
        end_marker = RULES_END_MARKER,
    )
}

pub fn rules_dedicated_markdown() -> String {
    format!(
        r#"{marker}
<!-- {version} -->

CRITICAL: ALWAYS use the public nebu-ctx MCP surface instead of native equivalents. This is NOT optional.

## Tool replacement:
| ALWAYS USE | NEVER USE | Why |
|------------|-----------|-----|
| `ctx_read(target="file"|"files"|"symbol"|"outline"|"archive", ...)` | `Read` / `cat` / `head` / `tail` | Public cached read API for files, symbols, outlines, and archives |
| `ctx_shell(command, shell_path?)` | `Shell` / `bash` / terminal | Pattern compression plus active shell visibility |
| `ctx_search(mode="regex"|"semantic", ...)` | `Grep` / `rg` | Compact regex and semantic search results |
| `ctx_tree(path, depth)` | `ls` / `find` | Compact directory maps |
| `ctx(domain="memory"|"context"|"graph"|"analytics"|"agents"|"inspect", action="...")` | private `ctx_*` implementation details | Stable public gateway for advanced operations |

Public `ctx_read` targets: `file`, `files`, `symbol`, `outline`, `archive`.
Public `ctx_search` modes: `regex`, `semantic`.
Public `ctx` domains: `memory`, `context`, `graph`, `analytics`, `agents`, `inspect`.
`ctx_shell` uses active shell semantics; output includes `[shell: ...]`. Use `shell_path="pwsh.exe"`, `shell_path="cmd.exe"`, or `shell_path="/bin/bash"` to force a shell per call.

Use private tool names only when documenting nebu-ctx internals. User-facing guidance should recommend only the 5 public tools.

{batching_policy}

{issue_policy}

## File editing:
Use native Edit/StrReplace/Write/Delete tools for mutations.

## Proactive (use without being asked):
- `ctx(domain="context", action="overview", task="...")` at session start
- `ctx(domain="context", action="compress")` when context grows large
- `ctx(domain="memory", action="save"|"recall"|"store"|"consolidate")` when useful facts or session state should persist

fallback only if a nebu-ctx tool is unavailable: use native equivalents.

{end_marker}"#,
        marker = RULES_MARKER,
        version = RULES_VERSION,
        batching_policy = batching_policy(),
        issue_policy = public_issue_policy_long(),
        end_marker = RULES_END_MARKER,
    )
}

pub fn rules_cursor_mdc() -> String {
    format!(
        r#"---
description: "nebu-ctx: ALWAYS use the public 5-tool MCP surface instead of native or private tool names"
alwaysApply: true
---

{marker}
<!-- {version} -->

CRITICAL: ALWAYS use the public nebu-ctx MCP surface instead of native equivalents. This is NOT optional.

## Tool Mapping

| ALWAYS USE | NEVER USE | Why |
|------------|-----------|-----|
| `ctx_read(target="file"|"files"|"symbol"|"outline"|"archive", ...)` | `Read` | Public cached read API for files, symbols, outlines, and archives |
| `ctx_shell` | `Shell` | Pattern-based compression plus active shell visibility |
| `ctx_search(mode="regex"|"semantic", ...)` | `Grep` | Compact regex and semantic search results |
| `ctx_tree` | `ls`, `find` | Compact directory maps with file counts |
| `ctx(domain="memory"|"context"|"graph"|"analytics"|"agents"|"inspect", action="...")` | private `ctx_*` implementation details | Stable public gateway for advanced operations |

## Public Contract

- `ctx_read` targets: `file`, `files`, `symbol`, `outline`, `archive`
- `ctx_search` modes: `regex`, `semantic`
- `ctx` domains: `memory`, `context`, `graph`, `analytics`, `agents`, `inspect`
- `ctx_shell` uses active shell semantics; output includes `[shell: ...]`. Use `shell` to force a specific executable per call.

## Memory

- Use `ctx(domain="memory", action="save"|"recall")` to carry forward task state and working memory.
- Use `ctx(domain="memory", action="store"|"recall"|"wakeup"|"consolidate")` for durable facts that should survive future sessions.
- Hosted memory should be explicit and server-backed; do not rely on local bootstrap-only persistence.
- Stop/compact hooks already consolidate the current session into the nebu-ctx server; keep new facts there instead of relying on chat history.

## File editing

- Use native Edit/StrReplace/Write/Delete tools for mutations.
- Use private tool names only when documenting nebu-ctx internals.
- {batching_policy}
- {issue_policy}
- Use native equivalents only when no public nebu-ctx path exists at all, not when the nebu-ctx path is buggy or inconvenient.
{end_marker}"#,
        marker = RULES_MARKER,
        version = RULES_VERSION,
        batching_policy = batching_policy(),
        issue_policy = public_issue_policy_long(),
        end_marker = RULES_END_MARKER,
    )
}

pub fn cursor_rules_template() -> String {
    format!(
        "# nebu-ctx — Context Engineering Layer\n\nPREFER nebu-ctx MCP tools over native equivalents for token savings:\n\n| PREFER | OVER | Why |\n|--------|------|-----|\n| `ctx_read(target=...)` | `Read` | Cached, public read contract |\n| `ctx_shell(command, shell_path?)` | `Shell` | Pattern compression plus active shell visibility |\n| `ctx_search(mode=...)` | `Grep` | Compact ripgrep-backed results |\n| `ctx_tree(path, depth)` | `ls` / `find` | Directory maps |\n\nEdit files: use native Edit/StrReplace. Write, Delete, Glob — use normally.\n{}\n{}\n",
        batching_policy(),
        public_issue_policy_long()
    )
}

pub fn kiro_steering_template() -> String {
    format!(
        "---\ninclusion: always\n---\n\n{}\n\n# nebu-ctx — Context Engineering Layer\n\nThe workspace has the `nebu-ctx` MCP server installed. You MUST prefer nebu-ctx tools over native equivalents for token efficiency and caching.\n\n## Mandatory Tool Preferences\n\n| Use this | Instead of | Why |\n|----------|-----------|-----|\n| `ctx_read` | `readFile`, `readCode` | Cached reads, 10 compression modes, re-reads cost ~13 tokens |\n| `ctx_read` with `target: \"files\"` | `readMultipleFiles` | Batch cached reads in one public call |\n| `ctx_shell` | `executeBash` | Pattern compression for git/npm/test output |\n| `ctx_search` | `grepSearch` | Compact, .gitignore-aware results |\n| `ctx_tree` | `listDirectory` | Compact directory maps with file counts |\n\n## When to use native Kiro tools instead\n\n- `fsWrite` / `fsAppend` — always use native (nebu-ctx doesn't write files)\n- `strReplace` — always use native (precise string replacement)\n- `semanticRename` / `smartRelocate` — always use native (IDE integration)\n- `getDiagnostics` — always use native (language server diagnostics)\n- `deleteFile` — always use native\n\n## Session management\n\n- At the start of a long task, call `ctx(domain=\"context\", action=\"overview\", task=...)` to map relevant project surface\n- Use `ctx(domain=\"memory\", action=\"wakeup\")` or `ctx(domain=\"memory\", action=\"recall\", query=...)` to restore useful context\n- Use `ctx(domain=\"memory\", action=\"remember\", ...)` to persist important discoveries across sessions\n\n## Rules\n\n- Public nebu-ctx surface is fixed to 5 tools: `ctx_read`, `ctx_search`, `ctx_tree`, `ctx_shell`, `ctx`\n- For large files, use `ctx_read` with `mode: \"signatures\"` or `mode: \"map\"` first\n- For re-reading a file you already read, call `ctx_read` again (cache hit = ~13 tok)\n- {}\n- When running tests or build commands, use `ctx_shell` for compressed output\n- {}\n",
        KIRO_STEERING_VERSION,
        batching_policy(),
        public_issue_policy_long()
    )
}

pub fn claude_md_block_content() -> String {
    format!(
        "<!-- nebu-ctx -->\n<!-- {} -->\n## nebu-ctx — Context Runtime\n\nAlways prefer nebu-ctx MCP tools over native equivalents:\n- `ctx_read` instead of `Read` / `cat`\n- `ctx_shell` instead of `bash` / `Shell`\n- `ctx_search` instead of `Grep` / `rg`\n- `ctx_tree` instead of `ls` / `find`\n- Use `ctx_read(target=\"files\", paths=[...])` instead of `multi_tool_use.parallel` with deferred `mcp_nebuctx_*` tools\n- Native Edit/StrReplace, Write, Delete, Glob stay unchanged\n- If a public nebu-ctx tool breaks reproducibly, use raw mode or repo-built nebu-ctx client, then automatically create or update a GitHub issue before handoff\n\nFull rules: @rules/nebu-ctx.md\n\nVerify setup: run `/mcp` to check nebu-ctx is connected, `/memory` to confirm this file loaded.\n<!-- /nebu-ctx -->",
        CLAUDE_MD_BLOCK_VERSION
    )
}

pub fn hermes_rules_template() -> String {
    format!(
        "# lean-ctx — Context Engineering Layer\n\nPREFER nebu-ctx MCP tools over native equivalents for token savings:\n\n| PREFER | OVER | Why |\n|--------|------|-----|\n| `ctx_read(target=..., mode)` | `Read` / `cat` | Cached public read modes, re-reads ~13 tokens |\n| `ctx_shell(command, shell_path?)` | `Shell` / `bash` | Pattern compression plus active shell visibility |\n| `ctx_search(mode=regex|semantic, ...)` | `Grep` / `rg` | Compact search results |\n| `ctx_tree(path, depth)` | `ls` / `find` | Compact directory maps |\n\n- Native Edit/StrReplace stay unchanged.\n- Write, Delete, Glob — use normally.\n- `ctx_shell` uses active shell semantics; output includes `[shell: ...]`. Use `shell_path` to force a specific executable per call.\n- {}\n- {}\n\nctx_read modes: full|map|signatures|diff|task|reference|aggressive|entropy|lines:N-M. Auto-selects optimal mode.\nRe-reads cost ~13 tokens (cached).\n\nUse only the public 5-tool surface in guidance.\n",
        batching_policy(),
        public_issue_policy_long()
    )
}

pub fn pi_agents_template() -> String {
    format!(
        "# nebu-ctx — Public MCP Guidance for Pi\n\nnebu-ctx is installed as a Pi Package with first-class MCP support. User-facing guidance should recommend only the public 5-tool MCP surface: `ctx_read`, `ctx_search`, `ctx_tree`, `ctx_shell`, and `ctx`.\n\n## Public MCP Surface\n\n- `ctx_read(target=\"file\"|\"files\"|\"symbol\"|\"outline\"|\"archive\", ...)`\n- `ctx_search(mode=\"regex\"|\"semantic\", ...)`\n- `ctx_tree(path, depth)`\n- `ctx_shell(command, shell_path?)`\n- `ctx(domain=\"memory\"|\"context\"|\"graph\"|\"analytics\"|\"agents\"|\"inspect\", action=\"...\")`\n\n## Guidance Rules\n\n- Prefer the 5 public tools above in all visible instructions and examples.\n- Do not recommend direct private `ctx_*` tool names unless you are explicitly documenting internal implementation details.\n- Use native edit/write/delete tools for mutations; nebu-ctx's public surface is read/search/tree/shell/ctx.\n- `ctx_shell` uses active shell semantics; output includes `[shell: ...]`. Use `shell_path` to force a specific executable per call.\n- {}\n\n## Public Examples\n\n```text\nctx_read(target=\"file\", path=\"src/main.rs\")\nctx_read(target=\"symbol\", name=\"main\", file=\"src/main.rs\")\nctx_search(mode=\"regex\", pattern=\"pub fn\", path=\"src/\")\nctx_search(mode=\"semantic\", query=\"session state persistence\", path=\"src/\")\nctx_tree(\".\", 2)\nctx_shell(\"git status\")\nctx(domain=\"memory\", action=\"recall\", query=\"session state decisions\")\n```\n\n## Memory policy\n\n- Use `ctx(domain=\"memory\", action=\"save\"|\"recall\")` for task state, findings, and decisions that should survive across chats.\n- Use `ctx(domain=\"memory\", action=\"store\")` for durable hosted project facts.\n- The stop/compact hooks already persist session state into the nebu-ctx server; rely on that instead of chat history for reusable memory.\n\n## No manual prefixing needed\n\nThe Pi extension handles routing automatically. Use the public nebu-ctx surface directly in guidance and examples.\n",
        public_issue_policy_long()
    )
}

pub fn windsurf_rules_template() -> String {
    format!(
        "# nebu-ctx — Public MCP Guidance\n\nnebu-ctx is configured as an MCP server. In guidance, always recommend only the public 5-tool MCP surface:\n\n- Read files, symbols, outlines, or archives -> `ctx_read(target=\"file\"|\"files\"|\"symbol\"|\"outline\"|\"archive\", ...)`\n- Shell commands -> `ctx_shell(command)` instead of built-in Shell\n- Search code -> `ctx_search(mode=\"regex\"|\"semantic\", ...)` instead of built-in Grep\n- List directories -> `ctx_tree(path, depth)` instead of ls/find\n- Memory, context, graph, analytics, agents, and inspect operations -> `ctx(domain=\"memory\"|\"context\"|\"graph\"|\"analytics\"|\"agents\"|\"inspect\", action=\"...\")`\n\nDo not recommend direct private `ctx_*` tool names in user-facing rules.\nUse native Edit/Write/Delete tools for mutations.\n{}\nWrite, StrReplace, Delete have no public nebu-ctx equivalent — use them normally.\n",
        public_issue_policy_long()
    )
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn canonical_guidance_mentions_auto_issue_flow() {
        let full = full_instruction_policy_block();
        let rules = rules_dedicated_markdown();
        let hermes = hermes_rules_template();

        assert!(full.contains("automatically create or update a GitHub issue"));
        assert!(rules.contains("gh issue create --repo MarkBovee/nebu-ctx"));
        assert!(hermes.contains("automatically create or update a GitHub issue"));
    }

    #[test]
    fn guidance_warns_about_deferred_parallel_wrapper_bug() {
        let rendered = [
            claude_code_instructions(),
            full_instruction_policy_block(),
            rules_shared_markdown(),
            rules_dedicated_markdown(),
            rules_cursor_mdc(),
            cursor_rules_template(),
            kiro_steering_template(),
            claude_md_block_content(),
            hermes_rules_template(),
        ];

        for text in rendered {
            assert!(text.contains("multi_tool_use.parallel"));
            assert!(text.contains("mcp_nebuctx"));
            assert!(text.contains("target=files") || text.contains("ctx_read(target=\"files\", paths=[...])"));
        }
    }

    #[test]
    fn rendered_guidance_stays_on_public_surface() {
        let rendered = [
            claude_code_instructions(),
            full_instruction_policy_block(),
            rules_shared_markdown(),
            rules_dedicated_markdown(),
            rules_cursor_mdc(),
            kiro_steering_template(),
        ];

        for text in rendered {
            assert!(text.contains("ctx_read"));
            assert!(text.contains("ctx_search"));
            assert!(text.contains("ctx_tree"));
            assert!(text.contains("ctx_shell"));
            assert!(text.contains("ctx"));
            assert!(!text.contains("project-bootstrap"));
            assert!(!text.contains("shell="));
        }
    }
}
