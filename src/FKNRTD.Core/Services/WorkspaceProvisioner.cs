using FKNRTD.Domain;

namespace FKNRTD.Services;

/// <summary>Caller choices; null mode or commands accept detection, empty commands disable verification.</summary>
public sealed record WorkspaceProvisioningOptions
{
    public WorkspaceMode? Mode { get; init; }
    public string? BaseRef { get; init; }
    public IReadOnlyList<string>? VerificationCommands { get; init; }
    public bool Force { get; init; }
}

public sealed record WorkspaceProvisioningResult(FknrtdConfig Config, string? BackupPath);

/// <summary>Creates workspace configuration and initial state without a presentation dependency.</summary>
public sealed class WorkspaceProvisioner(FknrtdPaths paths)
{
    /// <summary>Checks replacement policy before a caller collects interactive choices.</summary>
    public void EnsureCanProvision(bool force = false)
    {
        if (File.Exists(paths.Config) && !force)
        {
            throw new InvalidOperationException(
                $"FKNRTD.CLI is already set up at {paths.Root}. Nothing was changed. " +
                "Use -force to replace its configuration, keeping a backup of the old one.");
        }
    }

    public async Task<WorkspaceProvisioningResult> ProvisionAsync(
        WorkspaceProvisioningOptions options, CancellationToken cancellationToken = default)
    {
        EnsureCanProvision(options.Force);
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

        var root = paths.Root;
        var git = new GitService(new ProcessRunner());
        var isRepository = options.Mode != WorkspaceMode.Standalone &&
                           await git.IsRepositoryAsync(root, cancellationToken).ConfigureAwait(false);
        if (options.Mode == WorkspaceMode.Git && !isRepository)
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
            DefaultVerificationCommands = options.VerificationCommands?.ToList() ?? DetectVerificationCommands(root),
            Agents = BuiltInAgents.CreateDefaults().ToList()
        };

        if (mode == WorkspaceMode.Git && !string.IsNullOrEmpty(options.BaseRef))
        {
            config = config with { DefaultBaseRef = options.BaseRef };
        }

        var store = new StateStore(paths);
        await store.InitializeAsync(config, cancellationToken).ConfigureAwait(false);
        await store.AppendEventAsync(new FknrtdEvent
        {
            Severity = EventSeverity.Success,
            Type = EventTypes.ProjectInitialized,
            Message = $"Initialized a {DescribeMode(mode)} FKNRTD.CLI workspace for {config.ProjectName}."
        }, cancellationToken).ConfigureAwait(false);
        return new WorkspaceProvisioningResult(config, backup);
    }

    public static string DescribeMode(WorkspaceMode mode) =>
        mode == WorkspaceMode.Git ? "Git-backed" : "standalone";

    public static List<string> DetectVerificationCommands(string root)
    {
        var hasDotNet = Directory.EnumerateFiles(root, "*.sln", SearchOption.TopDirectoryOnly).Any() ||
                        Directory.EnumerateFiles(root, "*.slnx", SearchOption.TopDirectoryOnly).Any() ||
                        Directory.EnumerateFiles(root, "*.csproj", SearchOption.TopDirectoryOnly).Any();
        return hasDotNet ? ["dotnet build", "dotnet test --no-build"] : [];
    }
}
