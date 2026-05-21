---
name: project-bootstrap
description: Preview-first repository mapping and memory bootstrap workflow. Use when the user asks to scan, map, bootstrap, or understand a repository and wants candidate facts reviewed before storing them.
---

# project-bootstrap

Use this skill when the user wants a repository scan that turns into reviewed memory facts.

## Trigger

Load this skill when the user asks to:
- bootstrap project knowledge
- map or scan a repository for memory
- build a project map before storing facts
- review candidate facts before saving them

## Flow

Preview first:

```bash
nebu-ctx project-bootstrap preview [--path <repo>]
```

Apply only after approval:

```bash
nebu-ctx project-bootstrap apply [--path <repo>]
```

Preview summarizes stack, entrypoints, tests, infra, modules, and workflow signals.
Preview does not store anything by itself.

## Setup fallback

If `nebu-ctx` is missing locally:

```bash
which nebu-ctx || bash scripts/install.sh
```

If that local script is unavailable:

```bash
curl -fsSL https://raw.githubusercontent.com/markbovee/nebu-ctx/main/skills/project-bootstrap/scripts/install.sh | bash
```

Then configure integration:

```bash
nebu-ctx setup --global
nebu-ctx doctor --fix
```
