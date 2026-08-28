using System.Diagnostics;
using System.Text.Json;
using NAudio.Wave;

namespace Jarvis.Speech;

public sealed class RustpotterWakeWordEnroller(WakeWordOptions options) : IWakeWordEnroller
{
    private const int RequiredSamples = 5;
    private const int MaximumAttempts = 10;
    private const int SampleDurationMilliseconds = 1_900;
    private const int BackgroundDurationMilliseconds = 4_000;

    public event EventHandler<WakeEnrollmentProgress>? ProgressChanged;

    public async Task<string> EnrollAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(options.ExecutablePath))
        {
            throw new FileNotFoundException(
                "The local Rustpotter runtime was not found.",
                options.ExecutablePath);
        }

        var temporaryRoot = Path.Combine(
            Path.GetTempPath(),
            "JarvisWakeEnrollment",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporaryRoot);
        var accepted = new List<string>(RequiredSamples);
        var temporaryModel = options.ModelPath + ".download";
        try
        {
            for (var attempt = 1;
                 accepted.Count < RequiredSamples && attempt <= MaximumAttempts;
                 attempt++)
            {
                ProgressChanged?.Invoke(this, new WakeEnrollmentProgress(
                    accepted.Count,
                    RequiredSamples,
                    $"Say Jarvis now — sample {accepted.Count + 1} of {RequiredSamples}"));
                await Task.Delay(500, cancellationToken).ConfigureAwait(false);
                var samplePath = Path.Combine(temporaryRoot, $"sample-{attempt}.wav");
                await RunAsync(
                    [
                        "record",
                        "--device-index", options.DeviceIndex.ToString(),
                        "--sample-rate", options.SampleRate.ToString(),
                        "--ms", SampleDurationMilliseconds.ToString(),
                        samplePath
                    ],
                    cancellationToken).ConfigureAwait(false);
                if (IsSuitableSample(samplePath))
                {
                    accepted.Add(samplePath);
                    ProgressChanged?.Invoke(this, new WakeEnrollmentProgress(
                        accepted.Count,
                        RequiredSamples,
                        "Sample accepted"));
                }
                else
                {
                    EraseAndDelete(samplePath);
                    ProgressChanged?.Invoke(this, new WakeEnrollmentProgress(
                        accepted.Count,
                        RequiredSamples,
                        "Sample was too quiet; retrying"));
                }
            }

            if (accepted.Count < RequiredSamples)
            {
                throw new InvalidOperationException(
                    "Could not capture five suitable Jarvis samples. Check the microphone and retry.");
            }

            var modelDirectory = Path.GetDirectoryName(options.ModelPath)
                ?? throw new InvalidOperationException("Wake model directory could not be resolved.");
            Directory.CreateDirectory(modelDirectory);
            var arguments = new List<string>
            {
                "build",
                "--name", "Jarvis",
                "--path", temporaryModel
            };
            arguments.AddRange(accepted);
            await RunAsync(arguments, cancellationToken).ConfigureAwait(false);
            var positiveDetections = 0;
            foreach (var sample in accepted)
            {
                var output = await RunAsync(
                    CreateTestArguments(temporaryModel, sample),
                    cancellationToken).ConfigureAwait(false);
                if (output.Contains("Wakeword detection:", StringComparison.Ordinal))
                {
                    positiveDetections++;
                }
            }

            if (positiveDetections < RequiredSamples - 1)
            {
                throw new InvalidOperationException(
                    "Wake reference missed too many positive samples. Retry enrolment.");
            }

            var backgroundPath = Path.Combine(temporaryRoot, "background.wav");
            ProgressChanged?.Invoke(this, new WakeEnrollmentProgress(
                RequiredSamples,
                RequiredSamples,
                "Stay quiet — measuring background for four seconds"));
            await RunAsync(
                [
                    "record",
                    "--device-index", options.DeviceIndex.ToString(),
                    "--sample-rate", options.SampleRate.ToString(),
                    "--ms", BackgroundDurationMilliseconds.ToString(),
                    backgroundPath
                ],
                cancellationToken).ConfigureAwait(false);
            var backgroundOutput = await RunAsync(
                CreateTestArguments(temporaryModel, backgroundPath),
                cancellationToken).ConfigureAwait(false);
            var backgroundDetections = backgroundOutput.Split(
                "Wakeword detection:",
                StringSplitOptions.None).Length - 1;
            if (backgroundDetections > 0)
            {
                throw new InvalidOperationException(
                    "Wake reference triggered on background audio. Retry in normal room conditions.");
            }

            File.Move(temporaryModel, options.ModelPath, overwrite: true);
            await SaveCalibrationAsync(
                new WakeCalibrationResult(
                    DateTimeOffset.UtcNow,
                    options.Threshold,
                    options.MinimumScores,
                    RequiredSamples,
                    positiveDetections,
                    backgroundDetections),
                cancellationToken).ConfigureAwait(false);
            ProgressChanged?.Invoke(this, new WakeEnrollmentProgress(
                RequiredSamples,
                RequiredSamples,
                "Jarvis wake phrase enrolled"));
            return options.ModelPath;
        }
        finally
        {
            foreach (var sample in Directory.GetFiles(temporaryRoot, "*.wav"))
            {
                EraseAndDelete(sample);
            }

            File.Delete(temporaryModel);
            if (Directory.Exists(temporaryRoot))
            {
                Directory.Delete(temporaryRoot, recursive: false);
            }
        }
    }

    private async Task<string> RunAsync(
        IEnumerable<string> arguments,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo(options.ExecutablePath)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(options.ExecutablePath)
                ?? Environment.CurrentDirectory
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Rustpotter did not start.");
        using var cancellationRegistration = cancellationToken.Register(() => TryKill(process));
        var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
        try
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            await stdout.ConfigureAwait(false);
            await stderr.ConfigureAwait(false);
            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"Rustpotter enrolment failed with code {process.ExitCode}.");
            }

            return await stdout.ConfigureAwait(false);
        }
        catch
        {
            TryKill(process);
            throw;
        }
    }

    private IEnumerable<string> CreateTestArguments(string modelPath, string samplePath) =>
    [
        "test",
        "--threshold", options.Threshold.ToString(
            System.Globalization.CultureInfo.InvariantCulture),
        "--averaged-threshold", "0",
        "--min-scores", options.MinimumScores.ToString(),
        modelPath,
        samplePath
    ];

    private static async Task SaveCalibrationAsync(
        WakeCalibrationResult result,
        CancellationToken cancellationToken)
    {
        var localData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var path = Path.Combine(localData, "Jarvis", "Data", "wake-calibration.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(
            path,
            JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }),
            cancellationToken).ConfigureAwait(false);
    }

    private static bool IsSuitableSample(string path)
    {
        using var reader = new WaveFileReader(path);
        var provider = reader.ToSampleProvider();
        var buffer = new float[4096];
        var total = 0;
        var voiced = 0;
        var peak = 0f;
        int read;
        while ((read = provider.Read(buffer)) > 0)
        {
            for (var index = 0; index < read; index++)
            {
                var amplitude = Math.Abs(buffer[index]);
                peak = Math.Max(peak, amplitude);
                if (amplitude >= 0.01f)
                {
                    voiced++;
                }
            }

            total += read;
        }

        Array.Clear(buffer);
        return total > 0 && peak >= 0.04f && voiced / (double)total >= 0.03;
    }

    private static void EraseAndDelete(string path)
    {
        if (!File.Exists(path))
        {
            return;
        }

        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.None);
            var zeros = new byte[8192];
            var remaining = stream.Length;
            while (remaining > 0)
            {
                var count = (int)Math.Min(zeros.Length, remaining);
                stream.Write(zeros, 0, count);
                remaining -= count;
            }

            stream.Flush(flushToDisk: true);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
        }
    }
}
