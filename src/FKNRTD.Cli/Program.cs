using System.Runtime.InteropServices;
using System.Text;
using FKNRTD.Commands;

Console.InputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
Console.OutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
Terminal.EnableVirtualTerminal();

using var shutdown = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    shutdown.Cancel();
};

try
{
    return await CommandDispatcher.ExecuteAsync(new CliArguments(args), shutdown.Token).ConfigureAwait(false);
}
catch (OperationCanceledException)
{
    Console.Error.WriteLine("FKNRTD.CLI cancelled.");
    return 130;
}
catch (Exception exception) when (exception is not StackOverflowException && exception is not OutOfMemoryException)
{
    // Wrapped to the window. These messages were rewritten to say what went wrong and what to do
    // about it, which made several of them a paragraph - and a paragraph printed as one line is
    // read as far as the right edge and no further.
    Terminal.WriteError("FKNRTD.CLI error: " + exception.Message);
    return 1;
}

internal static partial class Terminal
{
    /// <summary>
    /// Writes a diagnostic to standard error, wrapped to the window and indented after the first
    /// line so the message reads as one block rather than trailing off the edge.
    /// </summary>
    public static void WriteError(string message)
    {
        var width = 100;
        try
        {
            width = Math.Clamp(Console.WindowWidth - 2, 40, 100);
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or
                                          PlatformNotSupportedException)
        {
            // No console attached; the default is what a pipe gets.
        }

        var line = new StringBuilder();
        foreach (var word in message.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.Length > 0 && line.Length + 1 + word.Length > width)
            {
                Console.Error.WriteLine(line.ToString());
                line.Clear().Append("  ");
            }
            else if (line.Length > 0)
            {
                line.Append(' ');
            }

            line.Append(word);
        }

        if (line.Length > 0)
        {
            Console.Error.WriteLine(line.ToString());
        }
    }

    private const int StandardOutputHandle = -11;
    private const uint EnableVirtualTerminalProcessing = 0x0004;

    public static void EnableVirtualTerminal()
    {
        if (!OperatingSystem.IsWindows() || Console.IsOutputRedirected)
        {
            return;
        }

        var handle = GetStdHandle(StandardOutputHandle);
        if (handle == IntPtr.Zero || handle == new IntPtr(-1) || !GetConsoleMode(handle, out var mode))
        {
            return;
        }

        _ = SetConsoleMode(handle, mode | EnableVirtualTerminalProcessing);
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetConsoleMode(IntPtr consoleHandle, out uint mode);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetConsoleMode(IntPtr consoleHandle, uint mode);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial IntPtr GetStdHandle(int standardHandle);
}
