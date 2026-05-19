//! `ctx_impact` — Graph-based impact analysis tool.
//!
//! Uses the persisted graph index to answer: "What breaks when file X changes?"

use crate::core::graph_index::ProjectIndex;
use crate::core::tokens::count_tokens;

struct ImpactResult {
    affected_files: Vec<String>,
    max_depth_reached: usize,
    edges_traversed: usize,
}

struct DependencyChain {
    path: Vec<String>,
    depth: usize,
}

pub fn handle(action: &str, path: Option<&str>, root: &str, depth: Option<usize>) -> String {
    match action {
        "analyze" => handle_analyze(path, root, depth.unwrap_or(5)),
        "chain" => handle_chain(path, root),
        "build" => handle_build(root),
        "status" => handle_status(root),
        _ => "Unknown action. Use: analyze, chain, build, status".to_string(),
    }
}

fn open_graph(root: &str) -> ProjectIndex {
    crate::core::graph_index::load_or_build(root)
}

fn handle_analyze(path: Option<&str>, root: &str, max_depth: usize) -> String {
    let target = match path {
        Some(p) => p,
        None => return "path is required for 'analyze' action".to_string(),
    };

    let graph = open_graph(root);
    let rel_target = graph_target_key(target, root);

    if graph.file_count() == 0 {
        return "Graph is empty after auto-build. No supported source files found.".to_string();
    }

    let impact = impact_from_index(&graph, &rel_target, max_depth);
    format_impact(&impact, &rel_target)
}

fn format_impact(impact: &ImpactResult, target: &str) -> String {
    if impact.affected_files.is_empty() {
        let result = format!("No files depend on {target} (leaf node in the dependency graph).");
        let tokens = count_tokens(&result);
        return format!("{result}\n[ctx_impact: {tokens} tok]");
    }

    let mut result = format!(
        "Impact of changing {target}: {} affected files (depth: {}, edges traversed: {})\n",
        impact.affected_files.len(),
        impact.max_depth_reached,
        impact.edges_traversed
    );

    let mut sorted = impact.affected_files.clone();
    sorted.sort();

    for file in &sorted {
        result.push_str(&format!("  {file}\n"));
    }

    let tokens = count_tokens(&result);
    format!("{result}[ctx_impact: {tokens} tok]")
}

fn handle_chain(path: Option<&str>, root: &str) -> String {
    let spec = match path {
        Some(p) => p,
        None => {
            return "path is required for 'chain' action (format: from_file->to_file)".to_string();
        }
    };

    let (from, to) = match spec.split_once("->") {
        Some((f, t)) => (f.trim(), t.trim()),
        None => {
            return format!(
                "Invalid chain spec '{spec}'. Use format: from_file->to_file\n\
                 Example: src/server.rs->src/core/config.rs"
            );
        }
    };

    let graph = open_graph(root);
    let rel_from = graph_target_key(from, root);
    let rel_to = graph_target_key(to, root);

    match dependency_chain_from_index(&graph, &rel_from, &rel_to) {
        Some(chain) => format_chain(&chain),
        None => {
            let result = format!("No dependency path from {rel_from} to {rel_to}");
            let tokens = count_tokens(&result);
            format!("{result}\n[ctx_impact chain: {tokens} tok]")
        }
    }
}

fn format_chain(chain: &DependencyChain) -> String {
    let mut result = format!("Dependency chain (depth {}):\n", chain.depth);
    for (i, step) in chain.path.iter().enumerate() {
        if i > 0 {
            result.push_str("  -> ");
        } else {
            result.push_str("  ");
        }
        result.push_str(step);
        result.push('\n');
    }
    let tokens = count_tokens(&result);
    format!("{result}[ctx_impact chain: {tokens} tok]")
}

fn graph_target_key(path: &str, root: &str) -> String {
    let rel = crate::core::graph_index::graph_relative_key(path, root);
    let rel_key = crate::core::graph_index::graph_match_key(&rel);
    if rel_key.is_empty() {
        crate::core::graph_index::graph_match_key(path)
    } else {
        rel_key
    }
}

fn impact_from_index(index: &ProjectIndex, target: &str, max_depth: usize) -> ImpactResult {
    use std::collections::{HashSet, VecDeque};

    let mut affected_files = Vec::new();
    let mut visited = HashSet::new();
    let mut queue = VecDeque::from([(target.to_string(), 0usize)]);
    let mut edges_traversed = 0usize;
    let mut max_depth_reached = 0usize;

    while let Some((current, depth)) = queue.pop_front() {
        if depth > max_depth || !visited.insert(current.clone()) {
            continue;
        }

        if current != target {
            affected_files.push(current.clone());
            max_depth_reached = max_depth_reached.max(depth);
        }

        for edge in index
            .edges
            .iter()
            .filter(|edge| edge.kind == "import" && edge.to == current)
        {
            edges_traversed += 1;
            if !visited.contains(&edge.from) {
                queue.push_back((edge.from.clone(), depth + 1));
            }
        }
    }

    affected_files.sort();
    affected_files.dedup();

    ImpactResult {
        max_depth_reached,
        edges_traversed,
        affected_files,
    }
}

fn dependency_chain_from_index(
    index: &ProjectIndex,
    from: &str,
    to: &str,
) -> Option<DependencyChain> {
    use std::collections::{HashSet, VecDeque};

    let mut queue = VecDeque::from([(from.to_string(), vec![from.to_string()])]);
    let mut visited = HashSet::new();

    while let Some((current, path)) = queue.pop_front() {
        if !visited.insert(current.clone()) {
            continue;
        }

        if current == to {
            return Some(DependencyChain {
                depth: path.len().saturating_sub(1),
                path,
            });
        }

        for edge in index
            .edges
            .iter()
            .filter(|edge| edge.kind == "import" && edge.from == current)
        {
            if visited.contains(&edge.to) {
                continue;
            }

            let mut next_path = path.clone();
            next_path.push(edge.to.clone());
            queue.push_back((edge.to.clone(), next_path));
        }
    }

    None
}

fn handle_build(root: &str) -> String {
    let index = crate::core::graph_index::scan(root);

    let result = format!(
        "Graph built: {} files, {} symbols, {} edges\n\
         Stored in graph index cache",
        index.file_count(),
        index.symbol_count(),
        index.edge_count()
    );
    let tokens = count_tokens(&result);
    format!("{result}\n[ctx_impact build: {tokens} tok]")
}

fn handle_status(root: &str) -> String {
    let index = open_graph(root);
    let nodes = index.file_count();
    let edges = index.edge_count();

    if nodes == 0 {
        return "Graph is empty. Run ctx_impact action='build' to index.".to_string();
    }

    format!("Graph Index: {nodes} files, {edges} edges\nStored in graph index cache")
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn format_impact_empty() {
        let impact = ImpactResult {
            affected_files: vec![],
            max_depth_reached: 0,
            edges_traversed: 0,
        };
        let result = format_impact(&impact, "a.rs");
        assert!(result.contains("No files depend on"));
    }

    #[test]
    fn format_impact_with_files() {
        let impact = ImpactResult {
            affected_files: vec!["b.rs".to_string(), "c.rs".to_string()],
            max_depth_reached: 2,
            edges_traversed: 3,
        };
        let result = format_impact(&impact, "a.rs");
        assert!(result.contains("2 affected files"));
        assert!(result.contains("b.rs"));
        assert!(result.contains("c.rs"));
    }

    #[test]
    fn format_chain_display() {
        let chain = DependencyChain {
            path: vec!["a.rs".to_string(), "b.rs".to_string(), "c.rs".to_string()],
            depth: 2,
        };
        let result = format_chain(&chain);
        assert!(result.contains("depth 2"));
        assert!(result.contains("a.rs"));
        assert!(result.contains("-> b.rs"));
        assert!(result.contains("-> c.rs"));
    }

    #[test]
    fn handle_missing_path() {
        let result = handle("analyze", None, "/tmp", None);
        assert!(result.contains("path is required"));
    }

    #[test]
    fn handle_invalid_chain_spec() {
        let result = handle("chain", Some("no_arrow_here"), "/tmp", None);
        assert!(result.contains("Invalid chain spec"));
    }

    #[test]
    fn handle_unknown_action() {
        let result = handle("invalid", None, "/tmp", None);
        assert!(result.contains("Unknown action"));
    }

    #[test]
    fn graph_target_key_normalizes_windows_styles() {
        let target = graph_target_key(r"C:/repo/src/main.rs", r"C:\repo");
        let expected = if cfg!(windows) {
            "src/main.rs"
        } else {
            "C:/repo/src/main.rs"
        };
        assert_eq!(target, expected);
    }

    #[test]
    fn impact_from_index_reports_actual_depth_and_edges() {
        let mut index = ProjectIndex::new("/test");
        index.edges.push(crate::core::graph_index::IndexEdge {
            from: "a.rs".to_string(),
            to: "b.rs".to_string(),
            kind: "import".to_string(),
        });
        index.edges.push(crate::core::graph_index::IndexEdge {
            from: "b.rs".to_string(),
            to: "c.rs".to_string(),
            kind: "import".to_string(),
        });
        index.edges.push(crate::core::graph_index::IndexEdge {
            from: "x.rs".to_string(),
            to: "y.rs".to_string(),
            kind: "import".to_string(),
        });

        let impact = impact_from_index(&index, "c.rs", 5);
        assert_eq!(
            impact.affected_files,
            vec!["a.rs".to_string(), "b.rs".to_string()]
        );
        assert_eq!(impact.max_depth_reached, 2);
        assert_eq!(impact.edges_traversed, 2);
    }
}
