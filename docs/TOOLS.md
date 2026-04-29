## 🛠 49 Intelligent Tools

### Core

| Tool | Purpose | Savings |
|:---|:---|:---:|
| `ctx_read` | File reads — 10 modes (incl. `lines:N-M`), caching, `fresh=true` | 74-99% |
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

### Memory & Multi-Agent

| Tool | What it does |
|:---|:---|
| `ctx_session` | Cross-session memory — persist task, findings, decisions across chats |
| `ctx_knowledge` | Persistent project knowledge — remember facts, recall by query/category |
| `ctx_agent` | Multi-agent coordination — register, post/read scratchpad, handoff tasks, sync status |
| `ctx_share` | Multi-agent context sharing — push/pull cached file contexts between agents |
| `ctx_wrapped` | Shareable savings report — "Spotify Wrapped" for your tokens |
| `ctx_task` | A2A task orchestration — create, assign, update, complete multi-agent tasks |
| `ctx_cost` | Cost attribution per agent — record, summarize, track token usage |

### Analysis

| Tool | What it does |
|:---|:---|
| `ctx_benchmark` | Single-file or project-wide benchmark with preservation scores |
| `ctx_metrics` | Session statistics with USD cost estimates |
| `ctx_analyze` | Shannon entropy analysis + mode recommendation |
| `ctx_cache` | Cache management: status, clear, invalidate |

### Integrations

| Package | What it provides |
|:---|:---|
| **VS Code Extension** (`packages/vscode-lean-ctx`) | Status bar token savings, one-click setup, MCP auto-config |
| **Chrome Extension** (`packages/chrome-lean-ctx`) | Auto-compress pastes in ChatGPT, Claude, Gemini via native messaging |

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

After `lean-ctx init --global`, **23 commands** are transparently compressed via shell aliases:

```
git · npm · pnpm · yarn · cargo · docker · docker-compose · kubectl · k
gh · pip · pip3 · ruff · go · golangci-lint · eslint · prettier · tsc
ls · find · grep · curl · wget
```
