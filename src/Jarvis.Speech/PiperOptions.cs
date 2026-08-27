namespace Jarvis.Speech;

public sealed record PiperOptions(
    string PythonExecutable,
    IReadOnlyDictionary<SpeechLanguage, PiperVoiceDefinition> Voices,
    TimeSpan MaximumSpokenDuration)
{
    public static PiperOptions CreateDefault()
    {
        var localData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var toolRoot = Path.Combine(localData, "Jarvis", "Tools", "piper", "v1.7.0");
        var voiceRoot = Path.Combine(localData, "Jarvis", "Models", "piper", "v1.7.0");
        var voices = new Dictionary<SpeechLanguage, PiperVoiceDefinition>
        {
            [SpeechLanguage.Russian] = CreateVoice(
                SpeechLanguage.Russian, "ru_RU-dmitri-medium", voiceRoot),
            [SpeechLanguage.Ukrainian] = CreateVoice(
                SpeechLanguage.Ukrainian, "uk_UA-ukrainian_tts-medium", voiceRoot),
            [SpeechLanguage.English] = CreateVoice(
                SpeechLanguage.English, "en_US-ryan-medium", voiceRoot)
        };

        return new PiperOptions(
            Path.Combine(toolRoot, ".venv", "Scripts", "python.exe"),
            voices,
            TimeSpan.FromSeconds(90));
    }

    private static PiperVoiceDefinition CreateVoice(
        SpeechLanguage language,
        string name,
        string root) =>
        new(language, name, Path.Combine(root, name + ".onnx"), SampleRate: 22_050);
}
