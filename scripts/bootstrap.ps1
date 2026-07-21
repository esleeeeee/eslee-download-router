[CmdletBinding()]
param()

. (Join-Path $PSScriptRoot 'common.ps1')

$root = Get-RepositoryRoot
Write-Host "Repository: $root"
Write-Host "PowerShell: $($PSVersionTable.PSVersion)"
if ($PSVersionTable.PSVersion.Major -lt 5) {
    throw 'PowerShell 5.1 or newer is required.'
}

$os = [Environment]::OSVersion.Version
Write-Host "Windows: $os"
if ($os.Major -lt 10) {
    throw 'Windows 11 is the supported development environment.'
}

$dotnet = Assert-DotNetVersion
$nodeTools = Assert-NodeTools
Write-Host ".NET SDK: $(& $dotnet --version)"
Write-Host "Node.js: $(& $nodeTools[0] --version)"
Write-Host "npm: $(& $nodeTools[1] --version)"

$localData = Initialize-LocalDataDirectories
$localConfig = Join-Path $localData 'config.local.json'
if (-not (Test-Path -LiteralPath $localConfig)) {
    Copy-Item -LiteralPath (Join-Path $root 'config.example.json') -Destination $localConfig
    Write-Host "Created local config: $localConfig"
}

Write-Host 'Restoring .NET packages...'
& $dotnet restore (Join-Path $root 'DownloadRouter.slnx') --configfile (Join-Path $root 'NuGet.Config') --nologo
if ($LASTEXITCODE -ne 0) { throw 'dotnet restore failed.' }

Write-Host 'Restoring extension packages with npm ci...'
Push-Location (Join-Path $root 'src\DownloadRouter.Extension')
try {
    & $nodeTools[1] ci
    if ($LASTEXITCODE -ne 0) { throw 'npm ci failed.' }
}
finally {
    Pop-Location
}

Write-Host 'Bootstrap completed. No tools or workloads were installed automatically.'
Write-Host 'Next: .\scripts\build.ps1, then .\scripts\test.ps1'
