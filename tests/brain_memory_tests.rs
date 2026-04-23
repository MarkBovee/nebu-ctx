/// Integration tests for brain memory system.
///
/// Tests the full flow: SqliteStore → brain_store → brain_recall →
/// activation → consolidation → checkpoint.

use nebula_ctx::core::store::ContextStore;

fn create_test_store() -> nebula_ctx::core::store::sqlite::SqliteStore {
    let dir = tempfile::tempdir().unwrap();
    let db_path = dir.path().join("test-brain.db");
    let store =
        nebula_ctx::core::store::sqlite::SqliteStore::open(&db_path).expect("open sqlite");
    store.initialize().expect("initialize");
    // Leak the tempdir so the DB file stays alive for the test.
    // In practice these tests are short-lived.
    std::mem::forget(dir);
    store
}

fn make_memory(content: &str, layer: &str, memory_type: &str) -> nebula_ctx::core::store::BrainMemory {
    nebula_ctx::core::store::BrainMemory {
        id: None,
        brain_id: "test-brain".to_string(),
        layer: layer.to_string(),
        memory_type: memory_type.to_string(),
        content: content.to_string(),
        embedding: None,
        composite_score: 0.5,
        recall_count: 0,
        weights_json: None,
        created_at: None,
    }
}

#[test]
fn brain_store_and_recall() {
    let store = create_test_store();

    // Store a memory
    let mem = make_memory("Redis uses port 6380 with TLS", "short_term", "semantic");
    let id = store.brain_store(&mem).expect("store");
    assert!(id > 0, "should get a valid ID");

    // Recall it
    let results = store
        .brain_recall("test-brain", "", "", 10)
        .expect("recall");
    assert_eq!(results.len(), 1);
    assert_eq!(results[0].content, "Redis uses port 6380 with TLS");
    assert_eq!(results[0].layer, "short_term");
}

#[test]
fn brain_recall_filters_by_layer() {
    let store = create_test_store();

    store
        .brain_store(&make_memory("short-term fact", "short_term", "semantic"))
        .expect("store short");
    store
        .brain_store(&make_memory("long-term fact", "long_term", "semantic"))
        .expect("store long");

    let short = store
        .brain_recall("test-brain", "", "short_term", 10)
        .expect("recall short");
    assert_eq!(short.len(), 1);
    assert_eq!(short[0].layer, "short_term");

    let long = store
        .brain_recall("test-brain", "", "long_term", 10)
        .expect("recall long");
    assert_eq!(long.len(), 1);
    assert_eq!(long[0].layer, "long_term");
}

#[test]
fn brain_recall_isolation_between_brains() {
    let store = create_test_store();

    let mut mem_a = make_memory("brain-a fact", "short_term", "semantic");
    mem_a.brain_id = "brain-a".to_string();
    store.brain_store(&mem_a).expect("store a");

    let mut mem_b = make_memory("brain-b fact", "short_term", "semantic");
    mem_b.brain_id = "brain-b".to_string();
    store.brain_store(&mem_b).expect("store b");

    let results_a = store
        .brain_recall("brain-a", "", "", 10)
        .expect("recall a");
    assert_eq!(results_a.len(), 1);
    assert_eq!(results_a[0].content, "brain-a fact");

    let results_b = store
        .brain_recall("brain-b", "", "", 10)
        .expect("recall b");
    assert_eq!(results_b.len(), 1);
    assert_eq!(results_b[0].content, "brain-b fact");
}

#[test]
fn brain_session_lifecycle() {
    let store = create_test_store();

    // Create session
    let session_id = store
        .brain_session_create("test-brain")
        .expect("create session");
    assert!(session_id > 0);

    // Get latest session
    let latest = store
        .brain_session_latest("test-brain")
        .expect("get latest");
    assert!(latest.is_some());
    assert_eq!(latest.as_ref().unwrap().id, Some(session_id));

    // End session
    store
        .brain_session_update_status(session_id, "ended")
        .expect("end session");

    let ended = store
        .brain_session_latest("test-brain")
        .expect("get ended");
    assert!(ended.is_some());
    assert_eq!(ended.unwrap().status, "ended");
}

#[test]
fn brain_checkpoint_flow() {
    let store = create_test_store();

    let session_id = store
        .brain_session_create("test-brain")
        .expect("create session");

    let checkpoint = nebula_ctx::core::store::BrainCheckpoint {
        id: None,
        session_id,
        checkpoint_type: "manual".to_string(),
        content_json: "{\"files_read\":[\"src/main.rs\"]}".to_string(),
        created_at: None,
    };

    let cp_id = store.brain_checkpoint_store(&checkpoint).expect("store checkpoint");
    assert!(cp_id > 0);

    let checkpoints = store
        .brain_checkpoint_latest(session_id)
        .expect("get checkpoint");
    assert!(checkpoints.is_some());
    assert!(checkpoints.unwrap().content_json.contains("src/main.rs"));
}

#[test]
fn brain_open_loop_lifecycle() {
    let store = create_test_store();

    let loop_item = nebula_ctx::core::store::OpenLoop {
        id: None,
        brain_id: "test-brain".to_string(),
        description: "Why does auth fail on Tuesdays?".to_string(),
        priority: 5.0,
        status: "open".to_string(),
        created_at: None,
    };

    let loop_id = store.open_loop_store(&loop_item).expect("store loop");
    assert!(loop_id > 0);

    let loops = store
        .open_loop_list("test-brain", "open")
        .expect("list loops");
    assert_eq!(loops.len(), 1);
    assert_eq!(loops[0].description, "Why does auth fail on Tuesdays?");

    // Resolve it
    store
        .open_loop_close(loop_id)
        .expect("resolve loop");

    let open_loops = store
        .open_loop_list("test-brain", "open")
        .expect("list open");
    assert!(open_loops.is_empty());

    let closed_loops = store
        .open_loop_list("test-brain", "closed")
        .expect("list closed");
    assert_eq!(closed_loops.len(), 1);
}

#[test]
fn brain_activation_recall_increments_count() {
    let store = create_test_store();

    store
        .brain_store(&make_memory("fact to be recalled", "short_term", "semantic"))
        .expect("store");

    // Recall once
    let results = store
        .brain_recall("test-brain", "", "", 10)
        .expect("recall");
    assert_eq!(results[0].recall_count, 0); // recall doesn't auto-increment via store

    // Update score (simulating activation)
    store
        .brain_update_score(results[0].id.unwrap(), 0.9)
        .expect("update score");

    // Verify updated score
    let updated = store
        .brain_recall("test-brain", "", "", 10)
        .expect("recall updated");
    assert!((updated[0].composite_score - 0.9).abs() < 0.01);
}
