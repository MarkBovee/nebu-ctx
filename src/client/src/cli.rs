use crate::config;
use crate::git_context;
use crate::server_client::ServerClient;
use anyhow::{anyhow, bail, Context, Result};
use serde_json::{json, Map, Number, Value};

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
        "manifest" => output_json(ServerClient::load()?.manifest()?),
        "server" => handle_server_command(&command_args[1..]),
        "tools" => handle_tools_command(&command_args[1..]),
        tool_name if tool_name.starts_with("ctx_") => handle_tool_call(tool_name, &command_args[1..]),
        other => bail!("Unknown command `{other}`. Run `nebu-ctx-client help`."),
    }
}

/// Handles `server ...` subcommands.
fn handle_server_command(command_args: &[String]) -> Result<()> {
    let subcommand = command_args.first().map(String::as_str).unwrap_or("status");
    match subcommand {
        "connect" => connect_server(&command_args[1..]),
        "status" => show_server_status(),
        "bind" => bind_current_workspace(),
        "disconnect" => disconnect_server(),
        other => bail!("Unknown server subcommand `{other}`."),
    }
}

/// Handles `tools ...` subcommands.
fn handle_tools_command(command_args: &[String]) -> Result<()> {
    let subcommand = command_args.first().map(String::as_str).unwrap_or("list");
    match subcommand {
        "list" => output_json(serde_json::to_value(ServerClient::load()?.list_tools()?)?),
        "call" => {
            let tool_name = command_args.get(1).ok_or_else(|| anyhow!("Usage: nebu-ctx-client tools call <tool-name> [key=value ...]"))?;
            handle_tool_call(tool_name, &command_args[2..])
        }
        other => bail!("Unknown tools subcommand `{other}`."),
    }
}

/// Persists a validated server connection.
fn connect_server(command_args: &[String]) -> Result<()> {
    let endpoint = option_value(command_args, &["--endpoint", "-e"])
        .or_else(|| config::load_connection().ok().flatten().map(|connection| connection.endpoint))
        .ok_or_else(|| anyhow!("Server endpoint is required. Pass --endpoint <url>."))?;
    let token = match option_value(command_args, &["--token", "-t"]) {
        Some(value) => value,
        None => rpassword::prompt_password("Token: ").context("failed to read token from terminal")?,
    };

    let connection = config::save_connection(&endpoint, &token)?;
    let client = ServerClient::new(connection.clone());
    let health = client.health()?;
    output_json(json!({
        "connected": true,
        "endpoint": connection.endpoint,
        "health": health,
    }))
}

/// Shows the saved connection and current server health.
fn show_server_status() -> Result<()> {
    let client = ServerClient::load()?;
    let health = client.health()?;
    output_json(json!({
        "saved": true,
        "endpoint": client.endpoint(),
        "health": health,
    }))
}

/// Resolves the current repository against the server project registry.
fn bind_current_workspace() -> Result<()> {
    let client = ServerClient::load()?;
    let repository_context = git_context::discover_repository_context(&std::env::current_dir().context("failed to read current directory")?);
    output_json(serde_json::to_value(client.resolve_project(&repository_context)?)?)
}

/// Removes the saved server connection.
fn disconnect_server() -> Result<()> {
    config::clear_connection()?;
    output_json(json!({ "disconnected": true }))
}

/// Calls a server tool using the current repository context.
fn handle_tool_call(tool_name: &str, command_args: &[String]) -> Result<()> {
    let client = ServerClient::load()?;
    let repository_context = git_context::discover_repository_context(&std::env::current_dir().context("failed to read current directory")?);
    let arguments = parse_tool_arguments(command_args)?;
    output_json(client.call_tool(tool_name, arguments, &repository_context)?)
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
        "nebu-ctx-client\n\nCommands:\n  server connect --endpoint <url> [--token <token>]\n  server status\n  server bind\n  server disconnect\n  manifest\n  tools list\n  tools call <tool-name> [key=value ...]\n  ctx_* [key=value ...]"
    );
}

#[cfg(test)]
mod tests {
    use super::{parse_tool_arguments, parse_value};
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
}