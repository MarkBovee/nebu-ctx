use serde::{Deserialize, Serialize};

#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash, Serialize, Deserialize)]
pub enum TaskCategory {
    Coding,
    Debugging,
    Refactoring,
    Testing,
    Exploration,
    Planning,
    Delegation,
    Git,
    BuildDeploy,
    Knowledge,
    Architecture,
    Review,
    General,
}

impl TaskCategory {
    pub fn label(&self) -> &'static str {
        match self {
            TaskCategory::Coding => "Coding",
            TaskCategory::Debugging => "Debugging",
            TaskCategory::Refactoring => "Refactoring",
            TaskCategory::Testing => "Testing",
            TaskCategory::Exploration => "Exploration",
            TaskCategory::Planning => "Planning",
            TaskCategory::Delegation => "Delegation",
            TaskCategory::Git => "Git",
            TaskCategory::BuildDeploy => "Build/Deploy",
            TaskCategory::Knowledge => "Knowledge",
            TaskCategory::Architecture => "Architecture",
            TaskCategory::Review => "Review",
            TaskCategory::General => "General",
        }
    }
}

pub struct TaskClassifier;

impl TaskClassifier {
    pub fn classify_tool(tool_name: &str) -> TaskCategory {
        let t = normalize(tool_name);
        match t.as_str() {
            "ctx_read" | "ctx_tree" | "ctx_search" => TaskCategory::Exploration,
            "ctx_shell" | "ctx_execute" => TaskCategory::Debugging,
            "ctx" => TaskCategory::Planning,
            _ => TaskCategory::General,
        }
    }

    pub fn classify_command_key(cmd_key: &str) -> TaskCategory {
        let k = normalize(cmd_key);
        if k.is_empty() {
            return TaskCategory::General;
        }
        if k.starts_with("git ") || k == "git" {
            return TaskCategory::Git;
        }

        if k.starts_with("cargo ") {
            let sub = k.trim_start_matches("cargo ").trim();
            if matches!(sub, "test" | "nextest" | "llvm-cov" | "tarpaulin") {
                return TaskCategory::Testing;
            }
            if matches!(sub, "build" | "check" | "clippy" | "fmt" | "run" | "doc") {
                return TaskCategory::BuildDeploy;
            }
            return TaskCategory::BuildDeploy;
        }

        if k.contains("test") || k.contains("pytest") || k.contains("jest") || k.contains("vitest")
        {
            return TaskCategory::Testing;
        }
        if k.contains("build")
            || k.contains("deploy")
            || k.contains("docker")
            || k.contains("compose")
            || k.contains("kubectl")
            || k.contains("helm")
            || k.contains("terraform")
        {
            return TaskCategory::BuildDeploy;
        }
        if k.contains("lint") || k.contains("clippy") || k.contains("fmt") || k.contains("format") {
            return TaskCategory::BuildDeploy;
        }
        if k.contains("grep") || k.contains("rg") || k.contains("ripgrep") {
            return TaskCategory::Exploration;
        }

        TaskCategory::General
    }
}

fn normalize(s: &str) -> String {
    s.trim().to_lowercase()
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn classify_git() {
        assert_eq!(
            TaskClassifier::classify_command_key("git status"),
            TaskCategory::Git
        );
    }

    #[test]
    fn classify_tools() {
        assert_eq!(TaskClassifier::classify_tool("ctx"), TaskCategory::Planning);
    }
}
