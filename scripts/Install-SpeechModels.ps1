[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$localData = [Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)
$assets = @(
    @{
        Uri = 'https://raw.githubusercontent.com/snakers4/silero-vad/v6.2.1/src/silero_vad/data/silero_vad_16k_op15.onnx'
        Path = Join-Path $localData 'Jarvis\Models\silero-vad\v6.2.1\silero_vad_16k_op15.onnx'
        Sha256 = '7ED98DDBAD84CCAC4CD0AEB3099049280713DF825C610A8ED34543318F1B2C49'
    },
    @{
        Uri = 'https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-large-v3-turbo-q5_0.bin'
        Path = Join-Path $localData 'Jarvis\Models\whisper\b4938\ggml-large-v3-turbo-q5_0.bin'
        Sha256 = '394221709CD5AD1F40C46E6031CA61BCE88931E6E088C188294C6D5A55FFA7E2'
    }
)

foreach ($asset in $assets) {
    $isCurrent = (Test-Path -LiteralPath $asset.Path) -and
        ((Get-FileHash -LiteralPath $asset.Path -Algorithm SHA256).Hash -eq $asset.Sha256)
    if ($isCurrent) {
        Write-Output "Verified $($asset.Path)"
        continue
    }

    $directory = Split-Path $asset.Path -Parent
    New-Item -ItemType Directory -Path $directory -Force | Out-Null
    $temporary = "$($asset.Path).download"
    try {
        Invoke-WebRequest -Uri $asset.Uri -OutFile $temporary -UseBasicParsing
        $actualHash = (Get-FileHash -LiteralPath $temporary -Algorithm SHA256).Hash
        if ($actualHash -ne $asset.Sha256) {
            throw "Speech model hash mismatch for $($asset.Path)."
        }

        Move-Item -LiteralPath $temporary -Destination $asset.Path -Force
        Write-Output "Installed $($asset.Path)"
    }
    finally {
        if (Test-Path -LiteralPath $temporary) {
            Remove-Item -LiteralPath $temporary -Force
        }
    }
}
