using Jarvis.Core;

namespace Jarvis.Speech;

public sealed class VoicePersonaResponder(IAssistantResponder inner) : IAssistantResponder
{
    public Task<string> RespondAsync(string request, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request);
        var speechLanguage = LocalSpeechLanguageDetector.Detect(request);
        if (IsUncertain(request))
        {
            return Task.FromResult(speechLanguage switch
            {
                SpeechLanguage.Ukrainian => "Не розчув. Уточніть, будь ласка, що потрібно зробити?",
                SpeechLanguage.Russian => "Не расслышал. Уточните, пожалуйста, что нужно сделать?",
                _ => "I didn't catch that. What would you like me to do?"
            });
        }

        var language = speechLanguage switch
        {
            SpeechLanguage.Ukrainian => "Ukrainian",
            SpeechLanguage.Russian => "Russian",
            _ => "English"
        };
        var prompt = $$"""
            You are Jarvis, a concise cinematic personal assistant. Use restrained initiative
            and only occasional dry humor. Respond in {{language}}, matching the user's latest
            request. If the transcript or intended action is unclear, ask one brief clarifying
            question; do not guess, invent an action, or claim an action was performed.

            Voice transcript:
            {{request.Trim()}}
            """;
        return inner.RespondAsync(prompt, cancellationToken);
    }

    private static bool IsUncertain(string request) =>
        request.Count(char.IsLetterOrDigit) < 2 ||
        request.Contains("[BLANK_AUDIO]", StringComparison.OrdinalIgnoreCase) ||
        request.Contains("[inaudible]", StringComparison.OrdinalIgnoreCase) ||
        request.Contains("неразборчив", StringComparison.OrdinalIgnoreCase) ||
        request.Contains("нерозбірлив", StringComparison.OrdinalIgnoreCase);
}
