param(
    [string]$ServerProject = "$(Join-Path $PSScriptRoot '..\..\server\src\NebuCtx.Server.Host\NebuCtx.Server.Host.csproj')",
    [string]$DistDir = "$(Join-Path $PSScriptRoot '..\..\server\dist\linux')",
    [string]$DockerfilePath = "$(Join-Path $PSScriptRoot '..\..\homeassistant\Dockerfile')",
    [string]$ImageName = "nebu-ctx-server:local",
    [string]$BuildContext = "$(Join-Path $PSScriptRoot '..\..')",
    [string]$Configuration = "Release",
    [string]$RuntimeId,
    [string]$ContainerTool,
    [switch]$PublishOnly,
    [switch]$BuildOnly
)

$ErrorActionPreference = 'Stop'

function Write-Log {
    param([string]$Message)
    Write-Host "[build-image] $Message"
}

function Resolve-FullPath {
    param([string]$PathValue)
    [System.IO.Path]::GetFullPath($PathValue)
}

function Resolve-RuntimeId {
    if ($RuntimeId) {
        return $RuntimeId
    }

    switch ([System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString()) {
        'X64' { 'linux-x64' }
        'Arm64' { 'linux-arm64' }
        default { 'linux-x64' }
    }
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

function Publish-Dist {
    param([string]$ResolvedRuntimeId)

    if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
        throw 'dotnet is required to publish the server dist.'
    }

    $resolvedProject = Resolve-FullPath $ServerProject
    $resolvedDistDir = Resolve-FullPath $DistDir

    if (-not (Test-Path $resolvedProject)) {
        throw "Server project not found: $resolvedProject"
    }

    Write-Log "Publishing server dist ($ResolvedRuntimeId -> $resolvedDistDir)"
    if (Test-Path $resolvedDistDir) {
        Remove-Item $resolvedDistDir -Recurse -Force
    }
    New-Item -ItemType Directory -Path $resolvedDistDir -Force | Out-Null

    $publishArguments = @(
        'publish'
        $resolvedProject
        '-c', $Configuration
        '-r', $ResolvedRuntimeId
        '--self-contained', 'false'
        '-o', $resolvedDistDir
        '/p:UseAppHost=false'
    )

    $previousFlag = $env:NEBULA_ALLOW_MNT_DOTNET
    $env:NEBULA_ALLOW_MNT_DOTNET = '1'
    try {
        & dotnet @publishArguments
        if ($LASTEXITCODE -ne 0) {
            throw "dotnet publish failed with exit code $LASTEXITCODE"
        }
    }
    finally {
        if ($null -eq $previousFlag) {
            Remove-Item Env:NEBULA_ALLOW_MNT_DOTNET -ErrorAction SilentlyContinue
        }
        else {
            $env:NEBULA_ALLOW_MNT_DOTNET = $previousFlag
        }
    }

    $expectedDll = Join-Path $resolvedDistDir 'NebuCtx.Server.Host.dll'
    if (-not (Test-Path $expectedDll)) {
        throw "Expected $expectedDll after publish"
    }
}

function Build-Image {
    param([string]$ResolvedContainerTool)

    $resolvedDockerfile = Resolve-FullPath $DockerfilePath
    $resolvedContext = Resolve-FullPath $BuildContext
    $resolvedDistDir = Resolve-FullPath $DistDir

    if (-not (Test-Path $resolvedDockerfile)) {
        throw "Dockerfile not found: $resolvedDockerfile"
    }

    if (-not (Test-Path $resolvedContext)) {
        throw "Build context not found: $resolvedContext"
    }

    if (-not (Test-Path $resolvedDistDir)) {
        throw "Dist directory not found: $resolvedDistDir"
    }

    Write-Log "Building image $ImageName from $resolvedDockerfile"
    & $ResolvedContainerTool build -t $ImageName -f $resolvedDockerfile $resolvedContext
    if ($LASTEXITCODE -ne 0) {
        throw "$ResolvedContainerTool build failed with exit code $LASTEXITCODE"
    }
}

$resolvedRuntimeId = Resolve-RuntimeId
$resolvedContainerTool = Resolve-ContainerTool

if (-not $BuildOnly) {
    Publish-Dist -ResolvedRuntimeId $resolvedRuntimeId
}

if (-not $PublishOnly) {
    Build-Image -ResolvedContainerTool $resolvedContainerTool
}

Write-Log 'Done'