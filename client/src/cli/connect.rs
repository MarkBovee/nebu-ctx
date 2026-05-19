use crate::models::ServerConnection;
use crate::server_client::ServerClient;
use crate::{config, core};
use anyhow::{Context, Result};
use serde_json::json;
use std::io::{self, Write};

pub fn cmd_connect(args: &[String]) {
    if super::has_flag(args, &["--help", "-h", "help"]) {
        println!("Usage: nebu-ctx connect [--endpoint <url>] [--token <token>]");
        return;
    }
    if let Err(error) = connect_server(args) {
        eprintln!("{error}");
        std::process::exit(1);
    }
}

pub fn cmd_disconnect() {
    if let Err(error) = disconnect_server() {
        eprintln!("{error}");
        std::process::exit(1);
    }
}

fn connect_server(command_args: &[String]) -> Result<()> {
    let saved_connection = config::load_connection().ok().flatten();
    let endpoint = match super::option_value(command_args, &["--endpoint", "-e", "--url"]) {
        Some(value) => value,
        None => match saved_connection.as_ref() {
            Some(connection) => connection.endpoint.clone(),
            None => prompt_required_value("Server URL", None)?,
        },
    };
    let token = match super::option_value(command_args, &["--token", "-t"]) {
        Some(value) => value,
        None => prompt_required_secret("Server token")?,
    };

    let (connection, client) = validate_and_save_connection(&endpoint, &token)?;
    let health = client.health()?;
    output_json(json!({
        "connected": true,
        "endpoint": connection.endpoint,
        "health": health,
    }))
}

fn disconnect_server() -> Result<()> {
    config::clear_connection()?;
    output_json(json!({ "disconnected": true }))
}

fn validate_and_save_connection(
    endpoint: &str,
    token: &str,
) -> Result<(ServerConnection, ServerClient)> {
    let connection = ServerConnection {
        endpoint: config::normalize_server_endpoint(endpoint),
        token: token.trim().to_string(),
    };
    let client = ServerClient::new(connection.clone());
    client.health()?;
    let saved_connection = config::save_connection(&connection.endpoint, &connection.token)?;
    Ok((saved_connection, client))
}

fn prompt_required_value(label: &str, default_value: Option<&str>) -> Result<String> {
    loop {
        print!("{label}");
        if let Some(default_value) = default_value {
            print!(" [{default_value}]");
        }
        print!(": ");
        io::stdout().flush().context("failed to flush prompt")?;

        let mut input = String::new();
        io::stdin()
            .read_line(&mut input)
            .context("failed to read terminal input")?;
        let trimmed = input.trim();
        if !trimmed.is_empty() {
            return Ok(trimmed.to_string());
        }

        if let Some(default_value) = default_value {
            return Ok(default_value.to_string());
        }
    }
}

fn prompt_required_secret(label: &str) -> Result<String> {
    loop {
        let value = rpassword::prompt_password(format!("{label}: "))
            .context("failed to read token from terminal")?;
        if !value.trim().is_empty() {
            return Ok(value);
        }
    }
}

fn output_json(value: serde_json::Value) -> Result<()> {
    println!("{}", serde_json::to_string_pretty(&value)?);
    Ok(())
}

pub fn cmd_gotchas(args: &[String]) {
    let action = args.first().map(|value| value.as_str()).unwrap_or("list");
    let project_root = std::env::current_dir()
        .map(|path| path.to_string_lossy().to_string())
        .unwrap_or_else(|_| ".".to_string());

    match action {
        "list" | "ls" => {
            let store = core::gotcha_tracker::GotchaStore::load(&project_root);
            println!("{}", store.format_list());
        }
        "clear" => {
            let mut store = core::gotcha_tracker::GotchaStore::load(&project_root);
            let count = store.gotchas.len();
            store.clear();
            let _ = store.save(&project_root);
            println!("Cleared {count} gotchas.");
        }
        "export" => {
            let store = core::gotcha_tracker::GotchaStore::load(&project_root);
            match serde_json::to_string_pretty(&store.gotchas) {
                Ok(json) => println!("{json}"),
                Err(error) => eprintln!("Export failed: {error}"),
            }
        }
        "stats" => {
            let store = core::gotcha_tracker::GotchaStore::load(&project_root);
            println!("Bug Memory Stats:");
            println!("  Active gotchas:      {}", store.gotchas.len());
            println!(
                "  Errors detected:     {}",
                store.stats.total_errors_detected
            );
            println!(
                "  Fixes correlated:    {}",
                store.stats.total_fixes_correlated
            );
            println!("  Bugs prevented:      {}", store.stats.total_prevented);
            println!("  Promoted to knowledge: {}", store.stats.gotchas_promoted);
            println!("  Decayed/archived:    {}", store.stats.gotchas_decayed);
            println!("  Session logs:        {}", store.error_log.len());
        }
        _ => {
            println!("Usage: nebu-ctx gotchas [list|clear|export|stats]");
        }
    }
}

pub fn cmd_buddy(args: &[String]) {
    let cfg = core::config::Config::load();
    if !cfg.buddy_enabled {
        println!("Buddy is disabled. Enable with: nebu-ctx config buddy_enabled true");
        return;
    }

    let action = args.first().map(|value| value.as_str()).unwrap_or("show");
    let buddy = core::buddy::BuddyState::compute();
    let theme = core::theme::load_theme(&cfg.theme);

    match action {
        "show" | "status" | "stats" => {
            println!("{}", core::buddy::format_buddy_full(&buddy, &theme));
        }
        "ascii" => {
            for line in &buddy.ascii_art {
                println!("  {line}");
            }
        }
        "json" => match serde_json::to_string_pretty(&buddy) {
            Ok(json) => println!("{json}"),
            Err(error) => eprintln!("JSON error: {error}"),
        },
        _ => {
            println!("Usage: nebu-ctx buddy [show|stats|ascii|json]");
        }
    }
}
