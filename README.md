```
  ███╗   ██╗███████╗██████╗ ██╗   ██╗      ██████╗████████╗██╗  ██╗
  ████╗  ██║██╔════╝██╔══██╗██║   ██║     ██╔════╝╚══██╔══╝╚██╗██╔╝
  ██╔██╗ ██║█████╗  ██████╔╝██║   ██║     ██║        ██║    ╚███╔╝
  ██║╚██╗██║██╔══╝  ██╔══██╗██║   ██║     ██║        ██║    ██╔██╗
  ██║ ╚████║███████╗██████╔╝╚██████╔╝     ╚██████╗   ██║   ██╔╝ ██╗
  ╚═╝  ╚═══╝╚══════╝╚═════╝  ╚═════╝       ╚═════╝   ╚═╝   ╚═╝  ╚═╝
          Context Runtime for AI Agents — Cloud Edition
```

<h3 align="center">Reduce Claude Code, Cursor & Copilot Token Costs by 60-99% — Open Source MCP Server + Cloud Dashboard</h3>

<p align="center">
  <strong>Shell Hook + Cloud Context Server · 49 tools · 10 read modes · 90+ patterns · Rust client · .NET cloud backend</strong>
</p>

<p align="center">
  <a href="https://github.com/MarkBovee/nebu-ctx/actions"><img src="https://github.com/MarkBovee/nebu-ctx/actions/workflows/ci.yml/badge.svg" alt="CI"></a>
  <a href="https://crates.io/crates/nebu-ctx"><img src="https://img.shields.io/crates/v/nebu-ctx?color=%23e6522c" alt="crates.io"></a>
  <a href="https://crates.io/crates/nebu-ctx"><img src="https://img.shields.io/crates/d/nebu-ctx?color=%23e6522c" alt="Downloads"></a>
  <a href="https://github.com/MarkBovee/nebu-ctx/pkgs/container/nebu-ctx"><img src="https://img.shields.io/badge/Container-GHCR-2496ED?logo=docker&logoColor=white" alt="Container"></a>
  <a href="LICENSE"><img src="https://img.shields.io/badge/License-Apache%202.0-blue.svg" alt="License"></a>
  <img src="https://img.shields.io/badge/Telemetry-Opt--in%20Only-brightgreen?logo=shield&logoColor=white" alt="Opt-in Telemetry">
</p>

<p align="center">
  <a href="#-get-started-60-seconds">Install</a> ·
  <a href="#-how-nebu-ctx-reduces-ai-token-costs">How It Works</a> ·
  <a href="#-49-intelligent-tools">49 Tools</a> ·
  <a href="#-cloud-dashboard">Dashboard</a> ·
  <a href="#-shell-hook-patterns-90">Patterns</a> ·
  <a href="HANDOVER.md">Architecture</a> ·
  <a href="docs/TOOLS.md">Full Tool Docs</a>
</p>

---

<br>

> **nebu-ctx** reduces LLM token consumption by **60-99%** through three complementary strategies: a Rust shell hook, 49 cloud-backed MCP tools, and a persistent .NET server with real-time Observatory dashboard — making AI coding faster, cheaper, and fully observable. Cached re-reads drop to **~13 tokens (99% savings)**.

<br>

## ⚡ How nebu-ctx Reduces AI Token Costs

```
  Without nebu-ctx:                              With nebu-ctx:

  LLM ──"read auth.ts"──▶ Editor ──▶ File       LLM ──"ctx_read auth.ts"──▶ nebu-ctx ──▶ File
    ▲                                  │           ▲                            │            │
    │      ~2,000 tokens (full file)   │           │   ~13 tokens (cached)      │ cloud+hash │
    └──────────────────────────────────┘           └────── (compressed) ────────┴────────────┘

  LLM ──"git status"──▶  Shell  ──▶  git        LLM ──"git status"──▶  nebu-ctx  ──▶  git
    ▲                                 │            ▲                        │              │
    │     ~800 tokens (raw output)    │            │   ~150 tokens          │ compress     │
    └─────────────────────────────────┘            └────── (filtered) ──────┴──────────────┘
```

| Strategy | How | Impact |
|:---|:---|:---|
| **Shell Hook** | Transparently compresses CLI output (90+ patterns) before it reaches the LLM | **60-95%** savings |
| **Cloud Context Server** | 49 MCP tools backed by .NET server + PostgreSQL — cached reads, 10 modes, multi-agent memory, project analytics | **74-99%** savings |
| **Live Observatory** | Real-time dashboard at `localhost:3333` — see every tool call, savings, agent sessions, and knowledge graph | **Full observability** |

<br>

## 🎯 Token Savings — Real Numbers

| Operation | Mode | Freq | Without | With nebu-ctx | Saved |
|:---|:---|:---:|---:|---:|:---:|
| File reads (cached) | Cloud cache | 15× | 30,000 | 195 | **99%** |
| File reads (map mode) | MCP tool | 10× | 20,000 | 2,000 | **90%** |
| ls / find | Shell hook | 8× | 6,400 | 1,280 | **80%** |
| git status/log/diff | Shell hook | 10× | 8,000 | 2,400 | **70%** |
| grep / rg | Shell hook | 5× | 8,000 | 2,400 | **70%** |
| cargo/npm build | Shell hook | 5× | 5,000 | 1,000 | **80%** |
| Test runners | Shell hook | 4× | 10,000 | 1,000 | **90%** |
| curl (JSON) | Shell hook | 3× | 1,500 | 165 | **89%** |
| **Session total** | | | **~89,800** | **~10,620** | **88%** |

> Based on real Cursor/Claude Code sessions. Cached re-reads cost ~13 tokens.

<br>

## 🚀 Get Started (60 seconds)

```bash
# 1. Install the Rust client (pick one)
cargo install nebu-ctx                    # from crates.io
# or download the pre-built binary from GitHub Releases

# 2. Start the cloud server (Docker / Podman)
podman run -d --name nebu-ctx \
  -p 3333:3333 -p 4242:4242 \
  -e NEBULA_CTX_HTTP_TOKEN=<your-token> \
  ghcr.io/markbovee/nebu-ctx

# 3. Connect client to server
nebu-ctx cloud bind        # from your project directory

# 4. Setup shell + editors
nebu-ctx setup

# 5. Verify
nebu-ctx doctor

# 6. Open the dashboard
nebu-ctx dashboard         # opens http://localhost:3333
```

<details>
<summary><strong>Local development setup</strong></summary>

```bash
git clone https://github.com/MarkBovee/nebu-ctx
cd nebu-ctx

# Build and start server
cp .env.example .env       # edit NEBULA_CTX_HTTP_TOKEN
podman build -t nebu-ctx-server -f Dockerfile .
podman run -d --name nebu-ctx-local -p 3333:3333 -p 4242:4242 --env-file .env nebu-ctx-server

# Build client
cargo build --manifest-path client/Cargo.toml

# Bind your project
cd /path/to/your-project
/path/to/nebu-ctx/client/target/debug/nebu-ctx cloud bind
```

</details>

<details>
<summary><strong>Supported editors (auto-detected by <code>nebu-ctx setup</code>)</strong></summary>

| Editor | Method | Status |
|:---|:---|:---:|
| **Claude Code** | MCP + PreToolUse hooks + rules | ✅ Auto |
| **GitHub Copilot** | MCP | ✅ Auto |
| **Cursor** | MCP + hooks + rules | ✅ Auto |
| **VS Code** | MCP + rules | ✅ Auto |
| **Windsurf** | MCP + rules | ✅ Auto |
| **Zed** | Context Server | ✅ Auto |
| **Codex CLI** | config.toml + AGENTS.md | ✅ Auto |
| **Gemini CLI** | MCP + hooks + rules | ✅ Auto |
| **OpenCode** | MCP + rules | ✅ Auto |

</details>

<br>

## 🖥 Cloud Dashboard

nebu-ctx ships a built-in real-time Observatory dashboard at `http://localhost:3333`.

| Section | What you see |
|:---|:---|
| **Overview** | All-time tokens saved, cost saved ($), Gain Score, Buddy level |
| **Live Observatory** | Real-time event feed — every tool call with mode, project, and savings |
| **Knowledge Graph** | `ctx_knowledge` facts — categories, values, creation time, cascade delete |
| **Dependency Map** | Project module dependency graph |
| **Compression Lab** | Interactive file compressor — see before/after token counts live |
| **Agent World** | Active agent sessions, pending messages, shared contexts |
| **Bug Memory** | `ctx_gotchas` — patterns that triggered unusual compression |
| **Brain Memory** | All `ctx_brain` stored memories, grouped by project |
| **Search Explorer** | Full-text search across all indexed project files and symbols |
| **Learning Curves** | Auto-learned entropy and Jaccard thresholds per language |
| **Symbol Explorer** | Project symbol index — functions, classes, constants (per project) |
| **Call Graph** | Cross-file dependency edges and symbol call references (per project) |
| **Route Map** | HTTP routes extracted from source files |
| **Context Layer** | Context pressure, tokens sent/saved per session |
| **MCP Token** | View and rotate the active MCP authentication token |

Open the dashboard:
```bash
nebu-ctx dashboard
# or: open http://localhost:3333?token=<your-token>
```

<br>

## 🧠 Three Intelligence Protocols

<table>
<tr>
<td width="33%">

### CEP
**Cognitive Efficiency Protocol**

Adaptive LLM communication with compliance scoring (0-100), task complexity classification, quality scoring, auto-validation pipeline.

*Measurable efficiency gains*

</td>
<td width="33%">

### CCP
**Context Continuity Protocol**

Cross-session memory that persists tasks, findings, decisions across chats. LITM-aware positioning for optimal attention placement. Backed by PostgreSQL — survives context resets.

*-99.2% cold-start tokens*

</td>
<td width="33%">

### TDD
**Token Dense Dialect**

Symbol shorthand (`λ` `§` `∂` `τ` `ε`) and ROI-based identifier mapping for compact LLM communication.

*8-25% extra savings*

</td>
</tr>
</table>

<br>

## 🛠 49 Intelligent Tools

### Core

| Tool | Purpose | Savings |
|:---|:---|:---:|
| `ctx_read` | File reads — 10 modes (incl. `lines:N-M`), cloud caching, `fresh=true` | 74-99% |
| `ctx_multi_read` | Multiple file reads in one round trip | 74-99% |
| `ctx_tree` | Directory listings (ls, find, Glob) | 34-60% |
| `ctx_shell` | Shell commands with 90+ compression patterns, cwd tracking | 60-90% |
| `ctx_search` | Code search (Grep) | 50-80% |
| `ctx_compress` | Context checkpoint for long conversations | 90-99% |

### Intelligence

| Tool | What it does |
|:---|:---|
| `ctx_smart_read` | Adaptive mode — auto-picks full/map/signatures/diff based on file type and cache |
| `ctx_delta` | Incremental updates — only sends changed hunks via Myers diff |
| `ctx_dedup` | Cross-file deduplication — finds shared imports and boilerplate |
| `ctx_fill` | Priority-based context filling — maximizes info within a token budget |
| `ctx_intent` | Semantic intent detection — classifies queries and auto-loads files |
| `ctx_response` | Response compression — removes filler, applies TDD |
| `ctx_context` | Multi-turn session overview — tracks what the LLM already knows |
| `ctx_graph` | Project intelligence graph — dependency analysis + related file discovery |
| `ctx_discover` | Shell history analysis — finds missed compression opportunities |
| `ctx_edit` | Search-and-replace file editing — works without native Read/Edit tools |
| `ctx_overview` | Task-relevant project map — use at session start |
| `ctx_preload` | Proactive context loader — caches task-relevant files, returns compact summary |
| `ctx_semantic_search` | BM25 code search by meaning — finds symbols and patterns across the project |
| `ctx_impact` | Measures impact of code changes via dependency chain analysis |
| `ctx_architecture` | Generates architectural overview from dependency graph and module structure |
| `ctx_heatmap` | File access heatmap — tracks read counts, compression ratios, access patterns |

### Memory & Multi-Agent (Cloud-Backed)

| Tool | What it does |
|:---|:---|
| `ctx_session` | Cross-session memory — persist task, findings, decisions across chats (PostgreSQL) |
| `ctx_knowledge` | Persistent project knowledge — remember facts, recall by query/category (PostgreSQL) |
| `ctx_brain` | Long-term agent memory — store/recall key-value facts scoped to project (PostgreSQL) |
| `ctx_agent` | Multi-agent coordination — register, post/read scratchpad, handoff tasks, sync status |
| `ctx_share` | Multi-agent context sharing — push/pull cached file contexts between agents |
| `ctx_wrapped` | Shareable savings report — "Spotify Wrapped" for your tokens |
| `ctx_task` | A2A task orchestration — create, assign, update, complete multi-agent tasks |
| `ctx_cost` | Cost attribution per agent — record, summarize, track token usage (cloud analytics) |

### Analysis

| Tool | What it does |
|:---|:---|
| `ctx_benchmark` | Single-file or project-wide benchmark with preservation scores |
| `ctx_metrics` | Session statistics with USD cost estimates |
| `ctx_analyze` | Shannon entropy analysis + mode recommendation |
| `ctx_cache` | Cache management: status, clear, invalidate |
| `ctx_gain` | Gain score — measures context efficiency improvement over baseline |
| `ctx_stats` | Per-command token statistics from the cloud server |

> 📄 Full tool reference with all parameters: [docs/TOOLS.md](docs/TOOLS.md)

<br>

## 📖 ctx_read Modes

| Mode | When to use | Token cost |
|:---|:---|:---|
| `full` | Files you will edit (cached re-reads ≈ 13 tokens) | 100% first, ~0% cached |
| `map` | Understanding a file — deps + exports + API | ~5-15% |
| `signatures` | API surface with more detail than map | ~10-20% |
| `diff` | Re-reading files that changed | changed lines only |
| `aggressive` | Large files with boilerplate | ~30-50% |
| `entropy` | Repetitive patterns (Shannon + Jaccard filtering) | ~20-40% |
| `task` | Task-relevant content via Information Bottleneck + KG filtering | ~15-35% |
| `reference` | Compact function references (F1, F2…) for delta-only follow-ups | ~5-10% |
| `lines:N-M` | Specific ranges (e.g. `lines:10-50,80-90`) | proportional |
| `auto` | Auto-select optimal mode (recommended default) | varies |

<br>

## 🔌 Shell Hook Patterns (90+)

Pattern-based compression for **90+ commands** across **34 categories**:

<details>
<summary><strong>View all 34 categories</strong></summary>

| Category | Commands | Savings |
|:---|:---|:---:|
| **Git** (19) | status, log, diff, add, commit, push, pull, fetch, clone, branch, checkout, switch, merge, stash, tag, reset, remote, blame, cherry-pick | 70-95% |
| **Docker** (10) | build, ps, images, logs, compose ps/up/down, exec, network, volume, inspect | 70-90% |
| **npm/pnpm/yarn** (6) | install, test, run, list, outdated, audit | 70-90% |
| **Cargo** (3) | build, test, clippy | 80% |
| **GitHub CLI** (9) | pr list/view/create/merge, issue list/view/create, run list/view | 60-80% |
| **Kubernetes** (8) | get pods/services/deployments, logs, describe, apply, delete, exec, top, rollout | 60-85% |
| **Python** (7) | pip install/list/outdated/uninstall/check, ruff check/format | 60-80% |
| **Ruby** (4) | rubocop, bundle install/update, rake test, rails test | 60-85% |
| **Linters** (4) | eslint, biome, prettier, stylelint | 60-70% |
| **Build Tools** (3) | tsc, next build, vite build | 60-80% |
| **Test Runners** (8) | jest, vitest, pytest, go test, playwright, cypress, rspec, minitest | 90% |
| **Terraform** | init, plan, apply, destroy, validate, fmt, state, import, workspace | 60-85% |
| **Make** | make targets, parallel jobs, dry-run | 60-80% |
| **Maven / Gradle** | compile, test, package, install, clean, dependency trees | 60-85% |
| **.NET** | dotnet build, test, restore, run, publish, pack | 60-85% |
| **Flutter / Dart** | flutter pub, analyze, test, build; dart pub, analyze, test | 60-85% |
| **Poetry / uv** | install, sync, lock, run, add, remove; uv pip/sync/run | 60-85% |
| **AWS** (7) | s3, ec2, lambda, cloudformation, ecs, logs, sts | 60-80% |
| **Databases** (2) | psql, mysql/mariadb | 50-80% |
| **Prisma** (6) | generate, migrate, db push/pull, format, validate | 70-85% |
| **Helm** (5) | list, install, upgrade, status, template | 60-80% |
| **Bun** (3) | test, install, build | 60-85% |
| **Deno** (5) | test, lint, check, fmt, task | 60-85% |
| **Swift** (3) | test, build, package resolve | 60-80% |
| **Zig** (2) | test, build | 60-80% |
| **CMake** (3) | configure, build, ctest | 60-80% |
| **Ansible** (2) | playbook recap, task summary | 60-80% |
| **Composer** (3) | install, update, outdated | 60-80% |
| **Mix** (5) | test, deps, compile, format, credo/dialyzer | 60-80% |
| **Bazel** (3) | test, build, query | 60-80% |
| **systemd** (2) | systemctl, journalctl | 50-80% |
| **Utils** (5) | curl, grep/rg, find, ls, wget | 50-89% |
| **Data** (3) | env (filtered), JSON schema extraction, log dedup | 50-80% |

</details>

After `nebu-ctx setup`, **23 commands** are transparently compressed via shell hooks:

```
git · npm · pnpm · yarn · cargo · docker · docker-compose · kubectl · k
gh · pip · pip3 · ruff · go · golangci-lint · eslint · prettier · tsc
ls · find · grep · curl · wget
```

Each time your shell starts, nebu-ctx prints a brief status line showing the active version and cloud connection:

```
  ◈ nebu-ctx v0.6.1  ·  ON  ·  cloud → 192.168.1.135:4242
```

> `nebu-ctx setup` automatically runs `init --global`, so the shell hook is always up-to-date after setup. No manual `source` step needed after the first install.

<br>

## 📊 CLI Commands

<details>
<summary><strong>Shell & File Operations</strong></summary>

```bash
nebu-ctx -c "git status"                    # Execute + compress output
nebu-ctx read file.rs                       # Full content
nebu-ctx read file.rs -m map               # Deps + API signatures (~10% tokens)
nebu-ctx read file.rs -m signatures        # Function signatures only
nebu-ctx read file.rs -m "lines:10-50"     # Specific line ranges
nebu-ctx grep "pattern" src/               # Grouped search results
nebu-ctx ls src/                           # Token-optimized directory listing
```

</details>

<details>
<summary><strong>Cloud & Project Management</strong></summary>

```bash
nebu-ctx cloud bind                        # Bind current directory to cloud server
nebu-ctx cloud bind --server http://...    # Bind to specific server instance
nebu-ctx cloud status                      # Show current cloud connection
```

</details>

<details>
<summary><strong>Setup & Analytics</strong></summary>

```bash
nebu-ctx setup                             # One-command setup: shell + editors + verify (runs init --global)
nebu-ctx init --global                     # Regenerate shell hook for current shell
nebu-ctx init --agent claude               # Claude Code MCP + hook
nebu-ctx init --agent cursor               # Cursor hooks.json
nebu-ctx init --agent gemini               # Gemini CLI hook
nebu-ctx init --agent copilot              # GitHub Copilot MCP
nebu-ctx on-brief                          # Print colored status line (used by shell startup hook)
nebu-ctx gain                              # Terminal token savings dashboard
nebu-ctx gain --live                       # Live auto-updating dashboard
nebu-ctx gain --json                       # Raw JSON export
nebu-ctx dashboard                         # Open web dashboard (localhost:3333)
nebu-ctx doctor                            # Diagnostics
nebu-ctx update                            # Self-update
nebu-ctx wrapped                           # Shareable savings report
```

</details>

<details>
<summary><strong>Home Assistant Add-on</strong></summary>

nebu-ctx ships as a native Home Assistant add-on for self-hosted deployments:

```yaml
# homeassistant/config.yaml
version: "0.5.6"
ports:
  3333/tcp: 3333
  4242/tcp: 4242
```

Install via the HA add-on store or manually by copying the `homeassistant/` directory to your HA add-ons folder.

</details>

<br>

## 🏗 Architecture

nebu-ctx is a two-process system:

```
┌──────────────────────────────────────────────────────────────────┐
│  AI Editor (Claude Code / Cursor / Copilot / etc.)               │
│  ↓ MCP stdio (tools/call)            ↑ compressed context        │
├──────────────────────────────────────────────────────────────────┤
│  Rust Client  (nebu-ctx binary)                                  │
│  • MCP stdio server — exposes all 49 tools to editors            │
│  • Shell hook — fish/zsh/bash, 90+ CLI compression patterns      │
│  • Claude Code hooks (7 types):                                  │
│      PreToolUse:Bash        → nebu-ctx hook rewrite              │
│      PreToolUse:Read/Grep   → nebu-ctx hook redirect             │
│      PostToolUse:.*         → nebu-ctx hook post-tool-use        │
│      Stop                   → nebu-ctx hook stop                 │
│      PreCompact             → nebu-ctx hook pre-compact          │
│      SessionStart           → nebu-ctx hook session-start        │
│      UserPromptSubmit       → nebu-ctx hook user-prompt-submit   │
│  • Proxies CLOUD_ONLY / CLOUD_PREFERRED tools to .NET server     │
├──────────────────────────────────────────────────────────────────┤
│  .NET 10 Server  (port 4242 MCP HTTP · port 3333 dashboard)      │
│  • ctx_brain / ctx_knowledge / ctx_session — PostgreSQL          │
│  • ctx_gain / ctx_cost / ctx_stats / ctx_heatmap / ctx_routes    │
│  • /api/events — polled by Live Observatory (dashboard)          │
│  • /v1/tools/call — MCP HTTP endpoint                            │
│  • /v1/telemetry/ingest — per-call telemetry from client         │
│  • /v1/index/sync — code index (files, symbols, call edges)      │
│  • Dashboard SPA — served at localhost:3333                      │
└──────────────────────────────────────────────────────────────────┘
```

**CLOUD_ONLY tools** (always routed to server): `ctx_brain`, `ctx_routes`, `ctx_gain`, `ctx_cost`, `ctx_heatmap`, `ctx_stats`  
**CLOUD_PREFERRED tools** (try cloud, fall back to local): `ctx_knowledge`, `ctx_session`  
**LOCAL tools** (all others): handled entirely in the Rust client

<br>

## 🔬 Scientific Compression Engine

Built on information theory and attention modeling:

| Feature | What it does | Impact |
|:---|:---|:---:|
| **Adaptive Entropy** | Per-language BPE entropy + Jaccard thresholds with Kolmogorov adjustment | 10-25% |
| **Attention Model** | Heuristic U-curve positional weighting + structural importance scoring | ↑ comprehension |
| **TF-IDF Codebook** | Cross-file pattern dedup via cosine similarity | 5-15% |
| **Feedback Loop** | Learns optimal thresholds per language/file type — stored in cloud server | auto-improving |
| **Info Bottleneck** | Entropy + task-relevance filtering (Tishby et al., 2000) | 20-40% |
| **ctx_overview** | Multi-resolution project map with graph-based relevance tiers | 90%+ |

<br>

## 🌳 tree-sitter Signature Engine

AST-based signature extraction for **18 languages**: TypeScript, JavaScript, Rust, Python, Go, Java, C, C++, Ruby, C#, Kotlin, Swift, PHP, Bash, Dart, Scala, Elixir, Zig.

<br>

## ⚙️ Editor Configuration

> **`nebu-ctx setup` handles this automatically.** Manual config below is only for edge cases.

<details>
<summary><strong>Claude Code</strong></summary>

```bash
nebu-ctx init --agent claude
# Or manually:
claude mcp add-json --scope user nebu-ctx '{"command":"nebu-ctx","args":["mcp"]}'
```

</details>

<details>
<summary><strong>GitHub Copilot (VS Code)</strong></summary>

`~/.config/Code/User/mcp.json`:
```json
{
  "servers": {
    "nebu-ctx": { "command": "nebu-ctx", "args": ["mcp"] }
  }
}
```

</details>

<details>
<summary><strong>Cursor</strong></summary>

`~/.cursor/mcp.json`:
```json
{
  "mcpServers": {
    "nebu-ctx": { "command": "nebu-ctx", "args": ["mcp"] }
  }
}
```

</details>

<details>
<summary><strong>Windsurf</strong></summary>

`~/.codeium/windsurf/mcp_config.json`:
```json
{
  "mcpServers": {
    "nebu-ctx": { "command": "nebu-ctx", "args": ["mcp"] }
  }
}
```

</details>

<details>
<summary><strong>Generic .mcp.json (project-level)</strong></summary>

`.mcp.json` in your project root:
```json
{
  "mcpServers": {
    "nebu-ctx": { "command": "nebu-ctx", "args": ["mcp"] }
  }
}
```

</details>

<br>

## 🔐 Privacy & Security

- **Opt-in telemetry only** — no tracking, no analytics, no PII leaves your machine unless you explicitly enable it
- **Fully local compression** — all shell hook and local MCP tools run in-process, no network calls
- **Self-hosted server** — cloud server runs on your own machine or infrastructure; no SaaS required
- **Auditable** — Apache 2.0 licensed, open source Rust + .NET stack

<br>

## 🗑 Uninstall

```bash
nebu-ctx uninstall      # Removes shell hooks and editor configs
cargo uninstall nebu-ctx
rm -rf ~/.nebu-ctx      # Removes config and local state
podman rm -f nebu-ctx   # Stop and remove server container
```

<br>

## ❓ Frequently Asked Questions

<details>
<summary><strong>How much can nebu-ctx save?</strong></summary>

Active developers typically save **$30-100+ per month** on AI API costs. The exact amount depends on your editor, model, and usage patterns. Run `nebu-ctx gain` to see your exact savings.

</details>

<details>
<summary><strong>Does it work with Claude Code / Cursor / Copilot?</strong></summary>

Yes — nebu-ctx supports all major AI coding editors. Run `nebu-ctx setup` and it auto-detects and configures all installed editors.

</details>

<details>
<summary><strong>Does it slow down my AI tool?</strong></summary>

No. The Rust binary adds <1ms overhead. The cloud server is persistent (no cold starts). Most users report their AI tools feel **faster** because less data means faster LLM responses.

</details>

<details>
<summary><strong>Do I need to run the cloud server?</strong></summary>

For local tools (`ctx_read`, `ctx_shell`, `ctx_search`, `ctx_tree`, etc.) — no. The Rust binary handles these entirely locally. For cloud-only tools (`ctx_brain`, `ctx_gain`, `ctx_stats`, `ctx_heatmap`, `ctx_cost`) and the web dashboard — yes, the .NET server needs to be running.

</details>

<details>
<summary><strong>What's the difference between nebu-ctx and lean-ctx?</strong></summary>

nebu-ctx extends lean-ctx with a persistent **cloud backend** (.NET + PostgreSQL), a **real-time Observatory dashboard**, server-backed memory (`ctx_brain`, `ctx_knowledge`), cloud analytics (`ctx_gain`, `ctx_cost`, `ctx_heatmap`, `ctx_stats`), and multi-project management via `nebu-ctx cloud bind`. The Rust client is wire-compatible with the lean-ctx tool interface.

</details>

<br>

## 🤝 Contributing

Contributions welcome! See [AGENTS.md](AGENTS.md) for the multi-agent development workflow and [docs/DEVELOPER-KNOWLEDGE.md](docs/DEVELOPER-KNOWLEDGE.md) for architecture details.

<br>

## 📄 License

Apache License 2.0 — see [LICENSE](LICENSE).

<br>

<p align="center">
  <sub>Rust client · .NET 10 server · PostgreSQL · Built with ❤️</sub>
</p>
