using FKNRTD.Dashboard;
using FKNRTD.Services;
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
        var html = Render();

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

    /// <summary>The document itself. It takes no clock, which is what makes it reproducible.</summary>
    /// <remarks>
    /// This used to stamp <c>DateTimeOffset.UtcNow</c>, and the file is committed, so regenerating it
    /// produced a one-line diff whether or not a word of it had changed — and the test that was meant
    /// to catch that held the timestamp fixed and compared two renders, proving the renderer pure
    /// while the command it backed was not. The portal describes a set of tables, so it is dated by
    /// the newest thing in them: the same tables now give the same bytes, and the date moves only
    /// when the product it describes does.
    /// </remarks>
    internal static string Render() => PortalWriter.Render(new PortalModel(
        Glossary.All,
        CommandCatalog.All,
        Keymap.All,
        CommandDispatcher.Version,
        Milestones.All.Select(milestone => milestone.Date).Max() ?? "",
        Milestones.All,
        SettingsCatalog.All,
        Screens()));

    /// <summary>
    /// The screens, drawn now by the renderer the product runs. Generating them rather than pasting
    /// them in is the point: a picture in a manual is the first thing to go stale, and one that is
    /// produced from the same code as the thing it depicts cannot.
    /// </summary>
    private static IReadOnlyList<(string Title, string Why, string Frame)> Screens()
    {
        var renderer = new DashboardApp(null!, null!, null!, null!, null!,
            new StateStore(WorkspaceLocator.ForRoot(Path.GetTempPath())), null!, null!, null!);
        var snapshot = SampleWorkspace.Snapshot;

        return
        [
            ("The command center",
                "What 'fknrtd' opens. The pipeline is your tasks; the radar is what each agent is " +
                "doing right now; the sentinel warns when two agents are about to touch the same " +
                "file. The bottom line always says what the highlighted task needs from you next.",
                renderer.Render(snapshot, 108, 30, useColor: false)),

            ("Describing a piece of work",
                "What N opens. Every question carries its own definition and a worked example, and " +
                "every field after the brief already holds the right answer for this workspace - so " +
                "the usual path through this form is a title, a brief, and Enter.",
                renderer.Render(snapshot, 108, 26, useColor: false, TaskWizard.Create(SampleWorkspace.Config))),

            ("The agent roster",
                "What A opens. It says which agents can actually be launched on this machine and " +
                "which can return a verdict, and it can change the list: Space enables or disables " +
                "one, E points one at a different program, N adds one, Del removes one.",
                renderer.Render(snapshot, 108, 28, useColor: false,
                    AgentManager.Create(snapshot))),

            ("Every setting, and what changing it costs",
                "What S opens. The configuration is plain JSON meant to be edited by hand, and this " +
                "is the explanation that was missing from it. Enter changes the highlighted field " +
                "through the same guided form the rest of the product uses.",
                renderer.Render(snapshot, 108, 30, useColor: false,
                    new SettingsBrowser(SampleWorkspace.Config, "/src/aurora-api/.fknrtd/config.json")))
        ];
    }
}
