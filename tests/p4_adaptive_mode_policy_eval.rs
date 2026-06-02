use serde_json::json;

#[tokio::test]
#[allow(clippy::await_holding_lock)]
async fn analytics_feedback_updates_adaptive_mode_policy_via_ctx() {
    let _g = nebula_ctx::core::data_dir::test_env_lock();
    let dir = tempfile::tempdir().expect("tempdir");
    let data_dir = dir.path().join("data");
    std::fs::create_dir_all(&data_dir).expect("create data dir");
    std::env::set_var("LEAN_CTX_DATA_DIR", &data_dir);
    assert_eq!(
        nebula_ctx::core::data_dir::nebula_ctx_data_dir().expect("data dir"),
        data_dir
    );

    let project = tempfile::tempdir().expect("project");
    let file = project.path().join("big.json");
    let payload = "{\"k\":\"v\"}\n".repeat(5000);
    std::fs::write(&file, payload).expect("write json");

    let engine = nebula_ctx::engine::ContextEngine::with_project_root(project.path());

    let _ = engine
        .call_tool_text(
            "ctx",
            Some(json!({"domain":"analytics","action":"feedback","format":"reset"})),
        )
        .await
        .expect("reset");

    // Generate real ctx_read calls so analytics feedback can attach ctx_read_modes.
    for _ in 0..3 {
        let _ = engine
            .call_tool_text(
                "ctx_read",
                Some(json!({"path": file.to_string_lossy().to_string(), "mode":"aggressive"})),
            )
            .await
            .expect("ctx_read aggressive");
    }

    let record_out = engine
        .call_tool_text(
            "ctx",
            Some(json!({
                "domain":"analytics",
                "action":"feedback",
                "format":"record",
                "agent_id":"test-agent",
                "llm_input_tokens":100,
                "llm_output_tokens":8000,
                "note":"output explosion"
            })),
        )
        .await
        .expect("record");
    assert!(
        record_out.contains("feedback recorded"),
        "record_out: {record_out}"
    );

    let status = engine
        .call_tool_text(
            "ctx",
            Some(json!({"domain":"analytics","action":"feedback","format":"status"})),
        )
        .await
        .expect("status");
    assert!(
        status.contains(data_dir.to_string_lossy().as_ref()),
        "status: {status}"
    );

    let policy_path = nebula_ctx::core::data_dir::nebula_ctx_data_dir()
        .expect("data dir2")
        .join("adaptive_mode_policy.json");
    let raw = std::fs::read_to_string(&policy_path).expect("policy exists");
    let v: serde_json::Value = serde_json::from_str(&raw).expect("policy json");
    let p = v["global"]["modes"]["aggressive"]["ema_badness"]
        .as_f64()
        .unwrap_or(0.0);
    assert!(p > 0.0, "expected penalty > 0, got {p} ({raw})");

    std::env::remove_var("LEAN_CTX_DATA_DIR");
}
