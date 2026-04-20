use std::path::Path;

use lean_ctx::core::knowledge::ProjectKnowledge;

#[test]
fn ctx_knowledge_recall_is_budgeted_and_deterministic() {
    let _g = lean_ctx::core::data_dir::test_env_lock();
    let tmp = tempfile::tempdir().expect("tempdir");
    let data_dir = tmp.path().join("data");
    std::fs::create_dir_all(&data_dir).expect("create data dir");

    std::env::set_var("LEAN_CTX_DATA_DIR", data_dir.to_string_lossy().to_string());

    let project_root = tmp.path().join("proj");
    std::fs::create_dir_all(&project_root).expect("create project root");
    let project_root_str = project_root.to_string_lossy().to_string();

    let mut knowledge = ProjectKnowledge::load_or_create(&project_root_str);
    for i in 0..50 {
        knowledge.remember(
            "architecture",
            &format!("k{i:02}"),
            &format!("v{i:02}"),
            "s1",
            0.8,
        );
    }
    knowledge.save().expect("save knowledge");

    let out1 = lean_ctx::tools::ctx_knowledge::handle(
        &project_root_str,
        "recall",
        Some("architecture"),
        None,
        None,
        None,
        "s1",
        None,
        None,
        None,
    );
    let out2 = lean_ctx::tools::ctx_knowledge::handle(
        &project_root_str,
        "recall",
        Some("architecture"),
        None,
        None,
        None,
        "s1",
        None,
        None,
        None,
    );

    assert_eq!(out1, out2, "recall output must be deterministic");
    assert!(
        out1.contains("showing 10/50"),
        "recall header must indicate truncation"
    );

    let fact_lines = out1
        .lines()
        .filter(|l| l.starts_with("  [architecture/"))
        .count();
    assert!(fact_lines <= 10, "must not exceed recall budget");

    std::env::remove_var("LEAN_CTX_DATA_DIR");
}

#[test]
fn ctx_knowledge_export_is_file_backed_not_json_stdout() {
    let _g = lean_ctx::core::data_dir::test_env_lock();
    let tmp = tempfile::tempdir().expect("tempdir");
    let data_dir = tmp.path().join("data");
    std::fs::create_dir_all(&data_dir).expect("create data dir");

    std::env::set_var("LEAN_CTX_DATA_DIR", data_dir.to_string_lossy().to_string());

    let project_root = tmp.path().join("proj");
    std::fs::create_dir_all(&project_root).expect("create project root");
    let project_root_str = project_root.to_string_lossy().to_string();

    let mut knowledge = ProjectKnowledge::load_or_create(&project_root_str);
    knowledge.remember("arch", "db", "MySQL", "s1", 0.8);
    knowledge.save().expect("save knowledge");

    let out = lean_ctx::tools::ctx_knowledge::handle(
        &project_root_str,
        "export",
        None,
        None,
        None,
        None,
        "s1",
        None,
        None,
        None,
    );

    assert!(
        out.starts_with("Export saved: "),
        "export must return a compact confirmation"
    );
    assert!(
        !out.trim_start().starts_with('{'),
        "export must not print full JSON to stdout"
    );

    let path_str = out
        .strip_prefix("Export saved: ")
        .and_then(|s| s.split_whitespace().next())
        .expect("extract export path");
    assert!(Path::new(path_str).exists(), "export file must exist");

    std::env::remove_var("LEAN_CTX_DATA_DIR");
}
