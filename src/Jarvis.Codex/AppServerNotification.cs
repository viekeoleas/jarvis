using System.Text.Json;

namespace Jarvis.Codex;

public sealed record AppServerNotification(string Method, JsonElement Params);
