[CmdletBinding(DefaultParameterSetName = "Deploy")]
param(
    [Parameter(Mandatory)]
    [string] $SubscriptionKey,

    [Parameter(Mandatory)]
    [ValidatePattern("^[a-z0-9-]+$")]
    [string] $Region,

    [Parameter(Mandatory, ParameterSetName = "Deploy")]
    [ValidateScript({ Test-Path $_ -PathType Leaf })]
    [string] $LanguageDatasetPath,

    [ValidatePattern("^[A-Za-z]{2,3}(?:-[A-Za-z0-9]{2,8})+$")]
    [string] $Locale = "en-US",

    [ValidateLength(1, 90)]
    [string] $ProjectDisplayName = "CSA Meeting Coach",

    [ValidateLength(1, 128)]
    [string] $EndpointDisplayName = "CSA Meeting Coach",

    [string] $ExpectedEndpointId = "",

    [string] $ResultPath = ".\custom-speech-result.json",

    [ValidateRange(60, 7200)]
    [int] $TimeoutSeconds = 3600,

    [Parameter(Mandatory, ParameterSetName = "Cleanup")]
    [switch] $CleanupOnly
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"

$apiVersion = "2025-10-15"
$serviceRoot = "https://$Region.api.cognitive.microsoft.com"
$apiRoot = "$serviceRoot/speechtotext"
$headers = @{ "Ocp-Apim-Subscription-Key" = $SubscriptionKey }

if ([string]::IsNullOrWhiteSpace($SubscriptionKey)) {
    throw "A Speech subscription key is required."
}
if ([string]::IsNullOrWhiteSpace($ProjectDisplayName) -or
    [string]::IsNullOrWhiteSpace($EndpointDisplayName)) {
    throw "Project and endpoint display names cannot be blank."
}

function Invoke-SpeechRequest {
    param(
        [Parameter(Mandatory)][ValidateSet("GET", "POST", "PUT", "PATCH", "DELETE")]
        [string] $Method,
        [Parameter(Mandatory)][string] $Uri,
        [Parameter(Mandatory)][string] $Operation,
        [object] $Body,
        [string] $ContentType = "application/json"
    )

    try {
        $parameters = @{
            Method = $Method
            Uri = $Uri
            Headers = $headers
            UseBasicParsing = $true
            ErrorAction = "Stop"
        }
        if ($PSBoundParameters.ContainsKey("Body")) {
            $parameters.Body = $Body
            $parameters.ContentType = $ContentType
        }
        $response = Invoke-WebRequest @parameters
        if ([string]::IsNullOrWhiteSpace($response.Content)) {
            return $null
        }
        return $response.Content | ConvertFrom-Json
    }
    catch {
        $status = "unknown"
        if ($_.Exception.Response -and $_.Exception.Response.StatusCode) {
            $status = [int] $_.Exception.Response.StatusCode
        }
        throw "Custom Speech operation '$Operation' failed with HTTP status $status."
    }
}

function Get-ResourceId {
    param(
        [Parameter(Mandatory)][object] $Resource,
        [Parameter(Mandatory)][string] $ResourceType
    )

    $candidate = $null
    if ($Resource.PSObject.Properties["self"]) {
        $resourcePath = ([Uri] $Resource.self).AbsolutePath.Trim("/")
        $candidate = $resourcePath.Substring($resourcePath.LastIndexOf("/") + 1)
    }
    elseif ($Resource.PSObject.Properties["id"]) {
        $candidate = [string] $Resource.id
    }
    if ([string]::IsNullOrWhiteSpace($candidate)) {
        throw "Custom Speech returned an invalid $ResourceType identifier."
    }
    return $candidate
}

function Get-ResourcePropertyValue {
    param(
        [AllowNull()][object] $Resource,
        [Parameter(Mandatory)][string] $PropertyName
    )

    if ($null -eq $Resource) {
        return $null
    }
    $property = $Resource.PSObject.Properties[$PropertyName]
    if ($null -eq $property) {
        return $null
    }
    return $property.Value
}

function ConvertTo-CanonicalResourceId {
    param(
        [Parameter(Mandatory)][string] $ResourceId,
        [Parameter(Mandatory)][string] $ResourceType
    )

    $parsedId = [Guid]::Empty
    if (-not [Guid]::TryParseExact($ResourceId, "D", [ref] $parsedId) -or
        $parsedId -eq [Guid]::Empty) {
        throw "Custom Speech returned a non-GUID $ResourceType identifier."
    }
    return $parsedId.ToString("D")
}

function Get-OptionalCanonicalResourceId {
    param([AllowNull()][object] $Resource)

    if ($null -eq $Resource) {
        return $null
    }
    $candidate = $null
    $self = [string] (Get-ResourcePropertyValue `
        -Resource $Resource `
        -PropertyName "self")
    if (-not [string]::IsNullOrWhiteSpace($self)) {
        $resourceUri = $null
        if (-not [Uri]::TryCreate(
                $self,
                [UriKind]::Absolute,
                [ref] $resourceUri)) {
            return $null
        }
        $resourcePath = $resourceUri.AbsolutePath.Trim("/")
        $candidate = $resourcePath.Substring($resourcePath.LastIndexOf("/") + 1)
    }
    else {
        $candidate = [string] (Get-ResourcePropertyValue `
            -Resource $Resource `
            -PropertyName "id")
    }
    $parsedId = [Guid]::Empty
    if ([string]::IsNullOrWhiteSpace($candidate) -or
        -not [Guid]::TryParseExact($candidate, "D", [ref] $parsedId) -or
        $parsedId -eq [Guid]::Empty) {
        return $null
    }
    return $parsedId.ToString("D")
}

function Get-CustomEndpointModelId {
    param(
        [Parameter(Mandatory)][object] $ModelReference,
        [Parameter(Mandatory)][string] $ReferenceType
    )

    $modelId = ConvertTo-CanonicalResourceId `
        -ResourceId (Get-ResourceId `
            -Resource $ModelReference `
            -ResourceType $ReferenceType) `
        -ResourceType $ReferenceType
    $self = [string] (Get-ResourcePropertyValue `
        -Resource $ModelReference `
        -PropertyName "self")
    $modelUri = $null
    if ([string]::IsNullOrWhiteSpace($self) -or
        -not [Uri]::TryCreate(
            $self,
            [UriKind]::Absolute,
            [ref] $modelUri) -or
        $modelUri.Scheme -ne [Uri]::UriSchemeHttps -or
        $modelUri.Host -cne "$Region.api.cognitive.microsoft.com") {
        throw "Custom Speech returned an invalid $ReferenceType reference."
    }
    $referencePath = $modelUri.AbsolutePath.TrimEnd("/")
    if ($referencePath -ceq "/speechtotext/models/$modelId") {
        return $modelId
    }
    if ($referencePath -ceq "/speechtotext/models/base/$modelId") {
        return $null
    }
    throw "Custom Speech returned an unexpected $ReferenceType path."
}

$normalizedExpectedEndpointId = $null
if (-not [string]::IsNullOrEmpty($ExpectedEndpointId)) {
    $normalizedExpectedEndpointId = ConvertTo-CanonicalResourceId `
        -ResourceId $ExpectedEndpointId `
        -ResourceType "expected endpoint"
    if ($normalizedExpectedEndpointId -cne $ExpectedEndpointId) {
        throw "The expected endpoint ID must be a canonical GUID."
    }
}
if ($CleanupOnly -and $null -eq $normalizedExpectedEndpointId) {
    throw "Cleanup mode requires a canonical expected endpoint ID."
}

function Get-Collection {
    param(
        [Parameter(Mandatory)][string] $ResourceType,
        [Parameter(Mandatory)][string] $Operation
    )

    $items = [Collections.Generic.List[object]]::new()
    $nextUri = "$apiRoot/$ResourceType`?api-version=$apiVersion"
    for ($page = 0; $page -lt 100 -and $nextUri; $page++) {
        $result = Invoke-SpeechRequest `
            -Method GET `
            -Uri $nextUri `
            -Operation $Operation
        if ($null -eq $result) {
            break
        }
        $pageItems = if ($result.PSObject.Properties["values"]) {
            @($result.values)
        }
        elseif ($result.PSObject.Properties["value"]) {
            @($result.value)
        }
        else {
            @($result)
        }
        foreach ($item in $pageItems) {
            $items.Add($item)
        }

        $nextUri = $null
        foreach ($propertyName in @("nextLink", "@nextLink", "@odata.nextLink")) {
            if ($result.PSObject.Properties[$propertyName]) {
                $candidateUri = [Uri] $result.$propertyName
                if ($candidateUri.Scheme -ne [Uri]::UriSchemeHttps -or
                    $candidateUri.Host -cne "$Region.api.cognitive.microsoft.com") {
                    throw "Custom Speech returned an invalid pagination link."
                }
                $nextUri = $candidateUri.AbsoluteUri
                break
            }
        }
    }
    if ($nextUri) {
        throw "Custom Speech collection pagination exceeded the safety limit."
    }
    return @($items)
}

function Wait-SpeechResource {
    param(
        [Parameter(Mandatory)][string] $ResourceType,
        [Parameter(Mandatory)][string] $ResourceId,
        [Parameter(Mandatory)][string] $Operation,
        [Parameter(Mandatory)][DateTime] $Deadline
    )

    while ([DateTime]::UtcNow -lt $Deadline) {
        $resource = Invoke-SpeechRequest `
            -Method GET `
            -Uri "$apiRoot/$ResourceType/$ResourceId`?api-version=$apiVersion" `
            -Operation $Operation
        $status = [string] $resource.status
        if ($status -eq "Succeeded") {
            return $resource
        }
        if ($status -eq "Failed") {
            throw "Custom Speech operation '$Operation' entered Failed state."
        }
        Start-Sleep -Seconds 10
    }
    throw "Custom Speech operation '$Operation' timed out."
}

function ConvertTo-JsonBody {
    param([Parameter(Mandatory)][object] $Value)
    return ConvertTo-Json -InputObject $Value -Depth 10 -Compress
}

function Get-DetailedCollection {
    param(
        [Parameter(Mandatory)][ValidateSet("endpoints", "models", "datasets")]
        [string] $ResourceType,
        [Parameter(Mandatory)][string] $Operation
    )

    $summaries = Get-Collection `
        -ResourceType $ResourceType `
        -Operation $Operation
    $details = [Collections.Generic.List[object]]::new()
    foreach ($summary in $summaries) {
        $resourceId = Get-OptionalCanonicalResourceId -Resource $summary
        if ([string]::IsNullOrWhiteSpace($resourceId)) {
            throw "Custom Speech returned an invalid $ResourceType inventory identifier."
        }
        $detail = Invoke-SpeechRequest `
            -Method GET `
            -Uri "$apiRoot/$ResourceType/$resourceId`?api-version=$apiVersion" `
            -Operation "get $ResourceType inventory detail"
        if ($null -eq $detail) {
            throw "Custom Speech returned an empty $ResourceType inventory detail."
        }
        $details.Add($detail)
    }
    return @($details)
}

function Test-ResourceBelongsToProject {
    param(
        [Parameter(Mandatory)][object] $Resource,
        [Parameter(Mandatory)][string] $ExpectedProjectId
    )

    $project = Get-ResourcePropertyValue `
        -Resource $Resource `
        -PropertyName "project"
    $resourceProjectId = Get-OptionalCanonicalResourceId -Resource $project
    return $null -ne $resourceProjectId -and
        $resourceProjectId -ceq $ExpectedProjectId
}

function Test-IsManagedTimestampedResource {
    param(
        [Parameter(Mandatory)][object] $Resource,
        [Parameter(Mandatory)][ValidateSet("language", "model")]
        [string] $ResourceKind,
        [Parameter(Mandatory)][string] $ExpectedProjectId
    )

    $displayName = [string] (Get-ResourcePropertyValue `
        -Resource $Resource `
        -PropertyName "displayName")
    $namePattern = "\A" +
        [Regex]::Escape("$ProjectDisplayName $ResourceKind ") +
        "[0-9]{8}-[0-9]{6}\z"
    if ([string]::IsNullOrWhiteSpace($displayName) -or
        -not [Regex]::IsMatch(
            $displayName,
            $namePattern,
            [Text.RegularExpressions.RegexOptions]::CultureInvariant)) {
        return $false
    }
    if (-not (Test-ResourceBelongsToProject `
            -Resource $Resource `
            -ExpectedProjectId $ExpectedProjectId)) {
        return $false
    }
    $resourceLocale = [string] (Get-ResourcePropertyValue `
        -Resource $Resource `
        -PropertyName "locale")
    return $resourceLocale -ceq $Locale
}

function Add-ProtectedDatasetReferences {
    param(
        [Parameter(Mandatory)][object] $Model,
        [Parameter(Mandatory)]
        [AllowEmptyCollection()]
        [Collections.Generic.HashSet[string]] $ProtectedDatasetIds
    )

    $datasetReferences = Get-ResourcePropertyValue `
        -Resource $Model `
        -PropertyName "datasets"
    $datasetReferences = @($datasetReferences)
    if ($datasetReferences.Count -eq 0) {
        return 0
    }
    $referenceCount = 0
    foreach ($datasetReference in $datasetReferences) {
        $datasetId = ConvertTo-CanonicalResourceId `
            -ResourceId (Get-ResourceId `
                -Resource $datasetReference `
                -ResourceType "protected model dataset") `
            -ResourceType "protected model dataset"
        $ProtectedDatasetIds.Add($datasetId) | Out-Null
        $referenceCount++
    }
    return $referenceCount
}

function Remove-ObsoleteSpeechResources {
    param(
        [Parameter(Mandatory)][string] $ExpectedProjectId,
        [Parameter(Mandatory)][object] $ActiveEndpoint
    )

    $activeModelReference = Get-ResourcePropertyValue `
        -Resource $ActiveEndpoint `
        -PropertyName "model"
    if ($null -eq $activeModelReference) {
        throw "The active endpoint model could not be verified before cleanup."
    }
    $activeModelId = Get-CustomEndpointModelId `
        -ModelReference $activeModelReference `
        -ReferenceType "active endpoint model"
    if ($null -eq $activeModelId) {
        throw "The active endpoint does not reference a custom model."
    }

    # Complete every read and protection check before issuing the first DELETE.
    $endpointDetails = Get-DetailedCollection `
        -ResourceType "endpoints" `
        -Operation "inventory endpoints for cleanup"
    $modelDetails = Get-DetailedCollection `
        -ResourceType "models" `
        -Operation "inventory models for cleanup"
    $datasetDetails = Get-DetailedCollection `
        -ResourceType "datasets" `
        -Operation "inventory datasets for cleanup"

    $protectedModelIds =
        [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $protectedModelIds.Add($activeModelId) | Out-Null
    foreach ($endpointDetail in $endpointDetails) {
        $modelReference = Get-ResourcePropertyValue `
            -Resource $endpointDetail `
            -PropertyName "model"
        if ($null -eq $modelReference) {
            continue
        }
        $referencedModelId = Get-CustomEndpointModelId `
            -ModelReference $modelReference `
            -ReferenceType "endpoint model"
        if ($null -eq $referencedModelId) {
            continue
        }
        $protectedModelIds.Add($referencedModelId) | Out-Null
    }

    $modelsById = @{}
    $managedModels = [Collections.Generic.List[object]]::new()
    foreach ($modelDetail in $modelDetails) {
        $modelId = Get-OptionalCanonicalResourceId -Resource $modelDetail
        if ([string]::IsNullOrWhiteSpace($modelId)) {
            throw "Custom Speech returned an invalid model inventory detail."
        }
        $modelsById[$modelId] = $modelDetail
        if (Test-IsManagedTimestampedResource `
                -Resource $modelDetail `
                -ResourceKind "model" `
                -ExpectedProjectId $ExpectedProjectId) {
            $managedModels.Add([pscustomobject]@{
                Id = $modelId
                Resource = $modelDetail
            })
        }
    }

    if (-not $modelsById.ContainsKey($activeModelId)) {
        $activeModel = Invoke-SpeechRequest `
            -Method GET `
            -Uri "$apiRoot/models/$activeModelId`?api-version=$apiVersion" `
            -Operation "get active model for cleanup"
        if ($null -eq $activeModel) {
            throw "The active endpoint model detail could not be verified."
        }
        $modelsById[$activeModelId] = $activeModel
    }
    $activeModelDetail = $modelsById[$activeModelId]
    if (-not (Test-ResourceBelongsToProject `
            -Resource $activeModelDetail `
            -ExpectedProjectId $ExpectedProjectId)) {
        throw "The active endpoint model belongs to a different project."
    }
    $activeModelLocale = [string] (Get-ResourcePropertyValue `
        -Resource $activeModelDetail `
        -PropertyName "locale")
    if ($activeModelLocale -cne $Locale) {
        throw "The active endpoint model uses a different locale."
    }

    $protectedDatasetIds =
        [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($protectedModelId in $protectedModelIds) {
        if (-not $modelsById.ContainsKey($protectedModelId)) {
            $protectedModel = Invoke-SpeechRequest `
                -Method GET `
                -Uri "$apiRoot/models/$protectedModelId`?api-version=$apiVersion" `
                -Operation "get protected endpoint model for cleanup"
            if ($null -eq $protectedModel) {
                throw "A protected endpoint model detail could not be verified."
            }
            $modelsById[$protectedModelId] = $protectedModel
        }
        $protectedModelDetail = $modelsById[$protectedModelId]
        $protectedReferenceCount = Add-ProtectedDatasetReferences `
            -Model $protectedModelDetail `
            -ProtectedDatasetIds $protectedDatasetIds
        if ($protectedModelId -ceq $activeModelId -and
            $protectedReferenceCount -eq 0) {
            throw "The active custom model did not expose its dataset references."
        }
    }

    $managedDatasets = [Collections.Generic.List[object]]::new()
    foreach ($datasetDetail in $datasetDetails) {
        $datasetId = Get-OptionalCanonicalResourceId -Resource $datasetDetail
        if ([string]::IsNullOrWhiteSpace($datasetId)) {
            throw "Custom Speech returned an invalid dataset inventory detail."
        }
        if (Test-IsManagedTimestampedResource `
                -Resource $datasetDetail `
                -ResourceKind "language" `
                -ExpectedProjectId $ExpectedProjectId) {
            $managedDatasets.Add([pscustomobject]@{
                Id = $datasetId
                Resource = $datasetDetail
            })
        }
    }

    $obsoleteModels = @($managedModels |
        Where-Object { -not $protectedModelIds.Contains($_.Id) })
    $obsoleteDatasets = @($managedDatasets |
        Where-Object { -not $protectedDatasetIds.Contains($_.Id) })
    if (@($obsoleteModels | Where-Object { $_.Id -ceq $activeModelId }).Count -gt 0) {
        throw "Cleanup attempted to classify the active endpoint model as obsolete."
    }
    if (@($obsoleteDatasets |
            Where-Object { $protectedDatasetIds.Contains($_.Id) }).Count -gt 0) {
        throw "Cleanup attempted to classify a protected dataset as obsolete."
    }

    foreach ($obsoleteModel in $obsoleteModels) {
        Invoke-SpeechRequest `
            -Method DELETE `
            -Uri "$apiRoot/models/$($obsoleteModel.Id)`?api-version=$apiVersion" `
            -Operation "delete obsolete managed model" | Out-Null
    }
    foreach ($obsoleteDataset in $obsoleteDatasets) {
        Invoke-SpeechRequest `
            -Method DELETE `
            -Uri "$apiRoot/datasets/$($obsoleteDataset.Id)`?api-version=$apiVersion" `
            -Operation "delete obsolete managed dataset" | Out-Null
    }

    return [pscustomobject]@{
        ProtectedModels = $protectedModelIds.Count
        ProtectedDatasets = $protectedDatasetIds.Count
        DeletedModels = $obsoleteModels.Count
        DeletedDatasets = $obsoleteDatasets.Count
    }
}

$deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
$timestamp = [DateTime]::UtcNow.ToString("yyyyMMdd-HHmmss")

$projects = Get-Collection -ResourceType "projects" -Operation "list projects"
$namedProjects = @($projects |
    Where-Object {
        [string] (Get-ResourcePropertyValue `
            -Resource $_ `
            -PropertyName "displayName") -ceq $ProjectDisplayName
    })
if ($namedProjects.Count -gt 1) {
    throw "Multiple exact-name Custom Speech projects exist."
}
$project = $namedProjects | Select-Object -First 1
if ($null -ne $project) {
    $projectLocale = [string] (Get-ResourcePropertyValue `
        -Resource $project `
        -PropertyName "locale")
    if ([string]::IsNullOrWhiteSpace($projectLocale)) {
        throw "The exact-name Custom Speech project locale could not be verified."
    }
    if ($projectLocale -cne $Locale) {
        throw "The exact-name Custom Speech project uses a different locale."
    }
}
if ($null -eq $project -and $CleanupOnly) {
    throw "Cleanup mode requires the existing exact-name Custom Speech project."
}
if ($null -eq $project) {
    $project = Invoke-SpeechRequest `
        -Method POST `
        -Uri "$apiRoot/projects?api-version=$apiVersion" `
        -Operation "create project" `
        -Body (ConvertTo-JsonBody @{
            displayName = $ProjectDisplayName
            locale = $Locale
            description = "Privacy-safe text-only language adaptation for CSA Meeting Coach."
        })
}
$projectId = ConvertTo-CanonicalResourceId `
    -ResourceId (Get-ResourceId -Resource $project -ResourceType "project") `
    -ResourceType "project"
$projectReference = @{
    self = "${apiRoot}/projects/${projectId}?api-version=$apiVersion"
}

$endpoint = $null
$endpointSelectedById = $null -ne $normalizedExpectedEndpointId
if ($endpointSelectedById) {
    $endpoint = Invoke-SpeechRequest `
        -Method GET `
        -Uri "$apiRoot/endpoints/$normalizedExpectedEndpointId`?api-version=$apiVersion" `
        -Operation "get expected endpoint"
}
else {
    $endpoints = Get-Collection -ResourceType "endpoints" -Operation "list endpoints"
    $namedEndpoints = @($endpoints |
        Where-Object {
            [string] (Get-ResourcePropertyValue `
                -Resource $_ `
                -PropertyName "displayName") -ceq $EndpointDisplayName
        })
    if ($namedEndpoints.Count -gt 1) {
        throw "Multiple exact-name Custom Speech endpoints exist."
    }
    $endpoint = $namedEndpoints | Select-Object -First 1
}
if ($null -ne $endpoint) {
    if ($endpointSelectedById) {
        $reusableEndpointDisplayName = [string] (Get-ResourcePropertyValue `
            -Resource $endpoint `
            -PropertyName "displayName")
        if ($reusableEndpointDisplayName -cne $EndpointDisplayName) {
            throw "The reusable Custom Speech endpoint has an unexpected display name."
        }
    }
    $reusableEndpointLocale = [string] (Get-ResourcePropertyValue `
        -Resource $endpoint `
        -PropertyName "locale")
    if ([string]::IsNullOrWhiteSpace($reusableEndpointLocale)) {
        throw "The reusable Custom Speech endpoint locale could not be verified."
    }
    if ($reusableEndpointLocale -cne $Locale) {
        throw "The reusable Custom Speech endpoint uses a different locale."
    }
    $endpointProject = Get-ResourcePropertyValue `
        -Resource $endpoint `
        -PropertyName "project"
    if ($null -eq $endpointProject) {
        throw "The reusable Custom Speech endpoint project could not be verified."
    }
    $endpointProjectId = ConvertTo-CanonicalResourceId `
        -ResourceId (Get-ResourceId `
            -Resource $endpointProject `
            -ResourceType "endpoint project") `
        -ResourceType "endpoint project"
    if ($endpointProjectId -cne $projectId) {
        throw "The reusable Custom Speech endpoint belongs to a different project."
    }
}
if ($CleanupOnly -and $null -eq $endpoint) {
    throw "The expected Custom Speech endpoint could not be verified for cleanup."
}
Write-Output "Custom Speech project and endpoint state validated."

if ($CleanupOnly) {
    $cleanupResult = Remove-ObsoleteSpeechResources `
        -ExpectedProjectId $projectId `
        -ActiveEndpoint $endpoint
    Write-Output ((
        "Custom Speech cleanup completed: {0} model(s) and {1} dataset(s) " +
        "deleted; {2} endpoint-referenced model(s) and {3} dataset(s) protected."
    ) -f
        $cleanupResult.DeletedModels,
        $cleanupResult.DeletedDatasets,
        $cleanupResult.ProtectedModels,
        $cleanupResult.ProtectedDatasets)
    return
}

$dataset = Invoke-SpeechRequest `
    -Method POST `
    -Uri "$apiRoot/datasets?api-version=$apiVersion" `
    -Operation "create language dataset" `
    -Body (ConvertTo-JsonBody @{
        displayName = "$ProjectDisplayName language $timestamp"
        locale = $Locale
        kind = "Language"
        project = $projectReference
        description = "Generated public product vocabulary and meeting-context text."
    })
$datasetId = ConvertTo-CanonicalResourceId `
    -ResourceId (Get-ResourceId -Resource $dataset -ResourceType "dataset") `
    -ResourceType "dataset"

$datasetBytes = [IO.File]::ReadAllBytes((Resolve-Path $LanguageDatasetPath))
if ($datasetBytes.Length -eq 0) {
    throw "The language dataset file is empty."
}
if ($datasetBytes.Length -lt 3 -or
    $datasetBytes[0] -ne 0xEF -or
    $datasetBytes[1] -ne 0xBB -or
    $datasetBytes[2] -ne 0xBF) {
    throw "The language dataset must be UTF-8 with a byte order mark."
}
$blockSize = 8MB
$blocks = [Collections.Generic.List[object]]::new()
for ($offset = 0; $offset -lt $datasetBytes.Length; $offset += $blockSize) {
    $length = [Math]::Min($blockSize, $datasetBytes.Length - $offset)
    $blockBytes = [byte[]]::new($length)
    [Array]::Copy($datasetBytes, $offset, $blockBytes, 0, $length)
    $blockId = [Convert]::ToBase64String(
        [Text.Encoding]::UTF8.GetBytes(("{0:D8}" -f ($offset / $blockSize))))
    $encodedBlockId = [Uri]::EscapeDataString($blockId)
    Invoke-SpeechRequest `
        -Method PUT `
        -Uri "$apiRoot/datasets/$datasetId/blocks?blockid=$encodedBlockId&api-version=$apiVersion" `
        -Operation "upload language dataset block" `
        -Body $blockBytes `
        -ContentType "application/octet-stream" | Out-Null
    $blocks.Add([ordered]@{ kind = "Uncommitted"; id = $blockId })
}

Invoke-SpeechRequest `
    -Method POST `
    -Uri "$apiRoot/datasets/$datasetId/blocks`:commit?api-version=$apiVersion" `
    -Operation "commit language dataset blocks" `
    -Body (ConvertTo-JsonBody @($blocks)) | Out-Null
$dataset = Wait-SpeechResource `
    -ResourceType "datasets" `
    -ResourceId $datasetId `
    -Operation "process language dataset" `
    -Deadline $deadline
Write-Output "Custom Speech language dataset processed."

$model = Invoke-SpeechRequest `
    -Method POST `
    -Uri "$apiRoot/models?api-version=$apiVersion" `
    -Operation "create model" `
    -Body (ConvertTo-JsonBody @{
        displayName = "$ProjectDisplayName model $timestamp"
        locale = $Locale
        project = $projectReference
        datasets = @(@{
            self = "${apiRoot}/datasets/${datasetId}?api-version=$apiVersion"
        })
        description = "Text-only language adaptation for CSA Meeting Coach."
    })
$modelId = ConvertTo-CanonicalResourceId `
    -ResourceId (Get-ResourceId -Resource $model -ResourceType "model") `
    -ResourceType "model"
$model = Wait-SpeechResource `
    -ResourceType "models" `
    -ResourceId $modelId `
    -Operation "train model" `
    -Deadline $deadline
if ($model.PSObject.Properties["properties"] -and
    $model.properties.PSObject.Properties["features"] -and
    $model.properties.features.PSObject.Properties["supportsEndpoints"] -and
    $model.properties.features.supportsEndpoints -ne $true) {
    throw "The trained Custom Speech model does not support real-time endpoints."
}
Write-Output "Custom Speech model trained."
$modelReference = @{
    self = "${apiRoot}/models/${modelId}?api-version=$apiVersion"
}

$endpointId = $null
if ($null -eq $endpoint) {
    $endpoint = Invoke-SpeechRequest `
        -Method POST `
        -Uri "$apiRoot/endpoints?api-version=$apiVersion" `
        -Operation "create endpoint" `
        -Body (ConvertTo-JsonBody @{
            displayName = $EndpointDisplayName
            locale = $Locale
            project = $projectReference
            model = $modelReference
            properties = @{ loggingEnabled = $false }
        })
}
else {
    $endpointId = ConvertTo-CanonicalResourceId `
        -ResourceId (Get-ResourceId -Resource $endpoint -ResourceType "endpoint") `
        -ResourceType "endpoint"
    if ($null -ne $normalizedExpectedEndpointId -and
        $endpointId -cne $normalizedExpectedEndpointId) {
        throw "The reusable Custom Speech endpoint ID does not match the expected ID."
    }
    Invoke-SpeechRequest `
        -Method PATCH `
        -Uri "$apiRoot/endpoints/$endpointId`?api-version=$apiVersion" `
        -Operation "update endpoint" `
        -Body (ConvertTo-JsonBody @{
            model = $modelReference
            properties = @{ contentLoggingEnabled = $false }
        }) | Out-Null
}

if ($null -eq $endpoint) {
    throw "Custom Speech returned an invalid endpoint response."
}
if ([string]::IsNullOrWhiteSpace($endpointId)) {
    $endpointId = Get-ResourceId -Resource $endpoint -ResourceType "endpoint"
}
$parsedEndpointId = [Guid]::Empty
if (-not [Guid]::TryParseExact($endpointId, "D", [ref] $parsedEndpointId) -or
    $parsedEndpointId -eq [Guid]::Empty) {
    throw "Custom Speech returned a non-GUID endpoint identifier."
}
$endpointId = $parsedEndpointId.ToString("D")
$endpoint = Wait-SpeechResource `
    -ResourceType "endpoints" `
    -ResourceId $endpointId `
    -Operation "deploy endpoint" `
    -Deadline $deadline
if (-not $endpoint.PSObject.Properties["model"] -or
    (Get-ResourceId -Resource $endpoint.model -ResourceType "endpoint model") -cne
        $modelId) {
    throw "Custom Speech endpoint does not reference the newly trained model."
}
if (-not $endpoint.PSObject.Properties["properties"] -or
    -not $endpoint.properties.PSObject.Properties["loggingEnabled"]) {
    throw "Custom Speech endpoint logging state could not be verified."
}
if ($endpoint.properties.loggingEnabled -ne $false) {
    throw "Custom Speech endpoint logging was not disabled."
}

$expirationDate = $null
if ($model.PSObject.Properties["expirationDateTime"]) {
    $expirationDate = $model.expirationDateTime
}
elseif ($model.PSObject.Properties["expirationDate"]) {
    $expirationDate = $model.expirationDate
}
elseif ($model.PSObject.Properties["properties"]) {
    if ($model.properties.PSObject.Properties["expirationDateTime"]) {
        $expirationDate = $model.properties.expirationDateTime
    }
    elseif ($model.properties.PSObject.Properties["expirationDate"]) {
        $expirationDate = $model.properties.expirationDate
    }
}

$result = [ordered]@{
    endpointId = $endpointId
    modelId = $modelId
    datasetId = $datasetId
    status = [string] $endpoint.status
    apiVersion = $apiVersion
    modelExpirationDate = $expirationDate
}
$resolvedResultPath = [IO.Path]::GetFullPath($ResultPath)
$resultDirectory = [IO.Path]::GetDirectoryName($resolvedResultPath)
if (-not [string]::IsNullOrWhiteSpace($resultDirectory)) {
    New-Item -ItemType Directory -Path $resultDirectory -Force | Out-Null
}
[IO.File]::WriteAllText(
    $resolvedResultPath,
    (ConvertTo-JsonBody $result),
    [Text.UTF8Encoding]::new($false))
Write-Output "Custom Speech endpoint deployment succeeded with logging disabled."
