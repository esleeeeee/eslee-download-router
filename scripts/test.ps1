[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug'
)

. (Join-Path $PSScriptRoot 'common.ps1')
$root = Get-RepositoryRoot
$dotnet = Assert-DotNetVersion
$nodeTools = Assert-NodeTools

& (Join-Path $PSScriptRoot 'test-native-host-registration.ps1')

& $dotnet test (Join-Path $root 'DownloadRouter.slnx') --configuration $Configuration --no-build --no-restore --nologo
if ($LASTEXITCODE -ne 0) { throw '.NET tests failed.' }

Push-Location (Join-Path $root 'src\DownloadRouter.Extension')
try {
    & $nodeTools[1] run lint
    if ($LASTEXITCODE -ne 0) { throw 'Extension lint failed.' }
    & $nodeTools[1] test
    if ($LASTEXITCODE -ne 0) { throw 'Extension tests failed.' }
}
finally {
    Pop-Location
}

Write-Host 'All automated tests passed.'
