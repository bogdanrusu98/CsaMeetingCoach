[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateScript({ Test-Path $_ -PathType Container })]
    [string] $PublishDirectory,

    [Parameter(Mandatory)]
    [ValidatePattern("^[a-z0-9.-]+$")]
    [string] $Hostname,

    [ValidateSet("Local", "Foundry")]
    [string] $CoachAgentProvider = "Local",

    [string] $FoundryProjectEndpoint =
        "https://csa-meeting-coach-resource.services.ai.azure.com/api/projects/csa-meeting-coach",

    [string] $FoundryModelDeployment = "gpt-4.1-mini",

    [ValidateLength(1, 64)]
    [string] $FoundryAgentName = "csa-meeting-coach-v5",

    [string] $FoundryVectorStoreIds = "",

    [switch] $BrowserSpeechEnabled,

    [string] $BrowserSpeechSubscriptionKey = "",

    [string] $BrowserSpeechAccessKey = "",

    [string] $BrowserSpeechRegion = "",

    [string] $BrowserSpeechLanguage = "en-US",

    [ValidateSet("Disabled", "DevelopmentApiKey", "Entra")]
    [string] $TranscriptAdapterAuthenticationMode = "Disabled",

    [string] $TranscriptAdapterDevelopmentApiKey = "",

    [string] $TranscriptAdapterEntraTenantId = "",

    [string] $TranscriptAdapterEntraAudience = "",

    [string] $TranscriptAdapterRequiredRole = "TranscriptIngestor"
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
$apiServiceRegistryPath =
    "HKLM:\SYSTEM\CurrentControlSet\Services\$apiServiceName"
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

function Protect-ServiceRegistryKey {
    param([Parameter(Mandatory)][string] $Path)

    $acl = Get-Acl -Path $Path
    $acl.SetAccessRuleProtection($true, $false)
    foreach ($existingRule in @($acl.Access)) {
        $acl.RemoveAccessRuleSpecific($existingRule)
    }
    foreach ($account in @(
            [Security.Principal.NTAccount]"NT AUTHORITY\SYSTEM",
            [Security.Principal.NTAccount]"BUILTIN\Administrators")) {
        $rule = [Security.AccessControl.RegistryAccessRule]::new(
            $account,
            [Security.AccessControl.RegistryRights]::FullControl,
            [Security.AccessControl.InheritanceFlags]::ContainerInherit,
            [Security.AccessControl.PropagationFlags]::None,
            [Security.AccessControl.AccessControlType]::Allow)
        $acl.AddAccessRule($rule)
    }

    Set-Acl -Path $Path -AclObject $acl
}

function Stop-ServiceIfRunning {
    param([Parameter(Mandatory)][string] $Name)

    $service = Get-Service -Name $Name -ErrorAction SilentlyContinue
    if (-not $service -or $service.Status -eq "Stopped") {
        return
    }

    $stopOutput = & "$env:SystemRoot\System32\sc.exe" stop $Name 2>&1
    $stopExitCode = $LASTEXITCODE
    $deadline = [DateTime]::UtcNow.AddSeconds(35)
    do {
        $service = Get-Service -Name $Name -ErrorAction Stop
        if ($service.Status -eq "Stopped") {
            return
        }

        Start-Sleep -Seconds 1
    } while ([DateTime]::UtcNow -lt $deadline)

    $escapedName = $Name.Replace("'", "''")
    $serviceProcess = Get-CimInstance `
        -ClassName Win32_Service `
        -Filter "Name='$escapedName'" `
        -ErrorAction Stop
    if ($serviceProcess.ProcessId -gt 0) {
        Stop-Process `
            -Id ([int] $serviceProcess.ProcessId) `
            -Force `
            -ErrorAction Stop
    }

    $deadline = [DateTime]::UtcNow.AddSeconds(15)
    do {
        $service = Get-Service -Name $Name -ErrorAction Stop
        if ($service.Status -eq "Stopped") {
            return
        }

        Start-Sleep -Seconds 1
    } while ([DateTime]::UtcNow -lt $deadline)

    $queryOutput = & "$env:SystemRoot\System32\sc.exe" queryex $Name 2>&1
    throw ("Service '{0}' did not stop. Stop exit code: {1}. " +
        "Stop output: {2}. Status: {3}" -f
        $Name,
        $stopExitCode,
        ($stopOutput -join " "),
        ($queryOutput -join " "))
}

function Move-DirectoryWithRetry {
    param(
        [Parameter(Mandatory)][string] $Source,
        [Parameter(Mandatory)][string] $Destination
    )

    $lastError = $null
    for ($attempt = 1; $attempt -le 15; $attempt++) {
        try {
            Move-Item -Path $Source -Destination $Destination -ErrorAction Stop
            return
        }
        catch {
            if (($_.Exception -isnot [System.IO.IOException]) -and
                ($_.Exception -isnot [System.UnauthorizedAccessException])) {
                throw
            }

            $lastError = $_
            if ($attempt -lt 15) {
                Start-Sleep -Seconds 2
            }
        }
    }

    throw ("Could not move '{0}' to '{1}' after 15 attempts. Last error: {2}" -f
        $Source,
        $Destination,
        $lastError.Exception.Message)
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

function Start-ServiceBounded {
    param(
        [Parameter(Mandatory)][string] $Name,
        [ValidateRange(1, 300)][int] $TimeoutSeconds = 30,
        [switch] $AcceptStartPending
    )

    $service = Get-Service -Name $Name -ErrorAction Stop
    if ($service.Status -eq "Running") {
        return
    }

    $startOutput = & "$env:SystemRoot\System32\sc.exe" start $Name 2>&1
    $startExitCode = $LASTEXITCODE
    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)

    do {
        $service = Get-Service -Name $Name -ErrorAction Stop
        if ($service.Status -eq "Running") {
            return
        }
        if ($AcceptStartPending -and $service.Status -eq "StartPending") {
            return
        }
        if ($service.Status -eq "Stopped") {
            $queryOutput =
                & "$env:SystemRoot\System32\sc.exe" queryex $Name 2>&1
            throw ("Service '{0}' stopped during startup. Start exit code: {1}. " +
                "Start output: {2}. Status: {3}" -f
                $Name,
                $startExitCode,
                ($startOutput -join " "),
                ($queryOutput -join " "))
        }

        Start-Sleep -Seconds 1
    } while ([DateTime]::UtcNow -lt $deadline)

    $queryOutput = & "$env:SystemRoot\System32\sc.exe" queryex $Name 2>&1
    throw (("Service '{0}' did not reach Running within {1} seconds. " +
        "Start exit code: {2}. Start output: {3}. Status: {4}") -f
        $Name,
        $TimeoutSeconds,
        $startExitCode,
        ($startOutput -join " "),
        ($queryOutput -join " "))
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
    param(
        [Parameter(Mandatory)][string] $PublicHostname,
        [ValidateRange(1, 300)][int] $TimeoutSeconds = 120
    )

    $curl = Get-Command "curl.exe" -ErrorAction Stop
    $lastError = $null
    $timeoutMilliseconds = [long]$TimeoutSeconds * 1000
    $stopwatch = [Diagnostics.Stopwatch]::StartNew()
    while ($stopwatch.ElapsedMilliseconds -lt $timeoutMilliseconds) {
        $remainingSeconds = [int][Math]::Floor((
            $timeoutMilliseconds - $stopwatch.ElapsedMilliseconds) / 1000)
        if ($remainingSeconds -lt 1) {
            break
        }
        $probeTimeoutSeconds = [Math]::Min(10, $remainingSeconds)
        $connectTimeoutSeconds = [Math]::Min(5, $probeTimeoutSeconds)

        $previousErrorActionPreference = $ErrorActionPreference
        try {
            $ErrorActionPreference = "Continue"
            $response = & $curl.Source `
                --fail `
                --silent `
                --show-error `
                --connect-timeout $connectTimeoutSeconds `
                --max-time $probeTimeoutSeconds `
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

        $remainingMilliseconds =
            $timeoutMilliseconds - $stopwatch.ElapsedMilliseconds
        if ($remainingMilliseconds -le 0) {
            break
        }
        Start-Sleep -Milliseconds ([Math]::Min(2000, $remainingMilliseconds))
    }

    throw "The HTTPS endpoint did not become healthy within $TimeoutSeconds seconds. Last error: $lastError"
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

if ($CoachAgentProvider -eq "Foundry") {
    $foundryEndpoint = $null
    if ((-not [Uri]::TryCreate(
            $FoundryProjectEndpoint,
            [UriKind]::Absolute,
            [ref] $foundryEndpoint)) -or
        $foundryEndpoint.Scheme -ne [Uri]::UriSchemeHttps -or
        [string]::IsNullOrWhiteSpace($FoundryModelDeployment)) {
        throw "Foundry deployment requires an HTTPS project endpoint and model deployment."
    }
}

$normalizedVectorStoreIds = [Collections.Generic.List[string]]::new()
$seenVectorStoreIds = [Collections.Generic.HashSet[string]]::new(
    [StringComparer]::Ordinal)
if (-not [string]::IsNullOrWhiteSpace($FoundryVectorStoreIds)) {
    foreach ($candidate in $FoundryVectorStoreIds.Split(",")) {
        $identifier = $candidate.Trim()
        if ($identifier -notmatch "^vs_[A-Za-z0-9]{1,125}$") {
            throw "Foundry vector store IDs must be comma-separated valid vs_ identifiers."
        }
        if ($seenVectorStoreIds.Add($identifier)) {
            $normalizedVectorStoreIds.Add($identifier)
        }
    }
}
if ($normalizedVectorStoreIds.Count -gt 10) {
    throw "No more than 10 Foundry vector store IDs may be configured."
}
if ($CoachAgentProvider -eq "Foundry" -and
    $normalizedVectorStoreIds.Count -eq 0) {
    throw "Foundry deployment requires at least one reviewed vector store ID."
}
$FoundryVectorStoreIds = $normalizedVectorStoreIds -join ","

if ($BrowserSpeechEnabled) {
    if ([string]::IsNullOrWhiteSpace($BrowserSpeechSubscriptionKey) -or
        $BrowserSpeechAccessKey.Length -lt 32 -or
        $BrowserSpeechRegion -notmatch "^[a-z0-9-]+$" -or
        $BrowserSpeechLanguage -notmatch "^[A-Za-z]{2,3}(?:-[A-Za-z0-9]{2,8})+$") {
        throw "Browser speech requires a key, Azure region, and valid speech locale."
    }
}

if ($TranscriptAdapterAuthenticationMode -eq "DevelopmentApiKey" -and
    $TranscriptAdapterDevelopmentApiKey.Length -lt 32) {
    throw "Transcript adapter development authentication requires a key of at least 32 characters."
}
if ($TranscriptAdapterAuthenticationMode -eq "Entra" -and
    ([string]::IsNullOrWhiteSpace($TranscriptAdapterEntraTenantId) -or
        [string]::IsNullOrWhiteSpace($TranscriptAdapterEntraAudience))) {
    throw "Transcript adapter Entra authentication requires tenant and audience values."
}

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
    grace_period 10s
}

$Hostname {
    encode zstd gzip
    reverse_proxy /bot/* 127.0.0.1:5065
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
$previousApiEnvironment = $null
$hadPreviousApiEnvironment = $false
if (Test-Path $apiServiceRegistryPath) {
    $registryValues = Get-ItemProperty `
        -Path $apiServiceRegistryPath `
        -Name "Environment" `
        -ErrorAction SilentlyContinue
    if ($null -ne $registryValues) {
        $previousApiEnvironment = @($registryValues.Environment)
        $hadPreviousApiEnvironment = $true
    }
}
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
    Move-DirectoryWithRetry `
        -Source $appDirectory `
        -Destination $backupDirectory
}
Move-DirectoryWithRetry `
    -Source $stagingDirectory `
    -Destination $appDirectory

try {
    $apiExecutable = Join-Path $appDirectory "CsaMeetingCoach.Api.exe"
    Ensure-ServiceDefinition `
        -Name $apiServiceName `
        -DisplayName "CSA Meeting Coach API" `
        -BinaryPath ('"{0}"' -f $apiExecutable)

    New-ItemProperty `
        -Path $apiServiceRegistryPath `
        -Name "Environment" `
        -PropertyType MultiString `
        -Value @(
            "ASPNETCORE_ENVIRONMENT=Production",
            "ASPNETCORE_URLS=http://127.0.0.1:5055",
            "Storage__DataDirectory=$dataDirectory",
            "DataProtection__KeyDirectory=$keyDirectory",
            "CoachAgent__Provider=$CoachAgentProvider",
            "CoachAgent__Foundry__ProjectEndpoint=$FoundryProjectEndpoint",
            "CoachAgent__Foundry__ModelDeployment=$FoundryModelDeployment",
            "CoachAgent__Foundry__AgentName=$FoundryAgentName",
            "CoachAgent__Foundry__VectorStoreIds=$FoundryVectorStoreIds",
            "BrowserSpeech__Enabled=$($BrowserSpeechEnabled.IsPresent.ToString().ToLowerInvariant())",
            "BrowserSpeech__SubscriptionKey=$BrowserSpeechSubscriptionKey",
            "BrowserSpeech__AccessKey=$BrowserSpeechAccessKey",
            "BrowserSpeech__Region=$BrowserSpeechRegion",
            "BrowserSpeech__Language=$BrowserSpeechLanguage",
            "TranscriptAdapter__AuthenticationMode=$TranscriptAdapterAuthenticationMode",
            "TranscriptAdapter__DevelopmentApiKey=$TranscriptAdapterDevelopmentApiKey",
            "TranscriptAdapter__Entra__TenantId=$TranscriptAdapterEntraTenantId",
            "TranscriptAdapter__Entra__Audience=$TranscriptAdapterEntraAudience",
            "TranscriptAdapter__Entra__RequiredRole=$TranscriptAdapterRequiredRole") `
        -Force | Out-Null
    Protect-ServiceRegistryKey -Path $apiServiceRegistryPath

    Start-ServiceBounded -Name $apiServiceName
    Wait-ApiHealth

    Ensure-HttpsFirewallRule
    Copy-Item -Path $caddyCandidateConfig -Destination $caddyConfig -Force
    $caddyBinaryPath =
        ('"{0}" run --config "{1}" --adapter caddyfile' -f $caddyExecutable, $caddyConfig)
    Ensure-ServiceDefinition `
        -Name $caddyServiceName `
        -DisplayName "CSA Meeting Coach HTTPS Proxy" `
        -BinaryPath $caddyBinaryPath
    Start-ServiceBounded `
        -Name $caddyServiceName `
        -TimeoutSeconds 180 `
        -AcceptStartPending
    Wait-HttpsHealth -PublicHostname $Hostname -TimeoutSeconds 180
}
catch {
    $deploymentError = $_
    Stop-ServiceIfRunning -Name $caddyServiceName
    Stop-ServiceIfRunning -Name $apiServiceName

    Remove-Item $appDirectory -Recurse -Force -ErrorAction SilentlyContinue
    if ($hadPreviousDeployment -and (Test-Path $backupDirectory)) {
        Move-DirectoryWithRetry `
            -Source $backupDirectory `
            -Destination $appDirectory
    }
    if ($apiServiceExisted) {
        if ($hadPreviousApiEnvironment) {
            New-ItemProperty `
                -Path $apiServiceRegistryPath `
                -Name "Environment" `
                -PropertyType MultiString `
                -Value $previousApiEnvironment `
                -Force | Out-Null
        }
        else {
            Remove-ItemProperty `
                -Path $apiServiceRegistryPath `
                -Name "Environment" `
                -ErrorAction SilentlyContinue
        }
        Start-ServiceBounded -Name $apiServiceName
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
        Start-ServiceBounded `
            -Name $caddyServiceName `
            -TimeoutSeconds 180 `
            -AcceptStartPending
        Wait-HttpsHealth -PublicHostname $Hostname -TimeoutSeconds 180
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
