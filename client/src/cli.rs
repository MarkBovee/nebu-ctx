use crate::config;
use crate::git_context;
use crate::local_tools;
use crate::models::{ServerConnection, ToolDefinition, ToolListResponse};
use crate::server_client::ServerClient;
use anyhow::{anyhow, bail, Context, Result};
use serde_json::{json, Map, Number, Value};
use std::io::{self, IsTerminal, Write};

/// Runs the thin client command-line interface.
pub fn run(arguments: impl IntoIterator<Item = String>) -> Result<()> {
    let args: Vec<String> = arguments.into_iter().collect();
    let command_args = &args[1..];

    if command_args.is_empty() {
        print_usage();
        return Ok(());
    }

    match command_args[0].as_str() {
        "help" | "--help" | "-h" => {
            print_usage();
            Ok(())
        }
        "manifest" => output_json(build_manifest()?),
        "server" => handle_server_command(&command_args[1..]),
        "tools" => handle_tools_command(&command_args[1..]),
        tool_name if tool_name.starts_with("ctx_") => handle_tool_call(tool_name, &command_args[1..]),
        other => bail!("Unknown command `{other}`. Run `nebu-ctx help`."),
    }
}

/// Handles `server ...` subcommands.
fn handle_server_command(command_args: &[String]) -> Result<()> {
    let subcommand = command_args.first().map(String::as_str).unwrap_or("status");
    match subcommand {
        "connect" => connect_server(&command_args[1..]),
        "status" => show_server_status(),
        "bind" => bind_current_project(),
        "disconnect" => disconnect_server(),
        other => bail!("Unknown server subcommand `{other}`."),
    }
}

/// Handles `tools ...` subcommands.
fn handle_tools_command(command_args: &[String]) -> Result<()> {
    let subcommand = command_args.first().map(String::as_str).unwrap_or("list");
    match subcommand {
        "list" => output_json(serde_json::to_value(list_tools()?)?),
        "call" => {
            let tool_name = command_args.get(1).ok_or_else(|| anyhow!("Usage: nebu-ctx-client tools call <tool-name> [key=value ...]"))?;
            handle_tool_call(tool_name, &command_args[2..])
        }
        other => bail!("Unknown tools subcommand `{other}`."),
    }
}

/// Persists a validated server connection.
fn connect_server(command_args: &[String]) -> Result<()> {
    if has_help_flag(command_args) {
        println!("Usage: nebu-ctx server connect [--endpoint <url>] [--token <token>]");
        return Ok(());
    }

    let saved_connection = config::load_connection().ok().flatten();
    let endpoint = match option_value(command_args, &["--endpoint", "-e"]) {
        Some(value) => value,
        None => match saved_connection.as_ref() {
            Some(connection) => connection.endpoint.clone(),
            None => prompt_required_value("Server URL", None)?,
        },
    };
    let token = match option_value(command_args, &["--token", "-t"]) {
        Some(value) => value,
        None => prompt_required_secret("Auth token")?,
    };

    let (connection, client) = validate_and_save_connection(&endpoint, &token)?;
    let health = client.health()?;
    output_json(json!({
        "connected": true,
        "endpoint": connection.endpoint,
        "health": health,
    }))
}

/// Shows the saved connection and current server health.
fn show_server_status() -> Result<()> {
    let client = load_or_prompt_server_client()?;
    let health = client.health()?;
    output_json(json!({
        "saved": true,
        "endpoint": client.endpoint(),
        "health": health,
    }))
}

/// Resolves the current project against the server project registry.
fn bind_current_project() -> Result<()> {
    let client = load_or_prompt_server_client()?;
    let project_context = git_context::discover_project_context(&std::env::current_dir().context("failed to read current directory")?);
    output_json(serde_json::to_value(client.resolve_project(&project_context)?)?)
}

/// Removes the saved server connection.
fn disconnect_server() -> Result<()> {
    config::clear_connection()?;
    output_json(json!({ "disconnected": true }))
}

/// Calls a local or remote tool using the current project context.
fn handle_tool_call(tool_name: &str, command_args: &[String]) -> Result<()> {
    let project_context = git_context::discover_project_context(&std::env::current_dir().context("failed to read current directory")?);
    let arguments = parse_tool_arguments(command_args)?;
    if local_tools::is_local_tool(tool_name) {
        return output_json(local_tools::execute(tool_name, arguments, &project_context)?);
    }

    let client = load_or_prompt_server_client()?;
    output_json(client.call_tool(tool_name, arguments, &project_context)?)
}

/// Returns the merged tool catalog that the hybrid client can execute.
fn list_tools() -> Result<ToolListResponse> {
    let mut tools = local_tools::tool_definitions();
    if let Ok(client) = load_or_prompt_server_client() {
        let remote = client.list_tools()?;
        merge_tool_definitions(&mut tools, remote.tools);
    }

    tools.sort_by(|left, right| left.name.cmp(&right.name));
    let total = tools.len();
    Ok(ToolListResponse { tools, total })
}

/// Builds a hybrid manifest that includes local client-side and remote server-side tools.
fn build_manifest() -> Result<Value> {
    let local_tools = local_tools::tool_definitions();
    if let Ok(client) = load_or_prompt_server_client() {
        let mut manifest = client.manifest()?;
        let manifest_tools = manifest
            .get_mut("tools")
            .and_then(Value::as_array_mut)
            .ok_or_else(|| anyhow!("Server manifest did not include a tools array."))?;

        for local_tool in local_tools {
            if !manifest_tools.iter().any(|tool| tool.get("name").and_then(Value::as_str) == Some(local_tool.name.as_str())) {
                manifest_tools.push(serde_json::to_value(local_tool)?);
            }
        }

        if let Some(manifest_object) = manifest.as_object_mut() {
            manifest_object.insert("client_mode".to_string(), json!("hybrid"));
        }

        return Ok(manifest);
    }

    Ok(json!({
        "name": "nebu-ctx",
        "version": env!("CARGO_PKG_VERSION"),
        "client_mode": "local-only",
        "project_mode": "project-first",
        "tools": local_tools,
    }))
}

/// Loads the saved server connection or interactively captures it when possible.
fn load_or_prompt_server_client() -> Result<ServerClient> {
    if let Ok(client) = ServerClient::load() {
        return Ok(client);
    }

    if !io::stdin().is_terminal() {
        bail!("No server connection saved. Run `nebu-ctx server connect --endpoint <url> --token <token>`." );
    }

    let endpoint = prompt_required_value("Server URL", None)?;
    let token = prompt_required_secret("Auth token")?;
    let (_, client) = validate_and_save_connection(&endpoint, &token)?;
    Ok(client)
}

/// Validates a server connection before persisting it to disk.
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

/// Reads a required plain-text value from the terminal.
fn prompt_required_value(label: &str, default_value: Option<&str>) -> Result<String> {
    loop {
        print!("{label}");
        if let Some(default_value) = default_value {
            print!(" [{default_value}]");
        }
        print!(": ");
        io::stdout().flush().context("failed to flush prompt")?;

        let mut input = String::new();
        io::stdin().read_line(&mut input).context("failed to read terminal input")?;
        let trimmed = input.trim();
        if !trimmed.is_empty() {
            return Ok(trimmed.to_string());
        }

        if let Some(default_value) = default_value {
            return Ok(default_value.to_string());
        }
    }
}

/// Reads a required secret value from the terminal without echoing it.
fn prompt_required_secret(label: &str) -> Result<String> {
    loop {
        let value = rpassword::prompt_password(format!("{label}: ")).context("failed to read token from terminal")?;
        if !value.trim().is_empty() {
            return Ok(value);
        }
    }
}

/// Returns true when the command args request help output.
fn has_help_flag(command_args: &[String]) -> bool {
    command_args.iter().any(|argument| argument == "--help" || argument == "-h")
}

/// Adds remote tool definitions without duplicating local client-owned tools.
fn merge_tool_definitions(target: &mut Vec<ToolDefinition>, incoming: Vec<ToolDefinition>) {
    for tool in incoming {
        if !target.iter().any(|existing| existing.name == tool.name) {
            target.push(tool);
        }
    }
}

/// Extracts a named option value from raw command arguments.
fn option_value(command_args: &[String], option_names: &[&str]) -> Option<String> {
    let mut index = 0;
    while index < command_args.len() {
        if option_names.contains(&command_args[index].as_str()) {
            return command_args.get(index + 1).cloned();
        }

        index += 1;
    }

    None
}

/// Parses generic `key=value` command arguments into a JSON object.
fn parse_tool_arguments(command_args: &[String]) -> Result<Map<String, Value>> {
    if let Some(raw_json) = option_value(command_args, &["--json"]) {
        let value: Value = serde_json::from_str(&raw_json).context("failed to parse --json payload")?;
        let object = value.as_object().cloned().ok_or_else(|| anyhow!("--json payload must be a JSON object"))?;
        return Ok(object);
    }

    let mut arguments = Map::new();
    for argument in command_args {
        if argument.starts_with('-') {
            continue;
        }

        let (key, raw_value) = argument
            .split_once('=')
            .ok_or_else(|| anyhow!("Tool arguments must use key=value format. Invalid argument: {argument}"))?;
        arguments.insert(key.to_string(), parse_value(raw_value)?);
    }

    Ok(arguments)
}

/// Parses a scalar CLI value into JSON using a small set of intuitive coercions.
fn parse_value(raw_value: &str) -> Result<Value> {
    if raw_value.eq_ignore_ascii_case("null") {
        return Ok(Value::Null);
    }

    if raw_value.eq_ignore_ascii_case("true") {
        return Ok(Value::Bool(true));
    }

    if raw_value.eq_ignore_ascii_case("false") {
        return Ok(Value::Bool(false));
    }

    if let Ok(parsed) = raw_value.parse::<i64>() {
        return Ok(Value::Number(Number::from(parsed)));
    }

    if let Ok(parsed) = raw_value.parse::<f64>() {
        if let Some(number) = Number::from_f64(parsed) {
            return Ok(Value::Number(number));
        }
    }

    if raw_value.starts_with('{') || raw_value.starts_with('[') {
        return serde_json::from_str(raw_value).context("failed to parse inline JSON argument");
    }

    Ok(Value::String(raw_value.to_string()))
}

/// Writes a JSON value to stdout with stable indentation.
fn output_json(value: Value) -> Result<()> {
    println!("{}", serde_json::to_string_pretty(&value)?);
    Ok(())
}

/// Prints the thin client usage summary.
fn print_usage() {
    println!(
        "nebu-ctx\n\nCommands:\n  server connect [--endpoint <url>] [--token <token>]\n  server status\n  server bind\n  server disconnect\n  manifest\n  tools list\n  tools call <tool-name> [key=value ...]\n  ctx_* [key=value ...]"
    );
}

#[cfg(test)]
mod tests {
    use super::{merge_tool_definitions, parse_tool_arguments, parse_value};
    use crate::models::ToolDefinition;
    use serde_json::{json, Value};

    #[test]
    fn parse_tool_arguments_supports_key_value_pairs() {
        let arguments = parse_tool_arguments(&["action=status".to_string(), "count=2".to_string()]).unwrap();
        assert_eq!(arguments.get("action"), Some(&Value::String("status".to_string())));
        assert_eq!(arguments.get("count"), Some(&json!(2)));
    }

    #[test]
    fn parse_value_supports_inline_json() {
        assert_eq!(parse_value("true").unwrap(), Value::Bool(true));
        assert_eq!(parse_value("3.5").unwrap(), json!(3.5));
        assert_eq!(parse_value("{\"k\":\"v\"}").unwrap(), json!({ "k": "v" }));
    }

    #[test]
    fn merge_tool_definitions_skips_duplicates() {
        let mut tools = vec![ToolDefinition {
            name: "ctx_read".to_string(),
            description: "local".to_string(),
            input_schema: json!({}),
        }];

        merge_tool_definitions(
            &mut tools,
            vec![
                ToolDefinition {
                    name: "ctx_read".to_string(),
                    description: "remote".to_string(),
                    input_schema: json!({}),
                },
                ToolDefinition {
                    name: "ctx_brain".to_string(),
                    description: "remote".to_string(),
                    input_schema: json!({}),
                },
            ],
        );

        assert_eq!(tools.len(), 2);
        assert!(tools.iter().any(|tool| tool.name == "ctx_brain"));
    }
}