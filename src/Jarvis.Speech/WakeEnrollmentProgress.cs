namespace Jarvis.Speech;

public sealed record WakeEnrollmentProgress(int AcceptedSamples, int RequiredSamples, string Status);
