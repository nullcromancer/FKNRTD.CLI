namespace FKNRTD.Help;

/// <summary>
/// One explained concept. Every surface that teaches the operator something — the dashboard's
/// inline hints, the <c>?</c> overlay, <c>fknrtd explain</c>, and the generated HTML portal —
/// reads from this one table, so an explanation cannot drift away from the code that uses it.
/// </summary>
/// <param name="Term">Stable lookup key. Dotted keys group a family: <c>stage.plan</c>.</param>
/// <param name="Title">Human title as it appears in a heading.</param>
/// <param name="Category">Grouping for the portal and the help browser.</param>
/// <param name="Summary">One line, short enough to sit under a form field on an 80-column terminal.</param>
/// <param name="Detail">Full explanation. Sentences, no markup; the renderer wraps it.</param>
/// <param name="Example">A concrete example, or the empty string when one would be noise.</param>
/// <param name="StandaloneSummary">
/// The summary to use in a standalone workspace, where it differs. Empty means the entry reads
/// the same in both modes, which is true of all but a handful. A standalone workspace has no
/// branch, no worktree and nothing to merge, and the summaries describing those were written for
/// Git mode - so `fknrtd task show` told a standalone operator, two lines under "agents edit the
/// project folder directly", that a branch and an isolated checkout would be created.
/// </param>
public sealed record GlossaryEntry(
    string Term,
    string Title,
    string Category,
    string Summary,
    string Detail,
    string Example = "",
    string StandaloneSummary = "")
{
    /// <summary>The summary that applies in this workspace, which is not always the same one.</summary>
    public string SummaryFor(bool standalone) =>
        standalone && StandaloneSummary.Length > 0 ? StandaloneSummary : Summary;
}

/// <summary>The explanation table. Ordered by category, then by the order an operator meets them.</summary>
public static class Glossary
{
    public const string Concepts = "Concepts";
    public const string TaskFields = "Task fields";
    public const string Roles = "Agent roles";
    public const string Stages = "Workflow stages";
    public const string Statuses = "Task status";
    public const string StageStates = "Stage state";
    public const string AgentStates = "Agent state";
    public const string Coordination = "Coordination";
    public const string Budget = "Budget and usage";
    public const string Configuration = "Configuration";

    private static readonly GlossaryEntry[] Entries =
    [
        // Concepts
        new("fknrtd", "FKNRTD.CLI", Concepts,
            "A command center that runs coding agents through one reviewed, verified pipeline.",
            "FKNRTD.CLI does not write code itself. It drives the coding agents you already have " +
            "installed — Claude Code, OpenAI Codex CLI, or anything else you register — through a " +
            "fixed pipeline: one agent plans, another implements, your own commands verify the " +
            "result, and a third agent audits it. Nothing reaches your base " +
            "branch until you type the confirmation yourself. The point is to keep the agent that " +
            "writes the change apart from the agent that judges it. That separation is a default " +
            "and not a rule: the task builder offers a different implementer from the lead, but " +
            "nothing stops you naming one agent for all three roles, and nothing warns you if you " +
            "do.",
            "fknrtd            opens the command center for the folder you are standing in"),

        new("workspace", "Workspace", Concepts,
            "One folder that FKNRTD.CLI manages, holding its state in a .fknrtd directory.",
            "A workspace is any folder you have pointed FKNRTD.CLI at. Everything it knows about " +
            "that folder — configuration, tasks, events, logs, messages — lives in a .fknrtd " +
            "directory at the folder's root, and deleting it forgets everything FKNRTD.CLI recorded. " +
            "It does not undo the work: a standalone workspace's agents edit the folder itself, a " +
            "Git workspace's tasks create branches and merge them into your base branch, and " +
            "'fknrtd integration install-claude-statusline' writes .claude/settings.json. Those are " +
            "changes to your project, not to .fknrtd. Running fknrtd in a folder that has no " +
            "workspace yet creates one with sensible defaults rather than asking you to configure " +
            "anything first.",
            "<your project>/.fknrtd/config.json"),

        new("what-to-commit", "What to commit", Concepts,
            "Commit .fknrtd/config.json and .fknrtd/.gitignore. Everything else is already ignored.",
            "The configuration is a project decision — which agents this repository uses, what " +
            "verifies its work, which branch tasks start from — so it belongs in the repository and " +
            "is worth sharing with whoever else works on it. Everything beside it is this machine's " +
            "own state: task records, stage logs, worktrees, claims, events. Setting a workspace up " +
            "writes an ignore file inside .fknrtd that excludes all of that, which is why Git shows " +
            "the directory as untracked but only offers you the two files worth keeping.",
            "git add .fknrtd/config.json .fknrtd/.gitignore"),

        new("mode", "Workspace mode", Concepts,
            "Git mode isolates work in a worktree; standalone mode edits the folder directly.",
            "A workspace is either Git-backed or standalone. Setting one up interactively asks you " +
            "which; anything non-interactive reads the folder and picks Git when it can. In Git " +
            "mode every task gets its own branch and its own " +
            "worktree, so an agent's edits are invisible to your checkout until you land them. In " +
            "standalone mode there is no repository to isolate against: agents edit the folder in " +
            "place, and landing simply records that verified work is already there. Git mode is " +
            "strictly safer. If a folder could be a repository, make it one before you start.",
            "fknrtd init -git          refuse to continue unless this is a Git repository"),

        new("worktree", "Worktree", Concepts,
            "A second checkout of your repository where one task's agent works in isolation.",
            "A Git worktree is a separate directory holding a separate checked-out branch of the " +
            "same repository. FKNRTD.CLI creates one per task so an agent can edit, build and break " +
            "things without touching the files you have open. Your own working copy never changes " +
            "while a task runs. When the task lands, its branch merges into the base branch; when " +
            "you clean the task up, the worktree directory is removed. The branch is kept too, " +
            "unless the task has already landed, in which case cleanup deletes it with 'git branch " +
            "-d' — which refuses to remove a branch that is not already merged.",
            ".fknrtd/worktrees/FKN-20260917-101500-a1b2"),

        new("base-ref", "Base branch", Concepts,
            "The branch a task starts from and eventually merges back into.",
            "The base branch is the known-good starting point for a task. The task's branch is cut " +
            "from it, the agent's work happens on top of it, and landing merges the task branch " +
            "back into it. It defaults to whatever branch the repository was on when the workspace " +
            "was created, which is usually what you want. Change it per task when the work belongs " +
            "on top of something other than your default line of development.",
            "main, develop, release/2.1"),

        new("task", "Task", Concepts,
            "One unit of agent work, from written brief through audit to landing.",
            "A task is the only thing FKNRTD.CLI executes. It carries the brief you wrote, the " +
            "three agents assigned to it, the commands that decide whether the work is correct, " +
            "and a record of every stage it has been through. Tasks are durable: they survive " +
            "restarts, and they can be re-run and retried. Their logs are not archived — a stage " +
            "that runs again at the same repair round writes over its previous log, so if you need " +
            "to keep the output of a failed run, copy it out before you press R. Task identifiers " +
            "encode the UTC moment of creation so they sort chronologically.",
            "FKN-20260917-101500-a1b2c3d4e5"),

        new("land", "Landing", Concepts,
            "Merging a verified, audited task branch into the base branch. Always manual.",
            "Landing is the only step that changes your base branch, and it is the only step " +
            "FKNRTD.CLI will never take on its own. A task becomes landable once its audit returns " +
            "a PASS verdict and verification did not fail. Those are not the same thing: a task with " +
            "no verification commands configured has its verify stage marked Skipped rather than " +
            "Passed, and a passing audit still makes it landable. Read the checks row before you " +
            "land something — three pending markers can mean nothing ran, not that nothing broke. " +
            "Even then you have to confirm by typing the word LAND in full — not y, not Enter — " +
            "because a typo should not be able to merge anything.",
            "fknrtd task land FKN-... -confirm LAND"),

        new("cleanup", "Cleanup", Concepts,
            "Removing a finished task's worktree directory while keeping its record and its logs.",
            "Cleanup deletes the worktree directory a task was working in. The task's record and its " +
            "logs are kept, so nothing you might want to read later is lost. Its branch is kept too, " +
            "unless the task has already landed — a landed branch is deleted with 'git branch -d', " +
            "which refuses to delete anything not already merged. A running task cannot be cleaned " +
            "up. Like landing, it requires a typed confirmation.",
            "fknrtd task cleanup FKN-... -confirm REMOVE"),

        new("doctor", "Doctor", Concepts,
            "A pre-flight check that reports what is ready and what will fail before you rely on it.",
            "Doctor inspects everything a task run depends on and reports each item as passing, " +
            "failing, or optional: whether the workspace is readable, whether Git is present when " +
            "the mode needs it, whether each configured agent's executable can actually be found " +
            "and launched, whether plan, implement and audit profiles resolve with default fallback, " +
            "and whether the configuration parses. In Git mode it checks that defaultBaseRef resolves " +
            "to a commit and warns when autoCommitAgentChanges is off with pending workspace changes. " +
            "Run it after installing or " +
            "reconfiguring anything. It reports and does not gate: nothing consults doctor before " +
            "running a task, so a failed required check is a warning that something is likely to go " +
            "wrong, not a lock. It can also fail on an enabled agent that the task you are about to " +
            "run does not name — which is worth knowing before you go looking for the wrong fault.",
            "fknrtd doctor"),

        // Task fields
        new("title", "Title", TaskFields,
            "A short name for the task, used in lists and in the branch name.",
            "The title is how you will recognise this task in a list of thirty. Keep it to a " +
            "handful of words describing the outcome, not the method. It is not the instruction to " +
            "the agent — that is the brief — so it does not need to be precise or complete. In Git " +
            "mode a simplified form of it becomes part of the task's branch name.",
            "Add rate limiting to the login endpoint"),

        new("brief", "Brief", TaskFields,
            "The actual instruction the agents read. Say what done looks like, not how to get there.",
            "The brief is the prompt. Every agent on the task reads it: the lead plans from it, the " +
            "implementer builds from the plan, and the auditor judges the finished work against it. " +
            "Write it the way you would write a ticket for a competent colleague who has not seen " +
            "the codebase — state the goal, the constraints that are not obvious from the code, and " +
            "how you will know it worked. Avoid prescribing an implementation unless the " +
            "implementation is the requirement. A vague brief produces a plausible change that " +
            "passes an audit and solves the wrong problem.",
            "Requests to POST /login from one IP should be limited to 5 per minute, returning 429 " +
            "with a Retry-After header. Keep the existing session behaviour unchanged."),

        new("verification", "Verification commands", TaskFields,
            "Shell commands that must exit 0 for the work to count as correct. Your build and tests.",
            "Verification is the part of the pipeline that cannot be talked around. After the " +
            "implementer finishes, FKNRTD.CLI runs each of these commands inside the task's " +
            "worktree; every one must exit with code 0 or the task fails and goes back for repair. " +
            "Agents do not get to interpret the result. Put your build and your test suite here. " +
            "These are trusted project configuration and deliberately run through the platform " +
            "shell, so they may contain pipes and redirection — which also means you should not " +
            "paste in a command you have not read.",
            "dotnet build   and   dotnet test --no-build"),

        new("checks", "The checks row", TaskFields,
            "BUILD, TEST and LINT: your verification commands, sorted by what they look like.",
            "Every verification command is filed into one of these by its own text. A command " +
            "mentioning build or compile counts as BUILD, one mentioning lint or format as LINT, " +
            "one mentioning type or security into its own category, and everything else as TEST. " +
            "The marker is that category's worst result, so one failing command among three shows " +
            "as a failure. A category with no command matching it stays pending forever, which is " +
            "why a project verified only by 'make check' shows TEST and nothing else. All three " +
            "pending usually means the task has not reached its verify stage — but it also happens " +
            "when verification ran and every command was filed under types or security, which have " +
            "no marker of their own on this row. Press I to see what actually ran.",
            "dotnet build -> BUILD,  dotnet test -> TEST,  npm run lint -> LINT"),

        new("repair-round", "Repair rounds", TaskFields,
            "How many times a failed verification is handed back to the implementer to fix.",
            "When verification fails, the implementer is given the failure output and one more " +
            "attempt, up to this limit. One round is the default and is usually right: it absorbs " +
            "the ordinary case of a missed import or a stale snapshot without letting an agent " +
            "grind indefinitely against a test it does not understand. Zero means a single failure " +
            "ends the task immediately. Raising it above two rarely converges and burns budget.",
            "1"),

        // Agent roles
        new("agent", "Agent", Roles,
            "A coding CLI that FKNRTD.CLI launches — Claude Code, Codex, or one you register.",
            "An agent is an entry in your configuration describing a command-line program that can " +
            "be handed a prompt: which executable to run, which arguments to pass for each stage, " +
            "and how the prompt reaches it. Claude and Codex are configured out of the box. Any " +
            "other tool that accepts a prompt on the command line or on standard input can be " +
            "added, and from then on it is eligible for any of the three roles.",
            "fknrtd agent add -id gemini -exe gemini -arg=-p -arg \"{prompt}\""),

        new("lead", "Lead", Roles,
            "Reads the brief and writes the plan. Does not change any files.",
            "The lead runs first, with a profile that for the shipped agents asks them not to edit, " +
            "and turns your brief into a " +
            "plan the implementer will follow. Its value is that it reads the actual codebase before " +
            "anything is written, so the implementer starts from a plan grounded in the real " +
            "structure rather than an assumed one. Pick the model you trust most with judgement " +
            "here; it is a short, cheap stage that determines the quality of a long, expensive one.",
            "claude"),

        new("implementer", "Implementer", Roles,
            "The agent asked to write files. Follows the plan and makes the change.",
            "The implementer works from the lead's plan inside the task's worktree, and it is the " +
            "one role whose profile asks it to edit. If verification fails, this is the agent handed the " +
            "failure output for the repair round. Choosing a different agent here from the lead is " +
            "the point of the tool rather than a quirk of it: a second model reading the first " +
            "model's plan catches assumptions that the model which wrote them cannot see.",
            "codex"),

        new("auditor", "Auditor", Roles,
            "Independently judges the finished work read-only and returns PASS or FAIL.",
            "The auditor runs last, after verification has finished. It compares the finished diff " +
            "against the original brief and answers one " +
            "question: does this actually do what was asked, without doing anything that was not? " +
            "It signals its answer with a verdict marker in its output. A FAIL blocks landing " +
            "regardless of how green the tests are, which is the entire reason the role exists — " +
            "tests confirm the code does what it does, not that it does what you wanted. Its " +
            "read-only posture comes from the audit profile shipped for Claude and Codex, which ask " +
            "those tools not to edit. FKNRTD.CLI launches whatever arguments the profile holds and " +
            "does not enforce it, so an agent you configure yourself is as restricted as you make it.",
            "claude"),

        new("verdict", "Audit verdict", Roles,
            "The line FKNRTD_VERDICT: PASS or FKNRTD_VERDICT: FAIL that an auditor ends its report with.",
            "An auditor communicates its judgement by printing a verdict marker. An audit passes " +
            "only when the PASS marker appears on exactly one line of the report and the FAIL marker " +
            "on none, so neither silence nor a hedged answer that prints both can be read as " +
            "consent. The match ignores case and tolerates a line wrapped in quoting, a list bullet " +
            "or Markdown emphasis, because agents format their conclusions. The prompt itself " +
            "contains the marker text, so a copy of the whole prompt appearing in the report is " +
            "removed before the count. That handles an agent that echoes its instructions back " +
            "verbatim; an agent that quotes only part of them can still leave a marker behind, so " +
            "read the audit log rather than trusting a surprising verdict.",
            "FKNRTD_VERDICT: PASS"),

        new("profile", "Command profile", Roles,
            "The argument list used to launch one agent for one stage.",
            "Each agent carries a profile per stage — plan, implement, audit — plus a default. A " +
            "profile is an array of arguments with the placeholder {prompt} where the prompt " +
            "belongs, which is what lets FKNRTD.CLI pass a multi-paragraph brief containing quotes " +
            "and newlines without any shell interpolation ever touching it. Profiles are also where " +
            "an agent's sandbox flags live, which is how the lead and auditor end up read-only " +
            "while the implementer can write.",
            "[\"exec\", \"--sandbox\", \"read-only\", \"{prompt}\"]"),

        new("prompt-delivery", "Prompt delivery", Roles,
            "Whether the prompt reaches the agent as a command-line argument or on standard input.",
            "Most agents accept a prompt as an argument, which is the default. Some cap argument " +
            "length or handle newlines badly; those want the prompt on standard input instead. If " +
            "an agent truncates long briefs or fails on multi-line ones, switch it to stdin.",
            "fknrtd agent add -id local -exe mytool -stdin"),

        // Workflow stages
        new("stage.brief", "Stage 1 — Brief", Stages,
            "The task is recorded with its brief, agents and verification commands. Nothing runs yet.",
            "Creating the task is what checks it is coherent: that the brief is present, that the " +
            "three named agents exist and are enabled, and that the base branch resolves. A task " +
            "that exists has already passed all of that. The stage itself writes the brief into the " +
            "task's artifacts as brief.md, so the agents and you are reading the same text, and " +
            "passes. A task sits here from creation until you run it."),

        new("stage.worktree", "Stage 2 — Worktree", Stages,
            "A branch and an isolated checkout are created for this task.",
            "In Git mode the task's branch is cut from the base branch and a worktree directory is " +
            "created for it, so every later stage operates on files that are not yours. In " +
            "standalone mode there is nothing to isolate and the stage is skipped, which is why a " +
            "standalone task shows a skip marker here rather than a failure.",
            StandaloneSummary: "Skipped: a standalone workspace has nothing to isolate."),

        new("stage.plan", "Stage 3 — Plan", Stages,
            "The lead agent reads the code and the brief, and writes the plan.",
            "The lead is launched against the worktree with its plan profile, which for the shipped " +
            "agents asks them not to edit. Its whole output stream goes to the task's log. What is " +
            "carried forward is not that stream: the final answer is extracted from it, saved as " +
            "plan.md, and it is plan.md that the implementer's prompt quotes. Press P to read the " +
            "prompt the implementer will actually be sent."),

        new("stage.implement", "Stage 4 — Implement", Stages,
            "The implementer makes the change in the worktree. The only agent asked to write files.",
            "The implementer is launched with the brief and the plan, and is the one agent whose " +
            "profile asks it to edit. It is not the only thing that writes to the worktree — your " +
            "verification commands run there too, and a build or a test writes whatever a build or " +
            "a test writes. Its work is not committed here: if the workspace commits " +
            "agent changes automatically, that happens once verification and the audit have both " +
            "passed, immediately before the task becomes landable.",
            StandaloneSummary: "The implementer edits the project folder. The only agent asked to write files."),

        new("stage.verify", "Stage 5 — Verify", Stages,
            "Your verification commands run. Every one must exit 0.",
            "Each configured command runs in the worktree with a timeout. A non-zero exit sends the " +
            "task back to the implementer for a repair round if any remain, and fails it outright " +
            "if none do. No agent is consulted about what the exit code meant."),

        new("stage.audit", "Stage 6 — Audit", Stages,
            "The auditor judges the finished work read-only and must return PASS.",
            "The auditor sees the brief and the finished state of the worktree, and its profile " +
            "asks it not to change either. Anything other than an explicit PASS verdict — a FAIL, " +
            "no verdict at all, a crash — leaves the task un-landable."),

        new("stage.readytoland", "Stage 7 — Ready to land", Stages,
            "Verified and audited. Waiting for you, and only you, to merge it.",
            "The task has done everything it can do on its own. It will sit here indefinitely; " +
            "nothing progresses without your typed confirmation. Read the diff in the worktree " +
            "before you land it — this stage exists so that you can.",
            StandaloneSummary: "Verified and audited. Waiting for you, and only you, to accept it."),

        new("stage.land", "Stage 8 — Land", Stages,
            "The task branch is merged into the base branch.",
            "The final stage merges the task branch back. In standalone mode there is no merge and " +
            "the stage records that the verified work is already in place in the folder.",
            StandaloneSummary: "The verified work is recorded as accepted; it is already in the folder."),

        new("pipeline", "The pipeline panel", Concepts,
            "Every task in the workspace, and the one you have highlighted in detail.",
            "The list gives each task a marker for its status and enough of its title to recognise " +
            "it. Under it, the highlighted task gets its stage strip, its progress bar and the " +
            "three agents assigned to it. The keys that act on a task — Enter, I, L, R, C, G, X — " +
            "all act on the highlighted one, so this panel is what the rest of the dashboard is " +
            "pointed at. F searches it once there are more tasks than rows.",
            "\u203a  \u2666 FKN-20260917-101500-a1b2  Add rate limiting to the login endpoint"),

        new("radar", "The agent radar", Concepts,
            "What each configured agent is doing at this moment, and what it last said.",
            "One row per agent in the configuration, not per agent that is running: an agent that " +
            "has never started is listed as offline rather than left out, because its absence from " +
            "the list would be indistinguishable from its absence from the workspace. The text " +
            "beside each one is its intent, which comes from reading its own output as it arrives " +
            "— the last thing it said, or the tool it most recently used. A is the roster, where " +
            "the list can be changed.",
            "\u25ba Codex Implementer      Rewriting CsvReader.Parse"),

        new("sentinel", "The conflict sentinel", Coordination,
            "Where two agents are about to get in each other's way, worst first.",
            "It compares the paths agents have reserved and the paths they report touching, and " +
            "shows what that comparison found. It watches and does not act: nothing here stops a " +
            "task, holds a lock or waits for anything, so an overlap is something for you to decide " +
            "about rather than something the pipeline is already handling. When it says Safe it " +
            "means nothing that counts was found, which is not the same as nothing overlapping — " +
            "read the conflict entry for what counts. K opens it in full.",
            "\u00d7 100 Live collision: claude and codex: src/auth.cs"),

        new("progress-bar", "The progress bar", Stages,
            "How many of a task's eight stages are behind it, and nothing more.",
            "The bar under the stage strip counts the stages that have passed or been skipped and " +
            "divides by eight. A skipped stage counts as done, which is why a task with no " +
            "verification commands moves faster through it than one with them. It measures the " +
            "pipeline's position and not the work: an implement stage that has been running for " +
            "twenty minutes and one that started a second ago look identical here. Press L to see " +
            "what the agent is actually doing.",
            "\u2588\u2588\u2588\u2588\u2588\u2588\u2588\u2588\u2591\u2591\u2591 75%   six of the eight stages are done"),

        new("resources", "The resource line", Concepts,
            "What this dashboard costs the machine. Not what the agents cost.",
            "CPU and RAM for the FKNRTD.CLI process itself, sampled between refreshes and averaged " +
            "across every core. It is not the agents: those run as their own processes and their " +
            "cost does not appear here or anywhere else in this product. A number close to zero is " +
            "the expected reading, because the dashboard spends almost every refresh waiting.",
            "FKNRTD.CLI CPU 3%  RAM 48 MB"),

        new("ahead-behind", "Ahead and behind", Concepts,
            "How far your branch has diverged from the remote it tracks.",
            "The arrows in the header are commits your checkout has that its upstream does not, and " +
            "commits the upstream has that you do not. They come from Git and describe your own " +
            "checkout rather than any task: landing adds to the first number, and nothing in this " +
            "product ever pushes or pulls, so the second only moves when you fetch. A branch with " +
            "no upstream shows zero for both — there is nothing to compare against, which is not " +
            "the same as being up to date.",
            "\u21911\u21930   one commit to push, nothing to pull"),

        new("changed", "Changed files", Concepts,
            "Uncommitted changes in your own checkout, not in any task's worktree.",
            "The count in the header is what 'git status' would list at the workspace root: your " +
            "own uncommitted work. An agent's edits do not appear here, because in a Git workspace " +
            "they happen in the task's own worktree. It matters at exactly one moment: landing " +
            "refuses while this is non-zero, unless requireCleanTreeForLanding is off, because " +
            "uncommitted work at the root is work no stage ever looked at sitting where the " +
            "verified change is about to arrive.",
            "\u2206 2 changed   two files with uncommitted edits"),

        new("stage-strip", "The stage strip", Stages,
            "The row of eight initials under a task: its whole pipeline in one line.",
            "b is Brief, w Worktree, p Plan, i Implement, v Verify, a Audit, r Ready to land and l " +
            "Land, in the order they run. Each initial carries the marker for that stage's state, so " +
            "the strip reads as a progress bar with the reason built in: a cross tells you which " +
            "stage failed, and a lozenge tells you which was skipped rather than run. Press I for the " +
            "same thing with the stages named in full.",
            "b√ w√ p√ i√ v× a○ r○ l○   verification failed on a task that planned and implemented cleanly"),

        // Task status
        new("status.queued", "Queued", Statuses,
            "Waiting for you to start it. Press Enter to run it.",
            "The task exists with everything it needs and is waiting for you to start it. For a task " +
            "that has never run, nothing has been launched and no worktree exists yet. A task that " +
            "you retried with R is also Queued — there, its worktree and every stage that already " +
            "passed are still there, and running it resumes from the first stage that has not " +
            "passed rather than starting over."),

        new("status.running", "Running", Statuses,
            "An agent or a verification command is executing right now.",
            "Some stage is live. The log view shows its output as it arrives. A running task can be " +
            "asked to cancel: the request is noticed within about half a second, and the running " +
            "process and everything it started are then killed rather than being waited for. " +
            "Whatever the agent had already written to disk stays where it wrote it."),

        new("status.waiting", "Waiting", Statuses,
            "Paused mid-pipeline, waiting for something outside the task.",
            "Nothing in the pipeline sets this today. The status exists in the task format and the " +
            "dashboard can draw it, but no stage assigns it: claims are advisory and the " +
            "orchestrator never waits on one. If a task cannot get the agent lease it needs it " +
            "waits briefly and then fails, rather than sitting here."),

        new("status.failed", "Failed", Statuses,
            "A stage failed. Read the log, fix the cause, then retry.",
            "Verification failed with no repair rounds left, the auditor returned FAIL, or an agent " +
            "crashed. The error is on the task and the full output is in its log. Retry resets the " +
            "failed stages and runs again from there; it does not start over from the beginning."),

        new("status.readytoland", "Ready to land", Statuses,
            "Verified and audited. Nothing further happens without your confirmation.",
            "The finished state. Inspect the diff, then land it or leave it — a task can sit here " +
            "indefinitely without costing anything."),

        new("status.landed", "Landed", Statuses,
            "Finished. In a Git workspace, merged into the base branch.",
            "In a Git workspace the task's branch has been merged into your base branch and the " +
            "worktree is still on disk until you clean it up, in case you want to look at it. In a " +
            "standalone workspace nothing is merged, because nothing was ever branched: landing " +
            "records that the verified work already in the folder is final."),

        new("status.cancelled", "Cancelled", Statuses,
            "Stopped on request. Can be retried.",
            "You asked the task to stop. Whatever the implementer had already written is still " +
            "there. In a Git workspace that is its own worktree, isolated from your checkout. In a " +
            "standalone workspace the agent was editing the folder directly, so those edits are " +
            "sitting in your working copy right now — press V to read them before you do anything " +
            "else."),

        // Stage state markers
        new("stagestate.pending", "Pending  ○", StageStates,
            "Not reached yet.",
            "The stage is waiting to run. It carries no outcome, and it may still end up being " +
            "skipped rather than run. It does not always mean the pipeline has never been here: a " +
            "repair round puts implement, verify and audit back to Pending so they can be run " +
            "again, and R does the same to whichever stages failed."),
        new("stagestate.running", "Running  ►", StageStates,
            "Executing now.",
            "This is the stage the task is currently in. Its output is being written to the task log as it " +
            "arrives, which is what the log view shows."),
        new("stagestate.passed", "Passed  √", StageStates,
            "Completed successfully.",
            "The stage finished and met its condition: the agent exited cleanly, or every verification " +
            "command returned zero. The pipeline moved on."),
        new("stagestate.failed", "Failed  ×", StageStates,
            "Completed unsuccessfully.",
            "The stage ran and did not meet its condition. Its log holds the full output that " +
            "explains why. It does not necessarily mean the task stopped: a failed verification or " +
            "audit is handed straight back for another attempt while the repair budget lasts, and " +
            "only becomes the end of the run once that budget is gone."),
        new("stagestate.skipped", "Skipped  ◊", StageStates,
            "Deliberately not applicable — not a failure.",
            "The stage did not apply and was passed over deliberately. Two stages can be skipped: the " +
            "worktree stage in a standalone workspace, where there is no repository to isolate " +
            "against, and the verify stage when the task has no verification commands — which is " +
            "worth noticing, because it means nothing independent checked the work. A skip is not a " +
            "failure and does not block landing."),

        // Agent state markers
        new("agentstate.unknown", "Not reporting  ?", AgentStates,
            "It was working, then stopped saying anything.",
            "An agent is put here when it was planning, running or reviewing and then stopped " +
            "reporting for longer than agentStaleAfterSeconds - two minutes by default. It does " +
            "not mean the agent has died: a long compile, a long download or a model thinking hard " +
            "all look the same from outside, because the only evidence is output and there has not " +
            "been any. Press L to see what it last wrote. If that was a while ago and nothing has " +
            "followed, C cancels the task and R resets it to run again. Raising " +
            "agentStaleAfterSeconds on the settings screen widens the window before this appears.",
            "fknrtd task cancel FKN-...   then   fknrtd task retry FKN-..."),
        new("agentstate.offline", "Offline  ○", AgentStates,
            "Configured but not currently running or reporting.",
            "The normal resting state. It does not mean the agent is broken; run doctor to check " +
            "whether its executable can be found."),
        new("agentstate.idle", "Idle  ○", AgentStates,
            "Present and reporting, with nothing assigned.",
            "The agent has reported in recently and has no task assigned. It is available for the next one."),
        new("agentstate.planning", "Planning  ◊", AgentStates,
            "Reading the brief and the code, writing a plan.",
            "The agent is executing a plan stage, with a profile that for the shipped agents asks it " +
            "to read the worktree without editing it."),
        new("agentstate.running", "Running  ►", AgentStates,
            "Working. If it is the implementer, it is writing files.",
            "The agent is executing an implement stage or another long operation. If it is the " +
            "implementer on a task, this is the stage where the change itself is being written — " +
            "though it is not the only thing that touches the worktree, since your verification " +
            "commands run there too."),
        new("agentstate.reviewing", "Reviewing  ♦", AgentStates,
            "Auditing finished work.",
            "The agent is executing an audit stage, with a profile that asks it not to edit, and " +
            "will end by printing a PASS or FAIL verdict that decides whether the task can be " +
            "landed."),
        new("agentstate.waiting", "Waiting  ▌", AgentStates,
            "Paused for something external.",
            "Set when the agent's run was cancelled and its process was stopped. Despite the name " +
            "nothing pauses an agent for a rate limit or a claimed path: the budget figures on U are " +
            "reported, not enforced, and claims only warn."),
        new("agentstate.blocked", "Blocked  ■", AgentStates,
            "Cannot proceed. Needs attention.",
            "Nothing inside FKNRTD.CLI sets this. It is here for an agent session the command " +
            "center did not launch, which can report it with 'fknrtd telemetry report -state " +
            "blocked'. That reports the state and nothing else: no event is recorded, so there is " +
            "no stored reason to read afterwards."),
        new("agentstate.failed", "Failed  ×", AgentStates,
            "The last thing it attempted ended badly.",
            "The last thing the agent attempted ended badly. Its exit code and its full output are recorded " +
            "against the task it was working on."),
        new("agentstate.completed", "Completed  √", AgentStates,
            "Its process exited successfully.",
            "The agent's process exited with a success code. That is all it means: it is set before " +
            "anything reads what the agent actually produced. An auditor that exits cleanly while " +
            "returning a FAIL verdict is Completed here and its audit stage is Failed, which is not " +
            "a contradiction — the tool ran fine and the answer was no."),

        // Coordination
        new("claim", "Claim", Coordination,
            "A time-limited declaration that an agent is working on specific files.",
            "A claim is how two agents working at once avoid editing the same file. An agent " +
            "registers the paths it is about to touch, in read or write mode, with an expiry. " +
            "FKNRTD.CLI compares live claims and raises a conflict when they overlap in a way that " +
            "matters. A claim carries an expiry so that a crashed agent stops being treated as an " +
            "authority on a path, which is why a long operation has to renew its claim rather than " +
            "set a long one. Expiring is not disappearing: the record stays until somebody releases " +
            "it, and until then it is reported as a stale claim. Claims only ever warn — nothing " +
            "in the pipeline waits for one or refuses to run because of one.",
            "fknrtd claim add -agent codex -path src/auth.cs -mode write -ttl 300"),

        new("claim-mode", "Claim mode", Coordination,
            "Read claims coexist; a write claim conflicts with any other claim on the same path.",
            "Two agents reading the same file is fine and registers as safe. A write claim " +
            "overlapping any other claim on the same path is a collision, because at least one of " +
            "them is about to invalidate what the other is doing."),

        new("ttl", "TTL", Coordination,
            "How many seconds a claim stays live before it expires on its own.",
            "Time to live, in seconds, defaulting to 300. An agent that is still working must renew " +
            "before this elapses. The deliberate consequence is that an agent which dies releases " +
            "its paths automatically rather than deadlocking the workspace."),

        new("conflict", "Conflict", Coordination,
            "An overlap between what two agents are doing, scored by how much it matters.",
            "FKNRTD.CLI compares live claims and the paths agents report touching, and classifies " +
            "each overlap. The header shows the worst one it currently sees. Safe does not mean no " +
            "overlap: two claims by the same agent are ignored, and so is an overlap where both " +
            "sides are reading. What counts is two different agents overlapping with at least one " +
            "of them writing."),

        new("conflict.safe", "Safe  √", Coordination,
            "No overlap that counts. The header shows this when there is nothing to report.",
            "Safe does not mean nothing is happening or that nothing overlaps: two claims held by " +
            "the same agent are ignored, and so is an overlap where both sides are only reading. " +
            "What it means is that no two different agents have claimed the same path with at " +
            "least one of them writing. It is also what you see when nobody has declared anything " +
            "at all, because an agent that makes no claims cannot be warned about — so read it as " +
            "\"nothing detected\" rather than as \"nothing to detect\"."),
        new("conflict.collision", "Collision  ×", Coordination,
            "Two agents have claimed the same path in one worktree, and at least one is writing.",
            "The highest severity, because both sides are working in the same directory rather than " +
            "in separate worktrees, so one can overwrite work the other has not finished. It does " +
            "not take two writers: one writer and one reader in the same tree counts, and a claim is " +
            "a declaration of intent rather than evidence that anything has been written yet. Cancel " +
            "one of them, or let one land before the other continues."),

        new("conflict.mergerisk", "Merge risk  ∆", Coordination,
            "Two worktrees have claimed the same path.",
            "The same path is claimed from two different worktrees. This is a path overlap and not a " +
            "demonstrated merge conflict: nothing here asks Git whether the two sides would merge, " +
            "and nothing checks whether they edited the same part of the file. Git may well merge " +
            "both cleanly. Treat it as somewhere to look before landing the second one."),

        new("conflict.staleclaim", "Stale claim  ∆", Coordination,
            "A claim outlived the agent holding it.",
            "The claim is past its expiry. It is only the expiry that is checked — whether the agent " +
            "is still alive and reporting is not. It does not clear on its own: the claim file stays " +
            "where it is and keeps being reported until somebody renews it with 'fknrtd claim renew' " +
            "or releases it with 'fknrtd claim release'."),

        new("message", "Message", Coordination,
            "A note recorded from one agent to another, with an acknowledgement state.",
            "The message bus is a durable record of agent-to-agent hand-offs — an implementer " +
            "announcing that work is ready for audit, for example. Messages are recorded and shown " +
            "in the dashboard; they are not a transport that interrupts a running agent. Each one " +
            "stays unacknowledged until something acknowledges it.",
            "fknrtd message send -from codex -to claude -text \"Ready for audit\""),

        new("event", "Event", Coordination,
            "An append-only log line of something that happened, with a severity.",
            "Events are the workspace's history: the workspace being set up, tasks created and " +
            "cancelled, workflows started, sent back for repair, failed, made ready and landed, and " +
            "messages sent between agents. Per-stage detail is kept on the task record rather than " +
            "here. The file is append-only and rotated, so it is the first place to look when you " +
            "want the sequence of what happened rather than the state it left behind.",
            "fknrtd events -limit 100"),

        new("telemetry", "Telemetry report", Coordination,
            "How an agent outside a task tells the dashboard what it is doing.",
            "An agent that FKNRTD.CLI did not launch — you, in another terminal, or a tool with its " +
            "own hooks — can still appear on the radar by reporting its state. That is what makes " +
            "the conflict sentinel useful across sessions it does not control. Reported progress " +
            "must come with a truthful basis describing what the number is measured against, " +
            "because an invented percentage is worse than none.",
            "fknrtd telemetry report -agent cline -state running -intent \"Editing auth\""),

        // Budget and usage
        new("usage", "Usage", Budget,
            "How much of each agent's rate-limit budget is left.",
            "Coding agents meter usage over rolling windows, and a long task that exhausts one " +
            "mid-flight fails in a confusing way. The header shows what is left so you can see it " +
            "coming. Codex reports on demand; Claude reports through its statusline integration; " +
            "anything else can be fed in manually.",
            "fknrtd usage refresh codex"),

        new("context", "Context window", Budget,
            "How much of the current conversation's context an agent has left.",
            "Distinct from the rate-limit windows: this is how much room remains in the session an " +
            "agent is working in, not how much quota remains for the day. It drops as a single " +
            "long task accumulates output."),

        new("five-hour", "5-hour window", Budget,
            "Rate-limit budget remaining in the rolling five-hour window.",
            "The short window, which is the one a burst of activity exhausts first. It refills " +
            "continuously rather than resetting on a clock edge."),

        new("weekly", "7-day window", Budget,
            "Rate-limit budget remaining in the rolling seven-day window.",
            "The long window. Running low here constrains the whole week, so it is worth watching " +
            "before starting anything large."),

        new("statusline", "Claude statusline integration", Budget,
            "Installs a line in Claude Code that reports usage back to FKNRTD.CLI.",
            "Claude Code renders a configurable status line and hands it a payload containing " +
            "session usage. Installing the integration points that line at FKNRTD.CLI, so every " +
            "refresh feeds real numbers into the dashboard and shows the FKN badge in Claude. It " +
            "writes to your Claude settings file and takes effect after a restart.",
            "fknrtd integration install-claude-statusline"),

        // Configuration
        new("max-parallel", "Max parallel agents", Configuration,
            "How many tasks may run at once. Defaults to 4.",
            "The dashboard refuses to start another task past this limit. Each running task means a " +
            "live agent process and its own worktree, so the practical ceiling is your machine and " +
            "your rate-limit budget rather than the tool."),

        new("auto-commit", "Auto-commit agent changes", Configuration,
            "Commits the work to the task branch once it has passed verification and its audit.",
            "On by default in Git mode, and unavailable in standalone mode where there is no " +
            "repository. The commit happens after verification and the audit have both succeeded, " +
            "immediately before the task becomes ready to land — not when the implementer finishes. " +
            "A task that fails verification therefore has its work in the worktree but not committed, " +
            "which is what V reads when it shows you the uncommitted half of a change."),

        new("timeouts", "Timeouts", Configuration,
            "Per-agent and per-verification-command limits, in seconds.",
            "An agent run is capped at one hour and a verification command at ten minutes by " +
            "default. A process that exceeds its limit is killed and reported as a timeout rather " +
            "than left to hang the pipeline. Raise them for genuinely long builds; lower them if " +
            "you would rather fail fast.",
            "agentTimeoutSeconds, verificationTimeoutSeconds in .fknrtd/config.json"),

        new("config", "Configuration file", Configuration,
            "Everything about a workspace lives in .fknrtd/config.json and is safe to edit by hand.",
            "The configuration is plain JSON with no schema tricks. Agents, defaults, timeouts and " +
            "mode all live there. Validate it after editing. Validation is a shape check, not a " +
            "readiness check: it confirms the file parses, that maxParallelAgents and " +
            "dashboardRefreshMilliseconds are usable, that no agent id is defined twice or contains " +
            "anything but letters, numbers, hyphens and underscores, and that every agent has an " +
            "executable name and at least one command profile. It never looks for those executables " +
            "— 'fknrtd doctor' is what does that — and it does not check the timeouts or the " +
            "repair limit at all.",
            "fknrtd config path   then   fknrtd config validate"),
    ];

    public static IReadOnlyList<GlossaryEntry> All => Entries;

    public static IReadOnlyList<string> Categories { get; } =
        Entries.Select(entry => entry.Category).Distinct(StringComparer.Ordinal).ToArray();

    public static IEnumerable<GlossaryEntry> InCategory(string category) =>
        Entries.Where(entry => entry.Category.Equals(category, StringComparison.Ordinal));

    /// <summary>
    /// Resolves a term the way an operator would type it. An exact key wins; otherwise a bare word
    /// matches the unprefixed concept before any dotted family member, so "brief" is the thing you
    /// write rather than the stage named after it.
    /// </summary>
    public static GlossaryEntry? Find(string? term)
    {
        if (string.IsNullOrWhiteSpace(term))
        {
            return null;
        }

        var needle = term.Trim().Replace(' ', '-');
        return Entries.FirstOrDefault(entry => entry.Term.Equals(needle, StringComparison.OrdinalIgnoreCase))
               ?? Entries.FirstOrDefault(entry =>
                   entry.Title.Equals(needle.Replace('-', ' '), StringComparison.OrdinalIgnoreCase))
               ?? Entries.FirstOrDefault(entry =>
                   entry.Term.EndsWith("." + needle, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Ranked substring search over term, title and summary, for the help browser.</summary>
    public static IReadOnlyList<GlossaryEntry> Search(string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return Entries;
        }

        var needle = query.Trim();
        return Entries
            .Select(entry => (entry, rank: Rank(entry, needle)))
            .Where(item => item.rank > 0)
            .OrderByDescending(item => item.rank)
            .Select(item => item.entry)
            .ToArray();
    }

    private static int Rank(GlossaryEntry entry, string needle)
    {
        if (entry.Term.Equals(needle, StringComparison.OrdinalIgnoreCase))
        {
            return 100;
        }

        if (entry.Title.StartsWith(needle, StringComparison.OrdinalIgnoreCase))
        {
            return 80;
        }

        if (entry.Term.Contains(needle, StringComparison.OrdinalIgnoreCase))
        {
            return 60;
        }

        if (entry.Title.Contains(needle, StringComparison.OrdinalIgnoreCase))
        {
            return 50;
        }

        if (entry.Summary.Contains(needle, StringComparison.OrdinalIgnoreCase))
        {
            return 30;
        }

        return entry.Detail.Contains(needle, StringComparison.OrdinalIgnoreCase) ? 10 : 0;
    }
}
