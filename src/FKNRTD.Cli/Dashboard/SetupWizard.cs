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
                Question = detected.IsRepository
                    ? "How should agents be kept away from your working copy?"
                    : "Agents will edit this folder directly. Is that what you want?",
                GlossaryTerm = "mode",
                Input = WizardInput.Choice,
                Default = _ => detected.IsRepository ? "git" : "standalone",

                // The step used to be skipped entirely when there was no repository, which meant the
                // one decision that most changes how safe this is got made silently - and the two
                // reasons it can be forced look identical from the outside while having completely
                // different fixes.
                Explanation = detected.IsRepository
                    ? "A Git-backed workspace gives every task its own branch and its own checkout, " +
                      "so an agent can edit, build and break things without touching the files you " +
                      "have open. A standalone workspace has nothing to isolate against: agents " +
                      "work in this folder, and landing records that the verified work is already " +
                      "here. Git mode is strictly safer."
                    : detected.GitInstalled
                        ? $"There is no Git repository at {detected.Root}, so there is nothing to " +
                          "branch from and no second checkout to give an agent. Agents will edit " +
                          "this folder in place. Git is installed here, so 'git init' followed by a " +
                          "first commit would let you set this up again with real isolation — which " +
                          "is worth doing before you let anything loose on work you care about."
                        : "Git is not installed on this machine, so isolation is not available at " +
                          "all: there is no way to give an agent its own checkout. Agents will edit " +
                          "this folder in place. Installing Git and running 'git init' here would " +
                          "let you set this up again with every task in its own branch.",
                Example = detected.IsRepository
                    ? string.Empty
                    : "Commit anything you care about before running a task, or work on a copy. " +
                      "There is no branch to throw away if an agent does something you did not want.",
                ExampleCaption = "UNTIL THEN",
                Options = _ => detected.IsRepository
                    ?
                    [
                        new WizardOption("git", "Git-backed",
                            "each task gets its own branch and checkout; your files never move",
                            Recommended: true),
                        new WizardOption("standalone", "Standalone",
                            "agents edit this folder directly, with nothing to roll back to")
                    ]
                    : new[]
                    {
                        new WizardOption("standalone", "Yes, edit this folder",
                            detected.GitInstalled
                                ? "the only option here until this folder is a Git repository"
                                : "the only option here until Git is installed",
                            Recommended: true)
                    }
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
