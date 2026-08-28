using System.Diagnostics;

namespace Jarvis.Speech;

public sealed class RustpotterWakeWordListener(WakeWordOptions options) : IWakeWordListener
{
    private readonly object _sync = new();
    private Process? _process;
    private CancellationTokenSource? _monitorCancellation;
    private Task? _monitorTask;
    private bool _disposed;

    public bool IsListening
    {
        get
        {
            lock (_sync)
            {
                return _process is { HasExited: false };
            }
        }
    }

    public event EventHandler<WakeWordDetection>? Detected;

    public event EventHandler<Exception>? Failed;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        lock (_sync)
        {
            if (_process is { HasExited: false })
            {
                return Task.CompletedTask;
            }

            if (!File.Exists(options.ExecutablePath))
            {
                throw new FileNotFoundException(
                    "The local Rustpotter runtime was not found.",
                    options.ExecutablePath);
            }

            if (!File.Exists(options.ModelPath))
            {
                throw new FileNotFoundException(
                    "Enrol the Jarvis wake phrase before enabling hands-free mode.",
                    options.ModelPath);
            }

            var startInfo = new ProcessStartInfo(options.ExecutablePath)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(options.ExecutablePath)
                    ?? Environment.CurrentDirectory
            };
            startInfo.ArgumentList.Add("spot");
            startInfo.ArgumentList.Add("--device-index");
            startInfo.ArgumentList.Add(options.DeviceIndex.ToString());
            startInfo.ArgumentList.Add("--sample-rate");
            startInfo.ArgumentList.Add(options.SampleRate.ToString());
            startInfo.ArgumentList.Add("--threshold");
            startInfo.ArgumentList.Add(options.Threshold.ToString(
                System.Globalization.CultureInfo.InvariantCulture));
            startInfo.ArgumentList.Add("--min-scores");
            startInfo.ArgumentList.Add(options.MinimumScores.ToString());
            startInfo.ArgumentList.Add(options.ModelPath);

            _process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("Rustpotter did not start.");
            _monitorCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);
            _monitorTask = MonitorAsync(_process, _monitorCancellation.Token);
        }

        return Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        Process? process;
        Task? monitor;
        lock (_sync)
        {
            process = _process;
            monitor = _monitorTask;
            _monitorCancellation?.Cancel();
            _process = null;
            _monitorTask = null;
        }

        TryKill(process);
        if (monitor is not null)
        {
            try
            {
                await monitor.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        process?.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        await StopAsync().ConfigureAwait(false);
        _monitorCancellation?.Dispose();
    }

    private async Task MonitorAsync(Process process, CancellationToken cancellationToken)
    {
        var stderr = DrainAsync(process.StandardError, cancellationToken);
        try
        {
            while (await process.StandardOutput.ReadLineAsync(cancellationToken)
                       .ConfigureAwait(false) is { } line)
            {
                if (line.Contains("Wakeword detection:", StringComparison.Ordinal))
                {
                    Detected?.Invoke(this, new WakeWordDetection(DateTimeOffset.UtcNow));
                }
            }

            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            await stderr.ConfigureAwait(false);
            if (process.ExitCode != 0 && !cancellationToken.IsCancellationRequested)
            {
                Failed?.Invoke(this, new InvalidOperationException(
                    $"Rustpotter stopped with code {process.ExitCode}."));
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            Failed?.Invoke(this, exception);
        }
    }

    private static async Task DrainAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is not null)
        {
        }
    }

    private static void TryKill(Process? process)
    {
        try
        {
            if (process is { HasExited: false })
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
        }
    }
}
