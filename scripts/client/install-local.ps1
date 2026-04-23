param(
    [string]$ClientPath,
    [string]$ClientBinary = 'nebu-ctx',
    [string]$InstallRoot = '',
    [switch]$NoForce
)

$ErrorActionPreference = 'Stop'

$rootDir = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
if (-not $ClientPath) {
    $ClientPath = Join-Path $rootDir 'client'
}

if (-not (Get-Command cargo -ErrorAction SilentlyContinue)) {
    throw 'cargo is required to install the client.'
}

if (-not (Test-Path (Join-Path $ClientPath 'Cargo.toml'))) {
    throw "Client package not found: $ClientPath"
}

Write-Host "[client-install] Installing $ClientBinary from $ClientPath"

$arguments = @('install', '--path', $ClientPath, '--bin', $ClientBinary)
if (-not $NoForce) {
    $arguments += '--force'
}

if ($InstallRoot) {
    $arguments += @('--root', $InstallRoot)
}

& cargo @arguments

if ($LASTEXITCODE -ne 0) {
    throw "cargo install failed with exit code $LASTEXITCODE"
}

Write-Host '[client-install] Done'