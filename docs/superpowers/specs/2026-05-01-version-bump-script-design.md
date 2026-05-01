# Version Bump Script Design

## Goal

Add a small bash script that updates the repo's required version locations in one run and optionally accepts an explicit target version.

## Scope

Add `scripts/release/bump-version.sh`.

The script must:
- accept an optional `x.y.z` version argument
- default to a patch bump when no version is provided
- update `client/Cargo.toml`
- update `homeassistant/config.yaml`
- update `server/src/NebuCtx.Server.Core/ToolRegistry.cs`
- run `cargo update --manifest-path client/Cargo.toml`
- fail fast if expected version markers are missing
- fail fast on invalid semver input
- print the old and new version

The script must not:
- create commits
- push to git
- create tags
- modify git config

## Behavior

When called without arguments, the script reads the current version from `homeassistant/config.yaml`, validates it as `x.y.z`, increments the patch segment, and applies the new version to all three required files.

When called with a single argument, the script validates the provided value as `x.y.z` and applies it to all three required files.

After file edits, the script runs `cargo update --manifest-path client/Cargo.toml` so the lockfile stays in sync with the version bump workflow already documented in the repo.

## Editing Strategy

Use simple text replacement against exact version patterns already enforced by the repo:
- `version = "..."` in `client/Cargo.toml`
- `version: "..."` in `homeassistant/config.yaml`
- `public const string Current = "...";` in `server/src/NebuCtx.Server.Core/ToolRegistry.cs`

If any expected pattern is not found exactly once, the script exits non-zero instead of guessing.

## Error Handling

The script exits with a clear error when:
- more than one positional argument is provided
- the provided version is not bare semver `x.y.z`
- the current version cannot be read from `homeassistant/config.yaml`
- any required file is missing
- any replacement pattern is missing or ambiguous
- `cargo update --manifest-path client/Cargo.toml` fails

## Validation

Implementation validation should cover:
- explicit bump to `0.7.2`
- successful sync of the three required version files
- successful `cargo update --manifest-path client/Cargo.toml`
- a quick readback of the updated values after the script runs
