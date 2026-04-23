pub fn handle(action: &str, agent_id: Option<&str>, limit: Option<usize>) -> String {
    let _ = (action, agent_id, limit);
    crate::cli::cloud_analytics_only_message("ctx_cost")
}
