namespace FKNRTD.Help;

/// <summary>One dashboard key, what it does, and when reaching for it is the right move.</summary>
/// <param name="Key">As printed in a legend.</param>
/// <param name="Action">Two or three words for the footer strip.</param>
/// <param name="Detail">A sentence for the help overlay and the portal.</param>
/// <param name="InFooter">Whether the key is common enough to earn space on the always-visible strip.</param>
/// <param name="Essential">
/// Whether the key must survive on a terminal too narrow for the whole strip. Quit and help are the
/// two an operator needs most when they are lost, and they were the first to be truncated away.
/// </param>
public sealed record KeyBinding(
    string Key,
    string Action,
    string Detail,
    bool InFooter = false,
    bool Essential = false);

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
            "stage. This is where you look when something is taking a long time or has failed. The " +
            "shipped agents write a stream of JSON so their progress can be followed; what is shown " +
            "here is a reading of it - what the agent said, the tools it used and the files it " +
            "touched - with anything unrecognised left exactly as it arrived. The file on disk is " +
            "always the raw output.",
            InFooter: true),
        new("F", "find a task",
            "Searches every task in the workspace by title, status or id and selects the one you " +
            "pick. The overview shows a handful of rows at a time, which stops being a way to find " +
            "anything once a workspace has a history."),
        new("V", "view the change",
            "Shows the finished diff for the highlighted task — what it committed on top of the " +
            "base branch, plus anything still uncommitted in its worktree — searchable by file or " +
            "by any text in it. The uncommitted half is 'git diff HEAD', so a file the agent created " +
            "and never staged does not appear here; run 'git status' in the worktree to catch those. " +
            "This is the reading that landing asks you to have done.",
            InFooter: true),
        new("P", "prompts",
            "Shows the text each of the three agents will be sent for this task, composed from your " +
            "brief. The lead's is exactly what it will receive. The other two carry a placeholder " +
            "wherever a real run pastes something that does not exist yet — the lead's plan before " +
            "the plan stage has run, and always the verification results in the auditor's. A repair " +
            "round adds the failure evidence too. Knowing what an agent is about to be told is the " +
            "part of authorising it that no amount of sandboxing substitutes for."),
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
            "task record and its logs are kept. Its branch is kept too, unless the task has already " +
            "landed — a landed branch is deleted, which 'git branch -d' will only do once it is " +
            "merged."),
        new("M", "message",
            "Records a note from one agent to another on the message bus."),
        new("U", "budget",
            "Asks Codex for its current rate-limit figures, then shows every agent's remaining " +
            "context and window budget with what each window means and why one might be blank. R " +
            "asks again without leaving the panel. The figures are reported to FKNRTD.CLI rather " +
            "than enforced by it: running low constrains what you should start, and nothing here " +
            "will stop a task."),
        new("D", "doctor",
            "Runs the pre-flight checks and shows what is ready and what would fail, without leaving " +
            "the dashboard."),
        new("A", "manage agents",
            "Lists the configured agents - which are enabled, which can actually be found on PATH, " +
            "and which are equipped to act as an auditor - and lets you change the roster: Space " +
            "enables or disables the highlighted one, N adds another, Del removes one after you " +
            "type the word REMOVE in full, and F1 opens the full detail for every agent: where its " +
            "executable actually is, and which command profiles it has."),
        new("S", "settings",
            "Every setting in this workspace's configuration with its current value, what it " +
            "controls, and what changing it would cost. Enter changes the highlighted one through " +
            "the same guided form the rest of the product uses, and writes the file. A few are " +
            "shown with the reason they cannot be changed from here rather than being left out. F1 " +
            "opens the full reference, including the nested agent fields."),
        new("K", "coordination",
            "Shows what the agents have reserved and where they overlap right now: every live file " +
            "claim, every conflict the sentinel currently sees, and the message bus. Claims warn; " +
            "they do not block, so an overlap here is something for you to act on rather than " +
            "something the pipeline is already waiting out. An expired claim is not removed by " +
            "expiring - it keeps being reported as stale until somebody clears it, which R does " +
            "for all of them at once."),
        new("E", "events",
            "Opens the workspace history — the most recent five hundred recorded events, newest " +
            "first and searchable: the workspace being set up, tasks created and cancelled, " +
            "workflows started, repaired, failed, made ready and landed, and messages sent. Stage " +
            "detail is not here; press I on a task for that. This is where you look when you want " +
            "the sequence of what happened rather than the state it left behind."),
        new("/", "commands",
            "Opens the command palette: every action the dashboard can take, searchable by name, " +
            "with the reason stated for any it can tell is unavailable. Moving around — the arrow " +
            "keys, Tab, and scrolling the log — is left out, because those are ways to navigate " +
            "rather than things to do. ':' does the same, for layouts where that is the same " +
            "physical key.",
            InFooter: true),
        new("?", "help",
            "Opens the key reference and the searchable glossary of every term the product uses. " +
            "Type to search it. F2 writes the whole thing out beside the workspace as a single " +
            "self-contained web page - the same document 'fknrtd portal' produces - so the " +
            "explanation is something you can keep rather than something that exists only while " +
            "the dashboard is open.",
            InFooter: true, Essential: true),
        new("PgUp PgDn", "scroll the log",
            "In the log view, moves ten lines back or forward through the output. A failure is " +
            "often explained a long way above the last line, so the tail alone is rarely enough."),
        new("Home End", "jump in the log",
            "In the log view, Home goes to the first line of the stage's output and End returns to " +
            "following the live tail."),
        new("Tab", "switch view",
            "Cycles between the overview and the log view."),
        new("Q", "quit",
            "Leaves the dashboard and restores the terminal. Leaving cancels the session, which " +
            "kills each running agent where it stands, so with anything running it asks first. " +
            "Whatever an agent had already written to disk stays there and nothing is merged; the " +
            "task is recorded as cancelled and R resets it to run again. Escape does the same as Q " +
            "when no panel is open.",
            InFooter: true, Essential: true),
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

    /// <summary>
    /// The footer entries that must survive a terminal too narrow for all of them, drawn pinned to
    /// the right while the rest fill the space that remains.
    /// </summary>
    public static (string Key, string Meaning)[] EssentialFooter { get; } =
        Bindings.Where(binding => binding.InFooter && binding.Essential)
            .Select(binding => (binding.Key, binding.Action))
            .ToArray();

    /// <summary>The rest, dropped from the end when there is not room.</summary>
    public static (string Key, string Meaning)[] OptionalFooter { get; } =
        Bindings.Where(binding => binding.InFooter && !binding.Essential)
            .Select(binding => (binding.Key, binding.Action))
            .ToArray();
}
