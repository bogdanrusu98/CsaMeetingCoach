[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateScript({ Test-Path $_ -PathType Container })]
    [string] $PublishDirectory,

    [Parameter(Mandatory)]
    [ValidatePattern("^[a-z0-9.-]+$")]
    [string] $Hostname,

    [switch] $Enabled,

    [string] $GraphTenantId = "",

    [string] $GraphAppId = "",

    [string] $GraphClientSecret = "",

    [string] $PublicIpAddress = "",

    [string] $CertificateThumbprint = "",

    [ValidateRange(1, 65535)]
    [int] $MediaPort = 8445,

    [string] $SpeechSubscriptionKey = "",

    [string] $SpeechRegion = "",

    [string] $SpeechLanguage = "en-US",

    [ValidateSet("Disabled", "DevelopmentApiKey", "Entra")]
    [string] $CoachApiAuthenticationMode = "Disabled",

    [string] $CoachApiDevelopmentApiKey = "",

    [string] $CoachApiEntraScope = "",

    [ValidateSet("Disabled", "DevelopmentApiKey", "Entra")]
    [string] $TranscriptAdapterAuthenticationMode = "Disabled",

    [string] $TranscriptAdapterEntraTenantId = "",

    [string] $TranscriptAdapterEntraAudience = "",

    [ValidateSet("Disabled", "DevelopmentApiKey", "Entra")]
    [string] $ControlAuthenticationMode = "Disabled",

    [string] $ControlDevelopmentApiKey = "",

    [string] $ControlEntraTenantId = "",

    [string] $ControlEntraAudience = "",

    [string] $ControlRequiredRole = "MediaBot.Controller"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"

$root = "C:\CsaMeetingCoach"
$botDirectory = Join-Path $root "bot"
$backupDirectory = Join-Path $root "bot-backup"
$stagingDirectory = Join-Path $root ("bot-staging-" + [Guid]::NewGuid().ToString("N"))
$serviceName = "CsaMeetingCoach.MediaBot"
$serviceRegistryPath = "HKLM:\SYSTEM\CurrentControlSet\Services\$serviceName"
$firewallRuleName = "CSA Meeting Coach Media"

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

function Start-ServiceBounded {
    param([Parameter(Mandatory)][string] $Name)

    $service = Get-Service -Name $Name -ErrorAction Stop
    if ($service.Status -eq "Running") {
        return
    }

    $startOutput = & "$env:SystemRoot\System32\sc.exe" start $Name 2>&1
    $startExitCode = $LASTEXITCODE
    $deadline = [DateTime]::UtcNow.AddSeconds(30)
    do {
        $service = Get-Service -Name $Name -ErrorAction Stop
        if ($service.Status -eq "Running") {
            return
        }
        if ($service.Status -eq "Stopped") {
            $queryOutput = & "$env:SystemRoot\System32\sc.exe" queryex $Name 2>&1
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
    throw ("Service '{0}' did not reach Running within 30 seconds. " +
        "Start exit code: {1}. Start output: {2}. Status: {3}" -f
        $Name,
        $startExitCode,
        ($startOutput -join " "),
        ($queryOutput -join " "))
}

function Ensure-ServiceDefinition {
    param([Parameter(Mandatory)][string] $BinaryPath)

    if (Get-Service -Name $serviceName -ErrorAction SilentlyContinue) {
        Stop-ServiceIfRunning -Name $serviceName
        Invoke-ServiceControl -Arguments @(
            "config",
            $serviceName,
            "binPath=",
            $BinaryPath,
            "start=",
            "auto",
            "DisplayName=",
            "CSA Meeting Coach Media Bot")
    }
    else {
        New-Service `
            -Name $serviceName `
            -DisplayName "CSA Meeting Coach Media Bot" `
            -BinaryPathName $BinaryPath `
            -StartupType Automatic | Out-Null
    }

    Invoke-ServiceControl -Arguments @(
        "failure",
        $serviceName,
        "reset=",
        "86400",
        "actions=",
        "restart/5000/restart/5000/restart/5000")
}

function Remove-ServiceDefinition {
    if (Get-Service -Name $serviceName -ErrorAction SilentlyContinue) {
        Stop-ServiceIfRunning -Name $serviceName
        Invoke-ServiceControl -Arguments @("delete", $serviceName)
    }
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

function Wait-BotHealth {
    $expectedStatus = if ($Enabled) { "ready" } else { "disabled" }
    $lastError = $null
    for ($attempt = 0; $attempt -lt 30; $attempt++) {
        try {
            $health = Invoke-RestMethod `
                -Uri "http://127.0.0.1:5065/bot/health" `
                -TimeoutSec 5
            if ($health.status -eq $expectedStatus) {
                return
            }

            $lastError = "Expected '$expectedStatus', received '$($health.status)'."
        }
        catch {
            $lastError = $_
        }

        Start-Sleep -Seconds 1
    }

    throw "The media bot did not become healthy. Last error: $lastError"
}

function Require-Value {
    param(
        [Parameter(Mandatory)][string] $Value,
        [Parameter(Mandatory)][string] $Name
    )

    if ([string]::IsNullOrWhiteSpace($Value)) {
        throw "Enabled media bot deployment requires '$Name'."
    }
}

function Disable-ExistingMediaBot {
    if (Get-Service -Name $serviceName -ErrorAction SilentlyContinue) {
        Stop-ServiceIfRunning -Name $serviceName
        New-ItemProperty `
            -Path $serviceRegistryPath `
            -Name "Environment" `
            -PropertyType MultiString `
            -Value @(
                "ASPNETCORE_ENVIRONMENT=Production",
                "ASPNETCORE_URLS=http://127.0.0.1:5065",
                "Graph__Enabled=false") `
            -Force | Out-Null
        Protect-ServiceRegistryKey -Path $serviceRegistryPath
        Invoke-ServiceControl -Arguments @(
            "config",
            $serviceName,
            "start=",
            "disabled")
    }

    Remove-NetFirewallRule `
        -DisplayName $firewallRuleName `
        -ErrorAction SilentlyContinue
}

Assert-Administrator

if ($Enabled) {
    try {
        if ([string]::IsNullOrWhiteSpace($PublicIpAddress)) {
            $PublicIpAddress = Resolve-DnsName `
                -Name $Hostname `
                -Type A `
                -ErrorAction Stop |
                Select-Object -First 1 -ExpandProperty IPAddress
        }

        Require-Value -Value $GraphTenantId -Name "GraphTenantId"
        Require-Value -Value $GraphAppId -Name "GraphAppId"
        Require-Value -Value $GraphClientSecret -Name "GraphClientSecret"
        Require-Value -Value $PublicIpAddress -Name "PublicIpAddress"
        Require-Value -Value $CertificateThumbprint -Name "CertificateThumbprint"
        Require-Value -Value $SpeechSubscriptionKey -Name "SpeechSubscriptionKey"
        Require-Value -Value $SpeechRegion -Name "SpeechRegion"
        Require-Value -Value $SpeechLanguage -Name "SpeechLanguage"

        $parsedPublicIp = $null
        if (-not [Net.IPAddress]::TryParse(
                $PublicIpAddress,
                [ref] $parsedPublicIp)) {
            throw "PublicIpAddress must be a valid IP address."
        }
        if ($CoachApiAuthenticationMode -eq "Disabled") {
            throw "Coach API authentication must be enabled with the media bot."
        }
        if ($TranscriptAdapterAuthenticationMode -ne
            $CoachApiAuthenticationMode) {
            throw "Coach API and transcript adapter authentication modes must match."
        }
        if ($CoachApiAuthenticationMode -eq "DevelopmentApiKey" -and
            $CoachApiDevelopmentApiKey.Length -lt 32) {
            throw "Coach API development authentication requires a key of at least 32 characters."
        }
        if ($CoachApiAuthenticationMode -eq "Entra") {
            Require-Value `
                -Value $TranscriptAdapterEntraTenantId `
                -Name "TranscriptAdapterEntraTenantId"
            Require-Value `
                -Value $TranscriptAdapterEntraAudience `
                -Name "TranscriptAdapterEntraAudience"
            $expectedScope = "$TranscriptAdapterEntraAudience/.default"
            if (-not [string]::Equals(
                    $CoachApiEntraScope,
                    $expectedScope,
                    [StringComparison]::Ordinal)) {
                throw "CoachApiEntraScope must target the transcript adapter audience."
            }
        }
        if ($ControlAuthenticationMode -eq "Disabled") {
            throw "Control endpoint authentication must be enabled with the media bot."
        }
        if ($ControlAuthenticationMode -eq "DevelopmentApiKey" -and
            $ControlDevelopmentApiKey.Length -lt 32) {
            throw "Control endpoint development authentication requires a key of at least 32 characters."
        }
        if ($ControlAuthenticationMode -eq "Entra") {
            Require-Value -Value $ControlEntraTenantId -Name "ControlEntraTenantId"
            Require-Value -Value $ControlEntraAudience -Name "ControlEntraAudience"
            Require-Value -Value $ControlRequiredRole -Name "ControlRequiredRole"
        }
    }
    catch {
        Disable-ExistingMediaBot
        throw
    }
}

New-Item -ItemType Directory -Path $root -Force | Out-Null
New-Item -ItemType Directory -Path $stagingDirectory -Force | Out-Null
Copy-Item `
    -Path (Join-Path (Resolve-Path $PublishDirectory) "*") `
    -Destination $stagingDirectory `
    -Recurse `
    -Force

$stagedExecutable = Join-Path $stagingDirectory "CsaMeetingCoach.BotService.exe"
if (-not (Test-Path $stagedExecutable -PathType Leaf)) {
    throw "The publish directory does not contain CsaMeetingCoach.BotService.exe."
}

$hadPreviousDeployment = Test-Path $botDirectory -PathType Container
$serviceExisted = [bool](Get-Service -Name $serviceName -ErrorAction SilentlyContinue)
$previousEnvironment = $null
$hadPreviousEnvironment = $false
if (Test-Path $serviceRegistryPath) {
    $registryValues = Get-ItemProperty `
        -Path $serviceRegistryPath `
        -Name "Environment" `
        -ErrorAction SilentlyContinue
    if ($null -ne $registryValues) {
        $previousEnvironment = @($registryValues.Environment)
        $hadPreviousEnvironment = $true
    }
}
$firewallExisted = [bool](Get-NetFirewallRule `
    -DisplayName $firewallRuleName `
    -ErrorAction SilentlyContinue)

Stop-ServiceIfRunning -Name $serviceName
Remove-Item $backupDirectory -Recurse -Force -ErrorAction SilentlyContinue
if ($hadPreviousDeployment) {
    Move-DirectoryWithRetry -Source $botDirectory -Destination $backupDirectory
}
Move-DirectoryWithRetry -Source $stagingDirectory -Destination $botDirectory

try {
    $botExecutable = Join-Path $botDirectory "CsaMeetingCoach.BotService.exe"
    Ensure-ServiceDefinition -BinaryPath ('"{0}"' -f $botExecutable)

    $serviceEnvironment = @(
        "ASPNETCORE_ENVIRONMENT=Production",
        "ASPNETCORE_URLS=http://127.0.0.1:5065",
        "Graph__Enabled=$($Enabled.IsPresent.ToString().ToLowerInvariant())")

    if ($Enabled) {
        $serviceEnvironment += @(
            "Graph__TenantId=$GraphTenantId",
            "Graph__AppId=$GraphAppId",
            "Graph__ClientSecret=$GraphClientSecret",
            "Graph__NotificationUrl=https://$Hostname/bot/calling",
            "Media__ServiceFqdn=$Hostname",
            "Media__PublicIpAddress=$PublicIpAddress",
            "Media__PublicPort=$MediaPort",
            "Media__InternalPort=$MediaPort",
            "Media__CertificateThumbprint=$CertificateThumbprint",
            "Speech__SubscriptionKey=$SpeechSubscriptionKey",
            "Speech__Region=$SpeechRegion",
            "Speech__Language=$SpeechLanguage",
            "CoachApi__BaseUrl=https://$Hostname/",
            "CoachApi__AuthenticationMode=$CoachApiAuthenticationMode",
            "CoachApi__DevelopmentApiKey=$CoachApiDevelopmentApiKey",
            "CoachApi__EntraScope=$CoachApiEntraScope",
            "ControlEndpoint__AuthenticationMode=$ControlAuthenticationMode",
            "ControlEndpoint__DevelopmentApiKey=$ControlDevelopmentApiKey",
            "ControlEndpoint__TenantId=$ControlEntraTenantId",
            "ControlEndpoint__Audience=$ControlEntraAudience",
            "ControlEndpoint__RequiredRole=$ControlRequiredRole")
    }

    New-ItemProperty `
        -Path $serviceRegistryPath `
        -Name "Environment" `
        -PropertyType MultiString `
        -Value $serviceEnvironment `
        -Force | Out-Null
    Protect-ServiceRegistryKey -Path $serviceRegistryPath

    if ($Enabled -and -not $firewallExisted) {
        New-NetFirewallRule `
            -DisplayName $firewallRuleName `
            -Direction Inbound `
            -Action Allow `
            -Protocol TCP `
            -LocalPort $MediaPort `
            -Profile Any | Out-Null
    }

    Start-ServiceBounded -Name $serviceName
    Wait-BotHealth

    if (-not $Enabled -and $firewallExisted) {
        Remove-NetFirewallRule `
            -DisplayName $firewallRuleName `
            -ErrorAction Stop
    }
}
catch {
    $deploymentError = $_
    Stop-ServiceIfRunning -Name $serviceName
    Remove-Item $botDirectory -Recurse -Force -ErrorAction SilentlyContinue
    if ($hadPreviousDeployment -and (Test-Path $backupDirectory)) {
        Move-DirectoryWithRetry -Source $backupDirectory -Destination $botDirectory
    }

    if ($serviceExisted) {
        if ($hadPreviousEnvironment) {
            New-ItemProperty `
                -Path $serviceRegistryPath `
                -Name "Environment" `
                -PropertyType MultiString `
                -Value $previousEnvironment `
                -Force | Out-Null
        }
        else {
            Remove-ItemProperty `
                -Path $serviceRegistryPath `
                -Name "Environment" `
                -ErrorAction SilentlyContinue
        }
        Protect-ServiceRegistryKey -Path $serviceRegistryPath
        Start-ServiceBounded -Name $serviceName
    }
    else {
        Remove-ServiceDefinition
    }

    if (-not $firewallExisted) {
        Remove-NetFirewallRule `
            -DisplayName $firewallRuleName `
            -ErrorAction SilentlyContinue
    }

    Remove-Item $stagingDirectory -Recurse -Force -ErrorAction SilentlyContinue
    throw $deploymentError
}

Remove-Item $backupDirectory, $stagingDirectory `
    -Recurse `
    -Force `
    -ErrorAction SilentlyContinue

$state = if ($Enabled) { "enabled" } else { "disabled" }
Write-Output "CSA Meeting Coach media bot deployed in $state mode."
