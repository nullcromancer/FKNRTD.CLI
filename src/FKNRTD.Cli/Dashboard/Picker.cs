using FKNRTD.Domain;
using FKNRTD.Help;

namespace FKNRTD.Dashboard;

/// <summary>One selectable row: the value returned, what is shown, and a line of context.</summary>
internal sealed record PickerItem(string Value, string Label, string Detail, Rgb? Colour = null);

/// <summary>
/// A searchable list that returns one chosen value. The overview's task panel shows five rows and
/// moves through them with the arrow keys, which is fine for five tasks and useless for forty — at
/// which point finding the one you want is the whole job.
/// </summary>
internal sealed class Picker : IOverlay
{
    private readonly string _title;
    private readonly Rgb _accent;
    private readonly IReadOnlyList<PickerItem> _items;
    private readonly string _hint;
    private readonly string _emptyMessage;
    private readonly TextField _filter = new();
    private int _selected;

    public Picker(
        string title,
        Rgb accent,
        IReadOnlyList<PickerItem> items,
        string hint,
        string emptyMessage)
    {
        _title = title;
        _accent = accent;
        _items = items;
        _hint = hint;
        _emptyMessage = emptyMessage;
    }

    public string Mode => _title;

    /// <summary>The value chosen, read by the dashboard after a submit.</summary>
    public string? Chosen { get; private set; }

    private IReadOnlyList<PickerItem> Matching
    {
        get
        {
            var needle = _filter.Value.Trim();
            return needle.Length == 0
                ? _items
                : _items.Where(item =>
                        item.Label.Contains(needle, StringComparison.OrdinalIgnoreCase) ||
                        item.Detail.Contains(needle, StringComparison.OrdinalIgnoreCase) ||
                        item.Value.Contains(needle, StringComparison.OrdinalIgnoreCase))
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
            case ConsoleKey.PageUp:
                _selected = Math.Max(0, _selected - 10);
                return OverlayResult.Continue;
            case ConsoleKey.PageDown:
                _selected = Math.Min(Math.Max(0, matching.Count - 1), _selected + 10);
                return OverlayResult.Continue;
            case ConsoleKey.Enter:
                if (matching.ElementAtOrDefault(_selected) is not { } chosen)
                {
                    return OverlayResult.Continue;
                }

                Chosen = chosen.Value;
                return OverlayResult.Submit;
        }

        if (_filter.HandleKey(key))
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

        var rows = Math.Max(1, Math.Min(Math.Max(matching.Count, 1), Math.Max(3, area.Height - 10)));
        var panel = Overlays.Centre(area, 96, Math.Clamp(rows + 7, 8, area.Height - 2));
        canvas.DrawPanel(panel, _title, _accent, Theme.Surface);

        var x = panel.X + 3;
        var width = Math.Max(10, panel.Width - 6);
        var top = panel.Y + 1;
        canvas.DrawText(x, top, "Find", Theme.Muted, maxWidth: 5, background: Theme.Surface);
        _filter.Draw(canvas, new Rect(x + 6, top, width - 6, 1), focused: true, _hint, _accent);
        top += 2;

        if (matching.Count == 0)
        {
            canvas.DrawWrapped(x, top, width, Math.Max(1, panel.Bottom - 4 - top + 1),
                _items.Count == 0 ? _emptyMessage : "Nothing matches that.", Theme.Muted,
                background: Theme.Surface);
            Overlays.Footer(canvas, panel, _accent, ("Esc", "close"));
            return;
        }

        var first = Math.Clamp(_selected - rows / 2, 0, Math.Max(0, matching.Count - rows));
        var labelWidth = Math.Min(30, matching.Max(item => Text.DisplayWidth(item.Label)) + 1);
        for (var index = first; index < first + rows && index < matching.Count; index++)
        {
            var item = matching[index];
            var selected = index == _selected;
            var y = top + index - first;
            var fill = selected ? Theme.SurfaceRaised : Theme.Surface;
            canvas.Fill(new Rect(x, y, width, 1), fill);
            canvas.DrawText(x, y, selected ? "▸ " : "  ", _accent, bold: true, maxWidth: 2, background: fill);
            canvas.DrawText(x + 2, y, Text.Truncate(item.Label, labelWidth), item.Colour ?? Theme.Foreground,
                bold: selected, maxWidth: labelWidth, background: fill);
            var detailX = x + 2 + labelWidth + 1;
            canvas.DrawText(detailX, y, Text.Truncate(item.Detail, Math.Max(0, width - (detailX - x))),
                Theme.Muted, maxWidth: Math.Max(0, width - (detailX - x)), background: fill);
        }

        if (matching.Count > rows)
        {
            var position = $"{_selected + 1} of {matching.Count}";
            var positionWidth = Text.DisplayWidth(position);
            canvas.DrawText(panel.Right - 3 - positionWidth, panel.Y, position, Theme.Muted,
                maxWidth: positionWidth, background: Theme.Surface);
        }

        Overlays.Footer(canvas, panel, _accent,
            ("↑↓", "choose"), ("Enter", "select"), ("type", "to filter"), ("Esc", "close"));
    }

    /// <summary>
    /// Every task in the workspace, searchable by title, status or id. Status is spelled out rather
    /// than only drawn as a glyph, so it is also what the filter matches on.
    /// </summary>
    public static Picker Tasks(DashboardSnapshot snapshot) => new(
        "FIND A TASK",
        Theme.Blue,
        snapshot.Tasks
            .Select(task => new PickerItem(
                task.Id,
                Describe(task.Status),
                task.Title,
                Colour(task.Status)))
            .ToArray(),
        "part of a title, a status such as failed, or a task id",
        "No tasks exist in this workspace yet. Press Esc, then N to describe the first one.");

    private static string Describe(WorkflowStatus status) =>
        Glossary.Find("status." + status.ToString().ToLowerInvariant())?.Title ?? status.ToString();

    private static Rgb Colour(WorkflowStatus status) => status switch
    {
        WorkflowStatus.Running => Theme.Blue,
        WorkflowStatus.ReadyToLand or WorkflowStatus.Landed => Theme.Green,
        WorkflowStatus.Failed => Theme.Red,
        WorkflowStatus.Cancelled => Theme.Amber,
        _ => Theme.Muted
    };
}
