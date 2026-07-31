[CmdletBinding()]
param(
    [string]$PublishRoot = (Join-Path (Split-Path -Parent $PSScriptRoot) 'artifacts\publish\app')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if (-not (Test-Path -LiteralPath $PublishRoot -PathType Container)) {
    throw "Publish directory was not found: $PublishRoot"
}

$forbiddenPathPatterns = @(
    'C:\Users\',
    '/Users/',
    '/home/'
)

$violations = [System.Collections.Generic.List[string]]::new()
$files = Get-ChildItem -LiteralPath $PublishRoot -File -Recurse

foreach ($file in $files) {
    if ($file.Extension -ieq '.pdb') {
        $violations.Add("Unexpected PDB: $($file.FullName)")
        continue
    }

    $bytes = [System.IO.File]::ReadAllBytes($file.FullName)
    $texts = @(
        [System.Text.Encoding]::UTF8.GetString($bytes),
        [System.Text.Encoding]::Unicode.GetString($bytes)
    )

    foreach ($pattern in $forbiddenPathPatterns) {
        if ($texts.Where({ $_.IndexOf($pattern, [StringComparison]::OrdinalIgnoreCase) -ge 0 }).Count -gt 0) {
            $violations.Add("Absolute user path '$pattern' in $($file.FullName)")
        }
    }
}

if ($violations.Count -gt 0) {
    throw ($violations -join [Environment]::NewLine)
}

Write-Host "Release privacy check passed for $($files.Count) files."
