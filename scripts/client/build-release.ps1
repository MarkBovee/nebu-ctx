param(
    [string]$ManifestPath,
    [string]$ClientBinary = 'nebu-ctx',
    [string]$Profile = 'Release',
    [string]$Target = ''
)

$ErrorActionPreference = 'Stop'

$rootDir = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
if (-not $ManifestPath) {
    $ManifestPath = Join-Path $rootDir 'client\Cargo.toml'
}

if (-not (Get-Command cargo -ErrorAction SilentlyContinue)) {
    throw 'cargo is required to build the client.'
}

if (-not (Test-Path $ManifestPath)) {
    throw "Client manifest not found: $ManifestPath"
}

Write-Host "[client-build] Building $ClientBinary ($Profile)"

$arguments = @('build', '--manifest-path', $ManifestPath, '--bin', $ClientBinary)
if ($Profile -ieq 'Release') {
    $arguments += '--release'
}

if ($Target) {
    $arguments += @('--target', $Target)
}

& cargo @arguments

if ($LASTEXITCODE -ne 0) {
    throw "cargo build failed with exit code $LASTEXITCODE"
}

Write-Host '[client-build] Done'