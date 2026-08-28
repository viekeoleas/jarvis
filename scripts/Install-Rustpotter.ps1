[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$version = '3.0.2'
$expectedHash = 'E3C96BDA19AA5CFE7D401CDCB3DC31DF3CE27F4DF5A6D57FC58F3ADABBEA0E24'
$uri = 'https://github.com/GiviMAD/rustpotter-cli/releases/download/v3.0.2/rustpotter-cli_win_x86_64.exe'
$localData = [Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)
$toolRoot = Join-Path $localData "Jarvis\Tools\rustpotter-cli\v$version"
$executable = Join-Path $toolRoot 'rustpotter-cli.exe'
$temporary = "$executable.download"
New-Item -ItemType Directory -Path $toolRoot -Force | Out-Null

try {
    Invoke-WebRequest -Uri $uri -OutFile $temporary -UseBasicParsing
    $actualHash = (Get-FileHash -LiteralPath $temporary -Algorithm SHA256).Hash
    if ($actualHash -ne $expectedHash) {
        throw 'Rustpotter binary failed SHA-256 verification.'
    }

    Move-Item -LiteralPath $temporary -Destination $executable -Force
}
finally {
    if (Test-Path -LiteralPath $temporary) {
        Remove-Item -LiteralPath $temporary -Force
    }
}

Write-Output $executable
