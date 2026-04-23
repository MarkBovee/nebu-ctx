use anyhow::{anyhow, bail, Context, Result};
use ignore::WalkBuilder;
use regex::Regex;
use serde_json::{json, Map, Value};
use std::fs;
use std::path::{Path, PathBuf};

#[derive(Clone, Debug)]
struct SymbolInfo {
    name: String,
    kind: String,
    line: usize,
    signature: String,
}

pub fn execute_outline(project_root: &Path, arguments: &Map<String, Value>) -> Result<Value> {
    let project_root = canonical_project_root(project_root)?;
    let requested_path = get_required_string(arguments, "path")?;
    let file_path = resolve_existing_path(&project_root, &requested_path)?;
    let kind_filter = get_optional_string(arguments, "kind");
    let content = fs::read_to_string(&file_path).with_context(|| format!("failed to read {}", file_path.display()))?;
    let symbols = extract_symbols(&content, &file_path)
        .into_iter()
        .filter(|symbol| kind_filter.as_ref().map(|kind| symbol.kind.eq_ignore_ascii_case(kind)).unwrap_or(true))
        .map(|symbol| symbol_to_value(&symbol))
        .collect::<Vec<_>>();

    Ok(json!({
        "local": true,
        "tool": "ctx_outline",
        "path": display_path(&file_path, &project_root),
        "count": symbols.len(),
        "symbols": symbols,
    }))
}

pub fn execute_symbol(project_root: &Path, arguments: &Map<String, Value>) -> Result<Value> {
    let project_root = canonical_project_root(project_root)?;
    let symbol_name = get_required_string(arguments, "name")?;
    let kind_filter = get_optional_string(arguments, "kind");
    let file_filter = get_optional_string(arguments, "file");

    let matches = find_symbol_matches(&project_root, &symbol_name, file_filter.as_deref(), kind_filter.as_deref())?;
    if matches.is_empty() {
        return Ok(json!({
            "local": true,
            "tool": "ctx_symbol",
            "name": symbol_name,
            "count": 0,
            "matches": [],
            "message": format!("Symbol `{}` was not found in the current project.", symbol_name),
        }));
    }

    let match_values = matches
        .iter()
        .map(|item| {
            json!({
                "path": display_path(&item.0, &project_root),
                "symbol": symbol_to_value(&item.1),
                "content": item.2,
            })
        })
        .collect::<Vec<_>>();

    Ok(json!({
        "local": true,
        "tool": "ctx_symbol",
        "name": symbol_name,
        "count": match_values.len(),
        "matches": match_values,
    }))
}

pub fn execute_callers(project_root: &Path, arguments: &Map<String, Value>) -> Result<Value> {
    let project_root = canonical_project_root(project_root)?;
    let symbol_name = get_required_string(arguments, "symbol")?;
    let file_filter = get_optional_string(arguments, "file");
    let call_regex = Regex::new(&format!(r"\b{}\s*\(", regex::escape(&symbol_name)))?;

    let mut matches = Vec::new();
    for file_path in enumerate_project_files(&project_root, file_filter.as_deref())? {
        let content = match fs::read_to_string(&file_path) {
            Ok(content) => content,
            Err(_) => continue,
        };

        for (index, line) in content.lines().enumerate() {
            if call_regex.is_match(line) && !is_definition_line(line, &symbol_name) {
                matches.push(json!({
                    "path": display_path(&file_path, &project_root),
                    "line": index + 1,
                    "text": line.trim(),
                }));
            }
        }
    }

    Ok(json!({
        "local": true,
        "tool": "ctx_callers",
        "symbol": symbol_name,
        "count": matches.len(),
        "matches": matches,
    }))
}

pub fn execute_callees(project_root: &Path, arguments: &Map<String, Value>) -> Result<Value> {
    let project_root = canonical_project_root(project_root)?;
    let symbol_name = get_required_string(arguments, "symbol")?;
    let file_filter = get_optional_string(arguments, "file");
    let matches = find_symbol_matches(&project_root, &symbol_name, file_filter.as_deref(), None)?;
    let Some((file_path, symbol, content)) = matches.into_iter().next() else {
        return Ok(json!({
            "local": true,
            "tool": "ctx_callees",
            "symbol": symbol_name,
            "count": 0,
            "callees": [],
            "message": format!("Symbol `{}` was not found in the current project.", symbol_name),
        }));
    };

    let call_pattern = Regex::new(r"\b([A-Za-z_][A-Za-z0-9_:]*)\s*\(")?;
    let ignored = ["if", "for", "while", "match", "switch", "return", "sizeof", "nameof", "catch", "lock", "using", symbol.name.as_str()];

    let mut callees = Vec::new();
    for (index, line) in content.lines().enumerate() {
        for capture in call_pattern.captures_iter(line) {
            let Some(name) = capture.get(1).map(|value| value.as_str()) else {
                continue;
            };
            if ignored.iter().any(|ignored_name| name.eq_ignore_ascii_case(ignored_name)) {
                continue;
            }

            callees.push(json!({
                "name": name,
                "line": symbol.line + index,
                "text": line.trim(),
            }));
        }
    }

    Ok(json!({
        "local": true,
        "tool": "ctx_callees",
        "symbol": symbol_name,
        "path": display_path(&file_path, &project_root),
        "count": callees.len(),
        "callees": callees,
    }))
}

fn find_symbol_matches(project_root: &Path, symbol_name: &str, file_filter: Option<&str>, kind_filter: Option<&str>) -> Result<Vec<(PathBuf, SymbolInfo, String)>> {
    let mut matches = Vec::new();
    for file_path in enumerate_project_files(project_root, file_filter)? {
        let content = match fs::read_to_string(&file_path) {
            Ok(content) => content,
            Err(_) => continue,
        };
        let symbols = extract_symbols(&content, &file_path);
        for (index, symbol) in symbols.iter().enumerate() {
            if !symbol.name.eq_ignore_ascii_case(symbol_name) {
                continue;
            }
            if let Some(kind_filter) = kind_filter {
                if !symbol.kind.eq_ignore_ascii_case(kind_filter) {
                    continue;
                }
            }

            let snippet = extract_symbol_snippet(&content, &symbols, index);
            matches.push((file_path.clone(), symbol.clone(), snippet));
        }
    }
    Ok(matches)
}

fn enumerate_project_files(project_root: &Path, file_filter: Option<&str>) -> Result<Vec<PathBuf>> {
    let mut files = Vec::new();
    let walker = WalkBuilder::new(project_root)
        .hidden(true)
        .git_ignore(true)
        .git_global(true)
        .git_exclude(true)
        .build();

    for entry in walker.filter_map(|entry| entry.ok()) {
        if entry.file_type().is_none_or(|file_type| file_type.is_dir()) {
            continue;
        }

        let path = entry.into_path();
        if !is_source_file(&path) {
            continue;
        }
        if let Some(file_filter) = file_filter {
            if !display_path(&path, project_root).contains(file_filter) {
                continue;
            }
        }

        files.push(path);
    }

    Ok(files)
}

fn extract_symbols(content: &str, path: &Path) -> Vec<SymbolInfo> {
    let extension = path.extension().and_then(|value| value.to_str()).unwrap_or_default();
    let patterns = symbol_patterns(extension);
    let mut symbols = Vec::new();

    for (index, line) in content.lines().enumerate() {
        for (kind, regex) in &patterns {
            if let Some(captures) = regex.captures(line) {
                let Some(name) = captures.name("name").map(|value| value.as_str()) else {
                    continue;
                };
                symbols.push(SymbolInfo {
                    name: name.to_string(),
                    kind: (*kind).to_string(),
                    line: index + 1,
                    signature: line.trim().to_string(),
                });
                break;
            }
        }
    }

    symbols
}

fn symbol_patterns(extension: &str) -> Vec<(&'static str, Regex)> {
    match extension {
        "rs" => vec![
            ("fn", Regex::new(r"^\s*(?:pub\s+)?(?:async\s+)?fn\s+(?P<name>[A-Za-z_][A-Za-z0-9_]*)").unwrap()),
            ("struct", Regex::new(r"^\s*(?:pub\s+)?struct\s+(?P<name>[A-Za-z_][A-Za-z0-9_]*)").unwrap()),
            ("enum", Regex::new(r"^\s*(?:pub\s+)?enum\s+(?P<name>[A-Za-z_][A-Za-z0-9_]*)").unwrap()),
            ("trait", Regex::new(r"^\s*(?:pub\s+)?trait\s+(?P<name>[A-Za-z_][A-Za-z0-9_]*)").unwrap()),
            ("impl", Regex::new(r"^\s*impl(?:<[^>]+>)?\s+(?P<name>[A-Za-z_][A-Za-z0-9_:]*)").unwrap()),
        ],
        "cs" => vec![
            ("class", Regex::new(r"^\s*(?:public|private|internal|protected|sealed|abstract|partial|static|\s)+class\s+(?P<name>[A-Za-z_][A-Za-z0-9_]*)").unwrap()),
            ("record", Regex::new(r"^\s*(?:public|private|internal|protected|sealed|abstract|partial|static|\s)+record\s+(?P<name>[A-Za-z_][A-Za-z0-9_]*)").unwrap()),
            ("struct", Regex::new(r"^\s*(?:public|private|internal|protected|readonly|ref|partial|\s)+struct\s+(?P<name>[A-Za-z_][A-Za-z0-9_]*)").unwrap()),
            ("interface", Regex::new(r"^\s*(?:public|private|internal|protected|partial|\s)+interface\s+(?P<name>[A-Za-z_][A-Za-z0-9_]*)").unwrap()),
            ("enum", Regex::new(r"^\s*(?:public|private|internal|protected|\s)+enum\s+(?P<name>[A-Za-z_][A-Za-z0-9_]*)").unwrap()),
            ("method", Regex::new(r"^\s*(?:public|private|internal|protected|static|virtual|override|sealed|async|partial|extern|new|\s)+[A-Za-z_][A-Za-z0-9_<>,\[\]?\.]*\s+(?P<name>[A-Za-z_][A-Za-z0-9_]*)\s*\(").unwrap()),
        ],
        "py" => vec![
            ("class", Regex::new(r"^\s*class\s+(?P<name>[A-Za-z_][A-Za-z0-9_]*)").unwrap()),
            ("fn", Regex::new(r"^\s*(?:async\s+)?def\s+(?P<name>[A-Za-z_][A-Za-z0-9_]*)").unwrap()),
        ],
        "js" | "ts" | "tsx" => vec![
            ("class", Regex::new(r"^\s*class\s+(?P<name>[A-Za-z_][A-Za-z0-9_]*)").unwrap()),
            ("fn", Regex::new(r"^\s*function\s+(?P<name>[A-Za-z_][A-Za-z0-9_]*)").unwrap()),
            ("fn", Regex::new(r"^\s*const\s+(?P<name>[A-Za-z_][A-Za-z0-9_]*)\s*=\s*(?:async\s*)?\(").unwrap()),
        ],
        _ => vec![
            ("fn", Regex::new(r"^\s*(?:pub\s+)?(?:async\s+)?fn\s+(?P<name>[A-Za-z_][A-Za-z0-9_]*)").unwrap()),
            ("class", Regex::new(r"^\s*class\s+(?P<name>[A-Za-z_][A-Za-z0-9_]*)").unwrap()),
            ("fn", Regex::new(r"^\s*function\s+(?P<name>[A-Za-z_][A-Za-z0-9_]*)").unwrap()),
        ],
    }
}

fn extract_symbol_snippet(content: &str, symbols: &[SymbolInfo], index: usize) -> String {
    let all_lines: Vec<&str> = content.lines().collect();
    let start = symbols[index].line.saturating_sub(1);
    let end = symbols
        .get(index + 1)
        .map(|next| next.line.saturating_sub(1))
        .unwrap_or_else(|| all_lines.len())
        .min(start + 60)
        .max(start + 1);
    all_lines[start..end].join("\n")
}

fn symbol_to_value(symbol: &SymbolInfo) -> Value {
    json!({
        "name": symbol.name,
        "kind": symbol.kind,
        "line": symbol.line,
        "signature": symbol.signature,
    })
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

fn is_source_file(path: &Path) -> bool {
    matches!(path.extension().and_then(|value| value.to_str()).unwrap_or_default(), "rs" | "cs" | "py" | "js" | "ts" | "tsx")
}

fn is_definition_line(line: &str, symbol: &str) -> bool {
    let trimmed = line.trim();
    trimmed.contains(&format!("fn {symbol}"))
        || trimmed.contains(&format!("def {symbol}"))
        || trimmed.contains(&format!("function {symbol}"))
        || trimmed.contains(&format!("class {symbol}"))
        || trimmed.contains(&format!("struct {symbol}"))
        || trimmed.contains(&format!("record {symbol}"))
}

fn get_required_string(arguments: &Map<String, Value>, key: &str) -> Result<String> {
    get_optional_string(arguments, key).ok_or_else(|| anyhow!("`{key}` is required."))
}

fn get_optional_string(arguments: &Map<String, Value>, key: &str) -> Option<String> {
    arguments.get(key)?.as_str().map(ToString::to_string)
}

#[cfg(test)]
mod tests {
    use super::{execute_callers, execute_callees, execute_outline, execute_symbol};
    use serde_json::{json, Map};
    use std::fs;
    use tempfile::tempdir;

    #[test]
    fn outline_lists_csharp_symbols() {
        let temp_dir = tempdir().unwrap();
        let file_path = temp_dir.path().join("Sample.cs");
        fs::write(&file_path, "public class Sample\n{\n    public void Run() {}\n}\n").unwrap();

        let mut arguments = Map::new();
        arguments.insert("path".to_string(), json!("Sample.cs"));
        let payload = execute_outline(temp_dir.path(), &arguments).unwrap();

        assert_eq!(payload["count"], json!(2));
        assert!(payload["symbols"].to_string().contains("Sample"));
        assert!(payload["symbols"].to_string().contains("Run"));
    }

    #[test]
    fn symbol_returns_snippet_for_single_match() {
        let temp_dir = tempdir().unwrap();
        let file_path = temp_dir.path().join("main.rs");
        fs::write(&file_path, "fn alpha() {\n    beta();\n}\n\nfn beta() {}\n").unwrap();

        let mut arguments = Map::new();
        arguments.insert("name".to_string(), json!("alpha"));
        let payload = execute_symbol(temp_dir.path(), &arguments).unwrap();

        assert_eq!(payload["count"], json!(1));
        assert!(payload["matches"][0]["content"].as_str().unwrap().contains("beta();"));
    }

    #[test]
    fn callers_find_symbol_invocations() {
        let temp_dir = tempdir().unwrap();
        let file_path = temp_dir.path().join("main.rs");
        fs::write(&file_path, "fn alpha() { helper(); }\nfn helper() {}\n").unwrap();

        let mut arguments = Map::new();
        arguments.insert("symbol".to_string(), json!("helper"));
        let payload = execute_callers(temp_dir.path(), &arguments).unwrap();

        assert_eq!(payload["count"], json!(1));
    }

    #[test]
    fn callees_extract_calls_from_symbol_body() {
        let temp_dir = tempdir().unwrap();
        let file_path = temp_dir.path().join("main.rs");
        fs::write(&file_path, "fn alpha() {\n    beta();\n    gamma();\n}\n\nfn beta() {}\nfn gamma() {}\n").unwrap();

        let mut arguments = Map::new();
        arguments.insert("symbol".to_string(), json!("alpha"));
        let payload = execute_callees(temp_dir.path(), &arguments).unwrap();

        assert_eq!(payload["count"], json!(2));
        assert!(payload["callees"].to_string().contains("beta"));
        assert!(payload["callees"].to_string().contains("gamma"));
    }
}