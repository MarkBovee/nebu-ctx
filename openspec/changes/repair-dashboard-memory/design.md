## Context

The dashboard frontend in `server/src/NebuCtx.Server.Host/Dashboard/dashboard.html` already loads knowledge, brain, project memory, and operational data. The server already builds `ProjectMemoryResponse` with health, wake-up, candidate, lifecycle, and maintenance-related data, and it already exposes maintenance, triage, candidate-review, brain, knowledge, project-memory, and session routes. The screenshots show that the primary failure is incomplete application integration: valid data is not rendered, operations do not report outcomes, and some display/filter paths use different brain-kind representations.

This change is limited to application behavior. It does not change authentication, token handling, ingress, reverse proxy, base path, deployment routing, or the server-side shared-memory model. Existing routes and services are preferred over parallel APIs. Raw journal events, prompts, assistant output, and transcripts remain outside dashboard payloads.

## Goals / Non-Goals

**Goals:**

- Make every existing project-memory health payload section visible and understandable in the dashboard.
- Distinguish loading, valid empty, error, and retry states.
- Make maintenance actions observable and refresh the affected view after completion.
- Normalize brain entry kinds at one boundary so counts, labels, and bulk operations agree.
- Keep changes testable through existing integration tests and manual dashboard validation.

**Non-Goals:**

- No security, authentication, token, or authorization changes.
- No ingress, reverse-proxy, base-path, deployment, or external routing changes.
- No redesign of the public MCP surface or memory lifecycle extraction.
- No new server-side shared-memory identity or projection model.
- No raw transcript or journal retention in dashboard or server responses.
- No replacement of existing maintenance, triage, candidate-review, or persistence services.

## Decisions

### 1. Render existing health payload

Map health fields from `ProjectMemoryResponse` (existing) to a dashboard card. No new endpoints needed — all data already flows through the existing project memory endpoint.

**Alternative considered:** create separate health endpoint. Rejected because it multiplies loading/error paths and risks inconsistent snapshots.

### 2. Use explicit view-state categories

Each memory view tracks loading, loaded-empty, loaded-data, failed, and retrying states. Operation controls track in-progress, succeeded, and failed-with-retry. A valid empty result must not be presented as a connection error.

**Alternative considered:** keep the current generic empty/error helpers. Rejected because they hide whether the server returned no data or the request failed.

### 3. Normalize brain kind at the server payload boundary

Brain entries with missing or legacy kinds are normalized to the canonical `fact` value before counts, entry types, and bulk-clear matching are exposed. The UI consumes the same normalized value for labels and action requests.

**Alternative considered:** normalize independently in each UI handler. Rejected because display and mutation behavior can drift again.

### 4. Keep maintenance operations on existing routes

The dashboard invokes existing maintain and triage endpoints, displays their typed results, and reloads the project memory view after mutations. The operation result remains visible until dismissed or replaced by a later operation.

**Alternative considered:** add a client-only cleanup implementation. Rejected because it would bypass server lifecycle rules and persistence semantics.

### 5. Session and sync visibility excluded

Session snapshots and sync-health panels are out of scope for this change. They add frontend complexity without corresponding operational value and can be added separately if needed later.

## Risks / Trade-offs

- **[Risk]** Rendering more sections increases dashboard density. **Mitigation:** use collapsible or bounded sections and show summaries before detail.
- **[Risk]** Existing payload fields may have inconsistent null/legacy shapes. **Mitigation:** normalize at the server boundary.
- **[Risk]** Maintenance results may become stale after another operation. **Mitigation:** clear or replace them on refresh.

## Migration Plan

1. Normalize brain kind at server boundary (`ClassifyBrainEntryType` → default `"fact"`).
2. Implement UI rendering for health metrics using existing project memory endpoint.
3. Wire maintenance/triage UI: analysis view + apply button + post-refresh.
4. Add shared `fetchWithState` helper, apply to all memory views.
5. Deploy — absent optional fields render as unavailable, not as errors.
6. Verify against populated and empty projects.
7. Roll back UI changes independently if needed; server normalization is additive.
