use nebu_ctx_client::cli;

fn main() {
    if let Err(error) = cli::run(std::env::args()) {
        eprintln!("{error}");
        std::process::exit(1);
    }
}