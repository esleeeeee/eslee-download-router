[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',
    [switch]$PublishNativeHost
)

. (Join-Path $PSScriptRoot 'common.ps1')
$root = Get-RepositoryRoot
$dotnet = Assert-DotNetVersion
$nodeTools = Assert-NodeTools
$gitRevision = & git -C $root rev-parse HEAD 2>$null
$sourceRevisionId = if ($LASTEXITCODE -eq 0) { ([string]$gitRevision).Trim() } else { '' }
$versionProperty = if ($sourceRevisionId) { "-p:SourceRevisionId=$sourceRevisionId" } else { $null }

& $dotnet build (Join-Path $root 'DownloadRouter.slnx') --configuration $Configuration --no-restore --nologo $versionProperty
if ($LASTEXITCODE -ne 0) { throw 'dotnet build failed. Run scripts/bootstrap.ps1 first.' }

Push-Location (Join-Path $root 'src\DownloadRouter.Extension')
try {
    & $nodeTools[1] run build
    if ($LASTEXITCODE -ne 0) { throw 'Extension build failed.' }
}
finally {
    Pop-Location
}

if ($PublishNativeHost) {
    $output = Join-Path $root 'artifacts\native-host'
    & $dotnet publish (Join-Path $root 'src\DownloadRouter.Agent\DownloadRouter.Agent.csproj') --configuration $Configuration --runtime win-x64 --self-contained true --output $output --nologo $versionProperty
    if ($LASTEXITCODE -ne 0) { throw 'Agent publish failed.' }
    & $dotnet publish (Join-Path $root 'src\DownloadRouter.NativeHost\DownloadRouter.NativeHost.csproj') --configuration $Configuration --runtime win-x64 --self-contained true --output $output --nologo $versionProperty
    if ($LASTEXITCODE -ne 0) { throw 'Native Host publish failed.' }
    Write-Host "Published Native Host bundle: $output"
}

Write-Host "Build completed: $Configuration"
