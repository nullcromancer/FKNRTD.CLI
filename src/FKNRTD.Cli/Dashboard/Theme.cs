namespace FKNRTD.Dashboard;

internal readonly record struct Rgb(byte Red, byte Green, byte Blue)
{
    public string ForegroundCode => $"[38;2;{Red};{Green};{Blue}m";

    public string BackgroundCode => $"[48;2;{Red};{Green};{Blue}m";

    /// <summary>
    /// Mixes towards <paramref name="other"/>. Used to derive a panel's chrome from its accent so a
    /// new surface needs one colour rather than five.
    /// </summary>
    public Rgb Blend(Rgb other, double amount)
    {
        var weight = Math.Clamp(amount, 0, 1);
        return new Rgb(
            (byte)Math.Round(Red + (other.Red - Red) * weight),
            (byte)Math.Round(Green + (other.Green - Green) * weight),
            (byte)Math.Round(Blue + (other.Blue - Blue) * weight));
    }
}

internal static class Theme
{
    public static readonly Rgb Foreground = new(230, 237, 243);
    public static readonly Rgb Muted = new(139, 148, 158);
    public static readonly Rgb Blue = new(88, 166, 255);
    public static readonly Rgb Cyan = new(86, 212, 221);
    public static readonly Rgb Green = new(86, 211, 100);
    public static readonly Rgb Amber = new(242, 204, 96);
    public static readonly Rgb Orange = new(255, 166, 87);
    public static readonly Rgb Red = new(255, 123, 114);
    public static readonly Rgb Violet = new(210, 168, 255);
    public static readonly Rgb Pink = new(255, 97, 136);

    /// <summary>The fill behind a modal. Darker than a terminal's default so the layer reads as raised.</summary>
    public static readonly Rgb Surface = new(21, 26, 35);

    /// <summary>A recessed strip inside a modal: help text, examples, footers.</summary>
    public static readonly Rgb SurfaceSunken = new(15, 19, 26);

    /// <summary>The fill behind a focused input or a selected row.</summary>
    public static readonly Rgb SurfaceRaised = new(33, 41, 54);

    /// <summary>The fill behind the text cursor.</summary>
    public static readonly Rgb Cursor = new(88, 166, 255);

    /// <summary>Text on top of <see cref="Cursor"/> or another saturated fill.</summary>
    public static readonly Rgb OnAccent = new(12, 16, 22);

    public static Rgb Agent(string kind) => kind.ToLowerInvariant() switch
    {
        "claude" => Orange,
        "codex" => Violet,
        "cline" => Cyan,
        "copilot" => Pink,
        _ => Blue
    };
}
