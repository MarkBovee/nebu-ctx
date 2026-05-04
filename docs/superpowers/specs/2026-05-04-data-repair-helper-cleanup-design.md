# Data Repair Helper Cleanup Design

## Goal

Make the local `NebuCtx.DataRepair` helper easier to understand and safer to use without changing its runtime behavior.

## Scope

- Add a short usage header comment to `server/tools/NebuCtx.DataRepair/Program.cs`
- Add a local `README.md` in `server/tools/NebuCtx.DataRepair/`
- Do small structural cleanup inside `Program.cs` to reduce repetition around connection handling and inspection output helpers

## Non-Goals

- No new repair behaviors
- No CLI argument parser
- No repo-wide docs rewrite

## Design

The helper remains a single-file console tool because it is intentionally admin-oriented and low-frequency. The cleanup focuses on readability:

- Centralize repeated `DATABASE_URL` / Npgsql connection setup in a tiny helper
- Keep the top-level flow visible: inspect -> optional delete/migrate -> report
- Keep the unresolved-delete flag explicit and documented in both the source header and the local README

## Usage

Document two supported modes:

1. Default inspect mode
2. Explicit unresolved-delete mode via `NEBU_REPAIR_DELETE_UNRESOLVED=1`

The README should recommend inspect-first, review output, then run destructive cleanup only when the operator has confirmed the target records are unresolved legacy data.
