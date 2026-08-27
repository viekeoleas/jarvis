using System.Diagnostics;

namespace Jarvis.Speech;

public sealed class WhisperProcessTranscriber(WhisperProcessOptions options) : ILocalTranscriber
{
    public async Task<LocalTranscription> TranscribeAsync(
        ReadOnlyMemory<short> samples,
        CancellationToken cancellationToken)
    {
        if (samples.IsEmpty)
        {
            return new LocalTranscription(string.Empty, WhisperBackend.Cpu, TimeSpan.Zero);
        }

        ValidateAssets();
        var wave = PcmWaveEncoder.Encode(samples.Span);
        try
        {
            if (options.PreferVulkan)
            {
                try
                {
                    return await RunAsync(wave, WhisperBackend.Vulkan, cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (WhisperProcessException) when (!cancellationToken.IsCancellationRequested)
                {
                    // One CPU attempt keeps speech usable if Vulkan initialization fails.
                }
            }

            return await RunAsync(wave, WhisperBackend.Cpu, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            Array.Clear(wave);
        }
    }

    private async Task<LocalTranscription> RunAsync(
        byte[] wave,
        WhisperBackend backend,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo(options.ExecutablePath)
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(options.ExecutablePath)
                ?? Environment.CurrentDirectory
        };
        AddArguments(startInfo, backend);

        using var process = Process.Start(startInfo)
            ?? throw new WhisperProcessException("whisper.cpp did not start.");
        using var cancellationRegistration = cancellationToken.Register(() => TryKill(process));
        var timer = Stopwatch.StartNew();
        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = DrainAsync(process.StandardError, cancellationToken);

        try
        {
            await process.StandardInput.BaseStream.WriteAsync(wave, cancellationToken)
                .ConfigureAwait(false);
            process.StandardInput.Close();
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            var transcript = (await stdoutTask.ConfigureAwait(false)).Trim();
            await stderrTask.ConfigureAwait(false);
            timer.Stop();

            if (process.ExitCode != 0)
            {
                throw new WhisperProcessException(
                    $"whisper.cpp {backend} transcription failed with code {process.ExitCode}.");
            }

            return new LocalTranscription(transcript, backend, timer.Elapsed);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            TryKill(process);
            throw;
        }
        catch
        {
            TryKill(process);
            throw;
        }
    }

    private void AddArguments(ProcessStartInfo startInfo, WhisperBackend backend)
    {
        startInfo.ArgumentList.Add("--model");
        startInfo.ArgumentList.Add(options.ModelPath);
        startInfo.ArgumentList.Add("--language");
        startInfo.ArgumentList.Add("auto");
        startInfo.ArgumentList.Add("--no-timestamps");
        startInfo.ArgumentList.Add("--no-prints");
        startInfo.ArgumentList.Add("--no-fallback");
        startInfo.ArgumentList.Add("--output-file");
        startInfo.ArgumentList.Add("NUL");
        if (backend == WhisperBackend.Cpu)
        {
            startInfo.ArgumentList.Add("--no-gpu");
        }

        startInfo.ArgumentList.Add("-");
    }

    private void ValidateAssets()
    {
        if (!File.Exists(options.ExecutablePath))
        {
            throw new FileNotFoundException("The local whisper.cpp executable was not found.", options.ExecutablePath);
        }

        if (!File.Exists(options.ModelPath))
        {
            throw new FileNotFoundException("The local multilingual Whisper model was not found.", options.ModelPath);
        }
    }

    private static async Task DrainAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is not null)
        {
            // Backend diagnostics are intentionally discarded; they are not Jarvis logs.
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

    private sealed class WhisperProcessException(string message) : Exception(message);
}
