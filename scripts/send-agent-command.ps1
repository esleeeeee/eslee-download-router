[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('ping', 'download.started', 'download.metadata', 'download.changed', 'downloads.active', 'rules.list', 'rules.upsert', 'rules.delete', 'jobs.list', 'jobs.delete', 'selection.complete', 'selection.skip', 'route.change', 'job.retry', 'diagnostics.status')]
    [string]$Command,
    [Parameter(Mandatory = $true)]
    [string]$PayloadJson,
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug'
)

. (Join-Path $PSScriptRoot 'common.ps1')
$root = Get-RepositoryRoot
$hostPath = Join-Path $root "src\DownloadRouter.NativeHost\bin\$Configuration\net10.0\DownloadRouter.NativeHost.exe"
if (-not (Test-Path -LiteralPath $hostPath -PathType Leaf)) {
    throw "Native Host development output was not found: $hostPath"
}

$payload = $PayloadJson | ConvertFrom-Json
$request = @{
    version = 1
    requestId = [guid]::NewGuid()
    command = $Command
    payload = $payload
} | ConvertTo-Json -Depth 20 -Compress
$requestBytes = [Text.Encoding]::UTF8.GetBytes($request)
$prefix = [BitConverter]::GetBytes([int]$requestBytes.Length)
$startInfo = New-Object Diagnostics.ProcessStartInfo
$startInfo.FileName = $hostPath
$startInfo.Arguments = 'chrome-extension://gilicenlclaemgiijcjjejilikbooggj/'
$startInfo.UseShellExecute = $false
$startInfo.CreateNoWindow = $true
$startInfo.RedirectStandardInput = $true
$startInfo.RedirectStandardOutput = $true
$startInfo.RedirectStandardError = $true
$process = [Diagnostics.Process]::Start($startInfo)
try {
    $process.StandardInput.BaseStream.Write($prefix, 0, $prefix.Length)
    $process.StandardInput.BaseStream.Write($requestBytes, 0, $requestBytes.Length)
    $process.StandardInput.BaseStream.Flush()
    $process.StandardInput.Close()

    $responsePrefix = New-Object byte[] 4
    if ($process.StandardOutput.BaseStream.Read($responsePrefix, 0, 4) -ne 4) {
        throw "Native Host returned no framed response: $($process.StandardError.ReadToEnd())"
    }

    $responseLength = [BitConverter]::ToInt32($responsePrefix, 0)
    if ($responseLength -lt 1 -or $responseLength -gt 1048576) {
        throw "Native Host returned an invalid response length: $responseLength"
    }

    $responseBytes = New-Object byte[] $responseLength
    $offset = 0
    while ($offset -lt $responseLength) {
        $read = $process.StandardOutput.BaseStream.Read($responseBytes, $offset, $responseLength - $offset)
        if ($read -le 0) { throw 'Native Host response ended before the declared length.' }
        $offset += $read
    }

    [Text.Encoding]::UTF8.GetString($responseBytes)
}
finally {
    $process.StandardInput.Dispose()
    $process.StandardOutput.Dispose()
    $process.StandardError.Dispose()
    $process.Dispose()
}
