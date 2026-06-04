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
    let instr = crate::public_guidance::claude_code_instructions();

    debug_assert!(
        instr.len() <= CLAUDE_CODE_INSTRUCTION_CAP,
        "Claude Code instructions exceed {CLAUDE_CODE_INSTRUCTION_CAP} chars: {} chars",
        instr.len()
    );
    instr
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

    let base = format!(
        "{}\n\nCEP v1: 1.ACT FIRST 2.DELTA ONLY (Fn refs) 3.STRUCTURED (+/-/~) 4.ONE LINE PER ACTION 5.QUALITY ANCHOR\n\n{}\n\n{session_block}{knowledge_block}{gotcha_block}\n--- TOOL PREFERENCE (LITM-END) ---\nPrefer: ctx_read over Read | ctx_search over Grep | ctx_tree over ls\nEdit files: native Edit/StrReplace/Write/Delete tools.\nWrite, Delete, Glob -> use normally.",
        crate::public_guidance::full_instruction_policy_block(),
        crate::core::protocol::instruction_decoder_block(),
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
