use std::io::Write;

pub fn cmd_db(args: &[String]) {
    let action = args.first().map(|s| s.as_str()).unwrap_or("status");

    match action {
        "connect" => cmd_db_connect(),
        "init" => cmd_db_init(),
        "status" => cmd_db_status(),
        "test" => cmd_db_test(),
        _ => {
            println!("Usage: nebula-ctx db <command>");
            println!("  connect   — Connect to a database (guided setup)");
            println!("  init     — Initialize database schema");
            println!("  status   — Show database connection status");
            println!("  test     — Test database connection");
        }
    }
}

fn cmd_db_status() {
    let store_type = std::env::var("NEBULA_STORE").unwrap_or_else(|_| "sqlite".to_string());

    println!("Database Status:");
    println!("  Store: {}", store_type.to_uppercase());

    if store_type == "postgres" {
        if let Ok(url) = std::env::var("DATABASE_URL") {
            let masked = mask_database_url(&url);
            println!("  Database URL: {}", masked);

            #[cfg(feature = "cloud-server")]
            {
                println!("  Testing connection...");
                let rt = tokio::runtime::Runtime::new();
                match rt {
                    Ok(mut runtime) => {
                        let result = runtime.block_on(async {
                            crate::core::store::postgres::PostgresStore::open(&url).await
                        });
                        match result {
                            Ok(_) => println!("  Status: Connected"),
                            Err(e) => println!("  Status: Error - {}", e),
                        }
                    }
                    Err(e) => println!("  Status: Runtime error - {}", e),
                }
            }
            #[cfg(not(feature = "cloud-server"))]
            {
                println!("  Rebuild with --features cloud-server to test connection");
            }
        } else {
            println!("  Warning: DATABASE_URL not set");
            println!("  Set it with: nebula-ctx db connect");
        }
    } else {
        // SQLite - show path
        if let Ok(data_dir) = crate::core::data_dir::nebula_ctx_data_dir() {
            let db_path = data_dir.join("nebula-ctx.db");
            if db_path.exists() {
                println!("  Database: {}", db_path.display());
                if let Ok(metadata) = std::fs::metadata(&db_path) {
                    println!("  Size: {} bytes", metadata.len());
                }
            } else {
                println!("  Database: {} (not yet created)", db_path.display());
            }
        }
    }
}

fn cmd_db_connect() {
    println!("=== Database Connection Setup ===\n");

    let store_type = std::env::var("NEBULA_STORE").unwrap_or_else(|_| "sqlite".to_string());
    if store_type == "postgres" {
        if std::env::var("DATABASE_URL").is_ok() {
            println!("Already connected to PostgreSQL.");
            println!("To change: nebula-ctx db connect\n");
            cmd_db_status();
            return;
        }
    }

    println!("This will help you connect to your PostgreSQL database.");
    println!();

    // Prompt for connection details
    let host = prompt_input("PostgreSQL host", "localhost").unwrap_or_else(|_| "localhost".to_string());
    let port = prompt_input("PostgreSQL port", "5432").unwrap_or_else(|_| "5432".to_string());
    let dbname = prompt_input("Database name", "nebula").unwrap_or_else(|_| "nebula".to_string());
    let user = prompt_input("Database user", "postgres").unwrap_or_else(|_| "postgres".to_string());
    let password = rpassword::prompt_password("Database password: ").unwrap_or_default();
    let use_ssl = prompt_input("Use SSL? (yes/no)", "no").unwrap_or_else(|_| "no".to_string());

    // Build connection URL
    let ssl_mode = if use_ssl.to_lowercase().starts_with('y') {
        "require"
    } else {
        "disable"
    };

    let database_url = if password.is_empty() {
        format!(
            "postgres://{}@{}:{}/{}?sslmode={}",
            user, host, port, dbname, ssl_mode
        )
    } else {
        format!(
            "postgres://{}:{}@{}:{}/{}?sslmode={}",
            user, password, host, port, dbname, ssl_mode
        )
    };

    println!();
    println!("Testing connection...");

    #[cfg(feature = "cloud-server")]
    {
        let rt = tokio::runtime::Runtime::new();
        let test_result = match rt {
            Ok(mut runtime) => runtime.block_on(async {
                crate::core::store::postgres::PostgresStore::open(&database_url).await
            }),
            Err(e) => Err(anyhow::anyhow!("Failed to create runtime: {}", e)),
        };

        match test_result {
            Ok(_) => {
                println!("✓ Connection successful!\n");

                // Save to config
                if let Err(e) = save_db_config(&database_url) {
                    eprintln!("Warning: Could not save config: {}", e);
                    println!("You can still use the database by setting environment variables:");
                    println!("  export NEBULA_STORE=postgres");
                    println!("  export DATABASE_URL={}", mask_database_url(&database_url));
                } else {
                    println!("Configuration saved.");
                }

                println!();
                println!("To enable PostgreSQL, run:");
                println!("  source ~/.nebula-ctx/db.env");
            }
            Err(e) => {
                eprintln!("✗ Connection failed: {}", e);
                println!();
                println!("Try again with: nebula-ctx db connect");
            }
        }
    }
    #[cfg(not(feature = "cloud-server"))]
    {
        eprintln!("Postgres support requires building with --features cloud-server");
        eprintln!("Run: cargo build --release --features cloud-server");
    }
}

fn cmd_db_init() {
    println!("Initializing database schema...");

    let store_type = std::env::var("NEBULA_STORE").unwrap_or_else(|_| "sqlite".to_string());

    if store_type == "postgres" {
        #[cfg(feature = "cloud-server")]
        {
            let url = match std::env::var("DATABASE_URL") {
                Ok(u) => u,
                Err(_) => {
                    eprintln!("DATABASE_URL not set. Run: nebula-ctx db connect");
                    std::process::exit(1);
                }
            };

            let rt = tokio::runtime::Runtime::new();
            match rt {
                Ok(mut runtime) => {
                    let result = runtime.block_on(async {
                        crate::core::store::postgres::PostgresStore::open(&url).await
                    });
                    match result {
                        Ok(_) => {
                            println!("✓ Schema initialized successfully.");
                        }
                        Err(e) => {
                            eprintln!("Failed to connect: {}", e);
                            std::process::exit(1);
                        }
                    }
                }
                Err(e) => {
                    eprintln!("Runtime error: {}", e);
                    std::process::exit(1);
                }
            }
        }
        #[cfg(not(feature = "cloud-server"))]
        {
            eprintln!("Postgres requires building with --features cloud-server");
            std::process::exit(1);
        }
    } else {
        // SQLite - just verify path
        let data_dir = match crate::core::data_dir::nebula_ctx_data_dir() {
            Ok(d) => d,
            Err(e) => {
                eprintln!("Failed to get data dir: {}", e);
                std::process::exit(1);
            }
        };
        let db_path = data_dir.join("nebula-ctx.db");
        println!("SQLite database path: {}", db_path.display());
        println!("✓ Schema ready (SQLite auto-initializes on first use).");
    }
}

fn cmd_db_test() {
    println!("Testing database connection...\n");

    let store_type = std::env::var("NEBULA_STORE").unwrap_or_else(|_| "sqlite".to_string());
    println!("Store type: {}", store_type);

    if store_type == "postgres" {
        #[cfg(feature = "cloud-server")]
        {
            let url = match std::env::var("DATABASE_URL") {
                Ok(u) => u,
                Err(_) => {
                    eprintln!("DATABASE_URL not set");
                    std::process::exit(1);
                }
            };

            let rt = tokio::runtime::Runtime::new();
            match rt {
                Ok(mut runtime) => {
                    let result = runtime.block_on(async {
                        crate::core::store::postgres::PostgresStore::open(&url).await
                    });
                    match result {
                        Ok(_) => println!("✓ Connection successful!"),
                        Err(e) => {
                            eprintln!("✗ Connection failed: {}", e);
                            std::process::exit(1);
                        }
                    }
                }
                Err(e) => {
                    eprintln!("✗ Runtime error: {}", e);
                    std::process::exit(1);
                }
            }
        }
        #[cfg(not(feature = "cloud-server"))]
        {
            eprintln!("Postgres requires building with --features cloud-server");
            std::process::exit(1);
        }
    } else {
        // SQLite - just check if path exists
        if let Ok(data_dir) = crate::core::data_dir::nebula_ctx_data_dir() {
            let db_path = data_dir.join("nebula-ctx.db");
            if db_path.exists() {
                println!("✓ SQLite database found at {}", db_path.display());
            } else {
                println!("SQLite database not found (will be created on first use)");
            }
        }
    }
}

fn prompt_input(prompt: &str, default: &str) -> Result<String, std::io::Error> {
    print!("{} [{}]: ", prompt, default);
    std::io::stdout().flush()?;

    let mut input = String::new();
    std::io::stdin().read_line(&mut input)?;

    let input = input.trim().to_string();
    if input.is_empty() {
        Ok(default.to_string())
    } else {
        Ok(input)
    }
}

fn mask_database_url(url: &str) -> String {
    // Mask password in connection URL
    if let Some(at_pos) = url.find('@') {
        let prefix = &url[..at_pos];
        let suffix = &url[at_pos..];
        // Find if there's a password between : and @
        if let Some(colon_pos) = prefix.find("://") {
            let cred_start = prefix[colon_pos + 3..].to_string();
            if let Some(colon_in_cred) = cred_start.find(':') {
                let user = &cred_start[..colon_in_cred];
                return format!("{}://{}:****{}@", &prefix[..colon_pos + 3], user, suffix);
            }
        }
    }
    url.to_string()
}

fn save_db_config(database_url: &str) -> Result<(), std::io::Error> {
    let config_dir = dirs::home_dir()
        .map(|h| h.join(".nebula-ctx"))
        .ok_or_else(|| std::io::Error::other("No home directory"))?;

    std::fs::create_dir_all(&config_dir)?;

    let env_path = config_dir.join("db.env");

    let content = format!(
        "# Database configuration - sourced by nebula-ctx\n\
         # Generated by: nebula-ctx db connect\n\n\
         export NEBULA_STORE=postgres\n\
         export DATABASE_URL={}\n",
        database_url
    );

    std::fs::write(&env_path, content)?;
    println!("Saved to: {}", env_path.display());
    println!("To use: source {}", env_path.display());
    Ok(())
}