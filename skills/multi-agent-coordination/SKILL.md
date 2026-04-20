---
name: multi-agent-coordination
description: Use this skill whenever a task could benefit from parallel execution, task handoff between sessions, or when the user mentions another running session, shell, or agent. Also trigger when a task is large enough to split into independent subtasks, involves different expertise areas, or when the user asks about multi-agent workflows. Use when the user says things like "I have another session running", "can we parallelize this", "split this up", or when the task clearly has independent parts that could run concurrently.
---

# Multi-Agent Coordination with lean-ctx

This skill helps evaluate whether a task benefits from multi-agent coordination and sets it up via lean-ctx MCP tools.

## When multi-agent makes sense

A task is a good candidate when **two or more** of these apply:

- The task has **independent subtasks** (e.g., write article A + write article B)
- The user has **another session running** that could help
- One part involves **waiting** (builds, deploys) while another can proceed
- The task spans **different expertise** (research + coding, content + images)
- The task is **large** enough that sequential execution is wasteful

A task is **NOT** a good candidate when:

- Steps depend on each other sequentially (B needs output from A)
- It's a small, focused task
- There's only one clear subtask

## Evaluation flow

When you detect multi-agent potential, follow this flow:

### Step 1: Quick assessment

Before proposing, silently evaluate:
1. Can the task split into >= 2 independent parts?
2. Are there >= 2 agents/sessions available or spawnable?
3. Does parallel execution actually save time?

If any answer is "no", proceed normally without multi-agent. Don't mention the skill or multi-agent — just do the work.

### Step 2: Propose the split

If multi-agent makes sense, present a proposal with **explicit agent count** and **copy-paste commands** for extra shells:

```
Multi-agent geschikt voor deze taak.

Benodigd: [N] agents (1 beschikbaar + [N-1] extra)

Agent 1 (deze sessie): [wat deze sessie doet]
Agent 2 (extra shell): [wat agent 2 doet]
[Agent 3 (extra shell): ...indien nodig]

Type: [subagents | bestaande sessie | mixed]
```

Then provide a **setup checklist** the user can copy into each new shell:

```markdown
## Setup voor extra shells

Open [N-1] nieuwe Claude Code sessies en plak dit per shell:

**Shell 2:**
\```bash
claude
\```
Plak in de prompt:
> Register met lean-ctx en start met [taak beschrijving]:
> ctx_agent(action="register", agent_type="claude", role="dev")
> ctx_agent(action="post", category="status", message="Started: [taak]")
> Gebruik de content-pipeline skill voor: [specifiek artikel/taak]
```

For subagent-based splits, no user action is needed — the current session spawns them automatically.

#### Auto-launch extra shells

Instead of asking the user to open shells manually, offer to auto-launch them:

**Interactive** (new terminal window, user can interact):
```bash
# Windows Terminal — new tab
wt -w 0 nt -d "PROJECT_DIR" cmd /k "claude"

# Windows — new CMD window
start cmd /k "cd /d PROJECT_DIR && claude"

# macOS — new Terminal.app window
osascript -e 'tell app "Terminal" to do script "cd PROJECT_DIR && claude"'
```

**Non-interactive / fire-and-forget** (runs in background, outputs to file):
```bash
# Claude with prompt — runs and exits
start /b claude -p "Register with lean-ctx and do: [task description]" > NUL 2>&1

# macOS / Linux
claude -p "Register with lean-ctx and do: [task description]" &
```

When offering auto-launch, present it as an option in the proposal:

```
Setup opties:
  [1] Ik start de extra shells automatisch (non-interactive background agents)
  [2] Ik open nieuwe terminal windows/tabbladen — jij typt het start-commando
  [3] Ik gebruik subagents (geen extra terminals nodig)
```

**Wait for user approval** before proceeding. The user may:
- Approve as proposed
- Adjust the split
- Ask to use subagents instead of extra shells
- Decline (proceed sequentially)

### Step 3: Agent types — how to create them

There are two ways to get additional agents:

#### Option A: Subagents (automated)

Spawn via the `Agent` tool. Each subagent:
1. Is a temporary agent that runs and reports back
2. Registers itself with lean-ctx if it needs coordination
3. Gets destroyed after completing its task

Use when: task is self-contained, no user interaction needed, can run in background.

```
Agent({
  description: "brief task description",
  prompt: "Register with lean-ctx: ctx_agent(action='register', agent_type='subagent', role='dev').
  Then do [specific task].
  When done: ctx_agent(action='post', category='status', message='Completed: [result]')",
  run_in_background: true
})
```

Key: include registration + status posting in the prompt so the subagent integrates with lean-ctx.

#### Option B: Existing sessions (manual coordination)

The user has another Claude Code / Copilot / Gemini session running. Coordination happens via lean-ctx message bus:

1. **Register** this session: `ctx_agent(action="register", agent_type="claude", role="dev")`
2. **Check who's online**: `ctx_agent(action="sync")`
3. **Claim your part**: `ctx_agent(action="post", category="status", message="Working on [task]")`
4. **Share files**: `ctx_share(action="push", paths="file1,file2", to_agent="agent-id")`
5. **Read messages**: `ctx_agent(action="read")`
6. **Hand off when done**: `ctx_agent(action="handoff", to_agent="agent-id", message="Completed X, Y still needs doing")`

Use when: user has multiple terminals open, needs interactive work in each.

#### Option C: Mixed

This session handles interactive work; subagents handle background tasks (research, file processing, etc.).

### Step 4: Coordination patterns

#### Parallel execution

Both agents work simultaneously on independent parts. No dependencies.

```
Agent 1 → task A ──┐
                    ├──→ merge results
Agent 2 → task B ──┘
```

Use `ctx_share` to share relevant files after each agent finishes.

#### Pipeline / handoff

Agent A does part 1, hands off to Agent B for part 2. Agent B picks up where A left off.

```
Agent 1 → task A → handoff ──→ Agent 2 → task B
```

Use `ctx_agent(action="handoff")` with a clear summary of what's done and what's next.

#### Research + execution

One agent researches while the other executes. Research results shared in real-time via `ctx_share`.

```
Agent 1 → research → ctx_share ──→ Agent 2 → implementation
```

### Step 5: During execution

While agents are working:

1. **Check status** periodically: `ctx_agent(action="sync")`
2. **Read messages**: `ctx_agent(action="read")`
3. **Share relevant files** as they're created/modified: `ctx_share(action="push", ...)`
4. **Post blockers**: `ctx_agent(action="post", category="warning", message="Blocked on X")`

### Step 6: Merge and complete

When all agents finish:

1. Pull shared contexts: `ctx_share(action="pull")`
2. Verify no conflicts in overlapping files
3. **Read and clear pending messages**: `ctx_agent(action="read")` — this drains the message queue so other sessions don't see stale pending messages
4. Post completion: `ctx_agent(action="status", status="finished")`
5. Report results to the user

Without step 3, messages accumulate in the bus and show up as "pending" in other sessions — even though the work is done. Always drain the queue before marking finished.

## Quick reference

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
| Set status | `ctx_agent(action="status", status="active"|"idle"|"finished")` |
