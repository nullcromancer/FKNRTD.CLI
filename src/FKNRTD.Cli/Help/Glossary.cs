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
public sealed record GlossaryEntry(
    string Term,
    string Title,
    string Category,
    string Summary,
    string Detail,
    string Example = "");

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
            "result, and a third agent audits it without write access. Nothing reaches your base " +
            "branch until you type the confirmation yourself. The point is that no single agent " +
            "both writes the change and decides the change is good.",
            "fknrtd            opens the command center for the folder you are standing in"),

        new("workspace", "Workspace", Concepts,
            "One folder that FKNRTD.CLI manages, holding its state in a .fknrtd directory.",
            "A workspace is any folder you have pointed FKNRTD.CLI at. Everything it knows about " +
            "that folder — configuration, tasks, events, logs, messages — lives in a .fknrtd " +
            "directory at the folder's root. Nothing is written anywhere else, so deleting .fknrtd " +
            "returns the folder to exactly its previous state. Running fknrtd in a folder that has " +
            "no workspace yet creates one with sensible defaults rather than asking you to " +
            "configure anything first.",
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
            "you clean the task up, the worktree directory is removed and the branch is kept.",
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
            "restarts, they can be re-run and retried, and the full log of each stage stays on " +
            "disk. Task identifiers encode the UTC moment of creation so they sort chronologically.",
            "FKN-20260917-101500-a1b2c3d4e5"),

        new("land", "Landing", Concepts,
            "Merging a verified, audited task branch into the base branch. Always manual.",
            "Landing is the only step that changes your base branch, and it is the only step " +
            "FKNRTD.CLI will never take on its own. A task becomes landable only after " +
            "verification passed and an auditor that could not write to the worktree returned a " +
            "PASS verdict. Even then you have to confirm by typing the word LAND in full — not y, " +
            "not Enter — because a typo should not be able to merge anything.",
            "fknrtd task land FKN-... -confirm LAND"),

        new("cleanup", "Cleanup", Concepts,
            "Removing a finished task's worktree directory while keeping its branch and record.",
            "Cleanup deletes the worktree directory a task was working in. It does not delete the " +
            "task's record, its logs, or its Git branch, so nothing you might want to read later is " +
            "lost — it only reclaims the disk space of a second checkout. A running task cannot be " +
            "cleaned up. Like landing, it requires a typed confirmation.",
            "fknrtd task cleanup FKN-... -confirm REMOVE"),

        new("doctor", "Doctor", Concepts,
            "A pre-flight check that reports what is ready and what will fail before you rely on it.",
            "Doctor inspects everything a task run depends on and reports each item as passing, " +
            "failing, or optional: whether the workspace is readable, whether Git is present when " +
            "the mode needs it, whether each configured agent's executable can actually be found " +
            "and launched, and whether the configuration parses. Run it after installing or " +
            "reconfiguring anything. A required check that fails will stop a task; an optional one " +
            "that fails only removes a capability.",
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
            "The lead runs first, with read-only access to the worktree, and turns your brief into a " +
            "plan the implementer will follow. Its value is that it reads the actual codebase before " +
            "anything is written, so the implementer starts from a plan grounded in the real " +
            "structure rather than an assumed one. Pick the model you trust most with judgement " +
            "here; it is a short, cheap stage that determines the quality of a long, expensive one.",
            "claude"),

        new("implementer", "Implementer", Roles,
            "The only agent allowed to write files. Follows the plan and makes the change.",
            "The implementer works from the lead's plan inside the task's worktree, and it is the " +
            "single role with write access. If verification fails, this is the agent handed the " +
            "failure output for the repair round. Choosing a different agent here from the lead is " +
            "the point of the tool rather than a quirk of it: a second model reading the first " +
            "model's plan catches assumptions that the model which wrote them cannot see.",
            "codex"),

        new("auditor", "Auditor", Roles,
            "Independently judges the finished work read-only and returns PASS or FAIL.",
            "The auditor runs last, after verification has already passed, and cannot write to the " +
            "worktree. It compares the finished diff against the original brief and answers one " +
            "question: does this actually do what was asked, without doing anything that was not? " +
            "It signals its answer with a verdict marker in its output. A FAIL blocks landing " +
            "regardless of how green the tests are, which is the entire reason the role exists — " +
            "tests confirm the code does what it does, not that it does what you wanted.",
            "claude"),

        new("verdict", "Audit verdict", Roles,
            "The line FKNRTD_VERDICT: PASS or FKNRTD_VERDICT: FAIL that an auditor ends its report with.",
            "An auditor communicates its judgement by printing a verdict marker. An audit passes " +
            "only when the PASS marker appears on exactly one line of the report and the FAIL marker " +
            "on none, so neither silence nor a hedged answer that prints both can be read as " +
            "consent. The match ignores case and tolerates a line wrapped in quoting, a list bullet " +
            "or Markdown emphasis, because agents format their conclusions. The prompt itself " +
            "contains the marker text, so lines that merely echo the instruction are stripped before " +
            "the count and only the agent's own conclusion is read.",
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
            "The first stage simply confirms the task is complete and coherent before anything " +
            "expensive starts: the brief is present, the three named agents exist and are enabled, " +
            "and the base branch resolves. A task sits here from creation until you run it."),

        new("stage.worktree", "Stage 2 — Worktree", Stages,
            "A branch and an isolated checkout are created for this task.",
            "In Git mode the task's branch is cut from the base branch and a worktree directory is " +
            "created for it, so every later stage operates on files that are not yours. In " +
            "standalone mode there is nothing to isolate and the stage is skipped, which is why a " +
            "standalone task shows a skip marker here rather than a failure."),

        new("stage.plan", "Stage 3 — Plan", Stages,
            "The lead agent reads the code and the brief, and writes the plan. Read-only.",
            "The lead is launched against the worktree with its plan profile, which is configured " +
            "read-only. Its full output is written to the task's log and carried forward into the " +
            "implementer's prompt."),

        new("stage.implement", "Stage 4 — Implement", Stages,
            "The implementer makes the change in the worktree. The only stage that writes files.",
            "The implementer is launched with the brief and the plan, and is the one agent given " +
            "write access to the worktree. Its work is not committed here: if the workspace commits " +
            "agent changes automatically, that happens once verification and the audit have both " +
            "passed, immediately before the task becomes landable."),

        new("stage.verify", "Stage 5 — Verify", Stages,
            "Your verification commands run. Every one must exit 0.",
            "Each configured command runs in the worktree with a timeout. A non-zero exit sends the " +
            "task back to the implementer for a repair round if any remain, and fails it outright " +
            "if none do. No agent is consulted about what the exit code meant."),

        new("stage.audit", "Stage 6 — Audit", Stages,
            "The auditor judges the finished work read-only and must return PASS.",
            "The auditor sees the brief and the finished state of the worktree, and cannot change " +
            "either. Anything other than an explicit PASS verdict — a FAIL, no verdict at all, a " +
            "crash — leaves the task un-landable."),

        new("stage.readytoland", "Stage 7 — Ready to land", Stages,
            "Verified and audited. Waiting for you, and only you, to merge it.",
            "The task has done everything it can do on its own. It will sit here indefinitely; " +
            "nothing progresses without your typed confirmation. Read the diff in the worktree " +
            "before you land it — this stage exists so that you can."),

        new("stage.land", "Stage 8 — Land", Stages,
            "The task branch is merged into the base branch.",
            "The final stage merges the task branch back. In standalone mode there is no merge and " +
            "the stage records that the verified work is already in place in the folder."),

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
            "Created but never started. Press Enter to run it.",
            "The task exists with everything it needs and is waiting for you to start it. Nothing " +
            "has been launched and no worktree exists yet."),

        new("status.running", "Running", Statuses,
            "An agent or a verification command is executing right now.",
            "Some stage is live. The log view shows its output as it arrives. A running task can be " +
            "asked to cancel, which takes effect once the current external process returns."),

        new("status.waiting", "Waiting", Statuses,
            "Paused mid-pipeline, waiting for something outside the task.",
            "The task has not failed but cannot proceed on its own — typically a blocked resource " +
            "or a claim held by another agent. The events panel records why."),

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
            "Merged into the base branch. Done.",
            "The work is in your base branch. The worktree is still on disk until you clean it up, " +
            "in case you want to look at it."),

        new("status.cancelled", "Cancelled", Statuses,
            "Stopped on request. Can be retried.",
            "You asked the task to stop. Whatever the implementer had already written to the " +
            "worktree is still there and still isolated from your checkout."),

        // Stage state markers
        new("stagestate.pending", "Pending  ○", StageStates,
            "Not reached yet.",
            "The pipeline has not reached this stage yet. It carries no outcome, and it may still end up " +
            "being skipped rather than run."),
        new("stagestate.running", "Running  ►", StageStates,
            "Executing now.",
            "This is the stage the task is currently in. Its output is being written to the task log as it " +
            "arrives, which is what the log view shows."),
        new("stagestate.passed", "Passed  √", StageStates,
            "Completed successfully.",
            "The stage finished and met its condition: the agent exited cleanly, or every verification " +
            "command returned zero. The pipeline moved on."),
        new("stagestate.failed", "Failed  ×", StageStates,
            "Completed unsuccessfully. The task stopped here.",
            "The stage ran and did not meet its condition, so the task stopped here rather than carrying a " +
            "known-bad result forward. Its log holds the full output that explains why."),
        new("stagestate.skipped", "Skipped  ◊", StageStates,
            "Deliberately not applicable — not a failure.",
            "The stage did not apply and was passed over deliberately. Two stages can be skipped: the " +
            "worktree stage in a standalone workspace, where there is no repository to isolate " +
            "against, and the verify stage when the task has no verification commands — which is " +
            "worth noticing, because it means nothing independent checked the work. A skip is not a " +
            "failure and does not block landing."),

        // Agent state markers
        new("agentstate.offline", "Offline  ○", AgentStates,
            "Configured but not currently running or reporting.",
            "The normal resting state. It does not mean the agent is broken; run doctor to check " +
            "whether its executable can be found."),
        new("agentstate.idle", "Idle  ○", AgentStates,
            "Present and reporting, with nothing assigned.",
            "The agent has reported in recently and has no task assigned. It is available for the next one."),
        new("agentstate.planning", "Planning  ◊", AgentStates,
            "Reading the brief and the code, writing a plan. Read-only.",
            "The agent is executing a plan stage. It has read access to the worktree and cannot change " +
            "anything in it."),
        new("agentstate.running", "Running  ►", AgentStates,
            "Working. If it is the implementer, it is writing files.",
            "The agent is executing an implement stage or another long operation. If it is the implementer " +
            "on a task, this is the one point in the pipeline where files are being written."),
        new("agentstate.reviewing", "Reviewing  ♦", AgentStates,
            "Auditing finished work read-only.",
            "The agent is executing an audit stage read-only, and will end by printing a PASS or FAIL " +
            "verdict that decides whether the task can be landed."),
        new("agentstate.waiting", "Waiting  ▌", AgentStates,
            "Paused for something external.",
            "The agent has paused for something outside itself — most often an exhausted rate-limit window, " +
            "or a path claimed by another agent."),
        new("agentstate.blocked", "Blocked  ■", AgentStates,
            "Cannot proceed. Needs attention.",
            "The agent has hit something it cannot resolve on its own and has stopped making progress. The " +
            "events panel records what it ran into."),
        new("agentstate.failed", "Failed  ×", AgentStates,
            "The last thing it attempted ended badly.",
            "The last thing the agent attempted ended badly. Its exit code and its full output are recorded " +
            "against the task it was working on."),
        new("agentstate.completed", "Completed  √", AgentStates,
            "Finished its assigned stage successfully.",
            "The agent finished its assigned stage successfully and handed the task on to the next stage."),

        // Coordination
        new("claim", "Claim", Coordination,
            "A time-limited declaration that an agent is working on specific files.",
            "A claim is how two agents working at once avoid editing the same file. An agent " +
            "registers the paths it is about to touch, in read or write mode, with an expiry. " +
            "FKNRTD.CLI compares live claims and raises a conflict when they overlap in a way that " +
            "matters. Claims expire on their own so a crashed agent cannot hold a path forever, " +
            "which is also why a long operation has to renew its claim rather than set a long one.",
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
            "FKNRTD.CLI continuously compares live claims and the paths agents report touching, and " +
            "classifies each overlap. The header shows the worst one it currently sees. Safe means " +
            "no overlap at all."),

        new("conflict.collision", "Collision  ×", Coordination,
            "Two agents are writing the same path. Act now.",
            "The highest severity. One agent is about to overwrite work the other has not finished. " +
            "Cancel one of them or let one land before the other continues."),

        new("conflict.mergerisk", "Merge risk  ∆", Coordination,
            "Different tasks touch the same file and will conflict at merge time.",
            "Not urgent, but the second task to land will need a manual merge. Landing them in the " +
            "order they finished usually avoids the worst of it."),

        new("conflict.staleclaim", "Stale claim  ∆", Coordination,
            "A claim outlived the agent holding it.",
            "The claim is past its TTL, or its agent has stopped reporting. It clears on its own; " +
            "it is shown so an unexplained block has a visible cause."),

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
            "Commits the implementer's work to the task branch as the implement stage ends.",
            "On by default in Git mode, and unavailable in standalone mode where there is no " +
            "repository. Keeping it on means every task has an inspectable diff even if a later " +
            "stage fails, which is almost always what you want when something goes wrong."),

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
            "mode all live there. Validate it after editing; validation checks the numbers are " +
            "sane, that no agent id is defined twice, and that every agent could actually be " +
            "launched as configured.",
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
