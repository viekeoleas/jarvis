namespace Jarvis.Codex;

public static class CodexCompatibility
{
    public const string SupportedCliVersionFamily = "0.150.0-alpha.";

    public static bool IsSupportedCliVersion(string versionOutput)
    {
        var markerIndex = versionOutput.IndexOf(
            SupportedCliVersionFamily,
            StringComparison.Ordinal);
        if (markerIndex < 0)
        {
            return false;
        }

        var suffix = versionOutput[(markerIndex + SupportedCliVersionFamily.Length)..]
            .TakeWhile(character => char.IsAsciiDigit(character) || character == '.')
            .ToArray();
        return suffix.Length > 0 &&
               char.IsAsciiDigit(suffix[0]) &&
               char.IsAsciiDigit(suffix[^1]);
    }
}
