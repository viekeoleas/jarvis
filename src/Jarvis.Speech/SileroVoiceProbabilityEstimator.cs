using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace Jarvis.Speech;

public sealed class SileroVoiceProbabilityEstimator : IVoiceProbabilityEstimator
{
    private const int ContextSamples = 64;
    private const int StateSize = 2 * 128;

    private readonly InferenceSession _session;
    private readonly float[] _context = new float[ContextSamples];
    private readonly float[] _state = new float[StateSize];
    private bool _disposed;

    public SileroVoiceProbabilityEstimator(string modelPath)
    {
        if (!File.Exists(modelPath))
        {
            throw new FileNotFoundException("The Silero VAD model was not found.", modelPath);
        }

        var options = new SessionOptions
        {
            InterOpNumThreads = 1,
            IntraOpNumThreads = 1
        };
        _session = new InferenceSession(modelPath, options);
    }

    public float Estimate(ReadOnlySpan<short> samples)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (samples.Length != SpeechAudioFormat.VadFrameSamples)
        {
            throw new ArgumentException(
                $"Silero VAD requires {SpeechAudioFormat.VadFrameSamples} samples per frame.",
                nameof(samples));
        }

        var input = new float[ContextSamples + SpeechAudioFormat.VadFrameSamples];
        _context.CopyTo(input, 0);
        for (var index = 0; index < samples.Length; index++)
        {
            input[ContextSamples + index] = samples[index] / 32768f;
        }

        var inputTensor = new DenseTensor<float>(input, [1, input.Length]);
        var stateTensor = new DenseTensor<float>(_state, [2, 1, 128]);
        var sampleRateTensor = new DenseTensor<long>(
            new Memory<long>([(long)SpeechAudioFormat.SampleRate]),
            ReadOnlySpan<int>.Empty);
        using var results = _session.Run(
        [
            NamedOnnxValue.CreateFromTensor("input", inputTensor),
            NamedOnnxValue.CreateFromTensor("state", stateTensor),
            NamedOnnxValue.CreateFromTensor("sr", sampleRateTensor)
        ]);

        input.AsSpan(input.Length - ContextSamples).CopyTo(_context);
        results.ElementAt(1).AsEnumerable<float>().ToArray().CopyTo(_state, 0);
        return results.First().AsTensor<float>().GetValue(0);
    }

    public float Estimate(short[] samples) => Estimate(samples.AsSpan());

    public void Reset()
    {
        Array.Clear(_context);
        Array.Clear(_state);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _session.Dispose();
    }
}
