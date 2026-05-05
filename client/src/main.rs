fn main() {
    std::panic::set_hook(Box::new(|info| {
        eprintln!("nebu-ctx: unexpected error (your command was not affected)");
        eprintln!("  Disable temporarily: nebu-ctx-off");
        eprintln!("  Full uninstall:      nebu-ctx uninstall");
        if let Some(msg) = info.payload().downcast_ref::<&str>() {
            eprintln!("  Details: {msg}");
        } else if let Some(msg) = info.payload().downcast_ref::<String>() {
            eprintln!("  Details: {msg}");
        }
        if let Some(loc) = info.location() {
            eprintln!("  Location: {}:{}", loc.file(), loc.line());
        }
    }));

    nebu_ctx::cli::run();
}
