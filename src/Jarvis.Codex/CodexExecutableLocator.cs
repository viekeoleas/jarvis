using System.Diagnostics;

namespace Jarvis.Codex;

public static class CodexExecutableLocator
{
    public static string Resolve()
    {
        var configured = Environment.GetEnvironmentVariable("JARVIS_CODEX_PATH");
        if (IsExecutable(configured))
        {
            return Path.GetFullPath(configured!);
        }

        var fromPath = FindOnPath();
        if (fromPath is not null)
        {
            return fromPath;
        }

        var fromExtension = FindInVsCodeExtensions();
        if (fromExtension is not null)
        {
            return fromExtension;
        }

        throw new FileNotFoundException(
            "The official Codex executable was not found. Set JARVIS_CODEX_PATH or install Codex.");
    }

    private static string? FindOnPath()
    {
        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var candidate = Path.Combine(directory.Trim(), "codex.exe");
                if (File.Exists(candidate))
                {
                    return Path.GetFullPath(candidate);
                }
            }
            catch (Exception exception) when (
                exception is ArgumentException or NotSupportedException or PathTooLongException)
            {
                // Ignore malformed PATH entries and continue to the supported fallback.
            }
        }

        return null;
    }

    private static string? FindInVsCodeExtensions()
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var extensionRoot = Path.Combine(profile, ".vscode", "extensions");
        if (!Directory.Exists(extensionRoot))
        {
            return null;
        }

        return Directory
            .EnumerateDirectories(extensionRoot, "openai.chatgpt-*-win32-x64")
            .OrderByDescending(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
            .Select(directory => Path.Combine(directory, "bin", "windows-x86_64", "codex.exe"))
            .FirstOrDefault(File.Exists);
    }

    private static bool IsExecutable(string? path) =>
        !string.IsNullOrWhiteSpace(path) && File.Exists(path);
}
