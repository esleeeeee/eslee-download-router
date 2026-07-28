function Test-WindowsAbsolutePath([string]$Path) {
    if ([string]::IsNullOrWhiteSpace($Path)) {
        return $false
    }

    $isDriveAbsolute = $Path -match '^[A-Za-z]:[\\/]'
    $isUnc = $Path -match '^\\\\[^\\]+\\[^\\]+'
    return $isDriveAbsolute -or $isUnc
}

function Get-NativeHostAllowedOrigins(
    [string]$DevelopmentExtensionId,
    [string[]]$AdditionalExtensionId = @()
) {
    return @($DevelopmentExtensionId) + $AdditionalExtensionId |
        Where-Object { $_ -match '^[a-p]{32}$' } |
        Select-Object -Unique |
        ForEach-Object { "chrome-extension://$_/" }
}

function Write-Utf8WithoutBom([string]$Path, [string]$Content) {
    $encoding = New-Object System.Text.UTF8Encoding -ArgumentList $false
    [IO.File]::WriteAllText($Path, $Content, $encoding)
}
