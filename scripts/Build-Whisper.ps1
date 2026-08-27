[CmdletBinding()]
param(
    [ValidateSet('Vulkan', 'Cpu')]
    [string] $Backend = 'Vulkan',
    [string] $WhisperTag = 'b4938'
)

$ErrorActionPreference = 'Stop'

function Resolve-CMake {
    $command = Get-Command cmake -ErrorAction SilentlyContinue
    if ($null -ne $command) {
        return $command.Source
    }

    $candidate = Get-ChildItem "$env:LOCALAPPDATA\Microsoft\WinGet\Packages" `
        -Recurse -File -Filter cmake.exe -ErrorAction SilentlyContinue |
        Sort-Object FullName -Descending |
        Select-Object -First 1 -ExpandProperty FullName
    if ([string]::IsNullOrWhiteSpace($candidate)) {
        throw 'CMake was not found. Install Kitware.CMake with winget.'
    }

    return $candidate
}

function Resolve-VulkanSdk {
    if (-not [string]::IsNullOrWhiteSpace($env:VULKAN_SDK) -and
        (Test-Path -LiteralPath $env:VULKAN_SDK)) {
        return $env:VULKAN_SDK
    }

    $candidate = Get-ChildItem 'C:\VulkanSDK' -Directory -ErrorAction SilentlyContinue |
        Sort-Object Name -Descending |
        Select-Object -First 1 -ExpandProperty FullName
    if ([string]::IsNullOrWhiteSpace($candidate)) {
        throw 'Vulkan SDK was not found. Install KhronosGroup.VulkanSDK with winget.'
    }

    return $candidate
}

$localData = [Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)
$repositoryRoot = Split-Path $PSScriptRoot -Parent
$sourceRoot = Join-Path $repositoryRoot ".build\w-$WhisperTag"
$buildRoot = Join-Path $sourceRoot "build-$($Backend.ToLowerInvariant())"
$installRoot = Join-Path $localData "Jarvis\Tools\whisper.cpp\$WhisperTag\$($Backend.ToLowerInvariant())"
$cmake = Resolve-CMake

if (-not (Test-Path -LiteralPath (Join-Path $sourceRoot '.git'))) {
    New-Item -ItemType Directory -Path (Split-Path $sourceRoot -Parent) -Force | Out-Null
    & git clone --depth 1 --branch $WhisperTag https://github.com/ggml-org/whisper.cpp.git $sourceRoot
    if ($LASTEXITCODE -ne 0) {
        throw "Could not clone whisper.cpp $WhisperTag."
    }
}

$configureArguments = @(
    '-S', $sourceRoot,
    '-B', $buildRoot,
    '-G', 'Visual Studio 17 2022',
    '-A', 'x64',
    '-DWHISPER_BUILD_TESTS=OFF',
    '-DWHISPER_BUILD_SERVER=OFF',
    '-DGGML_NATIVE=ON'
)

if ($Backend -eq 'Vulkan') {
    $vulkanSdk = Resolve-VulkanSdk
    $env:VULKAN_SDK = $vulkanSdk
    $env:Path = "$(Join-Path $vulkanSdk 'Bin');$env:Path"
    $configureArguments += '-DGGML_VULKAN=ON'
}
else {
    $configureArguments += '-DGGML_VULKAN=OFF'
}

& $cmake @configureArguments
if ($LASTEXITCODE -ne 0) {
    throw "whisper.cpp $Backend configuration failed."
}

& $cmake --build $buildRoot --config Release --target whisper-cli --parallel
if ($LASTEXITCODE -ne 0) {
    throw "whisper.cpp $Backend build failed."
}

New-Item -ItemType Directory -Path $installRoot -Force | Out-Null
$binaryRoot = Join-Path $buildRoot 'bin\Release'
Copy-Item -LiteralPath (Join-Path $binaryRoot 'whisper-cli.exe') -Destination $installRoot -Force
Get-ChildItem $binaryRoot -File -Filter '*.dll' | ForEach-Object {
    Copy-Item -LiteralPath $_.FullName -Destination $installRoot -Force
}

$executable = Join-Path $installRoot 'whisper-cli.exe'
Write-Output $executable
