use crate::cli::has_flag;
use crate::cli::option_value;
use crate::config;
use crate::server_client::ServerClient;
use anyhow::{Context, Result};
use serde_json::{json, Map, Value};

const USAGE: &str = "Usage:
  nebu-ctx memory list [--type brain|knowledge] [--category <name>] [--source-type <type>]
                       [--since <window>] [--lifecycle-status <status>]
                       [--limit N] [--offset N]
                       [--sort-field <field>] [--sort-direction asc|desc]
                       [--promoted-from-session <id>] [--promoted-from-brain-key <key>]
                       [--json]
  nebu-ctx memory lifecycle <stats|promotions|stale|scoring>
                            [--type brain|knowledge]
                            [--days N] [--limit N]
                            [--category <name>] [--key <key>]
                            [--json]
  nebu-ctx memory export [--type brain|knowledge] [--category <name>] [--since <window>]
                          [--lifecycle-status <status>] [--limit N] [--output <file>]
  nebu-ctx memory import <file> [--type brain|knowledge] [--overwrite]";

pub fn cmd_memory(args: &[String]) {
    if has_flag(args, &["--help", "-h", "help"]) || args.is_empty() {
        println!("{USAGE}");
        return;
    }

    let sub = args[0].as_str();
    match sub {
        "list" => match run_memory_list(&args[1..]) {
            Ok(()) => {}
            Err(e) => {
                eprintln!("nebu-ctx memory list: {e}");
                eprintln!("\n{USAGE}");
                std::process::exit(1);
            }
        },
        "lifecycle" => match run_memory_lifecycle(&args[1..]) {
            Ok(()) => {}
            Err(e) => {
                eprintln!("nebu-ctx memory lifecycle: {e}");
                eprintln!("\n{USAGE}");
                std::process::exit(1);
            }
        },
        "export" => match run_memory_export(&args[1..]) {
            Ok(()) => {}
            Err(e) => {
                eprintln!("nebu-ctx memory export: {e}");
                eprintln!("\n{USAGE}");
                std::process::exit(1);
            }
        },
        "import" => match run_memory_import(&args[1..]) {
            Ok(()) => {}
            Err(e) => {
                eprintln!("nebu-ctx memory import: {e}");
                eprintln!("\n{USAGE}");
                std::process::exit(1);
            }
        },
        _ => {
            eprintln!("Unknown memory subcommand '{sub}'.");
            eprintln!("\n{USAGE}");
            std::process::exit(1);
        }
    }
}

fn run_memory_list(args: &[String]) -> Result<()> {
    let connection = config::load_connection()?.ok_or_else(|| {
        anyhow::anyhow!(
            "nebu-ctx memory list requires a configured host.\n\
Run: nebu-ctx connect --endpoint <url> --token <token>"
        )
    })?;
    let client = ServerClient::new(connection);

    let memory_type = option_value(args, &["--type", "-t"]).unwrap_or_else(|| "knowledge".to_string());
    let tool_name = match memory_type.as_str() {
        "brain" => "ctx_brain",
        "knowledge" => "ctx_knowledge",
        other => {
            anyhow::bail!("--type must be 'brain' or 'knowledge', got '{other}'");
        }
    };

    let mut arguments = Map::new();
    arguments.insert("action".into(), Value::String("list".into()));
    if let Some(v) = option_value(args, &["--category", "-c"]) {
        arguments.insert("category".into(), Value::String(v));
    }
    if let Some(v) = option_value(args, &["--source-type"]) {
        arguments.insert("source_type".into(), Value::String(v));
    }
    if let Some(v) = option_value(args, &["--lifecycle-status"]) {
        arguments.insert("lifecycle_status".into(), Value::String(v));
    }
    if let Some(v) = option_value(args, &["--since"]) {
        arguments.insert("since".into(), Value::String(v));
    }
    if let Some(v) = option_value(args, &["--limit", "-n"]) {
        let n: i64 = v
            .parse()
            .with_context(|| format!("invalid --limit value: {v}"))?;
        arguments.insert("limit".into(), json!(n));
    }
    if let Some(v) = option_value(args, &["--offset"]) {
        let n: i64 = v
            .parse()
            .with_context(|| format!("invalid --offset value: {v}"))?;
        arguments.insert("offset".into(), json!(n));
    }
    if let Some(v) = option_value(args, &["--sort-field"]) {
        arguments.insert("sort_field".into(), Value::String(v));
    }
    if let Some(v) = option_value(args, &["--sort-direction", "--sort-dir"]) {
        arguments.insert("sort_direction".into(), Value::String(v));
    }
    if let Some(v) = option_value(args, &["--promoted-from-session"]) {
        arguments.insert("promoted_from_session".into(), Value::String(v));
    }
    if let Some(v) = option_value(args, &["--promoted-from-brain-key"]) {
        arguments.insert("promoted_from_brain_key".into(), Value::String(v));
    }

    let project_context = crate::git_context::discover_project_context(
        &std::env::current_dir().unwrap_or_else(|_| std::path::PathBuf::from(".")),
    );

    let result = client
        .call_tool(tool_name, arguments, &project_context)
        .with_context(|| format!("calling {tool_name} list"))?;

    if has_flag(args, &["--json"]) {
        println!(
            "{}",
            serde_json::to_string_pretty(&result).unwrap_or_else(|_| result.to_string())
        );
        return Ok(());
    }

    print_memory_list_human(&result);
    Ok(())
}

fn print_memory_list_human(result: &Value) {
    let memories = result
        .get("memories")
        .and_then(|v| v.as_array())
        .cloned()
        .unwrap_or_default();
    let total = result
        .get("total")
        .and_then(|v| v.as_i64())
        .unwrap_or(memories.len() as i64);
    let count = result
        .get("count")
        .and_then(|v| v.as_i64())
        .unwrap_or(memories.len() as i64);
    println!("{count} of {total} memories:");
    for (i, m) in memories.iter().enumerate() {
        let key = m.get("key").and_then(|v| v.as_str()).unwrap_or("?");
        let category = m.get("category").and_then(|v| v.as_str()).unwrap_or("-");
        let value = m
            .get("value")
            .and_then(|v| v.as_str())
            .unwrap_or("");
        let confidence = m
            .get("confidence")
            .and_then(|v| v.as_f64())
            .unwrap_or(0.0);
        let status = m
            .get("lifecycle_status")
            .and_then(|v| v.as_str())
            .unwrap_or("?");
        println!(
            "  {i:>3}. [{category:>14}] {key}\n       conf={confidence:.2} status={status}\n       {value}"
        );
    }
    if memories.is_empty() {
        println!("  (no memories matched)");
    }
}

fn run_memory_lifecycle(args: &[String]) -> Result<()> {
    if args.is_empty() {
        anyhow::bail!("missing lifecycle sub-action: stats|promotions|stale|scoring");
    }
    let sub = args[0].as_str();
    if !matches!(sub, "stats" | "promotions" | "stale" | "scoring") {
        anyhow::bail!("unknown lifecycle sub-action '{sub}'. Use stats|promotions|stale|scoring");
    }

    let connection = config::load_connection()?.ok_or_else(|| {
        anyhow::anyhow!(
            "nebu-ctx memory lifecycle requires a configured host.\nRun: nebu-ctx connect --endpoint <url> --token <token>"
        )
    })?;
    let client = ServerClient::new(connection);

    let memory_type = option_value(args, &["--type", "-t"]).unwrap_or_else(|| "knowledge".to_string());
    let tool_name = match memory_type.as_str() {
        "brain" => "ctx_brain",
        "knowledge" => "ctx_knowledge",
        other => anyhow::bail!("--type must be 'brain' or 'knowledge', got '{other}'"),
    };

    let mut arguments = Map::new();
    arguments.insert("action".into(), Value::String("lifecycle".into()));
    arguments.insert("lifecycle_subaction".into(), Value::String(sub.into()));
    if let Some(v) = option_value(args, &["--days"]) {
        let n: i64 = v
            .parse()
            .with_context(|| format!("invalid --days value: {v}"))?;
        arguments.insert("lifecycle_days".into(), json!(n));
    }
    if let Some(v) = option_value(args, &["--limit", "-n"]) {
        let n: i64 = v
            .parse()
            .with_context(|| format!("invalid --limit value: {v}"))?;
        arguments.insert("limit".into(), json!(n));
    }
    if let Some(v) = option_value(args, &["--category", "-c"]) {
        arguments.insert("category".into(), Value::String(v));
    }
    if let Some(v) = option_value(args, &["--key", "-k"]) {
        arguments.insert("key".into(), Value::String(v));
    }

    let project_context = crate::git_context::discover_project_context(
        &std::env::current_dir().unwrap_or_else(|_| std::path::PathBuf::from(".")),
    );
    let result = client
        .call_tool(tool_name, arguments, &project_context)
        .with_context(|| format!("calling {tool_name} lifecycle {sub}"))?;

    if has_flag(args, &["--json"]) {
        println!(
            "{}",
            serde_json::to_string_pretty(&result).unwrap_or_else(|_| result.to_string())
        );
        return Ok(());
    }
    println!(
        "{}",
        serde_json::to_string_pretty(&result).unwrap_or_else(|_| result.to_string())
    );
    Ok(())
}

fn run_memory_export(args: &[String]) -> Result<()> {
    let connection = config::load_connection()?.ok_or_else(|| {
        anyhow::anyhow!(
            "nebu-ctx memory export requires a configured host.\n\
Run: nebu-ctx connect --endpoint <url> --token <token>"
        )
    })?;
    let client = ServerClient::new(connection);

    let memory_type = option_value(args, &["--type", "-t"]).unwrap_or_else(|| "knowledge".to_string());
    let tool_name = match memory_type.as_str() {
        "brain" => "ctx_brain",
        "knowledge" => "ctx_knowledge",
        other => anyhow::bail!("--type must be 'brain' or 'knowledge', got '{other}'"),
    };

    let mut arguments = Map::new();
    arguments.insert("action".into(), Value::String("list".into()));
    if let Some(v) = option_value(args, &["--category", "-c"]) {
        arguments.insert("category".into(), Value::String(v));
    }
    if let Some(v) = option_value(args, &["--since"]) {
        arguments.insert("since".into(), Value::String(v));
    }
    if let Some(v) = option_value(args, &["--lifecycle-status"]) {
        arguments.insert("lifecycle_status".into(), Value::String(v));
    }
    if let Some(v) = option_value(args, &["--limit", "-n"]) {
        let n: i64 = v
            .parse()
            .with_context(|| format!("invalid --limit value: {v}"))?;
        arguments.insert("limit".into(), json!(n));
    }

    let project_context = crate::git_context::discover_project_context(
        &std::env::current_dir().unwrap_or_else(|_| std::path::PathBuf::from(".")),
    );

    let result = client
        .call_tool(tool_name, arguments, &project_context)
        .with_context(|| format!("calling {tool_name} export"))?;

    let memories = result
        .get("memories")
        .cloned()
        .unwrap_or(Value::Array(Vec::new()));
    let count = memories.as_array().map(|a| a.len()).unwrap_or(0);

    let mut filters = Map::new();
    if let Some(v) = option_value(args, &["--category", "-c"]) {
        filters.insert("category".into(), Value::String(v));
    }
    if let Some(v) = option_value(args, &["--since"]) {
        filters.insert("since".into(), Value::String(v));
    }
    if let Some(v) = option_value(args, &["--lifecycle-status"]) {
        filters.insert("lifecycle_status".into(), Value::String(v));
    }

    let export = json!({
        "export_info": {
            "timestamp": chrono::Utc::now().to_rfc3339(),
            "version": env!("CARGO_PKG_VERSION"),
            "memory_type": memory_type,
            "filters_applied": filters,
            "memory_counts": {
                memory_type: count,
                "total": count,
            },
        },
        "memories": memories,
        "schema_version": "1",
    });

    let serialized =
        serde_json::to_string_pretty(&export).context("serializing export")?;

    if let Some(path) = option_value(args, &["--output", "-o"]) {
        std::fs::write(&path, &serialized)
            .with_context(|| format!("writing export to {path}"))?;
        println!("Wrote {count} memories to {path}");
    } else {
        println!("{serialized}");
    }
    Ok(())
}

fn run_memory_import(args: &[String]) -> Result<()> {
    if args.is_empty() {
        anyhow::bail!("missing import file path.\nUsage: nebu-ctx memory import <file> [--type brain|knowledge] [--overwrite]");
    }
    let path = &args[0];
    let raw = std::fs::read_to_string(path)
        .with_context(|| format!("reading import file {path}"))?;
    let payload: Value = serde_json::from_str(&raw)
        .with_context(|| format!("parsing import file {path} as JSON"))?;

    let memories = payload
        .get("memories")
        .and_then(|v| v.as_array())
        .ok_or_else(|| anyhow::anyhow!("import file is missing 'memories' array"))?;
    if memories.is_empty() {
        println!("Import file contains no memories; nothing to do.");
        return Ok(());
    }

    let connection = config::load_connection()?.ok_or_else(|| {
        anyhow::anyhow!(
            "nebu-ctx memory import requires a configured host.\n\
Run: nebu-ctx connect --endpoint <url> --token <token>"
        )
    })?;
    let client = ServerClient::new(connection);

    let memory_type = option_value(args, &["--type", "-t"]).unwrap_or_else(|| "knowledge".to_string());
    let tool_name = match memory_type.as_str() {
        "brain" => "ctx_brain",
        "knowledge" => "ctx_knowledge",
        other => anyhow::bail!("--type must be 'brain' or 'knowledge', got '{other}'"),
    };

    let mut arguments = Map::new();
    arguments.insert("action".into(), Value::String("import".into()));
    arguments.insert("import_payload".into(), json!({ "memories": memories }));
    if has_flag(args, &["--overwrite", "-f"]) {
        arguments.insert("overwrite".into(), Value::Bool(true));
    }

    let project_context = crate::git_context::discover_project_context(
        &std::env::current_dir().unwrap_or_else(|_| std::path::PathBuf::from(".")),
    );

    let result = client
        .call_tool(tool_name, arguments, &project_context)
        .with_context(|| format!("calling {tool_name} import"))?;

    let added = result.get("added").and_then(|v| v.as_i64()).unwrap_or(0);
    let updated = result.get("updated").and_then(|v| v.as_i64()).unwrap_or(0);
    let skipped = result.get("skipped").and_then(|v| v.as_i64()).unwrap_or(0);
    println!(
        "Import complete: added={added} updated={updated} skipped={skipped} total={}",
        memories.len()
    );
    Ok(())
}
