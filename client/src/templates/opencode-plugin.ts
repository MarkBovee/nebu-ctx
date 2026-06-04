import type { Plugin } from "@opencode-ai/plugin"
import { mkdtemp, readFile, rm, writeFile } from "fs/promises"
import { tmpdir } from "os"
import { join } from "path"

const NEBU = "nebu-ctx"
const SESSION_STARTUP = "startup"
const SESSION_COMPACT = "compact"
const ROUTING_BLOCK = `<context_window_protection>
  Use nebu-ctx MCP tools instead of raw native tools to save tokens:
  - ctx_read / ctx_search / ctx_tree instead of Read / Grep / ls
  - Use native Bash/Shell directly; the nebu-ctx shell hook compresses output automatically
  - ctx_batch_execute for multi-step research (one call replaces many)
  Skills, roles, and decisions from this session remain active until revoked.
</context_window_protection>`
const ROUTING_MARKERS = ["<context_window_protection>", "ctx_read", "ctx_search"]

function resolveWindowsNebuExe() {
  const homeDir = process.env["USERPROFILE"] ?? process.env["HOME"] ?? ""
  return join(homeDir, ".cargo", "bin", "nebu-ctx.exe")
}

async function resolveConfiguredNebuBinary() {
  const configPath = process.env["OPENCODE_CONFIG"]
    ?? join(process.env["USERPROFILE"] ?? process.env["HOME"] ?? "", ".config", "opencode", "opencode.json")

  try {
    const raw = await readFile(configPath, "utf8")
    const config = JSON.parse(raw) as {
      mcp?: Record<string, { command?: unknown }>
    }
    const command = config.mcp?.[NEBU]?.command
    if (!Array.isArray(command)) return ""

    const binary = command.find((value): value is string => typeof value === "string" && value.trim().length > 0)
    return binary ?? ""
  } catch {
    return ""
  }
}

async function resolveNebuBinary() {
  const configuredBinary = await resolveConfiguredNebuBinary()
  if (configuredBinary) return configuredBinary

  if (process.platform !== "win32") return process.env["NEBU_CTX_BIN"] ?? process.env["NEBU_CTX_EXE"] ?? NEBU

  return process.env["NEBU_CTX_BIN"] ?? process.env["NEBU_CTX_EXE"] ?? resolveWindowsNebuExe()
}

function systemHasRoutingInstructions(system: string[]) {
  const text = system.join("\n")
  return ROUTING_MARKERS.filter((marker) => text.includes(marker)).length >= 2
}

function stripRoutingBlock(additionalContext: string) {
  return additionalContext.replace(ROUTING_BLOCK, "").trim()
}

function insertSystemBlock(system: string[], block: string, index: number) {
  if (!block) return
  const boundedIndex = Math.max(0, Math.min(index, system.length))
  system.splice(boundedIndex, 0, block)
}

export const NebuCtxOpenCodePlugin: Plugin = async ({ $, directory }) => {
  const homeDir = process.env["USERPROFILE"] ?? process.env["HOME"] ?? ""
  const dataDir = process.env["NEBU_CTX_DATA_DIR"] ?? `${homeDir}/.nebu-ctx`
  const initializedSessions = new Set<string>()
  const dirtySessions = new Set<string>()
  const pendingSessionContext = new Map<string, typeof SESSION_STARTUP | typeof SESSION_COMPACT>()
  const assistantMessages = new Map<string, string>()
  const assistantParts = new Map<string, { sessionID: string, messageID: string, text: string }>()
  const dirtyAssistantParts = new Set<string>()
  const projectDir = directory || process.cwd()
  const nebuBinary = await resolveNebuBinary()

  try {
    const result = await runNebu(["--version"])
    if (!result || result.exitCode !== 0) {
      throw new Error(result?.stderr || "nebu-ctx --version failed")
    }
  } catch {
    console.warn(`[nebu-ctx] nebu-ctx binary not found at '${nebuBinary}' - plugin disabled`)
    return {}
  }

  async function runNebu(args: string[], stdinText?: string, cwd = projectDir) {
    try {
      const proc = Bun.spawn([nebuBinary, ...args], {
        cwd,
        env: { ...process.env, NEBU_CTX_DATA_DIR: dataDir, NEBU_CTX_BIN: nebuBinary },
        stdin: stdinText ? new Response(stdinText) : null,
        stdout: "pipe",
        stderr: "pipe",
        windowsHide: true,
      })
      const stdout = proc.stdout ? await new Response(proc.stdout).text() : ""
      const stderr = proc.stderr ? await new Response(proc.stderr).text() : ""
      const exitCode = await proc.exited
      return { stdout, stderr, exitCode }
    } catch {
      return null
    }
  }

  function parseHookJson(stdout: string) {
    const trimmed = stdout.trim()
    if (!trimmed) return null

    try {
      return JSON.parse(trimmed) as Record<string, unknown>
    } catch {
      return null
    }
  }

  async function readAdditionalContext(
    hook: "session-start" | "pre-compact",
    input?: Record<string, unknown>,
  ) {
    const result = await runNebu(["hook", hook], input ? JSON.stringify(input) : undefined)
    const parsed = parseHookJson(String(result?.stdout ?? ""))
    const additionalContext = parsed?.additionalContext
    return typeof additionalContext === "string" ? additionalContext.trim() : ""
  }

  async function flushSessionMemory(sessionID: string, reason: "idle" | "stop" = "idle") {
    if (!dirtySessions.has(sessionID)) return

    dirtySessions.delete(sessionID)
    await runNebu(["hook", reason === "stop" ? "stop" : "idle-flush"], JSON.stringify({
      session_id: sessionID,
      source: "opencode",
    }))
  }

  async function flushAssistantOutput(sessionID: string) {
    const parts: string[] = []

    for (const [partID, state] of assistantParts) {
      if (state.sessionID !== sessionID || !dirtyAssistantParts.has(partID)) continue
      if (assistantMessages.get(state.messageID) !== sessionID) continue

      const text = state.text.trim()
      dirtyAssistantParts.delete(partID)
      if (text) {
        parts.push(text)
      }
    }

      const text = parts.join("\n\n").trim()
      if (!text) return

      await runNebu(["hook", "assistant-output-submit"], JSON.stringify({
        session_id: sessionID,
        message: text.slice(0, 4000),
        source: "opencode",
      }))
  }

  function forgetAssistantMessage(messageID: string) {
    assistantMessages.delete(messageID)

    for (const [partID, state] of assistantParts) {
      if (state.messageID !== messageID) continue
      assistantParts.delete(partID)
      dirtyAssistantParts.delete(partID)
    }
  }

  function clearAssistantSessionState(sessionID: string) {
    for (const [messageID, messageSessionID] of assistantMessages) {
      if (messageSessionID === sessionID) {
        assistantMessages.delete(messageID)
      }
    }

    for (const [partID, state] of assistantParts) {
      if (state.sessionID !== sessionID) continue
      assistantParts.delete(partID)
      dirtyAssistantParts.delete(partID)
    }
  }

  function getSessionID(event: unknown) {
    const properties = (event as { properties?: Record<string, unknown> } | null)?.properties
    const sessionID = properties?.sessionID
    if (typeof sessionID === "string" && sessionID) return sessionID

    const infoSessionID = (properties?.info as Record<string, unknown> | undefined)?.sessionID
    if (typeof infoSessionID === "string" && infoSessionID) return infoSessionID

    const partSessionID = (properties?.part as Record<string, unknown> | undefined)?.sessionID
    return typeof partSessionID === "string" && partSessionID ? partSessionID : ""
  }

  async function fireTelemetry(tool: string, tokensOriginal: number, tokensSaved: number) {
    await runNebu(["hook", "telemetry", tool, String(tokensOriginal), String(tokensSaved)])
  }

  return {
    "shell.env": async (_input, output) => {
      output.env["NEBU_CTX_DATA_DIR"] = dataDir
      output.env["NEBU_CTX_BIN"] = nebuBinary
    },

    // Route richer OpenCode lifecycle hooks through nebu-ctx where nebu-ctx
    // already knows how to build memory snapshots and durable session writes.
    "experimental.chat.system.transform": async (input, output) => {
      if (!input.sessionID) return

      // Keep the first system block stable so OpenCode can still fold the
      // remainder for provider prompt caching, matching the proven context-mode pattern.
      if (!systemHasRoutingInstructions(output.system)) {
        insertSystemBlock(output.system, ROUTING_BLOCK, Math.min(1, output.system.length))
      }

      const source = pendingSessionContext.get(input.sessionID)
        ?? (initializedSessions.has(input.sessionID) ? "" : SESSION_STARTUP)
      if (!source) return

      const additionalContext = await readAdditionalContext("session-start", {
        source,
        editor: "opencode",
        session_id: input.sessionID,
      })
      const snapshot = stripRoutingBlock(additionalContext)

      if (snapshot) {
        insertSystemBlock(output.system, snapshot, Math.min(2, output.system.length))
      }

      initializedSessions.add(input.sessionID)
      pendingSessionContext.delete(input.sessionID)
    },

    "experimental.session.compacting": async (_input, output) => {
      const additionalContext = await readAdditionalContext("pre-compact", {
        source: "compact",
        editor: "opencode",
        session_id: _input?.sessionID ?? "",
      })
      if (additionalContext) {
        output.context.push(additionalContext)
      }
    },

    "tool.execute.before": async (input, output) => {
      const tool = String(input?.tool ?? "").toLowerCase()
      if (tool !== "bash" && tool !== "shell") return
      const args = output?.args as Record<string, unknown> | null
      if (!args) return

      const command = args.command
      if (typeof command !== "string" || !command) return
      if (command.startsWith(`${NEBU} `)) return

      const result = await runNebu(["hook", "rewrite-inline", command])
      const rewritten = String(result?.stdout ?? "").trim()
      if (rewritten && rewritten !== command) {
        args.command = rewritten
      }
    },

    "tool.execute.after": async (input, output) => {
      if (typeof input?.sessionID === "string" && input.sessionID) {
        dirtySessions.add(input.sessionID)
      }

      const tool = String(input?.tool ?? "").toLowerCase()
      if (tool !== "bash" && tool !== "shell") return

       const rawOutput = typeof output?.output === "string" ? output.output : ""
       const command = typeof (output?.args as Record<string, unknown> | null)?.command === "string"
         ? String((output?.args as Record<string, unknown>).command)
         : typeof (input as Record<string, unknown> | null)?.command === "string"
           ? String((input as Record<string, unknown>).command)
           : ""

      if (typeof input?.sessionID === "string" && input.sessionID) {
        await runNebu(["hook", "tool-activity"], JSON.stringify({
          session_id: input.sessionID,
          source: "opencode",
          tool_name: tool,
          command,
          tool_response: rawOutput.slice(0, 4000),
        }))
      }

      if (rawOutput.length < 500) return

      try {
        const tmpDir = await mkdtemp(join(tmpdir(), "nebu-ctx-plugin-"))
        const tmpFile = join(tmpDir, "output.txt")
        try {
          await writeFile(tmpFile, rawOutput, "utf8")
          const result = await runNebu(["read", tmpFile])
          const compressed = String(result?.stdout ?? "").trim()
          if (compressed && compressed.length < rawOutput.length * 0.9) {
            const originalTokens = Math.round(rawOutput.length / 4)
            const compressedTokens = Math.round(compressed.length / 4)
            const saved = originalTokens - compressedTokens
            output.output = compressed
            fireTelemetry(tool, originalTokens, saved)
          }
        } finally {
          await rm(tmpDir, { recursive: true, force: true })
        }
      } catch {}
    },

    "chat.message": async (_input, output) => {
      const parts = output?.parts
      if (!Array.isArray(parts)) return
      const textPart = parts.find(
        (p: unknown) => (p as Record<string, unknown>)?.type === "text",
      )
      if (!textPart) return
      const text = String((textPart as Record<string, unknown>).text ?? "").trim()
      if (!text || text.length < 10) return
      if (
        text.startsWith("<session_state") ||
        text.startsWith("<context_guidance>") ||
        text.startsWith("<system-reminder>")
      ) return

      const hookInput = JSON.stringify({ session_id: _input?.sessionID ?? "", prompt: text.slice(0, 500), source: "opencode" })
      if (typeof _input?.sessionID === "string" && _input.sessionID) {
        dirtySessions.add(_input.sessionID)
      }
      await runNebu(["hook", "user-prompt-submit"], hookInput)
    },

    event: async ({ event }) => {
      const sessionID = getSessionID(event)

      if (event.type === "message.updated") {
        const info = (event.properties as { info?: Record<string, unknown> } | undefined)?.info
        const messageID = info?.id
        const role = info?.role
        const messageSessionID = info?.sessionID

        if (
          typeof messageID === "string"
          && typeof role === "string"
          && typeof messageSessionID === "string"
        ) {
          if (role === "assistant") {
            assistantMessages.set(messageID, messageSessionID)
          } else {
            forgetAssistantMessage(messageID)
          }
        }

        return
      }

      if (event.type === "message.removed") {
        const messageID = (event.properties as { messageID?: unknown } | undefined)?.messageID
        if (typeof messageID === "string" && messageID) {
          forgetAssistantMessage(messageID)
        }

        return
      }

      if (event.type === "message.part.updated") {
        const part = (event.properties as {
          part?: Record<string, unknown>
        } | undefined)?.part
        if (!part || part.type !== "text") return

        const messageID = part.messageID
        const partID = part.id
        const partSessionID = part.sessionID
        const text = String(part.text ?? "")
        if (
          typeof messageID !== "string"
          || typeof partID !== "string"
          || typeof partSessionID !== "string"
        ) return
        if (part.synthetic === true || part.ignored === true) return

        assistantParts.set(partID, {
          sessionID: partSessionID,
          messageID,
          text,
        })
        dirtyAssistantParts.add(partID)
        dirtySessions.add(partSessionID)
        return
      }

      if (event.type === "message.part.removed") {
        const partID = (event.properties as { partID?: unknown } | undefined)?.partID
        if (typeof partID === "string" && partID) {
          assistantParts.delete(partID)
          dirtyAssistantParts.delete(partID)
        }

        return
      }

      if (!sessionID) return

      if (event.type === "session.compacted") {
        await flushAssistantOutput(sessionID)
        pendingSessionContext.set(sessionID, SESSION_COMPACT)
        return
      }

      if (event.type === "session.idle") {
        await flushAssistantOutput(sessionID)
        await flushSessionMemory(sessionID, "idle")
        return
      }

      if (event.type === "session.deleted") {
        await flushAssistantOutput(sessionID)
        await flushSessionMemory(sessionID, "stop")
        dirtySessions.delete(sessionID)
        initializedSessions.delete(sessionID)
        pendingSessionContext.delete(sessionID)
        clearAssistantSessionState(sessionID)
      }
    },
  }
}
