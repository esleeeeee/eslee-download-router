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
    Whale = @(
        (Join-Path $env:LOCALAPPDATA 'Naver\Naver Whale\Application\whale.exe'),
        (Join-Path $env:ProgramFiles 'Naver\Naver Whale\Application\whale.exe'),
        (Join-Path ${env:ProgramFiles(x86)} 'Naver\Naver Whale\Application\whale.exe'))
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
$expectedManifestPath = Join-Path $env:LOCALAPPDATA 'eslee\DownloadRouter\native-host\com.eslee.download_router.json'
if (Test-Path -LiteralPath $expectedManifestPath -PathType Leaf) {
    try {
        $bytes = [IO.File]::ReadAllBytes($expectedManifestPath)
        $hasUtf8Bom = $bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF
        $offset = if ($hasUtf8Bom) { 3 } else { 0 }
        $json = [Text.Encoding]::UTF8.GetString($bytes, $offset, $bytes.Length - $offset)
        $manifest = $json | ConvertFrom-Json
        $expectedOrigin = 'chrome-extension://gilicenlclaemgiijcjjejilikbooggj/'
        $hostExists = Test-Path -LiteralPath $manifest.path -PathType Leaf
        $originAllowed = @($manifest.allowed_origins) -contains $expectedOrigin
        Write-Host "Native Host manifest: valid-json=yes, utf8-bom=$hasUtf8Bom, host-exists=$hostExists, development-origin=$originAllowed"

        if ($hostExists) {
            $startInfo = New-Object Diagnostics.ProcessStartInfo
            $startInfo.FileName = $manifest.path
            $startInfo.Arguments = '--self-test'
            $startInfo.UseShellExecute = $false
            $startInfo.CreateNoWindow = $true
            $startInfo.RedirectStandardError = $true
            $process = [Diagnostics.Process]::Start($startInfo)
            $null = $process.StandardError.ReadToEnd()
            $process.WaitForExit()
            Write-Host "Native Host self-test: $(if ($process.ExitCode -eq 0) { 'passed' } else { "failed (exit $($process.ExitCode))" })"
        }
    }
    catch {
        Write-Warning "Native Host manifest validation failed: $($_.Exception.GetType().Name)"
    }
}
else {
    Write-Warning 'Native Host manifest was not found at the expected per-user location.'
}
Write-Host "Extension load path after build: $(Join-Path $root 'src\DownloadRouter.Extension\dist')"
Write-Host 'No browser profile, full URL, downloaded file, token, or raw log content was collected.'
