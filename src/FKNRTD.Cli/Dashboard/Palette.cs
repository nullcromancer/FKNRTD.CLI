using FKNRTD.Domain;
using FKNRTD.Help;

namespace FKNRTD.Dashboard;

/// <summary>
/// One thing the dashboard can do right now, or one thing it cannot and why.
/// </summary>
/// <param name="Id">Matches the <see cref="KeyBinding.Key"/> the action is also bound to.</param>
/// <param name="Title">What it does, in the imperative.</param>
/// <param name="Detail">The fuller explanation, from the keymap.</param>
/// <param name="Unavailable">
/// Why it cannot be done at this moment, or null when it can. A greyed action that explains itself
/// teaches the pipeline; one that simply does nothing when pressed teaches only frustration.
/// </param>
internal sealed record PaletteAction(string Id, string Title, string Detail, string? Unavailable = null);

/// <summary>
/// The command palette behind <c>/</c>. Keyboard shortcuts only help someone who already knows
/// them, so every action is also reachable by typing part of its name — and the ones that are not
/// currently possible are listed anyway, with the reason, rather than hidden.
/// </summary>
internal sealed class Palette : IOverlay
{
    private readonly Func<IReadOnlyList<PaletteAction>> _source;
    private readonly TextField _filter = new();
    private int _selected;

    /// <summary>
    /// The actions are supplied as a function rather than a list because availability depends on
    /// live state. A palette left open while a task finishes must stop saying the task cannot be
    /// landed, and must then actually land it.
    /// </summary>
    public Palette(Func<IReadOnlyList<PaletteAction>> source)
    {
        _source = source;
        // Start on something that can actually be done, so the first Enter is never a no-op.
        _selected = Math.Max(0, source().ToList().FindIndex(action => action.Unavailable is null));
    }

    public string Mode => "COMMANDS";

    /// <summary>Every action offered right now, available or not.</summary>
    public IReadOnlyList<PaletteAction> Actions => _source();

    /// <summary>The action the operator chose. Read by the dashboard after a submit.</summary>
    public PaletteAction? Chosen { get; private set; }

    private IReadOnlyList<PaletteAction> Matching
    {
        get
        {
            var actions = _source();
            var needle = _filter.Value.Trim();
            if (needle.Length == 0)
            {
                return actions;
            }

            return actions
                .Where(action => action.Title.Contains(needle, StringComparison.OrdinalIgnoreCase) ||
                                 action.Detail.Contains(needle, StringComparison.OrdinalIgnoreCase) ||
                                 action.Id.Equals(needle, StringComparison.OrdinalIgnoreCase))
                .ToArray();
        }
    }

    public OverlayResult HandleKey(ConsoleKeyInfo key)
    {
        var matching = Matching;
        switch (key.Key)
        {
            case ConsoleKey.Escape:
                return OverlayResult.Cancel;
            case ConsoleKey.UpArrow:
                _selected = Math.Max(0, _selected - 1);
                return OverlayResult.Continue;
            case ConsoleKey.DownArrow:
                _selected = Math.Min(Math.Max(0, matching.Count - 1), _selected + 1);
                return OverlayResult.Continue;
            case ConsoleKey.Enter:
            {
                var action = matching.ElementAtOrDefault(_selected);
                if (action is null || action.Unavailable is not null)
                {
                    // Refusing to run an impossible action keeps the stated reason on screen.
                    return OverlayResult.Continue;
                }

                Chosen = action;
                return OverlayResult.Submit;
            }
        }

        // Only a change to the filter text resets the highlight. Moving the caret within the filter
        // is also consumed by the field, and resetting on that loses a selection the operator made
        // deliberately with the arrow keys.
        var before = _filter.Value;
        if (_filter.HandleKey(key) && !string.Equals(_filter.Value, before, StringComparison.Ordinal))
        {
            _selected = 0;
        }

        return OverlayResult.Continue;
    }

    public void Draw(Canvas canvas, Rect area)
    {
        canvas.Dim(area, Overlays.ScrimForeground, Overlays.ScrimBackground);
        var matching = Matching;
        _selected = Math.Clamp(_selected, 0, Math.Max(0, matching.Count - 1));

        var contentWidth = Math.Max(10, Math.Min(92, area.Width - 4) - 6);
        var rows = Math.Max(1, Math.Min(matching.Count, Math.Max(3, area.Height - 12)));
        var detailRows = matching.Count == 0
            ? 1
            // A description cut off mid-sentence is worse than a slightly taller panel, and a narrow
            // terminal is exactly where these wrap to four lines.
            : Math.Min(5, Text.Wrap(Describe(matching[_selected]), contentWidth).Count);
        var panel = Overlays.Centre(area, 92,
            Math.Clamp(rows + detailRows + 8, 10, Math.Max(10, area.Height - 2)));
        canvas.DrawPanel(panel, "WHAT WOULD YOU LIKE TO DO?", Theme.Blue, Theme.Surface);

        var x = panel.X + 3;
        var width = Math.Max(10, panel.Width - 6);
        var top = panel.Y + 1;
        canvas.DrawText(x, top, "Type", Theme.Muted, maxWidth: 5, background: Theme.Surface);
        _filter.Draw(canvas, new Rect(x + 6, top, width - 6, 1), focused: true,
            "part of an action, or the key it is bound to", Theme.Blue);
        top += 2;

        if (matching.Count == 0)
        {
            // Not "press ?" — this overlay owns every key, so ? would be typed into the filter.
            canvas.DrawText(x, top, "Nothing matches that. Esc, then ? for the full reference.",
                Theme.Muted, maxWidth: width, background: Theme.Surface);
            Overlays.Footer(canvas, panel, Theme.Blue, ("Esc", "close"));
            return;
        }

        var first = Math.Clamp(_selected - rows / 2, 0, Math.Max(0, matching.Count - rows));
        for (var index = first; index < first + rows && index < matching.Count; index++)
        {
            var action = matching[index];
            var selected = index == _selected;
            var available = action.Unavailable is null;
            var y = top + index - first;
            var fill = selected ? Theme.SurfaceRaised : Theme.Surface;
            canvas.Fill(new Rect(x, y, width, 1), fill);
            canvas.DrawText(x, y, selected ? "▸" : " ", Theme.Blue, bold: true, maxWidth: 1, background: fill);
            canvas.DrawText(x + 2, y, action.Id.PadRight(10), available ? Theme.Blue : Theme.Muted,
                bold: selected, maxWidth: 10, background: fill);
            var title = available ? action.Title : action.Title + "   (not now)";
            canvas.DrawText(x + 13, y, Text.Truncate(title, Math.Max(1, width - 13)),
                available ? Theme.Foreground : Theme.Muted, bold: selected, maxWidth: width - 13,
                background: fill);
        }

        var detailTop = top + rows + 1;
        var chosen = matching[_selected];
        canvas.DrawWrapped(x, detailTop, width, Math.Max(1, panel.Bottom - 4 - detailTop + 1),
            Describe(chosen), chosen.Unavailable is null ? Theme.Muted : Theme.Amber,
            background: Theme.Surface);

        Overlays.Footer(canvas, panel, Theme.Blue,
            ("↑↓", "choose"), ("Enter", "do it"), ("type", "to filter"), ("Esc", "close"));
    }

    private static string Describe(PaletteAction action) =>
        action.Unavailable is null ? action.Detail : "Not right now — " + action.Unavailable;

    /// <summary>
    /// Builds the palette for the current state. Availability is computed here rather than at the
    /// moment a key is pressed, so the reason an action is refused can be shown before it is tried.
    /// </summary>
    public static IReadOnlyList<PaletteAction> Build(
        DashboardSnapshot snapshot,
        WorkflowTask? selected,
        int running)
    {
        var git = snapshot.Config.Mode == WorkspaceMode.Git;
        string? NeedsTask() => selected is null ? "no task is selected. Press N to create one" : null;

        var actions = new List<PaletteAction>();
        foreach (var binding in Keymap.All)
        {
            // Ways to move around are not things to do, and listing them as commands would bury
            // the actions that actually change something.
            if (binding.Key is "↑↓" or "Tab" or "Esc" or "/" or "PgUp PgDn" or "Home End")
            {
                continue;
            }

            var unavailable = binding.Key switch
            {
                "Enter" => NeedsTask()
                           ?? (selected!.Status == WorkflowStatus.Running ? "it is already running" : null)
                           ?? (selected.Status == WorkflowStatus.Landed ? "it has already landed" : null)
                           // The orchestrator refuses a cancelled task outright, so offering to run
                           // one would be an action that fails the moment it is taken.
                           ?? (selected.Status == WorkflowStatus.Cancelled
                               ? "it was cancelled. Press R to reset it first"
                               : null)
                           ?? (running >= snapshot.Config.MaxParallelAgents
                               ? $"{running} tasks are already running, which is this workspace's limit"
                               : null),
                "C" => NeedsTask()
                       ?? (selected!.Status == WorkflowStatus.Running ? null : "it is not running"),
                "G" => NeedsTask()
                       ?? (selected!.Status == WorkflowStatus.ReadyToLand
                           ? null
                           : $"it is {selected.Status}. Only a verified, audited task can be landed"),
                "X" => NeedsTask()
                       ?? (!git ? "a standalone workspace has no worktree to remove" : null)
                       ?? (selected!.Status == WorkflowStatus.Running ? "it is still running" : null),
                "R" => NeedsTask()
                       ?? (selected!.Status is WorkflowStatus.Failed or WorkflowStatus.Cancelled
                           ? null
                           : $"it is {selected.Status}, so there is nothing to reset"),
                "I" or "L" => NeedsTask(),
                // The task builder cannot produce a valid task without somebody to assign it to.
                "N" => snapshot.Config.Agents.Any(agent => agent.Enabled)
                    ? null
                    : "no agents are enabled, so there would be nobody to give the work to",
                "F" => snapshot.Tasks.Count == 0 ? "there are no tasks to find yet" : null,
                "M" => snapshot.Config.Agents.Count == 0 ? "no agents are configured" : null,
                _ => null
            };

            actions.Add(new PaletteAction(binding.Key, Capitalise(binding.Action), binding.Detail, unavailable));
        }

        return actions;
    }

    private static string Capitalise(string value) =>
        value.Length == 0 ? value : char.ToUpperInvariant(value[0]) + value[1..];
}
