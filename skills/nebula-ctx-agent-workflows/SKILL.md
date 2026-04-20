---
name: nebula-ctx-agent-workflows
description: Use when coordinating multi-agent work with nebula-ctx. Triggers on parallel execution, task handoff between sessions, multi-agent workflows, or when the user mentions another running session, shell, or agent. Also use when registering agents, sharing scratchpad context, handing off tasks, or cleaning up pending messages and status. Use when the user says "parallelize", "split this up", "I have another session running", or when the task clearly has independent parts that could run concurrently.
---

# nebula-ctx Agent Workflows

Multi-agent coordination lifecycle for nebula-ctx. Covers evaluation, setup, execution patterns, and clean completion.

## When multi-agent makes sense

**Good candidate** (2+ of these apply):
- Task has independent subtasks
- Another session is already running that could help
- One part involves waiting (builds, deploys) while another can proceed
- Task spans different expertise (research + coding, content + images)
- Task is large enough that sequential execution is wasteful

**NOT a good candidate:**
- Steps depend on each other sequentially (B needs output from A)
- Small, focused task
- Only one clear subtask

## Core Lifecycle

1. **Register once** with a role and status
2. **Set the working agent active** before changing anything
3. **Pull shared context** before starting local work
4. **Push new findings early** so other agents do not duplicate work
5. **Hand off ownership explicitly** when another agent should continue
6. **Read and resolve pending messages immediately**
7. **Set `idle` or `finished` only after cleanup**

Violating the order is a leak. If a message is still pending, the session is not clean.

## Setup Flow

### Step 1: Quick assessment (silent)

Before proposing, evaluate:
1. Can the task split into >= 2 independent parts?
2. Are there >= 2 agents/sessions available or spawnable?
3. Does parallel execution actually save time?

If any answer is "no", proceed normally without multi-agent. Don't mention the skill — just do the work.

### Step 2: Propose the split

If multi-agent makes sense, present a proposal:

```
Multi-agent suitable for this task.

Required: [N] agents (1 available + [N-1] extra)

Agent 1 (this session): [what this session does]
Agent 2 (extra shell): [what agent 2 does]

Type: [subagents | existing session | mixed]
```

### Step 3: Agent types

#### Option A: Subagents (automated)

Spawn via the `Agent` tool. Each subagent registers with nebula-ctx, does its task, reports back.

```
Agent({
  description: "brief task description",
  prompt: "Register with nebula-ctx: ctx_agent(action='register', agent_type='subagent', role='dev').
  Then do [specific task].
  When done: ctx_agent(action='post', category='status', message='Completed: [result]')",
  run_in_background: true
})
```

Use when: task is self-contained, no user interaction needed.

#### Option B: Existing sessions (manual coordination)

The user has another session running. Coordination via nebula-ctx message bus:

1. **Register**: `ctx_agent(action="register", agent_type="claude", role="dev")`
2. **Check who's online**: `ctx_agent(action="sync")`
3. **Claim your part**: `ctx_agent(action="post", category="status", message="Working on [task]")`
4. **Share files**: `ctx_share(action="push", paths="file1,file2", to_agent="agent-id")`
5. **Read messages**: `ctx_agent(action="read")`
6. **Hand off when done**: `ctx_agent(action="handoff", to_agent="agent-id", message="Completed X")`

Use when: user has multiple terminals open, needs interactive work in each.

#### Option C: Mixed

This session handles interactive work; subagents handle background tasks.

### Step 4: Coordination patterns

**Parallel execution** — both agents work simultaneously on independent parts:
```
Agent 1 → task A ──┐
                    ├──→ merge results
Agent 2 → task B ──┘
```

**Pipeline / handoff** — Agent A does part 1, hands off to Agent B:
```
Agent 1 → task A → handoff ──→ Agent 2 → task B
```

**Research + execution** — one researches while other executes:
```
Agent 1 → research → ctx_share ──→ Agent 2 → implementation
```

### Step 5: During execution

1. Check status: `ctx_agent(action="sync")`
2. Read messages: `ctx_agent(action="read")`
3. Share files: `ctx_share(action="push", ...)`
4. Post blockers: `ctx_agent(action="post", category="warning", message="Blocked on X")`

### Step 6: Merge and complete

When all agents finish:
1. Pull shared contexts: `ctx_share(action="pull")`
2. Verify no conflicts in overlapping files
3. **Read and clear pending messages**: `ctx_agent(action="read")` — drains the message queue
4. Post completion: `ctx_agent(action="status", status="finished")`
5. Report results to user

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

- All relevant agents registered
- Current owner marked active
- Latest context shared
- Unread messages read
- Handoff completed if ownership changed
- No pending follow-ups left behind
- Final status updated last

## Quick Reference

| Action | Command |
|--------|---------|
| Register agent | `ctx_agent(action="register", agent_type="claude", role="dev")` |
| Check who's online | `ctx_agent(action="sync")` |
| List all agents | `ctx_agent(action="list")` |
| Post message | `ctx_agent(action="post", category="status", message="...")` |
| Read messages | `ctx_agent(action="read")` |
| Share files | `ctx_share(action="push", paths="...", to_agent="...")` |
| Receive shared files | `ctx_share(action="pull")` |
| Hand off task | `ctx_agent(action="handoff", to_agent="...", message="...")` |
| Log progress | `ctx_agent(action="diary", category="progress", message="...")` |
| Set status | `ctx_agent(action="status", status="active"\|"idle"\|"finished")` |
