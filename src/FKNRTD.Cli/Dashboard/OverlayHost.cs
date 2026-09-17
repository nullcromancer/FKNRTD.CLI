namespace FKNRTD.Dashboard;

/// <summary>
/// Runs one overlay on its own, without the dashboard behind it. This is what lets a command-line
/// invocation such as <c>fknrtd task new</c> ask its questions through the same explained form the
/// dashboard uses, rather than through a second, worse set of prompts that would then be free to
/// disagree with it.
/// </summary>
internal static class OverlayHost
{
    /// <summary>
    /// Shows <paramref name="overlay"/> until it is submitted or cancelled. Returns true when the
    /// operator completed it.
    /// </summary>
    public static async Task<bool> RunAsync(
        IOverlay overlay,
        string caption,
        bool useColor,
        CancellationToken cancellationToken)
    {
        if (Console.IsInputRedirected || Console.IsOutputRedirected)
        {
            throw new InvalidOperationException(
                "This command asks questions on screen and needs an interactive terminal. " +
                "Pass the values as options instead — run 'fknrtd help' to see them.");
        }

        Screen.Enter();
        var previousWidth = -1;
        var previousHeight = -1;
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var width = Screen.Width(100);
                var height = Screen.Height(32);
                Console.Write(width == previousWidth && height == previousHeight
                    ? "[H"
                    : "[2J[H");
                Console.Write(Frame(overlay, caption, width, height, useColor));
                previousWidth = width;
                previousHeight = height;

                while (!Console.KeyAvailable)
                {
                    await Task.Delay(30, cancellationToken).ConfigureAwait(false);
                    if (Screen.Width(100) != width || Screen.Height(32) != height)
                    {
                        break;
                    }
                }

                if (!Console.KeyAvailable)
                {
                    continue;
                }

                switch (overlay.HandleKey(Console.ReadKey(intercept: true)))
                {
                    case OverlayResult.Cancel:
                        return false;
                    case OverlayResult.Submit:
                        return true;
                }
            }

            return false;
        }
        finally
        {
            Screen.Exit();
        }
    }

    /// <summary>
    /// The backdrop behind a standalone overlay: a title bar naming the product and the workspace,
    /// so the operator can see what they are configuring while they answer.
    /// </summary>
    internal static string Frame(IOverlay overlay, string caption, int width, int height, bool useColor)
    {
        width = Math.Max(60, width);
        height = Math.Max(20, height);
        var canvas = new Canvas(width, height);
        canvas.DrawBox(new Rect(0, 0, width, 3), "◉ FKNRTD COMMAND CENTER", Theme.Cyan);
        canvas.DrawText(2, 1, Text.Truncate(caption, width - 4), Theme.Muted, maxWidth: width - 4);
        overlay.Draw(canvas, new Rect(0, 0, width, height));
        return canvas.Render(useColor);
    }
}

/// <summary>
/// Terminal control shared by every full-screen surface. Keeping enter, exit and size detection in
/// one place means a surface cannot leave the terminal in the alternate screen with a hidden cursor.
/// </summary>
internal static class Screen
{
    public static void Enter()
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        Console.Write("[?1049h[?25l[2J[H");
    }

    public static void Exit() => Console.Write("[0m[?25h[?1049l");

    public static int Width(int fallback) => Measure(() => Console.WindowWidth, 60, fallback);

    public static int Height(int fallback) => Measure(() => Console.WindowHeight, 20, fallback);

    private static int Measure(Func<int> read, int floor, int fallback)
    {
        try
        {
            return Math.Max(floor, read());
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or
                                          PlatformNotSupportedException)
        {
            return fallback;
        }
    }
}
