namespace FKNRTD.Dashboard;

/// <summary>
/// The guided form behind an interactive <c>fknrtd init</c>. Setting a workspace up used to be a
/// silent act with printed results: the tool decided the mode, guessed the verification commands and
/// told the operator afterwards. Those are the two decisions that most change how safe the thing is,
/// so they are worth asking about — with the consequence of each answer stated before it is given.
/// </summary>
internal static class SetupWizard
{
    /// <summary>What the folder looks like before anything has been decided about it.</summary>
    /// <param name="Root">The folder being set up.</param>
    /// <param name="IsRepository">Whether Git is present and this is a working tree.</param>
    /// <param name="GitInstalled">Whether git is on PATH at all.</param>
    /// <param name="DetectedBranch">The branch the repository is on, when there is one.</param>
    /// <param name="DetectedCommands">Verification commands inferred from the project's shape.</param>
    /// <param name="HasClaude">Whether an agent named claude is configured, for the statusline step.</param>
    public sealed record Detected(
        string Root,
        bool IsRepository,
        bool GitInstalled,
        string DetectedBranch,
        IReadOnlyList<string> DetectedCommands,
        bool HasClaude);

    public static Wizard Create(Detected detected)
    {
        var steps = new List<WizardStep>
        {
            new()
            {
                Key = "mode",
                Question = "How should agents be kept away from your working copy?",
                GlossaryTerm = "mode",
                Input = WizardInput.Choice,
                // With no repository there is no choice to offer, only a consequence to state.
                Applies = _ => detected.IsRepository,
                Default = _ => "git",
                Options = _ =>
                [
                    new WizardOption("git", "Git-backed",
                        "each task gets its own branch and checkout; your files never move",
                        Recommended: true),
                    new WizardOption("standalone", "Standalone",
                        "agents edit this folder directly, with nothing to roll back to")
                ]
            },
            new()
            {
                Key = "base",
                Question = "Which branch should tasks start from and merge back into?",
                GlossaryTerm = "base-ref",
                Applies = values => detected.IsRepository && values.GetValueOrDefault("mode", "git") == "git",
                Default = _ => detected.DetectedBranch,
                Placeholder = string.IsNullOrWhiteSpace(detected.DetectedBranch) ? "main" : detected.DetectedBranch
            },
            new()
            {
                Key = "verify",
                Question = "Which commands prove this project still works? One per line.",
                GlossaryTerm = "verification",
                Input = WizardInput.Commands,
                Default = _ => string.Join('\n', detected.DetectedCommands),
                Placeholder = detected.DetectedCommands.Count > 0
                    ? "Enter accepts what was detected"
                    : "dotnet build          (leave empty and nothing will check the work)"
            },
            new()
            {
                Key = "statusline",
                Question = "Should Claude Code report its remaining budget to this dashboard?",
                GlossaryTerm = "statusline",
                Input = WizardInput.Choice,
                Applies = _ => detected.HasClaude,
                Default = _ => "yes",
                Options = _ =>
                [
                    new WizardOption("yes", "Install it",
                        "writes a statusline into your Claude settings; takes effect on restart",
                        Recommended: true),
                    new WizardOption("no", "Not now",
                        "Claude's usage figures stay blank; install it later with fknrtd integration")
                ]
            },
            new()
            {
                Key = "then",
                Question = "Ready. What should happen once the workspace exists?",
                GlossaryTerm = "doctor",
                Input = WizardInput.Choice,
                Default = _ => "doctor",
                Options = _ =>
                [
                    new WizardOption("doctor", "Run the pre-flight checks",
                        "confirms every agent can actually be launched from here", Recommended: true),
                    new WizardOption("dashboard", "Open the command center",
                        "goes straight to the dashboard, which starts on an introduction"),
                    new WizardOption("nothing", "Just set it up",
                        "returns to the shell")
                ]
            }
        };

        return new Wizard("SET UP THIS WORKSPACE", Theme.Green, steps);
    }
}
