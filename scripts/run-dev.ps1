[CmdletBinding()]
param(
    [switch]$SkipBuild
)

. (Join-Path $PSScriptRoot 'common.ps1')
$root = Get-RepositoryRoot
if (-not $SkipBuild) {
    & (Join-Path $PSScriptRoot 'build.ps1') -Configuration Debug
}

$targetFramework = 'net10.0'
$agent = Join-Path $root "src\DownloadRouter.Agent\bin\Debug\$targetFramework\DownloadRouter.Agent.exe"
$app = Join-Path $root 'src\DownloadRouter.App\bin\Debug\net10.0-windows10.0.22621.0\win-x64\DownloadRouter.App.exe'
if (-not (Test-Path -LiteralPath $agent)) { throw "Agent executable not found: $agent" }
if (-not (Test-Path -LiteralPath $app)) { throw "App executable not found: $app" }

Start-Process -FilePath $agent -WorkingDirectory (Split-Path $agent -Parent) -WindowStyle Hidden
Start-Process -FilePath $app -WorkingDirectory (Split-Path $app -Parent)
Write-Host 'Agent started in the background and the settings app was opened.'
