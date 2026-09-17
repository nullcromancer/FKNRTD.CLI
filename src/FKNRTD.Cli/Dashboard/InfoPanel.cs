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

/// <summary>Something a reference panel can do, besides being read.</summary>
/// <param name="Id">What the dashboard matches on afterwards.</param>
/// <param name="Key">The key that asks for it.</param>
/// <param name="Label">How that key is printed in the footer.</param>
/// <param name="Meaning">What the footer says it does.</param>
internal readonly record struct PanelAction(string Id, ConsoleKey Key, string Label, string Meaning);

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
    private readonly IReadOnlyList<PanelAction> _actions;
    private readonly string _filterHint;
    private int _scroll;

    public InfoPanel(
        string title,
        Rgb accent,
        Func<string, IReadOnlyList<InfoBlock>> build,
        string? filterHint = null,
        params PanelAction[] actions)
    {
        _title = title;
        _accent = accent;
        _build = build;
        _filterHint = filterHint ?? string.Empty;
        _filter = filterHint is null ? null : new TextField();
        // Two rules, both about not stealing a key that means something else. A panel that filters
        // has already spent every letter on the filter, so only a function key is safe there. And
        // nothing may bind Escape, which is the only way out of a modal.
        _actions = actions
            .Where(action => action.Key != ConsoleKey.Escape)
            .Where(action => _filter is null || IsFunctionKey(action.Key))
            .ToArray();
    }

    public InfoPanel(string title, Rgb accent, IReadOnlyList<InfoBlock> blocks)
        : this(title, accent, _ => blocks)
    {
    }

    public string Mode => _title;

    /// <summary>
    /// The width of a list marker at the start of a paragraph — "1. ", "10. ", "- ", "* " — or
    /// zero when the paragraph does not begin with one. Continuation lines are indented by it, so a
    /// wrapped list item stays visibly inside its own item.
    /// </summary>
    /// <remarks>
    /// Derived from the text rather than declared alongside it. A hanging indent that has to be
    /// passed at every call site is one that will be correct on the paragraphs somebody remembered
    /// and wrong on the rest, and this product has numbered lists written months apart.
    /// </remarks>
    internal static int MarkerWidth(string text)
    {
        if (text.Length < 2)
        {
            return 0;
        }

        if ((text[0] is '-' or '*' or '•') && text[1] == ' ')
        {
            return 2;
        }

        var digits = 0;
        while (digits < text.Length && char.IsAsciiDigit(text[digits]))
        {
            digits++;
        }

        // "1. " and "1) " both count; a bare number opening a sentence does not.
        return digits > 0 &&
               digits + 1 < text.Length &&
               text[digits] is '.' or ')' &&
               text[digits + 1] == ' '
            ? digits + 2
            : 0;
    }

    private static bool IsFunctionKey(ConsoleKey key) => key is >= ConsoleKey.F1 and <= ConsoleKey.F12;

    /// <summary>
    /// Which action the operator asked for, or null. A reference panel is normally a dead end,
    /// which is right for a glossary and wrong for a panel listing something you can fix.
    /// </summary>
    public string? RequestedAction { get; private set; }

    /// <summary>True when any action was asked for.</summary>
    public bool ActionRequested => RequestedAction is not null;

    public OverlayResult HandleKey(ConsoleKeyInfo key)
    {
        // Cast to a nullable first. FirstOrDefault over a struct hands back a default instance
        // rather than null, and `is { }` matches any struct - so written the obvious way this fired
        // an action on every keystroke, with a null id. Three tests caught it at once.
        if (_actions.Cast<PanelAction?>().FirstOrDefault(action => action!.Value.Key == key.Key)
            is { } chosen)
        {
            RequestedAction = chosen.Id;
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

        foreach (var action in _actions)
        {
            keys.Add((action.Label, action.Meaning));
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
                {
                    // A numbered or bulleted paragraph hangs its continuation lines under its text
                    // rather than under its marker. Wrapping them all to the same column put
                    // "edit." and "while the repair budget lasts." hard against the left margin,
                    // in the column the numbers were in, where they read as further steps.
                    var hanging = MarkerWidth(paragraph.Text);
                    var first = true;
                    foreach (var wrapped in Text.Wrap(
                                 paragraph.Text,
                                 Math.Max(1, width - paragraph.Indent - hanging)))
                    {
                        rows.Add(new Row(
                            wrapped,
                            paragraph.Indent + (first ? 0 : hanging),
                            paragraph.Colour ?? Theme.Foreground,
                            false));
                        first = false;
                    }

                    break;
                }
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
