using System.Text.Json;
using FKNRTD.Domain;
using FKNRTD.Dashboard;
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
            // -root wins over the payload, and the payload over the ambient directory. Without the
            // first of those, a caller that passes -root is silently answered about wherever the
            // process happens to be standing — which reads as isolation and is not.
            var start = arguments.Get("root") ??
                        ReadString(document.RootElement, "workspace", "current_dir") ??
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

            // Claude Code consumes ANSI through a pipe; redirection does not disable colour here.
            var useColor = !arguments.Has("no-color") &&
                           string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NO_COLOR"));
            if (paths is null)
            {
                WriteFrame(RenderUninitialized(claude, useColor, TerminalWidth()));
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
            var git = await ReadGitAsync(start, config.Mode, cancellationToken).ConfigureAwait(false);
            repository = GitHubRepository(git.Remote) ?? repository;
            var quality = LatestQuality(await store.LoadTasksAsync(cancellationToken).ConfigureAwait(false));
            var width = TerminalWidth();

            WriteFrame(Render(
                repository,
                git.Branch,
                claude,
                codex,
                config,
                agents,
                conflicts,
                width,
                useColor, git, quality));
            return 0;
        }
        catch (System.Text.Json.JsonException)
        {
            // Claude Code pipes a JSON payload in on every prompt. When it is absent or malformed
            // there is nothing useful to say in a status bar, and the parser's own message - "The
            // input does not contain any JSON tokens. Expected the input to start with..." - is the
            // worst possible thing to put there: it is long, it is truncated mid-sentence, and it
            // describes a fault in something the reader did not run.
            WriteFrame(SingleLine("FKN | no status from Claude Code yet", TerminalWidth()));
            return 0;
        }
        catch (Exception exception) when (exception is not StackOverflowException &&
                                          exception is not OutOfMemoryException)
        {
            // Anything else is a workspace fault, and those messages were written to be read.
            WriteFrame(SingleLine($"FKN | {exception.Message}", TerminalWidth()));
            return 0;
        }
    }

    // The pipe protocol uses LF for both row separators and the final terminator,
    // independent of platform and colour mode.
    private static void WriteFrame(string frame) => Console.Write(frame + "\n");

    internal sealed record GitHealth(string Branch = "N/A", int? Modified = null,
        int? Ahead = null, int? Behind = null, string Remote = "");

    internal static QualitySnapshot? LatestQuality(IEnumerable<WorkflowTask> tasks) => tasks
        .Where(task => task.Quality.UpdatedAt is not null)
        .OrderByDescending(task => task.Quality.UpdatedAt)
        .ThenBy(task => task.Id, StringComparer.Ordinal)
        .Select(task => task.Quality).FirstOrDefault();

    internal static string Render(
        string repository, string branch, UsageSnapshot claude, UsageSnapshot? codex,
        FknrtdConfig config, IReadOnlyList<AgentRuntimeState> agents,
        IReadOnlyList<ConflictRecord> conflicts, int width, bool useColor,
        GitHealth? git = null, QualitySnapshot? quality = null)
    {
        width = Math.Max(0, width);
        var alerts = new List<string>();
        if (conflicts.Any(item => item.Kind == ConflictKind.Collision)) alerts.Add("×COLLISION");
        if (agents.Any(item => item.State == AgentActivityState.Blocked)) alerts.Add("■BLOCKED");
        if (quality is not null && (quality.Commands.Any(item => !item.Passed) ||
            new[] { quality.Build, quality.Tests, quality.Lint, quality.Types, quality.Security }
                .Contains(StageState.Failed))) alerts.Add("×VERIFY");
        var critical = alerts.Count > 0;
        var wide = width >= 140;
        var twoRow = width >= 100;

        // Every fit test measures plain text. ANSI never participates in display-column math,
        // which is what keeps a coloured frame exactly as wide as the colourless one.
        bool Fits(List<Segment> row, Segment candidate) =>
            Text.DisplayWidth(Compose(row.Append(candidate))) <= width;
        void Add(List<Segment> row, string text, Tone tone = Tone.Plain, string separator = " | ")
        {
            var candidate = new Segment(SingleLine(text, int.MaxValue), tone, separator);
            if (Fits(row, candidate)) row.Add(candidate);
        }

        // Marks are opt-in because two of the three fonts this product targets cannot draw them,
        // and a mark a font lacks is an empty box where a word used to be. They only ever precede
        // a label, never replace one, so the row still reads without a single glyph.
        var icons = config.StatuslineIcons;
        var first = new List<Segment> { new(icons ? "◉ FKN" : "FKN", Tone.Badge) };
        if (critical) first.Add(new(string.Join(" ", alerts), Tone.Alert));

        // The branch belongs to the repository, so it rides inside the same field. Behind a
        // separator it would read as a different subject rather than as where this one is.
        var located = SingleLine(repository, width < 80 ? 14 : wide ? 40 : 24);
        if (icons) located = "▣ " + located;
        if (twoRow && branch.Length > 0 && !branch.Equals("unknown", StringComparison.Ordinal))
        {
            located += " " + (icons ? " " : string.Empty) + SingleLine(branch, 20);
        }

        Add(first, located, Tone.Repository);

        // Context leads, because it is the figure most likely to change what to do next. The
        // meter shrinks before the row gives up any number: a bar without its percentage is
        // decoration, and a percentage without its bar still tells the reader everything.
        // On a narrow row a critical condition takes the space telemetry would have had. Context
        // is worth knowing; a collision is worth acting on, and only one of them fits.
        if (!critical || twoRow)
        {
            var meterSize = wide ? 5 : twoRow ? 3 : 0;
            string Context(int size) => (wide ? "CTX " : "ctx") +
                Meter(claude.ContextRemainingPercent, size) +
                (claude.ContextRemainingPercent is null ? string.Empty : " left");
            while (meterSize > 0 && !Fits(first, new Segment(Context(meterSize)))) meterSize--;
            Add(first, Context(meterSize));
        }

        // A glyph and a number, never a label. "mod:" and "ahead:" spend more columns than they
        // explain, and an unknown count is left out rather than shown as a zero that would read
        // as "in sync" when the truth is "no upstream to compare against".
        if (git is not null)
        {
            var state = new List<string>();
            if (git.Modified is { } modified) state.Add((icons ? "△" : "∆") + Number(modified));
            if (git.Ahead is { } ahead && git.Behind is { } behind)
            {
                state.Add("↑" + Number(ahead) + "↓" + Number(behind));
            }

            if (state.Count > 0) Add(first, string.Join(" ", state), Tone.Warn);
        }

        if (!twoRow)
        {
            Add(first, $"Cl {Meter(claude.FiveHourRemainingPercent, 0)}/{Meter(claude.WeeklyRemainingPercent, 0)}", Tone.Claude);
            Add(first, $"Cx {Meter(codex?.FiveHourRemainingPercent, 0)}/{Meter(codex?.WeeklyRemainingPercent, 0)}", Tone.Codex);
            return RenderFields(first, width, useColor);
        }

        var second = new List<Segment>();
        var usageSize = wide ? 0 : 0;
        Add(second, $"Claude 5h {Meter(claude.FiveHourRemainingPercent, usageSize)} 7d {Meter(claude.WeeklyRemainingPercent, usageSize)}", Tone.Claude);
        Add(second, $"Codex 5h {Meter(codex?.FiveHourRemainingPercent, usageSize)} 7d {Meter(codex?.WeeklyRemainingPercent, usageSize)}", Tone.Codex);

        // Agent entries are a group: a two-letter seat, its state glyph and what it is doing,
        // spaced rather than piped so the eye reads them as one list.
        var listed = 0;
        foreach (var definition in config.Agents.Take(wide ? 4 : 2))
        {
            var state = agents.FirstOrDefault(item =>
                item.AgentId.Equals(definition.Id, StringComparison.OrdinalIgnoreCase));
            var entry = Seat(definition.Id) + StateIcon(state?.State ?? AgentActivityState.Offline, icons) +
                        " " + SingleLine(state?.Intent ?? "offline", wide ? 24 : 16);
            var before = second.Count;
            Add(second, entry, SeatTone(definition.Id), listed == 0 ? " | " : " ");
            if (second.Count > before) listed++;
        }

        if (quality is not null)
        {
            // The persisted model has no measured-count flags. A stored zero is not evidence that
            // a runner parsed a count, so an unmeasured figure says so rather than claiming none.
            Add(second, $"tests:{Count(quality.TestsPassed)}/{Count(quality.TestsFailed)} " +
                        $"lint:{Count(quality.LintIssues)}");
        }

        // Nothing is said when nothing is wrong. A marker that reads SAFE whenever the workspace
        // is fine is telemetry that never changes what the reader should do next, and the row is
        // worth more to the figure it would have crowded out.
        if (!critical && conflicts.Count > 0) Add(second, "∆RISK", Tone.Alert);

        return RenderFields(first, width, useColor) + "\n" + RenderFields(second, width, useColor);
    }

    private enum Tone { Plain, Badge, Repository, Claude, Codex, Alert, Safe, Warn }

    /// <summary>
    /// A field and the separator that precedes it. Agent entries sit a space apart so the eye reads
    /// them as one list, while everything else is divided by a pipe.
    /// </summary>
    private sealed record Segment(string Text, Tone Tone = Tone.Plain, string Separator = " | ");

    private static string Compose(IEnumerable<Segment> fields) => string.Concat(
        fields.Select((field, index) => (index == 0 ? string.Empty : field.Separator) + field.Text));

    private static string RenderFields(IReadOnlyList<Segment> fields, int width, bool useColor)
    {
        // Fit and truncate plain text first. ANSI never participates in display-column math, which
        // is what keeps a coloured row exactly as wide as the colourless one.
        var plain = SingleLine(Compose(fields), width);
        if (!useColor) return plain;
        var output = new System.Text.StringBuilder();
        var offset = 0;
        foreach (var field in fields)
        {
            if (offset >= plain.Length) break;
            if (offset > 0)
            {
                var separatorLength = Math.Min(field.Separator.Length, plain.Length - offset);
                output.Append(plain.AsSpan(offset, separatorLength));
                offset += separatorLength;
            }

            var length = Math.Min(field.Text.Length, plain.Length - offset);
            var text = plain.Substring(offset, length);
            output.Append(field.Tone switch
            {
                Tone.Badge => Paint(text, 86, 212, 221, true, bold: true),
                Tone.Repository => Paint(text, 88, 166, 255, true),
                Tone.Claude => Paint(text, 255, 166, 87, true),
                Tone.Codex => Paint(text, 210, 168, 255, true),
                Tone.Alert => Paint(text, 255, 123, 114, true, bold: true),
                Tone.Safe => Paint(text, 86, 211, 100, true),
                Tone.Warn => Paint(text, 255, 196, 87, true),
                _ => text
            });
            offset += length;
        }

        return output.ToString();
    }

    /// <summary>Each seat keeps the colour it carries everywhere else, so a glance finds its entry.</summary>
    private static Tone SeatTone(string agentId) =>
        agentId.Equals("codex", StringComparison.OrdinalIgnoreCase) ? Tone.Codex : Tone.Claude;

    /// <summary>Two letters for an agent, so a row can name four of them and still say what each is doing.</summary>
    private static string Seat(string agentId) => agentId.ToLowerInvariant() switch
    {
        "claude" => "Cl",
        "codex" => "Cx",
        "cline" => "Cn",
        "copilot" => "Cp",
        _ => agentId.Length >= 2
            ? char.ToUpperInvariant(agentId[0]) + agentId[1..2].ToLowerInvariant()
            : agentId.ToUpperInvariant()
    };

    private static string Number(int? value) => value?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "N/A";
    private static string Count(int value) => value > 0 ? Number(value) : "N/A";

    internal static string Meter(double? value, int size)
    {
        if (value is null || !double.IsFinite(value.Value)) return "N/A";
        var percent = Math.Clamp(value.Value, 0, 100);
        var filled = (int)Math.Round(size * percent / 100, MidpointRounding.AwayFromZero);
        return size <= 0 ? Percent(percent) : new string('█', filled) + new string('░', size - filled) + " " + Percent(percent);
    }

    internal static async Task<GitHealth> ReadGitAsync(string root, WorkspaceMode mode, CancellationToken cancellationToken = default)
    {
        if (mode != WorkspaceMode.Git) return new();
        var runner = new ProcessRunner();
        Task<CommandResult> Run(params string[] args) => runner.RunAsync("git", args, root,
            cancellationToken: cancellationToken, timeout: TimeSpan.FromSeconds(1));
        var statusTask = Run("--no-optional-locks", "status", "--porcelain=v2", "--branch", "-z", "-uall");
        var remoteTask = Run("config", "--get", "remote.origin.url");
        await Task.WhenAll(statusTask, remoteTask).ConfigureAwait(false);
        var status = await statusTask.ConfigureAwait(false);
        var remote = await remoteTask.ConfigureAwait(false);
        return ParseGit(status.Success ? status.StandardOutput : null,
            remote.Success ? remote.StandardOutput.Trim() : "");
    }

    internal static GitHealth ParseGit(string? status, string remote)
    {
        if (status is null) return new(Remote: remote);
        var branch = "N/A";
        int? ahead = null, behind = null;
        var modified = 0;
        var records = status.Split('\0', StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < records.Length; i++)
        {
            var record = records[i];
            if (record.StartsWith("# branch.head ", StringComparison.Ordinal)) branch = record[14..];
            else if (record.StartsWith("# branch.ab ", StringComparison.Ordinal))
            {
                var parts = record[12..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 2 && int.TryParse(parts[0], out var a) && int.TryParse(parts[1], out var b))
                { ahead = a; behind = Math.Abs(b); }
            }
            else if (record.StartsWith("1 ", StringComparison.Ordinal) || record.StartsWith("2 ", StringComparison.Ordinal) ||
                     record.StartsWith("u ", StringComparison.Ordinal) || record.StartsWith("? ", StringComparison.Ordinal))
            {
                modified++;
                if (record.StartsWith("2 ", StringComparison.Ordinal)) i++; // Rename source is a second record.
            }
        }
        return new(branch, modified, ahead, behind, remote);
    }

    internal static string? GitHubRepository(string remote)
    {
        string path;
        if (remote.StartsWith("git@github.com:", StringComparison.OrdinalIgnoreCase)) path = remote[15..];
        else if (Uri.TryCreate(remote, UriKind.Absolute, out var uri) &&
                 uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) &&
                 uri.Scheme is "https" or "http" or "ssh") path = uri.AbsolutePath.Trim('/');
        else return null;
        path = path.TrimEnd('/');
        if (path.EndsWith(".git", StringComparison.OrdinalIgnoreCase)) path = path[..^4];
        var parts = path.Split('/');
        return parts.Length == 2 && parts.All(part => part.Length > 0) ? path : null;
    }

    private static string RenderUninitialized(UsageSnapshot claude, bool useColor, int width) =>
        RenderFields([
            new("FKN", Tone.Badge),
            new($"CTX {Meter(claude.ContextRemainingPercent, width >= 100 ? 5 : 0)} left"),
            new($"Claude 5h {Meter(claude.FiveHourRemainingPercent, 0)} 7d {Meter(claude.WeeklyRemainingPercent, 0)}", Tone.Claude),
            new("run fknrtd init")
        ], width, useColor);

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
            return Console.WindowWidth > 0 ? Console.WindowWidth : 140;
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

    /// <summary>
    /// The running mark is the one that differs between the two sets. Every font this product
    /// targets draws the pointer; only Cascadia Mono draws the rounder triangle, so it is reached
    /// solely through the opt-in.
    /// </summary>
    private static string StateIcon(AgentActivityState? state, bool icons = false) => state switch
    {
        AgentActivityState.Planning => "◊",
        AgentActivityState.Running => icons ? "▶" : "►",
        AgentActivityState.Reviewing => "♦",
        AgentActivityState.Waiting => "▌",
        AgentActivityState.Blocked => "■",
        AgentActivityState.Failed => "×",
        AgentActivityState.Completed => "√",
        AgentActivityState.Idle => "○",
        AgentActivityState.Offline => "○",
        // Unknown is what a stale report decays to, so it reads as offline rather than as a glyph
        // nothing in the reference explains.
        _ => "○"
    };

    private static string SingleLine(string value, int maximum)
    {
        var clean = new string(value.Select(character => char.IsControl(character) ? ' ' : character).ToArray());
        return Text.Truncate(clean, maximum);
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
