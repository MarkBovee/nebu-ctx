# nebu-ctx

Rust runtime client for the cloud-backed `nebu-ctx` MCP and dashboard stack.

Homepage: <https://nebu-ctx.com>

## Install

```bash
cargo install nebu-ctx
```

## Local install from source

```bash
cargo install --path client --bin nebu-ctx --force
```

## What it does

- Restores the original `lean-ctx` runtime surface under the `nebu-ctx` product name.
- Connects to a running `nebu-ctx` cloud host over HTTP with `nebu-ctx cloud connect`.
- Supports local runtime tools plus cloud-backed tools such as `ctx_brain` against the shared PostgreSQL-backed server state.

## Repository

Project source and deployment assets live in the main repository:

- <https://github.com/MarkBovee/nebu-ctx>