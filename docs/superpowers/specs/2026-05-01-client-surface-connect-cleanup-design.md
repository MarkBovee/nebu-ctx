# Client Surface Connect Cleanup Design

## Summary

Remove `bind` and `dashboard` from the Rust client surface and make project resolution fully implicit for server-backed flows. At the same time, remove `cloud` as the preferred product term from both the README and the codebase, including internal module/type/file names where practical in the same change.

The intended end state is:

- the client exposes `connect` / `disconnect`, not `cloud` / `bind`
- dashboard access belongs to the server/host, not the client
- project identity is resolved automatically from repository fingerprint + checkout metadata during normal server-backed calls
- `cloud` becomes compatibility debt to remove, not an actively used concept in the product surface or code organization

## Goals

- Remove `bind` from the client CLI, help text, README, and tests.
- Remove `dashboard` from the client CLI, help text, README, and tests.
- Keep project resolution working without an explicit bind step.
- Make the README reflect the new flow:
  - install client
  - run host
  - connect client to host
  - setup editor/agent integration
  - access the dashboard directly on the host
- Replace `cloud` terminology with `connect`, `host`, or `server` in user-facing text.
- Also clean up internal naming where practical in the same pass, including modules/files/types that still center `cloud`.

## Non-Goals

- Do not remove the actual server-backed routing model.
- Do not remove the dashboard from the .NET host.
- Do not redesign the server-side project registry model.
- Do not require a brand-new bootstrap mechanism if existing lazy project resolution already covers the use case.

## Current State

Today the client still exposes these user-facing surfaces:

- `connect`
- `disconnect`
- `bind`
- `dashboard`
- `watch`

And the codebase still contains internal naming built around `cloud`, including examples such as:

- `client/src/cli/cloud.rs`
- `client/src/cloud_client.rs`
- `CLOUD_ONLY_TOOLS`
- `CLOUD_PREFERRED_TOOLS`
- help text and error text that still say `cloud`

At the same time, the runtime already supports implicit project resolution:

- tool calls send `project_slug`, `repository_fingerprint`, and `checkout_binding`
- the server resolves or creates the canonical project on demand
- the server persists checkout binding metadata where supplied

That means explicit `bind` is no longer fundamental to normal operation.

## Design Decisions

### 1. Remove `bind` completely from the client surface

`bind` should no longer exist as a client command.

Rationale:

- it is redundant with the current lazy project resolution flow
- it adds a separate mental model for users that is no longer necessary
- it implies manual project registration even though normal server-backed flows already carry enough metadata to resolve a project

### 2. Remove `dashboard` and `watch` completely from the client surface

The dashboard belongs to the host. The client should not present dashboard opening or dashboard URL printing as part of its canonical job.

Rationale:

- the dashboard is served by the .NET host
- the client should stay focused on local tooling, hooks, MCP stdio, and host communication
- this makes the client thinner and avoids blurring host vs client responsibilities

### 3. Keep `connect` focused on connection setup only

`connect` should:

- save endpoint and token
- validate connectivity with a health check

`connect` should not:

- implicitly bind the current directory
- depend on being run inside a repository

Rationale:

- connection setup is host-level configuration
- project resolution is request-level behavior
- this keeps `connect` predictable and usable outside a repo directory

### 4. Project resolution becomes fully implicit

Server-backed flows should continue to rely on the existing model:

- client sends repository fingerprint and checkout metadata where available
- server resolves or creates the canonical project record
- server persists checkout binding metadata as part of normal request handling where relevant

If the existing lazy resolution path is already sufficient, prefer deleting `bind` over introducing replacement complexity.

### 5. `cloud` is removed as an active concept

This change should go beyond README copy and remove `cloud` from:

- CLI help text
- user-facing error text
- command names
- module names
- file names
- internal comments where they still describe the current architecture incorrectly

Preferred replacements:

- `server`
- `host`
- `connect`
- `server-only`
- `server-preferred`

Examples:

- `client/src/cli/cloud.rs` -> `client/src/cli/connect.rs`
- `client/src/cloud_client.rs` -> `client/src/server_client.rs` or equivalent
- `CLOUD_ONLY_TOOLS` -> `SERVER_ONLY_TOOLS`
- `CLOUD_PREFERRED_TOOLS` -> `SERVER_PREFERRED_TOOLS`

## Expected Code Changes

### Client CLI

Update `client/src/cli/dispatch.rs` to:

- remove dispatch arms for `bind`
- remove dispatch arms for `dashboard`
- remove dispatch arms for `watch` if it only exists as a dashboard-related surface
- remove dashboard-specific fallback messaging from local client help

Update client help text to:

- remove `bind`
- remove `dashboard`
- remove `watch`
- present `connect` / `disconnect` as the remaining host-connection commands

### Client modules and naming

Rename or reframe these surfaces:

- `client/src/cli/cloud.rs`
- `client/src/cloud_client.rs`
- any `cloud::*` module references
- constants such as `CLOUD_ONLY_TOOLS` and `CLOUD_PREFERRED_TOOLS`

Also update internal comments and messages that incorrectly say `cloud` when they mean host/server-backed behavior.

### README

Update `README.md` so it:

- removes `bind` from install flow and CLI surface lists
- removes `dashboard` from client CLI surface lists
- explains that the dashboard is accessed directly on the host
- removes `cloud` as a preferred term
- describes the new flow as:
  - install client
  - start host
  - connect client
  - setup integrations
  - visit the host/dashboard directly

### Tests

Update any tests that currently assume:

- `bind` is a valid command
- `dashboard` is a valid client command
- help text still lists removed surfaces
- `cloud` is the preferred wording in user-facing output

## Risks And Mitigations

### Risk: hidden reliance on `bind`

There may be flows that still assume explicit pre-binding.

Mitigation:

- inspect all server-backed entry points
- verify tool calls, telemetry ingestion, and index sync still resolve projects correctly without manual bind
- prefer adding a small missing implicit-resolve path only if verification shows a real gap

### Risk: dashboard discoverability drops

Removing `nebu-ctx dashboard` from the client means one less convenience command.

Mitigation:

- make README and host docs explicit about the dashboard URL and ownership
- keep the host-side dashboard visible in server docs and deployment instructions

### Risk: broad naming cleanup touches many files

Renaming `cloud` concepts internally can fan out into many references.

Mitigation:

- keep the rename focused on current code paths and terminology that materially affect maintainability
- prefer small consistent renames over partial dual terminology

## Acceptance Criteria

This change is successful when:

- `nebu-ctx bind` no longer exists in the client
- `nebu-ctx dashboard` no longer exists in the client
- client help text no longer lists `bind`, `dashboard`, or `watch`
- README no longer instructs users to run `bind` or `dashboard`
- README no longer presents `cloud` as the product term
- project resolution still works through normal server-backed flows without explicit bind
- code and docs use `server` / `host` / `connect` terminology consistently enough that `cloud` is no longer the dominant internal name

## Verification

At minimum, the implementation should verify:

- client test suite
- server test suite if any renamed contracts or messages touch shared behavior
- targeted CLI/help behavior checks confirming the removed commands are truly gone
- README review for remaining user-facing `cloud` language

## Notes

The current code already suggests that the product model moved past explicit binding, but the client surface and terminology did not catch up. This change formalizes the thinner-client direction instead of preserving legacy concepts that no longer match how the system actually behaves.
