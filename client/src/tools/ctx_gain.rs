pub fn handle(
    action: &str,
    period: Option<&str>,
    model: Option<&str>,
    limit: Option<usize>,
) -> String {
    let _ = (action, period, model, limit);
    crate::cli::cloud_analytics_only_message("ctx_gain")
}
