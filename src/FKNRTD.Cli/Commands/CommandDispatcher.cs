using System.Text.Json;
using FKNRTD.Dashboard;
using FKNRTD.Help;
using FKNRTD.Domain;
using FKNRTD.Services;

namespace FKNRTD.Commands;

internal static class CommandDispatcher
{
    public const string Version = "1.0.0";

    /// <summary>
    /// The commands the switch below handles. Kept beside it so a mistyped command can be rejected
    /// with a useful message before a workspace is located for it.
    /// </summary>
    private static readonly HashSet<string> Dispatched = new(StringComparer.OrdinalIgnoreCase)
    {
        "dashboard", "status", "doctor", "task", "run", "land", "agent", "message", "claim",
        "usage", "events", "config", "telemetry", "hook"
    };

    public static async Task<int> ExecuteAsync(
        CliArguments arguments,
        CancellationToken cancellationToken)
    {
        if (arguments.Command is "help" or "-h" or "--help" || arguments.Has("help") || arguments.Has("h"))
        {
            return HelpCommand.Execute(arguments, UseColor(arguments));
        }

        if (arguments.Command is "version" or "-v" or "--version" || arguments.Has("version") || arguments.Has("v"))
        {
            Console.WriteLine($"FKNRTD.CLI {Version}");
            return 0;
        }

        if (arguments.Command == "init")
        {
            return await InitializeAsync(arguments, cancellationToken).ConfigureAwait(false);
        }

        if (arguments.Command == "telemetry" && arguments.Subcommand == "claude-statusline")
        {
            return await StatusLineRenderer.RenderClaudeAsync(arguments, cancellationToken).ConfigureAwait(false);
        }

        // The glossary needs no workspace: someone who hit an unfamiliar word in an error message
        // should be able to look it up from any directory.
        if (arguments.Command is "explain" or "glossary")
        {
            return ExplainCommand.Execute(arguments, UseColor(arguments));
        }

        // The portal is generated from the same tables the running program reads, so it needs no
        // workspace and cannot describe a command or a term that does not exist.
        if (arguments.Command == "portal")
        {
            return PortalCommand.Execute(arguments);
        }

        if (arguments.Command == "integration")
        {
            return await IntegrationCommandAsync(arguments, cancellationToken).ConfigureAwait(false);
        }

        // A bare 'fknrtd' (or an explicit '.' / '.fknrtd') opens the current folder with the
        // default loading parameters rather than printing help.
        if (arguments.Command.Length == 0 || arguments.Command is "." or ".fknrtd")
        {
            return await OpenHereAsync(
                    arguments,
                    currentFolderOnly: arguments.Command.Length != 0,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        // A mistyped command is recognised before a workspace is located. Otherwise `fknrtd taks`
        // outside a workspace fails with "no workspace was found", which sends the operator off to
        // fix the wrong problem entirely.
        if (!Dispatched.Contains(arguments.Command))
        {
            return Unknown(arguments.Command);
        }

        var runtime = CreateRuntime(arguments);
        return arguments.Command switch
        {
            "dashboard" => await RunDashboardAsync(runtime, arguments, cancellationToken).ConfigureAwait(false),
            "status" => await StatusAsync(runtime, arguments, cancellationToken).ConfigureAwait(false),
            "doctor" => await DoctorAsync(runtime, arguments, cancellationToken).ConfigureAwait(false),
            "task" => await TaskCommandAsync(runtime, arguments, cancellationToken).ConfigureAwait(false),
            "run" => await RunTaskAsync(runtime, Required(arguments.Get("id") ?? arguments.Positional(1), "task ID"),
                cancellationToken).ConfigureAwait(false),
            "land" => await LandTaskAsync(runtime, arguments, cancellationToken).ConfigureAwait(false),
            "agent" => await AgentCommandAsync(runtime, arguments, cancellationToken).ConfigureAwait(false),
            "message" => await MessageCommandAsync(runtime, arguments, cancellationToken).ConfigureAwait(false),
            "claim" => await ClaimCommandAsync(runtime, arguments, cancellationToken).ConfigureAwait(false),
            "usage" => await UsageCommandAsync(runtime, arguments, cancellationToken).ConfigureAwait(false),
            "events" => await EventsAsync(runtime, arguments, cancellationToken).ConfigureAwait(false),
            "config" => await ConfigCommandAsync(runtime, arguments, cancellationToken).ConfigureAwait(false),
            "telemetry" or "hook" => await TelemetryCommandAsync(runtime, arguments, cancellationToken)
                .ConfigureAwait(false),
            _ => Unknown(arguments.Command)
        };
    }

    private static FknrtdRuntime CreateRuntime(CliArguments arguments)
    {
        var root = arguments.Get("root");
        return new FknrtdRuntime(WorkspaceLocator.Find(root));
    }

    /// <summary>
    /// Opens the dashboard for the folder the terminal is in, provisioning a workspace with the
    /// default loading parameters when none exists yet.
    /// </summary>
    /// <param name="currentFolderOnly">
    /// When <see langword="true"/> the current folder is used verbatim; otherwise an enclosing
    /// workspace is preferred so that running from a subdirectory still finds the project.
    /// </param>
    private static async Task<int> OpenHereAsync(
        CliArguments arguments,
        bool currentFolderOnly,
        CancellationToken cancellationToken)
    {
        var requestedRoot = arguments.Get("root");
        FknrtdPaths paths;
        if (requestedRoot is not null)
        {
            paths = WorkspaceLocator.ForRoot(requestedRoot);
        }
        else if (currentFolderOnly)
        {
            paths = WorkspaceLocator.ForRoot(Environment.CurrentDirectory);
        }
        else
        {
            paths = WorkspaceLocator.TryFind()
                ?? WorkspaceLocator.ForRoot(
                    await DefaultRootAsync(Environment.CurrentDirectory, cancellationToken)
                        .ConfigureAwait(false));
        }

        if (!File.Exists(paths.Config))
        {
            var config = await ProvisionAsync(paths, RequestedMode(arguments), cancellationToken)
                .ConfigureAwait(false);
            Console.WriteLine(
                $"√ Prepared a {DescribeMode(config.Mode)} FKNRTD.CLI workspace at {paths.Root}");
        }

        return await RunDashboardAsync(new FknrtdRuntime(paths), arguments, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Where a brand new workspace belongs when nothing was found above the current folder.
    /// Inside a Git repository that is the repository root, because worktrees, base refs and
    /// changed paths are all resolved against it; a subdirectory would be the wrong project root.
    /// Outside one it is the folder itself.
    /// </summary>
    private static async Task<string> DefaultRootAsync(string start, CancellationToken cancellationToken)
    {
        var snapshot = await new GitService(new ProcessRunner())
            .GetSnapshotAsync(start, cancellationToken)
            .ConfigureAwait(false);
        return snapshot.IsRepository ? snapshot.RepositoryRoot : start;
    }

    private static async Task<int> InitializeAsync(CliArguments arguments, CancellationToken cancellationToken)
    {
        var root = Path.GetFullPath(arguments.Get("root") ?? arguments.Positional(1) ?? Environment.CurrentDirectory);
        var paths = WorkspaceLocator.ForRoot(root);
        if (File.Exists(paths.Config) && !arguments.Has("force"))
        {
            throw new InvalidOperationException(
                $"FKNRTD.CLI is already set up at {root}. Nothing was changed. " +
                "Use -force to replace its configuration, keeping a backup of the old one.");
        }

        string? backup = null;
        if (File.Exists(paths.Config))
        {
            var backupDirectory = Path.Combine(paths.Runtime, "backups");
            Directory.CreateDirectory(backupDirectory);
            backup = Path.Combine(
                backupDirectory,
                "config-" + DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss-fff") + ".json");
            File.Copy(paths.Config, backup, overwrite: false);
        }

        // Scripts and the self-test suite pass -yes. An operator at a keyboard gets asked, because
        // the mode and the verification commands are the two settings that most determine how safe
        // this workspace is, and both were previously decided silently on their behalf.
        var guided = !arguments.Has("yes") && !arguments.Has("quiet") && !NonInteractive;
        Wizard? answers = null;
        if (guided)
        {
            answers = await GuidedSetupAsync(paths, arguments, cancellationToken).ConfigureAwait(false);
            if (answers is null)
            {
                Console.WriteLine("Cancelled. Nothing was set up.");
                return 0;
            }
        }

        var requested = answers is not null && answers.Value("mode") == "standalone"
            ? WorkspaceMode.Standalone
            : RequestedMode(arguments);
        var config = await ProvisionAsync(paths, requested, cancellationToken, answers).ConfigureAwait(false);

        Console.WriteLine($"√ FKNRTD.CLI is set up for {config.ProjectName} ({DescribeMode(config.Mode)})");
        Console.WriteLine($"  Configuration: {paths.Config}");
        if (backup is not null)
        {
            Console.WriteLine($"  The previous configuration was kept at {backup}");
        }

        Console.WriteLine(config.Mode == WorkspaceMode.Git
            ? $"  Tasks start from and merge back into: {config.DefaultBaseRef}"
            : "  No branch. Agents edit this folder directly and landing records completion.");
        Console.WriteLine(config.DefaultVerificationCommands.Count == 0
            ? "  Nothing verifies agent work yet. Add a build or test command when a task asks."
            : "  New tasks are verified by: " + string.Join("; ", config.DefaultVerificationCommands));
        Console.WriteLine($"  Agents configured: {string.Join(", ", config.Agents.Select(agent => agent.Id))}");

        if (answers?.Value("statusline") == "yes")
        {
            try
            {
                var installed = await new ClaudeIntegrationService()
                    .InstallStatusLineAsync(projectScope: false, force: false, cancellationToken)
                    .ConfigureAwait(false);
                Console.WriteLine($"  Installed the Claude statusline in {installed}. Restart Claude Code to load it.");
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
                                              InvalidOperationException)
            {
                Console.WriteLine("  Could not install the Claude statusline: " + exception.Message);
                Console.WriteLine("  Install it later with: fknrtd integration install-claude-statusline");
            }
        }

        Console.WriteLine();
        switch (answers?.Value("then"))
        {
            case "dashboard":
                return await RunDashboardAsync(new FknrtdRuntime(paths), arguments, cancellationToken)
                    .ConfigureAwait(false);
            case "nothing":
                Console.WriteLine("When you are ready: fknrtd doctor, then fknrtd task new.");
                return 0;
            default:
                if (answers is null)
                {
                    Console.WriteLine("Next: fknrtd doctor");
                    return 0;
                }

                Console.WriteLine("Checking that everything a task run depends on is installed...");
                Console.WriteLine();
                var code = await DoctorAsync(new FknrtdRuntime(paths), arguments, cancellationToken)
                    .ConfigureAwait(false);
                Console.WriteLine();
                Console.WriteLine(code == 0
                    ? "Everything is ready. Describe your first piece of work with: fknrtd task new"
                    : "Fix the failing items above, then run 'fknrtd doctor' again.");
                return 0;
        }
    }

    /// <summary>
    /// Asks the setup questions on screen. Returns null when the operator backed out, so a cancelled
    /// setup leaves the folder exactly as it was rather than half-configured.
    /// </summary>
    private static async Task<Wizard?> GuidedSetupAsync(
        FknrtdPaths paths,
        CliArguments arguments,
        CancellationToken cancellationToken)
    {
        var git = new GitService(new ProcessRunner());
        var isRepository = await git.IsRepositoryAsync(paths.Root, cancellationToken).ConfigureAwait(false);
        var snapshot = isRepository
            ? await git.GetSnapshotAsync(paths.Root, cancellationToken).ConfigureAwait(false)
            : null;
        var wizard = SetupWizard.Create(new SetupWizard.Detected(
            paths.Root,
            isRepository,
            GitService.IsInstalled(),
            snapshot is null || snapshot.Branch == "detached" ? string.Empty : snapshot.Branch,
            DetectVerificationCommands(paths.Root),
            HasClaude: BuiltInAgents.CreateDefaults()
                .Any(agent => agent.Id.Equals("claude", StringComparison.OrdinalIgnoreCase))));

        var completed = await OverlayHost
            .RunAsync(wizard, $"Setting up {paths.Root}", UseColor(arguments), "fknrtd init -yes",
                cancellationToken)
            .ConfigureAwait(false);
        return completed ? wizard : null;
    }

    /// <summary>Whether there is a terminal to ask questions in.</summary>
    private static bool NonInteractive => Console.IsInputRedirected || Console.IsOutputRedirected;

    /// <summary>
    /// Writes a workspace at <paramref name="paths"/> using the default loading parameters. The
    /// mode is detected when <paramref name="requestedMode"/> is <see langword="null"/>, so a folder
    /// that is not a Git repository is provisioned standalone instead of being rejected.
    /// </summary>
    private static async Task<FknrtdConfig> ProvisionAsync(
        FknrtdPaths paths,
        WorkspaceMode? requestedMode,
        CancellationToken cancellationToken,
        Wizard? answers = null)
    {
        var root = paths.Root;
        var git = new GitService(new ProcessRunner());
        var isRepository = requestedMode != WorkspaceMode.Standalone &&
                           await git.IsRepositoryAsync(root, cancellationToken).ConfigureAwait(false);
        if (requestedMode == WorkspaceMode.Git && !isRepository)
        {
            throw new InvalidOperationException(GitService.IsInstalled()
                ? $"{root} is not a Git repository. Drop -git to initialize a standalone workspace."
                : "Git was not found on PATH. Drop -git to initialize a standalone workspace.");
        }

        var mode = isRepository ? WorkspaceMode.Git : WorkspaceMode.Standalone;
        var snapshot = isRepository
            ? await git.GetSnapshotAsync(root, cancellationToken).ConfigureAwait(false)
            : null;
        var config = new FknrtdConfig
        {
            ProjectName = snapshot?.RepositoryName ?? new DirectoryInfo(root).Name,
            Mode = mode,
            DefaultBaseRef = snapshot is null || string.IsNullOrWhiteSpace(snapshot.Branch) ||
                             snapshot.Branch == "detached"
                ? string.Empty
                : snapshot.Branch,
            // Nothing can be committed or merged without a repository behind the workspace.
            AutoCommitAgentChanges = mode == WorkspaceMode.Git,
            DefaultVerificationCommands = DetectVerificationCommands(root),
            Agents = BuiltInAgents.CreateDefaults().ToList()
        };

        // What the operator chose beats what the folder suggested.
        if (answers is not null)
        {
            var chosenBase = answers.Value("base");
            config = config with
            {
                DefaultBaseRef = mode == WorkspaceMode.Git && chosenBase.Length > 0
                    ? chosenBase
                    : config.DefaultBaseRef,
                DefaultVerificationCommands = answers.Lines("verify").ToList()
            };
        }

        var store = new StateStore(paths);
        await store.InitializeAsync(config, cancellationToken).ConfigureAwait(false);
        await store.AppendEventAsync(new FknrtdEvent
        {
            Severity = EventSeverity.Success,
            Type = "project.initialized",
            Message = $"Initialized a {DescribeMode(mode)} FKNRTD.CLI workspace for {config.ProjectName}."
        }, cancellationToken).ConfigureAwait(false);
        return config;
    }

    private static WorkspaceMode? RequestedMode(CliArguments arguments) => arguments.Has("standalone")
        ? WorkspaceMode.Standalone
        : arguments.Has("git")
            ? WorkspaceMode.Git
            : null;

    private static string DescribeMode(WorkspaceMode mode) =>
        mode == WorkspaceMode.Git ? "Git-backed" : "standalone";

    private static List<string> DetectVerificationCommands(string root)
    {
        var hasDotNet = Directory.EnumerateFiles(root, "*.sln", SearchOption.TopDirectoryOnly).Any() ||
                        Directory.EnumerateFiles(root, "*.slnx", SearchOption.TopDirectoryOnly).Any() ||
                        Directory.EnumerateFiles(root, "*.csproj", SearchOption.TopDirectoryOnly).Any();
        return hasDotNet ? ["dotnet build", "dotnet test --no-build"] : [];
    }

    private static Task<int> RunDashboardAsync(
        FknrtdRuntime runtime,
        CliArguments arguments,
        CancellationToken cancellationToken) => RunDashboardCoreAsync(runtime, arguments, cancellationToken);

    private static async Task<int> RunDashboardCoreAsync(
        FknrtdRuntime runtime,
        CliArguments arguments,
        CancellationToken cancellationToken)
    {
        var color = UseColor(arguments);
        await runtime.Dashboard.RunAsync(
                arguments.Has("once"),
                color,
                GetDimension(arguments, "width"),
                GetDimension(arguments, "height"),
                cancellationToken)
            .ConfigureAwait(false);
        return 0;
    }

    private static async Task<int> StatusAsync(
        FknrtdRuntime runtime,
        CliArguments arguments,
        CancellationToken cancellationToken)
    {
        var snapshot = await runtime.Snapshots.CaptureAsync(cancellationToken).ConfigureAwait(false);
        if (arguments.Has("json"))
        {
            PrintJson(snapshot);
            return snapshot.Conflicts.Any(conflict => conflict.Kind == ConflictKind.Collision) ? 3 : 0;
        }

        await runtime.Dashboard.RunAsync(
                once: true,
                UseColor(arguments),
                GetDimension(arguments, "width"),
                GetDimension(arguments, "height"),
                cancellationToken)
            .ConfigureAwait(false);
        return snapshot.Conflicts.Any(conflict => conflict.Kind == ConflictKind.Collision) ? 3 : 0;
    }

    private static async Task<int> DoctorAsync(
        FknrtdRuntime runtime,
        CliArguments arguments,
        CancellationToken cancellationToken)
    {
        var checks = await runtime.Doctor.RunAsync(cancellationToken).ConfigureAwait(false);
        if (arguments.Has("json"))
        {
            PrintJson(checks);
        }
        else
        {
            Console.WriteLine($"FKNRTD.CLI {Version} diagnostics");
            foreach (var check in checks)
            {
                Console.WriteLine($"{(check.Passed ? "√" : check.Required ? "×" : "∆")} {check.Name,-26} {check.Detail}");
            }
        }

        return checks.Any(check => check.Required && !check.Passed) ? 2 : 0;
    }

    private static async Task<int> TaskCommandAsync(
        FknrtdRuntime runtime,
        CliArguments arguments,
        CancellationToken cancellationToken)
    {
        return arguments.Subcommand switch
        {
            "create" => await CreateTaskAsync(runtime, arguments, cancellationToken).ConfigureAwait(false),
            "list" or "ls" or "" => await ListTasksAsync(runtime, arguments, cancellationToken).ConfigureAwait(false),
            "show" => await ShowTaskAsync(runtime, arguments, cancellationToken).ConfigureAwait(false),
            "diff" => await DiffTaskAsync(runtime, arguments, cancellationToken).ConfigureAwait(false),
            "new" => await NewTaskAsync(runtime, arguments, cancellationToken).ConfigureAwait(false),
            "run" => await RunTaskAsync(runtime,
                Required(arguments.Get("id") ?? arguments.Positional(2), "task ID"), cancellationToken)
                .ConfigureAwait(false),
            "retry" => await RetryTaskAsync(runtime, arguments, cancellationToken).ConfigureAwait(false),
            "cancel" => await CancelTaskAsync(runtime, arguments, cancellationToken).ConfigureAwait(false),
            "land" => await LandTaskAsync(runtime, arguments, cancellationToken).ConfigureAwait(false),
            "cleanup" => await CleanupTaskAsync(runtime, arguments, cancellationToken).ConfigureAwait(false),
            _ => Unknown("task " + arguments.Subcommand)
        };
    }

    private static async Task<int> CreateTaskAsync(
        FknrtdRuntime runtime,
        CliArguments arguments,
        CancellationToken cancellationToken)
    {
        var config = await runtime.Store.LoadConfigAsync(cancellationToken).ConfigureAwait(false);
        var title = Required(arguments.Get("title") ?? arguments.Positional(2), "task title");
        var brief = arguments.Get("brief");
        var briefFile = arguments.Get("brief-file");
        if (!string.IsNullOrWhiteSpace(briefFile))
        {
            brief = await File.ReadAllTextAsync(Path.GetFullPath(briefFile), cancellationToken).ConfigureAwait(false);
        }

        brief = Required(brief, "-brief or -brief-file");
        var lead = arguments.Get("lead") ?? PreferredAgent(config, "claude", 0);
        var implementer = arguments.Get("implementer") ?? PreferredAgent(config, "codex", 1);
        var auditor = arguments.Get("auditor") ?? lead;
        var verification = arguments.GetMany("verify");
        if (verification.Count == 0)
        {
            verification = config.DefaultVerificationCommands;
        }

        var task = await runtime.Tasks.CreateAsync(
                title,
                brief,
                lead,
                implementer,
                auditor,
                verification,
                arguments.Get("base"),
                arguments.GetInt("repairs"),
                cancellationToken)
            .ConfigureAwait(false);
        Console.WriteLine($"√ Created {task.Id}: {task.Title}");
        Console.WriteLine($"  {task.LeadAgentId} plans, {task.ImplementerAgentId} implements, {task.AuditorAgentId} audits");
        Console.WriteLine($"  Verification commands: {task.VerificationCommands.Count}");
        if (arguments.Has("run"))
        {
            return await RunTaskAsync(runtime, task.Id, cancellationToken).ConfigureAwait(false);
        }

        Console.WriteLine($"  Run: fknrtd task run {task.Id}");
        return 0;
    }

    /// <summary>
    /// <c>fknrtd task new</c>. The same guided, explained form the dashboard opens for N, run on its
    /// own. It exists so that the command line is not the surface where an operator is expected to
    /// already know what a brief, a lead and an auditor are.
    /// </summary>
    private static async Task<int> NewTaskAsync(
        FknrtdRuntime runtime,
        CliArguments arguments,
        CancellationToken cancellationToken)
    {
        var config = await runtime.Store.LoadConfigAsync(cancellationToken).ConfigureAwait(false);
        if (!config.Agents.Any(agent => agent.Enabled))
        {
            throw new InvalidOperationException(
                "No agents are enabled, so there is nobody to give the work to. " +
                "Run 'fknrtd agent list' to see what is configured.");
        }

        var wizard = TaskWizard.Create(config);
        var completed = await OverlayHost
            .RunAsync(wizard, $"{config.ProjectName} · {DescribeMode(config.Mode)} workspace",
                UseColor(arguments), "fknrtd task create", cancellationToken)
            .ConfigureAwait(false);
        if (!completed)
        {
            Console.WriteLine("Cancelled. No task was created.");
            return 0;
        }

        var task = await runtime.Tasks.CreateAsync(
                wizard.Value("title"),
                wizard.Value("brief"),
                wizard.Value("lead"),
                wizard.Value("implementer"),
                wizard.Value("auditor"),
                wizard.Lines("verify"),
                config.Mode == WorkspaceMode.Git ? wizard.Value("base") : null,
                int.TryParse(wizard.Value("repairs"), out var repairs) ? repairs : null,
                cancellationToken)
            .ConfigureAwait(false);

        Console.WriteLine($"√ Created {task.Id}: {task.Title}");
        Console.WriteLine($"  {task.LeadAgentId} plans · {task.ImplementerAgentId} implements · {task.AuditorAgentId} audits");
        Console.WriteLine(task.VerificationCommands.Count == 0
            ? "  Nothing verifies this work, so the audit is the only gate."
            : "  Verified by: " + string.Join("; ", task.VerificationCommands));
        if (wizard.Value("then") == "run")
        {
            return await RunTaskAsync(runtime, task.Id, cancellationToken).ConfigureAwait(false);
        }

        Console.WriteLine($"  Run it with: fknrtd task run {task.Id}");
        return 0;
    }

    private static async Task<int> ListTasksAsync(
        FknrtdRuntime runtime,
        CliArguments arguments,
        CancellationToken cancellationToken)
    {
        var tasks = (await runtime.Store.LoadTasksAsync(cancellationToken).ConfigureAwait(false))
            .OrderByDescending(task => task.UpdatedAt)
            .ToArray();
        if (arguments.Has("json"))
        {
            PrintJson(tasks);
            return 0;
        }

        if (tasks.Length == 0)
        {
            Console.WriteLine("No FKNRTD.CLI tasks exist.");
            return 0;
        }

        Console.WriteLine("ID                           STATUS         STAGE          AGENTS                 TITLE");
        foreach (var task in tasks)
        {
            Console.WriteLine($"{task.Id,-28} {task.Status,-14} {task.CurrentStage,-14} {task.LeadAgentId}>{task.ImplementerAgentId,-14} {task.Title}");
        }

        return 0;
    }

    private static async Task<int> ShowTaskAsync(
        FknrtdRuntime runtime,
        CliArguments arguments,
        CancellationToken cancellationToken)
    {
        var id = Required(arguments.Get("id") ?? arguments.Positional(2), "task ID");
        var task = await runtime.Store.LoadTaskAsync(id, cancellationToken).ConfigureAwait(false);
        if (arguments.Has("json"))
        {
            PrintJson(task);
            return 0;
        }

        var config = await runtime.Store.LoadConfigAsync(cancellationToken).ConfigureAwait(false);
        var width = Math.Clamp(Screen.Width(88) - 2, 40, 96);

        Console.WriteLine(task.Title);
        Console.WriteLine($"{task.Id}   {Glossary.Find("status." + task.Status.ToString().ToLowerInvariant())?.Title ?? task.Status.ToString()}");
        Console.WriteLine();

        Console.WriteLine("WHAT WAS ASKED FOR");
        foreach (var line in Text.Wrap(task.Brief, width - 2))
        {
            Console.WriteLine("  " + line);
        }

        Console.WriteLine();
        Console.WriteLine("WHO IS ON IT");
        Console.WriteLine($"  Lead         {task.LeadAgentId}  —  reads the code and writes the plan, read-only");
        Console.WriteLine($"  Implementer  {task.ImplementerAgentId}  —  the only agent that may change files");
        Console.WriteLine($"  Auditor      {task.AuditorAgentId}  —  judges the finished work read-only, must return PASS");

        Console.WriteLine();
        Console.WriteLine("WHERE THE WORK HAPPENS");
        if (config.Mode == WorkspaceMode.Git)
        {
            Console.WriteLine($"  Base branch  {Blank(task.BaseRef)}");
            Console.WriteLine($"  Task branch  {Blank(task.BranchName)}");
            Console.WriteLine($"  Worktree     {Blank(task.WorktreePath)}");
        }
        else
        {
            Console.WriteLine("  This is a standalone workspace: agents edit the project folder directly.");
        }

        Console.WriteLine();
        Console.WriteLine("HOW CORRECTNESS IS DECIDED");
        if (task.VerificationCommands.Count == 0)
        {
            foreach (var line in Text.Wrap(
                         "Nothing verifies this work, so the audit is the only gate — and an audit is a " +
                         "judgement rather than a measurement.", width - 2))
            {
                Console.WriteLine("  " + line);
            }
        }
        else
        {
            foreach (var command in task.VerificationCommands)
            {
                Console.WriteLine($"  must exit 0  {command}");
            }
        }

        Console.WriteLine($"  Repair       {task.RepairRound} used of {task.MaxRepairRounds} allowed");

        Console.WriteLine();
        Console.WriteLine("PIPELINE");
        foreach (var stage in task.Stages)
        {
            var entry = Glossary.Find("stage." + stage.Stage.ToString().ToLowerInvariant());
            var summary = string.IsNullOrWhiteSpace(stage.Summary) ? entry?.Summary ?? string.Empty : stage.Summary;
            Console.WriteLine($"  {StageIcon(stage.State)} {stage.Stage,-13} {Text.Truncate(summary, Math.Max(20, width - 18))}");
        }

        if (!string.IsNullOrWhiteSpace(task.LastError))
        {
            Console.WriteLine();
            Console.WriteLine("WHAT WENT WRONG");
            foreach (var line in Text.Wrap(task.LastError, width - 2))
            {
                Console.WriteLine("  " + line);
            }
        }

        Console.WriteLine();
        Console.WriteLine("WHAT TO DO NEXT");
        foreach (var line in Text.Wrap(Reference.NextStep(task, config, onDashboard: false), width - 2))
        {
            Console.WriteLine("  " + line);
        }

        return task.Status == WorkflowStatus.Failed ? 3 : 0;
    }

    /// <summary>
    /// <c>fknrtd task diff</c>. The command-line half of the dashboard's V: the finished change, so
    /// the reading that landing asks for can be done without opening the dashboard at all.
    /// </summary>
    private static async Task<int> DiffTaskAsync(
        FknrtdRuntime runtime,
        CliArguments arguments,
        CancellationToken cancellationToken)
    {
        var id = Required(arguments.Get("id") ?? arguments.Positional(2), "task ID");
        var task = await runtime.Store.LoadTaskAsync(id, cancellationToken).ConfigureAwait(false);
        var config = await runtime.Store.LoadConfigAsync(cancellationToken).ConfigureAwait(false);

        if (config.Mode == WorkspaceMode.Standalone)
        {
            Console.WriteLine(
                "This is a standalone workspace, so there is no branch to compare against. Agents " +
                "edited the project folder directly; whatever changed is simply what is there now.");
            return 0;
        }

        if (string.IsNullOrWhiteSpace(task.WorktreePath) || !Directory.Exists(task.WorktreePath))
        {
            Console.WriteLine(
                $"{task.Id} has no worktree yet, so there is nothing to compare. It has not reached " +
                "its worktree stage.");
            return 0;
        }

        var (lines, truncated) = await runtime.Git
            .GetDiffAsync(task.WorktreePath, task.BaseRef, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        if (lines.Count == 0)
        {
            Console.WriteLine(
                $"Nothing has changed against {Blank(task.BaseRef)}. Either the task has not reached " +
                "its implement stage, or the implementer finished without editing anything.");
            return 0;
        }

        foreach (var line in lines)
        {
            Console.WriteLine(line);
        }

        if (truncated)
        {
            Console.WriteLine();
            Console.WriteLine($"Cut off here. Read the rest with git in {task.WorktreePath}.");
        }

        return 0;
    }

    private static async Task<int> RunTaskAsync(
        FknrtdRuntime runtime,
        string id,
        CancellationToken cancellationToken)
    {
        Console.WriteLine($"► Running {id}");
        var task = await runtime.Orchestrator.RunAsync(id, cancellationToken).ConfigureAwait(false);
        Console.WriteLine($"{(task.Status == WorkflowStatus.ReadyToLand ? "√" : "×")} {task.Id}: {task.Status}");
        if (task.Status == WorkflowStatus.ReadyToLand)
        {
            Console.WriteLine($"  Verified and audited. Land with: fknrtd task land {task.Id} -confirm LAND");
            return 0;
        }

        if (!string.IsNullOrWhiteSpace(task.LastError))
        {
            Console.Error.WriteLine("  " + task.LastError);
        }

        return 3;
    }

    private static async Task<int> RetryTaskAsync(
        FknrtdRuntime runtime,
        CliArguments arguments,
        CancellationToken cancellationToken)
    {
        var id = Required(arguments.Get("id") ?? arguments.Positional(2), "task ID");
        await runtime.Tasks.ResetFailedStagesAsync(id, cancellationToken).ConfigureAwait(false);
        return await RunTaskAsync(runtime, id, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<int> CancelTaskAsync(
        FknrtdRuntime runtime,
        CliArguments arguments,
        CancellationToken cancellationToken)
    {
        var id = Required(arguments.Get("id") ?? arguments.Positional(2), "task ID");
        await runtime.Tasks.RequestCancellationAsync(id, cancellationToken).ConfigureAwait(false);
        Console.WriteLine($"▌ Cancellation requested for {id}");
        return 0;
    }

    private static async Task<int> LandTaskAsync(
        FknrtdRuntime runtime,
        CliArguments arguments,
        CancellationToken cancellationToken)
    {
        var positional = arguments.Command == "land" ? arguments.Positional(1) : arguments.Positional(2);
        var id = Required(arguments.Get("id") ?? positional, "task ID");
        if (!string.Equals(arguments.Get("confirm"), "LAND", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Landing needs -confirm LAND spelled out. It is the only command that changes your " +
                "base branch, so it will not act on an abbreviation or a yes. Read the finished work " +
                $"first with 'fknrtd task show {id}'.");
        }

        var task = await runtime.Orchestrator.LandAsync(id, cancellationToken).ConfigureAwait(false);
        Console.WriteLine($"√ Landed {task.Id} on {task.BaseRef}");
        return 0;
    }

    private static async Task<int> CleanupTaskAsync(
        FknrtdRuntime runtime,
        CliArguments arguments,
        CancellationToken cancellationToken)
    {
        var id = Required(arguments.Get("id") ?? arguments.Positional(2), "task ID");
        var task = await runtime.Store.LoadTaskAsync(id, cancellationToken).ConfigureAwait(false);
        if (task.Status == WorkflowStatus.Running)
        {
            throw new InvalidOperationException("A running task worktree cannot be removed.");
        }

        if (!string.Equals(arguments.Get("confirm"), "REMOVE", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Removing a worktree needs -confirm REMOVE spelled out. Only the worktree directory " +
                "goes; the task record, its logs and its Git branch are all kept.");
        }

        var config = await runtime.Store.LoadConfigAsync(cancellationToken).ConfigureAwait(false);
        if (config.Mode == WorkspaceMode.Standalone)
        {
            Console.WriteLine($"√ Nothing to remove for {id}. A standalone workspace has no worktree or branch.");
            return 0;
        }

        await runtime.Worktrees
            .RemoveAsync(task, arguments.Has("force"), config.Mode, cancellationToken)
            .ConfigureAwait(false);
        Console.WriteLine(task.Status == WorkflowStatus.Landed
            ? $"√ Removed the worktree and landed task branch for {id}. The task record was retained."
            : $"√ Removed the worktree for {id}. The task record and Git branch were retained.");
        return 0;
    }

    private static async Task<int> AgentCommandAsync(
        FknrtdRuntime runtime,
        CliArguments arguments,
        CancellationToken cancellationToken)
    {
        return arguments.Subcommand switch
        {
            "list" or "ls" or "" => await ListAgentsAsync(runtime, arguments, cancellationToken).ConfigureAwait(false),
            "new" => await NewAgentAsync(runtime, arguments, cancellationToken).ConfigureAwait(false),
            "add" => await AddAgentAsync(runtime, arguments, cancellationToken).ConfigureAwait(false),
            "enable" => await SetAgentEnabledAsync(runtime, arguments, true, cancellationToken).ConfigureAwait(false),
            "disable" => await SetAgentEnabledAsync(runtime, arguments, false, cancellationToken).ConfigureAwait(false),
            "remove" => await RemoveAgentAsync(runtime, arguments, cancellationToken).ConfigureAwait(false),
            _ => Unknown("agent " + arguments.Subcommand)
        };
    }

    private static async Task<int> ListAgentsAsync(
        FknrtdRuntime runtime,
        CliArguments arguments,
        CancellationToken cancellationToken)
    {
        var config = await runtime.Store.LoadConfigAsync(cancellationToken).ConfigureAwait(false);
        if (arguments.Has("json"))
        {
            PrintJson(config.Agents);
            return 0;
        }

        Console.WriteLine("ID           ENABLED  FOUND  KIND       EXECUTABLE");
        var missing = new List<AgentDefinition>();
        foreach (var agent in config.Agents)
        {
            var found = ExecutableLocator.Find(agent.Executable) is not null;
            if (!found && agent.Enabled)
            {
                missing.Add(agent);
            }

            Console.WriteLine($"{agent.Id,-12} {(agent.Enabled ? "yes" : "no"),-8} {(found ? "yes" : "no"),-6} {agent.Kind,-10} {agent.Executable}");
        }

        // A column of yes and no is a diagnosis without a prescription. An enabled agent whose
        // executable is not on PATH will fail a task at launch, well after it was assigned.
        if (missing.Count > 0)
        {
            Console.WriteLine();
            foreach (var agent in missing)
            {
                Console.WriteLine(
                    $"  {agent.Id} is enabled but '{agent.Executable}' is not on PATH. A task assigned " +
                    $"to it would fail at launch. Install it, correct the executable in " +
                    $"{runtime.Paths.Config}, or run 'fknrtd agent disable {agent.Id}'.");
            }
        }

        return 0;
    }

    /// <summary>
    /// <c>fknrtd agent new</c>. The guided form for registering a coding CLI. `agent add` still
    /// exists and is what a script should use; this is what a person should use, because the option
    /// list it replaces is the most cryptic thing the product asks anyone to type.
    /// </summary>
    private static async Task<int> NewAgentAsync(
        FknrtdRuntime runtime,
        CliArguments arguments,
        CancellationToken cancellationToken)
    {
        var config = await runtime.Store.LoadConfigAsync(cancellationToken).ConfigureAwait(false);
        var wizard = AgentWizard.Create(config);
        var completed = await OverlayHost
            .RunAsync(wizard, $"{config.ProjectName} - registering a coding CLI", UseColor(arguments),
                "fknrtd agent add", cancellationToken)
            .ConfigureAwait(false);
        if (!completed)
        {
            Console.WriteLine("Cancelled. No agent was registered.");
            return 0;
        }

        var agent = AgentWizard.Build(wizard);
        ValidateAgentDefinition(agent);
        await runtime.Store
            .SaveConfigAsync(config with { Agents = config.Agents.Append(agent).ToList() }, cancellationToken)
            .ConfigureAwait(false);

        Console.WriteLine($"√ Registered {agent.DisplayName}");
        foreach (var line in AgentWizard.Report(agent, wizard.Value("audit") == "yes"))
        {
            Console.WriteLine(line);
        }

        return 0;
    }

    private static async Task<int> AddAgentAsync(
        FknrtdRuntime runtime,
        CliArguments arguments,
        CancellationToken cancellationToken)
    {
        AgentDefinition agent;
        var file = arguments.Get("file");
        if (!string.IsNullOrWhiteSpace(file))
        {
            await using var stream = File.OpenRead(Path.GetFullPath(file));
            agent = await JsonSerializer.DeserializeAsync<AgentDefinition>(stream, JsonSupport.Options, cancellationToken)
                        .ConfigureAwait(false)
                    ?? throw new InvalidDataException("The agent definition file was empty.");
        }
        else
        {
            var id = Required(arguments.Get("id") ?? arguments.Positional(2), "agent ID");
            var executable = Required(arguments.Get("exe"), "-exe");
            var fallbackArguments = arguments.GetMany("arg");
            if (fallbackArguments.Count == 0 && !arguments.Has("stdin"))
            {
                fallbackArguments = ["{prompt}"];
            }

            AgentCommandProfile Profile(string option, bool audit = false)
            {
                var values = arguments.GetMany(option);
                var profileArguments = values.Count == 0 ? fallbackArguments : values;
                return new AgentCommandProfile
                {
                    Arguments = profileArguments.ToList(),
                    PromptDelivery = arguments.Has("stdin") ? PromptDelivery.StandardInput : PromptDelivery.Argument,
                    SuccessMarker = audit ? "FKNRTD_VERDICT: PASS" : null,
                    FailureMarker = audit ? "FKNRTD_VERDICT: FAIL" : null
                };
            }

            agent = new AgentDefinition
            {
                Id = id,
                DisplayName = arguments.Get("name") ?? id,
                Kind = arguments.Get("kind") ?? "generic",
                Executable = executable,
                Color = arguments.Get("color") ?? "cyan",
                Profiles = new Dictionary<string, AgentCommandProfile>(StringComparer.OrdinalIgnoreCase)
                {
                    ["default"] = Profile("default-arg"),
                    ["plan"] = Profile("plan-arg"),
                    ["implement"] = Profile("implement-arg"),
                    ["audit"] = Profile("audit-arg", audit: true)
                }
            };
        }

        ValidateAgentDefinition(agent);
        var config = await runtime.Store.LoadConfigAsync(cancellationToken).ConfigureAwait(false);
        if (config.Agents.Any(item => item.Id.Equals(agent.Id, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException($"Agent '{agent.Id}' is already configured.");
        }

        var updated = config with { Agents = config.Agents.Append(agent).ToList() };
        await runtime.Store.SaveConfigAsync(updated, cancellationToken).ConfigureAwait(false);
        Console.WriteLine($"√ Added {agent.DisplayName} using {agent.Executable}");
        return 0;
    }

    private static async Task<int> SetAgentEnabledAsync(
        FknrtdRuntime runtime,
        CliArguments arguments,
        bool enabled,
        CancellationToken cancellationToken)
    {
        var id = Required(arguments.Get("id") ?? arguments.Positional(2), "agent ID");
        var config = await runtime.Store.LoadConfigAsync(cancellationToken).ConfigureAwait(false);
        if (!config.Agents.Any(agent => agent.Id.Equals(id, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException($"Agent '{id}' is not configured.");
        }

        var updated = config with
        {
            Agents = config.Agents.Select(agent =>
                agent.Id.Equals(id, StringComparison.OrdinalIgnoreCase) ? agent with { Enabled = enabled } : agent)
                .ToList()
        };
        await runtime.Store.SaveConfigAsync(updated, cancellationToken).ConfigureAwait(false);
        Console.WriteLine($"√ Agent {id} {(enabled ? "enabled" : "disabled")}");
        return 0;
    }

    private static async Task<int> RemoveAgentAsync(
        FknrtdRuntime runtime,
        CliArguments arguments,
        CancellationToken cancellationToken)
    {
        var id = Required(arguments.Get("id") ?? arguments.Positional(2), "agent ID");
        if (!string.Equals(arguments.Get("confirm"), "REMOVE", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Removing an agent requires the explicit option -confirm REMOVE.");
        }

        var config = await runtime.Store.LoadConfigAsync(cancellationToken).ConfigureAwait(false);
        var agents = config.Agents.Where(agent => !agent.Id.Equals(id, StringComparison.OrdinalIgnoreCase)).ToList();
        if (agents.Count == config.Agents.Count)
        {
            throw new InvalidOperationException($"Agent '{id}' is not configured.");
        }

        await runtime.Store.SaveConfigAsync(config with { Agents = agents }, cancellationToken).ConfigureAwait(false);
        Console.WriteLine($"√ Removed agent {id} from the configuration");
        return 0;
    }

    private static async Task<int> MessageCommandAsync(
        FknrtdRuntime runtime,
        CliArguments arguments,
        CancellationToken cancellationToken)
    {
        switch (arguments.Subcommand)
        {
            case "send":
            {
                var message = await runtime.Messages.SendAsync(
                        Required(arguments.Get("from"), "-from"),
                        Required(arguments.Get("to"), "-to"),
                        Required(arguments.Get("text"), "-text"),
                        arguments.Get("task"),
                        cancellationToken)
                    .ConfigureAwait(false);
                Console.WriteLine($"√ Message {message.Id} delivered");
                return 0;
            }
            case "ack":
            case "acknowledge":
                await runtime.Messages.AcknowledgeAsync(
                        Required(arguments.Get("id") ?? arguments.Positional(2), "message ID"), cancellationToken)
                    .ConfigureAwait(false);
                Console.WriteLine("√ Message acknowledged");
                return 0;
            case "list":
            case "":
            {
                var messages = await runtime.Messages.GetCurrentAsync(arguments.GetInt("limit") ?? 50, cancellationToken)
                    .ConfigureAwait(false);
                if (arguments.Has("json"))
                {
                    PrintJson(messages);
                }
                else
                {
                    foreach (var message in messages)
                    {
                        Console.WriteLine($"{message.CreatedAt:O} {message.FromAgentId}>{message.ToAgentId} [{message.Delivery}] {message.Text} ({message.Id})");
                    }
                }

                return 0;
            }
            default:
                return Unknown("message " + arguments.Subcommand);
        }
    }

    private static async Task<int> ClaimCommandAsync(
        FknrtdRuntime runtime,
        CliArguments arguments,
        CancellationToken cancellationToken)
    {
        switch (arguments.Subcommand)
        {
            case "add":
            {
                var mode = ParseEnum(arguments.Get("mode") ?? "write", ClaimMode.Write);
                var ttl = arguments.GetInt("ttl") ?? 300;
                if (ttl <= 0)
                {
                    throw new ArgumentException("Option -ttl must be greater than zero.");
                }

                var claim = await runtime.Claims.AddAsync(
                        Required(arguments.Get("agent"), "-agent"),
                        arguments.GetMany("path"),
                        mode,
                        arguments.Get("worktree") ?? runtime.Paths.Root,
                        arguments.Get("task"),
                        TimeSpan.FromSeconds(ttl),
                        cancellationToken)
                    .ConfigureAwait(false);
                Console.WriteLine($"√ Claim {claim.Id} registered until {claim.ExpiresAt:O}");
                return 0;
            }
            case "renew":
            {
                var ttl = arguments.GetInt("ttl") ?? 300;
                if (ttl <= 0)
                {
                    throw new ArgumentException("Option -ttl must be greater than zero.");
                }

                var claim = await runtime.Claims.RenewAsync(
                        Required(arguments.Get("id") ?? arguments.Positional(2), "claim ID"),
                        TimeSpan.FromSeconds(ttl),
                        cancellationToken)
                    .ConfigureAwait(false);
                Console.WriteLine($"√ Claim renewed until {claim.ExpiresAt:O}");
                return 0;
            }
            case "release":
                runtime.Claims.Release(Required(arguments.Get("id") ?? arguments.Positional(2), "claim ID"));
                Console.WriteLine("√ Claim released");
                return 0;
            case "list":
            case "":
            {
                var claims = await runtime.Store.LoadClaimsAsync(cancellationToken).ConfigureAwait(false);
                var agents = await runtime.Store.LoadAgentRuntimesAsync(cancellationToken).ConfigureAwait(false);
                var conflicts = runtime.Claims.Detect(claims, agents, DateTimeOffset.UtcNow);
                if (arguments.Has("json"))
                {
                    PrintJson(new { claims, conflicts });
                }
                else
                {
                    foreach (var claim in claims)
                    {
                        Console.WriteLine($"{claim.Id} {claim.AgentId} {claim.Mode} {string.Join(", ", claim.Paths)} until {claim.ExpiresAt:O}");
                    }

                    foreach (var conflict in conflicts)
                    {
                        Console.WriteLine($"{(conflict.Kind == ConflictKind.Collision ? "×" : "∆")} {conflict.Kind} {conflict.Summary}: {string.Join(", ", conflict.Paths)}");
                    }
                }

                return conflicts.Any(conflict => conflict.Kind == ConflictKind.Collision) ? 3 : 0;
            }
            default:
                return Unknown("claim " + arguments.Subcommand);
        }
    }

    private static async Task<int> UsageCommandAsync(
        FknrtdRuntime runtime,
        CliArguments arguments,
        CancellationToken cancellationToken)
    {
        switch (arguments.Subcommand)
        {
            case "refresh":
            {
                var agent = arguments.Get("agent") ?? arguments.Positional(2) ?? "codex";
                if (!agent.Equals("codex", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        "Only Codex supports an on-demand rate-limit refresh. Claude reports usage through its statusline payload.");
                }

                var snapshot = await runtime.Usage.RefreshCodexAsync(cancellationToken).ConfigureAwait(false);
                PrintUsage(snapshot);
                return 0;
            }
            case "set":
            {
                var snapshot = await runtime.Usage.SetAsync(
                        Required(arguments.Get("agent") ?? arguments.Positional(2), "agent ID"),
                        arguments.GetDouble("context"),
                        arguments.GetDouble("five-hour"),
                        arguments.GetDouble("weekly"),
                        arguments.Get("source") ?? "external-hook",
                        cancellationToken)
                    .ConfigureAwait(false);
                PrintUsage(snapshot);
                return 0;
            }
            case "list":
            case "":
            {
                var snapshots = await runtime.Store.LoadUsageAsync(cancellationToken).ConfigureAwait(false);
                if (arguments.Has("json"))
                {
                    PrintJson(snapshots);
                }
                else
                {
                    foreach (var snapshot in snapshots.OrderBy(item => item.AgentId))
                    {
                        PrintUsage(snapshot);
                    }
                }

                return 0;
            }
            default:
                return Unknown("usage " + arguments.Subcommand);
        }
    }

    private static async Task<int> EventsAsync(
        FknrtdRuntime runtime,
        CliArguments arguments,
        CancellationToken cancellationToken)
    {
        var events = (await runtime.Store.LoadEventsAsync(arguments.GetInt("limit") ?? 50, cancellationToken)
                .ConfigureAwait(false))
            .OrderByDescending(item => item.Timestamp)
            .ToArray();
        if (arguments.Has("json"))
        {
            PrintJson(events);
        }
        else
        {
            foreach (var item in events)
            {
                Console.WriteLine($"{item.Timestamp:O} {item.Severity,-11} {item.Type,-24} {item.Message}");
            }
        }

        return 0;
    }

    private static async Task<int> ConfigCommandAsync(
        FknrtdRuntime runtime,
        CliArguments arguments,
        CancellationToken cancellationToken)
    {
        switch (arguments.Subcommand)
        {
            case "path":
                Console.WriteLine(runtime.Paths.Config);
                return 0;
            case "show":
            case "":
                PrintJson(await runtime.Store.LoadConfigAsync(cancellationToken).ConfigureAwait(false));
                return 0;
            case "validate":
            {
                var config = await runtime.Store.LoadConfigAsync(cancellationToken).ConfigureAwait(false);
                if (config.MaxParallelAgents <= 0 || config.DashboardRefreshMilliseconds < 100)
                {
                    throw new InvalidDataException(
                        "maxParallelAgents must be positive and dashboardRefreshMilliseconds must be at least 100.");
                }

                var duplicate = config.Agents
                    .GroupBy(agent => agent.Id, StringComparer.OrdinalIgnoreCase)
                    .FirstOrDefault(group => group.Count() > 1);
                if (duplicate is not null)
                {
                    throw new InvalidDataException($"Agent ID '{duplicate.Key}' is configured more than once.");
                }

                foreach (var agent in config.Agents)
                {
                    ValidateAgentDefinition(agent);
                }

                Console.WriteLine($"√ Configuration is valid with {config.Agents.Count} agent(s)");
                return 0;
            }
            default:
                return Unknown("config " + arguments.Subcommand);
        }
    }

    private static async Task<int> IntegrationCommandAsync(
        CliArguments arguments,
        CancellationToken cancellationToken)
    {
        if (arguments.Subcommand is not ("install-claude-statusline" or "claude-statusline"))
        {
            return Unknown("integration " + arguments.Subcommand);
        }

        var path = await new ClaudeIntegrationService().InstallStatusLineAsync(
                arguments.Has("project"), arguments.Has("force"), cancellationToken)
            .ConfigureAwait(false);
        Console.WriteLine($"√ Installed the FKNRTD.CLI statusline in {path}");
        Console.WriteLine("  Restart Claude Code to load it.");
        return 0;
    }

    private static async Task<int> TelemetryCommandAsync(
        FknrtdRuntime runtime,
        CliArguments arguments,
        CancellationToken cancellationToken)
    {
        var subcommand = arguments.Command == "hook" ? "report" : arguments.Subcommand;
        if (subcommand != "report")
        {
            return Unknown("telemetry " + subcommand);
        }

        var agentId = Required(arguments.Get("agent"), "-agent");
        var state = ParseEnum(arguments.Get("state") ?? "running", AgentActivityState.Running);
        var role = ParseEnum(arguments.Get("role") ?? "observer", AgentRole.Observer);
        var progress = arguments.GetInt("progress");
        var basis = arguments.Get("basis");
        if (progress is not null && string.IsNullOrWhiteSpace(basis))
        {
            throw new InvalidOperationException("Measurable -progress requires a truthful -basis description.");
        }

        var existing = (await runtime.Store.LoadAgentRuntimesAsync(cancellationToken).ConfigureAwait(false))
            .FirstOrDefault(item => item.AgentId.Equals(agentId, StringComparison.OrdinalIgnoreCase));
        var snapshot = (existing ?? new AgentRuntimeState { AgentId = agentId }) with
        {
            State = state,
            Role = role,
            TaskId = arguments.Get("task") ?? existing?.TaskId,
            Intent = arguments.Get("intent") ?? existing?.Intent ?? state.ToString(),
            IntentSource = arguments.Get("source") ?? "external-hook",
            WorkingDirectory = arguments.Get("cwd") ?? existing?.WorkingDirectory ?? runtime.Paths.Root,
            Worktree = arguments.Get("worktree") ?? existing?.Worktree ?? runtime.Paths.Root,
            Branch = arguments.Get("branch") ?? existing?.Branch ?? string.Empty,
            ProcessId = arguments.GetInt("pid") ?? existing?.ProcessId,
            ProgressPercent = progress is null ? existing?.ProgressPercent : Math.Clamp(progress.Value, 0, 100),
            ProgressBasis = basis ?? existing?.ProgressBasis,
            LastExitCode = arguments.GetInt("exit-code") ?? existing?.LastExitCode,
            TouchedPaths = arguments.GetMany("path").Count > 0
                ? arguments.GetMany("path").ToList()
                : existing?.TouchedPaths ?? [],
            PlannedPaths = arguments.GetMany("planned-path").Count > 0
                ? arguments.GetMany("planned-path").ToList()
                : existing?.PlannedPaths ?? [],
            UpdatedAt = DateTimeOffset.UtcNow
        };
        await runtime.Store.SaveAgentRuntimeAsync(snapshot, cancellationToken).ConfigureAwait(false);
        if (!arguments.Has("quiet"))
        {
            Console.WriteLine($"√ {agentId} reported {state}: {snapshot.Intent}");
        }

        return 0;
    }

    private static void ValidateAgentDefinition(AgentDefinition agent)
    {
        if (string.IsNullOrWhiteSpace(agent.Id) || string.IsNullOrWhiteSpace(agent.Executable))
        {
            throw new InvalidDataException("Each agent requires non-empty id and executable values.");
        }

        if (agent.Id.Any(character => !char.IsLetterOrDigit(character) && character is not '-' and not '_'))
        {
            throw new InvalidDataException($"Agent ID '{agent.Id}' can contain only letters, numbers, hyphens, and underscores.");
        }

        if (agent.Profiles.Count == 0)
        {
            throw new InvalidDataException($"Agent '{agent.Id}' needs at least one command profile.");
        }
    }

    private static string PreferredAgent(FknrtdConfig config, string preferred, int fallbackIndex)
    {
        var enabled = config.Agents.Where(agent => agent.Enabled).ToArray();
        return enabled.FirstOrDefault(agent => agent.Id.Equals(preferred, StringComparison.OrdinalIgnoreCase))?.Id ??
               enabled.ElementAtOrDefault(fallbackIndex)?.Id ??
               enabled.FirstOrDefault()?.Id ??
               throw new InvalidOperationException("No enabled agents are configured.");
    }

    private static T ParseEnum<T>(string value, T _) where T : struct, Enum =>
        Enum.TryParse<T>(value, ignoreCase: true, out var result)
            ? result
            : throw new ArgumentException($"'{value}' is not a valid {typeof(T).Name} value.");

    private static int? GetDimension(CliArguments arguments, string name)
    {
        var value = arguments.GetInt(name);
        return value is null || value > 0
            ? value
            : throw new ArgumentException($"Option -{name} must be greater than zero.");
    }

    private static string Required(string? value, string label) => !string.IsNullOrWhiteSpace(value)
        ? value
        : throw new ArgumentException($"A {label} is required.");

    private static bool UseColor(CliArguments arguments) =>
        UseColor(arguments, Console.IsOutputRedirected);

    // Colour is suppressed for redirected output so piped text stays clean, but capturing a
    // coloured frame for documentation or a pager then becomes impossible. -color forces it on.
    // Explicit suppression still wins: -no-color and NO_COLOR override -color.
    internal static bool UseColor(CliArguments arguments, bool outputRedirected) =>
        !arguments.Has("no-color") &&
        string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NO_COLOR")) &&
        (arguments.Has("color") || !outputRedirected);

    private static string Blank(string value) => string.IsNullOrWhiteSpace(value) ? "N/A" : value;

    private static string StageIcon(StageState state) => state switch
    {
        StageState.Running => "►",
        StageState.Passed => "√",
        StageState.Failed => "×",
        StageState.Skipped => "◊",
        _ => "○"
    };

    private static void PrintUsage(UsageSnapshot snapshot) => Console.WriteLine(
        $"{snapshot.AgentId}: context {Percent(snapshot.ContextRemainingPercent)}, 5h {Percent(snapshot.FiveHourRemainingPercent)}, 7d {Percent(snapshot.WeeklyRemainingPercent)} [{snapshot.Source}, {snapshot.UpdatedAt:O}]");

    private static string Percent(double? value) => value is null ? "N/A" : $"{value:0}% left";

    private static void PrintJson<T>(T value) =>
        Console.WriteLine(JsonSerializer.Serialize(value, JsonSupport.Options));

    private static int Unknown(string command) => HelpCommand.Unknown(command);

}
