[CmdletBinding()]
param(
    [ValidateSet('Register', 'Unregister', 'Repair', 'Status')]
    [string]$Action = 'Register',
    [ValidateSet('All', 'Whale', 'Edge', 'Chrome', 'Brave', 'Vivaldi', 'Opera')]
    [string[]]$Browser = @('All'),
    [string]$HostPath,
    [string[]]$AdditionalExtensionId = @()
)

. (Join-Path $PSScriptRoot 'common.ps1')
. (Join-Path $PSScriptRoot 'native-host-registration.ps1')
$root = Get-RepositoryRoot
$hostName = 'com.eslee.download_router'
$developmentExtensionId = 'gilicenlclaemgiijcjjejilikbooggj'

$registryAdapters = [ordered]@{
    Whale = 'HKCU:\Software\Naver\Naver Whale\NativeMessagingHosts'
    Edge = 'HKCU:\Software\Microsoft\Edge\NativeMessagingHosts'
    Chrome = 'HKCU:\Software\Google\Chrome\NativeMessagingHosts'
    Brave = 'HKCU:\Software\BraveSoftware\Brave-Browser\NativeMessagingHosts'
    Vivaldi = 'HKCU:\Software\Vivaldi\NativeMessagingHosts'
    Opera = 'HKCU:\Software\Opera Software\NativeMessagingHosts'
}

$selected = if ($Browser -contains 'All') { @($registryAdapters.Keys) } else { @($Browser) }
$defaultHostPath = Join-Path $root 'artifacts\native-host\DownloadRouter.NativeHost.exe'
if ([string]::IsNullOrWhiteSpace($HostPath)) { $HostPath = $defaultHostPath }
$manifestDirectory = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'eslee\DownloadRouter\native-host'
$manifestPath = Join-Path $manifestDirectory "$hostName.json"

function Get-RegistryKey([string]$browserName) {
    return (Join-Path $registryAdapters[$browserName] $hostName)
}

if ($Action -eq 'Status') {
    foreach ($name in $selected) {
        $key = Get-RegistryKey $name
        $value = if (Test-Path $key) { (Get-Item -Path $key).GetValue('') } else { $null }
        [pscustomobject]@{ Browser = $name; Registered = [bool]$value; Manifest = $value }
    }
    return
}

if ($Action -eq 'Unregister') {
    foreach ($name in $selected) {
        $key = Get-RegistryKey $name
        if (Test-Path $key) { Remove-Item -Path $key -Force }
        Write-Host "Unregistered $name"
    }
    return
}

if (-not (Test-WindowsAbsolutePath $HostPath)) {
    throw 'HostPath must be an absolute path.'
}
$HostPath = [IO.Path]::GetFullPath($HostPath)
if (-not (Test-Path -LiteralPath $HostPath -PathType Leaf)) {
    throw "Native Host executable not found: $HostPath`nRun .\scripts\build.ps1 -Configuration Release -PublishNativeHost first."
}
if ([IO.Path]::GetFileName($HostPath) -ne 'DownloadRouter.NativeHost.exe') {
    throw 'HostPath must point to DownloadRouter.NativeHost.exe.'
}

if (-not (Test-Path -LiteralPath $manifestDirectory)) {
    New-Item -ItemType Directory -Path $manifestDirectory | Out-Null
}
$allowedOrigins = Get-NativeHostAllowedOrigins $developmentExtensionId $AdditionalExtensionId
$manifest = [ordered]@{
    name = $hostName
    description = 'eslee Download Router Native Messaging Host'
    path = $HostPath
    type = 'stdio'
    allowed_origins = @($allowedOrigins)
}
$manifestJson = $manifest | ConvertTo-Json -Depth 4
Write-Utf8WithoutBom $manifestPath ($manifestJson + [Environment]::NewLine)

foreach ($name in $selected) {
    $key = Get-RegistryKey $name
    if (-not (Test-Path $key)) { New-Item -Path $key -Force | Out-Null }
    Set-Item -LiteralPath $key -Value $manifestPath
    Write-Host "Registered $name -> $manifestPath"
}

Write-Host 'Registration is per-user (HKCU); administrator rights were not requested.'
Write-Warning 'Whale, Brave, Vivaldi, and Opera registry adapters require browser-level manual verification on this PC.'
