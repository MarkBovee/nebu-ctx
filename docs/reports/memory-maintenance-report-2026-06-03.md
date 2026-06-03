# Memory Maintenance Report

Date: `2026-06-03`
Project: `nebu-ctx`
Project ID: `proj_0e41160331b74e6cbf2c6fe9b8087057`
Mode: `apply`
Runner: live hosted server on `192.168.1.135:4242`

## Summary

Final live run on the updated host:

- Brain entries scanned before apply: `1112`
- Knowledge entries scanned before apply: `39`
- Findings before apply: `880`
- High-confidence findings before apply: `880`
- Brain updates applied: `876`
- Knowledge updates applied: `4`
- Legacy raw brain rows removed: `876`

Final verification run immediately after apply:

- Brain entries scanned after apply: `236`
- Knowledge entries scanned after apply: `39`
- Findings after apply: `0`
- High-confidence findings after apply: `0`

Net result:

- the hosted project brain no longer contains the old raw timeline/journal pollution
- the live hosted dataset is now clean under the current deterministic maintenance rules

## What Was Cleaned

### 1. Knowledge metadata repair

Four hosted knowledge facts were missing stable `logical_key` values and were repaired.

- Scope: `knowledge`
- Kind: `metadata_fix`
- Confidence: `1.0`
- Category: `root_cause`
- Key: `output-truncated-n-nfull-output-saved-to-home-mark-local-share-o`
- Change: derived and filled deterministic `logical_key`

Effect:

- This did not rewrite the fact text.
- This made the entries more consistent for later recall, dedupe, and maintenance runs.

### 2. Legacy raw brain cleanup

The live apply run removed `876` hosted brain rows that were clearly raw session timeline/journal content.

Common patterns:

- user turns
- assistant turns
- tool output blobs
- git diffs
- test logs
- command output
- maintenance/debug transcripts

These rows had been stored in hosted brain with timeline-like markers such as:

- `kind = session_event`
- `category = session_timeline`
- `lifecycle_status = timeline`
- `kind in [user_prompt, assistant_output, tool_activity]`

Maintenance now treats those rows as legacy raw data and removes them on apply.

## What Was Not Cleaned

- No duplicate merges were needed in the final live run.
- No formatting rewrites were needed in the final live run.
- No junk reclassification was needed in the final live run.
- No projection repairs were needed in the final live run.

## Root Cause Found

The unexpected timeline/chat/tool noise in hosted brain was real, and the exact source was identified.

Client path:

- `client/src/server_client.rs`
- function: `post_journal_events_to_server(...)`
- called from: `sync_session_memory_to_server(...)`

What it did:

- took raw `JournalEvent.text`
- wrapped it as `ctx_brain ingest`
- stored it with:
  - `kind = "session_event"`
  - `category = "session_timeline"`
  - `lifecycle_status = "timeline"`

That behavior conflicts with the intended model and with the repo specs:

- `openspec/specs/brain-facts/spec.md`
- `openspec/specs/local-journal/spec.md`

Those specs say raw prompt/assistant/session-log content should stay local and only derived facts should go to hosted brain.

## Fixes Applied After Investigation

### 1. Client sync no longer sends raw journal text to hosted brain

Changed file:

- `client/src/server_client.rs`

Change:

- `sync_session_memory_to_server(...)` no longer calls `post_journal_events_to_server(...)`
- it still sends:
  - derived brain facts via `flush_to_brain(...)`
  - durable knowledge candidates via `derive_durable_memory_candidates(...)`

Effect:

- raw timeline blobs stop flowing into hosted brain during normal sync
- only derived memory survives server-side

### 2. Maintenance now removes old non-canonical brain rows

Changed file:

- `server/src/NebuCtx.Server.Core/Services/MemoryMaintenanceService.cs`

Legacy hosted brain rows matching these raw timeline markers are removed during maintenance apply:

- `kind = session_event`
- `category = session_timeline`
- `lifecycle_status = timeline`
- `kind = user_prompt`
- `kind = assistant_output`
- `kind = tool_activity`

Effect:

- maintenance no longer reformats, dedupes, or junk-marks those rows
- maintenance deletes them from hosted brain on apply
- canonical fact cleanup stays focused on real memory/facts

### 3. Scan cap fixed

Changed file:

- `server/src/NebuCtx.Server.Core/Services/MemoryMaintenanceService.cs`

Change:

- maintenance no longer hardcodes `1000`
- it loads full brain/knowledge sets using store counts first

Effect:

- run now sees all currently persisted rows

## Current Follow-Up State

Current state after the final live rerun:

- root cause identified
- ingest path fixed
- maintenance scope fixed
- live hosted cleanup applied
- immediate post-apply analyze returned `0` findings
- tests added and passing

One operational note:

- an earlier post-apply check was run in parallel with the live apply request and briefly showed stale legacy findings
- a sequential rerun right after apply confirmed the real final state: `0` remaining findings

## Recommended Follow-Up

### High priority

Monitor for any new raw timeline rows after normal client activity.

Reason:

- confirm the new client ingest guard holds over time
- confirm only derived facts/candidates continue to reach hosted memory

### Medium priority

Persist maintenance runs in a dedicated store.

Suggested artifacts:

- `memory_maintenance_runs`
- optional `memory_maintenance_findings`

Reason:

- proper audit trail
- trend view over time
- easier operator dashboard

### Medium priority

Add dedicated maintenance-run persistence if you want audit/history in-product.

The one-off report is enough for now, but a stored run history would make operator review easier.

## Conclusion

The maintenance engine now does three important things correctly:

- scans beyond the old `1000` cap
- stops sending raw journal/timeline text into hosted brain during normal client sync
- removes old raw timeline-like brain rows during maintenance apply

Most important outcome of this investigation:

- your expectation was right
- raw chat/tool timeline content should not be treated as canonical hosted brain memory
- the code path that caused that has now been narrowed and the live hosted data has been cleaned accordingly
