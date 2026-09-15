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
    Console.Error.WriteLine("FKNRTD.CLI error: " + exception.Message);
    return 1;
}

internal static partial class Terminal
{
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
