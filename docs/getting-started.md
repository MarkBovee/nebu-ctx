# Getting Started

Install `nebu-ctx`, run setup once, then verify that the shell hook and MCP wiring are active.

## Get Started In 3 Steps

### 1. Install the binary

Pick one supported install path:

```bash
# Install from GitHub with Cargo
cargo install --git https://github.com/MarkBovee/nebu-ctx --bin nebu-ctx

# Or build from source
git clone https://github.com/MarkBovee/nebu-ctx.git
cd nebu-ctx
cargo build --release
```

If you built from source, use `./target/release/nebu-ctx` or add that directory to your `PATH`.

### 2. Run setup

```bash
nebu-ctx setup
```

This is the preferred path when you want shell hooks plus automatic editor detection.

Manual fallback:

```bash
nebu-ctx init --global
```

Agent rules fallback for a specific client:

```bash
nebu-ctx init --agent cursor
nebu-ctx init --agent claude
nebu-ctx init --agent copilot
```

### 3. Restart and verify

Restart your shell, then restart your editor completely.

```bash
nebu-ctx --version
nebu-ctx doctor
```

Expected verification path:

- `nebu-ctx --version` prints the installed version
- `nebu-ctx doctor` checks PATH, config, shell hook, MCP, and dashboard state
- your editor should show the `nebu-ctx` MCP server after restart

## Shell Restart Notes

- Zsh: `source ~/.zshrc`
- Bash: `source ~/.bashrc`
- Fish: `source ~/.config/fish/config.fish`
- PowerShell: close and reopen PowerShell

## Local HTTP Server Quick Check

If you want to verify the HTTP MCP surface locally:

```bash
nebu-ctx serve --host 127.0.0.1 --port 4242 --auth-token local-test-token
```

Then in a second terminal:

```bash
curl -H 'Authorization: Bearer local-test-token' http://127.0.0.1:4242/health
curl -H 'Authorization: Bearer local-test-token' http://127.0.0.1:4242/v1/tools
```

## Home Assistant Add-on

Home Assistant uses a separate add-on packaging path under `homeassistant/`.

Published add-on behavior:

- downloads the tagged `nebu-ctx` release binary for the target architecture
- starts the dashboard on `3333`
- starts the authenticated MCP HTTP server on `4242`
- uses PostgreSQL as the add-on backing store
- persists or generates the token in `/data/auth_token`

For Home Assistant-specific setup and local smoke testing, see [homeassistant/README.md](../homeassistant/README.md).

## Troubleshooting

### `nebu-ctx` not found

Make sure the binary directory is on your `PATH`, or use the full path to the built binary.

### MCP tools do not appear in the editor

- run `nebu-ctx doctor`
- run `nebu-ctx init --agent <name>` if editor auto-detection missed your client
- fully restart the editor, not just the active window

### Home Assistant add-on installs slowly

The published add-on should download a release binary, not compile Rust. If you still see a full Cargo build during install, verify that the add-on version in `homeassistant/build.yaml` points at a published tag with release assets.