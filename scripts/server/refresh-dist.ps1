param(
    [string]$BuildScript = "$(Join-Path $PSScriptRoot 'build-image.ps1')"
)

$ErrorActionPreference = 'Stop'

$resolvedBuildScript = [System.IO.Path]::GetFullPath($BuildScript)
if (-not (Test-Path $resolvedBuildScript)) {
    throw "Build script not found: $resolvedBuildScript"
}

Write-Host '[refresh-dist] Refreshing server/dist/linux'
& $resolvedBuildScript -PublishOnly
if ($LASTEXITCODE -ne 0) {
    throw "Build script failed with exit code $LASTEXITCODE"
}