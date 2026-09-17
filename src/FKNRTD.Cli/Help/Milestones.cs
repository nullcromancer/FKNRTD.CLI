namespace FKNRTD.Help;

/// <summary>One step in how the product came to work the way it does.</summary>
/// <param name="Date">ISO date, so the portal never has to format a clock.</param>
/// <param name="Title">What was built.</param>
/// <param name="Problem">The thing that was wrong, stated as an operator would have hit it.</param>
/// <param name="Change">What was actually done about it.</param>
/// <param name="Why">The reasoning, including what was deliberately not done.</param>
public sealed record Milestone(string Date, string Title, string Problem, string Change, string Why);

/// <summary>
/// The build log. It exists because the parts of this product only make sense together: the glossary
/// is why the task builder can explain itself, the keymap is why the palette cannot offer a key that
/// does not exist, and both are why the guide you are reading cannot drift from the program. A list
/// of features would not show any of that; a record of what each piece was for does.
/// </summary>
public static class Milestones
{
    private static readonly Milestone[] Entries =
    [
        new("2026-09-17",
            "One explanation table behind every surface",
            "Pressing N in the dashboard asked for a \"brief\", a \"lead\", an \"implementer\" and an " +
            "\"auditor\". None of those words was defined anywhere in the product, so the first " +
            "screen an operator met was one they could not answer.",
            "Every concept the product exposes — each task field, each role, each of the eight " +
            "stages, each status and state marker — became a row in one table, carrying a one-line " +
            "summary, a full explanation and a worked example.",
            "The alternative was writing help text into each screen that needed it, which is how " +
            "documentation ends up disagreeing with itself. Because the dashboard's inline hints, " +
            "the in-app reference, `fknrtd explain` and this page all read the same rows, an " +
            "explanation cannot drift from the code that uses it. A self-test fails if any marker " +
            "the dashboard can draw has no entry."),

        new("2026-09-17",
            "Questions that stay on screen and explain themselves",
            "Answering anything meant dropping out of the dashboard to a bare prompt on an empty " +
            "terminal. Six of them in a row for a new task, with no defaults shown, no way back, " +
            "and no indication of what a wrong answer would cost.",
            "A modal layer that draws on the same canvas as the dashboard: a raised panel over a " +
            "dimmed but still readable frame. Text fields became real in-screen editors with a " +
            "caret, word motion and wrapped multi-line input. Forms became steps that each name a " +
            "glossary term, so the question arrives with its own definition and example.",
            "Keeping the dashboard visible behind a question means the operator can still see the " +
            "task they are acting on while they answer it. Every field has a working default, so " +
            "pressing Enter through the form produces a valid task; a refused answer says what was " +
            "wrong with it, and going back keeps what was already typed."),

        new("2026-09-17",
            "Consequences stated before confirmation is asked for",
            "Landing a task asked for the word LAND with no statement of what was about to be " +
            "merged into what, and no way to see the diff first.",
            "Destructive actions open a panel that names the task, states the consequence in full, " +
            "and says where the finished work is if you want to read it before deciding — then " +
            "takes the typed word.",
            "The typed word was already there and is kept: a stray keystroke should not be able to " +
            "merge a branch. What was missing was the sentence above it. A confirmation whose " +
            "explanation is cut off mid-sentence is asking someone to agree to something it did " +
            "not finish telling them, so these panels size themselves to their text."),

        new("2026-09-17",
            "Reference surfaces, and a first screen that teaches",
            "The dashboard could show state but never explain it. There was no key list, no " +
            "glossary, no way to read a task's full record, and a brand new workspace opened on an " +
            "empty grid that taught nothing about what the tool was for.",
            "One scrollable, filterable panel, and four surfaces built on it: the key and glossary " +
            "reference, a task's full record with every stage explained, the pre-flight checks, and " +
            "the agent roster. A workspace with no tasks opens on an introduction that walks the " +
            "pipeline and names the three keys worth pressing first.",
            "Building them from one widget means they scroll, search and close identically, so " +
            "learning one teaches all four. The key reference and the glossary are searched " +
            "together, because someone who does not know a word also does not know which list it " +
            "is in."),

        new("2026-09-17",
            "Actions that are discoverable, and refusals that explain",
            "Keyboard shortcuts only help someone who already knows them, and a key that does " +
            "nothing when the task is not ready teaches an operator that the tool is broken.",
            "A command palette lists every action, filtered by typing part of its name. Actions " +
            "that cannot be taken right now are listed anyway, marked, and accompanied by the " +
            "reason — \"it is Running. Only a verified, audited task can be landed\".",
            "Keys and palette entries route through one handler keyed by the same identifier, so " +
            "they cannot drift apart and every action has exactly one implementation. " +
            "Unavailability is marked in text rather than only in colour, so it survives a " +
            "monochrome terminal and redirected output."),

        new("2026-09-17",
            "A command line that explains itself too",
            "Help was a hand-maintained block of text free to drift from the dispatcher, there was " +
            "no way to look a word up, and a mistyped command reported a missing workspace instead " +
            "of the typo.",
            "Every command became a row in a second table, with what it changes on disk, its " +
            "options, examples and the single most useful next step. `fknrtd help` renders from it, " +
            "`fknrtd explain` renders the glossary, and `fknrtd task new` runs the dashboard's own " +
            "guided form outside the dashboard.",
            "A mistyped command is now recognised before a workspace is located, so `fknrtd taks` " +
            "outside a project reports the typo rather than sending the operator to fix the wrong " +
            "problem. Suggestions use an edit distance that counts a transposition as one change, " +
            "because swapping two letters is the mistake people actually make."),

        new("2026-09-17",
            "Setup that asks about the decisions that matter",
            "Setting a workspace up chose the mode and guessed the verification commands silently, " +
            "then printed what it had decided. Those two settings determine whether an agent can " +
            "damage anything, and the operator met them as a summary they had no reason to read.",
            "Interactive setup walks five explained steps and ends by running the pre-flight " +
            "checks, saying in one line whether the workspace is ready or what to fix.",
            "Scripts pass -yes for the previous detection-only behaviour. Backing out of setup " +
            "leaves the folder untouched rather than half-configured."),

        new("2026-09-17",
            "The configuration file, explained",
            "`.fknrtd/config.json` is plain JSON meant to be edited by hand, and nothing in the " +
            "product said what any of it meant. An operator who opened it met agentStaleAfterSeconds " +
            "and requireCleanTreeForLanding with no way to find out what they controlled.",
            "Every settable field became a row in a third table, carrying what it controls, its " +
            "default, and what changing it actually costs. It reaches the dashboard showing the " +
            "value this workspace is really running, `fknrtd explain`, and this page.",
            "The consequence is the field that earns the file's existence. \"Raising this past two " +
            "rarely converges and burns rate-limit budget\" is usable; a restatement of the setting's " +
            "own name is not. A self-test walks the configuration record by reflection, so the next " +
            "setting added cannot arrive unexplained."),

        new("2026-09-17",
            "Errors that say what to do about them",
            "Thirty-eight thrown messages across the core stated a fact and stopped. \"No enabled " +
            "agents are configured.\" \"Unable to create the isolated worktree.\" Each was accurate " +
            "and each left the operator holding a correct sentence with nothing to do next.",
            "Every one now says what happened, why it matters, and the next action, naming an exact " +
            "command or file. Where a failure leaves work somewhere, the message says where.",
            "Strings only — no condition, exception type or signature changed, so the diff is " +
            "reviewable in one pass. An agent that crashes mid-implement has still written to the " +
            "task's worktree and not to your checkout, and that is worth knowing before you go " +
            "looking for it."),

        new("2026-09-17",
            "Glyphs the terminal can actually draw",
            "Every character the dashboard draws was measured against Cascadia Mono, Consolas and " +
            "Lucida Console. Four were missing from all three and rendered as empty boxes — one of " +
            "them the failure marker, so on a default Windows Terminal every failure in this product " +
            "was a blank rectangle.",
            "Replaced with characters all three fonts carry, chosen to keep each marker distinct " +
            "from its neighbours. Two had no universal equivalent that still meant anything, so the " +
            "header says \"on main\" rather than drawing a branch glyph nobody recognises.",
            "A design that only renders on the machine it was built on is not a design. The " +
            "documentation captures are now generated from the real renderer for the same reason " +
            "this page is, so a screenshot cannot go on showing a keymap the product no longer has."),

        new("2026-09-17",
            "Showing the diff, instead of naming a directory",
            "Every surface said to read the change before landing it — the confirmation, the task " +
            "record, the next-step hint — and not one of them would show it. They gave a filesystem " +
            "path, which for most people means not looking.",
            "V in the dashboard and `fknrtd task diff` print the finished change: everything the " +
            "task committed on top of its base branch, plus anything still uncommitted in its " +
            "worktree. Searchable by file or by any text in it.",
            "Both halves matter. A workspace that does not commit agent changes automatically has " +
            "the entire change sitting uncommitted, and showing only the committed half would report " +
            "an empty diff for work that is plainly there. Lines render verbatim rather than " +
            "wrapped, because a wrap that moves a leading plus or minus off the start of a row turns " +
            "an addition into a removal at a glance."),

        new("2026-09-17",
            "Showing what the agents are told",
            "The product asks you to authorise agents against your code, and the one thing it would " +
            "not show was the instruction each agent receives. The prompts were written inline in " +
            "the orchestrator, where nothing outside a live run could see them.",
            "They are composed as pure functions of the task now, so the same text that reaches the " +
            "agent can be previewed beforehand. P and `fknrtd task prompts` print all three, " +
            "searchable.",
            "This is the part of authorising an agent that no amount of sandboxing substitutes for: " +
            "a read-only profile constrains what an agent can do, not what it has been asked to do. " +
            "The extraction changed no behaviour — the end-to-end tests that run a real task through " +
            "all eight stages passed unchanged, which is what made it safe to do at all."),

        new("2026-09-17",
            "Checking the things that only fail later",
            "Doctor verified that agents could be launched but not that any of them could finish a " +
            "task. A workspace with no agent able to return a verdict looked completely healthy " +
            "right up until the first task was refused at creation.",
            "Two checks added: that some enabled agent can audit, and — as advice rather than an " +
            "error — that new tasks will have something verifying them.",
            "Writing the first one I reached for a non-null assertion and would have thrown on a " +
            "workspace whose configuration cannot be read, which is precisely when doctor is most " +
            "needed and precisely what its contract forbids. A test now runs the whole diagnostic " +
            "against a workspace containing unparseable JSON."),

        new("2026-09-17",
            "A second pair of eyes, which is the whole argument",
            "This work was written by one agent. The product's entire premise is that the one who " +
            "wrote the change is not the one who should decide it is good.",
            "A second agent reviewed the overlay layer read-only and returned twenty ranked " +
            "findings. Several were real, and the worst was a text wrap that never terminated when " +
            "a glyph was wider than the field it was drawn into — it appended empty lines until the " +
            "process ran out of memory.",
            "That bug was actually found by the regression test written for a different finding, " +
            "which is the argument for writing the test rather than fixing and moving on. The " +
            "review found what the author could not, on exactly the reasoning this tool exists to " +
            "enforce."),

        new("2026-09-17",
            "Three answers, not nine",
            "Creating a task walked nine questions even when every answer after the second was " +
            "already correct for the workspace. Each was one keystroke, but nine of them is still a " +
            "form, and a form is what stops people creating small tasks.",
            "After the brief it asks once whether anything else needs changing, and the option that " +
            "accepts the defaults spells out what they are — who plans, who implements, who audits, " +
            "and what verifies it. Choosing to look walks the same five questions as before.",
            "Accepting defaults blind would be the exact thing the form exists to prevent, which is " +
            "why the summary is on the option rather than behind it. Two real bugs surfaced doing " +
            "this: a step skipped because its default was right received no value at all, so the " +
            "quick path would have created a task with no lead agent; and choice steps sized their " +
            "visible rows with a constant that predated content-sized panels, hiding the second " +
            "option — which on a two-option step hides that there was a choice."),

        new("2026-09-17",
            "This page",
            "The only written guide was the repository's README, which nobody reads from the " +
            "machine they are working on, and which is free to describe a command that no longer " +
            "exists.",
            "`fknrtd portal` generates this document from the same tables the running program " +
            "reads: the glossary, the command catalog, the keymap and this log.",
            "It is one self-contained file with no external references, so it opens correctly with " +
            "no network from wherever you keep it. It is deterministic — the same version produces " +
            "the same bytes — so it can be committed and reviewed like anything else. A self-test " +
            "checks that every term, command and key reaches the page, and that nothing supplied " +
            "to it can become markup."),

        new("2026-09-17",
            "A roster you can act on",
            "A was the screen an operator reached for to find out why an agent was not being " +
            "offered to them. It answered with a list, and then left them to leave the dashboard " +
            "and remember `fknrtd agent enable`, or to hand-edit config.json.",
            "The agent list became the agent roster: Space enables or disables the highlighted " +
            "agent, N adds one through the builder that already existed, and Del removes one " +
            "after confirming by name.",
            "Showing somebody a problem and not the fix is worse than showing them neither, " +
            "because it costs them the trip. The panel also says, before either key is pressed, " +
            "which of the two is reversible: disabling an agent changes nothing already recorded, " +
            "and removing one deletes profiles and arguments that nothing else stores."),

        new("2026-09-17",
            "Settings you can change",
            "S explained every field in config.json and then left the operator to go and edit JSON " +
            "by hand. Knowing what `agentStaleAfterSeconds` means is most of the problem, but it " +
            "is not all of it.",
            "The settings screen lists every top-level field with its live value, and Enter opens " +
            "the same guided form the rest of the product uses to change it. A field this screen " +
            "will not change - the schema version, the workspace mode, the agent roster - is shown " +
            "with the reason, rather than left out.",
            "Two defects surfaced on the way, both of the same kind: a panel sized by a guess " +
            "rather than measured. The settings panel dropped its second explanation block " +
            "entirely on exactly the fields where that block was the whole answer, and the form " +
            "served its worked example before its explanation, starving a long explanation down " +
            "to one clipped line. A step can now carry its own explanation instead of borrowing " +
            "the general term it belongs to."),

        new("2026-09-17",
            "Twenty-six things this page was wrong about",
            "Every explanation in this product reads from one glossary, which makes it the single " +
            "most load-bearing text here and the one nobody could check. Its author wrote it and " +
            "then read it back, which is not a check.",
            "A second model read every entry against the services it describes and returned " +
            "twenty-six errors of fact with a line number for each. Eleven were verified directly " +
            "before anything was changed; all eleven held. Forty-two corrections followed.",
            "The three kinds are worth knowing, because the first is the one that should worry you " +
            "most. Some entries described behaviour no code implements at all: the Waiting status " +
            "is assigned nowhere, and the Blocked agent state promised an event that is never " +
            "recorded. Some turned a default into a guarantee: the lead and auditor were said to " +
            "be unable to write, when what is true is that their shipped profiles ask the tool not " +
            "to. And some were right about the ordinary case and wrong about the dangerous one - " +
            "landing was said to require verification to have passed, when a task with no " +
            "verification commands skips that stage and lands on the audit alone."),

        new("2026-09-17",
            "Clearing what the screen complains about",
            "Correcting the glossary turned up something the screen itself was guilty of. A file " +
            "reservation past its expiry is reported as stale for as long as its record exists, " +
            "and nothing deletes the record - so K listed a complaint that would never go away and " +
            "offered no way to end it.",
            "R on the coordination screen releases every expired reservation, and the panel says " +
            "plainly that expiring is not disappearing. The key only appears when there is " +
            "something to clear.",
            "Two entries on that one screen had been contradicting each other: the claim entry said " +
            "reservations expire on their own, and the stale-claim entry said an expired one is " +
            "reported until somebody releases it. The second was right. A screen that argues with " +
            "itself is worse than a screen that is simply wrong, because the reader cannot tell " +
            "which half to act on."),

        new("2026-09-17",
            "Twenty-one things the command list was wrong about",
            "The same review, turned on the forty-four commands. None of them had ever been " +
            "checked against the dispatcher that implements them.",
            "Twenty-one findings. One was a real defect rather than a wording problem, and is " +
            "recorded separately. The rest were corrections and, more often, omissions: exit " +
            "codes nobody had written down, options that exist and were never listed, and effects " +
            "a command has that its description did not mention.",
            "The omissions were the more interesting half. Eight commands accept -json and not " +
            "one of them said so, which is a failure of repetition rather than of knowledge - so " +
            "the shared options are now appended from a single place, the way -root already was. " +
            "One command advertised -root and ignored it entirely, for the same reason in reverse. " +
            "And three commands quietly replace a whole record when you might expect them to " +
            "update part of one: a telemetry report, a usage measurement and an agent definition " +
            "all discard what you leave out."),

        new("2026-09-17",
            "PgDn after Home needed a hundred million presses",
            "The third fact-check, against the key table and the settings table. It found five " +
            "wrong sentences and one real defect, and the defect had been invisible for the worst " +
            "possible reason: the picture on screen was correct.",
            "Home set the log scroll position to a sentinel a billion lines past the end of the " +
            "file. The frame clamped its own copy of that number, so the log displayed correctly " +
            "from its first line - but PgDn steps back ten lines at a time from the stored " +
            "position, so returning to the live tail would have taken about a hundred million " +
            "presses. Only End recovered. The position is now bounded by the length of the actual " +
            "file, at the keystroke rather than at the frame, because rendering has to stay pure.",
            "A clamp applied where the value is used rather than where it is stored keeps the " +
            "display honest and lets the state rot. PgUp had the same defect in milder form: it " +
            "ran past the end of the file at ten lines a press, so scrolling up past the top and " +
            "then back down did nothing for a while."),

        new("2026-09-17",
            "The messages that never reached the screen",
            "Pressing D set a message saying the pre-flight checks were running, then launched " +
            "every configured agent to see whether it answered. Pressing U said it was asking " +
            "Codex for its rate-limit figures, then shelled out to Codex. Neither message was ever " +
            "drawn: the key handler ran to completion before the loop repainted, so what an " +
            "operator saw was a frozen dashboard and then an answer.",
            "A key that starts slow work repaints before it blocks. The repaint is inert until the " +
            "loop has painted at least once, which keeps it out of the test suite and out of every " +
            "scriptable command's piped output.",
            "A frozen screen and a crashed one look identical, and the product had written the " +
            "reassurance and then thrown it away. This is the same defect as a panel whose measure " +
            "disagrees with its draw, one layer up: the state said one thing and the pixels said " +
            "another, and only the pixels are the product."),
    ];

    public static IReadOnlyList<Milestone> All => Entries;
}
