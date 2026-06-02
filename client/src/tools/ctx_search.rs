use std::collections::HashSet;
use std::path::{Path, PathBuf};

use grep_matcher::Matcher;
use grep_regex::RegexMatcherBuilder;
use grep_searcher::{sinks::Lossy, BinaryDetection, SearcherBuilder};
use ignore::WalkBuilder;

use crate::core::protocol;
use crate::core::symbol_map::{self, SymbolMap};
use crate::core::tokens::count_tokens;
use crate::tools::CrpMode;

const MAX_FILE_SIZE: u64 = 512_000;
const MAX_WALK_DEPTH: usize = 20;

#[derive(Debug)]
struct SearchMatch {
    path: PathBuf,
    line_number: u64,
    line_text: String,
}

#[derive(Debug)]
struct SearchSummary {
    matches: Vec<SearchMatch>,
    files_searched: u32,
    files_skipped_size: u32,
    files_skipped_generated: u32,
    files_skipped_binary: u32,
}

#[derive(Debug)]
enum SearchError {
    InvalidRegex(String),
    InvalidPath(String),
    SearchFailed(String),
}

pub fn handle(
    pattern: &str,
    dir: &str,
    ext_filter: Option<&str>,
    max_results: usize,
    _crp_mode: CrpMode,
    respect_gitignore: bool,
) -> (String, usize) {
    let summary = match collect_matches(pattern, dir, ext_filter, max_results, respect_gitignore) {
        Ok(summary) => summary,
        Err(SearchError::InvalidRegex(err)) => return (format!("ERROR: invalid regex: {err}"), 0),
        Err(SearchError::InvalidPath(err)) => return (format!("ERROR: {err}"), 0),
        Err(SearchError::SearchFailed(err)) => return (format!("ERROR: search failed: {err}"), 0),
    };

    if summary.matches.is_empty() {
        let mut msg = format!(
            "0 matches for '{pattern}' in {} files",
            summary.files_searched
        );
        if summary.files_skipped_size > 0 {
            msg.push_str(&format!(
                " ({} large files skipped)",
                summary.files_skipped_size
            ));
        }
        if summary.files_skipped_generated > 0 {
            msg.push_str(&format!(
                " ({} generated files skipped)",
                summary.files_skipped_generated
            ));
        }
        if summary.files_skipped_binary > 0 {
            msg.push_str(&format!(
                " ({} binary files skipped)",
                summary.files_skipped_binary
            ));
        }
        return (msg, 0);
    }

    let matches: Vec<String> = summary
        .matches
        .iter()
        .map(|entry| {
            let short_path = protocol::shorten_path(&entry.path.to_string_lossy());
            format!(
                "{short_path}:{} {}",
                entry.line_number,
                entry.line_text.trim()
            )
        })
        .collect();
    let raw_result_lines: Vec<String> = summary
        .matches
        .iter()
        .map(|entry| {
            format!(
                "{}:{}: {}",
                entry.path.to_string_lossy(),
                entry.line_number,
                entry.line_text.trim()
            )
        })
        .collect();

    let mut result = format!(
        "{} matches in {} files:\n{}",
        matches.len(),
        summary.files_searched,
        matches.join("\n")
    );

    if summary.files_skipped_size > 0 {
        result.push_str(&format!(
            "\n({} files >512KB skipped)",
            summary.files_skipped_size
        ));
    }
    if summary.files_skipped_generated > 0 {
        result.push_str(&format!(
            "\n({} generated files skipped)",
            summary.files_skipped_generated
        ));
    }
    if summary.files_skipped_binary > 0 {
        result.push_str(&format!(
            "\n({} binary files skipped)",
            summary.files_skipped_binary
        ));
    }

    let scope_hint = monorepo_scope_hint(&matches, dir);

    {
        let file_ext = ext_filter.unwrap_or("rs");
        let mut sym = SymbolMap::new();
        let idents = symbol_map::extract_identifiers(&result, file_ext);
        for ident in &idents {
            sym.register(ident);
        }
        if sym.len() >= 3 {
            let sym_table = sym.format_table();
            let compressed = sym.apply(&result);
            let original_tok = count_tokens(&result);
            let compressed_tok = count_tokens(&compressed) + count_tokens(&sym_table);
            let net_saving = original_tok.saturating_sub(compressed_tok);
            if original_tok > 0 && net_saving * 100 / original_tok >= 5 {
                result = format!("{compressed}{sym_table}");
            }
        }
    }

    if let Some(hint) = scope_hint {
        result.push_str(&hint);
    }

    let raw_output = raw_result_lines.join("\n");
    let raw_tokens = count_tokens(&raw_output);
    let sent = count_tokens(&result);
    let savings = protocol::format_savings(raw_tokens, sent);

    (format!("{result}\n{savings}"), raw_tokens)
}

fn collect_matches(
    pattern: &str,
    dir: &str,
    ext_filter: Option<&str>,
    max_results: usize,
    respect_gitignore: bool,
) -> Result<SearchSummary, SearchError> {
    let matcher = RegexMatcherBuilder::new()
        .case_smart(true)
        .build(pattern)
        .map_err(|err| SearchError::InvalidRegex(err.to_string()))?;

    if max_results == 0 {
        return Ok(SearchSummary {
            matches: Vec::new(),
            files_searched: 0,
            files_skipped_size: 0,
            files_skipped_generated: 0,
            files_skipped_binary: 0,
        });
    }

    let root = Path::new(dir);
    if !root.exists() {
        return Err(SearchError::InvalidPath(format!("{dir} does not exist")));
    }

    let walker = WalkBuilder::new(root)
        .hidden(true)
        .max_depth(Some(MAX_WALK_DEPTH))
        .git_ignore(respect_gitignore)
        .git_global(respect_gitignore)
        .git_exclude(respect_gitignore)
        .build();

    let mut summary = SearchSummary {
        matches: Vec::new(),
        files_searched: 0,
        files_skipped_size: 0,
        files_skipped_generated: 0,
        files_skipped_binary: 0,
    };

    for entry in walker.filter_map(|entry| entry.ok()) {
        if entry.file_type().is_none_or(|file_type| file_type.is_dir()) {
            continue;
        }
        if entry
            .file_type()
            .is_some_and(|file_type| file_type.is_symlink())
        {
            continue;
        }

        let path = entry.into_path();
        if is_generated_file(&path) {
            summary.files_skipped_generated += 1;
            continue;
        }
        if is_binary_ext(&path) {
            summary.files_skipped_binary += 1;
            continue;
        }
        if let Some(ext) = ext_filter {
            let file_ext = path
                .extension()
                .and_then(|value| value.to_str())
                .unwrap_or("");
            if file_ext != ext {
                continue;
            }
        }

        let metadata = std::fs::metadata(&path)
            .map_err(|err| SearchError::SearchFailed(format!("{}: {err}", path.display())))?;
        if metadata.len() > MAX_FILE_SIZE {
            summary.files_skipped_size += 1;
            continue;
        }

        summary.files_searched += 1;
        let mut searcher = SearcherBuilder::new()
            .line_number(true)
            .binary_detection(BinaryDetection::quit(0))
            .build();
        let mut per_file_matches = Vec::new();

        searcher
            .search_path(
                &matcher,
                &path,
                Lossy(|line_number, line| {
                    if matcher.find(line.as_bytes()).ok().flatten().is_none() {
                        return Ok(true);
                    }
                    per_file_matches.push(SearchMatch {
                        path: path.clone(),
                        line_number,
                        line_text: line.trim_end_matches('\n').to_string(),
                    });
                    Ok(per_file_matches.len() + summary.matches.len() < max_results)
                }),
            )
            .map_err(|err| SearchError::SearchFailed(format!("{}: {err}", path.display())))?;

        summary.matches.extend(per_file_matches);
        if summary.matches.len() >= max_results {
            break;
        }
    }

    Ok(summary)
}

fn is_binary_ext(path: &Path) -> bool {
    let ext = path.extension().and_then(|e| e.to_str()).unwrap_or("");
    matches!(
        ext,
        "png"
            | "jpg"
            | "jpeg"
            | "gif"
            | "webp"
            | "ico"
            | "svg"
            | "woff"
            | "woff2"
            | "ttf"
            | "eot"
            | "pdf"
            | "zip"
            | "tar"
            | "gz"
            | "br"
            | "zst"
            | "bz2"
            | "xz"
            | "mp3"
            | "mp4"
            | "webm"
            | "ogg"
            | "wasm"
            | "so"
            | "dylib"
            | "dll"
            | "exe"
            | "lock"
            | "map"
            | "snap"
            | "patch"
            | "db"
            | "sqlite"
            | "parquet"
            | "arrow"
            | "bin"
            | "o"
            | "a"
            | "class"
            | "pyc"
            | "pyo"
    )
}

fn is_generated_file(path: &Path) -> bool {
    let name = path.file_name().and_then(|n| n.to_str()).unwrap_or("");
    name.ends_with(".min.js")
        || name.ends_with(".min.css")
        || name.ends_with(".bundle.js")
        || name.ends_with(".chunk.js")
        || name.ends_with(".d.ts")
        || name.ends_with(".js.map")
        || name.ends_with(".css.map")
}

fn monorepo_scope_hint(matches: &[String], search_dir: &str) -> Option<String> {
    let top_dirs: HashSet<&str> = matches
        .iter()
        .filter_map(|m| {
            let path = m.split(':').next()?;
            let relative = path.strip_prefix("./").unwrap_or(path);
            let relative = relative.strip_prefix(search_dir).unwrap_or(relative);
            let relative = relative.strip_prefix('/').unwrap_or(relative);
            relative.split('/').next()
        })
        .collect();

    if top_dirs.len() > 3 {
        let mut dirs: Vec<&&str> = top_dirs.iter().collect();
        dirs.sort();
        let dir_list: Vec<String> = dirs.iter().take(6).map(|d| format!("'{d}'")).collect();
        let extra = if top_dirs.len() > 6 {
            format!(", +{} more", top_dirs.len() - 6)
        } else {
            String::new()
        };
        Some(format!(
            "\n\nResults span {} directories ({}{}). \
             Use the 'path' parameter to scope to a specific service, \
             e.g. path=\"{}/\".",
            top_dirs.len(),
            dir_list.join(", "),
            extra,
            dirs[0]
        ))
    } else {
        None
    }
}

#[cfg(test)]
mod tests {
    use super::handle;
    use crate::tools::CrpMode;

    #[test]
    fn search_finds_regex_hits_in_source_files() {
        let dir = tempfile::tempdir().unwrap();
        let file = dir.path().join("component.tsx");
        std::fs::write(
            &file,
            "export function Banner() {\n  return <div>Hello Search</div>;\n}\n",
        )
        .unwrap();

        let (result, raw_tokens) = handle(
            "Hello Search",
            dir.path().to_str().unwrap(),
            Some("tsx"),
            20,
            CrpMode::Off,
            true,
        );
        assert!(
            result.contains("matches in 1 files") || result.contains("1 matches in 1 files"),
            "unexpected result: {result}"
        );
        assert!(result.contains("Hello Search"), "match missing: {result}");
        assert!(raw_tokens > 0);
    }

    #[test]
    fn search_reports_invalid_regex_explicitly() {
        let dir = tempfile::tempdir().unwrap();
        let (result, raw_tokens) = handle(
            "(",
            dir.path().to_str().unwrap(),
            None,
            20,
            CrpMode::Off,
            true,
        );
        assert!(result.contains("ERROR: invalid regex"));
        assert_eq!(raw_tokens, 0);
    }

    #[test]
    fn search_respects_gitignore_toggle() {
        let dir = tempfile::tempdir().unwrap();
        std::fs::create_dir(dir.path().join(".git")).unwrap();
        std::fs::write(dir.path().join(".gitignore"), "ignored.ts\n").unwrap();
        std::fs::write(
            dir.path().join("ignored.ts"),
            "const value = 'hidden hit';\n",
        )
        .unwrap();

        let (ignored, _) = handle(
            "hidden hit",
            dir.path().to_str().unwrap(),
            Some("ts"),
            20,
            CrpMode::Off,
            true,
        );
        let (included, _) = handle(
            "hidden hit",
            dir.path().to_str().unwrap(),
            Some("ts"),
            20,
            CrpMode::Off,
            false,
        );

        assert!(
            ignored.contains("0 matches"),
            "expected gitignored file to stay hidden: {ignored}"
        );
        assert!(
            included.contains("hidden hit"),
            "expected ignore override to include file: {included}"
        );
    }
}
