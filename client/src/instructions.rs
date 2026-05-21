use crate::tools::CrpMode;

/// Claude Code truncates MCP server instructions at 2048 characters.
/// Full instructions are installed as `~/.claude/rules/nebu-ctx.md` instead.
const CLAUDE_CODE_INSTRUCTION_CAP: usize = 2048;

pub fn build_instructions(crp_mode: CrpMode) -> String {
    build_instructions_with_client(crp_mode, "")
}

pub fn build_instructions_with_client(crp_mode: CrpMode, client_name: &str) -> String {
    if is_claude_code_client(client_name) {
        return build_claude_code_instructions();
    }
    build_full_instructions(crp_mode, client_name)
}

fn is_claude_code_client(client_name: &str) -> bool {
    let lower = client_name.to_lowercase();
    lower.contains("claude") && !lower.contains("cursor")
}

fn build_claude_code_instructions() -> String {
    let instr = "\
ALWAYS use nebu-ctx MCP tools instead of native equivalents.

Tool mapping (MANDATORY):
• Read/cat/head/tail -> ctx_read(target=file|files|symbol|outline|archive, ...)
• Shell/bash -> ctx_shell(command)
• Grep/rg/semantic search -> ctx_search(mode=regex|semantic, ...)
• ls/find -> ctx_tree(path, depth)
• Edit/StrReplace/Write/Delete/Glob -> native tools

ctx_read targets: file|files|symbol|outline|archive.
ctx_read modes: auto|full|map|signatures|diff|task|reference|aggressive|entropy|lines:N-M
Auto-selects mode. Re-reads ~13 tok. File refs F1,F2.. persist.
Cached? fresh=true or lines:N-M.

Use ctx(domain=memory|context|graph|analytics|agents|inspect, action=...).
Memory/state: ctx(domain=memory, action=recall|store|task|finding|decision|save|load|status|wakeup|consolidate).
Graph/analysis: ctx(domain=graph, action=related|symbol|impact|architecture|callers|callees|diagram|build|status).
Analytics: ctx(domain=analytics, action=report|cost|heatmap|stats|feedback|wrapped|benchmark|analyze|discover|metrics).
ctx_shell raw=true for uncompressed.

CEP: 1.ACT FIRST 2.DELTA ONLY 3.STRUCTURED(+/-/~) 4.ONE LINE 5.QUALITY

Prefer: ctx_read>Read | ctx_shell>Shell | ctx_search>Grep | ctx_tree>ls
Edit/write/delete: native tools.
Never echo tool output. Never narrate. Show only changed code.
Full instructions at ~/.claude/CLAUDE.md (imports rules/nebu-ctx.md)";

    debug_assert!(
        instr.len() <= CLAUDE_CODE_INSTRUCTION_CAP,
        "Claude Code instructions exceed {CLAUDE_CODE_INSTRUCTION_CAP} chars: {} chars",
        instr.len()
    );
    instr.to_string()
}

fn build_full_instructions(crp_mode: CrpMode, client_name: &str) -> String {
    let profile = crate::core::litm::LitmProfile::from_client_name(client_name);
    let loaded_session = crate::core::session::SessionState::load_latest();

    let session_block = match loaded_session {
        Some(ref session) => {
            let positioned = crate::core::litm::position_optimize(session);
            let resume = if session.stats.total_tool_calls > 0 {
                format!("\n{}", session.build_resume_block())
            } else {
                String::new()
            };
            format!(
                "\n\n--- ACTIVE SESSION (LITM P1: begin position, profile: {}) ---\n{}{resume}\n---\n",
                profile.name, positioned.begin_block
            )
        }
        None => String::new(),
    };

    // Reuse loaded session instead of loading again (prevents race + saves I/O)
    let project_root_for_blocks = loaded_session
        .as_ref()
        .and_then(|s| s.project_root.clone())
        .or_else(|| {
            std::env::current_dir()
                .ok()
                .map(|p| p.to_string_lossy().to_string())
        });

    let knowledge_block = match &project_root_for_blocks {
        Some(root) => {
            let knowledge = crate::core::knowledge::ProjectKnowledge::load(root);
            match knowledge {
                Some(k) if !k.facts.is_empty() => {
                    let aaak = k.format_aaak();
                    if aaak.is_empty() {
                        String::new()
                    } else {
                        format!("\n--- PROJECT MEMORY (AAAK) ---\n{}\n---\n", aaak.trim())
                    }
                }
                _ => String::new(),
            }
        }
        None => String::new(),
    };

    let gotcha_block = match &project_root_for_blocks {
        Some(root) => {
            let store = crate::core::bug_memory::BugMemoryStore::load(root);
            let block = store.format_injection_block();
            if block.is_empty() {
                String::new()
            } else {
                format!("\n{block}\n")
            }
        }
        None => String::new(),
    };

    let base = format!("\
CRITICAL: ALWAYS use the public nebu-ctx MCP surface instead of native equivalents.\n\
\n\
Public MCP surface is fixed to 5 tools: ctx_read, ctx_search, ctx_tree, ctx_shell, ctx.\n\
\n\
MANDATORY tool mapping:\n\
• Read/cat/head/tail -> ctx_read(target=file|files|symbol|outline|archive, ...)\n\
• Shell/bash -> ctx_shell(command)\n\
• Grep/rg/semantic search -> ctx_search(mode=regex|semantic, ...)\n\
• ls/find -> ctx_tree(path, depth)\n\
• Edit/StrReplace/Write/Delete/Glob -> use native tools\n\
\n\
File mutation stays on native Edit/StrReplace/Write/Delete tools.\n\
\n\
ctx_read targets: file|files|symbol|outline|archive.\n\
ctx_read modes: auto|full|map|signatures|diff|task|reference|aggressive|entropy|lines:N-M. Auto-selects. Re-reads ~13 tok. Fn refs F1,F2.. persist.\n\
Cached? Use fresh=true, start_line=N, or lines:N-M.\n\
\n\
Use ctx(domain=memory|context|graph|analytics|agents|inspect, action=...) for higher-level workflows.\n\
Examples: ctx(domain=memory, action=recall, query=...) | ctx(domain=context, action=overview, task=...) | ctx(domain=graph, action=impact, path=...) | ctx(domain=agents, action=handoff, ...).\n\
ctx_shell raw=true for uncompressed output.\n\
\n\
CEP v1: 1.ACT FIRST 2.DELTA ONLY (Fn refs) 3.STRUCTURED (+/-/~) 4.ONE LINE PER ACTION 5.QUALITY ANCHOR\n\
\n\
{decoder_block}\n\
\n\
{session_block}\
{knowledge_block}\
{gotcha_block}\
\n\
--- TOOL PREFERENCE (LITM-END) ---\n\
Prefer: ctx_read over Read | ctx_shell over Shell | ctx_search over Grep | ctx_tree over ls\n\
Edit files: native Edit/StrReplace/Write/Delete tools.\n\
Write, Delete, Glob -> use normally.",
        decoder_block = crate::core::protocol::instruction_decoder_block()
    );

    let intelligence_block = build_intelligence_block();
    let terse_block = build_terse_agent_block(&crp_mode);

    let base = base;
    match crp_mode {
        CrpMode::Off => format!("{base}\n\n{terse_block}{intelligence_block}"),
        CrpMode::Compact => {
            format!(
                "{base}\n\n\
CRP MODE: compact\n\
Omit filler. Abbreviate: fn,cfg,impl,deps,req,res,ctx,err,ret,arg,val,ty,mod.\n\
Diff lines (+/-) only. TARGET: <=200 tok. Trust tool outputs.\n\n\
{terse_block}{intelligence_block}"
            )
        }
        CrpMode::Tdd => {
            format!(
                "{base}\n\n\
CRP MODE: tdd\n\
Max density. Every token carries meaning. Fn refs only, diff lines (+/-) only.\n\
Abbreviate: fn,cfg,impl,deps,req,res,ctx,err,ret,arg,val,ty,mod.\n\
+F1:42 param(timeout:Duration) | -F1:10-15 | ~F1:42 old->new\n\
BUDGET: <=150 tok. ZERO NARRATION. Trust tool outputs.\n\n\
{terse_block}{intelligence_block}"
            )
        }
    }
}

pub fn claude_code_instructions() -> String {
    build_claude_code_instructions()
}

pub fn full_instructions_for_rules_file(crp_mode: CrpMode) -> String {
    build_full_instructions(crp_mode, "")
}

fn build_terse_agent_block(crp_mode: &CrpMode) -> String {
    use crate::core::config::{Config, TerseAgent};
    let cfg = Config::load();
    let level = TerseAgent::effective(&cfg.terse_agent);
    if !level.is_active() {
        return String::new();
    }
    // CRP Tdd already enforces extreme density — only Ultra adds value on top
    if matches!(crp_mode, CrpMode::Tdd) && !matches!(level, TerseAgent::Ultra) {
        return String::new();
    }
    let text = match level {
        TerseAgent::Off => return String::new(),
        TerseAgent::Lite => {
            "\
OUTPUT STYLE: Prefer concise responses. Skip narration, explain only when asked.\n\
Use bullet points over paragraphs. Code > words. Diff > full file."
        }
        TerseAgent::Full => {
            "\
OUTPUT STYLE: Maximum density. Every token carries meaning.\n\
Code changes: diff only (+/-), no full blocks. Explanations: 1 sentence max unless asked.\n\
Lists: no filler words. Never repeat what the user said. Never explain what you're about to do."
        }
        TerseAgent::Ultra => {
            "\
OUTPUT STYLE: Ultra-terse. Expert pair programmer mode.\n\
Skip: greetings, transitions, summaries, \"I'll\", \"Let me\", \"Here's\".\n\
Max 2 sentences per explanation. Code speaks. Act, don't narrate. When uncertain: ask 1 question."
        }
    };
    format!("{text}\n\n")
}

fn build_intelligence_block() -> String {
    "\
OUTPUT EFFICIENCY:\n\
• Never echo tool output code. Never add narration comments. Show only changed code.\n\
• [TASK:type] and SCOPE hints included. Architecture=thorough, generate=code."
        .to_string()
}
