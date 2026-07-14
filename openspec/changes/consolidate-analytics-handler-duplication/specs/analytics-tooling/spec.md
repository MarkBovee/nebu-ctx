## ADDED Requirements

### Requirement: Analytics tool handlers share one JSON formatting configuration
All analytics MCP tool handlers (`ctx_cost`, `ctx_gain`, `ctx_heatmap`, `ctx_stats`) MUST serialize their `json` action output using one shared `JsonSerializerOptions` instance rather than independently declared, duplicate instances.

#### Scenario: Any analytics handler serializes its json action output
- **WHEN** `ctx_cost`, `ctx_gain`, `ctx_heatmap`, or `ctx_stats` serializes a report as JSON
- **THEN** it SHALL use the shared `AnalyticsSnapshotHelpers.IndentedJson` options instance
- **AND** the output SHALL be indented, consistent with today's behavior

### Requirement: Project-scoped command lookups use one shared resolution rule
Any analytics tool handler that resolves a project-scoped command telemetry breakdown MUST use one shared helper rather than an independently duplicated implementation.

#### Scenario: Cost or gain report is requested for a specific project
- **WHEN** `ctx_cost` or `ctx_gain` resolves command telemetry for a given `projectId`
- **THEN** it SHALL use the shared `AnalyticsSnapshotHelpers.GetCommands` helper
- **AND** the resolved commands SHALL match today's behavior exactly (the project's own commands when found, an empty dictionary otherwise, global commands when no project is specified)
