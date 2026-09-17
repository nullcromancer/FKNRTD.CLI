using FKNRTD.Domain;
using FKNRTD.Services;

namespace FKNRTD.Dashboard;

/// <summary>What the operator asked to do to the agent roster.</summary>
internal enum AgentAction
{
    /// <summary>Flip the highlighted agent between enabled and disabled.</summary>
    Toggle,

    /// <summary>Open the agent builder.</summary>
    Add,

    /// <summary>Take the highlighted agent out of the configuration.</summary>
    Remove,

    /// <summary>Open the full per-agent reference: every profile, and where the executable is.</summary>
    Explain,

    /// <summary>Change which program the highlighted agent runs.</summary>
    Repoint
}

/// <summary>
/// The agent roster, and the three things you can do to it. A is where an operator goes to find out
/// why a name is missing from the task builder, so it is also where the answer has to be actionable:
/// before this, the only way to enable, disable or remove an agent was to leave the dashboard and
/// remember the command, or to hand-edit config.json.
/// </summary>
internal sealed class AgentManager : IOverlay
{
    private readonly IReadOnlyList<AgentDefinition> _agents;
    private readonly IReadOnlyDictionary<string, int> _usage;
    private readonly IReadOnlyDictionary<string, bool> _onPath;
    private int _selected;

    /// <param name="onPath">
    /// Which executables were found, keyed by name. Supplied only by the renderer's test seam: what
    /// is installed on the machine running the suite is not something a test can arrange, and the
    /// state a first-time operator meets - nothing installed at all - is exactly the one worth
    /// drawing.
    /// </param>
    public AgentManager(
        IReadOnlyList<AgentDefinition> agents,
        IReadOnlyDictionary<string, int> usage,
        IReadOnlyDictionary<string, bool>? onPath = null)
    {
        _agents = agents;
        _usage = usage;

        // Resolved once, here, rather than per agent per frame. Find walks PATH with a File.Exists
        // for every directory and every extension - on Windows that is well over a hundred probes -
        // and this panel asked it about every agent about eight times a frame for as long as it was
        // open. The roster is a picture of one moment either way; installing something while it is
        // open and reopening it is the same gesture as any other refresh.
        _onPath = onPath ?? agents
            .Select(agent => agent.Executable)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                executable => executable,
                executable => ExecutableLocator.Find(executable) is not null,
                StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Whether this agent's command was on PATH when the panel was opened.</summary>
    private bool OnPath(AgentDefinition agent) => _onPath.GetValueOrDefault(agent.Executable);

    public string Mode => "AGENTS";

    /// <summary>The action chosen, read by the dashboard after a submit.</summary>
    public AgentAction Action { get; private set; }

    /// <summary>The agent the action applies to. Empty for <see cref="AgentAction.Add"/>.</summary>
    public string AgentId { get; private set; } = string.Empty;

    /// <summary>
    /// Builds the roster for a workspace, counting how many tasks each agent is already named on so
    /// that removing one can say what it would orphan.
    /// </summary>
    public static AgentManager Create(DashboardSnapshot snapshot)
    {
        var usage = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var task in snapshot.Tasks)
        {
            var named = new[] { task.LeadAgentId, task.ImplementerAgentId, task.AuditorAgentId }
                .Distinct(StringComparer.OrdinalIgnoreCase);
            foreach (var id in named)
            {
                usage[id] = usage.GetValueOrDefault(id) + 1;
            }
        }

        return new AgentManager(snapshot.Config.Agents, usage);
    }

    private AgentDefinition? Selected => _agents.ElementAtOrDefault(_selected);

    public OverlayResult HandleKey(ConsoleKeyInfo key)
    {
        switch (key.Key)
        {
            case ConsoleKey.Escape:
            case ConsoleKey.Q:
                return OverlayResult.Cancel;
            case ConsoleKey.UpArrow:
                _selected = Math.Max(0, _selected - 1);
                return OverlayResult.Continue;
            case ConsoleKey.DownArrow:
                _selected = Math.Min(Math.Max(0, _agents.Count - 1), _selected + 1);
                return OverlayResult.Continue;
            case ConsoleKey.N:
                Action = AgentAction.Add;
                AgentId = string.Empty;
                return OverlayResult.Submit;
            case ConsoleKey.F1:
                Action = AgentAction.Explain;
                AgentId = Selected?.Id ?? string.Empty;
                return OverlayResult.Submit;
            case ConsoleKey.E:
                // The commonest problem this screen reports is an executable that is not on PATH,
                // and until now the only thing it could do about it was name a shell command.
                if (Selected is { } repointed)
                {
                    Action = AgentAction.Repoint;
                    AgentId = repointed.Id;
                    return OverlayResult.Submit;
                }

                return OverlayResult.Continue;
        }

        // Space and Enter both toggle. Enter is the habit the rest of the dashboard builds, and
        // space is what a checkbox list trains; disagreeing with either would only cost a keystroke.
        if (key.Key is ConsoleKey.Spacebar or ConsoleKey.Enter && Selected is { } toggled)
        {
            Action = AgentAction.Toggle;
            AgentId = toggled.Id;
            return OverlayResult.Submit;
        }

        // Delete only. Backspace used to do this too, and Backspace is the key people press to go
        // back - putting a destructive confirmation behind it was a trap with no upside.
        if (key.Key is ConsoleKey.Delete && Selected is { } removed)
        {
            Action = AgentAction.Remove;
            AgentId = removed.Id;
            return OverlayResult.Submit;
        }

        return OverlayResult.Continue;
    }

    public void Draw(Canvas canvas, Rect area)
    {
        canvas.Dim(area, Overlays.ScrimForeground, Overlays.ScrimBackground);
        _selected = Math.Clamp(_selected, 0, Math.Max(0, _agents.Count - 1));

        var panel = Overlays.Centre(area, PreferredWidth, Measure(area));
        canvas.DrawPanel(panel, "AGENTS", Theme.Violet, Theme.Surface);

        var x = panel.X + 3;
        var width = Math.Max(10, panel.Width - 6);
        var y = panel.Y + 1;
        // The footer draws its own rule one row above itself, so content has to stop above that.
        var lastRow = panel.Bottom - 4;

        if (_agents.Count == 0)
        {
            canvas.DrawWrapped(x, y, width, Math.Max(1, lastRow - y + 1), EmptyMessage,
                Theme.Foreground, background: Theme.Surface);
            Overlays.Footer(canvas, panel, Theme.Violet,
                ("N", "add an agent"), ("F1", "full detail"), ("Esc", "close"));
            return;
        }

        y = canvas.DrawWrapped(x, y, width, IntroRows, Intro, Theme.Muted, background: Theme.Surface);
        y++;

        var idWidth = Math.Min(16, _agents.Max(agent => Text.DisplayWidth(agent.Id)) + 1);
        var columns = Columns();
        for (var index = 0; index < _agents.Count && y <= lastRow; index++)
        {
            var agent = _agents[index];
            var selected = index == _selected;
            var fill = selected ? Theme.SurfaceRaised : Theme.Surface;
            canvas.Fill(new Rect(x, y, width, 1), fill);
            canvas.DrawText(x, y, selected ? "▸ " : "  ", Theme.Violet, bold: true, maxWidth: 2,
                background: fill);

            var onPath = OnPath(agent);
            canvas.DrawText(x + 2, y, agent.Enabled ? "√" : "·",
                agent.Enabled ? Theme.Green : Theme.Muted, bold: true, maxWidth: 1, background: fill);
            canvas.DrawText(x + 4, y, Text.Truncate(agent.Id, idWidth),
                agent.Enabled ? Theme.Foreground : Theme.Muted, bold: selected, maxWidth: idWidth,
                background: fill);

            var status = string.Join("  ", Facts(agent, onPath)
                .Select((fact, column) => fact.PadRight(columns.ElementAtOrDefault(column))));
            var statusX = x + 4 + idWidth + 1;
            var statusWidth = Math.Max(0, width - (statusX - x));
            canvas.DrawText(statusX, y, Text.Truncate(status, statusWidth),
                onPath ? Theme.Muted : Theme.Amber, maxWidth: statusWidth, background: fill);
            y++;
        }

        if (Selected is { } current && y + 2 <= lastRow)
        {
            canvas.DrawRule(panel.X + 1, y, Math.Max(0, panel.Width - 2),
                Theme.Violet.Blend(Theme.Surface, 0.7), Theme.Surface);
            y = Overlays.Explain(canvas, x, y + 1, width, lastRow - y, "WHAT THIS ONE DOES",
                Describe(current), Theme.Violet);
            if (y + 1 <= lastRow)
            {
                Overlays.Explain(canvas, x, y + 1, width, lastRow - y, "IF YOU TURN IT OFF",
                    Consequence(current), Theme.Violet);
            }
        }

        Overlays.Footer(canvas, panel, Theme.Violet,
            ("↑↓", "choose"), ("Space", Selected?.Enabled == true ? "disable" : "enable"),
            ("N", "add"), ("E", "change its command"), ("Del", "remove"), ("F1", "full detail"),
            ("Esc", "close"));
    }

    private const int PreferredWidth = 100;

    private const string Intro =
        "Every task names three agents: one to plan, one to implement, and one to judge the result. " +
        "Only enabled agents are offered.";

    private const int IntroRows = 2;

    private const string EmptyMessage =
        "No agents are configured, so there is nobody to give work to. Press N to describe one: " +
        "you will need the name of a command-line coding tool that is already installed, such as " +
        "claude or codex.";

    /// <summary>
    /// How tall the panel has to be for the highlighted agent's explanation to finish its sentences.
    /// A fixed guess clipped the consequence paragraph mid-word, which is the one paragraph on the
    /// screen whose whole job is to say what a keystroke would cost.
    /// </summary>
    private int Measure(Rect area)
    {
        var width = Math.Min(Math.Min(PreferredWidth, area.Width), Math.Max(20, area.Width - 4));
        var inner = Math.Max(10, width - 6);

        // The empty panel is one message and one key, and padding it out to the size of a populated
        // roster would only make it look like something had failed to load.
        if (_agents.Count == 0)
        {
            return Math.Clamp(Text.Wrap(EmptyMessage, inner).Count + 5, 6, Math.Max(6, area.Height - 2));
        }

        // Top border, the intro, a gap, the roster, a rule, the footer's own rule, the footer, and
        // the bottom border. Each explanation adds its caption and its wrapped body, and the second
        // one is preceded by a blank row.
        var height = 1 + Math.Min(IntroRows, Text.Wrap(Intro, inner).Count) + 1 + _agents.Count + 1 + 3;
        if (Selected is { } current)
        {
            height += 1 + Text.Wrap(Describe(current), inner).Count;
            height += 1 + 1 + Text.Wrap(Consequence(current), inner).Count;
        }

        return Math.Clamp(height, 12, Math.Max(12, area.Height - 2));
    }

    /// <summary>
    /// The width of each status column, so the facts line up down the list. Ragged columns are
    /// readable one row at a time and useless for the comparison the whole panel exists to support.
    /// </summary>
    private int[] Columns()
    {
        var widths = new int[4];
        foreach (var agent in _agents)
        {
            var facts = Facts(agent, OnPath(agent)).ToArray();
            for (var column = 0; column < facts.Length && column < widths.Length; column++)
            {
                widths[column] = Math.Max(widths[column], Text.DisplayWidth(facts[column]));
            }
        }

        return widths;
    }

    /// <summary>The short, scannable facts: whether it can be launched, and whether it can judge.</summary>
    private IEnumerable<string> Facts(AgentDefinition agent, bool onPath)
    {
        yield return agent.Enabled ? "enabled" : "disabled";
        yield return onPath ? agent.Executable + " found" : agent.Executable + " NOT on PATH";
        yield return TaskWizard.CanAudit(_agents, agent.Id) ? "can audit" : "cannot audit";
        var used = _usage.GetValueOrDefault(agent.Id);
        if (used > 0)
        {
            yield return used == 1 ? "1 task" : used + " tasks";
        }
    }

    /// <summary>The same facts as a sentence, because the row is a summary and this is the answer.</summary>
    private string Describe(AgentDefinition agent)
    {
        var parts = new List<string>
        {
            $"{agent.DisplayName} runs the command {agent.Executable}."
        };

        parts.Add(!OnPath(agent)
            ? $"That command is not on this machine's PATH, so any task that names {agent.Id} would " +
              "fail the moment it tried to launch it. Install it, or press E to point this agent at " +
              "a program that is there."
            : "It was found on PATH, so a task can launch it.");

        parts.Add(TaskWizard.CanAudit(_agents, agent.Id)
            ? "Its audit profile defines both a pass and a fail marker, so it can return a verdict " +
              "and the task builder offers it as an auditor."
            : "Its audit profile has no pass and fail markers, so it could never return a verdict. " +
              "The task builder will not offer it as an auditor.");

        var used = _usage.GetValueOrDefault(agent.Id);
        if (used > 0)
        {
            parts.Add(used == 1
                ? "One existing task already names it."
                : $"{used} existing tasks already name it.");
        }

        return string.Join(" ", parts);
    }

    /// <summary>
    /// What flipping the switch actually costs. Disabling looks destructive and is not; removing
    /// looks the same and is, so the difference has to be on screen before either key is pressed.
    /// </summary>
    private string Consequence(AgentDefinition agent)
    {
        if (!agent.Enabled)
        {
            return "Enabling it puts it back in the task builder's choices. It changes nothing that " +
                   "has already run.";
        }

        var used = _usage.GetValueOrDefault(agent.Id);
        var orphaned = used == 0
            ? string.Empty
            : $" {(used == 1 ? "The one task that names it" : $"The {used} tasks that name it")} " +
              "would keep the name and fail on the stage that needs it.";

        return "Disabling it takes it out of the task builder's choices. Nothing already recorded " +
               "changes, and turning it back on restores it." + orphaned +
               " Removing it with Del is the destructive one: it deletes the profile, the arguments " +
               "and the environment you configured for it.";
    }
}
