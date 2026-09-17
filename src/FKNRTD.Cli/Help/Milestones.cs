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

        new("2026-09-17",
            "The log view stopped showing JSON",
            "The shipped agents are launched with machine-readable output so that their progress " +
            "can be followed, and the stage log is their stdout exactly as it arrived. Pressing L " +
            "on a running task therefore showed a wall of JSON, one object per line, in which the " +
            "sentence the agent had just written was a quoted field somewhere past column ninety.",
            "The log view now reads those bytes rather than printing them: what the agent said, " +
            "the tools it used and the file or command each one was about, its errors, and its " +
            "final answer. Anything unrecognised is shown exactly as it arrived, so an agent whose " +
            "format nobody has taught it about is no worse off than before, and plain build output " +
            "keeps its indentation because a failing assertion lines its values up under each other.",
            "Nothing on disk changed. The raw stream is still the file, which is what you want when " +
            "you are debugging the agent rather than the work. The reason this went unnoticed so " +
            "long is that the log panel had never been rendered with a log in it - the scene set " +
            "covered the empty case only, so the entire drawing path was exercised nowhere."),

        new("2026-09-17",
            "The guide started showing the screens",
            "This page explained every key and every term in words and never showed anybody a " +
            "screen, which is a strange way to document a product whose whole problem was that " +
            "people could not tell what it was asking them for.",
            "Four real frames are drawn into it when it is generated - the command center, the task " +
            "builder, the agent roster and the settings screen - by the same renderer the program " +
            "runs, from a small example workspace.",
            "A picture in a manual is the first thing to go stale, and one produced from the same " +
            "code as the thing it depicts cannot. The self-test checks each embedded frame is a " +
            "whole frame with every row the same width and no escape sequences in it, which is the " +
            "same check the renderer's own suite makes - if a picture here ever stops matching, it " +
            "is because the screen changed and the page was not regenerated."),

        new("2026-09-17",
            "Asking before killing the work",
            "Q set a flag, the loop exited, and the shutdown path cancelled the session - which " +
            "kills every running agent's process tree. That is a great deal to do for one " +
            "unconfirmed keystroke from somebody who may only have meant to put this away for a " +
            "minute, and Escape was bound to the same action.",
            "With nothing running it still leaves at once. With work in flight it asks, staying is " +
            "the recommended answer, and the panel says what leaving would do: each agent killed " +
            "where it stands, whatever it wrote left on disk, the task recorded as cancelled and " +
            "resettable with R.",
            "The guard is only worth having where there is something to lose. A confirmation on an " +
            "idle dashboard would be the kind of prompt people learn to dismiss without reading, " +
            "which is how a confirmation stops protecting anything."),

        new("2026-09-17",
            "The situations nobody had drawn",
            "Asking what the scene set never renders turned out to be the most productive question " +
            "of the day. A standalone workspace appeared in one diff panel and nowhere else. The " +
            "log panel had only ever been rendered empty. The roster had never been drawn with " +
            "nothing installed, because what is on the machine running the tests is not something " +
            "a test can arrange.",
            "All three are rendered now, along with a task naming an agent that was removed, a " +
            "confirmation for a task that has already landed, and a workspace with no Git at all. " +
            "The sweep grew from thirty-seven scenes to forty-eight.",
            "Every one of them was wrong. Three pieces of advice told standalone operators to clean " +
            "up a worktree they do not have; the log panel was showing raw JSON; the roster could " +
            "report a missing executable and not fix it. A screen that is never drawn is a screen " +
            "nobody has read, and the tests passing said nothing about it either way."),

        new("2026-09-17",
            "The same wrong sentence in three places",
            "A claim found false and corrected in one place kept turning up standing in another. " +
            "The promise that cleanup keeps a task's branch survived in the dialog that asks " +
            "permission to delete that branch, and again in the shell command's refusal message. " +
            "The promise that every field of the task builder has a default survived in the README " +
            "and again in the changelog.",
            "All of them are corrected, and a list of retired claims is checked against the " +
            "generated guide and against every rendered scene, so a sentence that has been " +
            "retired cannot quietly come back somewhere else.",
            "Each copy read perfectly well on its own, which is exactly why reading them did not " +
            "help. It is the same argument as the tables that drive every explanation in this " +
            "product: a fact stated in one place can be corrected, and a fact stated in four " +
            "places will be corrected in one of them."),

        new("2026-09-17",
            "Taking a workspace apart on purpose",
            "Nothing in the suite had ever broken a workspace, which made the error paths the " +
            "least-exercised part of the product. Four were tried by hand.",
            "Three were wrong. A task file that would not parse made the task vanish, and the shell " +
            "then reported the workspace empty - a wrong answer, not an unhelpful one, and the kind " +
            "that sends somebody off to write the task again. A broken config.json produced the " +
            "JSON parser's own message, which names no file and no remedy, on one unwrapped line. A " +
            "task whose worktree had been deleted was told it had not reached its worktree stage.",
            "The fourth - a torn last line in the append-only event log - was already handled " +
            "correctly, which is what that reader exists for. The pattern in the other three is the " +
            "same one this whole programme is about: the product knew something and said nothing, " +
            "or said something that was true of a different situation. Breaking things on purpose " +
            "found in an hour what reading the code had not found all day."),

        new("2026-09-17",
            "Asking the newcomer's question mechanically",
            "Every explanation in this product is reachable, and nobody had checked whether the " +
            "words on the screen are the words somebody would look up. Those are different " +
            "questions, and only the second one is the reader's.",
            "Every word the main screen draws was taken and looked up. Seven had no answer at all: " +
            "the progress bar, the resource line, the ahead-and-behind arrows, the changed count, " +
            "and three panel names, two of which resolved to an unrelated entry about telemetry.",
            "Four of those seven are in the header, which is the first thing anybody sees and the " +
            "part they see before they have done anything. The check runs on every build now. It " +
            "covers the main screen only, which is a finding rather than a shortcut: run across " +
            "every screen it returns ordinary English words, because the explanatory panels are " +
            "prose and prose is not a vocabulary."),

        new("2026-09-17",
            "Measuring what had only ever been assumed",
            "The dashboard takes a snapshot and draws a frame once a second, and neither had been " +
            "timed. 'Once a second' was a number chosen rather than a number justified, and the " +
            "advice written on the setting that controls it was written without knowing what it " +
            "was advising about.",
            "A snapshot of a standalone workspace takes 38 milliseconds and a frame takes two. A " +
            "Git-backed snapshot took 307, almost all of it starting Git processes - so the four " +
            "unrelated questions it asks Git are now asked at once rather than one after another, " +
            "which brought it to 236.",
            "The setting carries those numbers and what they imply, because that is what somebody " +
            "lowering it wants to know beforehand. The remaining sequential call was left alone " +
            "deliberately: removing it would change what 'git status' reports for a workspace whose " +
            "root is not the repository root, in exchange for eighty milliseconds of a thousand."),

        new("2026-09-17",
            "Showing the log without reading all of it",
            "The log view built a string for every line in the file to display the last twenty-five " +
            "of them, once a second. Against a log of the size a long task really produces that is " +
            "58 MB of allocation per refresh, on the one screen somebody sits and watches while " +
            "they wait.",
            "It scans bytes to find where the window starts and seeks there, so no line before the " +
            "window is ever built: 53 milliseconds and 58 MB per read became 27 milliseconds and " +
            "nothing. The first attempt only reached 56 MB, because discarding a line is not " +
            "cheaper than keeping it - the string is built either way.",
            "Counting the lines still reads the whole file, and that stays, because the footer says " +
            "'of 120000' and there is no way to know that without looking. What changed is that it " +
            "now looks without building anything."),

        new("2026-09-17",
            "A mistyped option was worse than a mistyped command",
            "A mistyped command has always been caught and corrected. A mistyped option was " +
            "discarded in silence: each command reads the options it recognises and nothing ever " +
            "looked at the rest.",
            "'fknrtd task list -jsno' printed a human table and exited 0 - a script that asked for " +
            "JSON, one letter off, being told everything went well. Every command line is now " +
            "checked against the options its own help page documents, and a near miss is named " +
            "with the option meant.",
            "The check fails open, and the interesting part was where that mattered. Refusing " +
            "anything undocumented would have rejected 'task create -title' and 'task show -id', " +
            "which have always worked: the catalog lists those without a dash because they are " +
            "usually positional, while the commands accept either form. A sweep of every command " +
            "found it; the self-tests did not, because nothing documented used that form."),

        new("2026-09-17",
            "A task that cannot be created no longer costs you the brief",
            "Creating a task is attempted after the form closes, and it can fail for a reason the " +
            "form could not have known - a base branch that does not exist is the ordinary one, " +
            "because only Git can say whether a branch is there.",
            "That used to leave a toast reading 'Could not create the task' and nothing else. The " +
            "title, the brief somebody had spent several minutes on, and up to eight other " +
            "answers were gone, and the only way forward was to press N and type all of it again. " +
            "None of those answers was the thing that was wrong.",
            "The form now comes back holding all of them, open on the question the reason names " +
            "rather than on the end of the form: a message that quotes an answer is matched to the " +
            "step that holds it, so a mistyped branch reopens on the branch question with the " +
            "value still in the field. Only a quoted answer counts, or a title of 'main' would " +
            "claim an error about a branch."),

        new("2026-09-17",
            "Two commands disagreeing about whether a workspace works",
            "The configuration is plain JSON, meant to be edited by hand, and editing it by hand " +
            "walks straight past the checking the settings screen does. Nothing else looked at the " +
            "result except two rules written out longhand in 'config validate', which knew about " +
            "two of the fifteen fields.",
            "A workspace with dashboardRefreshMilliseconds of 0 and maxParallelAgents of 0 - one " +
            "where nothing can ever run and the dashboard would spin - opened without comment. " +
            "'config validate' refused it. 'doctor' called it healthy, and doctor is the command " +
            "people are told to run.",
            "Both now ask each setting about the value it is holding, using the rule that setting's " +
            "own editor applies, so there is one answer to what is valid rather than three and a " +
            "setting added later cannot be left behind. Two more faults became visible immediately: " +
            "a negative repair budget and a zero agent timeout, neither of which anything had ever " +
            "checked. The complaint carries the current value, because a rule that says what a good " +
            "value would be still leaves the reader to go and look up the bad one."),

        new("2026-09-17",
            "Answering the question instead of refusing the word",
            "Looking up a word that is not a glossary term ran a search instead, which was the " +
            "right decision and had been made deliberately - the comment above the line said so. " +
            "The line itself opened with 'No term is called', and only then listed the entries " +
            "that answered the question.",
            "The words that reach that path are mostly words the product had just drawn: BUDGET, " +
            "STAGE and EVENTS are panel headings, and none is a term on its own. Somebody reading " +
            "a heading off the screen and asking about it was told first that it did not exist, " +
            "above seven entries that between them explained it completely.",
            "It now leads with the answer - \"'budget' appears in 7 entries\" - and says when the " +
            "list is longer than the page, because claiming 33 and printing 8 reads as a miscount. " +
            "A word that matches nothing is still refused and still exits 2, since a script that " +
            "looked something up and found nothing has to be able to tell."),

        new("2026-09-17",
            "Six steps that read as nine",
            "The first screen a new workspace opens explains how work moves through the product as " +
            "six numbered steps. Every line of a paragraph was wrapped to the same column, and the " +
            "column the text starts in is the column the numbers are in.",
            "So the second line of step 2 put 'edit.' hard against the left margin, level with the " +
            "numbers, and step 4 put 'while the repair budget lasts.' there. At a narrow window the " +
            "six-step summary read as eight or nine, several of them fragments - on the screen " +
            "somebody sees before they have done anything at all.",
            "A paragraph that begins with a list marker now indents its continuation lines past it. " +
            "The marker is measured from the text rather than declared at the call site, because a " +
            "hanging indent that has to be passed by hand is one that will be right on the " +
            "paragraphs somebody remembered and wrong on the rest. The test compares each " +
            "continuation against its own step's column, so it never needs to know where the panel " +
            "begins."),

        new("2026-09-17",
            "Telling a window it is too small instead of drawing over it",
            "The dashboard needs sixty columns by twenty rows. A smaller window had its size " +
            "clamped up to that and was drawn into anyway, which is a decision made once and never " +
            "looked at again.",
            "A forty-column terminal therefore received sixty columns of panel furniture. Every row " +
            "wrapped, every border landed in the middle of a sentence, and nothing anywhere said " +
            "what was wrong - even though the fix was entirely in the reader's hands and took one " +
            "drag of a window edge.",
            "It now says what it needs, what the window is, and that making it bigger redraws by " +
            "itself. The notice fits whatever space exists rather than having a minimum of its own, " +
            "because a message about the window being too small that is itself too big to read " +
            "would be the same fault twice; at very few columns it keeps the headline and drops the " +
            "rest. The renderer's invariant got simpler rather than weaker - a frame is now exactly " +
            "the size it was asked for at every size, with no point where the frame and the window " +
            "disagree, and all 11,660 fuzzed renders hold it."),
    ];

    public static IReadOnlyList<Milestone> All => Entries;
}
