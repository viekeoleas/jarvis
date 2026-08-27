namespace Jarvis.Speech;

public sealed class UtteranceBoundaryDetector(
    float speechThreshold = 0.5f,
    float silenceThreshold = 0.35f,
    int endSilenceMilliseconds = 700)
{
    private readonly int _endSilenceSamples = checked(
        SpeechAudioFormat.SampleRate * endSilenceMilliseconds / 1000);
    private bool _hasSpeech;
    private int _silenceSamples;

    public bool HasSpeech => _hasSpeech;

    public VoiceBoundaryEvent Accept(float speechProbability, int frameSamples)
    {
        if (speechProbability is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(speechProbability));
        }

        if (frameSamples <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(frameSamples));
        }

        if (speechProbability >= speechThreshold)
        {
            _silenceSamples = 0;
            if (!_hasSpeech)
            {
                _hasSpeech = true;
                return VoiceBoundaryEvent.SpeechStarted;
            }

            return VoiceBoundaryEvent.None;
        }

        if (!_hasSpeech || speechProbability >= silenceThreshold)
        {
            _silenceSamples = 0;
            return VoiceBoundaryEvent.None;
        }

        _silenceSamples += frameSamples;
        if (_silenceSamples < _endSilenceSamples)
        {
            return VoiceBoundaryEvent.None;
        }

        _hasSpeech = false;
        _silenceSamples = 0;
        return VoiceBoundaryEvent.UtteranceEnded;
    }

    public void Reset()
    {
        _hasSpeech = false;
        _silenceSamples = 0;
    }
}
