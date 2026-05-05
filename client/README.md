# nebu-ctx

Rust runtime client for the `nebu-ctx` MCP, host, and dashboard stack.

Homepage: <https://nebu-ctx.com>

## Install

```bash
cargo binstall nebu-ctx
```

This downloads the prebuilt lightweight client binary with `cargo-binstall` when available. If you do not have cargo-binstall yet, install it using the instructions at <https://github.com/cargo-bins/cargo-binstall>. If `cargo binstall nebu-ctx` is unavailable or no matching artifact exists, use the published release asset instead. If you prefer a source-build fallback, use `cargo install nebu-ctx`.

On Windows, the published crate keeps the `rust-lld` override so the packaged install path does not depend on Visual Studio Build Tools.

## Local install from source

```bash
cargo install --path client --bin nebu-ctx --force
```

## What it does

- Started from an earlier practical client surface and was reshaped into the current `nebu-ctx` runtime client.
- Connects to a running `nebu-ctx` host over HTTP with `nebu-ctx connect`.
- Supports local runtime tools plus server-backed tools such as `ctx_brain` against shared PostgreSQL-backed server state.

## Repository

Project source and deployment assets live in the main repository:

- <https://github.com/MarkBovee/nebu-ctx>
