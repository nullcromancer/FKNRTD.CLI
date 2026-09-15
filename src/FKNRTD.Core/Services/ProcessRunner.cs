using System.Diagnostics;
using System.Text;
using FKNRTD.Domain;

namespace FKNRTD.Services;

public sealed class ProcessRunner
{
    private const int CaptureLimitCharacters = 2_000_000;
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan KillDrainTimeout = TimeSpan.FromSeconds(5);

    public async Task<CommandResult> RunAsync(
        string executable,
        IEnumerable<string> arguments,
        string workingDirectory,
        IReadOnlyDictionary<string, string>? environment = null,
        string? standardInput = null,
        string? logPath = null,
        Func<string, bool, Task>? onLine = null,
        Action<int>? onStarted = null,
        CancellationToken cancellationToken = default,
        TimeSpan? timeout = null)
    {
        if (timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout), "Process timeout must be greater than zero.");
        }

        var effectiveTimeout = timeout ?? DefaultTimeout;

        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = standardInput is not null,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        if (environment is not null)
        {
            foreach (var (name, value) in environment)
            {
                startInfo.Environment[name] = value;
            }
        }

        var output = new OutputCapture();
        var error = new OutputCapture();
        StreamWriter? log = null;
        if (!string.IsNullOrWhiteSpace(logPath))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
            log = new StreamWriter(
                new FileStream(logPath, FileMode.Create, FileAccess.Write, FileShare.Read),
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false))
            {
                AutoFlush = true
            };
        }

        using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        using var logGate = new SemaphoreSlim(1, 1);
        var stopwatch = Stopwatch.StartNew();
        try
        {
            try
            {
                if (!process.Start())
                {
                    stopwatch.Stop();
                    var message = $"Unable to start process '{executable}': Process.Start returned false.";
                    if (log is not null)
                    {
                        await log.WriteLineAsync("[ERR] " + message).ConfigureAwait(false);
                    }

                    return new CommandResult
                    {
                        ExitCode = -1,
                        StandardError = message,
                        Duration = stopwatch.Elapsed,
                        StartFailed = true
                    };
                }
            }
            catch (System.ComponentModel.Win32Exception exception)
            {
                stopwatch.Stop();
                var message = $"Unable to start process '{executable}': {exception.Message}";
                if (log is not null)
                {
                    await log.WriteLineAsync("[ERR] " + message).ConfigureAwait(false);
                }

                return new CommandResult
                {
                    ExitCode = -1,
                    StandardError = message,
                    Duration = stopwatch.Elapsed,
                    StartFailed = true
                };
            }

            onStarted?.Invoke(process.Id);

            using var pumpCancellation = new CancellationTokenSource();
            var stdoutTask = PumpAsync(
                process.StandardOutput, output, false, log, logGate, onLine, pumpCancellation.Token);
            var stderrTask = PumpAsync(
                process.StandardError, error, true, log, logGate, onLine, pumpCancellation.Token);

            using var timeoutCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCancellation.CancelAfter(effectiveTimeout);
            var waitCancellation = timeoutCancellation.Token;
            var processExited = false;

            try
            {
                if (standardInput is not null)
                {
                    await process.StandardInput.WriteAsync(standardInput.AsMemory(), waitCancellation)
                        .ConfigureAwait(false);
                    await process.StandardInput.FlushAsync(waitCancellation).ConfigureAwait(false);
                    process.StandardInput.Close();
                }

                await process.WaitForExitAsync(waitCancellation).ConfigureAwait(false);
                processExited = true;
                await Task.WhenAll(stdoutTask, stderrTask).WaitAsync(waitCancellation).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (
                timeoutCancellation.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                TryKill(process);
                var pumpsDrained = processExited
                    ? await CancelAndDrainPumpsAsync(stdoutTask, stderrTask, pumpCancellation).ConfigureAwait(false)
                    : await DrainAfterKillAsync(process, stdoutTask, stderrTask, pumpCancellation).ConfigureAwait(false);
                stopwatch.Stop();
                var timeoutMessage = processExited
                    ? $"Process '{executable}' output drain timed out after " +
                      $"{effectiveTimeout.TotalSeconds:0.###} seconds; captured output was truncated."
                    : $"Process '{executable}' timed out after {effectiveTimeout.TotalSeconds:0.###} seconds.";
                return new CommandResult
                {
                    ExitCode = -1,
                    StandardOutput = output.Buffer.ToString(),
                    StandardError = AppendError(error, timeoutMessage),
                    Duration = stopwatch.Elapsed,
                    TimedOut = true,
                    OutputTruncated = output.Truncated || error.Truncated || !pumpsDrained
                };
            }
            catch (OperationCanceledException)
            {
                TryKill(process);
                await DrainAfterKillAsync(process, stdoutTask, stderrTask, pumpCancellation).ConfigureAwait(false);
                throw;
            }

            stopwatch.Stop();
            return new CommandResult
            {
                ExitCode = process.ExitCode,
                StandardOutput = output.Buffer.ToString(),
                StandardError = error.Buffer.ToString(),
                Duration = stopwatch.Elapsed,
                OutputTruncated = output.Truncated || error.Truncated
            };
        }
        finally
        {
            if (log is not null)
            {
                await log.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    public Task<CommandResult> RunShellAsync(
        string command,
        string workingDirectory,
        string? logPath = null,
        Func<string, bool, Task>? onLine = null,
        CancellationToken cancellationToken = default,
        TimeSpan? timeout = null)
    {
        if (OperatingSystem.IsWindows())
        {
            return RunAsync(
                Environment.GetEnvironmentVariable("COMSPEC") ?? "cmd.exe",
                ["/D", "/S", "/C", command],
                workingDirectory,
                logPath: logPath,
                onLine: onLine,
                cancellationToken: cancellationToken,
                timeout: timeout);
        }

        return RunAsync(
            "/bin/sh",
            ["-lc", command],
            workingDirectory,
            logPath: logPath,
            onLine: onLine,
            cancellationToken: cancellationToken,
            timeout: timeout);
    }

    private static async Task PumpAsync(
        StreamReader reader,
        OutputCapture capture,
        bool isError,
        StreamWriter? log,
        SemaphoreSlim logGate,
        Func<string, bool, Task>? onLine,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (line is null)
            {
                break;
            }

            if (capture.Buffer.Length < CaptureLimitCharacters)
            {
                var remaining = CaptureLimitCharacters - capture.Buffer.Length;
                capture.Buffer.AppendLine(line.Length <= remaining ? line : line[..remaining]);
                capture.Truncated |= line.Length > remaining;
            }
            else
            {
                capture.Truncated = true;
            }

            if (log is not null)
            {
                await logGate.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    await log.WriteLineAsync($"[{(isError ? "ERR" : "OUT")}] {line}")
                        .ConfigureAwait(false);
                }
                finally
                {
                    logGate.Release();
                }
            }

            if (onLine is not null)
            {
                await onLine(line, isError).ConfigureAwait(false);
            }
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
            // The process exited between the check and the kill request.
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // The operating system already released the process or denied the kill.
        }
        catch (NotSupportedException)
        {
            // Process-tree termination is unavailable on this platform.
        }
    }

    private static async Task<bool> DrainAfterKillAsync(
        Process process,
        Task stdoutTask,
        Task stderrTask,
        CancellationTokenSource pumpCancellation)
    {
        var exited = false;
        try
        {
            await process.WaitForExitAsync(CancellationToken.None).WaitAsync(KillDrainTimeout).ConfigureAwait(false);
            exited = true;
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or TimeoutException or System.ComponentModel.Win32Exception)
        {
            // Failed or denied termination must not turn timeout handling into another unbounded wait.
        }

        if (!exited)
        {
            pumpCancellation.Cancel();
        }

        try
        {
            await Task.WhenAll(stdoutTask, stderrTask).WaitAsync(KillDrainTimeout).ConfigureAwait(false);
            return !pumpCancellation.IsCancellationRequested;
        }
        catch (Exception exception) when (exception is OperationCanceledException or TimeoutException)
        {
            pumpCancellation.Cancel();
            return false;
        }
    }

    private static async Task<bool> CancelAndDrainPumpsAsync(
        Task stdoutTask,
        Task stderrTask,
        CancellationTokenSource pumpCancellation)
    {
        pumpCancellation.Cancel();
        try
        {
            await Task.WhenAll(stdoutTask, stderrTask).WaitAsync(KillDrainTimeout).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is OperationCanceledException or TimeoutException)
        {
            // Output readers may be blocked in inherited pipe handles or user callbacks. Do not wait forever.
        }

        return false;
    }

    private static string AppendError(OutputCapture error, string message) =>
        error.Buffer.Length == 0
            ? message
            : error.Buffer.ToString().TrimEnd() + Environment.NewLine + message;

    private sealed class OutputCapture
    {
        public StringBuilder Buffer { get; } = new();
        public bool Truncated { get; set; }
    }
}

public static class ExecutableLocator
{
    public static string? Find(string executable)
    {
        if (Path.IsPathRooted(executable) || executable.Contains(Path.DirectorySeparatorChar) ||
            executable.Contains(Path.AltDirectorySeparatorChar))
        {
            return File.Exists(executable) ? Path.GetFullPath(executable) : null;
        }

        var isWindows = OperatingSystem.IsWindows();
        var extensions = isWindows
            ? (Environment.GetEnvironmentVariable("PATHEXT") ?? ".EXE;.CMD;.BAT;.COM")
                .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            : [string.Empty];

        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
                     .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var searchDirectory = directory.Trim().Trim('"');
            var exact = Path.Combine(searchDirectory, executable);
            if (!isWindows || Path.HasExtension(executable))
            {
                if (File.Exists(exact))
                {
                    return Path.GetFullPath(exact);
                }
            }

            foreach (var extension in extensions)
            {
                var normalizedExtension = extension.Trim();
                if (normalizedExtension.Length == 0 ||
                    executable.EndsWith(normalizedExtension, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var candidate = Path.Combine(searchDirectory, executable + normalizedExtension);
                if (File.Exists(candidate))
                {
                    return Path.GetFullPath(candidate);
                }
            }

            if (isWindows && File.Exists(exact))
            {
                return Path.GetFullPath(exact);
            }
        }

        return null;
    }
}
