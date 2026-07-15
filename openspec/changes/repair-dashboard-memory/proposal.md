## Why

The dashboard receives richer memory data than it currently presents. Health metrics, maintenance findings, and operation feedback are missing or incomplete in the UI, while some memory operations fail silently. This leaves operators unable to distinguish empty memory from failed loading, stale state, or data that exists in the server payload but is not rendered.

## What Changes

- Render project memory health metrics and lifecycle state in the dashboard.
- Integrate existing maintenance and triage endpoints into visible dashboard workflows with refresh and outcome feedback.
- Normalize brain entry kinds consistently between server payloads, displayed counts, and bulk-clear operations.
- Add explicit loading, valid-empty, error, success, and retry states for memory views and operations.
- Keep the public MCP surface and existing memory lifecycle behavior unchanged.

## Capabilities

### New Capabilities

- `dashboard-memory-health`: Display health metrics, lifecycle states, and normalized brain-kind counts.
- `dashboard-memory-maintenance`: Present maintenance/triage findings and applied results using the existing server workflows.
- `dashboard-memory-operation-feedback`: Provide explicit loading, empty, error, success, and retry behavior for memory views and operations.

### Modified Capabilities

No existing capability requirements are replaced. The existing `dashboard` and memory specifications remain the baseline; this change adds missing application behavior around data already produced by the server.

## Impact

Primary UI changes are in `server/src/NebuCtx.Server.Host/Dashboard/dashboard.html`. Server changes are limited to `DashboardPayloadFactory.cs` (`ClassifyBrainEntryType` normalization). Existing maintenance, triage, brain, and knowledge routes are reused unchanged. No new endpoints, contracts, or session/sync services. Tests: `dotnet test` no regressions plus manual dashboard check. Security, authentication, token handling, ingress, reverse proxy, base path, deployment routing, and server-side shared-memory redesign are explicitly out of scope.
