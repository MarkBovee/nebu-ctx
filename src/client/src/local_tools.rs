use crate::local_symbols;
use crate::models::{ProjectContext, ToolDefinition};
use anyhow::{anyhow, bail, Context, Result};
use ignore::WalkBuilder;
use regex::Regex;
use serde_json::{json, Map, Value};
use std::fs;
use std::path::{Path, PathBuf};

const MAX_SEARCH_FILE_BYTES: u64 = 512_000;
const DEFAULT_SEARCH_LIMIT: usize = 20;
const DEFAULT_TREE_DEPTH: usize = 3;

pub fn is_local_tool(tool_name: &str) -> bool {
    matches!(tool_name, "ctx_read" | "ctx_multi_read" | "ctx_tree" | "ctx_search" | "ctx_outline" | "ctx_symbol" | "ctx_callers" | "ctx_callees")
}

pub fn tool_definitions() -> Vec<ToolDefinition> {
    vec![
        ToolDefinition {
            name: "ctx_read".to_string(),
            description: "Read a local file from the current project. Supports full, reference, and line-range reads.".to_string(),
            input_schema: json!({
                "type": "object",
                "properties": {
                    "path": { "type": "string" },
                    "mode": { "type": "string" },
                    "start_line": { "type": "integer" },
                    "end_line": { "type": "integer" }
                },
                "required": ["path"]
            }),
        },
        ToolDefinition {
            name: "ctx_multi_read".to_string(),
            description: "Read multiple local files from the current project in one call.".to_string(),
            input_schema: json!({
                "type": "object",
                "properties": {
                    "paths": { "type": "array", "items": { "type": "string" } },
                    "mode": { "type": "string" },
                    "start_line": { "type": "integer" },
                    "end_line": { "type": "integer" }
                },
                "required": ["paths"]
            }),
        },
        ToolDefinition {
            name: "ctx_tree".to_string(),
            description: "List the local project tree while respecting .gitignore by default.".to_string(),
            input_schema: json!({
                "type": "object",
                "properties": {
                    "path": { "type": "string" },
                    "depth": { "type": "integer" },
                    "show_hidden": { "type": "boolean" }
                }
            }),
        },
        ToolDefinition {
            name: "ctx_search".to_string(),
            description: "Regex search over local project files while respecting .gitignore.".to_string(),
            input_schema: json!({
                "type": "object",
                "properties": {
                    "pattern": { "type": "string" },
                    "path": { "type": "string" },
                    "ext": { "type": "string" },
                    "max_results": { "type": "integer" }
                },
                "required": ["pattern"]
            }),
        },
        ToolDefinition {
            name: "ctx_outline".to_string(),
            description: "List local project symbols for a file using lightweight signature extraction.".to_string(),
            input_schema: json!({
                "type": "object",
                "properties": {
                    "path": { "type": "string" },
                    "kind": { "type": "string" }
                },
                "required": ["path"]
            }),
        },
        ToolDefinition {
            name: "ctx_symbol".to_string(),
            description: "Find and read a symbol from the local project by name.".to_string(),
            input_schema: json!({
                "type": "object",
                "properties": {
                    "name": { "type": "string" },
                    "file": { "type": "string" },
                    "kind": { "type": "string" }
                },
                "required": ["name"]
            }),
        },
        ToolDefinition {
            name: "ctx_callers".to_string(),
            description: "Find local project call sites for a symbol.".to_string(),
            input_schema: json!({
                "type": "object",
                "properties": {
                    "symbol": { "type": "string" },
                    "file": { "type": "string" }
                },
                "required": ["symbol"]
            }),
        },
        ToolDefinition {
            name: "ctx_callees".to_string(),
            description: "Find local project callees used inside a symbol body.".to_string(),
            input_schema: json!({
                "type": "object",
                "properties": {
                    "symbol": { "type": "string" },
                    "file": { "type": "string" }
                },
                "required": ["symbol"]
            }),
        },
    ]
}

pub fn execute(tool_name: &str, arguments: Map<String, Value>, project_context: &ProjectContext) -> Result<Value> {
    let project_root = project_root(project_context)?;
    match tool_name {
        "ctx_tree" => execute_tree(&project_root, &arguments),
        "ctx_search" => execute_search(&project_root, &arguments),
        "ctx_read" => execute_read(&project_root, &arguments),
        "ctx_multi_read" => execute_multi_read(&project_root, &arguments),
        "ctx_outline" => local_symbols::execute_outline(&project_root, &arguments),
        "ctx_symbol" => local_symbols::execute_symbol(&project_root, &arguments),
        "ctx_callers" => local_symbols::execute_callers(&project_root, &arguments),
        "ctx_callees" => local_symbols::execute_callees(&project_root, &arguments),
        _ => bail!("Unsupported local tool `{tool_name}`."),
    }
}

fn execute_tree(project_root: &Path, arguments: &Map<String, Value>) -> Result<Value> {
    let project_root = canonical_project_root(project_root)?;
    let target_path = resolve_existing_path(&project_root, get_optional_string(arguments, "path").as_deref().unwrap_or("."))?;
    if !target_path.is_dir() {
        bail!("ctx_tree requires a directory path.");
    }

    let depth = get_optional_u64(arguments, "depth").unwrap_or(DEFAULT_TREE_DEPTH as u64) as usize;
    let show_hidden = get_optional_bool(arguments, "show_hidden").unwrap_or(false);

    let mut entries = Vec::new();
    let walker = WalkBuilder::new(&target_path)
        .hidden(!show_hidden)
        .git_ignore(true)
        .git_global(true)
        .git_exclude(true)
        .max_depth(Some(depth))
        .sort_by_file_name(|left, right| left.cmp(right))
        .build();

    for entry in walker.filter_map(|entry| entry.ok()) {
        if entry.depth() == 0 {
            continue;
        }

        let path = entry.path();
        let display_path = display_path(path, &project_root);
        let is_dir = entry.file_type().is_some_and(|file_type| file_type.is_dir());
        let child_file_count = if is_dir { Some(count_child_files(path)) } else { None };

        entries.push(json!({
            "path": display_path,
            "depth": entry.depth(),
            "kind": if is_dir { "directory" } else { "file" },
            "child_file_count": child_file_count,
        }));
    }

    let tree = entries
        .iter()
        .map(|entry| {
            let depth = entry.get("depth").and_then(Value::as_u64).unwrap_or(1) as usize;
            let path = entry.get("path").and_then(Value::as_str).unwrap_or_default();
            let kind = entry.get("kind").and_then(Value::as_str).unwrap_or("file");
            let indent = "  ".repeat(depth.saturating_sub(1));
            if kind == "directory" {
                let child_count = entry.get("child_file_count").and_then(Value::as_u64).unwrap_or(0);
                format!("{indent}{path}/ ({child_count})")
            } else {
                format!("{indent}{path}")
            }
        })
        .collect::<Vec<_>>()
        .join("\n");

    Ok(json!({
        "local": true,
        "tool": "ctx_tree",
        "root": display_path(&target_path, &project_root),
        "count": entries.len(),
        "tree": tree,
        "entries": entries,
    }))
}

fn execute_search(project_root: &Path, arguments: &Map<String, Value>) -> Result<Value> {
    let project_root = canonical_project_root(project_root)?;
    let pattern = get_required_string(arguments, "pattern")?;
    let regex = Regex::new(&pattern).with_context(|| format!("invalid regex `{pattern}`"))?;
    let path_argument = get_optional_string(arguments, "path").unwrap_or_else(|| ".".to_string());
    let target_path = resolve_existing_path(&project_root, &path_argument)?;
    let ext_filter = get_optional_string(arguments, "ext");
    let max_results = get_optional_u64(arguments, "max_results").unwrap_or(DEFAULT_SEARCH_LIMIT as u64) as usize;

    let mut matches = Vec::new();
    let mut files_searched = 0usize;

    if target_path.is_file() {
        search_file(&project_root, &target_path, &regex, ext_filter.as_deref(), max_results, &mut matches, &mut files_searched)?;
    } else {
        let walker = WalkBuilder::new(&target_path)
            .hidden(true)
            .git_ignore(true)
            .git_global(true)
            .git_exclude(true)
            .build();

        for entry in walker.filter_map(|entry| entry.ok()) {
            if matches.len() >= max_results {
                break;
            }

            if entry.file_type().is_none_or(|file_type| file_type.is_dir()) {
                continue;
            }

            search_file(&project_root, entry.path(), &regex, ext_filter.as_deref(), max_results, &mut matches, &mut files_searched)?;
        }
    }

    Ok(json!({
        "local": true,
        "tool": "ctx_search",
        "pattern": pattern,
        "path": display_path(&target_path, &project_root),
        "files_searched": files_searched,
        "count": matches.len(),
        "matches": matches,
    }))
}

fn execute_read(project_root: &Path, arguments: &Map<String, Value>) -> Result<Value> {
    let project_root = canonical_project_root(project_root)?;
    let requested_path = get_required_string(arguments, "path")?;
    let target_path = resolve_existing_path(&project_root, &requested_path)?;
    if !target_path.is_file() {
        bail!("ctx_read requires a file path.");
    }

    let requested_mode = get_optional_string(arguments, "mode").unwrap_or_else(|| "full".to_string());
    let start_line = get_optional_u64(arguments, "start_line").map(|value| value as usize);
    let end_line = get_optional_u64(arguments, "end_line").map(|value| value as usize);

    let content = fs::read_to_string(&target_path)
        .with_context(|| format!("failed to read {}", target_path.display()))?;
    let all_lines: Vec<&str> = content.lines().collect();
    let line_count = all_lines.len();

    let (mode_used, output) = if start_line.is_some() || end_line.is_some() {
        let start = start_line.unwrap_or(1).max(1);
        let end = end_line.unwrap_or(line_count).max(start);
        let slice = all_lines
            .iter()
            .enumerate()
            .filter(|(index, _)| {
                let line_number = index + 1;
                line_number >= start && line_number <= end
            })
            .map(|(_, line)| *line)
            .collect::<Vec<_>>()
            .join("\n");
        (format!("lines:{start}-{end}"), slice)
    } else if requested_mode.eq_ignore_ascii_case("reference") {
        (
            "reference".to_string(),
            format!(
                "{} ({} lines, {} bytes)",
                display_path(&target_path, &project_root),
                line_count,
                content.len()
            ),
        )
    } else {
        ("full".to_string(), content)
    };

    Ok(json!({
        "local": true,
        "tool": "ctx_read",
        "path": display_path(&target_path, &project_root),
        "mode_requested": requested_mode,
        "mode_used": mode_used,
        "line_count": line_count,
        "content": output,
    }))
}

fn execute_multi_read(project_root: &Path, arguments: &Map<String, Value>) -> Result<Value> {
    let project_root = canonical_project_root(project_root)?;
    let paths = get_required_paths(arguments)?;
    let requested_mode = get_optional_string(arguments, "mode").unwrap_or_else(|| "full".to_string());
    let start_line = get_optional_u64(arguments, "start_line");
    let end_line = get_optional_u64(arguments, "end_line");

    let files = paths
        .into_iter()
        .map(|path| {
            let mut per_file_arguments = Map::new();
            per_file_arguments.insert("path".to_string(), Value::String(path));
            per_file_arguments.insert("mode".to_string(), Value::String(requested_mode.clone()));
            if let Some(start_line) = start_line {
                per_file_arguments.insert("start_line".to_string(), Value::Number(start_line.into()));
            }
            if let Some(end_line) = end_line {
                per_file_arguments.insert("end_line".to_string(), Value::Number(end_line.into()));
            }
            execute_read(&project_root, &per_file_arguments)
        })
        .collect::<Result<Vec<_>>>()?;

    Ok(json!({
        "local": true,
        "tool": "ctx_multi_read",
        "mode_requested": requested_mode,
        "count": files.len(),
        "files": files,
    }))
}

fn search_file(project_root: &Path, file_path: &Path, regex: &Regex, ext_filter: Option<&str>, max_results: usize, matches: &mut Vec<Value>, files_searched: &mut usize) -> Result<()> {
    if matches.len() >= max_results {
        return Ok(());
    }

    if let Some(ext_filter) = ext_filter {
        let file_ext = file_path.extension().and_then(|ext| ext.to_str()).unwrap_or_default();
        if !file_ext.eq_ignore_ascii_case(ext_filter) {
            return Ok(());
        }
    }

    let metadata = fs::metadata(file_path)?;
    if metadata.len() > MAX_SEARCH_FILE_BYTES {
        return Ok(());
    }

    let content = match fs::read_to_string(file_path) {
        Ok(content) => content,
        Err(_) => return Ok(()),
    };

    *files_searched += 1;
    for (index, line) in content.lines().enumerate() {
        if regex.is_match(line) {
            matches.push(json!({
                "path": display_path(file_path, project_root),
                "line": index + 1,
                "text": line.trim(),
            }));
            if matches.len() >= max_results {
                break;
            }
        }
    }

    Ok(())
}

fn project_root(project_context: &ProjectContext) -> Result<PathBuf> {
    let path = PathBuf::from(&project_context.project_root);
    path.canonicalize()
        .with_context(|| format!("failed to resolve project root {}", path.display()))
}

fn canonical_project_root(project_root: &Path) -> Result<PathBuf> {
    project_root
        .canonicalize()
        .with_context(|| format!("failed to canonicalize project root {}", project_root.display()))
}

fn resolve_existing_path(project_root: &Path, requested_path: &str) -> Result<PathBuf> {
    let candidate = if Path::new(requested_path).is_absolute() {
        PathBuf::from(requested_path)
    } else {
        project_root.join(requested_path)
    };

    let resolved = candidate
        .canonicalize()
        .with_context(|| format!("failed to resolve {}", candidate.display()))?;
    if !resolved.starts_with(project_root) {
        bail!("Path `{}` is outside the current project root.", candidate.display());
    }

    Ok(resolved)
}

fn display_path(path: &Path, project_root: &Path) -> String {
    let relative = path.strip_prefix(project_root).unwrap_or(path).to_string_lossy().replace('\\', "/");
    if relative.is_empty() {
        ".".to_string()
    } else {
        relative
    }
}

fn count_child_files(path: &Path) -> u64 {
    WalkBuilder::new(path)
        .hidden(true)
        .git_ignore(true)
        .git_global(true)
        .git_exclude(true)
        .max_depth(Some(5))
        .build()
        .filter_map(|entry| entry.ok())
        .filter(|entry| entry.file_type().is_some_and(|file_type| file_type.is_file()))
        .count() as u64
}

fn get_required_string(arguments: &Map<String, Value>, key: &str) -> Result<String> {
    get_optional_string(arguments, key).ok_or_else(|| anyhow!("`{key}` is required."))
}

fn get_optional_string(arguments: &Map<String, Value>, key: &str) -> Option<String> {
    arguments.get(key)?.as_str().map(ToString::to_string)
}

fn get_optional_u64(arguments: &Map<String, Value>, key: &str) -> Option<u64> {
    arguments.get(key)?.as_u64()
}

fn get_optional_bool(arguments: &Map<String, Value>, key: &str) -> Option<bool> {
    arguments.get(key)?.as_bool()
}

fn get_required_paths(arguments: &Map<String, Value>) -> Result<Vec<String>> {
    let value = arguments.get("paths").ok_or_else(|| anyhow!("`paths` is required."))?;
    if let Some(paths) = value.as_array() {
        return paths
            .iter()
            .map(|item| {
                item.as_str()
                    .map(ToString::to_string)
                    .ok_or_else(|| anyhow!("Each `paths` item must be a string."))
            })
            .collect();
    }

    if let Some(single_path) = value.as_str() {
        return Ok(single_path.split(',').map(|item| item.trim().to_string()).filter(|item| !item.is_empty()).collect());
    }

    bail!("`paths` must be an array of strings.")
}

#[cfg(test)]
mod tests {
    use super::{execute_read, execute_search, execute_tree};
    use crate::models::{ProjectContext, RepositoryFingerprint, WorkspaceBinding};
    use serde_json::{json, Map};
    use std::fs;
    use tempfile::tempdir;

    fn test_project_context(root: &str) -> ProjectContext {
        ProjectContext {
            project_slug: "tmp".to_string(),
            project_root: root.to_string(),
            fingerprint: RepositoryFingerprint::default(),
            workspace_binding: WorkspaceBinding {
                local_root: Some(root.to_string()),
                ..WorkspaceBinding::default()
            },
            project_metadata: None,
        }
    }

    #[test]
    fn ctx_read_reads_file_content() {
        let temp_dir = tempdir().unwrap();
        let file_path = temp_dir.path().join("notes.txt");
        fs::write(&file_path, "alpha\nbeta\ngamma\n").unwrap();

        let mut arguments = Map::new();
        arguments.insert("path".to_string(), json!("notes.txt"));
        arguments.insert("start_line".to_string(), json!(2));
        arguments.insert("end_line".to_string(), json!(3));

        let payload = execute_read(temp_dir.path(), &arguments).unwrap();
        assert_eq!(payload["mode_used"], json!("lines:2-3"));
        assert!(payload["content"].as_str().unwrap().contains("beta"));
        assert!(payload["content"].as_str().unwrap().contains("gamma"));
    }

    #[test]
    fn ctx_search_finds_matches_in_project() {
        let temp_dir = tempdir().unwrap();
        fs::write(temp_dir.path().join("main.rs"), "fn alpha() {}\nfn beta() {}\n").unwrap();

        let mut arguments = Map::new();
        arguments.insert("pattern".to_string(), json!("alpha"));
        let payload = execute_search(temp_dir.path(), &arguments).unwrap();

        assert_eq!(payload["count"], json!(1));
        assert!(payload["matches"][0]["path"].as_str().unwrap().ends_with("main.rs"));
    }

    #[test]
    fn ctx_tree_lists_files() {
        let temp_dir = tempdir().unwrap();
        fs::create_dir_all(temp_dir.path().join("src")).unwrap();
        fs::write(temp_dir.path().join("src").join("main.rs"), "fn main() {}\n").unwrap();

        let payload = execute_tree(temp_dir.path(), &Map::new()).unwrap();
        assert_eq!(payload["count"], json!(2));
        assert!(payload["tree"].as_str().unwrap().contains("src/"));
    }

    #[test]
    fn project_context_helper_is_constructible() {
        let temp_dir = tempdir().unwrap();
        let project_context = test_project_context(temp_dir.path().to_string_lossy().as_ref());
        assert_eq!(project_context.project_root, temp_dir.path().to_string_lossy().as_ref());
    }
}