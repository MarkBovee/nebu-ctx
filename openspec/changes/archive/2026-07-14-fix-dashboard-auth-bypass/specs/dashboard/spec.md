## ADDED Requirements

### Requirement: Dashboard mutating API routes require authentication
The server MUST require a valid bearer token for any `/api/*` request that is not a safe (`GET`/`HEAD`) method, even when `DashboardDisableAuth` is enabled and the request arrives on the dashboard's configured port. The `DashboardDisableAuth` exemption SHALL apply only to safe methods and to static dashboard assets.

#### Scenario: Mutating request on the dashboard port with auth disabled
- **WHEN** a client sends a `DELETE`, `POST`, `PUT`, or `PATCH` request to an `/api/*` route on the dashboard's port
- **AND** `DashboardDisableAuth` is enabled
- **THEN** the server SHALL respond `401 Unauthorized` unless a valid bearer token is presented

#### Scenario: Read-only request on the dashboard port with auth disabled
- **WHEN** a client sends a `GET` or `HEAD` request to an `/api/*` route on the dashboard's port
- **AND** `DashboardDisableAuth` is enabled
- **THEN** the server SHALL serve the request without requiring a bearer token

#### Scenario: Static dashboard asset requests remain exempt regardless of method
- **WHEN** a client requests `/`, `/index.html`, `/dashboard`, `/logo.png`, or `/favicon.ico` on the dashboard's port with `DashboardDisableAuth` enabled
- **THEN** the server SHALL serve the request without requiring a bearer token, independent of HTTP method

### Requirement: Startup validation requires a token for non-loopback dashboard binds
The server MUST fail startup with a clear error when `DashboardHost` is bound to a non-loopback address and no auth token is configured, mirroring the existing `McpHost` validation rule.

#### Scenario: Dashboard bound to a non-loopback address without a token
- **WHEN** the server starts with `DashboardHost` set to a non-loopback address (e.g. `0.0.0.0`)
- **AND** no `AuthToken` is configured
- **THEN** startup validation SHALL return an error referencing the dashboard host and instructing the operator to set `NEBULA_CTX_HTTP_TOKEN`

#### Scenario: Dashboard bound to a non-loopback address with a token configured
- **WHEN** the server starts with `DashboardHost` set to a non-loopback address
- **AND** a valid `AuthToken` is configured
- **THEN** startup validation SHALL NOT report an error for the dashboard host
