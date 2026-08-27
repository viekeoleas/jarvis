namespace Jarvis.Codex;

public enum CodexAccountPhase
{
    Starting,
    SignedOut,
    SigningIn,
    Restarting,
    SignedIn,
    Failed
}

public sealed record CodexAccountState(
    CodexAccountPhase Phase,
    string Message,
    string? AuthMode = null,
    string? PlanType = null,
    string? Email = null)
{
    public static CodexAccountState Starting { get; } =
        new(CodexAccountPhase.Starting, "Connecting to Codex");
}
