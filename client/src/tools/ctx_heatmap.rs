pub fn handle(action: &str, path: Option<&str>) -> String {
    let _ = (action, path);
    crate::cli::hosted_analytics_only_message("ctx_heatmap")
}
