use crate::core::bug_memory::BugMemoryStore;

pub fn reminders_for_task(project_root: &str, task: &str) -> Vec<String> {
    let store = BugMemoryStore::load(project_root);
    store.reminders_for_task(task)
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn reminders_budgeted() {
        let _lock = crate::core::data_dir::test_env_lock();
        let tmp = tempfile::tempdir().expect("tempdir");
        std::env::set_var(
            "NEBU_CTX_DATA_DIR",
            tmp.path().to_string_lossy().to_string(),
        );

        let project_root = tmp.path().join("proj");
        std::fs::create_dir_all(&project_root).expect("mkdir");
        let project_root_str = project_root.to_string_lossy().to_string();

        let mut store = BugMemoryStore::load(&project_root_str);
        for i in 0..10 {
            store.record_failure(
                "cargo build",
                101,
                &format!("error[E050{i}]: borrow checker failure"),
                crate::core::bug_memory::BugMemorySource::Shell,
            );
        }

        store.save(&project_root_str).expect("save");

        let reminders = reminders_for_task(&project_root_str, "fix cargo build error E0502 borrow");
        assert!(reminders.len() <= crate::core::budgets::PROSPECTIVE_REMINDERS_LIMIT);

        std::env::remove_var("NEBU_CTX_DATA_DIR");
    }
}
