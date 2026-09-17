using FKNRTD.Help;
using FKNRTD.Portal;

namespace FKNRTD.Commands;

/// <summary>
/// <c>fknrtd portal</c>. Writes the offline HTML guide from the same tables the running program
/// reads: the glossary the dashboard shows inline, the command catalog, and the keymap the footer
/// draws from. Generating it rather than writing it by hand is the point — documentation that is
/// derived from the code cannot describe a command that was removed or a key that never existed.
/// </summary>
internal static class PortalCommand
{
    public const string DefaultFileName = "fknrtd-portal.html";

    public static int Execute(CliArguments arguments)
    {
        var destination = Path.GetFullPath(
            arguments.Get("out") ?? arguments.Positional(1) ?? DefaultFileName);
        var html = Render(DateTimeOffset.UtcNow);

        var directory = Path.GetDirectoryName(destination);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(destination, html, new System.Text.UTF8Encoding(false));
        Console.WriteLine($"√ Wrote the FKNRTD.CLI portal to {destination}");
        Console.WriteLine($"  {Glossary.All.Count} explained terms, {CommandCatalog.All.Count} commands, " +
                          $"{Keymap.All.Count} keys, " +
                          $"{SettingsCatalog.All.Count} settings, {Milestones.All.Count} build-log entries.");
        Console.WriteLine("  It is a single self-contained file. Open it from disk; it needs no network.");
        return 0;
    }

    /// <summary>The document itself, with the clock supplied so the output is testable.</summary>
    internal static string Render(DateTimeOffset generatedAt) => PortalWriter.Render(new PortalModel(
        Glossary.All,
        CommandCatalog.All,
        Keymap.All,
        CommandDispatcher.Version,
        generatedAt,
        Milestones.All,
        SettingsCatalog.All));
}
