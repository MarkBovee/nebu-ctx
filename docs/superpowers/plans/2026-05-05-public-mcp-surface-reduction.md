# Public MCP Surface Reduction Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the public MCP contract with exactly five tools and make the Rust client enforce the new `ctx_read`, `ctx_search`, `ctx_tree`, `ctx_shell`, and `ctx` surface without a compatibility layer.

**Architecture:** The Rust client becomes the canonical contract boundary. Public manifests, MCP tool listings, HTTP listings, and generated instructions all expose only the five-tool surface. Internally, the client may still call existing local or server-backed handlers, but only as private dispatch details behind the new `ctx_read` targets, `ctx_search` modes, and `ctx(domain, action)` routing.

**Tech Stack:** Rust client, rmcp, Axum HTTP bridge, Markdown docs, Cargo tests

---

## File Map

- Modify: `client/src/tool_defs/mod.rs`
  - Remove lazy/granular public discovery from the canonical MCP surface
- Modify: `client/src/tool_defs/granular.rs`
  - Replace the public tool definitions with the new five-tool schemas
- Modify: `client/src/mcp_server/mod.rs`
  - Enforce five-tool exposure and switch `ctx` parsing to `domain` + `action`
- Modify: `client/src/mcp_server/dispatch.rs`
  - Route `ctx_read`, `ctx_search`, and `ctx` domains to private handlers
- Modify: `client/src/core/mcp_manifest.rs`
  - Publish only the canonical five-tool manifest
- Modify: `client/src/mcp_http/mod.rs`
  - Return only the canonical five tools from `/v1/tools`
- Modify: `client/src/instructions.rs`
  - Rewrite agent guidance around the new public contract
- Modify: `client/src/core/editor_registry/writers.rs`
  - Update tool lists emitted to editor integrations
- Modify: `client/src/core/gain/task_classifier.rs`
  - Reclassify tasks using the new tool names
- Modify: `client/src/core/loop_detection.rs`
  - Treat `ctx_search` as the only public search tool
- Modify: `README.md`
  - Rewrite the documented MCP surface and examples
- Modify: `docs/TOOLS.md`
  - Replace the 48/49-tool catalog with the canonical five-tool API doc
- Test: `client/src/mcp_server/mod.rs`
  - Add public-contract regression tests

### Task 1: Lock the public contract in tests

**Files:**
- Modify: `client/src/mcp_server/mod.rs:804-910`

- [ ] **Step 1: Write the failing tests**

Add tests in `client/src/mcp_server/mod.rs` that assert:

```rust
#[test]
fn public_tool_count_is_exactly_five() {
    let tools = crate::tool_defs::unified_tool_defs();
    let names: Vec<_> = tools.iter().map(|t| t.name.as_ref()).collect();
    assert_eq!(names, vec!["ctx_read", "ctx_search", "ctx_tree", "ctx_shell", "ctx"]);
}

#[test]
fn public_manifest_contains_only_public_tools() {
    let manifest = crate::core::mcp_manifest::manifest_value();
    let tools = manifest["tools"].as_array().unwrap();
    assert_eq!(tools.len(), 5);
}

#[test]
fn ctx_requires_domain_and_action_in_public_mode() {
    let rt = tokio::runtime::Builder::new_current_thread().enable_all().build().unwrap();
    let engine = crate::engine::ContextEngine::new();
    let text = rt.block_on(engine.call_tool_text("ctx", Some(serde_json::json!({ "tool": "knowledge" })))).unwrap();
    assert!(text.contains("domain"));
}
```

- [ ] **Step 2: Run the focused tests to verify they fail**

Run: `cargo test --manifest-path client/Cargo.toml public_tool_count_is_exactly_five public_manifest_contains_only_public_tools ctx_requires_domain_and_action_in_public_mode -- --exact`

Expected: FAIL because the manifest still exposes granular tools and `ctx(tool=...)` still works.

- [ ] **Step 3: Commit no code yet**

Do not commit yet.

### Task 2: Replace public tool definitions and listings

**Files:**
- Modify: `client/src/tool_defs/mod.rs`
- Modify: `client/src/tool_defs/granular.rs`
- Modify: `client/src/core/mcp_manifest.rs`
- Modify: `client/src/mcp_server/mod.rs:134-187`
- Modify: `client/src/mcp_http/mod.rs:249-271`

- [ ] **Step 1: Replace the public tool definitions with the canonical five tools**

Update `client/src/tool_defs/granular.rs` so `unified_tool_defs()` returns only:

```rust
vec![
    tool_def("ctx_read", ...),
    tool_def("ctx_search", ...),
    tool_def("ctx_tree", ...),
    tool_def("ctx_shell", ...),
    tool_def("ctx", ...),
]
```

with these public contracts:

- `ctx_read`: `target=file|files|symbol|outline|archive`
- `ctx_search`: `mode=regex|semantic`
- `ctx`: `domain=memory|context|graph|analytics|agents|inspect`, `action=...`

- [ ] **Step 2: Make all public listing surfaces use only the five tools**

Update:

- `client/src/mcp_server/mod.rs` so `list_tools()` always uses `unified_tool_defs()`
- `client/src/mcp_http/mod.rs` so `/v1/tools` reads the canonical tools array
- `client/src/core/mcp_manifest.rs` so `manifest_value()` publishes a single `tools` array with the five public tools

- [ ] **Step 3: Re-run the focused tests to verify the contract is now green**

Run: `cargo test --manifest-path client/Cargo.toml public_tool_count_is_exactly_five public_manifest_contains_only_public_tools -- --exact`

Expected: PASS.

### Task 3: Replace `ctx(tool=...)` with `ctx(domain, action)` and re-route public tools

**Files:**
- Modify: `client/src/mcp_server/mod.rs:189-264`
- Modify: `client/src/mcp_server/dispatch.rs`

- [ ] **Step 1: Make `ctx(domain, action)` the only accepted public shape**

Update `client/src/mcp_server/mod.rs` so `ctx`:

- requires `domain`
- requires `action`
- rejects `tool`
- resolves to one of:
  - `ctx_memory`
  - `ctx_context`
  - `ctx_graph`
  - `ctx_analytics`
  - `ctx_agents`
  - `ctx_inspect`

If invalid, return `invalid_params` text describing the new contract.

- [ ] **Step 2: Route `ctx_read` targets and `ctx_search` modes inside `dispatch.rs`**

Implement private dispatch so:

- `ctx_read(target="file")` uses the existing read path
- `ctx_read(target="files")` uses the existing multi-read path
- `ctx_read(target="symbol")` uses the existing symbol path
- `ctx_read(target="outline")` uses the existing outline path
- `ctx_read(target="archive")` uses the existing archive retrieval path
- `ctx_search(mode="regex")` uses the existing regex search path
- `ctx_search(mode="semantic")` uses the existing semantic search path
- `ctx(domain="memory")` routes to the memory-related private handlers
- `ctx(domain="context")` routes to context-related private handlers
- `ctx(domain="graph")` routes to graph-related private handlers
- `ctx(domain="analytics")` routes to analytics-related private handlers
- `ctx(domain="agents")` routes to agent/workflow private handlers
- `ctx(domain="inspect")` routes to inspect/admin private handlers

- [ ] **Step 3: Re-run the domain contract test**

Run: `cargo test --manifest-path client/Cargo.toml ctx_requires_domain_and_action_in_public_mode -- --exact`

Expected: PASS.

### Task 4: Rewrite generated instructions and integration tool lists

**Files:**
- Modify: `client/src/instructions.rs`
- Modify: `client/src/core/editor_registry/writers.rs`
- Modify: `client/src/core/gain/task_classifier.rs`
- Modify: `client/src/core/loop_detection.rs`

- [ ] **Step 1: Update generated instructions to mention only the five public tools**

Replace old text such as:

```text
ctx_semantic_search for meaning-based search. ctx_session for memory. ctx_knowledge...
ctx(tool="<name>", ...params)
```

with new text such as:

```text
Use ctx_read for files, symbols, outlines, and archived output.
Use ctx_search for regex and semantic code search.
Use ctx(domain="memory|context|graph|analytics|agents|inspect", action="...") for higher-level workflows.
```

- [ ] **Step 2: Update integration writers and classification helpers**

Adjust editor-generated tool lists and helper classifiers so the new tool names are canonical.

- [ ] **Step 3: Run a focused instruction test if available, otherwise run a focused client test covering instructions**

Run: `cargo test --manifest-path client/Cargo.toml test_unified_tool_count -- --exact`

Expected: PASS and no stale public contract wording remains in the relevant files.

### Task 5: Rewrite the docs to the new model

**Files:**
- Modify: `README.md`
- Modify: `docs/TOOLS.md`

- [ ] **Step 1: Rewrite `README.md` around the five-tool public contract**

Update the MCP surface, routing explanation, and examples so only these tools appear publicly:

- `ctx_read`
- `ctx_search`
- `ctx_tree`
- `ctx_shell`
- `ctx`

- [ ] **Step 2: Rewrite `docs/TOOLS.md` as the canonical public API document**

Document:

- the five public tools
- `ctx_read` targets
- `ctx_search` modes
- `ctx` domains and example actions
- rationale: fewer tokens, lower confusion, better flow

- [ ] **Step 3: Verify docs no longer advertise the old public tool explosion**

Run: `rg -n "ctx_multi_read|ctx_semantic_search|ctx_session|ctx_knowledge|49 Intelligent Tools|48 `ctx_`" README.md docs/TOOLS.md`

Expected: no matches.

### Task 6: Run end-to-end verification

**Files:**
- Verify only

- [ ] **Step 1: Run the focused client tests used during TDD**

Run: `cargo test --manifest-path client/Cargo.toml public_tool_count_is_exactly_five public_manifest_contains_only_public_tools ctx_requires_domain_and_action_in_public_mode -- --exact`

Expected: PASS.

- [ ] **Step 2: Run the relevant client suite**

Run: `cargo test --manifest-path client/Cargo.toml`

Expected: PASS.

- [ ] **Step 3: Spot-check HTTP manifest/listing behavior**

Run the relevant focused test or smoke command that proves `/v1/tools` returns five tools.

Expected: PASS.

- [ ] **Step 4: Verify documentation grep checks**

Run: `rg -n "ctx_multi_read|ctx_semantic_search|ctx_session|ctx_knowledge|49 Intelligent Tools|48 `ctx_`" README.md docs/TOOLS.md`

Expected: no matches.
