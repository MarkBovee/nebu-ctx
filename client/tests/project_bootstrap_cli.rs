use std::process::Command;

fn run(bin: &str, args: &[&str], envs: &[(&str, &str)]) -> (i32, String, String) {
    let mut cmd = Command::new(bin);
    cmd.args(args);
    for (key, value) in envs {
        cmd.env(key, value);
    }
    let output = cmd.output().expect("spawn nebu-ctx");
    (
        output.status.code().unwrap_or(1),
        String::from_utf8_lossy(&output.stdout).to_string(),
        String::from_utf8_lossy(&output.stderr).to_string(),
    )
}

#[test]
fn project_bootstrap_preview_and_apply_json_work() {
    let _lock = nebu_ctx::core::data_dir::test_env_lock();
    let bin = env!("CARGO_BIN_EXE_nebu-ctx");
    let tmp = tempfile::tempdir().unwrap();
    let home = tmp.path().join("home");
    let data = tmp.path().join("data");
    let project = tmp.path().join("project");

    std::fs::create_dir_all(&home).unwrap();
    std::fs::create_dir_all(&data).unwrap();
    std::fs::create_dir_all(project.join("src")).unwrap();
    std::fs::create_dir_all(project.join("tests")).unwrap();
    std::fs::write(project.join("Cargo.toml"), "[package]\nname='demo'\n").unwrap();
    std::fs::write(project.join("src/main.rs"), "fn main() {}\n").unwrap();
    std::fs::write(project.join("Dockerfile"), "FROM scratch\n").unwrap();

    let home_str = home.to_string_lossy().to_string();
    let data_str = data.to_string_lossy().to_string();
    let project_str = project.to_string_lossy().to_string();
    std::env::set_var("NEBU_CTX_DATA_DIR", &data_str);
    std::env::set_var("NEBU_CTX_HOME", &home_str);
    let envs = [
        ("HOME", home_str.as_str()),
        ("NEBU_CTX_DATA_DIR", data_str.as_str()),
        ("SHELL", "/bin/bash"),
    ];

    let (code, stdout, _stderr) = run(
        bin,
        &["project-bootstrap", "preview", "--path", project_str.as_str(), "--json"],
        &envs,
    );
    assert_eq!(code, 0);
    let preview: serde_json::Value = serde_json::from_str(&stdout).unwrap();
    assert_eq!(preview["project_root"], serde_json::json!(project_str));
    assert!(preview["facts"].as_array().is_some_and(|facts| !facts.is_empty()));

    let (code, stdout, _stderr) = run(
        bin,
        &["project-bootstrap", "apply", "--path", project_str.as_str(), "--json"],
        &envs,
    );
    assert_eq!(code, 0);
    let report: serde_json::Value = serde_json::from_str(&stdout).unwrap();
    assert_eq!(
        report["stored"],
        serde_json::json!(report["facts"].as_array().unwrap().len())
    );

    let knowledge = nebu_ctx::core::knowledge::ProjectKnowledge::load(&project_str).unwrap();
    assert!(knowledge.facts.iter().any(|fact| fact.key == "stack"));
}

#[test]
fn project_bootstrap_help_mentions_preview_first_flow() {
    let bin = env!("CARGO_BIN_EXE_nebu-ctx");
    let (code, stdout, _stderr) = run(bin, &["--help"], &[]);
    assert_eq!(code, 0);
    assert!(stdout.contains("project-bootstrap [preview|apply]"));
    assert!(stdout.contains("Preview project map + candidate facts"));
}
