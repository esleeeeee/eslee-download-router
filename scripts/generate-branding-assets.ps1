[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$root = Split-Path -Parent $PSScriptRoot
$sourcePath = Join-Path $root 'assets\branding\eslee-download-router.png'
$icoPath = Join-Path $root 'assets\branding\eslee-download-router.ico'
$extensionDirectory = Join-Path $root 'src\DownloadRouter.Extension\icons'
$icoSizes = @(16, 20, 24, 32, 40, 48, 64, 128, 256)
$extensionSizes = @(16, 32, 48, 128)
$artworkScale = 1.18
$smallFrameArtworkScales = @{
    16 = 1.15
    20 = 1.15
}

if (-not (Test-Path -LiteralPath $sourcePath -PathType Leaf)) {
    throw "Branding master asset was not found: $sourcePath"
}

New-Item -ItemType Directory -Path $extensionDirectory -Force | Out-Null

function New-ResizedPngBytes {
    param(
        [Parameter(Mandatory)]
        [System.Drawing.Image]$Source,

        [Parameter(Mandatory)]
        [int]$Size,

        [Parameter(Mandatory)]
        [double]$ArtworkScale
    )

    $bitmap = [System.Drawing.Bitmap]::new(
        $Size,
        $Size,
        [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $stream = [System.IO.MemoryStream]::new()
    try {
        $graphics.CompositingMode = [System.Drawing.Drawing2D.CompositingMode]::SourceCopy
        $graphics.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
        $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
        $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $drawSize = [single]($Size * $ArtworkScale)
        $offset = [single](($Size - $drawSize) / 2.0)
        $graphics.DrawImage(
            $Source,
            [System.Drawing.RectangleF]::new($offset, $offset, $drawSize, $drawSize),
            [System.Drawing.RectangleF]::new(
                0,
                0,
                [single]$Source.Width,
                [single]$Source.Height),
            [System.Drawing.GraphicsUnit]::Pixel)
        $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
        return $stream.ToArray()
    }
    finally {
        $stream.Dispose()
        $graphics.Dispose()
        $bitmap.Dispose()
    }
}

$source = [System.Drawing.Image]::FromFile($sourcePath)
try {
    if ($source.Width -ne $source.Height) {
        throw 'The branding master asset must be square.'
    }

    $icoFrames = @{}
    foreach ($size in $icoSizes) {
        $frameArtworkScale = if ($smallFrameArtworkScales.ContainsKey($size)) {
            $smallFrameArtworkScales[$size]
        }
        else {
            $artworkScale
        }
        $icoFrames[$size] = New-ResizedPngBytes `
            -Source $source `
            -Size $size `
            -ArtworkScale $frameArtworkScale
    }

    $icoStream = [System.IO.File]::Create($icoPath)
    $writer = [System.IO.BinaryWriter]::new($icoStream)
    try {
        $writer.Write([uint16]0)
        $writer.Write([uint16]1)
        $writer.Write([uint16]$icoSizes.Count)

        $offset = 6 + (16 * $icoSizes.Count)
        foreach ($size in $icoSizes) {
            $frame = [byte[]]$icoFrames[$size]
            $writer.Write([byte]$(if ($size -eq 256) { 0 } else { $size }))
            $writer.Write([byte]$(if ($size -eq 256) { 0 } else { $size }))
            $writer.Write([byte]0)
            $writer.Write([byte]0)
            $writer.Write([uint16]1)
            $writer.Write([uint16]32)
            $writer.Write([uint32]$frame.Length)
            $writer.Write([uint32]$offset)
            $offset += $frame.Length
        }

        foreach ($size in $icoSizes) {
            $writer.Write([byte[]]$icoFrames[$size])
        }
    }
    finally {
        $writer.Dispose()
        $icoStream.Dispose()
    }

    foreach ($size in $extensionSizes) {
        $destination = Join-Path $extensionDirectory "icon-$size.png"
        $frameArtworkScale = if ($smallFrameArtworkScales.ContainsKey($size)) {
            $smallFrameArtworkScales[$size]
        }
        else {
            $artworkScale
        }
        [System.IO.File]::WriteAllBytes(
            $destination,
            (New-ResizedPngBytes `
                -Source $source `
                -Size $size `
                -ArtworkScale $frameArtworkScale))
    }
}
finally {
    $source.Dispose()
}

Write-Host "Generated Windows icon: $icoPath"
Write-Host "Generated extension icons: $($extensionSizes -join ', ')"
Write-Host "Artwork scale: $artworkScale (16/20px: 1.15)"
