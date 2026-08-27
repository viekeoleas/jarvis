namespace Jarvis.Speech;

public sealed record WhisperProcessOptions(
    string ExecutablePath,
    string ModelPath,
    bool PreferVulkan = true)
{
    public static WhisperProcessOptions CreateDefault()
    {
        var localData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return new WhisperProcessOptions(
            Path.Combine(
                localData,
                "Jarvis",
                "Tools",
                "whisper.cpp",
                SpeechModelCatalog.WhisperVersion,
                "vulkan",
                "whisper-cli.exe"),
            SpeechModelCatalog.GetWhisperModelPath());
    }
}
