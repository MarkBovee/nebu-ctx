const MAX_METADATA_LEN: usize = 200;

use regex::Regex;

pub fn neutralize_metadata(input: &str) -> String {
    let mut out = String::with_capacity(input.len().min(MAX_METADATA_LEN));
    let mut count = 0usize;
    for ch in input.chars() {
        if count >= MAX_METADATA_LEN {
            out.push('…');
            break;
        }
        if (ch as u32) < 0x20 && ch != '\n' && ch != '\t' && ch != '\r' {
            continue;
        }
        match ch {
            '<' => out.push('‹'),
            '>' => out.push('›'),
            '`' => out.push('\''),
            _ => out.push(ch),
        }
        count += 1;
    }
    out
}

pub fn neutralize_shell_content(input: &str) -> String {
    let mut out = String::with_capacity(input.len());
    let mut i = 0;
    let chars: Vec<char> = input.chars().collect();
    while i < chars.len() {
        let ch = chars[i];
        if (ch as u32) < 0x20 && ch != '\n' && ch != '\t' && ch != '\r' {
            i += 1;
            continue;
        }
        out.push(ch);
        i += 1;
    }
    out
}

pub fn telemetry_command_preview(command: &str) -> Option<String> {
    let sanitized = neutralize_metadata(command);
    let collapsed = sanitized.split_whitespace().collect::<Vec<_>>().join(" ");
    if collapsed.is_empty() {
        return None;
    }

    let bearer_regex = Regex::new(r#"(?i)bearer\s+[^\s"']+"#).unwrap();
    let auth_regex = Regex::new(r#"(?i)authorization:\s*[^"']+"#).unwrap();
    let token_flag_regex = Regex::new(r#"(?i)--(token|password)\s+[^\s"']+"#).unwrap();

    let redacted = token_flag_regex.replace_all(&collapsed, "--$1 [redacted]");
    let redacted = bearer_regex.replace_all(&redacted, "Bearer [redacted]");
    let redacted = auth_regex.replace_all(&redacted, "Authorization:[redacted]");

    Some(redacted.into_owned())
}

fn to_hex(bytes: &[u8]) -> String {
    const HEX: &[u8; 16] = b"0123456789abcdef";
    let mut out = String::with_capacity(bytes.len() * 2);
    for &b in bytes {
        out.push(HEX[(b >> 4) as usize] as char);
        out.push(HEX[(b & 0x0f) as usize] as char);
    }
    out
}

fn safe_label(label: &str) -> String {
    let mut out = String::new();
    for ch in label.chars() {
        if ch.is_ascii_alphanumeric() {
            out.push(ch.to_ascii_uppercase());
        } else if ch == '_' || ch == '-' {
            out.push('_');
        }
    }
    if out.is_empty() {
        "BLOCK".to_string()
    } else {
        out
    }
}

pub fn fence_content(label: &str, content: &str) -> String {
    let label = safe_label(label);
    let mut bytes = [0u8; 16];
    let _ = getrandom::fill(&mut bytes);
    let token = to_hex(&bytes);
    let marker = format!("LCTX_{label}_{token}");
    format!("‹‹‹{marker}›››\n{content}\n‹‹‹{marker}›››")
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn neutralize_replaces_angle_and_backticks() {
        let s = "<tag>`code`</tag>";
        let out = neutralize_metadata(s);
        assert!(out.contains('‹'));
        assert!(out.contains('›'));
        assert!(!out.contains('`'));
    }

    #[test]
    fn fence_wraps_symmetrically() {
        let out = fence_content("knowledge", "hello");
        let lines: Vec<&str> = out.lines().collect();
        assert!(lines.len() >= 3);
        assert_eq!(lines[0], lines[lines.len() - 1]);
    }

    #[test]
    fn telemetry_preview_redacts_bearer_values() {
        let preview = telemetry_command_preview(
            r#"curl -H "Authorization: Bearer secret-token" https://api.example.com"#,
        )
        .unwrap();
        assert!(!preview.contains("secret-token"));
        assert!(preview.contains("Authorization:[redacted]"));
    }

    #[test]
    fn telemetry_preview_redacts_token_flags() {
        let preview = telemetry_command_preview(
            "dotnet nuget push --token super-secret --password top-secret",
        )
        .unwrap();
        assert!(!preview.contains("super-secret"));
        assert!(!preview.contains("top-secret"));
        assert!(preview.contains("--token [redacted]"));
        assert!(preview.contains("--password [redacted]"));
    }
}
