namespace FKNRTD.Dashboard;

internal readonly record struct Rgb(byte Red, byte Green, byte Blue)
{
    public string ForegroundCode => $"\u001b[38;2;{Red};{Green};{Blue}m";
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

    public static Rgb Agent(string kind) => kind.ToLowerInvariant() switch
    {
        "claude" => Orange,
        "codex" => Violet,
        "cline" => Cyan,
        "copilot" => Pink,
        _ => Blue
    };
}
