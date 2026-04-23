param()

$ErrorActionPreference = 'Stop'

$rootDir = (& git rev-parse --show-toplevel).Trim()
if (-not $rootDir) {
    throw 'Failed to resolve repository root via git.'
}

$hooksDir = (& git -C $rootDir rev-parse --git-path hooks).Trim()
if (-not $hooksDir) {
    throw 'Failed to resolve .git/hooks path via git.'
}

$sourceHook = Join-Path $rootDir 'scripts\git\pre-push-dist-example.sh'
$targetHook = Join-Path $hooksDir 'pre-push'

if (-not (Test-Path $sourceHook)) {
    throw "Missing source hook: $sourceHook"
}

New-Item -ItemType Directory -Path $hooksDir -Force | Out-Null
Copy-Item -Path $sourceHook -Destination $targetHook -Force

Write-Host "[install-pre-push] Installed $targetHook"