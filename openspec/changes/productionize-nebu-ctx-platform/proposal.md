# Productionize nebu-ctx platform

## Why

`nebu-ctx` already has the right core shape, but the current product still behaves like a powerful prototype:

- the server dashboard has too many overlapping screens and weak contracts
- memory behavior differs too much between local client, server, Claude Code, and OpenCode
- offline work persists locally, but server sync is still mostly fire-and-forget
- several important production workflows rely on best-effort behavior instead of durable state

## What changes

- consolidate the dashboard into fewer, clearer operational views backed by new `/api/dashboard/*` endpoints
- improve editor memory activation, especially for Claude Code and OpenCode
- introduce a durable offline sync outbox so client activity can be replayed after reconnect
- make project memory visible and manageable per project on the server
- harden the client/server contracts and validation path for production use

## Initial delivery

This change starts with the lowest-risk high-value foundation:

- complete Claude hook installation for `SessionStart`, `UserPromptSubmit`, and `PreCompact`
- improve startup memory activation
- add a disk-backed sync outbox for client telemetry and queued server memory calls
- add new dashboard overview and per-project memory endpoints
- prepare the OpenSpec baseline for the larger dashboard and memory redesign

## Non-goals

- replacing the whole dashboard frontend in one step
- implementing fully generic bidirectional sync for every local store in the first iteration
- migrating all legacy dashboard endpoints immediately
