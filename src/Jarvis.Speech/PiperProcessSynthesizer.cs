using System.Diagnostics;
using System.Text;

namespace Jarvis.Speech;

public sealed class PiperProcessSynthesizer(PiperOptions options)
{
    public async Task<SynthesizedSpeech> SynthesizeAsync(
        string text,
        SpeechLanguage language,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return new SynthesizedSpeech([], 22_050, TimeSpan.Zero);
        }

        if (!File.Exists(options.PythonExecutable))
        {
            throw new FileNotFoundException("The local Piper runtime was not found.", options.PythonExecutable);
        }

        if (!options.Voices.TryGetValue(language, out var voice) || !File.Exists(voice.ModelPath))
        {
            throw new FileNotFoundException($"The local Piper {language} voice was not found.");
        }

        var startInfo = new ProcessStartInfo(options.PythonExecutable)
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = Encoding.UTF8,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(options.PythonExecutable)
                ?? Environment.CurrentDirectory
        };
        startInfo.Environment["PYTHONUTF8"] = "1";
        startInfo.Environment["PYTHONIOENCODING"] = "utf-8";
        startInfo.ArgumentList.Add("-m");
        startInfo.ArgumentList.Add("piper");
        startInfo.ArgumentList.Add("--model");
        startInfo.ArgumentList.Add(voice.ModelPath);
        startInfo.ArgumentList.Add("--output-raw");

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Piper did not start.");
        using var cancellationRegistration = cancellationToken.Register(() => TryKill(process));
        var timer = Stopwatch.StartNew();
        var maximumBytes = checked((int)(
            options.MaximumSpokenDuration.TotalSeconds * voice.SampleRate * sizeof(short)));
        var audioTask = ReadBoundedAsync(
            process.StandardOutput.BaseStream,
            maximumBytes,
            cancellationToken);
        var stderrTask = DrainAsync(process.StandardError, cancellationToken);

        try
        {
            await process.StandardInput.WriteLineAsync(text.AsMemory(), cancellationToken)
                .ConfigureAwait(false);
            process.StandardInput.Close();
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            var audio = await audioTask.ConfigureAwait(false);
            await stderrTask.ConfigureAwait(false);
            timer.Stop();

            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"Local Piper synthesis failed with code {process.ExitCode}.");
            }

            return new SynthesizedSpeech(audio, voice.SampleRate, timer.Elapsed);
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

    private static async Task<byte[]> ReadBoundedAsync(
        Stream stream,
        int maximumBytes,
        CancellationToken cancellationToken)
    {
        using var audio = new MemoryStream();
        var buffer = new byte[81_920];
        while (true)
        {
            var read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                return audio.ToArray();
            }

            if (audio.Length + read > maximumBytes)
            {
                throw new InvalidDataException("Piper exceeded the bounded in-memory speech duration.");
            }

            await audio.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task DrainAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is not null)
        {
            // Piper stderr is never copied into Jarvis diagnostics.
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
