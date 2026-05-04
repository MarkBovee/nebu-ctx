# NebuCtx.DataRepair

Local admin helper for direct Postgres project-identity repair and cleanup.

## What it does

- inspects legacy `mark` / `markb` project records
- shows bindings, inferred repo identity, and a small evidence sample
- deletes stale legacy shells automatically
- can optionally delete unresolved legacy records that still have no repo identity
- can migrate misidentified projects into a canonical repo-backed project when identity can be inferred

## Safe workflow

1. Run inspect mode first.
2. Review the JSON output.
3. Only run destructive cleanup when you have confirmed the target records are unresolved legacy data.

## Commands

Inspect only:

```bash
set -a && . ./.env && dotnet run --project server/tools/NebuCtx.DataRepair/NebuCtx.DataRepair.csproj
```

Delete unresolved legacy records with no repo identity:

```bash
set -a && . ./.env && NEBU_REPAIR_DELETE_UNRESOLVED=1 dotnet run --project server/tools/NebuCtx.DataRepair/NebuCtx.DataRepair.csproj
```

Build only:

```bash
dotnet build server/tools/NebuCtx.DataRepair/NebuCtx.DataRepair.csproj
```

## Notes

- The tool uses `DATABASE_URL` from the environment.
- The helper is intentionally local/admin-oriented and emits JSON for auditability.
- Keep it inspect-first; the unresolved delete mode is explicit by design.
