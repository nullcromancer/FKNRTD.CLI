using FKNRTD.Domain;
using FKNRTD.Services;

namespace FKNRTD.Dashboard;

/// <summary>
/// The guided form behind <c>fknrtd agent new</c>. Registering a coding CLI was the most cryptic
/// thing the product asked for — <c>-arg=-p -arg "{prompt}"</c> with nothing anywhere explaining
/// what an argument array was for, why the placeholder existed, or why an auditor needed markers.
/// </summary>
internal static class AgentWizard
{
    public static Wizard Create(FknrtdConfig config)
    {
        var existing = config.Agents.Select(agent => agent.Id).ToArray();
        var steps = new List<WizardStep>
        {
            new()
            {
                Key = "id",
                Question = "What should this agent be called?",
                GlossaryTerm = "agent",
                Placeholder = "gemini",
                Validate = (value, _) =>
                {
                    if (value.Length == 0)
                    {
                        return "A short name is required. It is how you will assign this agent to a task.";
                    }

                    if (value.Any(character => !char.IsLetterOrDigit(character) && character is not '-' and not '_'))
                    {
                        return "Use only letters, numbers, hyphens and underscores. The name ends up in " +
                               "file paths and command lines.";
                    }

                    return existing.Any(id => id.Equals(value, StringComparison.OrdinalIgnoreCase))
                        ? $"'{value}' is already configured. Pick another name, or remove that one first."
                        : null;
                }
            },
            new()
            {
                Key = "exe",
                Question = "Which program should be run?",
                GlossaryTerm = "agent",
                Placeholder = "gemini",
                Default = values => values.GetValueOrDefault("id", string.Empty),
                Validate = (value, _) => value.Length == 0
                    ? "An executable is required. Give the command you would type in a shell."
                    : null
            },
            new()
            {
                Key = "delivery",
                Question = "How does that program want to be given a prompt?",
                GlossaryTerm = "prompt-delivery",
                Input = WizardInput.Choice,
                Default = _ => "argument",
                Options = _ =>
                [
                    new WizardOption("argument", "As an argument",
                        "the usual answer; the prompt is one item in the command line", Recommended: true),
                    new WizardOption("stdin", "On standard input",
                        "for programs that truncate long arguments or mishandle newlines")
                ]
            },
            new()
            {
                Key = "args",
                Question = "What arguments launch it? One per line, with {prompt} where the prompt goes.",
                GlossaryTerm = "profile",
                Input = WizardInput.Commands,
                Applies = values => values.GetValueOrDefault("delivery", "argument") == "argument",
                Default = _ => "-p\n{prompt}",
                Placeholder = "-p        then on the next line:  {prompt}",
                Validate = (value, _) => value.Contains("{prompt}", StringComparison.Ordinal)
                    ? null
                    : "One of these lines has to be exactly {prompt}, or the agent is launched with " +
                      "no instruction at all."
            },
            new()
            {
                Key = "audit",
                Question = "Should this agent be allowed to audit finished work?",
                GlossaryTerm = "verdict",
                Input = WizardInput.Choice,
                Default = _ => "yes",
                Options = _ =>
                [
                    new WizardOption("yes", "Yes",
                        "its audit profile will require an explicit FKNRTD_VERDICT: PASS line",
                        Recommended: true),
                    new WizardOption("no", "No",
                        "it can still plan and implement, but never approve anything")
                ]
            }
        };

        return new Wizard("REGISTER AN AGENT", Theme.Violet, steps);
    }

    /// <summary>Turns the answers into the definition that gets written to the configuration.</summary>
    public static AgentDefinition Build(Wizard wizard)
    {
        var id = wizard.Value("id");
        var stdin = wizard.Value("delivery") == "stdin";
        var arguments = stdin ? [] : wizard.Lines("args").ToList();
        var delivery = stdin ? PromptDelivery.StandardInput : PromptDelivery.Argument;

        AgentCommandProfile Profile(bool audit) => new()
        {
            Arguments = arguments.ToList(),
            PromptDelivery = delivery,
            SuccessMarker = audit ? "FKNRTD_VERDICT: PASS" : null,
            FailureMarker = audit ? "FKNRTD_VERDICT: FAIL" : null
        };

        var audits = wizard.Value("audit") == "yes";
        return new AgentDefinition
        {
            Id = id,
            DisplayName = char.ToUpperInvariant(id[0]) + id[1..],
            Kind = "generic",
            Executable = wizard.Value("exe"),
            Color = "cyan",
            Profiles = new Dictionary<string, AgentCommandProfile>(StringComparer.OrdinalIgnoreCase)
            {
                ["default"] = Profile(audit: false),
                ["plan"] = Profile(audit: false),
                ["implement"] = Profile(audit: false),
                ["audit"] = Profile(audits)
            }
        };
    }

    /// <summary>
    /// What the operator should be told after the agent is written. A registration that looks like it
    /// worked but names a program that is not installed fails much later, inside a task.
    /// </summary>
    public static IEnumerable<string> Report(AgentDefinition agent, bool audits)
    {
        var found = ExecutableLocator.Find(agent.Executable);
        yield return found is not null
            ? $"  Found {agent.Executable} at {found}"
            : $"  Warning: {agent.Executable} is not on PATH. A task assigned to {agent.Id} would fail " +
              "to launch it. Install it, or fix the executable name in .fknrtd/config.json.";
        yield return audits
            ? $"  {agent.Id} may plan, implement, or audit."
            : $"  {agent.Id} may plan or implement, but cannot audit — it returns no verdict.";
        yield return $"  Try it with: fknrtd task new";
    }
}
