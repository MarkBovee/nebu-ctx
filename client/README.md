# nebu-ctx

Thin Rust client for the .NET `nebu-ctx` MCP and dashboard server.

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

- Connects to a running `nebu-ctx` server over HTTP.
- Supports the primary `nebu-ctx` CLI entrypoint plus compatibility aliases.
- Works with the shared PostgreSQL-backed MCP and dashboard flow used by this repository.

## Repository

Project source and deployment assets live in the main repository:

- <https://github.com/MarkBovee/nebu-ctx>