using System.Diagnostics;
using System.Text;
using FKNRTD.Domain;

namespace FKNRTD.Services;

public sealed class ProcessRunner
{
    private const int CaptureLimitCharacters = 2_000_000;

    public async Task<CommandResult> RunAsync(
        string executable,
        IEnumerable<string> arguments,
        string workingDirectory,
        IReadOnlyDictionary<string, string>? environment = null,
        string? standardInput = null,
        string? logPath = null,
        Func<string, bool, Task>? onLine = null,
        Action<int>? onStarted = null,
        CancellationToken cancellationToken = default)
    {
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

        var output = new StringBuilder();
        var error = new StringBuilder();
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
            if (!process.Start())
            {
                throw new InvalidOperationException($"Unable to start process '{executable}'.");
            }

            onStarted?.Invoke(process.Id);

            if (standardInput is not null)
            {
                await process.StandardInput.WriteAsync(standardInput).ConfigureAwait(false);
                await process.StandardInput.FlushAsync(cancellationToken).ConfigureAwait(false);
                process.StandardInput.Close();
            }

            var stdoutTask = PumpAsync(process.StandardOutput, output, false, log, logGate, onLine, cancellationToken);
            var stderrTask = PumpAsync(process.StandardError, error, true, log, logGate, onLine, cancellationToken);

            try
            {
                await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                TryKill(process);
                throw;
            }

            await Task.WhenAll(stdoutTask, stderrTask).ConfigureAwait(false);
            stopwatch.Stop();
            return new CommandResult
            {
                ExitCode = process.ExitCode,
                StandardOutput = output.ToString(),
                StandardError = error.ToString(),
                Duration = stopwatch.Elapsed
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
        CancellationToken cancellationToken = default)
    {
        if (OperatingSystem.IsWindows())
        {
            return RunAsync(
                Environment.GetEnvironmentVariable("COMSPEC") ?? "cmd.exe",
                ["/D", "/S", "/C", command],
                workingDirectory,
                logPath: logPath,
                onLine: onLine,
                cancellationToken: cancellationToken);
        }

        return RunAsync(
            "/bin/sh",
            ["-lc", command],
            workingDirectory,
            logPath: logPath,
            onLine: onLine,
            cancellationToken: cancellationToken);
    }

    private static async Task PumpAsync(
        StreamReader reader,
        StringBuilder capture,
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

            if (capture.Length < CaptureLimitCharacters)
            {
                var remaining = CaptureLimitCharacters - capture.Length;
                capture.AppendLine(line.Length <= remaining ? line : line[..remaining]);
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

        var extensions = OperatingSystem.IsWindows()
            ? (Environment.GetEnvironmentVariable("PATHEXT") ?? ".EXE;.CMD;.BAT;.COM")
                .Split(';', StringSplitOptions.RemoveEmptyEntries)
            : [string.Empty];

        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
                     .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var searchDirectory = directory.Trim().Trim('"');
            var exact = Path.Combine(searchDirectory, executable);
            if (File.Exists(exact))
            {
                return Path.GetFullPath(exact);
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
        }

        return null;
    }
}
