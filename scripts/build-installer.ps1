[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release'
)

. (Join-Path $PSScriptRoot 'common.ps1')
$root = Get-RepositoryRoot
$versionDocument = [xml](Get-Content -LiteralPath (Join-Path $root 'Directory.Build.props') -Raw)
$appVersion = [string]($versionDocument.Project.PropertyGroup.VersionPrefix | Select-Object -First 1)
if ($appVersion -notmatch '^\d+\.\d+\.\d+$') {
    throw 'Directory.Build.props VersionPrefix must be a three-part SemVer.'
}
$gitRevision = & git -C $root rev-parse HEAD 2>$null
$sourceRevisionId = if ($LASTEXITCODE -eq 0) { ([string]$gitRevision).Trim() } else { 'unknown' }

& (Join-Path $PSScriptRoot 'publish.ps1') -Configuration $Configuration
if ($LASTEXITCODE -ne 0) { throw 'Installer payload publish failed.' }

$isccCandidates = @(
    (Get-Command ISCC.exe -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Source -ErrorAction SilentlyContinue),
    (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
    (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe')
) | Where-Object { $_ -and (Test-Path -LiteralPath $_ -PathType Leaf) } | Select-Object -Unique

$iscc = $isccCandidates | Select-Object -First 1
if (-not $iscc) {
    throw 'Inno Setup 6 was not found. Install the trusted JRSoftware.InnoSetup winget package.'
}

foreach ($binary in @('DownloadRouter.App.exe', 'DownloadRouter.Agent.exe', 'DownloadRouter.NativeHost.exe')) {
    $productVersion = (Get-Item -LiteralPath (Join-Path $root "artifacts\publish\app\$binary")).VersionInfo.ProductVersion
    if (-not $productVersion.StartsWith("$appVersion+", [StringComparison]::Ordinal) -and $productVersion -ne $appVersion) {
        throw "$binary product version '$productVersion' does not match $appVersion."
    }
}

& $iscc "/DAppVersion=$appVersion" "/DAppCommit=$sourceRevisionId" (Join-Path $root 'installer\DownloadRouter.iss')
if ($LASTEXITCODE -ne 0) { throw 'Inno Setup compilation failed.' }

$setup = Join-Path $root 'artifacts\installer\eslee-download-router-setup.exe'
if (-not (Test-Path -LiteralPath $setup -PathType Leaf)) {
    throw 'The expected installer output was not created.'
}

Write-Host "Installer built: $setup"
Write-Host "Product version: $appVersion ($sourceRevisionId)"
