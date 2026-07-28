[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'native-host-registration.ps1')

function Assert-True([bool]$Condition, [string]$Message) {
    if (-not $Condition) {
        throw $Message
    }
}

Assert-True (Test-WindowsAbsolutePath 'C:\Program Files\eslee\DownloadRouter.NativeHost.exe') 'Drive-absolute paths must be accepted.'
Assert-True (Test-WindowsAbsolutePath '\\server\share\DownloadRouter.NativeHost.exe') 'UNC paths must be accepted.'
Assert-True (-not (Test-WindowsAbsolutePath 'C:relative\DownloadRouter.NativeHost.exe')) 'Drive-relative paths must be rejected.'
Assert-True (-not (Test-WindowsAbsolutePath '.\DownloadRouter.NativeHost.exe')) 'Relative paths must be rejected.'

$developmentExtensionId = 'gilicenlclaemgiijcjjejilikbooggj'
$additionalExtensionId = 'abcdefghijklmnopabcdefghijklmnop'
$origins = @(Get-NativeHostAllowedOrigins $developmentExtensionId @($additionalExtensionId, 'invalid-id', $developmentExtensionId))
Assert-True ($origins.Count -eq 2) 'Only unique extension IDs in the Chromium a-p alphabet must be retained.'
Assert-True ($origins -contains "chrome-extension://$developmentExtensionId/") 'The development extension origin is required.'
Assert-True ($origins -contains "chrome-extension://$additionalExtensionId/") 'A valid additional extension origin must be retained.'

$temporaryFile = [IO.Path]::GetTempFileName()
try {
    Write-Utf8WithoutBom $temporaryFile '{"name":"com.eslee.download_router"}'
    $bytes = [IO.File]::ReadAllBytes($temporaryFile)
    $hasUtf8Bom = $bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF
    Assert-True (-not $hasUtf8Bom) 'Native Messaging manifests must be UTF-8 without BOM.'
    $parsed = [Text.Encoding]::UTF8.GetString($bytes) | ConvertFrom-Json
    Assert-True ($parsed.name -eq 'com.eslee.download_router') 'The BOM-free manifest must remain valid JSON.'
}
finally {
    if (Test-Path -LiteralPath $temporaryFile) {
        Remove-Item -LiteralPath $temporaryFile -Force
    }
}

Write-Host 'Native Host registration compatibility tests passed.'
