use std::io::{self, IsTerminal, Write};
use crate::core::theme::Color;

const LOGO: [&str; 6] = [
    r"  ███╗   ██╗███████╗██████╗ ██╗   ██╗     ██████╗████████╗██╗  ██╗",
    r"  ████╗  ██║██╔════╝██╔══██╗██║   ██║    ██╔════╝╚══██╔══╝╚██╗██╔╝",
    r"  ██╔██╗ ██║█████╗  ██████╔╝██║   ██║    ██║        ██║    ╚███╔╝ ",
    r"  ██║╚██╗██║██╔══╝  ██╔══██╗██║   ██║    ██║        ██║    ██╔██╗ ",
    r"  ██║ ╚████║███████╗██████╔╝╚██████╔╝    ╚██████╗   ██║   ██╔╝ ██╗",
    r"  ╚═╝  ╚═══╝╚══════╝╚═════╝  ╚═════╝      ╚═════╝   ╚═╝   ╚═╝  ╚═╝",
];

const TAGLINE: &str = "Context Runtime for AI Agents";

/// Nebula icon — 13 rows, single-column Unicode characters only.
/// Color is applied in `nebula_char_color` based on character "density" (outermost = dimmest).
const NEBULA_ICON: [&str; 13] = [
    r"             ·:·             ",
    r"        .·:=+*+++*+=:·.      ",
    r"      .:+*+=-·   ·-=+*+:.   ",
    r"    .:+*=-  .:=+++=:.  -=*+:.",
    r"   :+*=-  =+██▓░  ░▓██=  =*+:",
    r"  :+*=  =██▓░        ░▓██=  =*+:",
    r"  :+*=  ██░   +  +   ░██  =*+: ",
    r"  :+*=  =██▓░        ░▓██=  =*+:",
    r"   :+*=-  =+██▓░  ░▓██=  =*+:",
    r"    .:+*=-  .:=+++=:.  -=*+:.",
    r"      .:+*+=-·   ·-=+*+:.   ",
    r"        .·:=+*+++*+=:·.      ",
    r"             ·:·             ",
];

/// Assign a theme-blended color to a nebula character based on its visual "density".
fn nebula_char_color(ch: char, t: &crate::core::theme::Theme) -> Color {
    let white = Color::Hex("#FFFFFF".to_string());
    match ch {
        '·' | '.' => t.muted.lerp(&t.secondary, 0.3),
        ':' | '-' => t.secondary.lerp(&t.primary, 0.3),
        '=' => t.secondary.lerp(&t.primary, 0.6),
        '+' => t.primary.lerp(&t.accent, 0.5),
        '*' => t.accent.lerp(&white, 0.25),
        '█' => t.accent.lerp(&white, 0.05),
        '▓' => t.accent.lerp(&white, 0.35),
        '░' => t.accent.lerp(&white, 0.6),
        _ => t.muted.clone(),
    }
}

/// Render the nebula icon with NEBU CTX text to its right (neofetch-style).
/// Called at the end of `nebu-ctx setup`.
pub fn print_nebu_splash() {
    let cfg = crate::core::config::Config::load();
    let t = crate::core::theme::load_theme(&cfg.theme);

    if crate::core::theme::no_color() || !io::stdout().is_terminal() {
        print_logo_plain();
        return;
    }

    let max_icon_width = NEBULA_ICON.iter().map(|l| l.chars().count()).max().unwrap_or(34);
    let gap = 4usize;

    // LOGO text lines start at icon row 3, tagline at icon row 3 + LOGO.len() + 1
    let text_start = 3usize;
    let tagline_row = text_start + LOGO.len() + 1;

    println!();

    for (icon_row, icon_line) in NEBULA_ICON.iter().enumerate() {
        // Colored icon segment
        let mut icon_buf = String::new();
        for ch in icon_line.chars() {
            if ch == ' ' {
                icon_buf.push(' ');
            } else {
                let color = nebula_char_color(ch, &t);
                icon_buf.push_str(&color.fg());
                icon_buf.push(ch);
                icon_buf.push_str("\x1b[0m");
            }
        }
        // Pad to uniform visual width + gap
        let vis_len = icon_line.chars().count();
        let padding = " ".repeat(max_icon_width - vis_len + gap);

        // Colored text segment (LOGO or tagline)
        let text_buf: String = if icon_row >= text_start && icon_row < text_start + LOGO.len() {
            let logo_line = LOGO[icon_row - text_start];
            let chars: Vec<char> = logo_line.chars().collect();
            let n = chars.len().max(1);
            let mut buf = String::new();
            for (j, &ch) in chars.iter().enumerate() {
                if ch == ' ' {
                    buf.push(' ');
                } else {
                    let blend = ((j as f64 / (n - 1) as f64)
                        + (icon_row - text_start) as f64 / (LOGO.len() - 1).max(1) as f64 * 0.3)
                        .min(1.0);
                    let c = t.primary.lerp(&t.secondary, blend);
                    buf.push_str(&c.fg());
                    buf.push(ch);
                    buf.push_str("\x1b[0m");
                }
            }
            buf
        } else if icon_row == tagline_row {
            format!("{}  {TAGLINE}\x1b[0m", t.muted.fg())
        } else {
            String::new()
        };

        println!("{icon_buf}{padding}{text_buf}");
    }

    println!();
}

pub fn print_logo_animated() {
    let cfg = crate::core::config::Config::load();
    let t = crate::core::theme::load_theme(&cfg.theme);
    print_logo_animated_themed(&t);
}

pub fn print_logo_animated_themed(t: &crate::core::theme::Theme) {
    if crate::core::theme::no_color() {
        print_logo_plain();
        return;
    }
    if !io::stdout().is_terminal() {
        print_logo_themed_static(t);
        return;
    }

    let mut stdout = io::stdout();
    let frames = 28;
    let frame_ms = 45;
    let top_padding = 2;

    let _ = writeln!(stdout);
    let _ = writeln!(stdout);

    for frame in 0..frames {
        if frame > 0 {
            print!("\x1b[{}A", LOGO.len() + 2 + top_padding);
            for _ in 0..top_padding {
                let _ = writeln!(stdout);
            }
        }

        let wave_offset = frame as f64 / frames as f64;

        for (i, line) in LOGO.iter().enumerate() {
            let chars: Vec<char> = line.chars().collect();
            let max_j = chars.len().max(1) as f64;
            let mut buf = String::with_capacity(chars.len() * 20);

            for (j, ch) in chars.iter().enumerate() {
                if *ch == ' ' {
                    buf.push(' ');
                    continue;
                }
                let pos = j as f64 / max_j + i as f64 * 0.15;
                let blend = ((pos + wave_offset * 2.0) * std::f64::consts::PI)
                    .sin()
                    .mul_add(0.5, 0.5);
                let c = t.primary.lerp(&t.secondary, blend);
                buf.push_str(&c.fg());
                buf.push(*ch);
            }
            buf.push_str("\x1b[0m");
            let _ = writeln!(stdout, "{buf}");
        }

        let tag_blend = ((wave_offset * 2.0 + 1.0) * std::f64::consts::PI)
            .sin()
            .mul_add(0.5, 0.5);
        let tag_color = t.muted.lerp(&t.accent, tag_blend * 0.5);
        let _ = writeln!(stdout, "{}             {TAGLINE}\x1b[0m", tag_color.fg());
        let _ = writeln!(stdout);

        let _ = stdout.flush();
        std::thread::sleep(std::time::Duration::from_millis(frame_ms));
    }

    print!("\x1b[{}A", LOGO.len() + 2 + top_padding);
    print_logo_themed_static(t);
}

pub fn print_logo_static() {
    let cfg = crate::core::config::Config::load();
    let t = crate::core::theme::load_theme(&cfg.theme);
    print_logo_themed_static(&t);
}

fn print_logo_themed_static(t: &crate::core::theme::Theme) {
    if crate::core::theme::no_color() {
        print_logo_plain();
        return;
    }
    let mut stdout = io::stdout();

    let _ = writeln!(stdout);
    let _ = writeln!(stdout);

    for (i, line) in LOGO.iter().enumerate() {
        let chars: Vec<char> = line.chars().collect();
        let mut buf = String::with_capacity(chars.len() * 20);

        for (j, ch) in chars.iter().enumerate() {
            if *ch == ' ' {
                buf.push(' ');
                continue;
            }
            let progress = if chars.len() > 1 {
                j as f64 / (chars.len() - 1) as f64
            } else {
                0.5
            };
            let row_t = i as f64 / (LOGO.len() - 1).max(1) as f64;
            let blend = (progress + row_t * 0.3).min(1.0);
            let c = t.primary.lerp(&t.secondary, blend);
            buf.push_str(&c.fg());
            buf.push(*ch);
        }
        buf.push_str("\x1b[0m");
        let _ = writeln!(stdout, "{buf}");
    }

    let _ = writeln!(stdout, "{}             {TAGLINE}\x1b[0m", t.muted.fg());
    let _ = writeln!(stdout);
    let _ = stdout.flush();
}

fn print_logo_plain() {
    println!();
    println!();
    for line in &LOGO {
        println!("{line}");
    }
    println!("             {TAGLINE}");
    println!();
}

pub fn print_command_box() {
    use crate::core::theme;
    let cfg = crate::core::config::Config::load();
    let t = theme::load_theme(&cfg.theme);
    let d = theme::dim();
    let b = theme::bold();
    let r = theme::rst();
    let cmd = t.accent.fg();
    let ok = t.success.fg();
    let m = t.muted.fg();

    println!("  {d}┌─────────────────────────────────────────────────────────┐{r}");
    println!(
        "  {d}│{r}  {cmd}{b}nebu-ctx connect{r}     {m}Connect to NebuCtx host{r}           {d}│{r}"
    );
    println!(
        "  {d}│{r}  {cmd}{b}nebu-ctx status{r}      {m}Connection & install status{r}       {d}│{r}"
    );
    println!(
        "  {d}│{r}  {cmd}{b}nebu-ctx gain{r}        {m}View compression savings{r}          {d}│{r}"
    );
    println!(
        "  {d}│{r}  {cmd}{b}nebu-ctx benchmark{r}   {m}Test compression quality{r}          {d}│{r}"
    );
    println!(
        "  {d}│{r}  {cmd}{b}nebu-ctx config{r}      {m}Edit settings{r}                     {d}│{r}"
    );
    println!(
        "  {d}│{r}  {cmd}{b}nebu-ctx doctor{r}      {m}Verify installation{r}               {d}│{r}"
    );
    println!(
        "  {d}│{r}  {cmd}{b}nebu-ctx off{r} / {cmd}{b}on{r}    {m}Toggle compression{r}                {d}│{r}"
    );
    println!(
        "  {d}│{r}  {cmd}{b}nebu-ctx report-issue{r} {m}Report a bug (auto-diagnostics){r}  {d}│{r}"
    );
    println!(
        "  {d}│{r}  {cmd}{b}nebu-ctx uninstall{r}   {m}Clean removal{r}                     {d}│{r}"
    );
    println!("  {d}└─────────────────────────────────────────────────────────┘{r}");
    println!("  {ok}Ready!{r} Your next AI command will be automatically optimized.");
    println!();
}

pub fn print_step_header(step: u8, total: u8, title: &str) {
    let dim = "\x1b[2m";
    let bold = "\x1b[1m";
    let cyan = "\x1b[36m";
    let rst = "\x1b[0m";
    println!();
    println!("  {cyan}{bold}[{step}/{total}]{rst} {bold}{title}{rst}");
    println!("  {dim}─────────────────────────────────────────────────────{rst}");
}

pub fn print_status_ok(msg: &str) {
    println!("  \x1b[32m✓\x1b[0m {msg}");
}

pub fn print_status_skip(msg: &str) {
    println!("  \x1b[2m○\x1b[0m \x1b[2m{msg}\x1b[0m");
}

pub fn print_status_new(msg: &str) {
    println!("  \x1b[1;32m✓\x1b[0m \x1b[1m{msg}\x1b[0m");
}

pub fn print_status_warn(msg: &str) {
    println!("  \x1b[33m⚠\x1b[0m {msg}");
}

pub fn spinner_tick(msg: &str, frame: usize) {
    let frames = ['⠋', '⠙', '⠹', '⠸', '⠼', '⠴', '⠦', '⠧', '⠇', '⠏'];
    let ch = frames[frame % frames.len()];
    print!("\r  \x1b[36m{ch}\x1b[0m {msg}");
    let _ = io::stdout().flush();
}

pub fn spinner_done(msg: &str) {
    print!("\r  \x1b[32m✓\x1b[0m {msg}\x1b[K\n");
    let _ = io::stdout().flush();
}

pub fn print_setup_header() {
    let dim = "\x1b[2m";
    let bold = "\x1b[1m";
    let green = "\x1b[32m";
    let rst = "\x1b[0m";
    println!();
    println!("  {dim}╭──────────────────────────────────────────╮{rst}");
    println!(
        "  {dim}│{rst}  {green}{bold}◆ nebu-ctx setup{rst}                         {dim}│{rst}"
    );
    println!("  {dim}│{rst}  {dim}Configuring your development environment{rst} {dim}│{rst}");
    println!("  {dim}╰──────────────────────────────────────────╯{rst}");
    println!();
}
