## Context

`pathjail.rs`'s `allow_paths_from_env()` builds the list of extra roots the path jail accepts, beyond the caller's project root. For each known IDE/agent config directory name in `IDE_CONFIG_DIRS`, it currently allow-lists `home.join(dir)` — the whole directory — because the join has no subpath restriction. The only real, tested use case is nebu-ctx reading its own previously-written session-state artifacts under `<tool-dir>/session-state/...`. Because this allow-list feeds the live, MCP-exposed `ctx_read` tool, the over-broad grant is a genuine credential-disclosure path reachable by an LLM agent, not just a human operator.

## Goals / Non-Goals

**Goals:**
- Eliminate the credential-disclosure path with the smallest possible change: scope the join to a named `session-state` subdirectory constant.
- Preserve the one legitimate, already-tested behavior (`allows_copilot_session_state_under_home`) exactly.

**Non-Goals:**
- A filename-based credential denylist (e.g. blocking `.env`, `id_rsa`, `*.pem` by name) as an additional defense-in-depth layer. Deferred: the structural fix already makes the concrete, demonstrated exploit unreachable, and a denylist would need extra branching to avoid also blocking legitimate project-root files with the same names.
- Any change to the `NEBU_CTX_ALLOW_PATH`/`LCTX_ALLOW_PATH` env-var branch — that is an explicit, operator-opted-in allow-list, a fundamentally different trust model from the auto-discovered `IDE_CONFIG_DIRS` list.

## Decisions

- **Scope by subdirectory name, not by denylist.** A single `SESSION_STATE_SUBDIR = "session-state"` constant joined onto each `IDE_CONFIG_DIRS` entry is the minimal fix that matches the one real, tested use case exactly — no new data structure, no new configuration surface.
- **Do not change `IDE_CONFIG_DIRS` itself.** The list of tool directory names is still correct; only the scope *within* each one was too broad.

## Risks / Trade-offs

- [Risk] If a future undiscovered codepath reads something outside `session-state/` from one of these directories, this fix would break it. → Mitigation: full-repo grep during planning found zero such callers; the plan's STOP conditions require reporting (not silently widening the allow-list back) if one is found during implementation.
- [Risk] Narrowing might not generalize if a new tool integration needs a different subdirectory name. → Mitigation: documented in Maintenance notes — add a second named constant (e.g. `PLANS_SUBDIR`) rather than reverting to a whole-directory grant.
