---
name: leanctx-agent-workflows
description: Use when coordinating LeanCTX multi-agent work, registering agents, sharing scratchpad context, handing off tasks, or cleaning up pending messages and status.
---

# LeanCTX Agent Workflows

## Overview

LeanCTX works best when every agent has one clear role, one current status, and no dangling messages. Treat the scratchpad like shared state: register cleanly, share what matters, hand off ownership explicitly, and close out every pending item before going idle.

## When to Use

Use this skill when:

- multiple agents are working in one LeanCTX session
- a controller needs to assign or reassign work
- a child agent needs to escalate to a specialist
- context must be shared instead of re-derived
- a session should end with no unread or pending messages

## Core Lifecycle

1. **Register once** with a role and status.
2. **Set the working agent active** before changing anything.
3. **Pull shared context** before starting local work.
4. **Push new findings early** so other agents do not duplicate work.
5. **Hand off ownership explicitly** when another agent should continue.
6. **Read and resolve pending messages immediately.**
7. **Set `idle` or `finished` only after cleanup.**

Violating the order is a leak. If a message is still pending, the session is not clean.

## Quick Reference

| Need | Tooling |
|---|---|
| Add or discover agents | `ctx_agent register`, `ctx_agent list` |
| Inspect work or messages | `ctx_agent read` |
| Update progress | `ctx_agent status` |
| Transfer ownership | `ctx_agent handoff` |
| Share scratchpad facts | `ctx_share push`, `ctx_share pull` |

## Role Notes

| Role | Responsibility |
|---|---|
| Parent | Set scope, assign work, reconcile overlaps, close the session cleanly |
| Child | Do local work, report blockers, keep context current, hand off when blocked |
| Specialist | Do the narrow expert task, return concise findings, avoid scope creep |

## Common Mistakes

- Leaving a message unread because the task is "basically done"
- Re-registering an agent that is already active
- Handoff without pushing the latest scratchpad
- Setting `finished` while there are still pending follow-ups
- Letting multiple agents drift without a single current owner

## Clean Finish Checklist

- all relevant agents registered
- current owner marked active
- latest context shared
- unread messages read
- handoff completed if ownership changed
- no pending follow-ups left behind
- final status updated last
