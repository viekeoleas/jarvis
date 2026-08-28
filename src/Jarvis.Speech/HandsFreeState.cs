namespace Jarvis.Speech;

public sealed record HandsFreeState(HandsFreePhase Phase, string Status, string? Error = null)
{
    public static HandsFreeState Disabled { get; } = new(
        HandsFreePhase.Disabled,
        "Wake phrase disabled; hotkey available");
}
