[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release'
)

. (Join-Path $PSScriptRoot 'common.ps1')
$root = Get-RepositoryRoot

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

& $iscc (Join-Path $root 'installer\DownloadRouter.iss')
if ($LASTEXITCODE -ne 0) { throw 'Inno Setup compilation failed.' }

$setup = Join-Path $root 'artifacts\installer\eslee-download-router-setup.exe'
if (-not (Test-Path -LiteralPath $setup -PathType Leaf)) {
    throw 'The expected installer output was not created.'
}

Write-Host "Installer built: $setup"
