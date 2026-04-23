$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$projectRoot = Split-Path -Parent $PSScriptRoot
$cargoPath = Join-Path $HOME '.cargo\bin\cargo.exe'
if (-not (Test-Path $cargoPath)) {
    throw "cargo.exe not found at $cargoPath"
}

$serverPort = 4246
$dashboardPort = 3336
$authToken = 'nctx_e2e_local_token'
$serverHome = Join-Path ([System.IO.Path]::GetTempPath()) ("nebu-ctx-server-" + [guid]::NewGuid().ToString('N'))
$serverLog = Join-Path $serverHome 'server.log'
$markerKey = 'e2e-shared-key'
$markerValue = 'e2e-shared-value'
$clientConfigDir = Join-Path $HOME '.nebu-ctx\cloud'
$clientConnectionPath = Join-Path $clientConfigDir 'server_connection.json'
$clientBackupPath = Join-Path ([System.IO.Path]::GetTempPath()) ("nebu-ctx-connection-backup-" + [guid]::NewGuid().ToString('N') + '.json')

New-Item -ItemType Directory -Force -Path $serverHome | Out-Null
New-Item -ItemType Directory -Force -Path $clientConfigDir | Out-Null

if (Test-Path $clientConnectionPath) {
    Copy-Item -Force $clientConnectionPath $clientBackupPath
}

function Invoke-ClientCommand {
    param(
        [Parameter(Mandatory = $true)]
        [string[]] $Arguments
    )

    $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $cargoPath
    $startInfo.WorkingDirectory = $projectRoot
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $startInfo.UseShellExecute = $false
    $startInfo.ArgumentList.Add('run')
    $startInfo.ArgumentList.Add('--manifest-path')
    $startInfo.ArgumentList.Add((Join-Path $projectRoot 'client/Cargo.toml'))
    $startInfo.ArgumentList.Add('--bin')
    $startInfo.ArgumentList.Add('nebu-ctx')
    $startInfo.ArgumentList.Add('--quiet')
    $startInfo.ArgumentList.Add('--')
    foreach ($argument in $Arguments) {
        $startInfo.ArgumentList.Add($argument)
    }

    $process = [System.Diagnostics.Process]::Start($startInfo)
    $process.WaitForExit()

    $stdout = $process.StandardOutput.ReadToEnd()
    $stderr = $process.StandardError.ReadToEnd()
    if ($process.ExitCode -ne 0) {
        throw "Client command failed: $($Arguments -join ' ')`nSTDOUT:`n$stdout`nSTDERR:`n$stderr"
    }

    if ($stderr) {
        Write-Host $stderr.Trim()
    }

    return $stdout.Trim()
}

function Wait-ForHttp {
    param(
        [Parameter(Mandatory = $true)]
        [string] $Url,
        [Parameter(Mandatory = $true)]
        [string] $Token
    )

    for ($attempt = 0; $attempt -lt 90; $attempt++) {
        try {
            Invoke-RestMethod -Uri $Url -Headers @{ Authorization = "Bearer $Token" } | Out-Null
            return
        }
        catch {
            Start-Sleep -Seconds 1
        }
    }

    throw "Timed out waiting for $Url"
}

$serverProcess = $null
try {
    $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = 'dotnet'
    $startInfo.WorkingDirectory = $projectRoot
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $startInfo.UseShellExecute = $false
    $startInfo.Environment['HOME'] = $serverHome
    $startInfo.Environment['USERPROFILE'] = $serverHome
    $startInfo.Environment['NEBULA_CTX_HOST'] = '127.0.0.1'
    $startInfo.Environment['NEBULA_CTX_HTTP_PORT'] = "$serverPort"
    $startInfo.Environment['NEBULA_CTX_PORT'] = "$dashboardPort"
    $startInfo.Environment['NEBULA_CTX_HTTP_TOKEN'] = $authToken
    $startInfo.ArgumentList.Add('run')
    $startInfo.ArgumentList.Add('--project')
    $startInfo.ArgumentList.Add('server/src/NebuCtx.Server.Host/NebuCtx.Server.Host.csproj')

    $serverProcess = [System.Diagnostics.Process]::Start($startInfo)
    Start-Job -ScriptBlock {
        param($process, $logPath)
        $stdout = $process.StandardOutput.ReadToEnd()
        $stderr = $process.StandardError.ReadToEnd()
        Set-Content -Path $logPath -Value ($stdout + [Environment]::NewLine + $stderr)
    } -ArgumentList $serverProcess, $serverLog | Out-Null

    Wait-ForHttp -Url "http://127.0.0.1:$serverPort/health" -Token $authToken

    $connect = Invoke-ClientCommand -Arguments @('server', 'connect', '--endpoint', "http://127.0.0.1:$serverPort", '--token', $authToken) | ConvertFrom-Json
    if (-not $connect.connected) {
        throw 'Client did not report a successful server connection.'
    }

    $toolList = Invoke-ClientCommand -Arguments @('tools', 'list') | ConvertFrom-Json
    if ($toolList.total -lt 1) {
        throw 'Expected at least one MCP tool from the server.'
    }

    $resolved = Invoke-ClientCommand -Arguments @('server', 'bind') | ConvertFrom-Json
    if (-not $resolved.project.project_id) {
        throw 'Project binding response did not include a project identifier.'
    }

    $store = Invoke-ClientCommand -Arguments @('ctx_brain', 'action=store', "key=$markerKey", "value=$markerValue") | ConvertFrom-Json
    if (-not $store) {
        throw 'Store command returned an empty payload.'
    }

    $recallText = Invoke-ClientCommand -Arguments @('ctx_brain', 'action=recall', "query=$markerKey")
    if ($recallText -notmatch [regex]::Escape($markerKey) -or $recallText -notmatch [regex]::Escape($markerValue)) {
        throw "Recall output did not include the stored marker. Output: $recallText"
    }

    Write-Host 'Client/server e2e passed.'
}
finally {
    if ($serverProcess -and -not $serverProcess.HasExited) {
        $serverProcess.Kill($true)
        $serverProcess.WaitForExit()
    }

    if (Test-Path $serverHome) {
        Remove-Item -Recurse -Force $serverHome
    }

    if (Test-Path $clientBackupPath) {
        Copy-Item -Force $clientBackupPath $clientConnectionPath
        Remove-Item -Force $clientBackupPath
    }
    elseif (Test-Path $clientConnectionPath) {
        Remove-Item -Force $clientConnectionPath
    }
}