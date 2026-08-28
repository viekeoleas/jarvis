namespace Jarvis.Speech;

public sealed record WakeCalibrationResult(
    DateTimeOffset CreatedUtc,
    double Threshold,
    int MinimumScores,
    int PositiveSamples,
    int PositiveDetections,
    int BackgroundDetections);
