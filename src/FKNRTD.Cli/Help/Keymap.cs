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
            "Moves the highlight through the task list. The keys that act on a task — Enter, I, L, " +
            "R, C, G, X — all act on the highlighted one.",
            InFooter: true),
        new("Enter", "run",
            "Starts the highlighted task, or resumes it from the first stage that has not passed. " +
            "Nothing is merged. In a Git workspace the work happens in the task's own worktree; in a " +
            "standalone one it happens in this folder.",
            InFooter: true),
        new("N", "new task",
            "Opens the guided task builder. Every field explains itself as you reach it. The title " +
            "and the brief are yours to write; everything after them is already filled in for this " +
            "workspace, so the rest of the form is Enter.",
            InFooter: true),
        new("L", "logs",
            "Switches between the overview and the live output of the highlighted task's current " +
            "stage. This is where you look when something is taking a long time or has failed.",
            InFooter: true),
        new("F", "find a task",
            "Searches every task in the workspace by title, status or id and selects the one you " +
            "pick. The overview shows a handful of rows at a time, which stops being a way to find " +
            "anything once a workspace has a history."),
        new("V", "view the change",
            "Shows the finished diff for the highlighted task — everything it committed on top of " +
            "the base branch, plus anything still uncommitted in its worktree — searchable by file " +
            "or by any text in it. This is the reading that landing asks you to have done.",
            InFooter: true),
        new("I", "inspect",
            "Opens the full record of the highlighted task: every stage and its outcome, the agents " +
            "assigned to it, where its worktree is on disk, and what to do next."),
        new("R", "retry",
            "Resets the highlighted task's failed stages so it can run again, then leaves it queued " +
            "— press Enter to actually start it. It does not discard the work already done, and a " +
            "resumed run picks up at the first stage that has not passed."),
        new("C", "cancel",
            "Asks the highlighted task to stop. The request is noticed within about half a second " +
            "and the running agent's process tree is killed, so cancelling is quick but not " +
            "instantaneous. Whatever the agent had already written stays where it wrote it."),
        new("G", "land",
            "Finishes the highlighted task, after asking you to type LAND in full. In a Git " +
            "workspace that merges its branch into the base branch; in a standalone one it records " +
            "that the verified work already in the folder is final. Only offered for a task that " +
            "verified and passed its audit."),
        new("X", "clean up",
            "Removes the highlighted task's worktree directory after asking you to type REMOVE. The " +
            "task record, its logs and its Git branch are all kept."),
        new("M", "message",
            "Records a note from one agent to another on the message bus."),
        new("U", "budget",
            "Asks Codex for its current rate-limit figures, then shows every agent's remaining " +
            "context and window budget with what each window means and why one might be blank."),
        new("D", "doctor",
            "Runs the pre-flight checks and shows what is ready and what would fail, without leaving " +
            "the dashboard."),
        new("A", "agents",
            "Lists the configured agents: which are enabled, which can actually be found on PATH, " +
            "and which are equipped to act as an auditor."),
        new("S", "settings",
            "Explains every setting in this workspace's configuration: what it controls and what " +
            "changing it would cost, with the current value shown for each top-level one. The file " +
            "is plain JSON meant to be edited by hand; this is the explanation that was missing."),
        new("K", "coordination",
            "Shows what the agents have reserved and where they overlap right now: every live file " +
            "claim, every conflict the sentinel currently sees, and the message bus. Claims warn; " +
            "they do not block, so an overlap here is something for you to act on rather than " +
            "something the pipeline is already waiting out."),
        new("E", "events",
            "Opens the workspace history — the most recent five hundred recorded events, newest " +
            "first and searchable: tasks created, stages that passed or failed, landings, agent " +
            "check-ins. This is where you look when you want to know what actually happened rather " +
            "than what the current state implies."),
        new("/", "commands",
            "Opens the command palette: every action the dashboard can take, searchable by name, " +
            "with the reason stated for any it can tell is unavailable. Moving around — the arrow " +
            "keys, Tab, and scrolling the log — is left out, because those are ways to navigate " +
            "rather than things to do. ':' does the same, for layouts where that is the same " +
            "physical key.",
            InFooter: true),
        new("?", "help",
            "Opens the key reference and the searchable glossary of every term the product uses.",
            InFooter: true),
        new("PgUp PgDn", "scroll the log",
            "In the log view, moves ten lines back or forward through the output. A failure is " +
            "often explained a long way above the last line, so the tail alone is rarely enough."),
        new("Home End", "jump in the log",
            "In the log view, Home goes to the first line of the stage's output and End returns to " +
            "following the live tail."),
        new("Tab", "switch view",
            "Cycles between the overview and the log view."),
        new("Q", "quit",
            "Leaves the dashboard and restores the terminal. Running tasks are cancelled first; " +
            "nothing is merged and nothing is lost.",
            InFooter: true),
        new("Esc", "back",
            "Closes whatever overlay is open. With nothing open it quits, the same as Q, from the " +
            "log view as well as the overview.")
    ];

    public static IReadOnlyList<KeyBinding> All => Bindings;

    /// <summary>The subset earning space on the always-visible strip, in the order drawn.</summary>
    public static (string Key, string Meaning)[] Footer { get; } =
        Bindings.Where(binding => binding.InFooter)
            .Select(binding => (binding.Key, binding.Action))
            .ToArray();
}
