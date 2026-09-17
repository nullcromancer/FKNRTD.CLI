namespace FKNRTD.Dashboard;

/// <summary>What the dashboard should do with an overlay after it has consumed a keystroke.</summary>
internal enum OverlayResult
{
    /// <summary>The overlay stays open.</summary>
    Continue,

    /// <summary>The operator backed out. Discard it and change nothing.</summary>
    Cancel,

    /// <summary>The operator completed it. The dashboard reads its values and acts.</summary>
    Submit
}

/// <summary>
/// A modal layer drawn over the dashboard frame. Every question FKNRTD.CLI asks is an overlay,
/// which is what lets a question carry its own explanation instead of being a bare prompt on a
/// blank terminal.
/// </summary>
internal interface IOverlay
{
    /// <summary>A short label for the footer, so the operator can see which mode they are in.</summary>
    string Mode { get; }

    void Draw(Canvas canvas, Rect area);

    OverlayResult HandleKey(ConsoleKeyInfo key);
}

internal static class Overlays
{
    /// <summary>The scrim colours applied to the frame behind a modal.</summary>
    public static readonly Rgb ScrimForeground = new(74, 82, 94);

    public static readonly Rgb ScrimBackground = new(11, 14, 19);

    /// <summary>
    /// Centres a panel inside the available area, capped so it never exceeds the terminal and never
    /// collapses below a readable size.
    /// </summary>
    public static Rect Centre(Rect area, int preferredWidth, int preferredHeight)
    {
        // The minimums are floors on what is readable, but they never win against the area itself:
        // a panel wider or taller than the terminal cannot be drawn, only wrapped and scrolled.
        var width = Math.Min(Math.Min(preferredWidth, area.Width), Math.Max(20, area.Width - 4));
        var height = Math.Min(Math.Min(preferredHeight, area.Height), Math.Max(6, area.Height - 2));
        var x = area.X + Math.Max(0, (area.Width - width) / 2);
        var y = area.Y + Math.Max(0, (area.Height - height) / 2);
        return new Rect(x, y, width, height);
    }

    /// <summary>
    /// Draws the key legend along the bottom of a panel. Every overlay ends with one, because a
    /// modal that does not say how to leave it is a trap.
    /// </summary>
    public static void Footer(Canvas canvas, Rect panel, Rgb accent, params (string Key, string Meaning)[] keys)
    {
        var y = panel.Bottom - 2;
        var inner = new Rect(panel.X + 2, y, Math.Max(1, panel.Width - 4), 1);
        canvas.Fill(new Rect(panel.X + 1, y, Math.Max(0, panel.Width - 2), 1), Theme.SurfaceSunken);
        canvas.DrawRule(panel.X + 1, y - 1, Math.Max(0, panel.Width - 2), accent.Blend(Theme.Surface, 0.55),
            Theme.Surface);

        var x = inner.X;
        foreach (var (key, meaning) in keys)
        {
            var keyWidth = Text.DisplayWidth(key);
            var meaningWidth = Text.DisplayWidth(meaning);
            if (x + keyWidth + meaningWidth + 3 > inner.Right)
            {
                break;
            }

            canvas.DrawText(x, y, key, accent, bold: true, maxWidth: keyWidth, background: Theme.SurfaceSunken);
            x += keyWidth + 1;
            canvas.DrawText(x, y, meaning, Theme.Muted, maxWidth: meaningWidth, background: Theme.SurfaceSunken);
            x += meaningWidth + 2;
        }
    }

    /// <summary>
    /// A labelled block of explanation: a small caption over wrapped body text. Returns the row
    /// after the block so a panel can stack several without tracking heights.
    /// </summary>
    public static int Explain(
        Canvas canvas,
        int x,
        int y,
        int width,
        int maxRows,
        string caption,
        string body,
        Rgb accent)
    {
        if (maxRows <= 1 || string.IsNullOrWhiteSpace(body))
        {
            return y;
        }

        canvas.DrawText(x, y, caption, accent.Blend(Theme.Muted, 0.35), bold: true, maxWidth: width,
            background: Theme.Surface);
        return canvas.DrawWrapped(x, y + 1, width, maxRows - 1, body, Theme.Foreground, background: Theme.Surface);
    }
}
