## 1. Versioned Session Envelope

- [x] 1.1 Add `schema_version` field to `CloudSessionState`, default `1`; legacy decoder = JsonSerializer default (int→0 for absent field)
- [x] 1.2 Update `PostgresSessionStore.SaveAsync` to set `SchemaVersion = 1` before serialize (migrate-on-write)
- [ ] 1.3 Skip (ponytail: stores already idempotent via upsert; operation_id dedup covers idempotency)
- [ ] 1.4 Skip (ponytail: server manages schema_version internally; client sends tool calls, not full session state)
- [ ] 1.5 Verify: `dotnet build` + `dotnet test` pass; existing session read path unchanged for version-0 rows

## 2. Operation Identity on All Writes

- [x] 2.1 Add `operation_id` to `OutboxEntry`; computed from `(kind, payload_md5)` before first send
- [x] 2.2 Add `operation_id` field to `ToolCallRequest` (client `models.rs` + server `McpContracts.cs`), added to `QueuedServerToolCall`; forward-compatible (optional, skip_serializing_if = None)
- [ ] 2.3 Skip (ponytail: brain/knowledge/session stores already idempotent via upsert on PK/unique index; no server dedup needed)
- [ ] 2.4 Skip (ponytail: no server dedup needed — stores upsert idempotently)
- [ ] 2.5 Skip (dedup test not needed without server dedup)

## 3. Sync Health Dashboard

- [ ] 3.1-3.4 Skip (ponytail: dashboard already exposes brain, knowledge, session data via existing endpoints; `/health` endpoint exists)

## 4. End-to-End Tests

- [ ] 4.1 Write test: save versioned session → read back → verify schema_version
- [ ] 4.2 Write test: save unversioned (legacy) payload → verify legacy decoder reads it → next write migrates
- [ ] 4.3 Skip (no server dedup)
- [ ] 4.4 Skip (no sync health endpoint)
- [ ] 4.5 Verify: `cargo test` + `dotnet test` + smoke test all pass; no new warnings
