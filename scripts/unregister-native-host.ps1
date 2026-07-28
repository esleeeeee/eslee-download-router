[CmdletBinding()]
param(
    [ValidateSet('All', 'Whale', 'Edge', 'Chrome', 'Brave', 'Vivaldi', 'Opera')]
    [string[]]$Browser = @('All')
)

& (Join-Path $PSScriptRoot 'register-native-host.ps1') -Action Unregister -Browser $Browser

$manifestPath = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'eslee\DownloadRouter\native-host\com.eslee.download_router.json'
if (Test-Path -LiteralPath $manifestPath -PathType Leaf) {
    Remove-Item -LiteralPath $manifestPath -Force
}
