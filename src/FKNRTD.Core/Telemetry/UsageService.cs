using System.Diagnostics;
using System.Text;
using System.Text.Json;
using FKNRTD.Domain;
using FKNRTD.Services;

namespace FKNRTD.Telemetry;

public sealed class UsageService
{
    private readonly StateStore _store;

    public UsageService(StateStore store)
    {
        _store = store;
    }

    public async Task<UsageSnapshot> IngestClaudeAsync(
        Stream input,
        CancellationToken cancellationToken = default)
    {
        using var document = await JsonDocument.ParseAsync(input, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        var snapshot = ParseClaudeStatusLine(document.RootElement);
        await _store.SaveUsageAsync(snapshot, cancellationToken).ConfigureAwait(false);
        return snapshot;
    }

    public static UsageSnapshot ParseClaudeStatusLine(JsonElement root) => new()
    {
        AgentId = "claude",
        ContextRemainingPercent = ReadNumber(root, "context_window", "remaining_percentage") ??
                                  Remaining(ReadNumber(root, "context_window", "used_percentage")),
        FiveHourRemainingPercent = Remaining(
            ReadNumber(root, "rate_limits", "five_hour", "used_percentage")),
        WeeklyRemainingPercent = Remaining(
            ReadNumber(root, "rate_limits", "seven_day", "used_percentage")),
        FiveHourResetsAt = ReadUnixTime(root, "rate_limits", "five_hour", "resets_at"),
        WeeklyResetsAt = ReadUnixTime(root, "rate_limits", "seven_day", "resets_at"),
        Source = "claude-statusline",
        UpdatedAt = DateTimeOffset.UtcNow
    };

    public async Task<UsageSnapshot> RefreshCodexAsync(CancellationToken cancellationToken = default)
    {
        var executable = ExecutableLocator.Find("codex")
            ?? throw new FileNotFoundException("The Codex executable was not found on PATH.");
        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            WorkingDirectory = _store.Paths.Root,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardInputEncoding = Encoding.UTF8,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        startInfo.ArgumentList.Add("app-server");

        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
        {
            throw new InvalidOperationException("Unable to start the Codex app server.");
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        var errorTask = process.StandardError.ReadToEndAsync(timeout.Token);

        try
        {
            await SendAsync(process, new
            {
                method = "initialize",
                id = 1,
                @params = new
                {
                    clientInfo = new { name = "fknrtd", title = "FKNRTD.CLI", version = "1.0.0" }
                }
            }, timeout.Token).ConfigureAwait(false);
            await SendAsync(process, new { method = "initialized", @params = new { } }, timeout.Token)
                .ConfigureAwait(false);
            await SendAsync(process, new { method = "account/rateLimits/read", id = 2 }, timeout.Token)
                .ConfigureAwait(false);

            while (!timeout.IsCancellationRequested)
            {
                var line = await process.StandardOutput.ReadLineAsync(timeout.Token).ConfigureAwait(false);
                if (line is null)
                {
                    break;
                }

                using var document = JsonDocument.Parse(line);
                if (!TryGetInt(document.RootElement, "id", out var id) || id != 2)
                {
                    continue;
                }

                if (TryGetProperty(document.RootElement, "error", out var error))
                {
                    throw new InvalidOperationException("Codex rate-limit query failed: " + error.GetRawText());
                }

                if (!TryGetProperty(document.RootElement, "result", out var result))
                {
                    throw new InvalidDataException("Codex returned a rate-limit response without a result.");
                }

                var snapshot = ParseCodexRateLimits(result);
                await _store.SaveUsageAsync(snapshot, cancellationToken).ConfigureAwait(false);
                return snapshot;
            }

            var stderr = await errorTask.ConfigureAwait(false);
            throw new InvalidOperationException(
                "Codex app-server ended before returning rate limits." +
                (stderr.Length > 0 ? " " + stderr.Trim() : string.Empty));
        }
        finally
        {
            TryStop(process);
        }
    }

    public async Task<UsageSnapshot> SetAsync(
        string agentId,
        double? contextRemaining,
        double? fiveHourRemaining,
        double? weeklyRemaining,
        string source = "manual",
        CancellationToken cancellationToken = default)
    {
        var snapshot = new UsageSnapshot
        {
            AgentId = agentId,
            ContextRemainingPercent = Clamp(contextRemaining),
            FiveHourRemainingPercent = Clamp(fiveHourRemaining),
            WeeklyRemainingPercent = Clamp(weeklyRemaining),
            Source = source,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        await _store.SaveUsageAsync(snapshot, cancellationToken).ConfigureAwait(false);
        return snapshot;
    }

    public static UsageSnapshot ParseCodexRateLimits(JsonElement result)
    {
        var windows = new List<(long? Minutes, double Used, long? ResetsAt)>();
        if (TryGetProperty(result, "rateLimits", out var defaultLimits))
        {
            CollectWindows(defaultLimits, windows);
        }

        if (TryGetProperty(result, "rateLimitsByLimitId", out var byId) &&
            byId.ValueKind == JsonValueKind.Object)
        {
            foreach (var bucket in byId.EnumerateObject())
            {
                CollectWindows(bucket.Value, windows);
            }
        }

        var fiveHour = windows.FirstOrDefault(window => window.Minutes == 300);
        var weekly = windows.FirstOrDefault(window => window.Minutes == 10_080);
        return new UsageSnapshot
        {
            AgentId = "codex",
            FiveHourRemainingPercent = fiveHour.Minutes is null ? null : Remaining(fiveHour.Used),
            WeeklyRemainingPercent = weekly.Minutes is null ? null : Remaining(weekly.Used),
            FiveHourResetsAt = FromUnix(fiveHour.ResetsAt),
            WeeklyResetsAt = FromUnix(weekly.ResetsAt),
            Source = "codex-app-server",
            UpdatedAt = DateTimeOffset.UtcNow
        };
    }

    private static void CollectWindows(
        JsonElement limits,
        ICollection<(long? Minutes, double Used, long? ResetsAt)> windows)
    {
        foreach (var name in new[] { "primary", "secondary" })
        {
            if (!TryGetProperty(limits, name, out var window) || window.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var minutes = GetInt64(window, "windowDurationMins") ?? GetInt64(window, "window_minutes");
            var used = GetDouble(window, "usedPercent") ?? GetDouble(window, "used_percent");
            var resetsAt = GetInt64(window, "resetsAt") ?? GetInt64(window, "resets_at");
            if (used is not null)
            {
                windows.Add((minutes, used.Value, resetsAt));
            }
        }
    }

    private static async Task SendAsync(Process process, object message, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(message, JsonSupport.CompactOptions);
        await process.StandardInput.WriteLineAsync(json.AsMemory(), cancellationToken).ConfigureAwait(false);
        await process.StandardInput.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static double? ReadNumber(JsonElement root, params string[] path)
    {
        var current = root;
        foreach (var name in path)
        {
            if (!TryGetProperty(current, name, out current))
            {
                return null;
            }
        }

        return current.ValueKind == JsonValueKind.Number && current.TryGetDouble(out var value) ? value : null;
    }

    private static DateTimeOffset? ReadUnixTime(JsonElement root, params string[] path)
    {
        var value = ReadNumber(root, path);
        return value is null ? null : DateTimeOffset.FromUnixTimeSeconds(Convert.ToInt64(value.Value));
    }

    private static double? Remaining(double? used) => used is null ? null : Clamp(100 - used.Value);

    private static double? Clamp(double? value) => value is null ? null : Math.Clamp(value.Value, 0, 100);

    private static DateTimeOffset? FromUnix(long? value) =>
        value is null ? null : DateTimeOffset.FromUnixTimeSeconds(value.Value);

    private static double? GetDouble(JsonElement element, string name) =>
        TryGetProperty(element, name, out var value) && value.TryGetDouble(out var result) ? result : null;

    private static long? GetInt64(JsonElement element, string name) =>
        TryGetProperty(element, name, out var value) && value.TryGetInt64(out var result) ? result : null;

    private static bool TryGetInt(JsonElement element, string name, out int result)
    {
        result = 0;
        return TryGetProperty(element, name, out var value) && value.TryGetInt32(out result);
    }

    private static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (property.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }
        }

        value = default;
        return false;
    }

    private static void TryStop(Process process)
    {
        try
        {
            process.StandardInput.Close();
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(2000);
            }
        }
        catch (InvalidOperationException)
        {
            // The process has already exited.
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // The operating system already released the process.
        }
    }
}
