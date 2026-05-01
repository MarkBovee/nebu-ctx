# README Architecture Rewrite Design

## Summary

Rewrite `README.md` so it reflects how `nebu-ctx` actually works today: a thin Rust client paired with a self-hosted .NET host and dashboard stack, with PostgreSQL-backed server state and a clear split between local and server-routed tool surfaces.

The README should remain useful as a landing page for new visitors, but its primary job is to be technically honest and aligned with the current codebase. Product language should move away from `cloud` and establish `connect`, `host`, and `server` as the canonical vocabulary for the project going forward.

## Goals

- Replace the current README's marketing-heavy framing with a hybrid overview that is inviting but technically accurate.
- Make the current architecture explicit:
  - Rust client in `client/`
  - .NET host in `server/src/`
  - Dashboard served by the same .NET host
  - PostgreSQL-backed production stores
- Explain real routing boundaries between local, server-only, and server-preferred tools.
- Base tool counts, routing claims, dashboard panels, and integration support on current code and docs, not stale copy.
- Establish future-facing README language around `connect` / `server` / `host` instead of `cloud`.
- Include the project's origin story: the client was based on and inspired by a fork of `lean-ctx`, then reshaped into `nebu-ctx` so it fits Mark's broader stack and product direction.

## Non-Goals

- Do not rename code, CLI commands, or directories as part of this README rewrite.
- Do not claim roadmap work as if it already exists.
- Do not preserve outdated headline claims purely for marketing value.
- Do not attempt a full docs-site rewrite; this change is limited to `README.md` and any minimal follow-up references needed to keep it internally consistent.

## Audience

The rewritten README must work for two audiences at once:

1. New GitHub visitors who need to understand what `nebu-ctx` is.
2. Developers/operators who need an accurate model of the client/server architecture.

The document should prioritize truth and clarity over aggressive conversion language.

## Positioning

The README becomes the canonical top-level explanation of the current system.

- It should still feel like a product README, not an internal handover note.
- It should no longer lead with large savings claims or a "cloud" story that is no longer the right product language.
- It should describe `nebu-ctx` as a system you run with a local Rust client and a self-hosted host/server component.
- It should make room for a roadmap section that mentions richer agent-session collaboration as future work.

## Canonical Terminology

Use these terms in the new README:

- `client` for the Rust CLI / MCP stdio binary
- `host` or `server` for the .NET HTTP host and dashboard process
- `connect` for the project-to-host relationship and setup language
- `server-backed` or `host-backed` for capabilities that rely on the .NET service

Avoid using `cloud` as a primary product term in the README.

If necessary for factual accuracy, the README may briefly note that some existing code or commands still use older `cloud` terminology internally, but the document should present `connect` / `host` / `server` as the authoritative language going forward.

## Proposed README Structure

### 1. `nebu-ctx` in one paragraph

Open with a short, direct explanation of what the project is today:

- a Rust client that integrates with agent/editor workflows
- a .NET host that serves MCP HTTP and the dashboard
- a Postgres-backed server layer for persistent project state and analytics

### 2. How `nebu-ctx` actually works

Add a short flow-oriented section describing:

- agent/editor -> Rust client
- local tool handling and hook handling in the client
- server-routed calls to the .NET host
- dashboard and state served from the host

### 3. Architecture

Include a compact but accurate architecture section with:

- `client/` responsibilities
- `server/src/` responsibilities
- port split (`3333` dashboard, `4242` MCP HTTP)
- production storage model
- local vs server routing overview

### 4. Main capabilities

Group capabilities by what exists now:

- local file/context operations
- shell and hook workflow support
- persistent memory and project context
- telemetry, dashboard, and analytics
- agent/session coordination surfaces

Mention future multi-session collaboration as roadmap, not as a completed system.

### 5. Tool inventory

Replace the current fixed marketing count with a real inventory derived from code.

Represent each tool with:

- tool name
- routing class (`local`, `server-only`, `server-preferred`)
- short purpose

The exact count should be recalculated from the current codebase before the README is edited.

### 6. Client architecture

Describe the most important client surfaces:

- `client/src/main.rs`
- `client/src/cli/dispatch.rs`
- `client/src/hook_handlers.rs`
- `client/src/mcp_server/mod.rs`
- `client/src/mcp_server/dispatch.rs`
- the current HTTP client surface for host communication

Also summarize the hook system and shell/setup responsibilities.

### 7. Server architecture

Describe the server stack in terms of:

- host
- dashboard
- contracts
- tool handlers
- storage
- telemetry

Make clear that the dashboard is served by the same .NET host.

### 8. Install and run

Reframe onboarding around:

- install the Rust client
- run the server/host
- connect the project
- setup the editor/agent integration
- verify with doctor/tests

If current CLI still uses `cloud`-named commands, document them carefully without making `cloud` the main product language.

### 9. Supported agent/editor integrations

Keep this section concise and factual.

Focus on what `setup` / `init` actually configures today, not just brand presence.

### 10. Project origin

Add a short section explaining:

- the client started from a fork/inspiration of `lean-ctx`
- it was then rebuilt and adapted into `nebu-ctx`
- the rename and reshaping were done to fit Mark's broader projects and long-term architecture

### 11. Roadmap

Add a short roadmap section covering:

- richer collaboration between agent sessions
- broader shared session state and context handoff
- continued cleanup of legacy `cloud` terminology
- further client/server consolidation where it makes sense

### 12. Development

Close with accurate build/test commands and links to deeper docs such as `AGENTS.md`.

## Source of Truth

The README rewrite must derive factual claims from these sources:

- `AGENTS.md`
- `client/src/mcp_server/mod.rs`
- `client/src/mcp_server/dispatch.rs`
- `client/src/hook_handlers.rs`
- `client/src/main.rs`
- `client/src/cli/dispatch.rs`
- `server/src/NebuCtx.Server.Core/ToolRegistry.cs`
- `server/src/NebuCtx.Tools/`
- `server/src/NebuCtx.Server.Host/`
- `server/tests/`
- `client/tests/`
- `homeassistant/`
- `Dockerfile`

Before editing the README, verify:

- actual tool count
- actual server-only and server-preferred tool lists
- actual dashboard panel count and names
- actual supported agent/editor integrations
- whether the user-facing CLI still exposes `cloud` terminology in commands that must be documented for compatibility

## Claims To Remove Or Reframe

The rewrite should remove or soften any claims that are not directly supportable from current sources, including:

- headline savings percentages presented as the main story
- fixed tool totals unless revalidated
- fixed shell-pattern counts unless revalidated
- `cloud dashboard` / `cloud-backed` as primary wording

Allowed claims include:

- Rust client + .NET host architecture
- self-hosted host/server model
- dashboard on `3333`
- MCP HTTP on `4242`
- PostgreSQL-backed production stores
- split routing between local and server-backed tools
- origin from `lean-ctx` into `nebu-ctx`

## Tone And Style

- Direct, technical, and readable
- Low hype, high signal
- Product-facing but honest
- No inflated metrics without evidence
- No internal-only jargon without explanation

## Acceptance Criteria

The README rewrite is successful when:

- a new visitor can understand the system in under two minutes
- a technical reader can correctly explain the client/server split after reading it
- `cloud` is no longer the README's main product term
- all counts and routing claims in the README can be traced back to current code/docs
- the origin story and future direction are present but concise
- the README matches the current architecture more closely than the existing version

## Implementation Notes

- Update the README using the new product vocabulary first, then fit any necessary legacy command notes underneath it.
- Prefer removing stale sections over trying to preserve them with caveats.
- If a number cannot be verified quickly from code, omit the number and describe the capability instead.
- Keep deeper operational details in supporting docs where appropriate, but make the README complete enough to stand on its own.
