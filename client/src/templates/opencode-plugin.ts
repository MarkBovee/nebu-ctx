import type { Plugin } from "@opencode-ai/plugin"
import { mkdtemp, rm, writeFile } from "fs/promises"
import { tmpdir } from "os"
import { join } from "path"

const NEBU = "nebu-ctx"

function resolveWindowsNebuExe() {
  const homeDir = process.env["USERPROFILE"] ?? process.env["HOME"] ?? ""
  return join(homeDir, ".cargo", "bin", "nebu-ctx.exe")
}

function resolveNebuBinary() {
  if (process.platform !== "win32") return NEBU

  return process.env["NEBU_CTX_BIN"] ?? process.env["NEBU_CTX_EXE"] ?? resolveWindowsNebuExe()
}

export const NebuCtxOpenCodePlugin: Plugin = async ({ $ }) => {
  try {
    const result = await runNebu(["--version"])
    if (!result || result.exitCode !== 0) {
      throw new Error(result?.stderr || "nebu-ctx --version failed")
    }
  } catch {
    console.warn("[nebu-ctx] nebu-ctx binary not found in PATH - plugin disabled")
    return {}
  }

  const homeDir = process.env["USERPROFILE"] ?? process.env["HOME"] ?? ""
  const dataDir = process.env["NEBU_CTX_DATA_DIR"] ?? `${homeDir}/.nebu-ctx`

  async function runNebu(args: string[], stdinText?: string) {
    try {
      const command = resolveNebuBinary()
      const proc = Bun.spawn([command, ...args], {
        env: { ...process.env, NEBU_CTX_DATA_DIR: dataDir },
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

  async function fireTelemetry(tool: string, tokensOriginal: number, tokensSaved: number) {
    await runNebu(["hook", "telemetry", tool, String(tokensOriginal), String(tokensSaved)])
  }

  return {
    "shell.env": async (_input, output) => {
      output.env["NEBU_CTX_DATA_DIR"] = dataDir
      output.env["NEBU_CTX_BIN"] = resolveNebuBinary()
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

      const hookInput = JSON.stringify({ prompt: text.slice(0, 500) })
      await runNebu(["hook", "user-prompt-submit"], hookInput)
    },
  }
}
