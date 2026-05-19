# Tasks

## 1. Dashboard foundation

- [x] Add aggregated dashboard overview endpoint
- [x] Add per-project dashboard memory endpoint and admin delete/clear routes
- [x] Start moving overview loading to the aggregated API
- [x] Add dashboard Memory Admin on top of project-scoped memory endpoints
- [x] Consolidate the remaining dashboard screens into fewer domain views

## 2. Editor memory activation

- [x] Install full Claude memory hook set during setup/init
- [x] Improve startup memory injection at `SessionStart`
- [x] Upgrade OpenCode plugin transport for safer hook calls
- [ ] Add deeper OpenCode lifecycle parity once more editor hook surface is available

## 3. Offline sync

- [x] Add disk-backed outbox for telemetry and queued server memory tool calls
- [x] Drain pending items during MCP runtime and startup hooks
- [x] Add status/reporting CLI for outbox inspection
- [x] Add one-shot outbox flush/replay command
- [x] Extend replay to code index snapshots
- [x] Queue session summaries through server tool-call outbox

## 4. Production hardening

- [x] Add focused tests for new offline and hook behavior
- [x] Expand typed dashboard contracts beyond the new top-level endpoints
- [x] Add sync outbox doctor/status checks
- [x] Add dashboard port/health doctor checks
- [ ] Complete end-to-end offline-to-online replay validation
