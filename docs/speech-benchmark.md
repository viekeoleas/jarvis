# Local speech benchmark

Target machine: Windows 10 build 19045, Intel Core i5-11400F, AMD Radeon RX 6800 XT, 16 GB RAM. Measurements were taken from a Release build on 2026-08-27.

## Backends and assets

- Silero VAD 6.2.1, CPU, 512-sample frames at 16 kHz.
- whisper.cpp `b4938`, `large-v3-turbo-q5_0`, Vulkan primary backend.
- Piper 1.7.0 with `ru_RU-dmitri-medium`, `uk_UA-ukrainian_tts-medium`, and `en_US-ryan-medium`.
- All intermediate PCM and WAV data remained in bounded process memory.

## Multilingual probe

The repeatable probe uses synthetic, non-personal speech so acceptance audio does not have to be committed or retained. It covers Russian, Ukrainian, English, and a Russian/English mixed command.

| Fixture | Voice | Transcript | Vulkan STT | First audio |
| --- | --- | --- | ---: | ---: |
| RU | Russian | `Кропокнот.` | 2.13 s | 2.97 s |
| UK | Ukrainian | `Ідкрай блокнот.` | 2.03 s | 3.11 s |
| EN | English | `Open notepad.` | 2.01 s | 3.00 s |
| Mixed | Russian | `Джарвис от CoinOutpad.` | 2.07 s | 2.94 s |

The short synthetic RU/UK/mixed clips demonstrate language-path execution but also expose clipping and code-switch recognition quality that should be improved before action routing relies on exact words. Real microphone input includes natural leading context and is evaluated separately through the same in-memory path.

`First audio` is measured from Piper process launch until the complete bounded sample is available and playback can begin. The current one-shot Python runtime is reliable but cold-start heavy; keeping the engine warm is a later latency optimization.

## CPU fallback

The official whisper.cpp JFK fixture was transcribed with both paths. Vulkan completed in 5.60 seconds including model load. Forced `--no-gpu` CPU fallback completed in 31.60 seconds and returned the same transcript. The fallback is therefore functional on the target PC, although intentionally slower.

Run the probe with:

```powershell
dotnet run --project tools/Jarvis.SpeechProbe/Jarvis.SpeechProbe.csproj -c Release
```

Run a local audible playback check with:

```powershell
dotnet run --project tools/Jarvis.SpeechProbe/Jarvis.SpeechProbe.csproj -c Release -- --play
```

## Live Plus-to-voice smoke test

With the isolated Jarvis ChatGPT account already signed in, the live probe returned `Я — Джарвис, ваш лаконичный цифровой помощник.` and spoke it through the Russian local voice. Piper first audio was available in 3.33 seconds. The request used managed ChatGPT authentication; no OpenAI API key or metered speech service was involved.
