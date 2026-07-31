[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateScript({ Test-Path $_ -PathType Container })]
    [string] $PublishDirectory,

    [Parameter(Mandatory)]
    [ValidatePattern("^[a-z0-9.-]+$")]
    [string] $Hostname
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"

$root = "C:\CsaMeetingCoach"
$appDirectory = Join-Path $root "app"
$backupDirectory = Join-Path $root "backup"
$stagingDirectory = Join-Path $root ("staging-" + [Guid]::NewGuid().ToString("N"))
$caddyDirectory = Join-Path $root "caddy"
$caddyExecutable = Join-Path $caddyDirectory "caddy.exe"
$caddyConfig = Join-Path $caddyDirectory "Caddyfile"
$caddyCandidateConfig = Join-Path $caddyDirectory "Caddyfile.next"
$caddyBackupConfig = Join-Path $caddyDirectory "Caddyfile.previous"
$caddyVersionFile = Join-Path $caddyDirectory "version.txt"
$dataDirectory = Join-Path $env:ProgramData "CsaMeetingCoach\data"
$keyDirectory = Join-Path $env:ProgramData "CsaMeetingCoach\keys"
$apiServiceName = "CsaMeetingCoach.Api"
$caddyServiceName = "CsaMeetingCoach.Caddy"
$firewallRuleName = "CSA Meeting Coach HTTPS"
$caddyUrl =
    "https://github.com/caddyserver/caddy/releases/download/v2.11.4/caddy_2.11.4_windows_amd64.zip"
$caddySha256 = "1708333f79e274c7697285afe6d592ab39314e0b131e9ec6bea08ad27df62ebf"

function Assert-Administrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = [Security.Principal.WindowsPrincipal]::new($identity)
    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        throw "The deployment runner must execute with administrator privileges."
    }
}

function Invoke-ServiceControl {
    param([Parameter(Mandatory)][string[]] $Arguments)

    $output = & "$env:SystemRoot\System32\sc.exe" @Arguments 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "sc.exe $($Arguments -join ' ') failed: $($output -join ' ')"
    }
}

function Stop-ServiceIfRunning {
    param([Parameter(Mandatory)][string] $Name)

    $service = Get-Service -Name $Name -ErrorAction SilentlyContinue
    if ($service -and $service.Status -ne "Stopped") {
        Stop-Service -Name $Name -Force
        $service.WaitForStatus("Stopped", [TimeSpan]::FromSeconds(30))
    }
}

function Ensure-ServiceDefinition {
    param(
        [Parameter(Mandatory)][string] $Name,
        [Parameter(Mandatory)][string] $DisplayName,
        [Parameter(Mandatory)][string] $BinaryPath
    )

    $service = Get-Service -Name $Name -ErrorAction SilentlyContinue
    if ($service) {
        Stop-ServiceIfRunning -Name $Name
        Invoke-ServiceControl -Arguments @(
            "config",
            $Name,
            "binPath=",
            $BinaryPath,
            "start=",
            "auto",
            "DisplayName=",
            $DisplayName)
    }
    else {
        New-Service `
            -Name $Name `
            -DisplayName $DisplayName `
            -BinaryPathName $BinaryPath `
            -StartupType Automatic | Out-Null
    }

    Invoke-ServiceControl -Arguments @(
        "failure",
        $Name,
        "reset=",
        "86400",
        "actions=",
        "restart/5000/restart/5000/restart/5000")
}

function Wait-ServiceRunning {
    param([Parameter(Mandatory)][string] $Name)

    $service = Get-Service -Name $Name -ErrorAction Stop
    $service.WaitForStatus("Running", [TimeSpan]::FromSeconds(30))
}

function Wait-ApiHealth {
    $lastError = $null
    for ($attempt = 0; $attempt -lt 30; $attempt++) {
        try {
            $health = Invoke-RestMethod `
                -Uri "http://127.0.0.1:5055/api/health" `
                -TimeoutSec 5
            if ($health.status -eq "healthy") {
                return
            }
        }
        catch {
            $lastError = $_
        }

        Start-Sleep -Seconds 1
    }

    throw "The API did not become healthy. Last error: $lastError"
}

function Wait-HttpsHealth {
    param([Parameter(Mandatory)][string] $PublicHostname)

    $curl = Get-Command "curl.exe" -ErrorAction Stop
    $lastError = $null
    for ($attempt = 0; $attempt -lt 60; $attempt++) {
        $previousErrorActionPreference = $ErrorActionPreference
        try {
            $ErrorActionPreference = "Continue"
            $response = & $curl.Source `
                --fail `
                --silent `
                --show-error `
                --connect-timeout 5 `
                --max-time 10 `
                --resolve "${PublicHostname}:443:127.0.0.1" `
                "https://$PublicHostname/api/health" 2>&1
            $curlExitCode = $LASTEXITCODE
        }
        finally {
            $ErrorActionPreference = $previousErrorActionPreference
        }

        if ($curlExitCode -eq 0) {
            try {
                $health = ($response -join [Environment]::NewLine) | ConvertFrom-Json
                if ($health.status -eq "healthy") {
                    return
                }
            }
            catch {
                $lastError = $_
            }
        }
        else {
            $lastError = $response -join [Environment]::NewLine
        }

        Start-Sleep -Seconds 2
    }

    throw "The HTTPS endpoint did not become healthy. Last error: $lastError"
}

function Ensure-HttpsFirewallRule {
    $rule = Get-NetFirewallRule `
        -DisplayName $firewallRuleName `
        -ErrorAction SilentlyContinue
    if (-not $rule) {
        New-NetFirewallRule `
            -DisplayName $firewallRuleName `
            -Direction Inbound `
            -Action Allow `
            -Protocol TCP `
            -LocalPort 443 `
            -Profile Any | Out-Null
    }
}

function Install-Caddy {
    if ((Test-Path $caddyExecutable) -and
        (Test-Path $caddyVersionFile) -and
        ((Get-Content $caddyVersionFile -Raw).Trim() -eq "2.11.4")) {
        return
    }

    $archive = Join-Path $env:TEMP "caddy_2.11.4_windows_amd64.zip"
    $extractDirectory = Join-Path $env:TEMP ("caddy-" + [Guid]::NewGuid().ToString("N"))
    try {
        Invoke-WebRequest -Uri $caddyUrl -OutFile $archive -UseBasicParsing
        $archiveHash = (Get-FileHash -Path $archive -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($archiveHash -ne $caddySha256) {
            throw "Caddy archive hash verification failed."
        }

        Expand-Archive -Path $archive -DestinationPath $extractDirectory -Force
        $candidate = Get-ChildItem `
            -Path $extractDirectory `
            -Filter "caddy.exe" `
            -Recurse | Select-Object -First 1
        if (-not $candidate) {
            throw "The Caddy archive did not contain caddy.exe."
        }

        Stop-ServiceIfRunning -Name $caddyServiceName
        New-Item -ItemType Directory -Path $caddyDirectory -Force | Out-Null
        Copy-Item -Path $candidate.FullName -Destination $caddyExecutable -Force
        Set-Content -Path $caddyVersionFile -Value "2.11.4" -Encoding Ascii
    }
    finally {
        Remove-Item $archive -Force -ErrorAction SilentlyContinue
        Remove-Item $extractDirectory -Recurse -Force -ErrorAction SilentlyContinue
    }
}

function Remove-ServiceDefinition {
    param([Parameter(Mandatory)][string] $Name)

    if (Get-Service -Name $Name -ErrorAction SilentlyContinue) {
        Stop-ServiceIfRunning -Name $Name
        Invoke-ServiceControl -Arguments @("delete", $Name)
    }
}

Assert-Administrator
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

New-Item `
    -ItemType Directory `
    -Path $root, $dataDirectory, $keyDirectory `
    -Force | Out-Null
New-Item -ItemType Directory -Path $stagingDirectory -Force | Out-Null
Copy-Item `
    -Path (Join-Path (Resolve-Path $PublishDirectory) "*") `
    -Destination $stagingDirectory `
    -Recurse `
    -Force

$stagedExecutable = Join-Path $stagingDirectory "CsaMeetingCoach.Api.exe"
if (-not (Test-Path $stagedExecutable -PathType Leaf)) {
    throw "The publish directory does not contain CsaMeetingCoach.Api.exe."
}

$caddyFileContents = @"
{
    admin 127.0.0.1:2019
}

$Hostname {
    encode zstd gzip
    reverse_proxy 127.0.0.1:5055
    header {
        Strict-Transport-Security "max-age=31536000; includeSubDomains"
        X-Content-Type-Options "nosniff"
        Referrer-Policy "no-referrer"
        Permissions-Policy "camera=(), geolocation=(), microphone=(self), payment=()"
        -Server
    }
}
"@

Install-Caddy
Set-Content -Path $caddyCandidateConfig -Value $caddyFileContents -Encoding Ascii
& $caddyExecutable `
    validate `
    --config $caddyCandidateConfig `
    --adapter caddyfile
if ($LASTEXITCODE -ne 0) {
    throw "Caddy rejected the candidate configuration."
}

$hadPreviousDeployment = Test-Path $appDirectory -PathType Container
$apiServiceExisted =
    [bool](Get-Service -Name $apiServiceName -ErrorAction SilentlyContinue)
$caddyServiceExisted =
    [bool](Get-Service -Name $caddyServiceName -ErrorAction SilentlyContinue)
$hadPreviousCaddyConfig = Test-Path $caddyConfig -PathType Leaf
Remove-Item $caddyBackupConfig -Force -ErrorAction SilentlyContinue
if ($hadPreviousCaddyConfig) {
    Copy-Item -Path $caddyConfig -Destination $caddyBackupConfig -Force
}

Stop-ServiceIfRunning -Name $apiServiceName
Remove-Item $backupDirectory -Recurse -Force -ErrorAction SilentlyContinue
if ($hadPreviousDeployment) {
    Move-Item -Path $appDirectory -Destination $backupDirectory
}
Move-Item -Path $stagingDirectory -Destination $appDirectory

try {
    $apiExecutable = Join-Path $appDirectory "CsaMeetingCoach.Api.exe"
    Ensure-ServiceDefinition `
        -Name $apiServiceName `
        -DisplayName "CSA Meeting Coach API" `
        -BinaryPath ('"{0}"' -f $apiExecutable)

    $serviceRegistryPath =
        "HKLM:\SYSTEM\CurrentControlSet\Services\$apiServiceName"
    New-ItemProperty `
        -Path $serviceRegistryPath `
        -Name "Environment" `
        -PropertyType MultiString `
        -Value @(
            "ASPNETCORE_ENVIRONMENT=Production",
            "ASPNETCORE_URLS=http://127.0.0.1:5055",
            "Storage__DataDirectory=$dataDirectory",
            "DataProtection__KeyDirectory=$keyDirectory") `
        -Force | Out-Null

    Start-Service -Name $apiServiceName
    Wait-ServiceRunning -Name $apiServiceName
    Wait-ApiHealth

    Ensure-HttpsFirewallRule
    Copy-Item -Path $caddyCandidateConfig -Destination $caddyConfig -Force
    $caddyBinaryPath =
        ('"{0}" run --config "{1}" --adapter caddyfile' -f $caddyExecutable, $caddyConfig)
    Ensure-ServiceDefinition `
        -Name $caddyServiceName `
        -DisplayName "CSA Meeting Coach HTTPS Proxy" `
        -BinaryPath $caddyBinaryPath
    Start-Service -Name $caddyServiceName
    Wait-ServiceRunning -Name $caddyServiceName
    Wait-HttpsHealth -PublicHostname $Hostname
}
catch {
    $deploymentError = $_
    Stop-ServiceIfRunning -Name $caddyServiceName
    Stop-ServiceIfRunning -Name $apiServiceName

    Remove-Item $appDirectory -Recurse -Force -ErrorAction SilentlyContinue
    if ($hadPreviousDeployment -and (Test-Path $backupDirectory)) {
        Move-Item -Path $backupDirectory -Destination $appDirectory
    }
    if ($apiServiceExisted) {
        Start-Service -Name $apiServiceName
    }
    else {
        Remove-ServiceDefinition -Name $apiServiceName
    }

    if ($hadPreviousCaddyConfig -and (Test-Path $caddyBackupConfig)) {
        Copy-Item -Path $caddyBackupConfig -Destination $caddyConfig -Force
    }
    else {
        Remove-Item $caddyConfig -Force -ErrorAction SilentlyContinue
    }
    if ($caddyServiceExisted) {
        Start-Service -Name $caddyServiceName
    }
    else {
        Remove-ServiceDefinition -Name $caddyServiceName
    }

    Remove-Item $caddyCandidateConfig -Force -ErrorAction SilentlyContinue
    throw $deploymentError
}

Remove-Item $caddyCandidateConfig, $caddyBackupConfig `
    -Force `
    -ErrorAction SilentlyContinue

Write-Output "CSA Meeting Coach deployed and verified at https://$Hostname."
