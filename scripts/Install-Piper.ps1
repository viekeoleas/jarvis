[CmdletBinding()]
param(
    [string] $PiperVersion = '1.7.0'
)

$ErrorActionPreference = 'Stop'
$localData = [Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)
$toolRoot = Join-Path $localData "Jarvis\Tools\piper\v$PiperVersion"
$voiceRoot = Join-Path $localData "Jarvis\Models\piper\v$PiperVersion"
$python = Join-Path $toolRoot '.venv\Scripts\python.exe'

if (-not (Test-Path -LiteralPath $python)) {
    & python -m venv (Join-Path $toolRoot '.venv')
    if ($LASTEXITCODE -ne 0) {
        throw 'Could not create the local Piper Python environment.'
    }
}

& $python -m pip install --disable-pip-version-check --no-input "piper-tts==$PiperVersion"
if ($LASTEXITCODE -ne 0) {
    throw "Could not install Piper $PiperVersion."
}

& $python -m piper.download_voices --download-dir $voiceRoot `
    ru_RU-dmitri-medium `
    uk_UA-ukrainian_tts-medium `
    en_US-ryan-medium
if ($LASTEXITCODE -ne 0) {
    throw 'Could not download the configured Piper voices.'
}

Write-Output $python
