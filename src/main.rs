mod cli;
mod config;
mod git_context;
mod local_symbols;
mod local_tools;
mod models;
mod project_metadata;
mod server_client;

fn main() {
    if let Err(error) = cli::run(std::env::args()) {
        eprintln!("{error}");
        std::process::exit(1);
    }
}