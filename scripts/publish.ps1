[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release'
)

. (Join-Path $PSScriptRoot 'common.ps1')
$root = Get-RepositoryRoot
$dotnet = Assert-DotNetVersion
$nodeTools = Assert-NodeTools
$publishRoot = Join-Path $root 'artifacts\publish\app'
$stagingRoot = Join-Path $root 'artifacts\publish\staging'

if (Test-Path -LiteralPath $publishRoot) { Remove-Item -LiteralPath $publishRoot -Recurse -Force }
if (Test-Path -LiteralPath $stagingRoot) { Remove-Item -LiteralPath $stagingRoot -Recurse -Force }
New-Item -ItemType Directory -Path $publishRoot -Force | Out-Null
New-Item -ItemType Directory -Path $stagingRoot -Force | Out-Null

& $dotnet publish (Join-Path $root 'src\DownloadRouter.App\DownloadRouter.App.csproj') --configuration $Configuration --runtime win-x64 --self-contained true --output $publishRoot --nologo
if ($LASTEXITCODE -ne 0) { throw 'Settings app publish failed.' }

foreach ($project in @('DownloadRouter.Agent', 'DownloadRouter.NativeHost')) {
    $projectOutput = Join-Path $stagingRoot $project
    & $dotnet publish (Join-Path $root "src\$project\$project.csproj") --configuration $Configuration --runtime win-x64 --self-contained true --output $projectOutput --nologo
    if ($LASTEXITCODE -ne 0) { throw "$project publish failed." }
    Copy-Item -Path (Join-Path $projectOutput '*') -Destination $publishRoot -Recurse -Force
}

Push-Location (Join-Path $root 'src\DownloadRouter.Extension')
try {
    & $nodeTools[1] ci
    if ($LASTEXITCODE -ne 0) { throw 'Extension npm ci failed.' }
    & $nodeTools[1] run check
    if ($LASTEXITCODE -ne 0) { throw 'Extension checks failed.' }
}
finally {
    Pop-Location
}

$extensionOutput = Join-Path $publishRoot 'extension'
$scriptOutput = Join-Path $publishRoot 'scripts'
New-Item -ItemType Directory -Path $extensionOutput -Force | Out-Null
New-Item -ItemType Directory -Path $scriptOutput -Force | Out-Null
Copy-Item -Path (Join-Path $root 'src\DownloadRouter.Extension\dist\*') -Destination $extensionOutput -Recurse -Force
Copy-Item -LiteralPath (Join-Path $root 'scripts\common.ps1') -Destination $scriptOutput
Copy-Item -LiteralPath (Join-Path $root 'scripts\register-native-host.ps1') -Destination $scriptOutput
Copy-Item -LiteralPath (Join-Path $root 'scripts\unregister-native-host.ps1') -Destination $scriptOutput

Write-Host "Self-contained installer payload: $publishRoot"
Write-Host 'Build installer\DownloadRouter.iss with Inno Setup after reviewing signing settings.'
