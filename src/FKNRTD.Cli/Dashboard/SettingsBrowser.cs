using FKNRTD.Domain;
using FKNRTD.Help;

namespace FKNRTD.Dashboard;

/// <summary>
/// One configuration field the dashboard can change, and everything needed to change it safely:
/// how to read it, how to write it back, and what would be a bad value.
/// </summary>
/// <remarks>
/// The explanation of each field lives in <see cref="SettingsCatalog"/> and is not duplicated here.
/// This table is only the behaviour, keyed by the same JSON name, and a self-test fails if the two
/// ever stop lining up.
/// </remarks>
internal sealed record EditableSetting(
    string Key,
    WizardInput Input,
    Func<FknrtdConfig, string> Read,
    Func<FknrtdConfig, string, FknrtdConfig> Write,
    Func<string, string?>? Validate = null,
    string Placeholder = "")
{
    /// <summary>Whether pressing one key can flip it, rather than opening an editor for it.</summary>
    public bool IsToggle => Input == WizardInput.Choice;
}

/// <summary>
/// The settings screen behind S. It was a searchable explanation of a file the operator then had to
/// go and edit by hand; the explanation was the missing half, but so was the edit. Every field here
/// arrives with its current value, what it controls, and what changing it would cost.
/// </summary>
internal sealed class SettingsBrowser : IOverlay
{
    /// <summary>
    /// Why Git could never accept this as a branch name, or null when it could.
    /// </summary>
    /// <remarks>
    /// Whether the branch <em>exists</em> is deliberately not asked here: the answer changes, and
    /// the settings catalog already says that "a name that does not resolve fails the task at
    /// creation rather than silently picking another". What this rejects is a string no repository
    /// could ever have - a name with a space in it was accepted and written to the config file,
    /// and every Git-mode task created afterwards failed at creation, one at a time, each blaming
    /// the task rather than the setting that broke them all.
    /// <para>
    /// Empty is allowed. A standalone workspace has no branch to start from, which is why the
    /// field's own entry says it is empty there.
    /// </para>
    /// </remarks>
    internal static string? BranchNameProblem(string value)
    {
        var name = value.Trim();
        if (name.Length == 0)
        {
            return null;
        }

        foreach (var character in name)
        {
            if (char.IsWhiteSpace(character) || char.IsControl(character))
            {
                return "A branch name cannot contain spaces or control characters. Git would " +
                       "refuse it, so every task starting from it would fail at creation.";
            }

            if (character is '~' or '^' or ':' or '?' or '*' or '[' or '\\')
            {
                return $"A branch name cannot contain '{character}'. Git reserves it, so every " +
                       "task starting from this branch would fail at creation.";
            }
        }

        if (name.Contains("..", StringComparison.Ordinal) ||
            name.Contains("@{", StringComparison.Ordinal))
        {
            return "A branch name cannot contain '..' or '@{'. Git reads both as range syntax.";
        }

        if (name.StartsWith('/') || name.EndsWith('/') ||
            name.StartsWith('.') || name.EndsWith('.') ||
            name.EndsWith(".lock", StringComparison.OrdinalIgnoreCase))
        {
            return "A branch name cannot begin or end with '/' or '.', or end with '.lock'.";
        }

        return null;
    }

    /// <summary>
    /// The fields this screen can change. Anything in <see cref="SettingsCatalog"/> and not here is
    /// shown with the reason it is not editable, rather than silently omitted — a setting missing
    /// from the list reads as a setting that does not exist.
    /// </summary>
    public static readonly IReadOnlyList<EditableSetting> Editable =
    [
        new("projectName", WizardInput.Text,
            config => config.ProjectName,
            (config, value) => config with { ProjectName = value.Trim() },
            value => value.Trim().Length == 0 ? "A workspace needs a name. It is what the header shows." : null,
            "aurora-api"),

        new("defaultBaseRef", WizardInput.Text,
            config => config.DefaultBaseRef,
            (config, value) => config with { DefaultBaseRef = value.Trim() },
            BranchNameProblem,
            "main"),

        new("defaultVerificationCommands", WizardInput.Commands,
            config => string.Join('\n', config.DefaultVerificationCommands),
            (config, value) => config with
            {
                DefaultVerificationCommands = value
                    .Split('\n')
                    .Select(line => line.Trim())
                    .Where(line => line.Length > 0)
                    .ToList()
            },
            null,
            "dotnet build          (leave empty to skip verification entirely)"),

        new("defaultMaxRepairRounds", WizardInput.Number,
            config => config.DefaultMaxRepairRounds.ToString(),
            (config, value) => config with { DefaultMaxRepairRounds = int.Parse(value) },
            Whole(0, 5, "One is the usual answer.")),

        new("maxParallelAgents", WizardInput.Number,
            config => config.MaxParallelAgents.ToString(),
            (config, value) => config with { MaxParallelAgents = int.Parse(value) },
            Whole(1, 16, "Each one is a real process competing for the same machine.")),

        new("agentTimeoutSeconds", WizardInput.Number,
            config => config.AgentTimeoutSeconds.ToString(),
            (config, value) => config with { AgentTimeoutSeconds = int.Parse(value) },
            Whole(30, 86400, "3600 is one hour, which is the default.")),

        new("verificationTimeoutSeconds", WizardInput.Number,
            config => config.VerificationTimeoutSeconds.ToString(),
            (config, value) => config with { VerificationTimeoutSeconds = int.Parse(value) },
            Whole(10, 86400, "It has to outlast your slowest test run.")),

        new("agentStaleAfterSeconds", WizardInput.Number,
            config => config.AgentStaleAfterSeconds.ToString(),
            (config, value) => config with { AgentStaleAfterSeconds = int.Parse(value) },
            Whole(10, 3600, "Below about thirty seconds a working agent starts being called stale.")),

        new("claimStaleAfterSeconds", WizardInput.Number,
            config => config.ClaimStaleAfterSeconds.ToString(),
            (config, value) => config with { ClaimStaleAfterSeconds = int.Parse(value) },
            Whole(10, 86400, "It should outlast the longest stage that holds a claim.")),

        new("dashboardRefreshMilliseconds", WizardInput.Number,
            config => config.DashboardRefreshMilliseconds.ToString(),
            (config, value) => config with { DashboardRefreshMilliseconds = int.Parse(value) },
            Whole(100, 60000, "1000 is once a second, which is the default.")),

        new("requireCleanTreeForLanding", WizardInput.Choice,
            config => config.RequireCleanTreeForLanding ? "true" : "false",
            (config, value) => config with { RequireCleanTreeForLanding = value == "true" }),

        new("autoCommitAgentChanges", WizardInput.Choice,
            config => config.AutoCommitAgentChanges ? "true" : "false",
            (config, value) => config with { AutoCommitAgentChanges = value == "true" }),

        new("statuslineIcons", WizardInput.Choice,
            config => config.StatuslineIcons ? "true" : "false",
            (config, value) => config with { StatuslineIcons = value == "true" })
    ];

    /// <summary>
    /// Settings this screen deliberately will not change, each with the reason. Silence would be
    /// indistinguishable from an oversight.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> ReadOnly =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["schemaVersion"] = "The format version. It is set by the tool that wrote the file.",
            ["mode"] = "Whether this workspace uses Git worktrees. Changing it after tasks exist " +
                       "would leave them referring to worktrees and branches that the other mode " +
                       "does not use. Initialize a new workspace instead.",
            ["agents"] = "The agent roster. Press Esc and then A, which can also enable, disable " +
                         "and remove them."
        };

    private readonly FknrtdConfig _config;
    private readonly string _configPath;
    private int _selected;

    public SettingsBrowser(FknrtdConfig config, string configPath)
    {
        _config = config;
        _configPath = configPath;
    }

    public string Mode => "SETTINGS";

    /// <summary>The setting the operator asked to change, read by the dashboard after a submit.</summary>
    public string ChosenKey { get; private set; } = string.Empty;

    /// <summary>
    /// True when the operator asked for the full reference rather than an edit — every field
    /// including the nested agent ones, which this screen does not list.
    /// </summary>
    public bool WantsReference { get; private set; }

    /// <summary>Every top-level setting in catalog order, editable or not.</summary>
    private static IReadOnlyList<SettingEntry> Rows { get; } = SettingsCatalog.All
        .Where(entry => !entry.Key.Contains('[') && !entry.Key.Contains('.'))
        .ToArray();

    private SettingEntry? Selected => Rows.ElementAtOrDefault(_selected);

    /// <summary>The behaviour for a key, when this screen has any.</summary>
    public static EditableSetting? Editor(string key) =>
        Editable.FirstOrDefault(setting => setting.Key == key);

    public OverlayResult HandleKey(ConsoleKeyInfo key)
    {
        switch (key.Key)
        {
            case ConsoleKey.Escape:
                return OverlayResult.Cancel;
            case ConsoleKey.UpArrow:
                _selected = Math.Max(0, _selected - 1);
                return OverlayResult.Continue;
            case ConsoleKey.DownArrow:
                _selected = Math.Min(Math.Max(0, Rows.Count - 1), _selected + 1);
                return OverlayResult.Continue;
            case ConsoleKey.F1:
                WantsReference = true;
                ChosenKey = string.Empty;
                return OverlayResult.Submit;
        }

        if (key.Key is ConsoleKey.Enter or ConsoleKey.Spacebar && Selected is { } chosen)
        {
            ChosenKey = chosen.Key;
            WantsReference = false;
            return OverlayResult.Submit;
        }

        return OverlayResult.Continue;
    }

    public void Draw(Canvas canvas, Rect area)
    {
        canvas.Dim(area, Overlays.ScrimForeground, Overlays.ScrimBackground);
        _selected = Math.Clamp(_selected, 0, Math.Max(0, Rows.Count - 1));

        var panel = Overlays.Centre(area, PreferredWidth, Measure(area));
        canvas.DrawPanel(panel, "SETTINGS", Theme.Blue, Theme.Surface);

        var x = panel.X + 3;
        var width = Math.Max(10, panel.Width - 6);
        var y = panel.Y + 1;
        var lastRow = panel.Bottom - 4;

        y = canvas.DrawWrapped(x, y, width, IntroRows, Intro(_configPath), Theme.Muted,
            background: Theme.Surface);
        y++;

        // The list scrolls rather than being clipped, so a short terminal still reaches the last
        // setting instead of hiding it behind nothing. What the explanation needs is reserved
        // first, because a row of the list is replaceable and a truncated consequence is not.
        var reserved = Selected is null ? 0 : ExplanationRows(Math.Max(10, width));
        var visible = Math.Max(1, Math.Min(Rows.Count, lastRow - y + 1 - reserved));
        var first = Math.Clamp(_selected - visible / 2, 0, Math.Max(0, Rows.Count - visible));
        var keyWidth = Math.Min(32, Rows.Max(entry => Text.DisplayWidth(entry.Key)) + 1);

        for (var index = first; index < first + visible && index < Rows.Count; index++)
        {
            var entry = Rows[index];
            var selected = index == _selected;
            var fill = selected ? Theme.SurfaceRaised : Theme.Surface;
            canvas.Fill(new Rect(x, y, width, 1), fill);
            canvas.DrawText(x, y, selected ? "▸ " : "  ", Theme.Blue, bold: true, maxWidth: 2, background: fill);

            var locked = ReadOnly.ContainsKey(entry.Key);
            canvas.DrawText(x + 2, y, Text.Truncate(entry.Key, keyWidth),
                locked ? Theme.Muted : Theme.Foreground, bold: selected, maxWidth: keyWidth,
                background: fill);

            var valueX = x + 2 + keyWidth + 1;
            var valueWidth = Math.Max(0, width - (valueX - x));
            canvas.DrawText(valueX, y, Text.Truncate(Value(entry.Key), valueWidth),
                locked ? Theme.Muted : Theme.Cyan, maxWidth: valueWidth, background: fill);
            y++;
        }

        if (Rows.Count > visible)
        {
            var position = $"{_selected + 1} of {Rows.Count}";
            var positionWidth = Text.DisplayWidth(position);
            canvas.DrawText(panel.Right - 3 - positionWidth, panel.Y, position, Theme.Muted,
                maxWidth: positionWidth, background: Theme.Surface);
        }

        if (Selected is { } current && y + 2 <= lastRow)
        {
            canvas.DrawRule(panel.X + 1, y, Math.Max(0, panel.Width - 2),
                Theme.Blue.Blend(Theme.Surface, 0.7), Theme.Surface);
            y = Overlays.Explain(canvas, x, y + 1, width, lastRow - y + 1,
                current.Title.ToUpperInvariant(), current.Detail, Theme.Blue);

            // Explain draws nothing at all with one row to work in, so asking it for one row would
            // silently lose the consequence rather than shorten it.
            if (lastRow - y >= 2)
            {
                Overlays.Explain(canvas, x, y + 1, width, lastRow - y,
                    ReadOnly.ContainsKey(current.Key) ? "WHY YOU CANNOT CHANGE IT HERE" : "IF YOU CHANGE IT",
                    Consequence(current), Theme.Blue);
            }
        }

        var editable = Selected is { } row && !ReadOnly.ContainsKey(row.Key);
        var verb = editable && Editor(Selected!.Key) is { IsToggle: true } ? "flip it" : "change it";
        Overlays.Footer(canvas, panel, Theme.Blue,
            ("↑↓", "choose"), ("Enter", editable ? verb : "read why not"),
            ("F1", "every field"), ("Esc", "close"));
    }

    private const int PreferredWidth = 104;

    private const int IntroRows = 2;

    private static string Intro(string configPath) =>
        "This workspace's configuration, as it stands right now. It is plain JSON in " + configPath +
        "; everything here writes to that file.";

    /// <summary>Why the highlighted setting is worth care, or why this screen will not touch it.</summary>
    private static string Consequence(SettingEntry entry) =>
        ReadOnly.TryGetValue(entry.Key, out var reason) ? reason : entry.IfYouChangeIt;

    /// <summary>The rows the two explanation blocks need, including the rule and the blank between.</summary>
    private int ExplanationRows(int width)
    {
        if (Selected is not { } current)
        {
            return 0;
        }

        var inner = Math.Max(10, width);
        return 1 + 1 + Text.Wrap(current.Detail, inner).Count +
               1 + 1 + Text.Wrap(Consequence(current), inner).Count;
    }

    /// <summary>
    /// How tall the panel has to be to show the list and finish both explanations. Guessing at it
    /// dropped the second block entirely on the settings this screen refuses to change - which is
    /// the one case where the block is the whole answer.
    /// </summary>
    private int Measure(Rect area)
    {
        var width = Math.Min(Math.Min(PreferredWidth, area.Width), Math.Max(20, area.Width - 4));
        var inner = Math.Max(10, width - 6);
        var height = 1 + Math.Min(IntroRows, Text.Wrap(Intro(_configPath), inner).Count) + 1 +
                     Rows.Count + ExplanationRows(inner) + 3;
        return Math.Clamp(height, 14, Math.Max(14, area.Height - 2));
    }

    /// <summary>The live value, formatted the way the JSON file holds it.</summary>
    private string Value(string key)
    {
        if (Editor(key) is { } setting)
        {
            var raw = setting.Read(_config);
            return setting.Input == WizardInput.Commands
                ? raw.Length == 0 ? "(nothing verifies work by default)" : raw.Replace('\n', ' ')
                : raw.Length == 0 ? "(empty)" : raw;
        }

        return key switch
        {
            "schemaVersion" => _config.SchemaVersion.ToString(),
            "mode" => _config.Mode == WorkspaceMode.Git ? "git" : "standalone",
            "agents" => _config.Agents.Count == 0
                ? "none configured"
                : string.Join(", ", _config.Agents.Select(agent =>
                    agent.Enabled ? agent.Id : agent.Id + " (disabled)")),
            _ => string.Empty
        };
    }

    /// <summary>
    /// Builds the one-question form for a setting, so an edit is asked the same way everything else
    /// in the product is asked: with its own explanation and its own worked consequence beside it.
    /// </summary>
    public static Wizard? Form(FknrtdConfig config, string key)
    {
        if (Editor(key) is not { } setting || SettingsCatalog.Find(key) is not { } entry)
        {
            return null;
        }

        var current = setting.Read(config);

        // A boolean is a two-option choice rather than the word "true", because what the two values
        // mean is exactly what an operator reaching this screen does not know.
        var (whenTrue, whenFalse) = Meanings(key);
        IReadOnlyList<WizardOption> options = setting.IsToggle
            ?
            [
                new WizardOption("true", "Yes", whenTrue, Recommended: current == "true"),
                new WizardOption("false", "No", whenFalse, Recommended: current == "false")
            ]
            : [];

        var validate = setting.Validate ?? (_ => null);
        var step = new WizardStep
        {
            Key = key,
            Question = entry.Title + "?",
            GlossaryTerm = entry.GlossaryTerm ?? "config",
            Input = setting.Input,
            Default = _ => current,
            Placeholder = setting.Placeholder,
            Validate = (value, _) => validate(value),
            Options = _ => options,
            Explanation = entry.Detail,
            Example = entry.IfYouChangeIt,
            ExampleCaption = "IF YOU CHANGE IT"
        };

        return new Wizard(entry.Title.ToUpperInvariant(), Theme.Blue, [step], finishVerb: "save");
    }

    /// <summary>What each side of a boolean actually does, in the operator's terms.</summary>
    private static (string WhenTrue, string WhenFalse) Meanings(string key) => key switch
    {
        "requireCleanTreeForLanding" => (
            "Refuse to land a task while its worktree has uncommitted changes",
            "Land anyway, leaving whatever was uncommitted behind in the worktree"),
        "autoCommitAgentChanges" => (
            "Commit what an agent wrote at the end of its stage, so the work survives",
            "Leave the changes uncommitted for you to handle yourself"),
        "statuslineIcons" => (
            "Draw the badge, project and branch marks — needs a font that has them",
            "Name the badge, project and branch in plain text, which every font can draw"),
        _ => ("On", "Off")
    };

    /// <summary>A whole-number validator that says the range and the usual answer, not just "invalid".</summary>
    /// <summary>
    /// Every setting in a configuration whose current value its own editor would refuse, as the
    /// JSON key and the reason.
    /// </summary>
    /// <remarks>
    /// The file is plain JSON and meant to be edited by hand, which walks straight past the
    /// checking the settings screen does. A config with dashboardRefreshMilliseconds of 0 and
    /// maxParallelAgents of 0 — a workspace where nothing can ever run — opened the dashboard
    /// without comment and was called healthy by doctor, because the only checking outside this
    /// screen was a separate pair of rules in `config validate` that knew about two fields.
    /// Asking each setting about the value it already holds means there is one answer to what is
    /// valid rather than three, and adding a setting cannot leave the check behind.
    /// </remarks>
    public static IReadOnlyList<(string Key, string Value, string Problem)> Problems(FknrtdConfig config)
    {
        var found = new List<(string, string, string)>();

        // Asked of the list itself rather than through a validator, because the validator is handed
        // the commands already joined with newlines and cannot tell one command holding a line
        // break from two commands. That is the same blindness that makes this worth reporting: the
        // settings screen reads the list as one line per command, so opening it and saving would
        // quietly turn such a command into two, and these are trusted shell commands.
        foreach (var command in config.DefaultVerificationCommands)
        {
            if (command.Contains('\n') || command.Contains('\r'))
            {
                found.Add((
                    "defaultVerificationCommands",
                    command.Replace('\n', ' ').Replace('\r', ' '),
                    "A verification command contains a line break. The settings screen reads one " +
                    "command per line, so editing settings would split this into two commands and " +
                    "run both. Put it on one line in .fknrtd/config.json, or move it into a script " +
                    "and call that."));
                break;
            }
        }

        foreach (var setting in Editable)
        {
            if (setting.Validate is not { } validate)
            {
                continue;
            }

            // The value is carried out with the complaint. The rule alone says what a good value
            // would be and leaves the reader to go and look up what the bad one is, which is one
            // more trip to the file than the answer needs.
            var value = setting.Read(config);
            if (validate(value) is { } problem)
            {
                found.Add((setting.Key, value, problem));
            }
        }

        return found;
    }

    private static Func<string, string?> Whole(int minimum, int maximum, string advice) =>
        value => int.TryParse(value, out var parsed) && parsed >= minimum && parsed <= maximum
            ? null
            : $"Enter a whole number between {minimum} and {maximum}. {advice}";
}
