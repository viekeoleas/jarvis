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

Jarvis starts in the system tray. `Win+Shift+O` shows the panel and starts or stops a local speech turn. A red `СЛУШАЮ` indicator is visible while the microphone is recording. Use the tray menu to show or hide the panel. The Exit tray command shuts down the process and unregisters the hotkey.

Microphone input is 16 kHz mono PCM held in a fixed 60-second memory buffer. Silero VAD ends an utterance after 700 ms of speech-following silence; the separate 60-second session timeout does not invent an utterance. Whisper uses the pinned multilingual `large-v3-turbo-q5_0` model and first attempts the locally built Vulkan backend, with one automatic CPU retry if Vulkan startup fails. After sign-in, only the transcript and concise persona context are sent to Codex. RU, UK, and EN responses are then spoken through local Piper voices. Uncertain transcripts produce a local clarification instead of an invented action.

Run the in-memory multilingual probe after installing the assets:

```powershell
dotnet run --project tools/Jarvis.SpeechProbe/Jarvis.SpeechProbe.csproj -c Release
```

The probe synthesizes non-personal RU, UK, EN, and mixed-language fixtures, resamples them in memory, transcribes them, and prints the selected backend plus transcription and first-audio latency. It writes no fixture audio to disk.

See [docs/speech-benchmark.md](docs/speech-benchmark.md) for measurements on the target PC and the current recognition-quality caveat.

Install Rustpotter with `./scripts/Install-Rustpotter.ps1`, then use **Enrol** in the panel and say “Jarvis” for five prompted samples. Enrolment and detection stay local. Temporary WAV samples are overwritten and deleted after a `.rpw` wake reference is produced; a four-second background sample rejects obviously noisy references and is also discarded. The hotkey remains available without a model. Advanced threshold calibration can set `JARVIS_WAKE_THRESHOLD` (default `0.52`) and `JARVIS_WAKE_MIN_SCORES` (default `10`) before launch; the measured non-audio result is saved under `%LOCALAPPDATA%\Jarvis\Data\wake-calibration.json`.

For an explicit live subscription-to-local-voice smoke test after ChatGPT sign-in:

```powershell
dotnet run --project tools/Jarvis.SpeechProbe/Jarvis.SpeechProbe.csproj -c Release -- --live-codex
```

On first use, choose **Sign in** in the panel. Codex app-server opens the official ChatGPT browser flow and owns token persistence and refresh under the isolated Jarvis application-data directory. Jarvis accepts only `chatgpt` account mode and reports the plan returned by Codex.

For development, Jarvis currently pins official Codex CLI `0.150.0-alpha.8`. Set `JARVIS_CODEX_PATH` to an exact `codex.exe` when it is not available on `PATH` or through the official ChatGPT VS Code extension. The child process does not inherit OpenAI API-key or alternate API-base environment variables.

## Target-machine compatibility

Measured on the initial target machine:

- Windows 10 Pro 22H2 x64, build 19045
- .NET SDK 10.0.400 and Windows Desktop runtime 10.0.11 install successfully
- `win-x64` is selected by the .NET host
- The Release build completes without warnings, and all acceptance tests pass
- The real WPF process remains responsive while hidden, and `Win+Shift+O` opens Jarvis and starts or stops listening
- A second launch exits without creating another long-running Jarvis process

Windows 10 Pro 22H2 is outside Microsoft support. Compatibility is therefore best effort and must continue to be exercised on this machine as native dependencies are added.

## Privacy boundary

This slice opens no inbound network listener and uses no API key. Microphone PCM, Whisper WAV input, and synthesized speech remain in bounded memory and are cleared after each turn; no audio is written to disk. Conversation text and action summaries are stored in `%LOCALAPPDATA%\Jarvis\Data\jarvis.db` with an explicit seven-day deadline and can be viewed with the **History** button. Startup cleanup deletes expired text immediately and retries matching Codex thread deletion through a text-free tombstone if Codex is temporarily unavailable. Long-term memories use a separate non-expiring table. OAuth data remains owned by the official Codex process under `%LOCALAPPDATA%\Jarvis\Codex`; audio, screenshots, OAuth material, and secrets have no database columns and are not copied into Jarvis diagnostics.

See [SPEC.md](SPEC.md) for the approved MVP specification.
