# Jarvis MVP: Local-first Windows Voice Assistant

## Problem Statement

The user wants a deeply customizable personal assistant inspired by the cinematic Jarvis experience. It must start automatically after sign-in to Windows, wake on a spoken phrase, understand Russian, Ukrainian, and English, reply with speech, and perform useful actions on the local computer.

The assistant must use the user's existing ChatGPT Plus subscription through an officially supported sign-in flow. It must not require OpenAI API billing, extract browser cookies, copy raw subscription tokens, reverse engineer private endpoints, or depend on a third-party OAuth proxy.

Existing desktop assistants do not provide the required combination of a customizable wake word, local multilingual speech processing, local memory, a compact native Windows interface, explicit action policy, and official ChatGPT subscription authentication. The project therefore needs a custom Windows application built around the official Codex app-server.

## Solution

Build a local-first Windows tray application named Jarvis. After the user signs in to Windows, Jarvis starts in the background and listens locally for the configurable wake phrase "Jarvis". It opens a conversational session for up to 60 seconds of silence, transcribes speech locally, routes deterministic commands to local handlers, and sends open-ended requests to Codex through the official app-server and ChatGPT managed OAuth.

Jarvis speaks responses using local text-to-speech, displays a compact panel with the transcript and current action, and can be interrupted by voice, hotkey, or a visible Stop button. It performs safe actions autonomously, asks for clarification when uncertain, and binds destructive or elevated actions to a specific randomized spoken confirmation challenge.

The application stores text history locally for seven days, never persists audio or screenshots, and maintains a separate user-reviewable long-term memory. It supports only one local Windows computer in the MVP.

## User Stories

1. As the sole user, I want Jarvis to start after I sign in to Windows, so that it is available without manual setup each session.
2. As the sole user, I want Jarvis to start minimized to the system tray, so that it does not interrupt my desktop workflow.
3. As the sole user, I want Jarvis to listen for a wake phrase immediately after startup, so that I can use it hands-free.
4. As the sole user, I want the default wake phrase to be "Jarvis", so that the first-run experience matches the intended assistant identity.
5. As the sole user, I want to retrain or replace the wake phrase, so that I can customize activation later.
6. As the sole user, I want a global Win+Shift+J shortcut, so that I can activate Jarvis when wake-word detection fails or would be inappropriate.
7. As the sole user, I want a visible microphone state indicator, so that I always know when Jarvis is listening.
8. As the sole user, I want an audible ready cue after activation, so that I can speak without watching the screen.
9. As the sole user, I want an active conversation to remain open until 60 seconds of silence, so that I do not need to repeat the wake phrase for follow-up turns.
10. As the sole user, I want to stop Jarvis by saying "Jarvis, stop", so that I can interrupt it hands-free.
11. As the sole user, I want the activation hotkey to stop an active operation, so that I always have a keyboard fallback.
12. As the sole user, I want a prominent Stop button in the panel, so that I can immediately halt speech and pending work.
13. As the sole user, I want Jarvis to understand Russian speech, so that I can use my primary conversational language.
14. As the sole user, I want Jarvis to understand Ukrainian speech, so that I can use Ukrainian naturally.
15. As the sole user, I want Jarvis to understand English speech, so that application names and technical vocabulary work correctly.
16. As the sole user, I want Jarvis to understand mixed-language commands, so that phrases containing English program names do not fail.
17. As the sole user, I want Jarvis to ask for clarification when recognition or intent is uncertain, so that it does not guess incorrectly.
18. As the sole user, I want Jarvis to reply aloud, so that the interaction is useful without looking at the display.
19. As the sole user, I want to change the voice, speed, and volume later, so that the assistant can evolve toward my preferred character.
20. As the sole user, I want a concise cinematic personality with restrained initiative and occasional dry humor, so that Jarvis feels distinctive without becoming distracting.
21. As the sole user, I want to configure brevity, initiative, and humor, so that the personality is not hard-coded.
22. As the sole user, I want Jarvis to address me as "sir" only occasionally, so that the cinematic style does not become repetitive.
23. As a ChatGPT Plus subscriber, I want to sign in through the official ChatGPT OAuth flow, so that I can use my subscription without an API key.
24. As a ChatGPT Plus subscriber, I want Jarvis to respect subscription limits, so that it never silently creates API charges.
25. As the sole user, I want a clear error when Codex or the internet is unavailable, so that failures are understandable.
26. As the sole user, I want common deterministic commands to run locally, so that they respond quickly and do not consume subscription quota.
27. As the sole user, I want ambiguous and open-ended requests to use Codex, so that Jarvis can reason about complex tasks.
28. As the sole user, I want Jarvis to launch, switch, and close applications, so that I can control my desktop by voice.
29. As the sole user, I want Jarvis to search the web and open pages in my default browser, so that I can retrieve information hands-free.
30. As the sole user, I want Jarvis to control system volume and media playback, so that frequent media actions are immediate.
31. As the sole user, I want Jarvis to create timers, notes, and reminders, so that it can handle everyday assistance.
32. As the sole user, I want Jarvis to find and open files, so that I can access local information by description.
33. As the sole user, I want Jarvis to copy, move, and rename files, so that I can organize files conversationally.
34. As the sole user, I want Jarvis to request confirmation before deleting files, so that recognition errors do not destroy data.
35. As the sole user, I want Jarvis to run allowed PowerShell commands, so that it can automate Windows tasks beyond fixed commands.
36. As the sole user, I want elevated actions to trigger UAC only for that action, so that the main assistant never runs continuously as administrator.
37. As the sole user, I want destructive and elevated actions to require a randomized spoken challenge, so that a generic or misheard "yes" cannot approve the wrong operation.
38. As the sole user, I want payments and password handling excluded, so that the MVP does not cross unacceptable security boundaries.
39. As the sole user, I want Jarvis to prefer Windows APIs and UI Automation over coordinate clicks, so that actions survive window movement and display scaling.
40. As the sole user, I want Jarvis to request a screen capture only when needed, so that it can understand visual context without continuous screen recording.
41. As the sole user, I want a visible indicator whenever screen context is captured, so that visual access is never hidden.
42. As the sole user, I want screenshots to remain only in memory, so that sensitive screen content is not retained.
43. As the sole user, I want Desktop, Documents, Downloads, and explicitly selected folders to be accessible by default, so that useful file work is convenient.
44. As the sole user, I want to add and remove permitted folders in settings, so that file access remains customizable.
45. As the sole user, I want Windows system directories denied by default, so that ordinary voice commands cannot modify the operating system accidentally.
46. As the sole user, I want the panel to show the recognized text, current state, proposed action, and errors, so that I can understand what Jarvis is doing.
47. As the sole user, I want the initial panel to be visually simple and dark, so that engineering effort prioritizes reliability before visual polish.
48. As the sole user, I want text conversations stored locally for seven days, so that recent context is available without creating a permanent archive.
49. As the sole user, I want audio never written to disk, so that raw microphone recordings are not retained.
50. As the sole user, I want long-term memories stored separately from conversation history, so that useful preferences survive automatic history deletion.
51. As the sole user, I want Jarvis to propose useful memories automatically, so that personalization does not require manually recording every preference.
52. As the sole user, I want sensitive memories to require confirmation, so that private facts are not silently retained.
53. As the sole user, I want to inspect, edit, and delete every long-term memory, so that I retain control over personalization.
54. As the sole user, I want recent conversation records and corresponding Codex threads deleted after seven days, so that retention is consistent across local components.
55. As the sole user, I want settings for the wake phrase, hotkey, voice, personality, permissions, confirmations, retention, and autostart, so that Jarvis is deeply customizable.
56. As the sole user, I want Jarvis to remain scoped to this computer, so that the MVP does not expose a network service or create remote-access risk.
57. As the sole user, I want a straightforward local installation, so that Jarvis behaves like a normal Windows application.
58. As the sole user, I want the application tested directly on my Windows 10 computer, so that best-effort compatibility is demonstrated despite the platform being out of support.
59. As the sole user, I want diagnostics that exclude conversation text and audio, so that troubleshooting does not create a second private-data archive.
60. As the sole user, I want all ongoing work to be cancellable, so that a mistaken or obsolete request cannot continue uncontrolled.

## Implementation Decisions

- Build a Windows-only native desktop application using C# and WPF. Prefer a supported LTS .NET runtime, with an initial compatibility spike on the target Windows 10 Pro 22H2 machine before committing the full application to that target.
- Use a tray-first process with a compact borderless panel. The process runs as the normal user and starts per-user after Windows sign-in.
- Use the official Codex app-server as a child process over its stable standard-input/standard-output transport. Do not use the experimental WebSocket transport in the MVP.
- Let Codex app-server own ChatGPT managed OAuth, refresh, account state, and Plus plan detection. Jarvis must not read, expose, copy, or refresh raw tokens itself.
- Isolate Jarvis's Codex configuration and sessions from any ordinary Codex CLI installation by giving its child process a dedicated application data home.
- Pin and test a specific Codex version and generate protocol schemas from that version. Treat protocol upgrades as explicit dependency upgrades.
- Use a single long-running application process for tray, panel, audio orchestration, local routing, policy, memory, and the Codex client.
- Use a separate one-shot elevated helper only for a specifically approved administrative action. The main microphone process must never remain elevated.
- Capture microphone audio through the native Windows shared audio path as 16 kHz mono PCM into a bounded in-memory ring buffer.
- Use Rustpotter on CPU for the configurable wake phrase. Enrol the initial phrase from several recordings made with the user's actual microphone.
- Preserve the global hotkey as a required fallback because arbitrary-language open-source wake-word detection is not perfectly reliable.
- Use Silero VAD on CPU to determine speech boundaries. Treat 60 seconds as the conversation-session timeout, not the end-of-utterance timeout.
- Use whisper.cpp with a multilingual large-v3-turbo family model and Vulkan acceleration for free-form RU/UK/EN speech recognition. Keep a smaller CPU-capable multilingual model as a fallback.
- Keep the detected language sticky within a session and constrain expected languages to RU/UK/EN to reduce errors between short Russian and Ukrainian utterances.
- Use sherpa-onnx with compatible Piper VITS voices for local RU/UK/EN text-to-speech. Keep the TTS engine warm to reduce first-audio latency.
- Do not use OpenAI Realtime audio or another paid speech API. Speech input and output must remain local.
- Implement a small deterministic Local Intent Router before Codex. Its initial intents cover activation, stop, panel visibility, volume, media, application launch, and timers.
- Route all uncertain, compositional, or conversational requests to Codex rather than expanding the local router into a second natural-language model.
- Pass every real side effect, whether proposed locally or by Codex, through one Action Policy and Confirmation Coordinator.
- Categorize actions as safe, external/reversible, destructive, or elevated. Safe actions may run immediately; destructive and elevated actions require a challenge bound to the pending action.
- Accept voice confirmation for destructive actions through a short randomized phrase. Expire pending confirmations and reject confirmations that do not match the currently displayed action.
- Prefer native Windows APIs, application protocols, PowerShell, and Microsoft UI Automation. Use screenshot-guided pointer input only when structured automation is unavailable.
- Perform UI Automation on a dedicated worker with timeouts and cancellation.
- Capture a selected window or display only after Jarvis requests visual context. Keep captured pixels in memory and expose an unambiguous capture indicator.
- Store settings, conversations, messages, actions, and long-term memories in SQLite using explicit migrations and ordinary SQL.
- Give seven-day expiry timestamps to conversation text and ordinary action history. Run cleanup automatically and delete the corresponding Codex thread data as part of the same retention operation.
- Keep long-term memories separate from expiring transcripts. Memories include provenance and can be reviewed, edited, or deleted from settings.
- Begin with structured memories and SQLite full-text search. Do not introduce embeddings or a vector database until document retrieval or data volume demonstrates a need for RAG.
- Do not persist raw audio, PCM buffers, synthesized audio, or screenshots.
- Protect local secrets and sensitive settings using Windows user-scoped data protection. Do not require an additional application password in the personal MVP.
- Keep diagnostic logs bounded and exclude conversation text, raw prompts, audio, screenshots, OAuth material, and secrets.
- Permit Desktop, Documents, Downloads, and user-selected directories. Deny Windows system locations by default and require an explicit permission change for other roots.
- Make assistant name, wake phrase, hotkey, speech settings, personality, address, permissions, confirmation policy, retention, and autostart user-configurable.
- Use a cinematic but concise default persona, with restrained initiative and occasional dry humor. Default to the address "sir" and avoid repeating it in every response.
- Package as a per-user Windows installation with no permanent administrative requirement. Defer store packaging, auto-update infrastructure, and public distribution concerns until after the personal MVP.
- Treat Windows 10 Pro 22H2 as a best-effort target. The first engineering spike must prove application startup, tray behavior, audio capture, Vulkan STT, Codex app-server login, and TTS on the actual machine before broader implementation.
- Keep the application local-only in the MVP. Do not open a listening network port for phone control, remote access, or synchronization.

## Testing Decisions

- Prefer one high application-level seam over many implementation-level seams. The primary acceptance harness supplies recorded audio or deterministic transcripts, a fake Codex app-server, and fake Windows capabilities, then observes user-visible transcript events, spoken responses, confirmations, cancellations, and requested side effects.
- Test externally visible behavior and policy outcomes rather than private class structure, framework calls, or exact prompt wording.
- Run the same scenario suite with RU, UK, EN, and mixed-language fixtures.
- Verify that deterministic local intents do not invoke Codex, while ambiguous requests do.
- Verify that safe actions execute once, destructive actions remain pending, incorrect or expired challenge phrases fail, and the correct phrase approves only its bound action.
- Verify cancellation from voice, hotkey, and panel at listening, transcription, reasoning, speaking, and action stages.
- Verify that screen capture occurs only after a request, produces a visible state, remains in memory, and is released after use.
- Verify allowed-directory and system-directory policy using temporary test roots rather than real user files.
- Verify that expired conversations, messages, diagnostics, and corresponding Codex threads are removed after seven days while long-term memories remain.
- Verify that audio, screenshots, OAuth tokens, and conversation text never appear in diagnostic logs.
- Verify child-process lifecycle behavior: initialization, normal exit, crash, bounded restart, cancellation, and cleanup when Jarvis exits.
- Verify that an elevated request cannot elevate the main Jarvis process and that the helper can execute only the approved action payload.
- Add contract tests against the generated schema for the pinned Codex app-server version.
- Add live smoke tests on the target PC for microphone capture, wake-word enrollment, false activations, Vulkan model loading, multilingual STT latency, TTS playback, tray startup, global hotkey, screen capture, UI Automation, UAC, and managed ChatGPT login.
- Use the confirmed MVP acceptance journey as the release gate: start after Windows sign-in, activate with "Jarvis", understand a mixed-language utterance, answer through Plus without an API key, launch an application, search the web, complete a file operation, require deletion confirmation, and stop immediately through voice or UI.
- Target practical voice performance on the actual machine rather than assuming published benchmarks: tune toward a 0.6-0.9 second utterance endpoint, responsive warmed TTS, and acceptably low wake-word false activations and misses.
- There is no existing test prior art in the repository because the repository begins empty. The acceptance harness established by the first vertical slice becomes the project standard for later capabilities.

## Out of Scope

- OpenAI API keys, metered API billing, Realtime API audio, or any other paid per-request provider.
- Reverse-engineered ChatGPT endpoints, browser cookie extraction, raw token reuse, or third-party subscription proxies.
- Payments, purchasing, banking, password entry, credential management, or secret submission.
- Email, messaging, social posting, calendar connectors, and external account automation in the MVP.
- Smart-home control.
- Phone clients, remote control, multi-PC synchronization, or any inbound network service.
- Always-on screen capture, continuous screen recording, video retention, audio retention, or screenshot retention.
- Voice biometrics as an authentication factor.
- A large local language model or an offline replacement for Codex.
- A vector database or generalized RAG platform before a concrete document-retrieval requirement exists.
- Pixel-perfect cinematic HUD visuals, voice cloning, multiple polished themes, and advanced animation in the MVP.
- Unrestricted shell execution, unrestricted filesystem access, or a permanently elevated assistant process.
- Public marketplace distribution, commercial multi-user support, automatic updates, and enterprise administration.
- Guaranteed support for Windows 10 after its vendor end-of-support date.

## Further Notes

- The target machine is Windows 10 Pro 22H2 x64 with an Intel Core i5-11400F, 16 GB RAM, and an AMD Radeon RX 6800 XT with 16 GB VRAM.
- The system drive currently has approximately 26 GB free. The voice stack fits, but free space should be monitored before adding more models or document indexes.
- Current AMD HIP/ROCm support is not a viable assumption for this Windows 10 and RX 6800 XT combination. The STT spike therefore uses whisper.cpp's Vulkan backend and retains a CPU fallback.
- ChatGPT Plus includes Codex subject to plan limits. The supported integration point is Codex app-server with ChatGPT managed OAuth.
- Windows 10 Pro 22H2 reached end of support on 14 October 2025. Compatibility must be demonstrated on the target PC and remains best effort.
- Relevant primary references:
  - [Codex app-server protocol and authentication](https://github.com/openai/codex/blob/main/codex-rs/app-server/README.md)
  - [Codex availability in ChatGPT plans](https://openai.com/index/introducing-upgrades-to-codex/)
  - [WPF overview](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/overview/)
  - [Microsoft UI Automation overview](https://learn.microsoft.com/en-us/windows/win32/winauto/uiauto-uiautomationoverview)
  - [Rustpotter wake-word engine](https://github.com/GiviMAD/rustpotter)
  - [Silero VAD](https://github.com/snakers4/silero-vad)
  - [whisper.cpp](https://github.com/ggml-org/whisper.cpp)
  - [sherpa-onnx](https://github.com/k2-fsa/sherpa-onnx)
  - [Windows 10 lifecycle](https://learn.microsoft.com/en-us/lifecycle/announcements/windows-10-end-of-support)

