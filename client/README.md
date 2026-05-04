# nebu-ctx

Rust runtime client for the `nebu-ctx` MCP, host, and dashboard stack.

Homepage: <https://nebu-ctx.com>

## Install

```bash
cargo install nebu-ctx
```

This default install keeps the client lightweight. For a local source build, use:

On Windows, the published crate carries a `rust-lld` override for `x86_64-pc-windows-msvc`, and the default `cargo install nebu-ctx` path is designed to work without Visual Studio Build Tools. Optional native features can still require extra toolchains when you explicitly enable them.

## Local install from source

```bash
cargo install --path client --bin nebu-ctx --force
```

## What it does

- Started from the practical `lean-ctx` client surface and was reshaped into the current `nebu-ctx` runtime client.
- Connects to a running `nebu-ctx` host over HTTP with `nebu-ctx connect`.
- Supports local runtime tools plus server-backed tools such as `ctx_brain` against shared PostgreSQL-backed server state.

## Repository

Project source and deployment assets live in the main repository:

- <https://github.com/MarkBovee/nebu-ctx>
