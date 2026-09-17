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
            "Which shape of this file FKNRTD.CLI is reading.",
            "Set when the workspace is created and used to apply defaults to a configuration written " +
            "by an older version. Missing fields are filled in on load rather than rejected.",
            "Do not edit it by hand. Lowering it will not roll anything back, and raising it claims " +
            "a shape the file does not have."),

        new("projectName", "Project name", Workspace, "the repository or folder name",
            "What this workspace is called in the dashboard header and in event messages.",
            "Detected from the Git repository name, or the folder name outside one. Cosmetic: nothing " +
            "resolves paths or branches through it.",
            "Safe to change to anything readable. It affects only what is displayed.",
            "workspace"),

        new("mode", "Workspace mode", Workspace, "\"Git\" when the folder is a repository",
            "Whether tasks are isolated in a worktree or edit this folder directly.",
            "Git mode gives every task its own branch and checkout. Standalone mode has neither: " +
            "agents edit the folder in place.",
            "Switching to Standalone in a repository gives up all isolation — agent edits land " +
            "directly in your working copy with nothing to roll back to. Switching to Git in a " +
            "folder that is not a repository will fail every task at the worktree stage.",
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
            "The dashboard refuses to start another task past this number and says so. Each running " +
            "task means a live agent process and, in Git mode, its own checkout on disk.",
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
            "How long an agent's reported state is trusted before it is treated as offline.",
            "Agents report what they are doing; a report older than this stops counting towards the " +
            "agent radar and towards conflict detection.",
            "Setting it too high leaves a crashed agent looking busy and its paths looking contested. " +
            "Setting it too low makes a working agent flicker offline between reports.",
            "telemetry"),

        new("claimStaleAfterSeconds", "Claim staleness window", Limits, "300",
            "How long a path reservation survives without being renewed.",
            "A claim past this age is reported as stale and stops blocking other agents.",
            "This is the safety valve that stops a crashed agent holding a file forever. Raising it " +
            "lengthens how long the workspace stays blocked after a crash; lowering it makes a slow " +
            "but healthy agent lose its reservation mid-edit.",
            "ttl"),

        new("dashboardRefreshMilliseconds", "Dashboard refresh interval", DashboardFields, "1000",
            "How often the dashboard re-reads state and redraws.",
            "Must be at least 100. Each refresh reads the workspace's state files and queries Git.",
            "Lowering it makes a running task feel more live at the cost of steady disk and Git " +
            "activity. Raising it is worth doing over a slow network filesystem."),

        new("requireCleanTreeForLanding", "Require a clean tree to land", Safety, "true",
            "Whether landing refuses to merge while the worktree has uncommitted changes.",
            "Checked immediately before the merge, after verification and the audit have already " +
            "passed. Uncommitted changes at that point are work no stage ever looked at.",
            "Turning it off lets a task land on top of uncommitted work, which is how a change that " +
            "passed verification merges alongside one that was never checked at all. There is no " +
            "good reason to turn this off.",
            "land"),

        new("autoCommitAgentChanges", "Commit agent changes automatically", Safety, "true in Git mode",
            "Whether the implementer's work is committed to the task branch as its stage ends.",
            "Unavailable in a standalone workspace, where there is no repository to commit to.",
            "Turning it off means a task that fails after the implement stage leaves no inspectable " +
            "diff, so the most useful evidence about what went wrong is exactly what you lose.",
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

        new("agents[].profiles", "Command profiles", AgentFields, "plan, implement, audit, default",
            "The argument list used to launch the agent for each stage.",
            "Each profile is an array of arguments containing the placeholder {prompt}, which is " +
            "substituted without any shell interpolation — that is what lets a multi-paragraph brief " +
            "containing quotes and newlines be passed safely.",
            "This is where an agent's sandbox flags live, and therefore where the read-only guarantee " +
            "for the lead and auditor actually comes from. Removing a read-only flag from a plan or " +
            "audit profile silently gives that stage write access to the worktree.",
            "profile"),

        new("agents[].profiles.audit.successMarker", "Audit verdict markers", Safety,
            "\"FKNRTD_VERDICT: PASS\" and \"FKNRTD_VERDICT: FAIL\"",
            "The exact lines an auditor prints to pass or fail work.",
            "Both markers are required before an agent may be assigned as an auditor. A missing " +
            "verdict is treated as a failure, never as consent.",
            "Changing them without changing what the agent actually prints means every audit is read " +
            "as a failure, and no task can ever become landable.",
            "verdict"),

        new("agents[].profiles.*.promptDelivery", "Prompt delivery", AgentFields, "\"Argument\"",
            "Whether the prompt arrives as a command-line argument or on standard input.",
            "Most agents take an argument. Some cap argument length or mishandle newlines.",
            "Switch to StandardInput if an agent truncates long briefs or fails on multi-line ones.",
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
