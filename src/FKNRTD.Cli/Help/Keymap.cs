namespace FKNRTD.Help;

/// <summary>One dashboard key, what it does, and when reaching for it is the right move.</summary>
/// <param name="Key">As printed in a legend.</param>
/// <param name="Action">Two or three words for the footer strip.</param>
/// <param name="Detail">A sentence for the help overlay and the portal.</param>
/// <param name="InFooter">Whether the key is common enough to earn space on the always-visible strip.</param>
public sealed record KeyBinding(string Key, string Action, string Detail, bool InFooter = false);

/// <summary>
/// The dashboard's keys, in one table. The footer strip, the in-app help overlay and the generated
/// HTML portal all read from here, so a key cannot exist without being documented.
/// </summary>
public static class Keymap
{
    private static readonly KeyBinding[] Bindings =
    [
        new("↑↓", "select",
            "Moves the highlight through the task list. Every other key acts on the highlighted task.",
            InFooter: true),
        new("Enter", "run",
            "Starts the highlighted task, or restarts it from the first stage that has not passed. " +
            "Nothing is merged; the work happens in the task's own worktree.",
            InFooter: true),
        new("N", "new task",
            "Opens the guided task builder. Every field explains itself as you reach it, and every " +
            "field has a working default, so Enter through it produces a valid task.",
            InFooter: true),
        new("L", "logs",
            "Switches between the overview and the live output of the highlighted task's current " +
            "stage. This is where you look when something is taking a long time or has failed.",
            InFooter: true),
        new("I", "inspect",
            "Opens the full record of the highlighted task: every stage and its outcome, the agents " +
            "assigned to it, where its worktree is on disk, and what to do next."),
        new("R", "retry",
            "Resets the highlighted task's failed stages and runs it again from there. It does not " +
            "start over from the beginning, and it does not discard the work already done."),
        new("C", "cancel",
            "Asks the highlighted task to stop. A running stage finishes its current external " +
            "process first, so cancelling is not instant. Whatever was written stays in the worktree."),
        new("G", "land",
            "Merges the highlighted task into its base branch, after asking you to type LAND in " +
            "full. Only offered for a task that verified and passed its audit."),
        new("X", "clean up",
            "Removes the highlighted task's worktree directory after asking you to type REMOVE. The " +
            "task record, its logs and its Git branch are all kept."),
        new("M", "message",
            "Records a note from one agent to another on the message bus."),
        new("U", "usage",
            "Refreshes Codex's rate-limit figures. Claude's arrive on their own once the statusline " +
            "integration is installed."),
        new("D", "doctor",
            "Runs the pre-flight checks and shows what is ready and what would fail, without leaving " +
            "the dashboard."),
        new("A", "agents",
            "Lists the configured agents: which are enabled, which can actually be found on PATH, " +
            "and which are equipped to act as an auditor."),
        new("/", "commands",
            "Opens the command palette: every action the dashboard can take, searchable by name, " +
            "with the reason stated for any that cannot be taken right now.",
            InFooter: true),
        new("?", "help",
            "Opens the key reference and the searchable glossary of every term the product uses.",
            InFooter: true),
        new("Tab", "switch view",
            "Cycles between the overview and the log view."),
        new("Q", "quit",
            "Leaves the dashboard and restores the terminal. Running tasks are cancelled first; " +
            "nothing is merged and nothing is lost.",
            InFooter: true),
        new("Esc", "back",
            "Closes whatever is open. From the overview it quits, the same as Q.")
    ];

    public static IReadOnlyList<KeyBinding> All => Bindings;

    /// <summary>The subset earning space on the always-visible strip, in the order drawn.</summary>
    public static (string Key, string Meaning)[] Footer { get; } =
        Bindings.Where(binding => binding.InFooter)
            .Select(binding => (binding.Key, binding.Action))
            .ToArray();
}
