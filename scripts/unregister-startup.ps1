[CmdletBinding()]
param()

$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
if (Test-Path -LiteralPath $runKey) {
    Remove-ItemProperty -LiteralPath $runKey -Name 'eslee Download Router' -ErrorAction SilentlyContinue
}

Write-Host 'Removed current-user login startup registration.'
