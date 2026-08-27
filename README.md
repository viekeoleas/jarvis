# Jarvis

Jarvis is a local-first Windows voice assistant designed to use an existing ChatGPT Plus subscription through the official [Codex app-server](https://learn.chatgpt.com/docs/app-server).

The repository contains a native WPF tray application, compact panel, global activation hotkey, local multilingual speech, and an application-level acceptance seam. Jarvis integrates the official Codex app-server with managed ChatGPT OAuth; it never accepts or stores an OpenAI API key.

## Run the compatibility tracer

Requirements:

- Windows x64
- .NET 10 SDK
- Python 3 for the current Piper runtime
- CMake, Visual Studio 2022 Build Tools, and the Vulkan SDK to build whisper.cpp

Commands:

```powershell
./scripts/Install-SpeechModels.ps1
./scripts/Build-Whisper.ps1 -Backend Vulkan
./scripts/Install-Piper.ps1
dotnet restore Jarvis.slnx
dotnet test Jarvis.slnx
dotnet run --project src/Jarvis.App/Jarvis.App.csproj
```

Jarvis starts in the system tray. `Win+Shift+J` shows the panel and starts or stops a local speech turn. Use the tray menu to show or hide the panel. The Exit tray command shuts down the process and unregisters the hotkey.

Microphone input is 16 kHz mono PCM held in a fixed 60-second memory buffer. Silero VAD ends an utterance after 700 ms of speech-following silence; the separate 60-second session timeout does not invent an utterance. Whisper uses the pinned multilingual `large-v3-turbo-q5_0` model and first attempts the locally built Vulkan backend, with one automatic CPU retry if Vulkan startup fails. RU, UK, and EN responses use local Piper voices. The current deterministic response echoes the recognized command without contacting Codex.

Run the in-memory multilingual probe after installing the assets:

```powershell
dotnet run --project tools/Jarvis.SpeechProbe/Jarvis.SpeechProbe.csproj -c Release
```

The probe synthesizes non-personal RU, UK, EN, and mixed-language fixtures, resamples them in memory, transcribes them, and prints the selected backend plus transcription and first-audio latency. It writes no fixture audio to disk.

See [docs/speech-benchmark.md](docs/speech-benchmark.md) for measurements on the target PC and the current recognition-quality caveat.

On first use, choose **Sign in** in the panel. Codex app-server opens the official ChatGPT browser flow and owns token persistence and refresh under the isolated Jarvis application-data directory. Jarvis accepts only `chatgpt` account mode and reports the plan returned by Codex.

For development, Jarvis currently pins official Codex CLI `0.150.0-alpha.8`. Set `JARVIS_CODEX_PATH` to an exact `codex.exe` when it is not available on `PATH` or through the official ChatGPT VS Code extension. The child process does not inherit OpenAI API-key or alternate API-base environment variables.

## Target-machine compatibility

Measured on the initial target machine:

- Windows 10 Pro 22H2 x64, build 19045
- .NET SDK 10.0.400 and Windows Desktop runtime 10.0.11 install successfully
- `win-x64` is selected by the .NET host
- The Release build completes without warnings, and all acceptance tests pass
- The real WPF process remains responsive while hidden, and `Win+Shift+J` shows and hides the Jarvis window
- A second launch exits without creating another long-running Jarvis process

Windows 10 Pro 22H2 is outside Microsoft support. Compatibility is therefore best effort and must continue to be exercised on this machine as native dependencies are added.

## Privacy boundary

This slice opens no inbound network listener and uses no API key. Microphone PCM, Whisper WAV input, and synthesized speech remain in bounded memory and are cleared after each turn; no audio is written to disk. Jarvis does not write conversation history or diagnostic stderr; the official Codex process owns its private operational and OAuth data under `%LOCALAPPDATA%\Jarvis\Codex`. Threads are ephemeral, and raw prompts or responses are not copied into Jarvis logs.

See [SPEC.md](SPEC.md) for the approved MVP specification.
