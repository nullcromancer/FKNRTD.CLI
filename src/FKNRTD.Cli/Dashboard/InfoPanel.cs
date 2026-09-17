namespace FKNRTD.Dashboard;

/// <summary>One piece of a read-only panel. Blocks are flattened to rows at render width.</summary>
internal abstract record InfoBlock;

/// <summary>A section caption, drawn in the panel's accent.</summary>
internal sealed record InfoHeading(string Text) : InfoBlock;

/// <summary>A label and a value on one row, with the label column aligned across the panel.</summary>
internal sealed record InfoLine(string Label, string Text, Rgb? Colour = null, bool Bold = false) : InfoBlock;

/// <summary>Wrapped prose. This is where an explanation goes.</summary>
internal sealed record InfoParagraph(string Text, Rgb? Colour = null, int Indent = 0) : InfoBlock;

/// <summary>A blank row.</summary>
internal sealed record InfoGap : InfoBlock;

/// <summary>
/// One row rendered verbatim and truncated rather than wrapped. Diff and log lines lose their
/// meaning when a wrap moves a leading + or - away from the start of the row.
/// </summary>
internal sealed record InfoRaw(string Text, Rgb? Colour = null) : InfoBlock;

/// <summary>
/// A scrollable — and optionally filterable — read-only panel. The dashboard's reference surfaces
/// are all one of these: the key and glossary reference behind <c>?</c>, the task record behind
/// <c>I</c>, the pre-flight checks behind <c>D</c>, the agent roster behind <c>A</c>. Building them
/// from one widget means they scroll, filter and close the same way, so learning one teaches all.
/// </summary>
internal sealed class InfoPanel : IOverlay
{
    private readonly string _title;
    private readonly Rgb _accent;
    private readonly Func<string, IReadOnlyList<InfoBlock>> _build;
    private readonly TextField? _filter;
    private readonly (ConsoleKey Key, string Label, string Meaning)? _action;
    private readonly string _filterHint;
    private int _scroll;

    public InfoPanel(
        string title,
        Rgb accent,
        Func<string, IReadOnlyList<InfoBlock>> build,
        string? filterHint = null,
        (ConsoleKey Key, string Label, string Meaning)? action = null)
    {
        _title = title;
        _accent = accent;
        _build = build;
        _filterHint = filterHint ?? string.Empty;
        _filter = filterHint is null ? null : new TextField();
        // A panel that filters has already spent every letter key on the filter, so an action key
        // would eat a character the operator meant to type. Only an unfiltered panel can offer one.
        _action = _filter is null ? action : null;
    }

    public InfoPanel(string title, Rgb accent, IReadOnlyList<InfoBlock> blocks)
        : this(title, accent, _ => blocks)
    {
    }

    public string Mode => _title;

    /// <summary>
    /// True when the operator pressed this panel's action key. A reference panel is normally a
    /// dead end, which is right for a glossary and wrong for a panel listing something you can fix.
    /// </summary>
    public bool ActionRequested { get; private set; }

    public OverlayResult HandleKey(ConsoleKeyInfo key)
    {
        if (_action is { } available && key.Key == available.Key)
        {
            ActionRequested = true;
            return OverlayResult.Submit;
        }

        switch (key.Key)
        {
            case ConsoleKey.Escape:
            case ConsoleKey.Enter:
                return OverlayResult.Cancel;
            case ConsoleKey.UpArrow:
                _scroll = Math.Max(0, _scroll - 1);
                return OverlayResult.Continue;
            case ConsoleKey.DownArrow:
                _scroll++;
                return OverlayResult.Continue;
            case ConsoleKey.PageUp:
                _scroll = Math.Max(0, _scroll - 10);
                return OverlayResult.Continue;
            case ConsoleKey.PageDown:
                _scroll += 10;
                return OverlayResult.Continue;
            case ConsoleKey.Home:
                _scroll = 0;
                return OverlayResult.Continue;
        }

        // Typing filters rather than scrolling, so a long reference is searched instead of paged.
        if (_filter is not null && _filter.HandleKey(key))
        {
            _scroll = 0;
            return OverlayResult.Continue;
        }

        return OverlayResult.Continue;
    }

    public void Draw(Canvas canvas, Rect area)
    {
        canvas.Dim(area, Overlays.ScrimForeground, Overlays.ScrimBackground);

        // Lay the content out at the width the panel will have, then give the panel exactly the
        // height that content needs — short panels stay short instead of being framed by emptiness,
        // and long ones fill the terminal and scroll.
        var contentWidth = Math.Max(10, Math.Min(104, area.Width - 4) - 6);
        var rows = Flatten(_build(_filter?.Value.Trim() ?? string.Empty), contentWidth);
        var chrome = 4 + (_filter is null ? 0 : 2);
        var panel = Overlays.Centre(area, 104,
            Math.Clamp(rows.Count + chrome, 8, Math.Max(8, area.Height - 2)));
        canvas.DrawPanel(panel, _title, _accent, Theme.Surface);
        var x = panel.X + 3;
        var width = Math.Max(10, panel.Width - 6);
        var top = panel.Y + 1;
        var lastRow = panel.Bottom - 4;

        if (_filter is not null)
        {
            canvas.DrawText(x, top, "Search", Theme.Muted, maxWidth: 7, background: Theme.Surface);
            _filter.Draw(canvas, new Rect(x + 8, top, width - 8, 1), focused: true, _filterHint, _accent);
            top += 2;
        }
        var visible = Math.Max(1, lastRow - top + 1);
        _scroll = Math.Clamp(_scroll, 0, Math.Max(0, rows.Count - visible));

        if (rows.Count == 0)
        {
            canvas.DrawText(x, top, "Nothing matches that.", Theme.Muted, maxWidth: width,
                background: Theme.Surface);
        }

        for (var index = _scroll; index < rows.Count && index - _scroll < visible; index++)
        {
            var row = rows[index];
            canvas.DrawText(x + row.Indent, top + index - _scroll, row.Text, row.Colour, row.Bold,
                Math.Max(0, width - row.Indent), Theme.Surface);
        }

        if (rows.Count > visible)
        {
            var position = $"{_scroll + 1}–{Math.Min(rows.Count, _scroll + visible)} of {rows.Count}";
            var positionWidth = Text.DisplayWidth(position);
            canvas.DrawText(panel.Right - 3 - positionWidth, panel.Y, position, Theme.Muted,
                maxWidth: positionWidth, background: Theme.Surface);
        }

        Overlays.Footer(canvas, panel, _accent, FooterKeys(rows.Count > visible));
    }

    private (string Key, string Meaning)[] FooterKeys(bool scrollable)
    {
        var keys = new List<(string, string)>();
        if (scrollable)
        {
            keys.Add(("↑↓ PgUp PgDn", "scroll"));
        }

        if (_filter is not null)
        {
            keys.Add(("type", "to search"));
        }

        if (_action is { } available)
        {
            keys.Add((available.Label, available.Meaning));
        }

        keys.Add(("Esc", "close"));
        return keys.ToArray();
    }

    private readonly record struct Row(string Text, int Indent, Rgb Colour, bool Bold);

    private List<Row> Flatten(IReadOnlyList<InfoBlock> blocks, int width)
    {
        var rows = new List<Row>();
        var labelWidth = blocks.OfType<InfoLine>().Select(line => Text.DisplayWidth(line.Label)).DefaultIfEmpty(0).Max();
        labelWidth = Math.Clamp(labelWidth + 2, 0, Math.Max(0, width / 2));

        foreach (var block in blocks)
        {
            switch (block)
            {
                case InfoHeading heading:
                    if (rows.Count > 0)
                    {
                        rows.Add(new Row(string.Empty, 0, Theme.Muted, false));
                    }

                    rows.Add(new Row(Text.Truncate(heading.Text.ToUpperInvariant(), width), 0, _accent, true));
                    break;
                case InfoLine line:
                {
                    var value = Text.Wrap(line.Text, Math.Max(1, width - labelWidth));
                    // Padded by display width, not by UTF-16 length: a CJK label is two columns per
                    // character, and PadRight would leave the value column one short of where every
                    // other row puts it.
                    var label = Text.Truncate(line.Label, labelWidth);
                    label += new string(' ', Math.Max(0, labelWidth - Text.DisplayWidth(label)));
                    rows.Add(new Row(
                        label + value.FirstOrDefault(),
                        0,
                        line.Colour ?? Theme.Foreground,
                        line.Bold));
                    foreach (var continuation in value.Skip(1))
                    {
                        rows.Add(new Row(continuation, labelWidth, line.Colour ?? Theme.Foreground, false));
                    }

                    break;
                }
                case InfoParagraph paragraph:
                    foreach (var wrapped in Text.Wrap(paragraph.Text, Math.Max(1, width - paragraph.Indent)))
                    {
                        rows.Add(new Row(wrapped, paragraph.Indent, paragraph.Colour ?? Theme.Foreground, false));
                    }

                    break;
                case InfoRaw raw:
                    rows.Add(new Row(Text.Truncate(raw.Text, width), 0, raw.Colour ?? Theme.Foreground, false));
                    break;
                case InfoGap:
                    rows.Add(new Row(string.Empty, 0, Theme.Muted, false));
                    break;
            }
        }

        return rows;
    }
}
