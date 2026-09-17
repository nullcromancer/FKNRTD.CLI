using FKNRTD.Domain;
using FKNRTD.Services;

namespace FKNRTD.Dashboard;

/// <summary>
/// Builds the dashboard's guided forms. The step definitions live here rather than inside
/// <see cref="DashboardApp"/> so that the renderer's test seam can construct exactly the form the
/// operator sees, instead of a copy of it that is free to drift.
/// </summary>
internal static class TaskWizard
{
    /// <summary>
    /// The task builder behind N. Every step names a glossary term, which is what makes the form
    /// explain itself: the question arrives with its own definition and a worked example, and every
    /// field carries a default that is already correct for this workspace.
    /// </summary>
    public static Wizard Create(FknrtdConfig config)
    {
        var enabled = config.Agents.Where(agent => agent.Enabled).ToArray();
        var git = config.Mode == WorkspaceMode.Git;
        var steps = new List<WizardStep>
        {
            new()
            {
                Key = "title",
                Question = "What should this task be called?",
                GlossaryTerm = "title",
                Placeholder = "Add rate limiting to the login endpoint",
                Validate = (value, _) => value.Length == 0
                    ? "A title is required. A few words describing the outcome is enough."
                    : null
            },
            new()
            {
                Key = "brief",
                Question = "What should the agents build? Describe the finished state.",
                GlossaryTerm = "brief",
                Input = WizardInput.LongText,
                Placeholder = "Start typing. Alt+Enter starts a new line.",
                Validate = (value, _) => value.Length switch
                {
                    0 => "A brief is required. It is the instruction every agent on this task reads.",
                    < 15 => "That is too short to act on. Say what done looks like and how you will know.",
                    _ => null
                }
            },
            new()
            {
                Key = "review",
                Question = "Everything else is already set for this workspace. Change any of it?",
                GlossaryTerm = "task",
                Input = WizardInput.Choice,
                Default = _ => "no",
                Options = _ =>
                [
                    new WizardOption("no", "Create it now", Summarise(config, enabled), Recommended: true),
                    new WizardOption("yes", "Let me look",
                        "walks the agents, the branch, the checks and the repair budget")
                ]
            },
            new()
            {
                Key = "lead",
                Applies = Reviewing,
                Question = "Which agent should read the code and write the plan?",
                GlossaryTerm = "lead",
                Input = WizardInput.Choice,
                Default = _ => Prefer(enabled, "claude", 0),
                Options = _ => AgentOptions(enabled, requireVerdict: false)
            },
            new()
            {
                Key = "implementer",
                Applies = Reviewing,
                Question = "Which agent should make the change?",
                GlossaryTerm = "implementer",
                Input = WizardInput.Choice,
                // Defaulting to someone other than the lead is the whole argument for the tool: a
                // second model reading the first model's plan catches what the author cannot see.
                Default = values => enabled
                        .FirstOrDefault(agent => !agent.Id.Equals(values.GetValueOrDefault("lead"),
                            StringComparison.OrdinalIgnoreCase))?.Id
                    ?? Prefer(enabled, "codex", 1),
                Options = _ => AgentOptions(enabled, requireVerdict: false)
            },
            new()
            {
                Key = "auditor",
                Applies = Reviewing,
                Question = "Which agent should independently judge the result?",
                GlossaryTerm = "auditor",
                Input = WizardInput.Choice,
                Default = values => values.GetValueOrDefault("lead", Prefer(enabled, "claude", 0)),
                Options = _ => AgentOptions(enabled, requireVerdict: true),
                Validate = (value, _) => CanAudit(enabled, value)
                    ? null
                    : $"'{value}' cannot return a verdict. Its audit profile needs successMarker and " +
                      "failureMarker values before it can be trusted to pass or fail work."
            },
            new()
            {
                Key = "base",
                Question = "Which branch should this start from and merge back into?",
                GlossaryTerm = "base-ref",
                Applies = values => git && Reviewing(values),
                Default = _ => config.DefaultBaseRef,
                Placeholder = string.IsNullOrWhiteSpace(config.DefaultBaseRef) ? "main" : config.DefaultBaseRef
            },
            new()
            {
                Key = "verify",
                Applies = Reviewing,
                Question = "Which commands decide whether the work is correct? One per line.",
                GlossaryTerm = "verification",
                Input = WizardInput.Commands,
                Default = _ => string.Join('\n', config.DefaultVerificationCommands),
                Placeholder = "dotnet build          (leave empty to skip verification entirely)"
            },
            new()
            {
                Key = "repairs",
                Applies = Reviewing,
                Question = "How many times may a failed verification be handed back for repair?",
                GlossaryTerm = "repair-round",
                Input = WizardInput.Number,
                Default = _ => config.DefaultMaxRepairRounds.ToString(),
                Validate = (value, _) => int.TryParse(value, out var rounds) && rounds is >= 0 and <= 5
                    ? null
                    : "Enter a whole number between 0 and 5. One is the usual answer."
            },
            new()
            {
                Key = "then",
                Question = "Ready. What should happen once the task is created?",
                GlossaryTerm = "task",
                Input = WizardInput.Choice,
                Default = _ => "queue",
                Options = _ =>
                [
                    new WizardOption("queue", "Just create it",
                        "It waits in the list until you press Enter on it", Recommended: true),
                    new WizardOption("run", "Create it and run it now",
                        "The plan stage starts immediately in a new worktree")
                ]
            }
        };

        return new Wizard("NEW TASK", Theme.Blue, steps);
    }

    /// <summary>
    /// Whether the operator asked to see the settings after the brief. They all have a default that
    /// is already right for the workspace, so the form skips them unless asked.
    /// </summary>
    private static bool Reviewing(IReadOnlyDictionary<string, string> values) =>
        values.GetValueOrDefault("review", "no") == "yes";

    /// <summary>
    /// What accepting the defaults actually means, spelled out on the option itself. Accepting them
    /// blind would be exactly the kind of thing this whole form exists to stop.
    /// </summary>
    private static string Summarise(FknrtdConfig config, IReadOnlyList<AgentDefinition> enabled)
    {
        var lead = Prefer(enabled, "claude", 0);
        var implementer = enabled
                              .FirstOrDefault(agent => !agent.Id.Equals(lead, StringComparison.OrdinalIgnoreCase))
                              ?.Id
                          ?? Prefer(enabled, "codex", 1);
        var checks = config.DefaultVerificationCommands.Count == 0
            ? "nothing verifies it"
            : "verified by " + string.Join(", ", config.DefaultVerificationCommands);
        return $"{lead} plans, {implementer} implements, {lead} audits; {checks}";
    }

    /// <summary>The message-bus form behind M.</summary>
    public static Wizard Message(FknrtdConfig config)
    {
        IReadOnlyList<WizardOption> Everyone() => config.Agents
            .Select(agent => new WizardOption(agent.Id, agent.DisplayName,
                agent.Enabled ? $"runs {agent.Executable}" : "disabled in this workspace"))
            .ToArray();

        return new Wizard("SEND A MESSAGE", Theme.Orange, new List<WizardStep>
        {
            new()
            {
                Key = "from",
                Question = "Who is this message from?",
                GlossaryTerm = "message",
                Input = WizardInput.Choice,
                Options = _ => Everyone()
            },
            new()
            {
                Key = "to",
                Question = "Who is it for?",
                GlossaryTerm = "message",
                Input = WizardInput.Choice,
                Options = _ => Everyone()
            },
            new()
            {
                Key = "text",
                Question = "What does it say?",
                GlossaryTerm = "message",
                Input = WizardInput.LongText,
                Placeholder = "Ready for audit — the failing snapshot test was stale, not wrong.",
                Validate = (value, _) => value.Length == 0
                    ? "An empty message would not tell anyone anything."
                    : null
            }
        });
    }

    /// <summary>Agents as selectable options, annotated with whatever would stop them working.</summary>
    private static IReadOnlyList<WizardOption> AgentOptions(
        IReadOnlyList<AgentDefinition> agents,
        bool requireVerdict)
    {
        return agents.Select(agent =>
        {
            var found = ExecutableLocator.Find(agent.Executable) is not null;
            var warning = !found
                ? $"'{agent.Executable}' is not on PATH — this task could not launch it"
                : requireVerdict && !CanAudit(agents, agent.Id)
                    ? "cannot return a verdict, so it cannot audit"
                    : null;
            var description = requireVerdict
                ? $"runs {agent.Executable}, returns a PASS or FAIL verdict"
                : $"runs {agent.Executable}, found on PATH";
            return new WizardOption(agent.Id, agent.DisplayName, warning is null ? description : agent.Executable,
                Warning: warning);
        }).ToArray();
    }

    /// <summary>
    /// Whether an agent is equipped to act as an auditor. An auditor that cannot signal PASS or FAIL
    /// could never approve anything, so offering it silently would be a trap.
    /// </summary>
    public static bool CanAudit(IReadOnlyList<AgentDefinition> agents, string agentId)
    {
        var agent = agents.FirstOrDefault(item => item.Id.Equals(agentId, StringComparison.OrdinalIgnoreCase));
        if (agent is null)
        {
            return false;
        }

        var profile = agent.Profiles.TryGetValue("audit", out var audit)
            ? audit
            : agent.Profiles.GetValueOrDefault("default");
        return profile is not null &&
               !string.IsNullOrWhiteSpace(profile.SuccessMarker) &&
               !string.IsNullOrWhiteSpace(profile.FailureMarker);
    }

    private static string Prefer(IReadOnlyList<AgentDefinition> agents, string preferred, int fallbackIndex) =>
        agents.FirstOrDefault(agent => agent.Id.Equals(preferred, StringComparison.OrdinalIgnoreCase))?.Id ??
        agents.ElementAtOrDefault(fallbackIndex)?.Id ??
        agents.FirstOrDefault()?.Id ??
        string.Empty;
}
