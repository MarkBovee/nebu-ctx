use crate::models::{ProjectLanguageStat, ProjectMetadataEnvelope, ProjectMetadataSummary};
use anyhow::{Context, Result};
use ignore::WalkBuilder;
use std::collections::BTreeMap;
use std::path::Path;

const MARKER_FILES: &[&str] = &[
    ".git",
    "Cargo.toml",
    "package.json",
    "tsconfig.json",
    "pyproject.toml",
    "requirements.txt",
    "README.md",
    "compose.yaml",
    "docker-compose.yml",
    "NebulaRAG.slnx",
];

pub fn build_project_metadata(project_root: &Path) -> Result<ProjectMetadataEnvelope> {
    let project_root = project_root
        .canonicalize()
        .with_context(|| format!("failed to canonicalize project root {}", project_root.display()))?;

    let mut total_file_count = 0u64;
    let mut source_file_count = 0u64;
    let mut language_counts = BTreeMap::<String, u64>::new();

    let walker = WalkBuilder::new(&project_root)
        .hidden(true)
        .git_ignore(true)
        .git_global(true)
        .git_exclude(true)
        .build();

    for entry in walker.filter_map(|entry| entry.ok()) {
        if entry.file_type().is_none_or(|file_type| file_type.is_dir()) {
            continue;
        }

        total_file_count += 1;
        let path = entry.into_path();
        if let Some(language) = infer_language(&path) {
            source_file_count += 1;
            *language_counts.entry(language.to_string()).or_default() += 1;
        }
    }

    let markers = MARKER_FILES
        .iter()
        .filter(|marker| project_root.join(marker).exists())
        .map(|marker| marker.to_string())
        .collect::<Vec<_>>();

    let mut languages = language_counts
        .into_iter()
        .map(|(language, file_count)| ProjectLanguageStat { language, file_count })
        .collect::<Vec<_>>();
    languages.sort_by(|left, right| right.file_count.cmp(&left.file_count).then_with(|| left.language.cmp(&right.language)));
    languages.truncate(8);

    Ok(ProjectMetadataEnvelope {
        schema_version: 1,
        summary: ProjectMetadataSummary {
            total_file_count,
            source_file_count,
            markers,
            languages,
        },
    })
}

fn infer_language(path: &Path) -> Option<&'static str> {
    match path.extension().and_then(|value| value.to_str()).unwrap_or_default() {
        "rs" => Some("rust"),
        "cs" => Some("csharp"),
        "py" => Some("python"),
        "js" | "jsx" => Some("javascript"),
        "ts" | "tsx" => Some("typescript"),
        "go" => Some("go"),
        "java" => Some("java"),
        "kt" | "kts" => Some("kotlin"),
        "swift" => Some("swift"),
        "php" => Some("php"),
        "rb" => Some("ruby"),
        "cpp" | "cc" | "cxx" | "hpp" | "h" | "c" => Some("cpp"),
        "html" | "css" | "scss" => Some("web"),
        _ => None,
    }
}

#[cfg(test)]
mod tests {
    use super::build_project_metadata;
    use std::fs;
    use tempfile::tempdir;

    #[test]
    fn build_project_metadata_collects_markers_and_languages() {
        let temp_dir = tempdir().unwrap();
        fs::write(temp_dir.path().join("Cargo.toml"), "[package]\nname='demo'\n").unwrap();
        fs::create_dir_all(temp_dir.path().join("src")).unwrap();
        fs::write(temp_dir.path().join("src").join("main.rs"), "fn main() {}\n").unwrap();
        fs::write(temp_dir.path().join("src").join("lib.rs"), "pub fn demo() {}\n").unwrap();
        fs::write(temp_dir.path().join("notes.txt"), "ignore me\n").unwrap();

        let metadata = build_project_metadata(temp_dir.path()).unwrap();
        assert_eq!(metadata.schema_version, 1);
        assert_eq!(metadata.summary.total_file_count, 4);
        assert_eq!(metadata.summary.source_file_count, 2);
        assert!(metadata.summary.markers.contains(&"Cargo.toml".to_string()));
        assert_eq!(metadata.summary.languages[0].language, "rust");
        assert_eq!(metadata.summary.languages[0].file_count, 2);
    }
}