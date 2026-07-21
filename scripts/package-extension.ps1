[CmdletBinding()]
param()

. (Join-Path $PSScriptRoot 'common.ps1')
$root = Get-RepositoryRoot
$nodeTools = Assert-NodeTools
$extension = Join-Path $root 'src\DownloadRouter.Extension'

Push-Location $extension
try {
    & $nodeTools[1] ci
    if ($LASTEXITCODE -ne 0) { throw 'npm ci failed.' }
    & $nodeTools[1] run check
    if ($LASTEXITCODE -ne 0) { throw 'Extension checks failed.' }
}
finally {
    Pop-Location
}

$artifacts = Join-Path $root 'artifacts'
if (-not (Test-Path -LiteralPath $artifacts)) { New-Item -ItemType Directory -Path $artifacts | Out-Null }
$archive = Join-Path $artifacts 'eslee-download-router-extension.zip'
if (Test-Path -LiteralPath $archive) { Remove-Item -LiteralPath $archive }
Compress-Archive -Path (Join-Path $extension 'dist\*') -DestinationPath $archive -CompressionLevel Optimal
Write-Host "Extension package: $archive"
