Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Get-RepositoryRoot {
    return (Split-Path -Parent $PSScriptRoot)
}

function Get-DotNetCommand {
    $command = Get-Command dotnet -ErrorAction SilentlyContinue
    if (-not $command) {
        throw '.NET SDK 10.0.302 is required. Install it from https://dotnet.microsoft.com/download/dotnet/10.0 and reopen PowerShell.'
    }

    return $command.Source
}

function Assert-DotNetVersion {
    $dotnet = Get-DotNetCommand
    $actual = (& $dotnet --version).Trim()
    if ($actual -ne '10.0.302') {
        throw "Expected .NET SDK 10.0.302 but found $actual. global.json intentionally disables roll-forward."
    }

    return $dotnet
}

function Assert-NodeTools {
    $node = Get-Command node -ErrorAction SilentlyContinue
    $npm = Get-Command npm -ErrorAction SilentlyContinue
    if (-not $node -or -not $npm) {
        throw 'Node.js 24 LTS with npm 11 or newer is required. Install it manually, then reopen PowerShell.'
    }

    $nodeVersion = (& $node.Source --version).TrimStart('v').Split('.')[0]
    if ([int]$nodeVersion -lt 24) {
        throw "Node.js 24 or newer is required; found $(& $node.Source --version)."
    }

    $npmVersion = (& $npm.Source --version).Trim().Split('.')[0]
    if ([int]$npmVersion -lt 11) {
        throw "npm 11 or newer is required; found $(& $npm.Source --version)."
    }

    return @($node.Source, $npm.Source)
}

function Initialize-LocalDataDirectories {
    $root = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'eslee\DownloadRouter'
    @('data', 'logs', 'diagnostics', 'native-host') | ForEach-Object {
        $path = Join-Path $root $_
        if (-not (Test-Path -LiteralPath $path)) {
            New-Item -ItemType Directory -Path $path | Out-Null
        }
    }

    return $root
}
