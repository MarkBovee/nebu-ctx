import type { Plugin } from "@opencode-ai/plugin"

export const NebuCtxOpenCodePlugin: Plugin = async ({ $ }) => {
  try {
    await $`which nebu-ctx`.quiet()
  } catch {
    console.warn("[nebu-ctx] nebu-ctx binary not found in PATH — plugin disabled")
    return {}
  }

  return {
    "tool.execute.before": async (input, output) => {
      const tool = String(input?.tool ?? "").toLowerCase()
      if (tool !== "bash" && tool !== "shell") return
      const args = output?.args
      if (!args || typeof args !== "object") return

      const command = (args as Record<string, unknown>).command
      if (typeof command !== "string" || !command) return
      if (command.startsWith("nebu-ctx ")) return

      try {
        const result = await $`nebu-ctx hook rewrite-inline ${command}`.quiet().nothrow()
        const rewritten = String(result.stdout).trim()
        if (rewritten && rewritten !== command) {
          ;(args as Record<string, unknown>).command = rewritten
        }
      } catch {
        // nebu-ctx rewrite failed — pass through unchanged
      }
    },
  }
}
