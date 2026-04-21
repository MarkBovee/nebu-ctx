# nebu-ctx Deployment Guide

## Quick Start

### Build with Postgres Support
```bash
cargo build --release --features cloud-server
```

### Connect to Your Postgres
```bash
# Option 1: Interactive wizard
./target/release/nebu-ctx db connect

# Option 2: Environment variables
export NEBULA_STORE=postgres
export DATABASE_URL="postgres://user:pass@host:5432/db"
```

### Commands
```bash
nebu-ctx db status    # Show database status
nebu-ctx db init      # Initialize schema
nebu-ctx db test      # Test connection
nebu-ctx db connect   # Interactive setup
```

## Configuration

### Environment Variables
| Variable | Description |
|----------|-------------|
| `NEBULA_STORE` | `sqlite` (default) or `postgres` |
| `DATABASE_URL` | PostgreSQL connection URL |

### Config File
Save to `~/.nebu-ctx/db.env`:
```bash
export NEBULA_STORE=postgres
export DATABASE_URL=postgres://user:pass@host:5432/db
```

Source it: `source ~/.nebu-ctx/db.env`

## MCP Server

### Run as MCP Server
```bash
# Stdio mode
./target/release/nebu-ctx

# HTTP mode
./target/release/nebu-ctx serve
```

## Issues Known

- Build requires `--features cloud-server` for Postgres support
- Some warnings about unused mut ( cosmetic)