## Context

The current public nebu-ctx command story has three related weaknesses.

First, `ctx_search` uses a custom walker plus `regex` matching over UTF-8 file reads. That diverges from ripgrep semantics for file discovery, ignore handling, binary detection, encoding behavior, and match execution. Issue `#21` shows the practical result: false zero-match responses even when ripgrep finds hits in the same workspace.

Second, public guidance is duplicated across `instructions.rs`, `rules_inject.rs`, hook templates, and markdown templates. The same policy is expressed in slightly different words and with slightly different guarantees, which makes it easy for one surface to lag behind the others.

Third, `nebu-ctx report-issue` exists but is optimized for an interactive human workflow. It is not a reliable automation primitive for an agent that must create or update an issue before handoff.

This change is cross-cutting because it touches search behavior, instruction generation, hook/rule rendering, CLI issue reporting, tests, and user-facing guidance guarantees.

## Goals / Non-Goals

**Goals:**
- Make `ctx_search` regex mode behave like ripgrep closely enough that public-tool users can trust positive and zero-match results.
- Eliminate guidance drift by defining one canonical public-guidance policy and rendering all supported instruction outputs from it.
- Give agents a non-interactive, duplicate-aware issue reporting path for reproducible public nebu-ctx tool bugs.
- Preserve the public 5-tool contract and existing user entrypoints wherever possible.

**Non-Goals:**
- Redesign semantic search behavior.
- Replace every existing template with a single identical text blob; different surfaces may still need different formatting or length limits.
- Build a generic issue triage system for all repositories or all bug classes.
- Change the public `ctx_search` schema beyond additive, compatibility-safe fields if they are needed internally.

## Decisions

### 1. Replace bespoke regex search internals with embedded ripgrep components

`ctx_search` regex mode will stop using the current custom `WalkBuilder + Regex + read_to_string` implementation as the authoritative search path. Instead, it will use ripgrep-compatible crates embedded in the client process.

Rationale:
- Preserves cross-platform behavior without depending on an external `rg` binary on PATH.
- Aligns search semantics with the tool users already trust.
- Keeps the existing public `ctx_search` command intact while swapping the internals.

Alternatives considered:
- Spawn external `rg --json`: simpler parity, but adds runtime dependency on an installed binary and more quoting/process-management edge cases.
- Patch the current custom scanner: lower dependency cost, but still leaves nebu-ctx maintaining its own search semantics and edge cases.

### 2. Separate search execution from compact-output rendering

The search stack will produce a structured internal match model first, then format that model into the compact `ctx_search` response.

Rationale:
- Prevents formatting bugs from being misreported as `0 matches`.
- Makes parity tests easier because raw match collection can be validated separately from output compression.

Alternatives considered:
- Keep the current string-first pipeline: simpler locally, but hides whether failures come from search, parsing, or output construction.

### 3. Fail explicitly when reliable zero-match semantics cannot be guaranteed

If regex compilation, ripgrep execution, result parsing, or formatting cannot produce a trustworthy answer, `ctx_search` must return an explicit error or timeout response instead of a misleading zero-match success message.

Rationale:
- A false negative is worse than a loud failure for coding and review workflows.

Alternatives considered:
- Return best-effort empty results with warnings: still too easy for agents to treat as real zero matches.

### 4. Create a canonical public-guidance policy layer

Introduce a single client-side policy module that owns:
- public 5-tool mapping
- no-bypass rule for public paths
- reproducible bug handling policy
- automatic issue filing rule
- short and long render variants for different clients

Instruction builders, rules injection, and hook-specific templates will render from that policy instead of maintaining independent source strings.

Rationale:
- Removes copy/paste drift.
- Makes future policy updates one-source changes.
- Keeps format-specific rendering while centralizing semantics.

Alternatives considered:
- Keep `rules_inject.rs` as the canonical source and manually mirror elsewhere: still duplicates formatting logic and does not solve hook-template drift.

### 5. Make `report-issue` automation-first while preserving interactive mode

`nebu-ctx report-issue` will gain a non-interactive submission path with structured flags and duplicate lookup. Interactive preview/confirmation will remain for manual use, but agents can call the same command in automation mode.

Rationale:
- Product behavior becomes dependable instead of relying on prompt wording alone.
- Centralizes issue formatting, diagnostics capture, duplicate lookup, and fallback handling.

Alternatives considered:
- Tell agents to use `gh issue create` directly: fast, but duplicates logic in prompts and loses nebu-ctx diagnostics collection.

### 6. Duplicate detection lives in product logic, not only in agent instructions

Automation mode will search for relevant open issues before creating a new one and will update/comment on an existing issue when a confident duplicate is found.

Rationale:
- Prevents prompt drift from creating duplicate bugs.
- Keeps issue hygiene consistent across agent surfaces.

Alternatives considered:
- Instruct agents to search manually every time: too fragile and inconsistent.

## Risks / Trade-offs

- [Ripgrep crate integration adds API complexity and dependencies] -> Keep the public `ctx_search` surface stable, wrap ripgrep internals behind a narrow adapter, and add focused regression tests.
- [Canonical guidance renderer may still need per-surface formatting tweaks] -> Centralize semantics and allow thin render adapters for Claude-cap, markdown, and hook-template output.
- [Duplicate matching may produce false positives] -> Limit automatic update behavior to clear matches and otherwise create a new issue with explicit diagnostics.
- [Automatic issue filing can fail due to missing `gh` auth or network access] -> Return explicit failure status, save the report locally, and keep the agent-visible guidance honest about what happened.
- [Search parity work can expose hidden assumptions in current tests or telemetry] -> Update tests and metrics deliberately around structured match results rather than current string formatting quirks.

## Migration Plan

1. Add the new search adapter and keep the current `ctx_search` public dispatch unchanged.
2. Introduce the canonical guidance policy and convert existing instruction/rule builders to render from it.
3. Extend `report-issue` with automation flags and duplicate lookup while preserving the interactive path.
4. Update tests and snapshots for guidance rendering, search parity, and issue automation.
5. Validate with targeted client tests first, then full client test suite.

Rollback:
- Revert to the prior `ctx_search` implementation if search parity regressions appear.
- Keep old rendered guidance text available in git history; renderer changes are isolated to client guidance modules.
- Auto issue filing changes are additive and can fall back to interactive mode if automation path misbehaves.

## Open Questions

- Use embedded ripgrep crates as the primary implementation path; only fall back to external `rg --json` if crate integration proves materially unworkable during implementation.
- Duplicate detection confidence can start with open-issue title/body search plus tool-name heuristics; broader fingerprinting can be a follow-up if needed.
