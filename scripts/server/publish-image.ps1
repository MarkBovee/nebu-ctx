param(
    [string]$BuildScript = "$(Join-Path $PSScriptRoot 'build-image.ps1')",
    [string]$ImageRepository = 'localhost/nebu-ctx-local',
    [string]$ImageTag = 'local',
    [string]$SourceImage,
    [string]$TargetImage,
    [string]$ContainerTool,
    [switch]$SkipBuild
)

$ErrorActionPreference = 'Stop'

function Write-Log {
    param([string]$Message)
    Write-Host "[publish-image] $Message"
}

function Resolve-FullPath {
    param([string]$PathValue)
    [System.IO.Path]::GetFullPath($PathValue)
}

function Resolve-ContainerTool {
    if ($ContainerTool) {
        return $ContainerTool
    }

    if (Get-Command docker -ErrorAction SilentlyContinue) {
        return 'docker'
    }

    if (Get-Command podman -ErrorAction SilentlyContinue) {
        return 'podman'
    }

    throw 'Neither docker nor podman is available.'
}

$resolvedBuildScript = Resolve-FullPath $BuildScript
if (-not (Test-Path $resolvedBuildScript)) {
    throw "Build script not found: $resolvedBuildScript"
}

$resolvedContainerTool = Resolve-ContainerTool

if (-not $SourceImage) {
    $SourceImage = "${ImageRepository}:${ImageTag}"
}

if (-not $TargetImage) {
    $TargetImage = $SourceImage
}

if (-not $SkipBuild) {
    Write-Log "Building source image $SourceImage"
    & $resolvedBuildScript -ImageName $SourceImage -ContainerTool $resolvedContainerTool
    if ($LASTEXITCODE -ne 0) {
        throw "Build script failed with exit code $LASTEXITCODE"
    }
}

if ($SourceImage -ne $TargetImage) {
    Write-Log "Tagging $SourceImage as $TargetImage"
    & $resolvedContainerTool tag $SourceImage $TargetImage
    if ($LASTEXITCODE -ne 0) {
        throw "$resolvedContainerTool tag failed with exit code $LASTEXITCODE"
    }
}

Write-Log "Pushing $TargetImage"
& $resolvedContainerTool push $TargetImage
if ($LASTEXITCODE -ne 0) {
    throw "$resolvedContainerTool push failed with exit code $LASTEXITCODE"
}

Write-Log 'Done'