[CmdletBinding()]
param(
    [string]$Configuration = 'Debug'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'common.ps1')
$root = Get-RepositoryRoot
$agentPath = Join-Path $root "src\DownloadRouter.Agent\bin\$Configuration\net10.0\DownloadRouter.Agent.exe"
if (-not (Test-Path -LiteralPath $agentPath)) {
    throw "Agent executable not found: $agentPath. Run scripts\build.ps1 first."
}

$artifactsRoot = [IO.Path]::GetFullPath((Join-Path $root 'artifacts'))
$smokeRoot = [IO.Path]::GetFullPath((Join-Path $artifactsRoot 'agent-smoke'))
if (-not $smokeRoot.StartsWith($artifactsRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Refusing to use a smoke-test directory outside the repository artifacts directory.'
}

New-Item -ItemType Directory -Path $smokeRoot -Force | Out-Null
$previousDataDirectory = $env:DOWNLOAD_ROUTER_DATA_DIR
$env:DOWNLOAD_ROUTER_DATA_DIR = $smokeRoot
$process = $null

try {
    $process = Start-Process -FilePath $agentPath -WorkingDirectory (Split-Path $agentPath -Parent) -WindowStyle Hidden -PassThru
    $pipe = [IO.Pipes.NamedPipeClientStream]::new('.', 'eslee.download-router.agent.v1', [IO.Pipes.PipeDirection]::InOut)
    try {
        $pipe.Connect(5000)
        $requestId = [Guid]::NewGuid()
        $payload = [Text.Encoding]::UTF8.GetBytes((@{
            version = 1
            requestId = $requestId
            command = 'ping'
            payload = @{}
        } | ConvertTo-Json -Compress))
        $length = [BitConverter]::GetBytes([int]$payload.Length)
        $pipe.Write($length, 0, $length.Length)
        $pipe.Write($payload, 0, $payload.Length)
        $pipe.Flush()

        $lengthBuffer = [byte[]]::new(4)
        $read = $pipe.Read($lengthBuffer, 0, 4)
        if ($read -ne 4) { throw 'Agent returned an incomplete response length.' }
        $responseLength = [BitConverter]::ToInt32($lengthBuffer, 0)
        if ($responseLength -le 0 -or $responseLength -gt 1048576) { throw "Agent returned invalid response length: $responseLength" }

        $responseBuffer = [byte[]]::new($responseLength)
        $offset = 0
        while ($offset -lt $responseLength) {
            $count = $pipe.Read($responseBuffer, $offset, $responseLength - $offset)
            if ($count -eq 0) { throw 'Agent disconnected before the response completed.' }
            $offset += $count
        }

        $response = [Text.Encoding]::UTF8.GetString($responseBuffer) | ConvertFrom-Json
        if (-not $response.success -or $response.requestId -ne $requestId.ToString()) {
            throw "Agent ping failed: $($response | ConvertTo-Json -Compress)"
        }

        Write-Host 'DownloadRouter.Agent named-pipe smoke test passed.'
    }
    finally {
        $pipe.Dispose()
    }
}
finally {
    if ($process -and -not $process.HasExited) {
        Stop-Process -Id $process.Id
        [void]$process.WaitForExit(5000)
    }

    $env:DOWNLOAD_ROUTER_DATA_DIR = $previousDataDirectory
    if (Test-Path -LiteralPath $smokeRoot) {
        Remove-Item -LiteralPath $smokeRoot -Recurse -Force
    }
}
