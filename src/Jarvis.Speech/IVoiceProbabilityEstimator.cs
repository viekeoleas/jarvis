namespace Jarvis.Speech;

public interface IVoiceProbabilityEstimator : IDisposable
{
    float Estimate(ReadOnlySpan<short> samples);

    void Reset();
}
