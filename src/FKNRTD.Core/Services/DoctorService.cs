using System.Runtime.InteropServices;
using FKNRTD.Domain;

namespace FKNRTD.Services;

public sealed class DoctorService
{
    private readonly StateStore _store;
    private readonly GitService _git;
    private readonly ProcessRunner _process;

    public DoctorService(StateStore store, GitService git, ProcessRunner process)
    {
        _store = store;
        _git = git;
        _process = process;
    }

    public async Task<IReadOnlyList<DoctorCheck>> RunAsync(CancellationToken cancellationToken = default)
    {
        var checks = new List<DoctorCheck>
        {
            new()
            {
                Name = ".NET runtime",
                Passed = Environment.Version.Major >= 10,
                Detail = $"{RuntimeInformation.FrameworkDescription} on {RuntimeInformation.OSDescription}"
            }
        };

        FknrtdConfig? config = null;
        var configDetail = _store.Paths.Config;
        if (File.Exists(_store.Paths.Config))
        {
            try
            {
                config = await _store.LoadConfigAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
            {
                configDetail += $" ({exception.Message})";
            }
        }

        checks.Add(new DoctorCheck
        {
            Name = "Configuration",
            Passed = config is not null,
            Detail = configDetail
        });

        // A standalone workspace deliberately runs without Git, so the two Git checks below stay
        // informational there instead of failing the diagnostic.
        var mode = config?.Mode ?? WorkspaceMode.Git;
        var gitRequired = mode == WorkspaceMode.Git;
        checks.Add(new DoctorCheck
        {
            Name = "Workspace mode",
            Passed = true,
            Detail = gitRequired ? "Git-backed" : "Standalone (no Git required)"
        });

        var gitExecutable = FindExecutable("git");
        checks.Add(new DoctorCheck
        {
            Name = "Git executable",
            Passed = gitExecutable is not null,
            Required = gitRequired,
            Detail = gitExecutable ?? "Not found"
        });
        var isRepository = false;
        try
        {
            isRepository = await _git.IsRepositoryAsync(_store.Paths.Root, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            // Report failed check below instead of terminating doctor.
        }

        checks.Add(new DoctorCheck
        {
            Name = "Git repository",
            Passed = isRepository || !gitRequired,
            Required = gitRequired,
            // A standalone workspace is either a choice made inside a repository or the only option
            // there was, and those have completely different routes back to isolation. Reporting
            // "not required" for both told an operator nothing about which one they were in.
            Detail = gitRequired
                ? isRepository
                    ? _store.Paths.Root
                    : $"{_store.Paths.Root} is not a Git repository"
                : isRepository
                    ? "Not required here, but this folder is a Git repository — so a Git-backed " +
                      "workspace, with a branch and a checkout per task, is available with " +
                      "'fknrtd init -git -force'"
                    : "Not required in a standalone workspace, and this folder is not a Git " +
                      "repository. 'git init' here, then 'fknrtd init -git -force', would give " +
                      "every task its own branch and checkout"
        });

        var installed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var agent in config?.Agents ?? [])
        {
            var executable = FindExecutable(agent.Executable);
            // "Not found on PATH" states the fault and leaves the reader with it. Every other
            // check that can fail here names what to do next, and this is the one most likely to
            // be a new reader's first failure: the tool installs cleanly and configures two agents
            // whether or not either is present.
            var detail = executable ?? $"'{agent.Executable}' is not on PATH. Install it, or press " +
                "A in the dashboard and press E to point this entry at the executable you have. " +
                $"'fknrtd agent disable {agent.Id}' stops it being offered at all.";
            var launchSucceeded = false;
            if (executable is not null)
            {
                installed.Add(agent.Id);
                var result = await GetVersionAsync(
                        executable,
                        TimeSpan.FromSeconds(Math.Clamp(config!.AgentTimeoutSeconds, 1, 10)),
                        cancellationToken)
                    .ConfigureAwait(false);
                launchSucceeded = !result.StartFailed && !result.TimedOut;
                var version = (result.StandardOutput + " " + result.StandardError).Trim();
                if (version.Length > 0)
                {
                    detail += $" ({(version.Length <= 100 ? version : version[..100])})";
                }
            }

            checks.Add(new DoctorCheck
            {
                Name = $"Agent: {agent.DisplayName}",
                Passed = launchSucceeded || !agent.Enabled,
                Required = agent.Enabled,
                Detail = agent.Enabled ? detail : detail + " [disabled]"
            });
        }

        // A task names an auditor at creation and is refused if that agent cannot return a verdict.
        // Without this check the workspace looks healthy right up until the first task is refused.
        // Note the null-safe access: an unreadable configuration is already reported above, and
        // doctor's entire contract is that it reports rather than throws.
        var auditors = (config?.Agents ?? [])
            .Where(agent => agent.Enabled)
            // Installed, not merely configured. Checking the profile alone put a tick beside "An
            // agent can audit", naming both agents, two lines under both of them being reported
            // missing - a green claim that the workspace could do something it could not do at all.
            .Where(agent => installed.Contains(agent.Id))
            .Where(agent =>
            {
                var profile = agent.Profiles.TryGetValue("audit", out var audit)
                    ? audit
                    : agent.Profiles.GetValueOrDefault("default");
                return profile is not null &&
                       !string.IsNullOrWhiteSpace(profile.SuccessMarker) &&
                       !string.IsNullOrWhiteSpace(profile.FailureMarker);
            })
            .Select(agent => agent.Id)
            .ToArray();
        checks.Add(new DoctorCheck
        {
            Name = "An agent can audit",
            Passed = auditors.Length > 0,
            Detail = auditors.Length > 0
                ? string.Join(", ", auditors)
                : installed.Count == 0
                    ? "No configured agent is installed, so nothing can audit a task — and the " +
                      "auditor is required, so no task can be created either. Install one of the " +
                      "agents listed above, or add one you already have."
                    : "No installed agent has successMarker and failureMarker in its audit profile, " +
                      "so no task can be created — the auditor is required and must be able to " +
                      "return a verdict."
        });

        // Not an error: a workspace can legitimately have none. But it means the audit is the only
        // gate on every new task, and that is worth knowing before the first one rather than after.
        checks.Add(new DoctorCheck
        {
            Name = "Work gets verified",
            Passed = config?.DefaultVerificationCommands.Count > 0,
            Required = false,
            Detail = config?.DefaultVerificationCommands.Count > 0
                ? string.Join("; ", config.DefaultVerificationCommands)
                : "No default verification commands. New tasks start with nothing checking them, " +
                  "leaving the audit as the only gate — and an audit is a judgement, not a " +
                  "measurement. Set defaultVerificationCommands to the build or test command you " +
                  "already trust; the dashboard's S key edits it, and any task can override it."
        });

        var writable = false;
        var probe = Path.Combine(_store.Paths.Runtime, $"write-probe-{Guid.NewGuid():N}.tmp");
        try
        {
            await File.WriteAllTextAsync(probe, "ok", cancellationToken).ConfigureAwait(false);
            File.Delete(probe);
            writable = true;
        }
        catch (IOException)
        {
            writable = false;
        }
        catch (UnauthorizedAccessException)
        {
            writable = false;
        }

        checks.Add(new DoctorCheck
        {
            Name = "State directory writable",
            Passed = writable,
            Detail = _store.Paths.StateRoot
        });
        return checks;
    }

    private async Task<CommandResult> GetVersionAsync(
        string executable,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _process.RunAsync(
                    executable,
                    ["--version"],
                    _store.Paths.Root,
                    cancellationToken: cancellationToken,
                    timeout: timeout)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return new CommandResult
            {
                ExitCode = -1,
                StandardError = $"Unable to launch '{executable}': {exception.Message}",
                StartFailed = true
            };
        }
    }

    private static string? FindExecutable(string executable)
    {
        try
        {
            return ExecutableLocator.Find(executable);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }
}
