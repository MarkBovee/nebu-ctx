# Project Intake Identity Repair Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Stop creating persistent projects from empty repository fingerprints and remap the remaining mis-identified `markb` projects to their real canonical repositories.

**Architecture:** Tighten project intake at both client and server boundaries so only safe repository identity can create or resolve canonical projects. Extend the direct Postgres repair helper to infer real repository identity from persisted checkout bindings, then reassign project-scoped data into the correct canonical project and delete the wrong legacy project records.

**Tech Stack:** Rust client, .NET 10 server, Npgsql/Postgres, xUnit

---

### Task 1: Block Unsafe Intake Identity

**Files:**
- Modify: `client/src/git_context.rs`
- Modify: `client/src/server_client.rs`
- Modify: `client/src/tools/mod.rs`
- Modify: `server/src/NebuCtx.Server.Core/ProjectRegistry.cs`
- Modify: `server/src/NebuCtx.Server.Host/Endpoints/McpEndpoints.cs`
- Modify: `server/src/NebuCtx.Server.Host/Projects/ProjectApiEndpoints.cs`
- Modify: `server/src/NebuCtx.Storage/Postgres/PostgresProjectStore.cs`
- Test: `client/src/git_context.rs`
- Test: `server/tests/NebuCtx.ProjectIdentityTests/ProjectResolutionTests.cs`

- [ ] **Step 1: Write the failing server-side tests**

Add tests covering these behaviors in `server/tests/NebuCtx.ProjectIdentityTests/ProjectResolutionTests.cs`:

```csharp
[Fact]
public async Task UnsafeFingerprint_DoesNotResolveOrCreateProject()
{
    var fingerprint = new RepositoryFingerprint();

    var project = await _registry.ResolveOrCreateAsync(fingerprint, "project-markb");

    Assert.Null(project);
}

[Fact]
public async Task SafeFingerprint_StillCreatesProject()
{
    var fingerprint = new RepositoryFingerprint
    {
        RemoteUrl = "https://github.com/MarkBovee/nebu-ctx.git",
        Host = "github.com",
        Owner = "MarkBovee",
        RepoName = "nebu-ctx",
    };

    var project = await _registry.ResolveOrCreateAsync(fingerprint, "nebu-ctx");

    Assert.NotNull(project);
    Assert.Equal("nebu-ctx", project!.Slug);
}
```

- [ ] **Step 2: Run server-side tests to verify failure**

Run: `dotnet test server/tests/NebuCtx.ProjectIdentityTests/NebuCtx.ProjectIdentityTests.csproj --filter "UnsafeFingerprint_DoesNotResolveOrCreateProject|SafeFingerprint_StillCreatesProject"`
Expected: The unsafe-fingerprint test fails because the current server creates a project.

- [ ] **Step 3: Write the failing client-side test**

Add this test in `client/src/git_context.rs`:

```rust
#[test]
fn empty_fingerprint_is_not_safe_repository_identity() {
    let fingerprint = RepositoryFingerprint {
        remote_url: None,
        host: None,
        owner: None,
        repo_name: None,
        default_branch: None,
    };

    assert!(!fingerprint.has_safe_identity());
}
```

- [ ] **Step 4: Run client-side test to verify failure**

Run: `cargo test --manifest-path client/Cargo.toml empty_fingerprint_is_not_safe_repository_identity`
Expected: FAIL because `has_safe_identity()` does not exist yet.

- [ ] **Step 5: Implement minimal client identity guard**

Add a helper on `RepositoryFingerprint` in `client/src/models.rs` and use it before sending project resolution / tool / telemetry requests:

```rust
impl RepositoryFingerprint {
    pub fn has_safe_identity(&self) -> bool {
        self.remote_url.as_ref().is_some_and(|value| !value.trim().is_empty())
            || (self.host.as_ref().is_some_and(|value| !value.trim().is_empty())
                && self.owner.as_ref().is_some_and(|value| !value.trim().is_empty())
                && self.repo_name.as_ref().is_some_and(|value| !value.trim().is_empty()))
    }
}
```

Update `client/src/server_client.rs` and `client/src/tools/mod.rs` so empty fingerprints are serialized as `None`, not as an empty object.

- [ ] **Step 6: Implement minimal server identity guard**

In `server/src/NebuCtx.Server.Core/ProjectRegistry.cs`, reject unsafe fingerprints before querying or creating:

```csharp
if (!LegacyProjectCleanupRules.HasSafeFingerprint(new ProjectRecord
    {
        ProjectId = "validation",
        Slug = suggestedSlug,
        Fingerprint = fingerprint,
    }))
{
    return null;
}
```

In `server/src/NebuCtx.Server.Host/Endpoints/McpEndpoints.cs`, skip project auto-create for telemetry when fingerprint is unsafe and keep `projectId` empty.

In `server/src/NebuCtx.Server.Host/Projects/ProjectApiEndpoints.cs`, keep the existing `409` behavior when project resolution returns `null`.

- [ ] **Step 7: Run the narrow tests to verify green**

Run: `dotnet test server/tests/NebuCtx.ProjectIdentityTests/NebuCtx.ProjectIdentityTests.csproj --filter "UnsafeFingerprint_DoesNotResolveOrCreateProject|SafeFingerprint_StillCreatesProject"`
Expected: PASS

Run: `cargo test --manifest-path client/Cargo.toml empty_fingerprint_is_not_safe_repository_identity`
Expected: PASS

### Task 2: Infer Real Repo Identity For Remaining markb Projects

**Files:**
- Modify: `server/tools/NebuCtx.DataRepair/Program.cs`
- Test: `server/tests/NebuCtx.ProjectIdentityTests/LegacyProjectCleanupRulesTests.cs`

- [ ] **Step 1: Write the failing helper-focused test**

Extend `server/tests/NebuCtx.ProjectIdentityTests/LegacyProjectCleanupRulesTests.cs` with a pure helper test around slug normalization and merge eligibility:

```csharp
[Fact]
public void CanonicalSlugFromRepoName_UsesRepositoryName()
{
    Assert.Equal("nebu-ctx", LegacyProjectCleanupRules.CanonicalSlugFromRepoName("nebu-ctx"));
}
```

- [ ] **Step 2: Run test to verify failure**

Run: `dotnet test server/tests/NebuCtx.ProjectIdentityTests/NebuCtx.ProjectIdentityTests.csproj --filter CanonicalSlugFromRepoName_UsesRepositoryName`
Expected: FAIL because the helper does not exist yet.

- [ ] **Step 3: Extend repair helper to inspect bindings and derive git identity**

Update `server/tools/NebuCtx.DataRepair/Program.cs` to:

```csharp
SELECT p.project_id, p.slug, ..., b.local_root
FROM projects p
LEFT JOIN checkout_bindings b ON b.project_id = p.project_id
WHERE p.slug ILIKE '%mark%'
```

For each preserved `markb` project:

```csharp
var remoteUrl = await GitOutputAsync(localRoot, "config", "--get", "remote.origin.url");
var parsed = ParseRemoteUrl(remoteUrl);
```

Create an in-memory target fingerprint from the parsed remote and derive canonical slug from `repo_name`.

- [ ] **Step 4: Add reassign/merge path in the helper**

Inside a transaction, for each misidentified project with a resolvable remote:

```csharp
var targetProjectId = await FindOrCreateCanonicalProjectAsync(connection, transaction, fingerprint, canonicalSlug, sourceProject.ProjectMetadata);
await ReassignProjectScopedDataAsync(connection, transaction, sourceProject.ProjectId, targetProjectId);
await DeleteByProjectIdAsync(connection, transaction, "projects", sourceProject.ProjectId);
```

Reassign these tables with `UPDATE ... SET project_id = @to WHERE project_id = @from` where possible:
- `knowledge_entries`
- `brain_entries`
- `session_state`
- `telemetry_events`
- `project_files`
- `project_symbols`
- `project_call_edges`
- `checkout_bindings`

Keep `projects` as delete/insert logic, not reassignment.

- [ ] **Step 5: Run helper build to verify compile**

Run: `dotnet build server/tools/NebuCtx.DataRepair/NebuCtx.DataRepair.csproj`
Expected: Build succeeds with 0 errors.

### Task 3: Run Live Repair And Verify Canonical Projects

**Files:**
- Modify: `/tmp/nebu-data-repair-live.json` (runtime artifact)
- Modify: `/tmp/nebu-data-repair-postcheck.json` (runtime artifact)

- [ ] **Step 1: Run live repair against DATABASE_URL**

Run:

```bash
set -a && . "/mnt/work/Projects/Personal/nebu-ctx/.env" && dotnet run --project server/tools/NebuCtx.DataRepair/NebuCtx.DataRepair.csproj >/tmp/nebu-data-repair-live.json
```

Expected: JSON output that shows preserved candidates, inferred repo identity, migration targets, and moved/deleted counts.

- [ ] **Step 2: Run a post-check verification pass**

Run:

```bash
set -a && . "/mnt/work/Projects/Personal/nebu-ctx/.env" && dotnet run --project server/tools/NebuCtx.DataRepair/NebuCtx.DataRepair.csproj >/tmp/nebu-data-repair-postcheck.json
```

Expected: No remaining `safe_to_delete` legacy projects, and no remaining wrong `markb` projects if remapping succeeded.

- [ ] **Step 3: Verify only canonical projects remain**

Run:

```bash
rg '"Slug": "markb"|"Slug": "project-mark"' /tmp/nebu-data-repair-postcheck.json
```

Expected: No matches, or only explicitly documented unresolved projects that could not be mapped from checkout bindings.

### Task 4: Final Regression Verification

**Files:**
- Modify: none
- Test: `server/tests/NebuCtx.ProjectIdentityTests/NebuCtx.ProjectIdentityTests.csproj`
- Test: `client/Cargo.toml`

- [ ] **Step 1: Run project identity tests**

Run: `dotnet test server/tests/NebuCtx.ProjectIdentityTests/NebuCtx.ProjectIdentityTests.csproj`
Expected: PASS

- [ ] **Step 2: Run relevant Rust tests**

Run: `cargo test --manifest-path client/Cargo.toml git_context`
Expected: PASS

- [ ] **Step 3: Build the repair helper one more time**

Run: `dotnet build server/tools/NebuCtx.DataRepair/NebuCtx.DataRepair.csproj`
Expected: Build succeeds.

- [ ] **Step 4: Commit the fix**

```bash
git add client/src/git_context.rs client/src/models.rs client/src/server_client.rs client/src/tools/mod.rs server/src/NebuCtx.Server.Core/ProjectRegistry.cs server/src/NebuCtx.Server.Core/LegacyProjectCleanupRules.cs server/src/NebuCtx.Server.Host/Endpoints/McpEndpoints.cs server/src/NebuCtx.Server.Host/Projects/ProjectApiEndpoints.cs server/tools/NebuCtx.DataRepair/Program.cs server/tools/NebuCtx.DataRepair/NebuCtx.DataRepair.csproj server/tests/NebuCtx.ProjectIdentityTests/ProjectResolutionTests.cs server/tests/NebuCtx.ProjectIdentityTests/LegacyProjectCleanupRulesTests.cs docs/superpowers/plans/2026-05-04-project-intake-identity-repair.md
git commit -m "fix: block unsafe project intake and repair misidentified projects"
```
