using FKNRTD.Help;

namespace FKNRTD.Dashboard;

/// <summary>
/// A type-the-word confirmation for an action that cannot be undone from inside the dashboard.
/// The word has to be typed in full — not y, not Enter — because a stray keystroke should not be
/// able to merge a branch or delete a directory. The panel states the consequence in plain language
/// above the input, so the operator is confirming something they have actually been told.
/// </summary>
internal sealed class Confirmation : IOverlay
{
    private readonly string _title;
    private readonly Rgb _accent;
    private readonly string _subject;
    private readonly string _consequence;
    private readonly string _word;
    private readonly string? _alternative;
    private readonly GlossaryEntry? _entry;
    private readonly TextField _field = new();
    private bool _mistyped;

    public Confirmation(
        string title,
        Rgb accent,
        string subject,
        string consequence,
        string word,
        string glossaryTerm,
        string? alternative = null)
    {
        _title = title;
        _accent = accent;
        _subject = subject;
        _consequence = consequence;
        _word = word;
        _alternative = alternative;
        _entry = Glossary.Find(glossaryTerm);
    }

    public string Mode => _title;

    public OverlayResult HandleKey(ConsoleKeyInfo key)
    {
        if (key.Key == ConsoleKey.Escape)
        {
            return OverlayResult.Cancel;
        }

        if (_field.HandleKey(key))
        {
            _mistyped = false;
            return OverlayResult.Continue;
        }

        if (key.Key != ConsoleKey.Enter)
        {
            return OverlayResult.Continue;
        }

        if (string.Equals(_field.Value.Trim(), _word, StringComparison.Ordinal))
        {
            return OverlayResult.Submit;
        }

        _mistyped = true;
        return OverlayResult.Continue;
    }

    public void Draw(Canvas canvas, Rect area)
    {
        canvas.Dim(area, Overlays.ScrimForeground, Overlays.ScrimBackground);
        var contentWidth = Math.Max(10, Math.Min(84, area.Width - 4) - 6);
        var panel = Overlays.Centre(area, 84, Measure(contentWidth));
        canvas.DrawPanel(panel, _title, _accent, Theme.Surface);
        var x = panel.X + 3;
        var width = Math.Max(10, panel.Width - 6);
        var lastRow = panel.Bottom - 4;

        var row = canvas.DrawWrapped(x, panel.Y + 1, width, 2, _subject, Theme.Foreground, bold: true,
            background: Theme.Surface);
        row = canvas.DrawWrapped(x, row + 1, width, 6, _consequence, Theme.Amber, background: Theme.Surface);
        row++;

        canvas.DrawText(x, row++, $"Type {_word} to confirm:", Theme.Muted, maxWidth: width,
            background: Theme.Surface);
        _field.Draw(canvas, new Rect(x, row, Math.Min(width, 28), 1), focused: true, _word, _accent);
        row += 2;

        if (_mistyped)
        {
            row = canvas.DrawWrapped(x, row, width, 2, MistypedMessage, Theme.Red, bold: true,
                background: Theme.Surface);
            row++;
        }

        if (_alternative is not null && row < lastRow)
        {
            row = canvas.DrawWrapped(x, row, width, 2, _alternative, Theme.Muted, background: Theme.Surface);
            row++;
        }

        if (_entry is not null && row <= lastRow)
        {
            Overlays.Explain(canvas, x, row, width, lastRow - row + 1, "WHAT THIS MEANS", _entry.Detail, _accent);
        }

        Overlays.Footer(canvas, panel, _accent, ("Enter", "confirm"), ("Esc", "back out, change nothing"));
    }

    /// <summary>
    /// The panel's height, from what it actually has to say. A confirmation whose explanation is cut
    /// off mid-sentence is asking the operator to agree to something it did not finish telling them.
    /// </summary>
    private int Measure(int width)
    {
        // Mirrors Draw row for row, gaps included, so the panel is never a line short of its text.
        var rows = Math.Min(2, Text.Wrap(_subject, width).Count);
        rows += 1 + Math.Min(6, Text.Wrap(_consequence, width).Count) + 1;
        rows += 3;                                       // the "type X" caption, the field, a gap
        if (_mistyped)
        {
            rows += Math.Min(2, Text.Wrap(MistypedMessage, width).Count) + 1;
        }

        if (_alternative is not null)
        {
            rows += Math.Min(2, Text.Wrap(_alternative, width).Count) + 1;
        }

        if (_entry is not null)
        {
            rows += Text.Wrap(_entry.Detail, width).Count + 1;
        }

        return rows + 4;                                 // rule, footer, and both borders
    }

    private string MistypedMessage =>
        $"× That is not the word. Type {_word} exactly, or press Esc to back out.";
}
