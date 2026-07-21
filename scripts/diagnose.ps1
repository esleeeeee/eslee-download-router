[CmdletBinding()]
param()

. (Join-Path $PSScriptRoot 'common.ps1')
$root = Get-RepositoryRoot
Write-Host 'eslee Download Router diagnostics (sanitized)'
Write-Host "OS: $([Environment]::OSVersion.VersionString)"
Write-Host "PowerShell: $($PSVersionTable.PSVersion)"

try { Write-Host ".NET SDK: $(& (Get-DotNetCommand) --version)" } catch { Write-Warning $_.Exception.Message }
$node = Get-Command node -ErrorAction SilentlyContinue
$npm = Get-Command npm -ErrorAction SilentlyContinue
Write-Host "Node.js: $(if ($node) { & $node.Source --version } else { 'not found' })"
Write-Host "npm: $(if ($npm) { & $npm.Source --version } else { 'not found' })"

$browserCandidates = [ordered]@{
    Whale = @((Join-Path $env:LOCALAPPDATA 'Naver\Naver Whale\Application\whale.exe'))
    Edge = @((Join-Path ${env:ProgramFiles(x86)} 'Microsoft\Edge\Application\msedge.exe'), (Join-Path $env:ProgramFiles 'Microsoft\Edge\Application\msedge.exe'))
    Chrome = @((Join-Path $env:ProgramFiles 'Google\Chrome\Application\chrome.exe'), (Join-Path ${env:ProgramFiles(x86)} 'Google\Chrome\Application\chrome.exe'))
    Brave = @((Join-Path $env:ProgramFiles 'BraveSoftware\Brave-Browser\Application\brave.exe'))
    Vivaldi = @((Join-Path $env:LOCALAPPDATA 'Vivaldi\Application\vivaldi.exe'))
    Opera = @((Join-Path $env:LOCALAPPDATA 'Programs\Opera\opera.exe'))
}
foreach ($entry in $browserCandidates.GetEnumerator()) {
    $installed = @($entry.Value | Where-Object { Test-Path -LiteralPath $_ }).Count -gt 0
    Write-Host "$($entry.Key): $(if ($installed) { 'installed' } else { 'not detected' })"
}

& (Join-Path $PSScriptRoot 'register-native-host.ps1') -Action Status -Browser All | Format-Table -AutoSize
Write-Host "Extension load path after build: $(Join-Path $root 'src\DownloadRouter.Extension\dist')"
Write-Host 'No browser profile, full URL, downloaded file, token, or raw log content was collected.'
