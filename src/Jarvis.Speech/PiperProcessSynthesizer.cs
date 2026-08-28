using System.Buffers.Binary;
using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace Jarvis.Speech;

public sealed class PiperProcessSynthesizer(PiperOptions options) : IAsyncDisposable
{
    private static readonly byte[] ReadyMarker = "JRV1"u8.ToArray();
    private readonly Dictionary<SpeechLanguage, Task<PiperWorker>> _workers = [];
    private readonly object _workersSync = new();
    private bool _disposed;

    public async Task PreloadAsync(
        SpeechLanguage language,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _ = await GetWorkerAsync(language, cancellationToken).ConfigureAwait(false);
    }

    public async Task<SynthesizedSpeech> SynthesizeAsync(
        string text,
        SpeechLanguage language,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (string.IsNullOrWhiteSpace(text))
        {
            return new SynthesizedSpeech([], 22_050, TimeSpan.Zero);
        }

        if (!options.Voices.TryGetValue(language, out var voice))
        {
            throw new FileNotFoundException($"The local Piper {language} voice was not found.");
        }

        var worker = await GetWorkerAsync(language, cancellationToken).ConfigureAwait(false);
        try
        {
            var maximumBytes = checked((int)(
                options.MaximumSpokenDuration.TotalSeconds * voice.SampleRate * sizeof(short)));
            var timer = Stopwatch.StartNew();
            var audio = await worker.SynthesizeAsync(text, maximumBytes, cancellationToken)
                .ConfigureAwait(false);
            timer.Stop();
            return new SynthesizedSpeech(audio, voice.SampleRate, timer.Elapsed);
        }
        catch
        {
            await RemoveWorkerAsync(language, worker).ConfigureAwait(false);
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Task<PiperWorker>[] workers;
        lock (_workersSync)
        {
            workers = _workers.Values.ToArray();
            _workers.Clear();
        }
        foreach (var workerTask in workers)
        {
            try
            {
                var worker = await workerTask.ConfigureAwait(false);
                await worker.DisposeAsync().ConfigureAwait(false);
            }
            catch
            {
                // A worker that failed during startup owns no reusable resources.
            }
        }
    }

    private async Task<PiperWorker> GetWorkerAsync(
        SpeechLanguage language,
        CancellationToken cancellationToken)
    {
        ValidateAssets(language);
        Task<PiperWorker> workerTask;
        lock (_workersSync)
        {
            if (!_workers.TryGetValue(language, out workerTask!))
            {
                workerTask = PiperWorker.StartAsync(
                    options.PythonExecutable,
                    options.WorkerScriptPath,
                    options.Voices[language].ModelPath,
                    CancellationToken.None);
                _workers.Add(language, workerTask);
            }
        }

        try
        {
            return await workerTask.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Startup continues for the background preloader or the next request.
            throw;
        }
        catch
        {
            lock (_workersSync)
            {
                if (_workers.TryGetValue(language, out var currentTask) &&
                    ReferenceEquals(currentTask, workerTask))
                {
                    _workers.Remove(language);
                }
            }

            throw;
        }
    }

    private async Task RemoveWorkerAsync(SpeechLanguage language, PiperWorker worker)
    {
        Task<PiperWorker>? task;
        lock (_workersSync)
        {
            if (!_workers.TryGetValue(language, out task) ||
                !task.IsCompletedSuccessfully ||
                !ReferenceEquals(task.Result, worker))
            {
                return;
            }

            _workers.Remove(language);
        }

        await worker.DisposeAsync().ConfigureAwait(false);
    }

    private void ValidateAssets(SpeechLanguage language)
    {
        if (!File.Exists(options.PythonExecutable))
        {
            throw new FileNotFoundException("The local Piper runtime was not found.", options.PythonExecutable);
        }

        if (!File.Exists(options.WorkerScriptPath))
        {
            throw new FileNotFoundException("The Jarvis Piper worker was not found.", options.WorkerScriptPath);
        }

        if (!options.Voices.TryGetValue(language, out var voice) || !File.Exists(voice.ModelPath))
        {
            throw new FileNotFoundException($"The local Piper {language} voice was not found.");
        }
    }

    private sealed class PiperWorker : IAsyncDisposable
    {
        private readonly Process _process;
        private readonly SemaphoreSlim _requestGate = new(1, 1);
        private readonly CancellationTokenSource _lifetime = new();
        private readonly Task _stderrTask;
        private bool _disposed;

        private PiperWorker(Process process)
        {
            _process = process;
            _stderrTask = DrainAsync(process.StandardError, _lifetime.Token);
        }

        public static async Task<PiperWorker> StartAsync(
            string pythonExecutable,
            string workerScript,
            string modelPath,
            CancellationToken cancellationToken)
        {
            var startInfo = new ProcessStartInfo(pythonExecutable)
            {
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardInputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                StandardErrorEncoding = Encoding.UTF8,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(pythonExecutable)
                    ?? Environment.CurrentDirectory
            };
            startInfo.Environment["PYTHONUTF8"] = "1";
            startInfo.Environment["PYTHONIOENCODING"] = "utf-8";
            startInfo.ArgumentList.Add("-u");
            startInfo.ArgumentList.Add(workerScript);
            startInfo.ArgumentList.Add(modelPath);

            var process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("Piper worker did not start.");
            try
            {
                var ready = new byte[ReadyMarker.Length];
                await process.StandardOutput.BaseStream.ReadExactlyAsync(ready, cancellationToken)
                    .ConfigureAwait(false);
                if (!ready.AsSpan().SequenceEqual(ReadyMarker))
                {
                    throw new InvalidDataException("Piper worker returned an invalid ready marker.");
                }

                return new PiperWorker(process);
            }
            catch
            {
                TryKill(process);
                process.Dispose();
                throw;
            }
        }

        public async Task<byte[]> SynthesizeAsync(
            string text,
            int maximumBytes,
            CancellationToken cancellationToken)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            await _requestGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await _process.StandardInput.WriteLineAsync(
                        JsonSerializer.Serialize(text).AsMemory(),
                        cancellationToken)
                    .ConfigureAwait(false);
                await _process.StandardInput.FlushAsync(cancellationToken).ConfigureAwait(false);

                var header = new byte[sizeof(int)];
                await _process.StandardOutput.BaseStream.ReadExactlyAsync(header, cancellationToken)
                    .ConfigureAwait(false);
                var length = BinaryPrimitives.ReadInt32LittleEndian(header);
                if (length < 0)
                {
                    var error = new byte[-length];
                    await _process.StandardOutput.BaseStream.ReadExactlyAsync(error, cancellationToken)
                        .ConfigureAwait(false);
                    throw new InvalidOperationException(
                        $"Local Piper synthesis failed: {Encoding.UTF8.GetString(error)}");
                }

                if (length > maximumBytes)
                {
                    throw new InvalidDataException("Piper exceeded the bounded in-memory speech duration.");
                }

                var audio = new byte[length];
                await _process.StandardOutput.BaseStream.ReadExactlyAsync(audio, cancellationToken)
                    .ConfigureAwait(false);
                return audio;
            }
            finally
            {
                _requestGate.Release();
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _lifetime.Cancel();
            try
            {
                _process.StandardInput.Close();
                if (!_process.HasExited)
                {
                    _process.Kill(entireProcessTree: true);
                }

                await _process.WaitForExitAsync().ConfigureAwait(false);
            }
            catch (InvalidOperationException)
            {
            }

            try
            {
                await _stderrTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }

            _process.Dispose();
            _requestGate.Dispose();
            _lifetime.Dispose();
        }

        private static async Task DrainAsync(StreamReader reader, CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested &&
                   await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is not null)
            {
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
}
