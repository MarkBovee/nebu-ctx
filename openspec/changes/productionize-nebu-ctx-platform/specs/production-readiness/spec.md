## ADDED Requirements

### Requirement: No silent loss for critical sync paths
The system MUST prefer durable retry behavior over silent data loss for client-originated production data.

#### Scenario: Server-backed sync path fails
- WHEN a retryable client-originated sync operation fails because the server is unavailable
- THEN the operation SHALL be queued for later replay

### Requirement: Dashboard migration safety
The system MUST allow the new dashboard API shape to coexist with legacy endpoints during migration.

#### Scenario: Legacy dashboard surfaces still exist
- WHEN the new dashboard overview and memory APIs are introduced
- THEN existing legacy `/api/*` endpoints SHALL remain available until their consumers are migrated
