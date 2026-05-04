# Windows Client Install Design

## Context

`nebu-ctx` is a thin client product. The current documented Rust install path uses `cargo install nebu-ctx`, which compiles from source on the user's machine. On Windows this still exposes linker and native toolchain requirements, which is not acceptable for a simple client install experience.

## Goal

Make Windows client installation simple and reliable for end users without requiring Visual Studio Build Tools or any other local build toolchain.

## Non-Goals

- Replacing Cargo as a developer workflow for local source builds.
- Adding a full Windows GUI installer in this change.
- Changing the client/server runtime architecture.

## Approaches Considered

### 1. Keep `cargo install` as the primary path

This keeps the current Rust-first documentation and continues trying to remove Windows compile blockers. It is the smallest release-process change, but it still depends on local compilation behavior and transitive crate/toolchain details on user machines.

This was rejected because it keeps the user experience fragile for a product client.

### 2. Use `cargo-binstall` as the primary Rust install path

Publish prebuilt release binaries for supported targets and document `cargo binstall nebu-ctx` as the main Rust-user install path. Keep `cargo install nebu-ctx` only as a fallback for developers or unsupported targets.

This is the recommended approach because it preserves a Rust-native install flow while removing local compilation from the normal user path.

### 3. Move directly to standalone Windows installers only

Ship `.zip`, `.exe`, or `.msi` artifacts and stop promoting Cargo-based installs for Windows. This is attractive for non-Rust users, but it is a larger distribution and packaging change than needed right now.

This is deferred. It can be layered on later without conflicting with approach 2.

## Chosen Design

Adopt `cargo-binstall` as the primary installation path for Rust users, including Windows users.

The product-facing install flow becomes:

```bash
cargo binstall nebu-ctx
```

This requires the release pipeline to publish prebuilt client binaries for the supported targets so `cargo-binstall` can fetch them instead of compiling from source.

`cargo install nebu-ctx` remains supported as a fallback and developer-oriented path, but it is no longer the recommended default in docs aimed at end users.

## Release And Packaging Requirements

- Continue publishing release assets for Windows client binaries.
- Ensure asset naming and metadata are compatible with `cargo-binstall` lookup expectations.
- Treat missing prebuilt artifacts as a release regression because they would force users back onto local compilation.

## Documentation Changes

- Update the main `README.md` install section to recommend `cargo binstall nebu-ctx` first.
- Update `client/README.md` to do the same.
- Reframe `cargo install nebu-ctx` as a fallback or source-build path rather than the primary product install route.
- Remove wording that implies the normal Windows install path should depend on linker workarounds.

## Error Handling And Fallbacks

If `cargo-binstall` cannot find a matching artifact for a target, the docs should direct users to:

1. install via the published release asset directly, or
2. use `cargo install nebu-ctx` only if they intentionally want a local source build.

The default documentation should not normalize local compilation as the expected Windows user experience.

## Testing And Verification

- Verify that the release pipeline produces downloadable Windows client assets.
- Verify that `cargo binstall nebu-ctx` resolves those assets correctly for Windows targets.
- Keep a narrow smoke test or release validation step that checks the documented install command still works against published artifacts.
- Keep `cargo install` validation as a secondary developer path, not as the main Windows acceptance criterion.

## Success Criteria

- A Windows user can install the client through the documented Rust path without needing Visual Studio Build Tools.
- The primary install docs no longer rely on source compilation.
- Release validation catches missing prebuilt artifacts before the release is considered complete.
