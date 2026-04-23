use crate::cloud_client::ServerClient;
use crate::models::ServerConnection;
use crate::{config, core, git_context};
use anyhow::{bail, Context, Result};
use serde_json::json;
use std::io::{self, IsTerminal, Write};

fn removed_cloud_command(command: &str) -> ! {
    eprintln!(
        "`nebu-ctx {command}` is no longer available in the Rust client. Use `nebu-ctx cloud connect --endpoint <url> --token <token>` against your NebuCtx server."
    );
    std::process::exit(1);
}

pub fn cmd_login(_args: &[String]) {
    removed_cloud_command("login");
}

pub fn cmd_forgot_password(_args: &[String]) {
    removed_cloud_command("forgot-password");
}

pub fn cmd_register(_args: &[String]) {
    removed_cloud_command("register");
}

pub fn cmd_sync() {
    removed_cloud_command("sync");
}

pub fn cmd_contribute() {
    removed_cloud_command("contribute");
}

pub fn cmd_cloud(args: &[String]) {
    let action = args.first().map(|value| value.as_str()).unwrap_or("help");

    match action {
        "connect" => {
            if let Err(error) = connect_cloud(&args[1..]) {
                eprintln!("{error}");
                std::process::exit(1);
            }
        }
        "bind" => {
            if let Err(error) = bind_current_project() {
                eprintln!("{error}");
                std::process::exit(1);
            }
        }
        "disconnect" => {
            if let Err(error) = disconnect_cloud() {
                eprintln!("{error}");
                std::process::exit(1);
            }
        }
        "status" => {
            if let Err(error) = show_cloud_status() {
                eprintln!("{error}");
                std::process::exit(1);
            }
        }
        _ => {
            println!("Usage: nebu-ctx cloud <command>");
            println!("  connect     - Save and validate a cloud endpoint + token");
            println!("  status      - Show cloud connection status");
            println!("  bind        - Bind the current checkout to a canonical project");
            println!("  disconnect  - Remove the saved cloud connection");
        }
    }
}

fn connect_cloud(command_args: &[String]) -> Result<()> {
    if has_help_flag(command_args) {
        println!("Usage: nebu-ctx cloud connect [--endpoint <url>] [--token <token>]");
        return Ok(());
    }

    let saved_connection = config::load_connection().ok().flatten();
    let endpoint = match option_value(command_args, &["--endpoint", "-e", "--url"]) {
        Some(value) => value,
        None => match saved_connection.as_ref() {
            Some(connection) => connection.endpoint.clone(),
            None => prompt_required_value("Cloud URL", None)?,
        },
    };
    let token = match option_value(command_args, &["--token", "-t"]) {
        Some(value) => value,
        None => prompt_required_secret("Cloud token")?,
    };

    let (connection, client) = validate_and_save_connection(&endpoint, &token)?;
    let health = client.health()?;
    output_json(json!({
        "connected": true,
        "endpoint": connection.endpoint,
        "health": health,
    }))
}

fn show_cloud_status() -> Result<()> {
    let client = load_or_prompt_cloud_client()?;
    let health = client.health()?;
    output_json(json!({
        "saved": true,
        "endpoint": client.endpoint(),
        "health": health,
    }))
}

fn bind_current_project() -> Result<()> {
    let client = load_or_prompt_cloud_client()?;
    let project_context = git_context::discover_project_context(
        &std::env::current_dir().context("failed to read current directory")?,
    );
    output_json(serde_json::to_value(client.resolve_project(&project_context)?)?)
}

fn disconnect_cloud() -> Result<()> {
    config::clear_connection()?;
    output_json(json!({ "disconnected": true }))
}

fn load_or_prompt_cloud_client() -> Result<ServerClient> {
    if let Ok(client) = ServerClient::load() {
        return Ok(client);
    }

    if !io::stdin().is_terminal() {
        bail!("No cloud connection saved. Run `nebu-ctx cloud connect --endpoint <url> --token <token>`." );
    }

    let endpoint = prompt_required_value("Cloud URL", None)?;
    let token = prompt_required_secret("Cloud token")?;
    let (_, client) = validate_and_save_connection(&endpoint, &token)?;
    Ok(client)
}

fn validate_and_save_connection(endpoint: &str, token: &str) -> Result<(ServerConnection, ServerClient)> {
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

fn option_value(command_args: &[String], flags: &[&str]) -> Option<String> {
    let mut index = 0;
    while index < command_args.len() {
        if flags.contains(&command_args[index].as_str()) {
            return command_args.get(index + 1).cloned();
        }

        index += 1;
    }

    None
}

fn has_help_flag(command_args: &[String]) -> bool {
    command_args
        .iter()
        .any(|argument| matches!(argument.as_str(), "--help" | "-h" | "help"))
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
            println!("  Errors detected:     {}", store.stats.total_errors_detected);
            println!("  Fixes correlated:    {}", store.stats.total_fixes_correlated);
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

pub fn cmd_upgrade() {
    println!("'upgrade' has been renamed to 'update'. Running 'nebu-ctx update' instead.\n");
    core::updater::run(&[]);
}
