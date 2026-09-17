using System.Text.Json;
using FKNRTD.Domain;
using FKNRTD.Services;
using FKNRTD.Telemetry;

namespace FKNRTD.Commands;

internal static class StatusLineRenderer
{
    public static async Task<int> RenderClaudeAsync(
        CliArguments arguments,
        CancellationToken cancellationToken)
    {
        try
        {
            var input = await Console.In.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
            using var document = JsonDocument.Parse(input);
            var claude = UsageService.ParseClaudeStatusLine(document.RootElement);
            var start = ReadString(document.RootElement, "workspace", "current_dir") ??
                        ReadString(document.RootElement, "workspace", "project_dir") ??
                        Environment.CurrentDirectory;

            FknrtdPaths? paths = null;
            try
            {
                paths = WorkspaceLocator.Find(start);
            }
            catch (InvalidOperationException)
            {
                // Claude may render before FKNRTD.CLI is initialized. A useful usage line is still possible.
            }

            var useColor = !arguments.Has("no-color") &&
                           string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NO_COLOR"));
            if (paths is null)
            {
                Console.WriteLine(RenderUninitialized(claude, useColor));
                return 0;
            }

            var store = new StateStore(paths);
            var configTask = store.LoadConfigAsync(cancellationToken);
            var usageTask = store.LoadUsageAsync(cancellationToken);
            var agentsTask = store.LoadAgentRuntimesAsync(cancellationToken);
            var claimsTask = store.LoadClaimsAsync(cancellationToken);
            await Task.WhenAll(configTask, usageTask, agentsTask, claimsTask).ConfigureAwait(false);

            var config = await configTask.ConfigureAwait(false);
            var persistedUsage = await usageTask.ConfigureAwait(false);
            var previousClaude = persistedUsage.FirstOrDefault(item =>
                item.AgentId.Equals("claude", StringComparison.OrdinalIgnoreCase));
            if (previousClaude is null ||
                UsageChanged(previousClaude, claude) ||
                claude.UpdatedAt - previousClaude.UpdatedAt >= TimeSpan.FromSeconds(15))
            {
                await store.SaveUsageAsync(claude, cancellationToken).ConfigureAwait(false);
            }

            var usage = persistedUsage
                .Where(item => !item.AgentId.Equals("claude", StringComparison.OrdinalIgnoreCase))
                .Append(claude)
                .ToArray();
            var agents = await agentsTask.ConfigureAwait(false);
            var claims = await claimsTask.ConfigureAwait(false);
            var conflicts = new ClaimService(store).Detect(claims, agents, DateTimeOffset.UtcNow);
            var codex = usage.FirstOrDefault(item =>
                item.AgentId.Equals("codex", StringComparison.OrdinalIgnoreCase));
            var repository = string.IsNullOrWhiteSpace(config.ProjectName)
                ? new DirectoryInfo(paths.Root).Name
                : config.ProjectName;
            var branch = ReadBranch(start);
            var width = TerminalWidth();

            Console.WriteLine(Render(
                repository,
                branch,
                claude,
                codex,
                config,
                agents,
                conflicts,
                width,
                useColor));
            return 0;
        }
        catch (Exception exception) when (exception is not StackOverflowException &&
                                          exception is not OutOfMemoryException)
        {
            Console.WriteLine($"FKN | telemetry unavailable: {SingleLine(exception.Message, 80)}");
            return 0;
        }
    }

    private static string Render(
        string repository,
        string branch,
        UsageSnapshot claude,
        UsageSnapshot? codex,
        FknrtdConfig config,
        IReadOnlyList<AgentRuntimeState> agents,
        IReadOnlyList<ConflictRecord> conflicts,
        int width,
        bool useColor)
    {
        var risk = conflicts.FirstOrDefault();
        var riskAgents = risk is null ? string.Empty : string.Join("+", risk.AgentIds);
        var riskPaths = risk is null ? string.Empty : string.Join(",", risk.Paths.Take(2));
        var riskText = risk is null
            ? "√ SAFE"
            : risk.Kind == ConflictKind.Collision
                ? $"× COLLISION {riskAgents}: {riskPaths}"
                : $"∆ {risk.Kind.ToString().ToUpperInvariant()}: {riskPaths}";

        if (width < 80)
        {
            var fields = new List<string>
            {
                Paint("FKN", 86, 212, 221, useColor),
                Paint(SingleLine(repository, 14), 88, 166, 255, useColor),
                $"ctx{Percent(claude.ContextRemainingPercent)}",
                $"Cl{Percent(claude.FiveHourRemainingPercent)}/{Percent(claude.WeeklyRemainingPercent)}",
                $"Cx{Percent(codex?.FiveHourRemainingPercent)}/{Percent(codex?.WeeklyRemainingPercent)}"
            };
            if (risk is not null)
            {
                fields.Add(Paint(risk.Kind == ConflictKind.Collision ? "×COLLISION" : "∆RISK",
                    255, 123, 114, useColor, bold: true));
            }

            return string.Join(" | ", fields);
        }

        if (width < 100)
        {
            return string.Join(" | ",
                Paint("FKN", 86, 212, 221, useColor),
                Paint(repository, 88, 166, 255, useColor),
                $"ctx{Percent(claude.ContextRemainingPercent)}",
                $"Cl {Percent(claude.FiveHourRemainingPercent)}/{Percent(claude.WeeklyRemainingPercent)}",
                $"Cdx {Percent(codex?.FiveHourRemainingPercent)}/{Percent(codex?.WeeklyRemainingPercent)}",
                Paint(riskText, risk is null ? (byte)86 : (byte)255, risk is null ? (byte)211 : (byte)123, risk is null ? (byte)100 : (byte)114,
                    useColor));
        }

        var activity = config.Agents
            .Select(definition =>
            {
                var state = agents.FirstOrDefault(item =>
                    item.AgentId.Equals(definition.Id, StringComparison.OrdinalIgnoreCase));
                return $"{definition.DisplayName} {StateIcon(state?.State)} {SingleLine(state?.Intent ?? "idle", 32)}";
            })
            .Take(width >= 140 ? 4 : 2);
        var second = string.Join(" | ", activity.Append(Paint(
            riskText,
            risk is null ? (byte)86 : (byte)255,
            risk is null ? (byte)211 : (byte)123,
            risk is null ? (byte)100 : (byte)114,
            useColor,
            bold: risk is not null)));
        if (width < 140)
        {
            var medium = string.Join(" | ",
                Paint("FKN", 86, 212, 221, useColor, bold: true),
                Paint($"{repository} on {branch}", 88, 166, 255, useColor),
                $"ctx {Percent(claude.ContextRemainingPercent)}",
                Paint($"Cl {Percent(claude.FiveHourRemainingPercent)}/{Percent(claude.WeeklyRemainingPercent)}",
                    255, 166, 87, useColor),
                Paint($"Cdx {Percent(codex?.FiveHourRemainingPercent)}/{Percent(codex?.WeeklyRemainingPercent)}",
                    210, 168, 255, useColor));
            return medium + Environment.NewLine + second;
        }

        var first = string.Join(" | ",
            Paint("FKN", 86, 212, 221, useColor, bold: true),
            Paint($"{repository}  on {branch}", 88, 166, 255, useColor),
            $"CTX {Percent(claude.ContextRemainingPercent)} left",
            Paint($"Claude 5h {Percent(claude.FiveHourRemainingPercent)} 7d {Percent(claude.WeeklyRemainingPercent)}",
                255, 166, 87, useColor),
            Paint($"Codex 5h {Percent(codex?.FiveHourRemainingPercent)} 7d {Percent(codex?.WeeklyRemainingPercent)}",
                210, 168, 255, useColor));
        return first + Environment.NewLine + second;
    }

    private static string RenderUninitialized(UsageSnapshot claude, bool useColor) => string.Join(" | ",
        Paint("FKN", 86, 212, 221, useColor),
        $"CTX {Percent(claude.ContextRemainingPercent)} left",
        Paint($"Claude 5h {Percent(claude.FiveHourRemainingPercent)} 7d {Percent(claude.WeeklyRemainingPercent)}",
            255, 166, 87, useColor),
        "run fknrtd init");

    private static string ReadBranch(string root)
    {
        try
        {
            var current = new DirectoryInfo(Path.GetFullPath(root));
            while (current is not null &&
                   !File.Exists(Path.Combine(current.FullName, ".git")) &&
                   !Directory.Exists(Path.Combine(current.FullName, ".git")))
            {
                current = current.Parent;
            }

            if (current is null)
            {
                return "unknown";
            }

            var gitPath = Path.Combine(current.FullName, ".git");
            var gitDirectory = gitPath;
            if (File.Exists(gitPath))
            {
                var pointer = File.ReadAllText(gitPath).Trim();
                if (pointer.StartsWith("gitdir:", StringComparison.OrdinalIgnoreCase))
                {
                    var value = pointer[7..].Trim();
                    gitDirectory = Path.GetFullPath(value, current.FullName);
                }
            }

            var head = File.ReadAllText(Path.Combine(gitDirectory, "HEAD")).Trim();
            const string prefix = "ref: refs/heads/";
            return head.StartsWith(prefix, StringComparison.Ordinal)
                ? head[prefix.Length..]
                : head.Length >= 7 ? head[..7] : head;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return "unknown";
        }
    }

    private static string? ReadString(JsonElement root, params string[] path)
    {
        var current = root;
        foreach (var name in path)
        {
            if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(name, out current))
            {
                return null;
            }
        }

        return current.ValueKind == JsonValueKind.String ? current.GetString() : null;
    }

    private static int TerminalWidth()
    {
        if (int.TryParse(Environment.GetEnvironmentVariable("COLUMNS"), out var columns) && columns > 0)
        {
            return columns;
        }

        try
        {
            return Console.WindowWidth;
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or
                                          PlatformNotSupportedException)
        {
            return 140;
        }
    }

    private static string Percent(double? value) => value is null ? "N/A" : $"{value:0}%";

    private static bool UsageChanged(UsageSnapshot previous, UsageSnapshot current) =>
        !Same(previous.ContextRemainingPercent, current.ContextRemainingPercent) ||
        !Same(previous.FiveHourRemainingPercent, current.FiveHourRemainingPercent) ||
        !Same(previous.WeeklyRemainingPercent, current.WeeklyRemainingPercent) ||
        previous.FiveHourResetsAt != current.FiveHourResetsAt ||
        previous.WeeklyResetsAt != current.WeeklyResetsAt;

    private static bool Same(double? left, double? right) => left is null && right is null ||
                                                              left is not null && right is not null &&
                                                              Math.Abs(left.Value - right.Value) < 0.01;

    private static string StateIcon(AgentActivityState? state) => state switch
    {
        AgentActivityState.Planning => "◊",
        AgentActivityState.Running => "►",
        AgentActivityState.Reviewing => "♦",
        AgentActivityState.Waiting => "▌",
        AgentActivityState.Blocked => "■",
        AgentActivityState.Failed => "×",
        AgentActivityState.Completed => "√",
        AgentActivityState.Idle => "○",
        AgentActivityState.Offline => "○",
        _ => "?"
    };

    private static string SingleLine(string value, int maximum)
    {
        var clean = string.Join(' ', value.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries));
        return clean.Length <= maximum ? clean : clean[..Math.Max(1, maximum - 1)] + "…";
    }

    private static string Paint(
        string text,
        byte red,
        byte green,
        byte blue,
        bool enabled,
        bool bold = false)
    {
        if (!enabled)
        {
            return text;
        }

        var weight = bold ? "\u001b[1m" : string.Empty;
        return $"\u001b[0m\u001b[38;2;{red};{green};{blue}m{weight}{text}\u001b[0m";
    }
}
