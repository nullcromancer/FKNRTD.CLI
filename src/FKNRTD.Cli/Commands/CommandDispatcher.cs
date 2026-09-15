using System.Text.Json;
using FKNRTD.Domain;
using FKNRTD.Services;

namespace FKNRTD.Commands;

internal static class CommandDispatcher
{
    public const string Version = "1.0.0";

    public static async Task<int> ExecuteAsync(
        CliArguments arguments,
        CancellationToken cancellationToken)
    {
        if (arguments.Command is "help" or "-h" or "--help" || arguments.Has("help") || arguments.Has("h"))
        {
            PrintHelp();
            return 0;
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
                $"✓ Prepared a {DescribeMode(config.Mode)} FKNRTD.CLI workspace at {paths.Root}");
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
                $"FKNRTD.CLI is already initialized at {root}. Use -force to replace only its configuration.");
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

        var config = await ProvisionAsync(paths, RequestedMode(arguments), cancellationToken).ConfigureAwait(false);
        Console.WriteLine($"✓ FKNRTD.CLI initialized for {config.ProjectName} ({DescribeMode(config.Mode)})");
        Console.WriteLine($"  Config: {paths.Config}");
        if (backup is not null)
        {
            Console.WriteLine($"  Previous config backup: {backup}");
        }

        Console.WriteLine(config.Mode == WorkspaceMode.Git
            ? $"  Base branch: {config.DefaultBaseRef}"
            : "  Base branch: none. Agents work directly in the workspace.");
        Console.WriteLine($"  Verification: {(config.DefaultVerificationCommands.Count == 0
            ? "not configured"
            : string.Join("; ", config.DefaultVerificationCommands))}");
        Console.WriteLine("  Next: fknrtd doctor");
        return 0;
    }

    /// <summary>
    /// Writes a workspace at <paramref name="paths"/> using the default loading parameters. The
    /// mode is detected when <paramref name="requestedMode"/> is <see langword="null"/>, so a folder
    /// that is not a Git repository is provisioned standalone instead of being rejected.
    /// </summary>
    private static async Task<FknrtdConfig> ProvisionAsync(
        FknrtdPaths paths,
        WorkspaceMode? requestedMode,
        CancellationToken cancellationToken)
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
                Console.WriteLine($"{(check.Passed ? "✓" : check.Required ? "✖" : "△")} {check.Name,-26} {check.Detail}");
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
        Console.WriteLine($"✓ Created {task.Id}: {task.Title}");
        Console.WriteLine($"  {task.LeadAgentId} plans, {task.ImplementerAgentId} implements, {task.AuditorAgentId} audits");
        Console.WriteLine($"  Verification commands: {task.VerificationCommands.Count}");
        if (arguments.Has("run"))
        {
            return await RunTaskAsync(runtime, task.Id, cancellationToken).ConfigureAwait(false);
        }

        Console.WriteLine($"  Run: fknrtd task run {task.Id}");
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

        Console.WriteLine($"{task.Id}  {task.Status}  {task.Title}");
        Console.WriteLine($"Base {task.BaseRef}  Branch {Blank(task.BranchName)}  Worktree {Blank(task.WorktreePath)}");
        Console.WriteLine($"Lead {task.LeadAgentId}  Implementer {task.ImplementerAgentId}  Auditor {task.AuditorAgentId}");
        Console.WriteLine($"Repair {task.RepairRound}/{task.MaxRepairRounds}");
        foreach (var stage in task.Stages)
        {
            Console.WriteLine($"  {StageIcon(stage.State)} {stage.Stage,-14} {stage.State,-9} {stage.Summary}");
        }

        if (!string.IsNullOrWhiteSpace(task.LastError))
        {
            Console.WriteLine("Error: " + task.LastError);
        }

        return task.Status == WorkflowStatus.Failed ? 3 : 0;
    }

    private static async Task<int> RunTaskAsync(
        FknrtdRuntime runtime,
        string id,
        CancellationToken cancellationToken)
    {
        Console.WriteLine($"▶ Running {id}");
        var task = await runtime.Orchestrator.RunAsync(id, cancellationToken).ConfigureAwait(false);
        Console.WriteLine($"{(task.Status == WorkflowStatus.ReadyToLand ? "✓" : "✖")} {task.Id}: {task.Status}");
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
        Console.WriteLine($"⏸ Cancellation requested for {id}");
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
            throw new InvalidOperationException("Landing requires the explicit option -confirm LAND.");
        }

        var task = await runtime.Orchestrator.LandAsync(id, cancellationToken).ConfigureAwait(false);
        Console.WriteLine($"✓ Landed {task.Id} on {task.BaseRef}");
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
            throw new InvalidOperationException("Worktree cleanup requires the explicit option -confirm REMOVE.");
        }

        var config = await runtime.Store.LoadConfigAsync(cancellationToken).ConfigureAwait(false);
        if (config.Mode == WorkspaceMode.Standalone)
        {
            Console.WriteLine($"✓ Nothing to remove for {id}. A standalone workspace has no worktree or branch.");
            return 0;
        }

        await runtime.Worktrees
            .RemoveAsync(task, arguments.Has("force"), config.Mode, cancellationToken)
            .ConfigureAwait(false);
        Console.WriteLine(task.Status == WorkflowStatus.Landed
            ? $"✓ Removed the worktree and landed task branch for {id}. The task record was retained."
            : $"✓ Removed the worktree for {id}. The task record and Git branch were retained.");
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
        foreach (var agent in config.Agents)
        {
            Console.WriteLine($"{agent.Id,-12} {(agent.Enabled ? "yes" : "no"),-8} {(ExecutableLocator.Find(agent.Executable) is not null ? "yes" : "no"),-6} {agent.Kind,-10} {agent.Executable}");
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
        Console.WriteLine($"✓ Added {agent.DisplayName} using {agent.Executable}");
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
        Console.WriteLine($"✓ Agent {id} {(enabled ? "enabled" : "disabled")}");
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
        Console.WriteLine($"✓ Removed agent {id} from the configuration");
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
                Console.WriteLine($"✓ Message {message.Id} delivered");
                return 0;
            }
            case "ack":
            case "acknowledge":
                await runtime.Messages.AcknowledgeAsync(
                        Required(arguments.Get("id") ?? arguments.Positional(2), "message ID"), cancellationToken)
                    .ConfigureAwait(false);
                Console.WriteLine("✓ Message acknowledged");
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
                Console.WriteLine($"✓ Claim {claim.Id} registered until {claim.ExpiresAt:O}");
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
                Console.WriteLine($"✓ Claim renewed until {claim.ExpiresAt:O}");
                return 0;
            }
            case "release":
                runtime.Claims.Release(Required(arguments.Get("id") ?? arguments.Positional(2), "claim ID"));
                Console.WriteLine("✓ Claim released");
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
                        Console.WriteLine($"{(conflict.Kind == ConflictKind.Collision ? "✖" : "△")} {conflict.Kind} {conflict.Summary}: {string.Join(", ", conflict.Paths)}");
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

                Console.WriteLine($"✓ Configuration is valid with {config.Agents.Count} agent(s)");
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
        Console.WriteLine($"✓ Installed the FKNRTD.CLI statusline in {path}");
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
            Console.WriteLine($"✓ {agentId} reported {state}: {snapshot.Intent}");
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
        StageState.Running => "▶",
        StageState.Passed => "✓",
        StageState.Failed => "✖",
        StageState.Skipped => "◇",
        _ => "○"
    };

    private static void PrintUsage(UsageSnapshot snapshot) => Console.WriteLine(
        $"{snapshot.AgentId}: context {Percent(snapshot.ContextRemainingPercent)}, 5h {Percent(snapshot.FiveHourRemainingPercent)}, 7d {Percent(snapshot.WeeklyRemainingPercent)} [{snapshot.Source}, {snapshot.UpdatedAt:O}]");

    private static string Percent(double? value) => value is null ? "N/A" : $"{value:0}% left";

    private static void PrintJson<T>(T value) =>
        Console.WriteLine(JsonSerializer.Serialize(value, JsonSupport.Options));

    private static int Unknown(string command)
    {
        Console.Error.WriteLine($"Unknown FKNRTD.CLI command: {command}");
        Console.Error.WriteLine("Run 'fknrtd help' for usage.");
        return 2;
    }

    private static void PrintHelp()
    {
        Console.WriteLine("""
            FKNRTD COMMAND CENTER
            Coordinate Claude, Codex, and any command-line coding agent from one C# console.

            Start
              fknrtd                        Open the current folder, initializing it if needed
              fknrtd init [path] [-standalone] [-git]
              fknrtd doctor
              fknrtd dashboard [-once] [-width <cols>] [-height <rows>]
              fknrtd status [-json] [-width <cols>] [-height <rows>]

            Tasks
              fknrtd task create "Title" -brief "What to build" -verify "dotnet test" [-run]
              fknrtd task list | show <id> | run <id> | retry <id> | cancel <id>
              fknrtd task land <id> -confirm LAND
              fknrtd task cleanup <id> -confirm REMOVE [-force]

            Agents
              fknrtd agent list
              fknrtd agent add -id gemini -exe gemini -arg=-p -arg="{prompt}"
              fknrtd agent add -file agent.json
              fknrtd agent enable|disable <id>
              fknrtd telemetry report -agent cline -state running -intent "Editing auth" -path src/auth.cs

            Coordination
              fknrtd message send -from codex -to claude -text "Ready for audit" [-task <id>]
              fknrtd message list
              fknrtd claim add -agent codex -path src/auth.cs [-mode write] [-ttl 300]
              fknrtd claim list | renew <id> | release <id>

            Usage and integration
              fknrtd usage refresh codex
              fknrtd usage list
              fknrtd usage set <agent> -context 70 -five-hour 80 -weekly 60
              fknrtd integration install-claude-statusline [-project] [-force]

            Common options
              -root <path>    Select an FKNRTD.CLI workspace
              -standalone     Initialize without Git, for projects that will never be versioned
              -git            Require a Git repository and fail if there is none
              -json           Emit machine-readable JSON where supported
              -no-color       Disable ANSI color
              -color          Force ANSI color even when output is redirected

            Workflow
              brief → isolated worktree → lead plan → implementation → deterministic verification
              → independent read-only audit → explicit landing

            In a Git repository each task runs in its own worktree and lands by merge. In a
            standalone workspace there is no worktree, branch or merge: agents work directly in the
            folder, and landing simply records that the verified work is already in place.

            Configuration lives at <root>/.fknrtd/config.json. Agent arguments are arrays, so
            prompts and paths are passed without shell interpolation. Verification commands are trusted
            project configuration and intentionally run through the platform shell.
            """);
    }
}
