[CmdletBinding()]
param(
    [ValidateSet('All', 'Whale', 'Edge', 'Chrome', 'Brave', 'Vivaldi', 'Opera')]
    [string[]]$Browser = @('All')
)

& (Join-Path $PSScriptRoot 'register-native-host.ps1') -Action Unregister -Browser $Browser
