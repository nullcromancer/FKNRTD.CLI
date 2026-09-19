namespace FKNRTD.Help;

/// <summary>An option or positional argument accepted by a command.</summary>
public sealed record CommandOption(string Name, string ValueHint, string Meaning, bool Required = false);

/// <summary>One command, its effects, and the next useful step for its operator.</summary>
public sealed record CommandEntry(
    string Invocation,
    string Name,
    string Group,
    string Summary,
    string Detail,
    string WhatHappensNext,
    IReadOnlyList<CommandOption> Options,
    IReadOnlyList<string> Examples,
    IReadOnlyList<string> GlossaryTerms);

/// <summary>The command explanation table shared by help surfaces and the offline portal.</summary>
public static class CommandCatalog
{
    public const string GettingStarted = "Getting started";
    public const string Tasks = "Tasks";
    public const string Agents = "Agents";
    public const string Coordination = "Coordination";
    public const string Budget = "Budget";
    public const string Configuration = "Configuration";

    private static readonly IReadOnlyList<CommandEntry> Entries = Array.AsReadOnly<CommandEntry>(
    [
        Entry("fknrtd", "fknrtd", GettingStarted,
            "Open the command center for the current folder, creating a workspace if needed.",
            "Finds an existing workspace in this folder or its parents. If none exists, initializes " +
            "one at the enclosing Git repository root, or in the current folder without Git. This " +
            "creates .fknrtd configuration and state directories before opening the dashboard. " +
            "Opening the dashboard does not start an agent or merge a task. Use fknrtd . to pin " +
            "initialization to the current folder.",
            "Run fknrtd doctor before commissioning your first task.",
            [], ["fknrtd", "fknrtd ."], ["fknrtd", "workspace", "mode"]),

        Entry("fknrtd init [path]", "init", GettingStarted,
            "Set up workspace configuration and discover suitable verification commands.",
            "Creates .fknrtd/config.json, its ignore file, and task, runtime, log, artifact and " +
            "worktree directories. Detects Git when available; otherwise creates a standalone " +
            "workspace. It never initializes a Git repository. An existing configuration stops it " +
            "unless you pass -force, which copies the old one into .fknrtd/runtime/backups before " +
            "replacing it. Run interactively it then offers to install the Claude statusline, " +
            "which writes outside .fknrtd, and to run doctor, which does launch each configured " +
            "agent with --version to see whether it is there. Use -yes to take the defaults or " +
            "-quiet to skip the guided setup entirely. Review the generated agent profiles and " +
            "verification commands before using them.",
            "Run fknrtd doctor to check that the workspace and configured agent executables are ready.",
            [new("path", "<folder>", "Folder to initialize; defaults to the current location."),
             new("-standalone", "", "Force direct edits in the folder, without Git worktrees."),
             new("-git", "", "Require an existing Git repository; fail when none is available."),
             new("-force", "", "Replace an existing configuration, backing the old one up first."),
             new("-yes", "", "Accept the guided setup's defaults without asking."),
             new("-quiet", "", "Skip the guided setup and its follow-up offers entirely.")],
            ["fknrtd init", "fknrtd init ./notes -standalone", "fknrtd init -git"],
            ["workspace", "mode", "config", "verification"]),

        Entry("fknrtd doctor", "doctor", GettingStarted,
            "Check workspace readiness before spending agent time.",
            "Checks configuration, state access, runtime prerequisites and configured agent " +
            "executables and their plan, implement and audit profiles (with default fallback). " +
            "In Git mode it checks defaultBaseRef and warns about pending workspace changes when " +
            "autoCommitAgentChanges is off. Git checks depend on workspace mode. Probes can launch executable " +
            "checks and test writable state; this does not run a coding task or repair your " +
            "configuration. Exit 0 means required checks passed; exit 2 means a required check failed.",
            "Fix each failed required check, then run doctor again before creating or running a task.",
            [], ["fknrtd doctor"], ["doctor", "agent", "mode"], json: true),

        Entry("fknrtd dashboard [-once]", "dashboard", GettingStarted,
            "See tasks, agent activity, conflicts and logs in one terminal.",
            "Opens the command center; -once renders one snapshot and exits. Viewing a frame " +
            "does not start task implementation or merge work. Interactive actions can create, " +
            "run or cancel tasks and request confirmed landing; their records live in .fknrtd. " +
            "Redirected output renders a single frame.",
            "Use the dashboard keymap to create a task, or inspect an existing task and its logs.",
            [new("-once", "", "Render one frame and return."),
             new("-width", "<columns>", "Set the width of a rendered frame."),
             new("-height", "<rows>", "Set the height of a rendered frame."),
             new("-no-color", "", "Disable ANSI color."),
             new("-color", "", "Request ANSI color, including for a captured frame.")],
            ["fknrtd dashboard", "fknrtd dashboard -once -no-color -width 100 -height 35"],
            ["task", "telemetry", "conflict"]),

        Entry("fknrtd status [-json]", "status", GettingStarted,
            "Print a workspace snapshot and signal live collisions to scripts.",
            "Reads current task, agent and coordination state and prints a single snapshot. " +
            "It does not edit project files or start agents. Exit 0 means no collision outcome " +
            "was reported; exit 3 signals a live collision requiring attention.",
            "Inspect conflicting paths with fknrtd claim list before running more work.",
            [new("-json", "", "Emit the complete snapshot as JSON."),
             new("-width", "<columns>", "Set rendered frame width."),
             new("-height", "<rows>", "Set rendered frame height."),
             new("-no-color", "", "Disable ANSI color."),
             new("-color", "", "Request ANSI color for rendered output.")],
            ["fknrtd status", "fknrtd status -json"], ["conflict", "telemetry", "task"], json: true),

        Entry("fknrtd help", "help", GettingStarted,
            "Print the command-line quick reference.",
            "Shows command syntax and common options without changing workspace files or " +
            "launching an agent. Successful help exits 0; an unknown command exits 2. " +
            "Options accept -name value or --name=value; repeat list options, and use the " +
            "equals form when a value starts with a hyphen.",
            "Initialize your workspace with fknrtd init, then run fknrtd doctor.",
            [], ["fknrtd help"], ["fknrtd", "workspace"], workspace: false),

        Entry("fknrtd task new", "task new", Tasks,
            "Describe a piece of work through a guided form that explains every field.",
            "Opens the same builder the dashboard opens for N, on its own. Each question arrives " +
            "with its definition and a worked example. The title and the brief are yours to write " +
            "and have no default — an empty title is refused, and so is a brief under fifteen " +
            "characters, because neither is something an agent could act on. Everything after them " +
            "is already correct for this workspace, so the rest of the form is Enter. A refused " +
            "answer says what was wrong with it. Nothing is written until the last step, " +
            "and backing out with Esc creates nothing. Needs an interactive terminal; use " +
            "'task create' from a script.",
            "The form ends by offering to run the task immediately, or you can press Enter on it later.",
            [], ["fknrtd task new"],
            ["title", "brief", "lead", "implementer", "auditor", "verification", "repair-round"]),

        Entry("fknrtd task create \"Title\" -brief \"What to build\" -verify \"dotnet build\"", "task create", Tasks,
            "Record a task with an explicit brief, agent roles and acceptance commands.",
            "Writes a queued task record under .fknrtd/tasks. A title and a brief are both " +
            "required: give the brief with -brief or -brief-file, and supplying neither is an " +
            "error rather than an empty brief. Leaving out -verify does not mean the work goes " +
            "unverified — the workspace's defaultVerificationCommands are copied into the task, " +
            "so pass -verify with no value only if you mean to check nothing. Creation alone does " +
            "not launch an agent, create its worktree or merge anything. Use -run only when you " +
            "intend to start immediately: the implementer may then edit its worktree, or the " +
            "project folder directly in standalone mode. Verification commands are trusted shell " +
            "commands.",
            "Run fknrtd task run <id> using the identifier printed at creation.",
            [new("title", "<text>", "Short task title; quote it when it contains spaces.", true),
             new("-brief", "<text>", "The goal, constraints and acceptance criteria the agents read. Required unless -brief-file is given."),
             new("-brief-file", "<path>", "Read the brief from a text file. Required unless -brief is given."),
             new("-lead", "<agent>", "Agent that plans the change."),
             new("-implementer", "<agent>", "Agent allowed to write the change."),
             new("-auditor", "<agent>", "Independent agent that reviews the change."),
             new("-verify", "<command>", "Repeat for each command that must exit 0."),
             new("-base", "<branch>", "Override the base branch in Git mode."),
             new("-repairs", "<count>", "Maximum repair rounds after verification or audit failure."),
             new("-run", "", "Run the newly created task immediately.")],
            ["fknrtd task create \"Fix parser\" -brief \"Accept quoted spaces; preserve existing syntax\" -verify \"dotnet build\"",
             "fknrtd task create \"Fix parser\" -brief-file brief.txt -lead claude -implementer codex -auditor claude"],
            ["title", "brief", "lead", "implementer", "auditor", "verification", "repair-round"]),

        Entry("fknrtd task list", "task list", Tasks,
            "List saved tasks so you can find the one you need.",
            "Reads task records from .fknrtd/tasks and lists them by recency. It does not " +
            "start, reset or delete tasks, and does not change project files.",
            "Copy a task identifier into fknrtd task show <id> to inspect its stages.",
            [], ["fknrtd task list"], ["task"], json: true),

        Entry("fknrtd task show <id>", "task show", Tasks,
            "Inspect a task's brief, stages, outcomes and verification evidence.",
            "Reads the saved task record and shows its current state. It does not rerun " +
            "verification or ask an agent for a new opinion, and makes no project edits. It exits 3 " +
            "when the task has failed and 0 otherwise, so a script can branch on it without parsing " +
            "anything — except with -json, which always exits 0 and expects you to read the " +
            "status field.",
            "For a failed stage, read its output under .fknrtd/logs/<task-id>/ before retrying.",
            TaskId(), ["fknrtd task show FKN-<id>"], ["task", "status.failed", "verification"], json: true),

        Entry("fknrtd task prompts <id>", "task prompts", Tasks,
            "Print the exact instruction each agent on a task will be sent.",
            "Composes the three prompts from the task's brief — the lead's, the implementer's and " +
            "the auditor's. The lead's is exactly what will be sent. The other two carry a " +
            "placeholder wherever a real run would paste something that does not exist yet: the " +
            "implementer's includes the lead's plan once the plan stage has run, and the auditor's " +
            "always shows a placeholder where the verification results will go. A repair round " +
            "also adds the failure evidence, which no preview can show. It reads the task and the " +
            "plan artifact and changes nothing. Knowing what an agent is about to be told is the " +
            "part of authorising it that no amount of sandboxing substitutes for.",
            "If a prompt is not what you meant, the brief is what to change; create a new task.",
            [new("<id>", "<task id>", "The task whose prompts to print.", Required: true)],
            ["fknrtd task prompts FKN-20260917-101500-a1b2"],
            ["brief", "lead", "implementer", "auditor", "verdict"]),

        Entry("fknrtd task diff <id>", "task diff", Tasks,
            "Print the finished change a task made, so it can be read before it is landed.",
            "Shows what the task committed on top of its base branch, followed by anything still " +
            "uncommitted in its worktree — both halves, because a workspace that does not commit " +
            "agent changes automatically has the whole change sitting uncommitted. The uncommitted " +
            "half is 'git diff HEAD', so a file the agent created and never staged does not appear " +
            "here at all; 'git status' in the worktree is what finds those. Long changes stop at " +
            "four thousand lines and say so. It reads " +
            "the worktree and changes nothing. A task with no worktree yet, or a standalone " +
            "workspace with no branch to compare against, says so rather than printing nothing. " +
            "Git failures show their reason and exit 2; they are not reported as an empty change. " +
            "Output is plain diff text, so it pipes into a pager or a reviewer as it stands.",
            "Once you have read it, land it with fknrtd task land <id> -confirm LAND.",
            [new("<id>", "<task id>", "The task whose change to print.", Required: true)],
            ["fknrtd task diff FKN-20260917-101500-a1b2",
             "fknrtd task diff FKN-20260917-101500-a1b2 | less"],
            ["worktree", "base-ref", "land", "auto-commit"]),

        Entry("fknrtd task run <id>", "task run", Tasks,
            "Run or resume the pipeline through verification and independent audit.",
            "Creates or uses the task worktree in Git mode, launches the assigned agents, " +
            "runs verification commands and writes task records, logs and artifacts. The " +
            "implementer writes project files; standalone mode writes directly in your folder. " +
            "In Git mode the verified, audited work is committed to the task branch at the end, " +
            "unless autoCommitAgentChanges is off — in which case a worktree still holding " +
            "uncommitted changes fails the task at that last step, after everything else has " +
            "passed, because there would be nothing to merge. Nothing merges automatically. " +
            "Exit 0 means a successful outcome, 3 means a failed outcome, and 130 means cancellation; " +
            "an exception reports a diagnostic and exits 1.",
            "If the run fails, read its stage log under .fknrtd/logs/<task-id>/; if ready to land, inspect the diff.",
            TaskId(), ["fknrtd task run FKN-<id>"], ["task", "worktree", "verification", "verdict", "land"]),

        Entry("fknrtd task retry <id>", "task retry", Tasks,
            "Reset failed stages and run the task again.",
            "Updates the task record and resumes work after resetting failed stages. Existing " +
            "work remains available; this is not a clean checkout or rollback. The resumed " +
            "implementer may write files and produce new logs, artifacts and task commits. " +
            "It does not merge automatically. Run outcomes use exit 0 for success, 3 for " +
            "failure and 130 for cancellation; operational exceptions exit 1.",
            "Read the new stage log if retry fails; inspect the diff if the task becomes ready to land.",
            TaskId(), ["fknrtd task retry FKN-<id>"], ["status.failed", "status.cancelled", "repair-round"]),

        Entry("fknrtd task cancel <id>", "task cancel", Tasks,
            "Request that a task stop running.",
            "What it does depends on whether the task is running. A running task gets a " +
            "cancellation request written under .fknrtd/runtime/cancels, which the workflow notices " +
            "within about half a second and then kills its external process. A task that is not " +
            "running is marked cancelled immediately and no request file is written. Either way it " +
            "does not roll back completed edits, remove the worktree or merge anything. It exits 1 " +
            "on a task id that does not exist, and on a task that has already landed — there is " +
            "nothing left to cancel, and reverting a merge is a job for Git.",
            "Inspect fknrtd task show <id> and its log before choosing whether to retry.",
            TaskId(), ["fknrtd task cancel FKN-<id>"], ["status.cancelled", "task"]),

        Entry("fknrtd task land <id> -confirm LAND", "task land", Tasks,
            "Explicitly accept a verified and audited task.",
            "Requires a task that is ready to land and your LAND confirmation. In Git mode, " +
            "merges its branch into the base branch after landing checks, changing the base " +
            "checkout and task record. In standalone mode it records completion of edits " +
            "already in the folder. It does not approve a failed task or clean up its worktree. " +
            "Exit 0 means the task landed. Anything that stops it — a task that is not ready, a " +
            "dirty checkout, a merge Git refuses — reports the reason and exits 1. A merge Git " +
            "refuses leaves the task Failed with its land stage failed, and your base branch " +
            "exactly where it was; fix the cause and retry it.",
            "After checking the landed result, reclaim the checkout with fknrtd task cleanup <id> -confirm REMOVE.",
            [.. TaskId(), new("-confirm", "LAND", "Explicit confirmation of landing.", true)],
            ["fknrtd task land FKN-<id> -confirm LAND"], ["land", "base-ref", "status.readytoland", "mode"]),

        Entry("fknrtd task cleanup <id> -confirm REMOVE", "task cleanup", Tasks,
            "Remove a task's worktree while retaining its record and history.",
            "Removes the task checkout. The task record and its logs remain, and cleanup does not " +
            "merge anything. It does not change the task's record at all, so a cleaned-up task " +
            "still reads as it did. The branch remains too, unless the task has already landed: a " +
            "landed branch is deleted with 'git branch -d', which refuses to remove anything not " +
            "already merged. Cleanup also runs 'git worktree prune' across the whole repository, " +
            "which clears Git's record of any worktree directory that no longer exists, including " +
            "ones FKNRTD.CLI did not create. A running task cannot be " +
            "cleaned up. Forced cleanup can discard uncommitted checkout contents, so inspect " +
            "them before choosing -force. Standalone mode must preserve the project folder.",
            "Use fknrtd task show <id> when you need the retained record or log paths.",
            [.. TaskId(), new("-confirm", "REMOVE", "Explicit confirmation of checkout removal.", true),
             new("-force", "", "Allow removal when the worktree contains changes.")],
            ["fknrtd task cleanup FKN-<id> -confirm REMOVE"], ["cleanup", "worktree"]),

        Entry("fknrtd agent list", "agent list", Agents,
            "See registered agents, enabled state and executable discovery.",
            "Reads workspace agent definitions and checks executable discovery. It does not " +
            "start a coding task, install software or change the configuration.",
            "Run fknrtd doctor if an enabled agent cannot be found or launched. The dashboard shows the " +
            "same roster, and lets you change it, on the A key.",
            [], ["fknrtd agent list"], ["agent", "profile", "doctor"], json: true),

        Entry("fknrtd agent new", "agent new", Agents,
            "Register a coding CLI through a guided form instead of an option list.",
            "Asks what to call the agent, which program runs it, how that program wants to be given " +
            "a prompt, what arguments launch it, and whether it may audit. It explains why the " +
            "{prompt} placeholder exists and why an auditor needs verdict markers, then writes a " +
            "complete definition with plan, implement and audit profiles. It reports whether the " +
            "executable is actually on PATH, because a registration that looks fine but names a " +
            "missing program fails much later, inside a task. Needs an interactive terminal; use " +
            "'agent add' from a script.",
            "Run fknrtd doctor to confirm the new agent can be launched, then assign it in fknrtd task " +
            "new. The same form opens on N from the dashboard's A screen.",
            [], ["fknrtd agent new"],
            ["agent", "profile", "prompt-delivery", "verdict"]),

        Entry("fknrtd agent add -id <id> -exe <path>", "agent add", Agents,
            "Register an existing coding CLI and its prompt profiles.",
            "Writes an agent definition into .fknrtd/config.json, either from JSON or command " +
            "options. It does not install the executable, authenticate it or start a task. " +
            "Give plan and audit profiles read-only flags supported by that CLI; registration " +
            "alone does not enforce its sandbox. Arguments support {prompt}, {taskId}, " +
            "{workspace} and {branch} placeholders.",
            "Run fknrtd config validate and fknrtd doctor before assigning the agent a task.",
            [new("-file", "<json>", "Read a complete agent definition instead of building one from flags."),
             new("-id", "<id>", "Agent identifier; required when not using -file."),
             new("-exe", "<path>", "Executable name or path; required when not using -file."),
             new("-arg", "<argument>", "Repeat to supply default arguments; use -arg=-flag for leading hyphens."),
             new("-default-arg", "<argument>", "Repeat to define the default profile."),
             new("-plan-arg", "<argument>", "Repeat to define the planning profile."),
             new("-implement-arg", "<argument>", "Repeat to define the writing profile."),
             new("-audit-arg", "<argument>", "Repeat to define the read-only auditing profile."),
             new("-stdin", "", "Deliver the prompt on standard input."),
             new("-name", "<text>", "Display name; defaults to the id."),
             new("-kind", "<kind>", "claude, codex or generic. Chooses the colour and the status glyphs."),
             new("-color", "<colour>", "Colour to draw this agent in on the dashboard.")],
            ["fknrtd agent add -file examples/generic-agent.json",
             "fknrtd agent add -id local -exe mytool -stdin"], ["agent", "profile", "prompt-delivery"]),

        Entry("fknrtd agent set <id> -exe <path>", "agent set", Agents,
            "Change what a configured agent runs, or what it is called.",
            "Rewrites the executable or the display name of an existing definition in " +
            ".fknrtd/config.json, leaving its profiles, its identifier and whether it is enabled " +
            "alone. Use it when the tool moved, when it is installed under a different name, or " +
            "when it is not on PATH and you want to give the full path to it. It does not install " +
            "anything, does not change how the agent is invoked — the profile arguments are " +
            "unchanged, so a program that wants its prompt differently needs 'fknrtd agent add' " +
            "instead — and does not affect a task that has already run. The new command is used " +
            "the next time a task runs. The executable is not required to exist yet, so a machine " +
            "can be configured before the tool is installed on it; an unresolvable value is " +
            "reported and then accepted.",
            "Run fknrtd doctor to confirm the new command answers. In the dashboard, A then E does " +
            "the same thing to the highlighted agent.",
            [new("id", "<agent-id>", "Identifier of a configured agent.", true),
             new("-exe", "<path>", "Program the agent should run: a name on PATH, or a full path."),
             new("-name", "<text>", "Display name shown in the roster and on the agent radar.")],
            ["fknrtd agent set claude -exe /opt/claude/bin/claude",
             "fknrtd agent set codex -name \"Codex (work account)\""],
            ["agent", "config"]),

        Entry("fknrtd agent enable <id>", "agent enable", Agents,
            "Make a registered agent available for task assignments.",
            "Enables the named definition in .fknrtd/config.json. It does not install or " +
            "launch the agent, or start queued tasks.",
            "Run fknrtd doctor to confirm the newly enabled executable is ready. In the dashboard, A then " +
            "Space does the same thing to the highlighted agent.",
            AgentId(), ["fknrtd agent enable codex"], ["agent", "config"]),

        Entry("fknrtd agent disable <id>", "agent disable", Agents,
            "Keep an agent definition while making it unavailable for new assignments.",
            "Disables the named definition in .fknrtd/config.json without uninstalling the " +
            "CLI or deleting its profiles. This is configuration, not a task cancellation request.",
            "Review queued task role assignments and choose enabled agents before running them. In the " +
            "dashboard, A then Space does the same thing to the highlighted agent.",
            AgentId(), ["fknrtd agent disable local"], ["agent", "config"]),

        Entry("fknrtd agent remove <id> -confirm REMOVE", "agent remove", Agents,
            "Remove an agent adapter from workspace configuration.",
            "Deletes its registration from .fknrtd/config.json after REMOVE confirmation. " +
            "It does not uninstall the external CLI or delete its own settings or credentials.",
            "Check what is left with fknrtd agent list, which shows the definitions and whether each " +
            "executable can be found. It does not read tasks, so it will not tell you which tasks " +
            "named the agent you removed; those tasks keep the name and fail on the stage that " +
            "needed it. In the dashboard, A then Del asks for the same confirmation and does say " +
            "how many tasks name it.",
            [.. AgentId(), new("-confirm", "REMOVE", "Confirm removal of the adapter.", true)],
            ["fknrtd agent remove local -confirm REMOVE"], ["agent", "config"]),

        Entry("fknrtd telemetry report -agent <id> -state running", "telemetry report", Coordination,
            "Report activity from an agent session the command center did not launch.",
            "Updates a runtime snapshot under .fknrtd/runtime/agents so the dashboard can " +
            "show intent and touched paths. The hook alias accepts the same report. Reporting " +
            "does not launch an agent, edit those paths or reserve them; use claims for reservations. " +
            "A progress percentage requires a truthful basis describing what was measured. Each " +
            "report replaces the whole snapshot rather than updating part of it: anything you leave " +
            "out goes back to its default — state to running, role to observer, source to " +
            "external-hook — even when the previous report said otherwise. Send the full picture " +
            "every time.",
            "Use fknrtd status to check the report and any resulting conflict indications.",
            [new("-agent", "<id>", "Agent producing this report.", true),
             new("-state", "<state>", "unknown, offline, idle, planning, running, reviewing, waiting, blocked, failed or completed."),
             new("-role", "<role>", "observer, lead, implementer or auditor."),
             new("-intent", "<text>", "Describe the work currently underway."),
             new("-task", "<id>", "Associated task identifier."),
             new("-source", "<text>", "Where this report came from."),
             new("-cwd", "<path>", "The agent's working directory."),
             new("-worktree", "<path>", "Its worktree directory, when applicable."),
             new("-branch", "<name>", "Its current Git branch."),
             new("-pid", "<number>", "The reporting process identifier."),
             new("-exit-code", "<number>", "Reported exit code of completed work."),
             new("-path", "<path>", "Repeat for paths already touched."),
             new("-planned-path", "<path>", "Repeat for paths the agent plans to touch."),
             new("-progress", "<0..100>", "Measured progress; requires -basis."),
             new("-basis", "<text>", "Evidence explaining the progress measurement."),
             new("-quiet", "", "Suppress normal report output.")],
            ["fknrtd telemetry report -agent local -state running -intent \"Editing parser\" -path src/parser.cs",
             "fknrtd hook -agent local -state completed -exit-code 0"], ["telemetry", "agent", "conflict"]),

        Entry("fknrtd claim add -agent <id> -path <pattern>", "claim add", Coordination,
            "Declare a temporary read or write reservation for paths.",
            "Writes a claim under .fknrtd/runtime/claims so overlapping work can be detected. " +
            "The claim expires after its TTL; it is not an operating-system lock and does " +
            "not prevent another tool from editing the files. It does not change claimed contents.",
            "Check fknrtd claim list for overlaps and renew the claim before its TTL expires.",
            [new("-agent", "<id>", "Agent holding the reservation.", true),
             new("-path", "<pattern>", "Path to reserve; repeat for multiple paths.", true),
             new("-mode", "read|write", "Read claims coexist; write claims conflict with overlapping work."),
             new("-ttl", "<seconds>", "Reservation lifetime; defaults to 300 seconds."),
             new("-worktree", "<path>", "Which checkout this is in. Defaults to the workspace root, and decides whether an overlap reads as a collision or a merge risk."),
             new("-task", "<id>", "The task this reservation belongs to.")],
            ["fknrtd claim add -agent codex -path src/parser.cs -mode write -ttl 300"],
            ["claim", "claim-mode", "ttl", "conflict"]),

        Entry("fknrtd claim list", "claim list", Coordination,
            "Inspect file reservations and detected overlap risks.",
            "Reads claim and activity records to show reservations and conflicts. It does " +
            "not resolve collisions, cancel tasks or edit the reserved files. It exits 3 when any " +
            "detected conflict is a collision and 0 otherwise, so a script can gate on it. An " +
            "expired reservation is not removed by expiring: it keeps being reported as a stale " +
            "claim until 'fknrtd claim release' clears it.",
            "Coordinate overlapping writers before proceeding; renew or release your own claims as needed.",
            [], ["fknrtd claim list"], ["claim", "conflict", "conflict.collision"], json: true),

        Entry("fknrtd claim renew <id>", "claim renew", Coordination,
            "Extend a reservation while its agent is still working.",
            "Updates the claim's expiry on disk. It does not change file contents or " +
            "silently renew any other claim. Renewing does not make overlapping writes safe.",
            "Release the claim with fknrtd claim release <id> when work on its paths finishes.",
            [new("id", "<claim-id>", "Identifier of the existing claim.", true),
             new("-ttl", "<seconds>", "Lifetime to apply to the renewed reservation.")],
            ["fknrtd claim renew <claim-id> -ttl 300"], ["claim", "ttl"]),

        Entry("fknrtd claim release <id>", "claim release", Coordination,
            "Release a file reservation once the work is finished.",
            "Removes the claim record from .fknrtd/runtime/claims. It does not delete the " +
            "reserved files, cancel an agent or undo its edits.",
            "Use fknrtd claim list to confirm the remaining reservations and risks.",
            [new("id", "<claim-id>", "Identifier of the claim to release.", true)],
            ["fknrtd claim release <claim-id>"], ["claim"]),

        Entry("fknrtd message send -from <id> -to <id> -text \"Message\"", "message send", Coordination,
            "Record a durable hand-off note between agents.",
            "Appends a message to .fknrtd/runtime/messages.jsonl for the dashboard and " +
            "message readers. It does not inject a prompt into or interrupt a running CLI " +
            "session, and it does not launch the recipient.",
            "The recipient can inspect fknrtd message list and acknowledge the message identifier.",
            [new("-from", "<id>", "Sender identifier.", true),
             new("-to", "<id>", "Recipient identifier.", true),
             new("-text", "<text>", "Message body.", true),
             new("-task", "<id>", "Task this hand-off concerns.")],
            ["fknrtd message send -from codex -to claude -text \"Ready for audit\""], ["message"]),

        Entry("fknrtd message list", "message list", Coordination,
            "Read recent agent hand-off messages.",
            "Reads the workspace message history, most recent first, fifty at a time unless you " +
            "ask for more. Listing a message does not acknowledge it, deliver a new prompt or " +
            "change project files.",
            "Acknowledge a handled note with fknrtd message ack <id>.",
            [new("-limit", "<count>", "How many to read. Defaults to 50."),
             new("-json", "", "Machine-readable output instead of the table.")],
            ["fknrtd message list", "fknrtd message list -limit 200"], ["message"]),

        Entry("fknrtd message ack <id>", "message ack", Coordination,
            "Record that a hand-off message has been acknowledged.",
            "Appends an acknowledgement to message history. It does not delete the " +
            "original note, run its requested work or mark any task complete.",
            "Use fknrtd message list to review the acknowledgement state.",
            [new("id", "<message-id>", "Identifier of the message being acknowledged.", true)],
            ["fknrtd message ack <message-id>"], ["message"]),

        Entry("fknrtd events -limit 50", "events", Coordination,
            "Read the recent workspace event history.",
            "Reads the tail of .fknrtd/runtime/events.jsonl to show what happened. This " +
            "does not replay events, launch agents or modify project files.",
            "Inspect the affected task and its stage log when an event reports a failure.",
            [new("-limit", "<count>", "Maximum number of recent events to display.")],
            ["fknrtd events -limit 100"], ["event", "task"], json: true),

        Entry("fknrtd usage list", "usage list", Budget,
            "Show the latest recorded capacity figures for each agent.",
            "Reads usage snapshots under .fknrtd/runtime/usage. These are the latest " +
            "reported values, not a promise of current quota. Listing does not refresh " +
            "remote data, launch a coding task or modify project files.",
            "Run fknrtd usage refresh codex for a fresh Codex report, or install Claude's statusline integration.",
            [], ["fknrtd usage list"], ["usage", "context", "five-hour", "weekly"], json: true),

        Entry("fknrtd usage refresh codex", "usage refresh", Budget,
            "Ask Codex for structured rate-limit information.",
            "Invokes the supported Codex usage probe and saves the resulting snapshot " +
            "under .fknrtd/runtime/usage. It does not commission implementation work or " +
            "edit project files. Other agents need their supported integration or an " +
            "explicit usage set report.",
            "Read fknrtd usage list before starting work that may exceed the remaining budget.",
            [new("agent", "codex", "Agent whose supported rate-limit probe should run.")],
            ["fknrtd usage refresh codex"], ["usage", "five-hour", "weekly"]),

        Entry("fknrtd usage set <agent> -context 70 -five-hour 80 -weekly 60", "usage set", Budget,
            "Record explicit capacity measurements from an external source.",
            "Writes a usage snapshot under .fknrtd/runtime/usage. Values are percentages " +
            "remaining, clamped to 0 through 100. This does not query the provider, change " +
            "your subscription or grant more quota; report measured values rather than guesses. " +
            "It replaces that agent's snapshot outright: a percentage you leave out becomes " +
            "unknown rather than keeping its previous value, and any recorded reset times go with " +
            "it. Pass everything you know each time. It says afterwards which figures it dropped, " +
            "so a hand-typed correction that lost the others does not do it quietly.",
            "Use fknrtd usage list to check the recorded values and source.",
            [new("agent", "<id>", "Agent the measurements describe.", true),
             new("-context", "<percent>", "Conversation context remaining."),
             new("-five-hour", "<percent>", "Five-hour rate-limit budget remaining."),
             new("-weekly", "<percent>", "Seven-day rate-limit budget remaining."),
             new("-source", "<text>", "Where these measurements came from.")],
            ["fknrtd usage set local -context 70 -five-hour 80 -weekly 60 -source manual"],
            ["usage", "context", "five-hour", "weekly"]),

        Entry("fknrtd integration install-claude-statusline", "integration install-claude-statusline", Budget,
            "Connect Claude Code's statusline to workspace usage reporting.",
            "Writes a command statusline to Claude's own settings. Without -project that is the " +
            "user-scoped settings file in your home directory, which affects every project you " +
            "open in Claude Code. With -project it is .claude/settings.json under the directory " +
            "you are standing in — not the workspace root, so run it from the top of the project " +
            "you mean. Existing settings are backed up before modification, and replacing an " +
            "existing statusline requires -force. This changes settings outside .fknrtd and does " +
            "not restart Claude or run a coding task.",
            "Restart Claude Code so its statusline begins supplying usage reports.",
            [new("-project", "", "Write project-scoped Claude settings."),
             new("-force", "", "Back up and replace an existing statusline.")],
            ["fknrtd integration install-claude-statusline",
             "fknrtd integration install-claude-statusline -project"], ["statusline", "usage"], workspace: false),

        Entry("fknrtd config show", "config show", Configuration,
            "Print the active workspace configuration.",
            "Reads .fknrtd/config.json and displays agent definitions, defaults and " +
            "workflow policy. It does not rewrite configuration or launch agents.",
            "Use fknrtd config path to locate the file if you need to change a setting.",
            [], ["fknrtd config show"], ["config", "profile", "timeouts"]),

        Entry("fknrtd config path", "config path", Configuration,
            "Locate the configuration file for the selected workspace.",
            "Prints the path to .fknrtd/config.json. It does not open an editor, modify " +
            "the file or change which workspace another terminal uses.",
            "After editing that file, run fknrtd config validate.",
            [], ["fknrtd config path"], ["config", "workspace"]),

        Entry("fknrtd config validate", "config validate", Configuration,
            "Check that configured agent definitions and settings are coherent.",
            "Validates the saved configuration without rewriting it or starting a coding " +
            "task. Invalid definitions must be corrected before relying on a run. " +
            "Validation is not a substitute for doctor executable checks.",
            "Run fknrtd doctor after validation to check the local execution environment.",
            [], ["fknrtd config validate"], ["config", "agent", "doctor"]),

        Entry("fknrtd run <id>", "run", Tasks,
            "Shorthand for task run. Starts the named task from its first unfinished stage.",
            "Identical to 'fknrtd task run <id>' and kept because it is the command an operator " +
            "reaches for most. It resumes rather than restarts: stages that already passed are not " +
            "run again. Exit 0 means the task reached ready-to-land; exit 3 means it failed, and " +
            "the reason is on the task and in its stage log.",
            "On failure, read the log and run fknrtd task retry <id> once the cause is fixed.",
            [new("<id>", "<task id>", "The task to run.", Required: true)],
            ["fknrtd run FKN-20260917-101500-a1b2"],
            ["task", "stage.plan", "stage.verify", "repair-round"]),

        Entry("fknrtd land <id> -confirm LAND", "land", Tasks,
            "Shorthand for task land. Merges a verified, audited task into its base branch.",
            "Identical to 'fknrtd task land'. This is the only command that changes your base " +
            "branch, so it refuses to act without -confirm LAND spelled exactly. It will not land a " +
            "task whose verification failed, or that has not received an explicit PASS verdict from " +
            "its auditor. A task with no verification commands configured has nothing to fail, so a " +
            "passing audit is enough on its own.",
            "Run fknrtd task cleanup <id> -confirm REMOVE once you no longer need the worktree.",
            [new("<id>", "<task id>", "The task to land.", Required: true),
             new("-confirm", "LAND", "Required. Typed in full, so a stray keystroke cannot merge.", Required: true)],
            ["fknrtd land FKN-20260917-101500-a1b2 -confirm LAND"],
            ["land", "base-ref", "verdict", "worktree"]),

        Entry("fknrtd explain [term]", "explain", GettingStarted,
            "Look up any word this product uses, from any directory.",
            "Prints the same explanation the dashboard shows inline: what the term means, why it " +
            "matters, and a worked example. With no argument it lists every term grouped by " +
            "category. A term that does not exist is treated as a search rather than a refusal, " +
            "because the word an operator types is usually a word inside the entry they want. " +
            "It needs no workspace and changes nothing on disk.",
            "Nothing follows from it. It is safe to run at any time, including mid-task.",
            [new("term", "<word>", "The term to explain. Omit it to list every term.")],
            ["fknrtd explain", "fknrtd explain brief", "fknrtd explain auditor"],
            ["brief", "lead", "auditor", "verification"],
            workspace: false),

        Entry("fknrtd portal [-out <file>]", "portal", GettingStarted,
            "Write the offline HTML operator guide.",
            "Generates a single self-contained HTML file covering the pipeline, every command, the " +
            "dashboard keys and the full glossary. It is built from the same tables the running " +
            "program reads, so it cannot document a command that was removed or a key that never " +
            "existed. The file has no external references and opens correctly with no network. It " +
            "overwrites the destination and reads nothing from the workspace.",
            "Open the generated file in a browser. Regenerate it after upgrading FKNRTD.CLI. The " +
            "dashboard writes the same document on F2 from its help screen.",
            [new("-out", "<file>", "Where to write it. Defaults to fknrtd-portal.html here.")],
            ["fknrtd portal", "fknrtd portal -out docs/guide.html"],
            ["fknrtd", "workspace"],
            workspace: false),

        Entry("fknrtd version", "version", GettingStarted,
            "Print the installed version and nothing else.",
            "Writes the version string to standard output and exits 0. Useful in a script that " +
            "needs to check what is installed before relying on a newer command. It reads no " +
            "configuration and needs no workspace.",
            "Compare it against the release you expected before reporting a problem.",
            [], ["fknrtd version"], ["fknrtd"],
            workspace: false)
    ]);

    public static IReadOnlyList<CommandEntry> All => Entries;

    public static IReadOnlyList<string> Groups { get; } =
        Array.AsReadOnly(Entries.Select(entry => entry.Group).Distinct(StringComparer.Ordinal).ToArray());

    public static IEnumerable<CommandEntry> InGroup(string group) =>
        Entries.Where(entry => entry.Group.Equals(group, StringComparison.OrdinalIgnoreCase));

    /// <summary>Exact names win; hyphenated names and an unambiguous final word also resolve.</summary>
    /// <summary>
    /// The entry whose name is exactly this, or null. Unlike <see cref="Find"/> this never falls
    /// back to a near match, because its caller rejects command lines: resolving "run" to "task
    /// run" would check one command's options against another command's list.
    /// </summary>
    public static CommandEntry? Exact(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        var needle = Normalize(name);
        return Entries.FirstOrDefault(entry =>
            Normalize(entry.Name).Equals(needle, StringComparison.OrdinalIgnoreCase));
    }

    public static CommandEntry? Find(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        var needle = Normalize(name);
        if (needle == "hook") needle = "telemetry report";
        var exact = Entries.FirstOrDefault(entry => Normalize(entry.Name).Equals(needle, StringComparison.OrdinalIgnoreCase));
        if (exact is not null) return exact;
        var matches = Entries.Where(entry => Normalize(entry.Name).EndsWith(" " + needle, StringComparison.OrdinalIgnoreCase))
            .Take(2).ToArray();
        return matches.Length == 1 ? matches[0] : null;
    }

    /// <summary>Stable ranked substring search: exact name, prefix, name, summary, then detail.</summary>
    public static IReadOnlyList<CommandEntry> Search(string? query)
    {
        if (string.IsNullOrWhiteSpace(query)) return Entries;
        var needle = query.Trim();
        var found = Find(needle);
        return Array.AsReadOnly(Entries.Select(entry => (entry, rank: ReferenceEquals(entry, found) ? 100 : Rank(entry, needle)))
            .Where(item => item.rank > 0).OrderByDescending(item => item.rank).Select(item => item.entry).ToArray());
    }

    private static int Rank(CommandEntry entry, string needle)
    {
        var name = Normalize(entry.Name);
        var normalized = Normalize(needle);
        if (name.StartsWith(normalized, StringComparison.OrdinalIgnoreCase)) return 80;
        if (name.Contains(normalized, StringComparison.OrdinalIgnoreCase)) return 60;
        if (entry.Invocation.Contains(needle, StringComparison.OrdinalIgnoreCase)) return 50;
        if (entry.Summary.Contains(needle, StringComparison.OrdinalIgnoreCase)) return 30;
        if (entry.Detail.Contains(needle, StringComparison.OrdinalIgnoreCase)
            || entry.WhatHappensNext.Contains(needle, StringComparison.OrdinalIgnoreCase)
            || entry.Options.Any(option => (option.Name + " " + option.Meaning).Contains(needle, StringComparison.OrdinalIgnoreCase))) return 10;
        return 0;
    }

    private static string Normalize(string value)
    {
        var result = string.Join(' ', value.Trim().Replace('-', ' ').Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return result.StartsWith("fknrtd ", StringComparison.OrdinalIgnoreCase) ? result[7..] : result;
    }

    private static CommandOption[] TaskId() => [new("id", "<task-id>", "Identifier printed when the task was created.", true)];
    private static CommandOption[] AgentId() => [new("id", "<agent-id>", "Identifier of a configured agent.", true)];

    private static CommandEntry Entry(string invocation, string name, string group, string summary,
        string detail, string next, CommandOption[] options, string[] examples, string[] terms,
        bool workspace = true, bool json = false)
    {
        // The options that several commands share are appended from here rather than written out on
        // each entry. Eight commands accepted -json and not one of them documented it, because
        // remembering to repeat an option eight times is not a thing that happens reliably.
        var all = new List<CommandOption>(options);
        if (json)
        {
            all.Add(new("-json", "", "Machine-readable output instead of the table."));
        }

        if (workspace)
        {
            all.Add(new("-root", "<path>", "Select the workspace to use."));
        }

        return new(invocation, name, group, summary, detail, next,
            Array.AsReadOnly(all.ToArray()), Array.AsReadOnly(examples), Array.AsReadOnly(terms));
    }
}
