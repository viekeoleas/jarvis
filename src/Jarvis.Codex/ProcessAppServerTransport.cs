using System.Diagnostics;
using System.Text;

namespace Jarvis.Codex;

public sealed class ProcessAppServerTransport(CodexProcessOptions options) : IAppServerTransport
{
    private readonly CancellationTokenSource _lifetime = new();
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private Process? _process;
    private Task? _stdoutLoop;
    private Task? _stderrLoop;
    private bool _disposing;

    public event EventHandler<string>? MessageReceived;

    public event EventHandler<AppServerTransportExitedEventArgs>? Exited;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (_process is not null)
        {
            throw new InvalidOperationException("The Codex app-server transport is already started.");
        }

        await ValidateVersionAsync(cancellationToken).ConfigureAwait(false);
        Directory.CreateDirectory(options.CodexHome);

        var startInfo = new ProcessStartInfo(options.ExecutablePath)
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("app-server");
        startInfo.ArgumentList.Add("--stdio");
        startInfo.Environment["CODEX_HOME"] = options.CodexHome;
        startInfo.Environment.Remove("OPENAI_API_KEY");
        startInfo.Environment.Remove("OPENAI_BASE_URL");
        startInfo.Environment.Remove("OPENAI_API_BASE");

        var process = new Process
        {
            StartInfo = startInfo,
            EnableRaisingEvents = true
        };
        process.Exited += OnProcessExited;

        if (!process.Start())
        {
            process.Dispose();
            throw new InvalidOperationException("The Codex app-server process did not start.");
        }

        _process = process;
        _stdoutLoop = ReadStdoutAsync(process, _lifetime.Token);
        _stderrLoop = DrainStderrAsync(process, _lifetime.Token);

        await Task.Yield();
        if (process.HasExited)
        {
            throw new InvalidOperationException(
                $"Codex app-server exited during startup with code {process.ExitCode}.");
        }
    }

    public async Task SendAsync(string message, CancellationToken cancellationToken)
    {
        var process = _process ?? throw new InvalidOperationException("Codex app-server is not started.");
        if (process.HasExited)
        {
            throw new InvalidOperationException("Codex app-server is no longer running.");
        }

        await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await process.StandardInput.WriteLineAsync(message.AsMemory(), cancellationToken)
                .ConfigureAwait(false);
            await process.StandardInput.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposing)
        {
            return;
        }

        _disposing = true;
        _lifetime.Cancel();

        var process = _process;
        if (process is not null)
        {
            process.Exited -= OnProcessExited;
            try
            {
                process.StandardInput.Close();
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }

                await process.WaitForExitAsync().ConfigureAwait(false);
            }
            catch (InvalidOperationException)
            {
                // The process ended between the state check and cleanup.
            }

            process.Dispose();
        }

        await IgnoreCancellationAsync(_stdoutLoop).ConfigureAwait(false);
        await IgnoreCancellationAsync(_stderrLoop).ConfigureAwait(false);
        _writeGate.Dispose();
        _lifetime.Dispose();
    }

    private async Task ValidateVersionAsync(CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo(options.ExecutablePath)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("--version");

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("The Codex version probe did not start.");
        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        var output = await outputTask.ConfigureAwait(false);

        if (process.ExitCode != 0 || !CodexCompatibility.IsSupportedCliVersion(output))
        {
            throw new InvalidOperationException(
                $"Jarvis requires Codex {options.ExpectedVersion}x; the selected executable reported {output.Trim()}.");
        }
    }

    private async Task ReadStdoutAsync(Process process, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var line = await process.StandardOutput.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                if (line is null)
                {
                    break;
                }

                if (!string.IsNullOrWhiteSpace(line))
                {
                    MessageReceived?.Invoke(this, line);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private static async Task DrainStderrAsync(Process process, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested &&
                   await process.StandardError.ReadLineAsync(cancellationToken).ConfigureAwait(false) is not null)
            {
                // Intentionally discard app-server stderr. It may contain private request context.
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private void OnProcessExited(object? sender, EventArgs e)
    {
        if (_disposing)
        {
            return;
        }

        var process = (Process?)sender;
        var exception = new InvalidOperationException(
            $"Codex app-server exited unexpectedly with code {process?.ExitCode}.");
        Exited?.Invoke(this, new AppServerTransportExitedEventArgs(exception));
    }

    private static async Task IgnoreCancellationAsync(Task? task)
    {
        if (task is null)
        {
            return;
        }

        try
        {
            await task.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
    }
}
