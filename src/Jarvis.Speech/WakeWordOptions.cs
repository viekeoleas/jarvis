namespace Jarvis.Speech;

public sealed record WakeWordOptions(
    string ExecutablePath,
    string ModelPath,
    int DeviceIndex = 0,
    int SampleRate = SpeechAudioFormat.SampleRate,
    double Threshold = 0.52,
    int MinimumScores = 10)
{
    public static WakeWordOptions CreateDefault()
    {
        var localData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var threshold = ReadDouble("JARVIS_WAKE_THRESHOLD", 0.52, 0.1, 0.99);
        var minimumScores = ReadInt("JARVIS_WAKE_MIN_SCORES", 10, 1, 100);
        return new WakeWordOptions(
            Path.Combine(
                localData,
                "Jarvis",
                "Tools",
                "rustpotter-cli",
                "v3.0.2",
                "rustpotter-cli.exe"),
            Path.Combine(
                localData,
                "Jarvis",
                "Models",
                "rustpotter",
                "v3.0.2",
                "jarvis.rpw"),
            Threshold: threshold,
            MinimumScores: minimumScores);
    }

    private static double ReadDouble(
        string name,
        double fallback,
        double minimum,
        double maximum) =>
        double.TryParse(
            Environment.GetEnvironmentVariable(name),
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture,
            out var value) && value >= minimum && value <= maximum
            ? value
            : fallback;

    private static int ReadInt(string name, int fallback, int minimum, int maximum) =>
        int.TryParse(Environment.GetEnvironmentVariable(name), out var value) &&
        value >= minimum && value <= maximum
            ? value
            : fallback;
}
