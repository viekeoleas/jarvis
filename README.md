# Jarvis

Jarvis is a local-first Windows voice assistant designed to use an existing ChatGPT Plus subscription through the official Codex app-server.

The repository currently contains the Windows 10 compatibility tracer from [issue #2](https://github.com/viekeoleas/jarvis/issues/2): a native WPF tray application, compact panel, global activation hotkey, and an application-level acceptance seam. The assistant response is intentionally local and deterministic until the official subscription integration lands in the next slice.

## Run the compatibility tracer

Requirements:

- Windows x64
- .NET 10 SDK

Commands:

```powershell
dotnet restore Jarvis.slnx
dotnet test Jarvis.slnx
dotnet run --project src/Jarvis.App/Jarvis.App.csproj
```

Jarvis starts in the system tray. Use `Win+Shift+J` or the tray menu to show or hide the panel. The Exit tray command shuts down the process and unregisters the hotkey.

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

This tracer opens no inbound network listener, uses no API key, captures no microphone audio, and stores no conversation data. Later slices must preserve the specification's local-audio and diagnostic-data boundaries.

See [SPEC.md](SPEC.md) for the approved MVP specification.
