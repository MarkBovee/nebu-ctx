## ADDED Requirements

### Requirement: Client and server changes are verified by automated CI
The repository MUST run an automated build-and-test check for both the Rust client and the .NET server on every pull request and every push to the default branch, independent of the release/tagging pipeline.

#### Scenario: Pull request is opened
- **WHEN** a pull request is opened or updated against the repository
- **THEN** CI SHALL run `cargo test` for the client
- **AND** CI SHALL run `dotnet build` and `dotnet test` for the server
- **AND** CI SHALL report success or failure on the pull request

#### Scenario: Push to the default branch
- **WHEN** a commit is pushed to the default branch
- **THEN** CI SHALL run the same client and server verification independent of whether `auto-release.yml` also triggers a release tag

#### Scenario: Client-only or server-only change gets isolated feedback
- **WHEN** a pull request only touches files under `client/` or only under `server/`
- **THEN** CI SHALL still run both the `client` and `server` jobs independently, so failures in one stack are clearly attributable and do not block reporting on the other
