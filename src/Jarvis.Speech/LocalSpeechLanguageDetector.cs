namespace Jarvis.Speech;

public static class LocalSpeechLanguageDetector
{
    private const string UkrainianLetters = "іїєґІЇЄҐ";

    public static SpeechLanguage Detect(string text)
    {
        if (text.Any(UkrainianLetters.Contains))
        {
            return SpeechLanguage.Ukrainian;
        }

        if (text.Any(character => character is >= '\u0400' and <= '\u04FF'))
        {
            return SpeechLanguage.Russian;
        }

        return SpeechLanguage.English;
    }
}
