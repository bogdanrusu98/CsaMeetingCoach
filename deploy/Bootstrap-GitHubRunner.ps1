[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateNotNullOrEmpty()]
    [string] $RegistrationToken,

    [ValidateNotNullOrEmpty()]
    [string] $RepositoryUrl = "https://github.com/bogdanrusu98/CsaMeetingCoach",

    [ValidateNotNullOrEmpty()]
    [string] $RunnerName = $env:COMPUTERNAME
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"

$runnerRoot = "C:\actions-runner"
$runnerUrl =
    "https://github.com/actions/runner/releases/download/v2.336.0/actions-runner-win-x64-2.336.0.zip"
$runnerSha256 = "d59123a43003e357b0805b5d0f611d0bd2f65ab67d51bd070dd4e7a0f685c162"

function Assert-Administrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = [Security.Principal.WindowsPrincipal]::new($identity)
    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        throw "Run this script from an elevated PowerShell session."
    }
}

function Wait-RunnerService {
    param([Parameter(Mandatory)][System.ServiceProcess.ServiceController] $Service)

    for ($attempt = 0; $attempt -lt 30; $attempt++) {
        $Service.Refresh()
        if ($Service.Status -eq [System.ServiceProcess.ServiceControllerStatus]::Running) {
            return
        }

        Start-Sleep -Seconds 1
    }

    throw "The GitHub Actions runner service did not reach Running state."
}

Assert-Administrator
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

$existingService = Get-Service -Name "actions.runner.*" -ErrorAction SilentlyContinue |
    Select-Object -First 1
if ((Test-Path (Join-Path $runnerRoot ".runner")) -and $existingService) {
    Set-Service -Name $existingService.Name -StartupType Automatic
    if ($existingService.Status -ne "Running") {
        Start-Service -Name $existingService.Name
    }

    Wait-RunnerService -Service $existingService
    Write-Output "GitHub Actions runner is already configured and running."
    exit 0
}

if ((Test-Path (Join-Path $runnerRoot ".runner")) -or $existingService) {
    throw "A partial runner installation exists. Remove it before bootstrapping again."
}

New-Item -ItemType Directory -Path $runnerRoot -Force | Out-Null
$runnerArchive = Join-Path $env:TEMP "actions-runner-win-x64-2.336.0.zip"
Invoke-WebRequest -Uri $runnerUrl -OutFile $runnerArchive -UseBasicParsing

$actualHash = (Get-FileHash -Path $runnerArchive -Algorithm SHA256).Hash.ToLowerInvariant()
if ($actualHash -ne $runnerSha256) {
    Remove-Item $runnerArchive -Force
    throw "GitHub Actions runner archive hash verification failed."
}

Expand-Archive -Path $runnerArchive -DestinationPath $runnerRoot -Force
Remove-Item $runnerArchive -Force

Push-Location $runnerRoot
try {
    & .\config.cmd `
        --unattended `
        --url $RepositoryUrl `
        --token $RegistrationToken `
        --name $RunnerName `
        --labels "csa-demo" `
        --work "_work" `
        --runasservice `
        --windowslogonaccount "NT AUTHORITY\SYSTEM" `
        --replace
    if ($LASTEXITCODE -ne 0) {
        throw "GitHub Actions runner configuration failed with exit code $LASTEXITCODE."
    }
}
finally {
    Pop-Location
}

$runnerService = Get-Service -Name "actions.runner.*" -ErrorAction Stop |
    Select-Object -First 1
Set-Service -Name $runnerService.Name -StartupType Automatic
if ($runnerService.Status -ne "Running") {
    Start-Service -Name $runnerService.Name
}

Wait-RunnerService -Service $runnerService
Write-Output "GitHub Actions runner '$RunnerName' is configured and running."
