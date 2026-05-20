import type { Plugin } from "@opencode-ai/plugin"
import { mkdtemp, readFile, rm, writeFile } from "fs/promises"
import { tmpdir } from "os"
import { join } from "path"

const NEBU = "nebu-ctx"
const SESSION_STARTUP = "startup"
const SESSION_COMPACT = "compact"
const ROUTING_BLOCK = `<context_window_protection>
  Use nebu-ctx MCP tools instead of raw native tools to save tokens:
  - ctx_read / ctx_search / ctx_shell / ctx_tree instead of Read / Grep / Bash / ls
  - ctx_batch_execute for multi-step research (one call replaces many)
  - Bash only for: git, mkdir, rm, mv, navigation
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

  async function flushSessionMemory(sessionID: string) {
    if (!dirtySessions.has(sessionID)) return

    dirtySessions.delete(sessionID)
    await runNebu(["hook", "stop"])
    pendingSessionContext.set(sessionID, SESSION_COMPACT)
  }

  function getSessionID(event: unknown) {
    const properties = (event as { properties?: Record<string, unknown> } | null)?.properties
    const sessionID = properties?.sessionID
    return typeof sessionID === "string" && sessionID ? sessionID : ""
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
      })
      const snapshot = stripRoutingBlock(additionalContext)

      if (snapshot) {
        insertSystemBlock(output.system, snapshot, Math.min(2, output.system.length))
      }

      initializedSessions.add(input.sessionID)
      pendingSessionContext.delete(input.sessionID)
    },

    "experimental.session.compacting": async (_input, output) => {
      const additionalContext = await readAdditionalContext("pre-compact")
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

      const rawOutput = output?.output
      if (typeof rawOutput !== "string" || rawOutput.length < 500) return

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

      const hookInput = JSON.stringify({ prompt: text.slice(0, 500), source: "opencode" })
      if (typeof _input?.sessionID === "string" && _input.sessionID) {
        dirtySessions.add(_input.sessionID)
      }
      await runNebu(["hook", "user-prompt-submit"], hookInput)
    },

    event: async ({ event }) => {
      const sessionID = getSessionID(event)
      if (!sessionID) return

      if (event.type === "session.compacted") {
        pendingSessionContext.set(sessionID, SESSION_COMPACT)
        return
      }

      if (event.type === "session.idle") {
        await flushSessionMemory(sessionID)
        return
      }

      if (event.type === "session.deleted") {
        await flushSessionMemory(sessionID)
        dirtySessions.delete(sessionID)
        initializedSessions.delete(sessionID)
        pendingSessionContext.delete(sessionID)
      }
    },
  }
}
