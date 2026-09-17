namespace FKNRTD.Help;

/// <summary>One setting in <c>.fknrtd/config.json</c>, and what changing it actually costs.</summary>
/// <param name="Key">The JSON name exactly as it appears in the file.</param>
/// <param name="Title">Human title for a heading.</param>
/// <param name="Section">Grouping for the settings browser and the portal.</param>
/// <param name="Default">The shipped default, written as it appears in JSON.</param>
/// <param name="Summary">One line, short enough for a list.</param>
/// <param name="Detail">What the setting controls.</param>
/// <param name="IfYouChangeIt">
/// The consequence, concretely. This is the field that earns the file's existence: an operator
/// looking at an unfamiliar key needs to know what happens if they move it, not a restatement of
/// its name.
/// </param>
/// <param name="GlossaryTerm">A <see cref="Glossary"/> key when one applies.</param>
public sealed record SettingEntry(
    string Key,
    string Title,
    string Section,
    string Default,
    string Summary,
    string Detail,
    string IfYouChangeIt,
    string? GlossaryTerm = null);

/// <summary>
/// Every settable field in a workspace's configuration. The file is plain JSON meant to be edited by
/// hand, and until now nothing in the product said what any of it meant — an operator opening it met
/// <c>agentStaleAfterSeconds</c> and <c>requireCleanTreeForLanding</c> with no way to find out.
/// </summary>
public static class SettingsCatalog
{
    public const string Workspace = "Workspace";
    public const string Limits = "Limits and timeouts";
    public const string Safety = "Safety";
    public const string AgentFields = "Agent definitions";
    public const string DashboardFields = "Dashboard";

    private static readonly SettingEntry[] Entries =
    [
        new("schemaVersion", "Schema version", Workspace, "1",
            "Records which shape of this file the workspace was written with.",
            "Written when the workspace is created. Nothing currently reads it: a configuration " +
            "missing a field gets that field's default regardless of the version recorded here. It " +
            "exists so that a future version has something to branch on if the shape ever changes " +
            "in a way defaults cannot absorb.",
            "Nothing, today. Changing it neither migrates anything nor breaks anything, which is " +
            "worth knowing before you spend time on it."),

        new("projectName", "Project name", Workspace, "the repository or folder name",
            "What this workspace is called in the introduction and in setup output.",
            "Detected from the Git repository name, or the folder name outside one. Cosmetic: nothing " +
            "resolves paths or branches through it. The dashboard header shows the repository's own " +
            "name rather than this, so the two can differ if you change it.",
            "Safe to change to anything readable. It affects only what is displayed.",
            "workspace"),

        new("mode", "Workspace mode", Workspace, "\"git\" when the folder is a repository",
            "Whether tasks are isolated in a worktree or edit this folder directly.",
            "Git mode gives every task its own branch and checkout. Standalone mode has neither: " +
            "agents edit the folder in place.",
            "Switching to standalone in a repository gives up all isolation — agent edits land " +
            "directly in your working copy with nothing to roll back to. Switching to git in a " +
            "folder that is not a repository makes every new task fail as it is created, when the " +
            "base branch cannot be resolved.",
            "mode"),

        new("defaultBaseRef", "Default base branch", Workspace, "the branch the repository was on",
            "Which branch new tasks start from and merge back into when none is given.",
            "A per-task base branch overrides it. Empty in a standalone workspace, where there is no " +
            "branch to start from.",
            "Point it at a branch that exists and that you are willing to have merged into. A name " +
            "that does not resolve fails the task at creation rather than silently picking another.",
            "base-ref"),

        new("defaultVerificationCommands", "Default verification commands", Safety, "[] or detected build and test commands",
            "The commands new tasks inherit as their definition of correct.",
            "Detected from the project's shape when the workspace is created. Each one must exit 0 " +
            "inside the task's worktree. They run through the platform shell, so they may contain " +
            "pipes and redirection.",
            "Emptying this means new tasks are created with nothing checking them, leaving the audit " +
            "as the only gate — and an audit is a judgement rather than a measurement. Adding a " +
            "command you have not read is the one genuinely dangerous edit in this file.",
            "verification"),

        new("defaultMaxRepairRounds", "Default repair rounds", Limits, "1",
            "How many times a failed verification is handed back to the implementer by default.",
            "A per-task value overrides it. Zero means one failure ends the task.",
            "Raising it above two rarely converges and burns rate-limit budget: an agent that cannot " +
            "see why a test fails does not usually see it on the fourth attempt either. Lowering it " +
            "to zero fails tasks on the sort of missed import a single retry would have fixed.",
            "repair-round"),

        new("maxParallelAgents", "Maximum parallel agents", Limits, "4",
            "How many tasks may run at once from the dashboard.",
            "The dashboard refuses to start another task past this number and says so. It counts " +
            "tasks it is running, not processes: a task waiting for the agent lease, or running your " +
            "verification commands, counts against the limit without an agent being live.",
            "The practical ceiling is your machine and your rate-limit budget rather than this " +
            "number. Raising it past what your budget supports converts a queue into a set of " +
            "simultaneous rate-limit failures."),

        new("agentTimeoutSeconds", "Agent timeout", Limits, "3600",
            "How long a single agent run may take before it is killed.",
            "Applies to each plan, implement and audit stage separately. A process that exceeds it " +
            "has its process tree killed and is reported as a timeout rather than left to hang.",
            "Lower it to fail fast on an agent that has stopped making progress. Raise it for a " +
            "genuinely long implementation — but an agent that needs more than an hour on one task " +
            "is usually a sign the brief was too large rather than that the limit was too low.",
            "timeouts"),

        new("verificationTimeoutSeconds", "Verification timeout", Limits, "600",
            "How long a single verification command may take before it is killed.",
            "Applies per command, not to the whole set.",
            "Raise it if your build or test suite legitimately takes longer than ten minutes, or " +
            "every task will fail at the verify stage with a timeout that looks like a test failure.",
            "timeouts"),

        new("agentStaleAfterSeconds", "Agent staleness window", Limits, "120",
            "How long an agent's claim to be working is trusted before it is marked stale.",
            "Agents report what they are doing. A report older than this that still claims the agent " +
            "is running, planning or reviewing is downgraded to unknown, and the radar marks it as " +
            "stale rather than hiding it. States that are not claims of activity are left alone.",
            "Setting it too high leaves a crashed agent looking busy and its paths looking contested. " +
            "Setting it too low makes a working agent flicker to stale between reports.",
            "telemetry"),

        new("claimStaleAfterSeconds", "Claim staleness window", Limits, "300",
            "Recorded but not currently read by anything.",
            "Each claim carries its own expiry, set from the -ttl given when it was registered and " +
            "defaulting to five minutes, and staleness is judged against that expiry rather than " +
            "against this setting. Nothing in the product reads this value.",
            "Nothing. Change the lifetime of a claim with -ttl when you register it. This entry says " +
            "so rather than describing behaviour the setting does not have.",
            "ttl"),

        new("dashboardRefreshMilliseconds", "Dashboard refresh interval", DashboardFields, "1000",
            "How often the dashboard re-reads state and redraws.",
            "Must be at least 100. Each refresh reads the workspace's state files and queries Git.",
            "Lowering it makes a running task feel more live at the cost of steady disk and Git " +
            "activity. Raising it is worth doing over a slow network filesystem."),

        new("requireCleanTreeForLanding", "Require a clean tree to land", Safety, "true",
            "Whether landing refuses while your own checkout has uncommitted changes.",
            "Checked immediately before the merge, against the workspace root — the checkout being " +
            "merged into, not the task's worktree. Uncommitted changes there are work no stage ever " +
            "looked at, sitting exactly where the verified work is about to land.",
            "Turning it off lets a task merge on top of unchecked work, which is how a change that " +
            "passed verification ends up indistinguishable from one that was never checked at all. " +
            "There is no good reason to turn this off.",
            "land"),

        new("autoCommitAgentChanges", "Commit agent changes automatically", Safety, "true in Git mode",
            "Whether verified, audited work is committed to the task branch before it becomes landable.",
            "The commit is made once verification and the audit have both passed, immediately before " +
            "the task reaches ready-to-land — not when the implementer finishes. Unavailable in a " +
            "standalone workspace, where there is no repository to commit to.",
            "Turning it off is a trap worth understanding. A task whose worktree still has " +
            "uncommitted changes fails at the end — after its verification and its audit have both " +
            "passed — because there is no commit to merge, and it is recorded as Failed rather " +
            "than held back. The work itself is never lost: it is sitting in the worktree. Commit " +
            "it there yourself and retry, or leave this on.",
            "auto-commit"),

        new("agents", "Agents", AgentFields, "Claude and Codex",
            "The coding CLIs this workspace may assign work to.",
            "An array of agent definitions. Each carries an id, the executable to run, whether it is " +
            "enabled, and a command profile per stage.",
            "Edit by hand only if you are comfortable with the profile format; 'fknrtd agent new' " +
            "asks the same questions and explains each one. Run 'fknrtd config validate' afterwards " +
            "either way.",
            "agent"),

        new("agents[].id", "Agent id", AgentFields, "claude, codex",
            "The short name used to assign an agent to a role.",
            "Letters, numbers, hyphens and underscores only, because it appears in file paths and " +
            "command lines. Must be unique within the workspace.",
            "Renaming one does not update the tasks that already reference it, and those tasks will " +
            "fail with an agent that no longer exists.",
            "agent"),

        new("agents[].executable", "Agent executable", AgentFields, "claude, codex",
            "The program to launch, resolved against PATH.",
            "On Windows the PATHEXT resolution deliberately prefers a real executable over an " +
            "extensionless shim of the same name.",
            "If this names something not installed, every task assigned to the agent fails at launch. " +
            "'fknrtd doctor' reports it before a task does.",
            "agent"),

        new("agents[].enabled", "Agent enabled", AgentFields, "true",
            "Whether the agent is offered for any role.",
            "A disabled agent stays configured and keeps its profiles.",
            "Disabling is the reversible way to take an agent out of use while you fix its " +
            "installation. Removing it loses the profiles.",
            "agent"),

        new("agents[].displayName", "Agent display name", AgentFields, "the id, capitalised",
            "What the agent is called on screen, where the id would read as a shell token.",
            "Shown in the agent radar, the roster and the role pickers. Nothing resolves anything " +
            "through it; assignments are always by id.",
            "Safe to change to anything readable. It affects only what is displayed.",
            "agent"),

        new("agents[].kind", "Agent kind", AgentFields, "\"claude\", \"codex\", or \"generic\"",
            "A family label used to pick the colour the agent is drawn in.",
            "Recognised values get a distinct colour in the dashboard; anything else falls back to " +
            "blue. It carries no behaviour beyond that — it does not change how the agent is launched.",
            "Setting it to an unrecognised value only loses the colour. It cannot break a task.",
            "agent"),

        new("agents[].color", "Agent colour", AgentFields, "\"cyan\"",
            "Recorded per agent, but the dashboard colours agents by kind rather than by this.",
            "Written when an agent is registered and kept in the file. Nothing currently reads it; " +
            "the colour an agent is drawn in comes from its kind.",
            "Nothing. Change agents[].kind if you want a different colour.",
            "agent"),

        new("agents[].profiles", "Command profiles", AgentFields, "plan, implement, audit, default",
            "How the agent is launched for each stage.",
            "A profile is an object holding an argument array, a prompt delivery mode and, for an " +
            "auditor, its verdict markers. Arguments may contain {prompt}, {taskId}, {workspace} and " +
            "{branch}, substituted without any shell interpolation — that is what lets a " +
            "multi-paragraph brief containing quotes and newlines be passed safely. If an argument " +
            "profile contains no {prompt} at all, the prompt is appended as a final argument.",
            "This is where an agent's sandbox flags live, and therefore where the read-only guarantee " +
            "for the lead and auditor actually comes from. Removing a read-only flag from a plan or " +
            "audit profile silently gives that stage write access to the worktree.",
            "profile"),

        new("agents[].profiles.audit.successMarker", "Audit verdict markers", Safety,
            "\"FKNRTD_VERDICT: PASS\" and \"FKNRTD_VERDICT: FAIL\"",
            "The lines an auditor prints to pass or fail work.",
            "Both markers must be configured before an agent may be assigned as an auditor. An audit " +
            "passes only when the success marker appears on exactly one line and the failure marker " +
            "on none. The match ignores case and tolerates a line wrapped in quoting, list bullets " +
            "or Markdown emphasis, so an agent that writes **FKNRTD_VERDICT: PASS.** still counts. A " +
            "missing verdict is a failure, never consent.",
            "Change the success marker without changing what the agent prints and no task can ever " +
            "become landable. Change only the failure marker and an audit that should have failed " +
            "can pass, because the old failure line no longer matches anything.",
            "verdict"),

        new("agents[].profiles.*.promptDelivery", "Prompt delivery", AgentFields, "\"argument\"",
            "Whether the prompt is written to the agent's standard input as well as built into its arguments.",
            "With \"argument\", the prompt is substituted into {prompt} or appended as a final " +
            "argument. With \"standardInput\", it is also written to the process's standard input.",
            "Switching to standardInput does not by itself take the prompt off the command line: an " +
            "argument list that still contains {prompt} still carries the whole thing. Remove " +
            "{prompt} from the arguments as well if the point was to keep a long brief out of them.",
            "prompt-delivery"),

        new("agents[].environment", "Agent environment", AgentFields, "{}",
            "Extra environment variables set for this agent's processes.",
            "Merged over the environment FKNRTD.CLI itself is running in.",
            "Useful for an API key or a model selection an agent reads from its environment. Anything " +
            "put here is written to the configuration file in plain text, so it is the wrong place " +
            "for a secret you would not commit."),
    ];

    public static IReadOnlyList<SettingEntry> All => Entries;

    public static IReadOnlyList<string> Sections { get; } =
        Entries.Select(entry => entry.Section).Distinct(StringComparer.Ordinal).ToArray();

    public static IEnumerable<SettingEntry> InSection(string section) =>
        Entries.Where(entry => entry.Section.Equals(section, StringComparison.Ordinal));

    /// <summary>Resolves a setting by its JSON name, its title, or a hyphenated spelling of either.</summary>
    public static SettingEntry? Find(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return null;
        }

        var needle = key.Trim();
        return Entries.FirstOrDefault(entry => entry.Key.Equals(needle, StringComparison.OrdinalIgnoreCase))
               ?? Entries.FirstOrDefault(entry => entry.Title.Equals(needle, StringComparison.OrdinalIgnoreCase))
               ?? Entries.FirstOrDefault(entry =>
                   Flatten(entry.Key).Equals(Flatten(needle), StringComparison.OrdinalIgnoreCase) ||
                   Flatten(entry.Title).Equals(Flatten(needle), StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Reduces a name to comparable letters, so agents[].id matches agents-id and AgentsId.</summary>
    private static string Flatten(string value) =>
        new(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
}
