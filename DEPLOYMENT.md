# Nebula Ctx Deployment Guide

## Quick Start

### Build with Postgres Support
```bash
cargo build --release --features cloud-server
```

### Connect to Your Postgres
```bash
# Option 1: Interactive wizard
./target/release/nebula-ctx db connect

# Option 2: Environment variables
export NEBULA_STORE=postgres
export DATABASE_URL="postgres://user:pass@host:5432/db"
```

### Commands
```bash
nebula-ctx db status    # Show database status
nebula-ctx db init    # Initialize schema
nebula-ctx db test   # Test connection
nebula-ctx db connect # Interactive setup
```

## Configuration

### Environment Variables
| Variable | Description |
|----------|-------------|
| `NEBULA_STORE` | `sqlite` (default) or `postgres` |
| `DATABASE_URL` | PostgreSQL connection URL |

### Config File
Save to `~/.nebula-ctx/db.env`:
```bash
export NEBULA_STORE=postgres
export DATABASE_URL=postgres://user:pass@host:5432/db
```

Source it: `source ~/.nebula-ctx/db.env`

## MCP Server

### Run as MCP Server
```bash
# Stdio mode
./target/release/nebula-ctx

# HTTP mode
./target/release/nebula-ctx serve
```

## Issues Known

- Build requires `--features cloud-server` for Postgres support
- Some warnings about unused mut ( cosmetic)