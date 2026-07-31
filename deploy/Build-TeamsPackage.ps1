[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateScript({
        $parsed = [Guid]::Empty
        [Guid]::TryParse($_, [ref] $parsed) -and $parsed -ne [Guid]::Empty
    })]
    [string] $BotAppId,

    [ValidatePattern("^\d+\.\d+\.\d+$")]
    [string] $PackageVersion = "0.3.0",

    [string] $OutputPath = ""
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$packageSource = Join-Path $repositoryRoot "appPackage"
$manifestPath = Join-Path $packageSource "manifest.json"
if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = Join-Path $repositoryRoot "CsaMeetingCoach-Teams.zip"
}
else {
    $OutputPath = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath(
        $OutputPath)
}

foreach ($requiredFile in @("manifest.json", "color.png", "outline.png")) {
    $path = Join-Path $packageSource $requiredFile
    if (-not (Test-Path $path -PathType Leaf)) {
        throw "Required Teams package file is missing: $path"
    }
}

$manifest = Get-Content $manifestPath -Raw | ConvertFrom-Json
$manifest.version = $PackageVersion
$botDefinition = [ordered]@{
    botId = $BotAppId
    scopes = @("groupChat")
    supportsFiles = $false
    isNotificationOnly = $false
    supportsCalling = $true
    supportsVideo = $false
}

if ($manifest.PSObject.Properties.Name -contains "bots") {
    $manifest.bots = @($botDefinition)
}
else {
    $manifest | Add-Member -NotePropertyName "bots" -NotePropertyValue @($botDefinition)
}

$temporaryDirectory = Join-Path `
    $env:TEMP `
    ("csa-meeting-coach-teams-" + [Guid]::NewGuid().ToString("N"))
try {
    New-Item -ItemType Directory -Path $temporaryDirectory -Force | Out-Null
    $json = $manifest | ConvertTo-Json -Depth 20
    $utf8WithoutBom = [Text.UTF8Encoding]::new($false)
    [IO.File]::WriteAllText(
        (Join-Path $temporaryDirectory "manifest.json"),
        $json,
        $utf8WithoutBom)
    Copy-Item `
        -Path (Join-Path $packageSource "color.png") `
        -Destination $temporaryDirectory
    Copy-Item `
        -Path (Join-Path $packageSource "outline.png") `
        -Destination $temporaryDirectory

    $outputDirectory = Split-Path -Parent $OutputPath
    if (-not [string]::IsNullOrWhiteSpace($outputDirectory)) {
        New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
    }
    Remove-Item $OutputPath -Force -ErrorAction SilentlyContinue
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [IO.Compression.ZipFile]::CreateFromDirectory(
        $temporaryDirectory,
        $OutputPath,
        [IO.Compression.CompressionLevel]::Optimal,
        $false)
}
finally {
    Remove-Item $temporaryDirectory -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Output "Unified Teams package created at $OutputPath."
