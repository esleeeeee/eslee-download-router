[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$Root,
    [ValidateRange(1024, 65535)]
    [int]$Port = 8765,
    [ValidateRange(0, 5000)]
    [int]$DelayMilliseconds = 75,
    [ValidateRange(4096, 1048576)]
    [int]$ChunkSize = 65536
)

$rootFull = [IO.Path]::GetFullPath($Root).TrimEnd([IO.Path]::DirectorySeparatorChar)
if (-not (Test-Path -LiteralPath $rootFull -PathType Container)) {
    throw "Download fixture root does not exist: $rootFull"
}

$listener = New-Object Net.HttpListener
$listener.Prefixes.Add("http://127.0.0.1:$Port/")
$listener.Start()
Write-Host "Manual download server listening on http://127.0.0.1:$Port/"

try {
    while ($listener.IsListening) {
        $context = $listener.GetContext()
        try {
            $relative = [Uri]::UnescapeDataString($context.Request.Url.AbsolutePath.TrimStart('/'))
            if ([string]::IsNullOrWhiteSpace($relative) -or $relative.Contains('..')) {
                $context.Response.StatusCode = 404
                $context.Response.Close()
                continue
            }

            $filePath = [IO.Path]::GetFullPath((Join-Path $rootFull $relative))
            if ((-not $filePath.StartsWith(
                        $rootFull + [IO.Path]::DirectorySeparatorChar,
                        [StringComparison]::OrdinalIgnoreCase)) -or
                (-not (Test-Path -LiteralPath $filePath -PathType Leaf))) {
                $context.Response.StatusCode = 404
                $context.Response.Close()
                continue
            }

            $file = Get-Item -LiteralPath $filePath
            $context.Response.StatusCode = 200
            $context.Response.ContentType = 'application/octet-stream'
            $context.Response.ContentLength64 = $file.Length
            $context.Response.AddHeader('Content-Disposition', "attachment; filename=`"$($file.Name)`"")
            $buffer = New-Object byte[] $ChunkSize
            $input = [IO.File]::OpenRead($file.FullName)
            try {
                while (($read = $input.Read($buffer, 0, $buffer.Length)) -gt 0) {
                    $context.Response.OutputStream.Write($buffer, 0, $read)
                    $context.Response.OutputStream.Flush()
                    if ($DelayMilliseconds -gt 0) {
                        Start-Sleep -Milliseconds $DelayMilliseconds
                    }
                }
            }
            finally {
                $input.Dispose()
                $context.Response.OutputStream.Dispose()
            }
        }
        catch [Net.HttpListenerException] {
            if ($listener.IsListening) {
                Write-Warning $_.Exception.GetType().Name
            }
        }
        catch [IO.IOException] {
            # Browser cancellation closes the response stream. This is expected in manual tests.
        }
        finally {
            try { $context.Response.Close() } catch { }
        }
    }
}
finally {
    $listener.Stop()
    $listener.Close()
}
