namespace Jarvis.Core;

public sealed record AssistantViewState(
    AssistantPhase Phase,
    string Status,
    string? Request = null,
    string? Response = null,
    string? Error = null)
{
    public static AssistantViewState Initial { get; } =
        new(AssistantPhase.Idle, "Ready");
}
