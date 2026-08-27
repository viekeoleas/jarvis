namespace Jarvis.Codex;

public sealed record CodexProcessOptions(
    string ExecutablePath,
    string CodexHome,
    string ExpectedVersion)
{
    public static CodexProcessOptions CreateDefault()
    {
        var localData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return new CodexProcessOptions(
            CodexExecutableLocator.Resolve(),
            Path.Combine(localData, "Jarvis", "Codex"),
            CodexCompatibility.ExpectedCliVersion);
    }
}
