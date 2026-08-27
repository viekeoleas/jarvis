namespace Jarvis.Speech;

public static class SpeechModelCatalog
{
    public const string SileroVersion = "v6.2.1";
    public const string SileroFileName = "silero_vad_16k_op15.onnx";
    public const string SileroSha256 =
        "7ED98DDBAD84CCAC4CD0AEB3099049280713DF825C610A8ED34543318F1B2C49";
    public const string WhisperVersion = "b4938";
    public const string WhisperModelName = "large-v3-turbo-q5_0";
    public const string WhisperFileName = "ggml-large-v3-turbo-q5_0.bin";
    public const string WhisperSha256 =
        "394221709CD5AD1F40C46E6031CA61BCE88931E6E088C188294C6D5A55FFA7E2";
    public static Uri SileroDownloadUri { get; } = new(
        "https://raw.githubusercontent.com/snakers4/silero-vad/v6.2.1/" +
        "src/silero_vad/data/silero_vad_16k_op15.onnx");
    public static Uri WhisperDownloadUri { get; } = new(
        "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/" + WhisperFileName);

    public static string GetSileroModelPath()
    {
        var localData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(
            localData,
            "Jarvis",
            "Models",
            "silero-vad",
            SileroVersion,
            SileroFileName);
    }

    public static string GetWhisperModelPath()
    {
        var localData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(
            localData,
            "Jarvis",
            "Models",
            "whisper",
            WhisperVersion,
            WhisperFileName);
    }
}
