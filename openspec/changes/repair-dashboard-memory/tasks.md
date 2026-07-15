## 1. Normalize Brain Kinds

- [x] 1.1 Change `ClassifyBrainEntryType` to default null/empty kind to `"fact"` instead of `"other"`.
- [x] 1.2 Delete/clear endpoints use same normalized kind for matching; verify no silent failures.

## 2. Render Health Metrics

- [x] 2.1 Render health card in project memory view: total facts, current/non-current, lifecycle score, density, last maintenance.
- [x] 2.2 Render lifecycle status badges with consistent legend, normalized brain kinds, and valid-empty state.

## 3. Maintenance & Triage UI

- [x] 3.1 Render triage findings (duplicate groups, stale, junk) with acceptance actions and post-apply view refresh.
- [x] 3.2 Wire maintenance analysis + apply buttons to existing endpoints, show outcome summary.

## 4. View States

- [x] 4.1 Add shared `fetchWithState(url, onState)` helper tracking loading/error/loaded-empty/loaded-data states.
- [x] 4.2 Apply helper to memory, knowledge, brain, maintenance, and candidate views.

## 5. Validation

- [x] 5.1 Run `dotnet test` — no regressions. (157/157 passed)
- [ ] 5.2 Manual check: project memory with data, empty project, error/timeout states render correctly.
