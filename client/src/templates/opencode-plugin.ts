import type { Plugin } from "@opencode-ai/plugin"

const NEBU = "nebu-ctx"

export const NebuCtxOpenCodePlugin: Plugin = async ({ $ }) => {
  try {
    await $`which nebu-ctx`.quiet()
  } catch {
    console.warn("[nebu-ctx] nebu-ctx binary not found in PATH — plugin disabled")
    return {}
  }

  const dataDir = process.env["NEBU_CTX_DATA_DIR"] ?? `${process.env["HOME"]}/.nebu-ctx`

  async function fireTelemetry(tool: string, tokensOriginal: number, tokensSaved: number) {
    try {
      await $`env NEBU_CTX_DATA_DIR=${dataDir} ${NEBU} hook telemetry ${tool} ${String(tokensOriginal)} ${String(tokensSaved)}`.quiet().nothrow()
    } catch {}
  }

  return {
    "shell.env": async (_input, output) => {
      output.env["NEBU_CTX_DATA_DIR"] = dataDir
    },

    "tool.execute.before": async (input, output) => {
      const tool = String(input?.tool ?? "").toLowerCase()
      if (tool !== "bash" && tool !== "shell") return
      const args = output?.args as Record<string, unknown> | null
      if (!args) return

      const command = args.command
      if (typeof command !== "string" || !command) return
      if (command.startsWith(`${NEBU} `)) return

      try {
        const result = await $`nebu-ctx hook rewrite-inline ${command}`.quiet().nothrow()
        const rewritten = String(result.stdout).trim()
        if (rewritten && rewritten !== command) {
          args.command = rewritten
        }
      } catch {}
    },

    "tool.execute.after": async (input, output) => {
      const tool = String(input?.tool ?? "").toLowerCase()
      if (tool !== "bash" && tool !== "shell") return

      const rawOutput = output?.output
      if (typeof rawOutput !== "string" || rawOutput.length < 500) return

      try {
        const { mkdtemp, writeFile, rm } = await import("fs/promises")
        const { join } = await import("path")
        const tmpDir = await mkdtemp("/tmp/nebu-ctx-plugin-")
        const tmpFile = join(tmpDir, "output.txt")
        try {
          await writeFile(tmpFile, rawOutput, "utf8")
          const result = await $`env NEBU_CTX_DATA_DIR=${dataDir} ${NEBU} read ${tmpFile}`.quiet().nothrow()
          const compressed = String(result.stdout).trim()
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

      try {
        const hookInput = JSON.stringify({ prompt: text.slice(0, 500) })
        await $`sh -c ${`echo ${JSON.stringify(hookInput)} | NEBU_CTX_DATA_DIR=${dataDir} ${NEBU} hook user-prompt-submit`}`.quiet().nothrow()
      } catch {}
    },
  }
}
