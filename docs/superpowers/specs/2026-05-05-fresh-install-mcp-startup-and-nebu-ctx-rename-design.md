# Fresh Install MCP Startup And Nebu-ctx Rename Design

## Summary

Fix the fresh-install behavior where `nebu-ctx` is started as an MCP stdio server before any host connection is configured and currently emits a low-level EOF/serde failure instead of a useful instruction.

At the same time, remove the remaining `lean-ctx` naming debt from the Rust client codebase so the product surface, source references, and test imports consistently use `nebu-ctx`.

## Goals

- Replace the noisy fresh-install MCP startup failure with a short, clean instruction message.
- Detect the unconfigured-host case before the stdio MCP session falls into a low-level parse/read failure.
- Give the user immediate next steps using existing commands:
  - `nebu-ctx status`
  - `nebu-ctx connect`
- Include concrete `connect` examples for both local and network hosts on port `4242`.
- Remove remaining `lean-ctx` references from the active Rust client code and user-facing client text.
- Rename the Rust library crate away from `lean_ctx` so binaries and tests no longer import through the old name.

## Non-Goals

- Do not redesign the client/server architecture.
- Do not introduce a local fallback mode for cloud-only or host-backed tools in this change.
- Do not add an interactive setup wizard to MCP stdio startup.
- Do not attempt a repo-wide historical scrub of generated build output under `target/`.
- Do not change the host connection model beyond clearer startup handling.

## Current State

Issue #2 shows a fresh install starting `nebu-ctx` in stdio MCP mode and producing:

- `ERROR lean_ctx::mcp_stdio: Error reading from stream: serde error EOF while parsing a value at line 1 column 0`
- `nebu-ctx: connection closed: initialize request`

At the same time, `nebu-ctx status` already reports the real situation clearly:

- no saved host connection
- setup has not been completed
- the user should run `nebu-ctx connect`

The codebase also still has active rename debt, including examples such as:

- `client/src/main.rs` calling `lean_ctx::cli::run()`
- `client/Cargo.toml` exposing `[lib] name = "lean_ctx"`
- tests importing `lean_ctx::*`
- type names such as `LeanCtxServer`
- a few docs and user-facing strings that still mention `lean-ctx`

## Root Cause

The failure is happening at the wrong layer.

Today, stdio MCP startup proceeds straight into transport setup and waits for the initialize handshake. In the unconfigured fresh-install case, that path does not surface a user-oriented prerequisite check first. The transport then reports an empty or aborted stream as a codec/read error, which leaks an internal serde/EOF failure instead of the actual problem: the client has not been connected to a host yet.

This means the symptom is a transport error, but the real cause is missing preflight validation for the stdio MCP startup path.

## Approaches Considered

### 1. Add a startup preflight gate before stdio MCP serving

Before creating or waiting on the stdio MCP service, check whether the required saved host connection exists. If not, print a short instruction message and exit cleanly.

This is the recommended approach because it fixes the problem at the correct layer and keeps the failure understandable.

### 2. Let startup continue and improve downstream tool errors only

This would leave MCP startup untouched and rely on individual tool failures to explain that a host connection is missing.

This is rejected because issue #2 happens before those tool-level paths help. The user sees startup noise first.

### 3. Add a temporary local-only fallback for unconfigured installs

This would allow MCP startup to succeed with a reduced tool set until the user connects to a host.

This is rejected for now because it broadens behavior, risks ambiguity around cloud-only versus local tools, and is much larger than the issue requires.

## Chosen Design

### 1. Gate stdio MCP startup on saved host configuration

The stdio MCP startup path should perform a small preflight check before transport/service startup.

If no saved host connection exists:

- print a concise message to stderr
- explain that `nebu-ctx` still needs a host connection
- include the next commands to run
- exit cleanly without surfacing serde/EOF transport noise

The message should be short and operational, for example covering:

- `nebu-ctx status`
- `nebu-ctx connect --endpoint http://127.0.0.1:4242 --token <token>`
- `nebu-ctx connect --endpoint http://192.168.1.50:4242 --token <token>`
- a brief note that `4242` is the MCP/host port

The tone should match existing CLI guidance: direct, compact, and action-oriented.

### 2. Suppress misleading low-level EOF logging for this path

The desired outcome is that the user does not see the internal `serde error EOF while parsing a value` message for the unconfigured-host startup case.

Prefer preventing the server from entering the failing path at all rather than special-casing the transport codec after the fact.

If any residual logging remains necessary for real protocol failures, keep it for true runtime problems, but not for the preflight-missing-config case.

### 3. Finish the active `lean-ctx` to `nebu-ctx` rename in Rust client code

This change should remove remaining active old-name references from source and tests where they are still part of current behavior or maintenance overhead.

Expected scope includes:

- `client/Cargo.toml` library crate name
- `client/src/main.rs` binary entrypoint import path
- test imports that still use `lean_ctx`
- active type names like `LeanCtxServer`
- active user-facing text and comments that still say `lean-ctx`

Generated artifacts under `client/target/` are not a design target and should be ignored.

### 4. Keep the change minimal and behaviorally narrow

This should be a focused fix, not a broad refactor.

Specifically:

- no new setup flow
- no new persisted config model
- no compatibility layer unless required by compilation/tests
- no behavior change for already configured users except removal of stale naming

## Expected Code Changes

### Client startup

Update the stdio MCP startup path in `client/src/cli/dispatch.rs` so it validates host connection prerequisites before starting transport/service handling.

The preflight should reuse existing saved-connection logic instead of inventing a second source of truth.

### Config usage

Reuse `crate::config::load_connection()` or an extracted helper built on top of it for the startup check.

If extracting a helper improves reuse between startup messaging and other status surfaces, keep it very small.

### Transport/logging

Do not rely on `client/src/mcp_stdio.rs` as the primary fix point unless a small follow-up adjustment is needed after the startup gate is added.

The main correction belongs in startup orchestration, not codec error translation.

### Rename cleanup

Update remaining active references such as:

- `[lib] name = "lean_ctx"`
- `lean_ctx::...` imports
- `LeanCtxServer`
- any remaining active `lean-ctx` strings in client docs or help output

Prefer consistent rename completion over leaving mixed old/new terminology in live code.

## Error Message Requirements

The fresh-install stderr message should:

- explain that no host/server connection is configured yet
- point the user to `nebu-ctx status`
- show one localhost example with `127.0.0.1:4242`
- show one LAN/network example with an explicit IP and port `4242`
- include `--token <token>` in the examples
- stay short enough that it reads as one actionable block in editor logs

The message should not:

- mention serde, codec, EOF, or internal protocol terms
- imply the install is corrupt
- promise local fallback behavior that does not exist

## Risks And Mitigations

### Risk: startup gate blocks valid local-only scenarios

There may be flows where users expect some local tools to work without a host connection.

Mitigation:

- limit the new gate to the stdio MCP startup path that currently fails on fresh install
- do not change standalone CLI commands like `status`, `setup`, or `doctor`

### Risk: renaming the Rust lib crate fans out into many tests or imports

The old crate name may still be referenced in integration tests and internal build assumptions.

Mitigation:

- keep the rename scoped to active source/test code
- fix compilation errors directly rather than adding dual naming

### Risk: message examples age poorly if defaults change

If the default host port changes later, the instruction examples could drift.

Mitigation:

- keep the port reference aligned with the documented current host/MCP port `4242`
- if a shared constant already exists and is practical to reuse, prefer it

## Acceptance Criteria

This change is successful when:

- a fresh install without saved host config no longer shows the serde EOF startup noise from issue #2
- stdio MCP startup exits cleanly with a short instructional message instead
- that message includes both localhost and network `connect` examples on port `4242`
- `nebu-ctx status` remains the diagnostic command referenced in the message
- active Rust client code and tests no longer use `lean_ctx` as the library import name
- remaining active `LeanCtx*` style naming in the client source is replaced with `NebuCtx*` or an equivalent neutral name
- user-facing client text no longer refers to `lean-ctx`

## Verification

At minimum, implementation should verify:

- targeted client tests covering the unconfigured startup case
- targeted client tests covering the new message contents
- `cargo test --manifest-path client/Cargo.toml` or a narrower relevant subset first
- targeted grep confirming no active `lean-ctx` references remain in intended source/test/doc scope

## Notes

This change should make first-run behavior match the rest of the product surface: the status command already knows how to explain an unconfigured install, and the client should be equally clear when launched through MCP stdio. The rename cleanup is intentionally bundled because the startup failure currently leaks the old crate name into logs, which makes the issue more visible and more confusing than it should be.
