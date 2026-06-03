# Memory Maintenance Report

Date: `2026-06-03`
Project: `nebu-ctx`
Project ID: `proj_0e41160331b74e6cbf2c6fe9b8087057`
Mode: `apply`
Runner: repo-built server on `127.0.0.1:4342` against production Postgres

## Summary

- Brain entries scanned: `1051`
- Knowledge entries scanned: `36`
- Findings: `4`
- High-confidence findings: `4`
- Brain updates applied: `2`
- Knowledge updates applied: `1`

This second run happened after removing the old hard scan cap of `1000`. The prior run stopped exactly at `1000`; the new run confirmed there were `1051` brain entries in scope.

Since that run, the ingest path was also tightened:

- raw journal events are no longer pushed into hosted brain during normal client sync
- maintenance now removes existing raw timeline-like brain entries instead of trying to clean them as canonical memory

## What Was Cleaned

### 1. Knowledge metadata repair

One hosted knowledge fact was missing a stable `logical_key` and got repaired.

- Scope: `knowledge`
- Kind: `metadata_fix`
- Confidence: `1.0`
- Category: `root_cause`
- Key: `output-truncated-n-nfull-output-saved-to-home-mark-local-share-o`
- Change: derived and filled deterministic `logical_key`

Effect:

- This did not rewrite the fact text.
- This made the entry more consistent for later recall, dedupe, and maintenance runs.

### 2. Brain formatting cleanup

Two `session_timeline` brain entries were reformatted.

Common pattern:

- Excess indentation / leading spaces removed
- Repeated whitespace collapsed
- Text made more compact for storage consistency

Examples:

#### Example A

- Scope: `brain`
- Kind: `formatting`
- Confidence: `0.93`
- Key: `timeline-1780477671688-ses-17365c6d2ffecffcjjityvvzhh-tool-activity-65ef55f0c5`

Before:

```text
{
  "project_id": "proj_0e41160331b74e6cbf2c6fe9b8087057",
  "mode": "apply",
  "brain_scanned": 1000,
  ...
```

After:

```text
{
 "project_id": "proj_0e41160331b74e6cbf2c6fe9b8087057",
 "mode": "apply",
 "brain_scanned": 1000,
 ...
```

What changed:

- only whitespace/indentation normalization
- no semantic rewrite

#### Example B

- Scope: `brain`
- Kind: `formatting`
- Confidence: `0.93`
- Key: `timeline-1780478394513-ses-17365c6d2ffecffcjjityvvzhh-tool-activity-a3eb7e2783`

Before:

```text
  Determining projects to restore...
  All projects are up-to-date for restore.
  NebuCtx.Contracts -> ...
```

After:

```text
Determining projects to restore...
 All projects are up-to-date for restore.
 NebuCtx.Contracts -> ...
```

What changed:

- leading padding stripped
- line formatting compacted

### 3. One item marked as junk

One `session_timeline` brain entry was marked as junk.

- Scope: `brain`
- Kind: `junk`
- Confidence: `0.90`
- Target status: `junk`
- Key: `timeline-1780475810290-ses-17365c6d2ffecffcjjityvvzhh-assistant-turn-22fe0fbdad`

Why it was flagged:

- It contains long planning/proposal text for the maintenance design itself.
- That looks more like transient assistant/session output than durable project memory.
- The junk heuristic treated it as low-value timeline noise rather than canonical fact memory.

## What Was Not Cleaned

- No new duplicate merges in this second uncapped run
- No additional knowledge projection repairs
- No broad semantically-meaningful text rewrites

That is expected: most of the heavy formatting cleanup happened in the first run.

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

The report above still includes findings from the first uncapped run, before the new ingest guard and skip rules were added.

So current state is:

- root cause identified
- ingest path fixed
- maintenance scope fixed
- tests added and passing
- one fresh production-style rerun on the updated host is still needed to produce a final clean post-fix report

That rerun was blocked only by the temporary alternate host process not staying alive long enough during restart attempts, not by code/test failures.

## Recommended Follow-Up

### High priority

Run one fresh maintenance pass on the updated normal server build.

Reason:

- confirm new ingest path stops raw timeline growth
- confirm maintenance report no longer contains timeline/session cleanup
- produce final post-fix audit snapshot

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

Deploy updated server build to the normal host.

Current production-style server on `192.168.1.135:4242` did not yet expose `ctx(memory, action="maintain")`, so this run had to use a repo-built alternate host.

## Conclusion

The maintenance engine now does three important things correctly:

- scans beyond the old `1000` cap
- stops sending raw journal/timeline text into hosted brain during normal client sync
- removes old raw timeline-like brain rows during maintenance apply

Most important outcome of this investigation:

- your expectation was right
- raw chat/tool timeline content should not be treated as canonical hosted brain memory
- the code path that caused that has now been narrowed and the maintenance scope has been corrected accordingly
