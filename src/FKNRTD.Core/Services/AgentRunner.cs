using FKNRTD.Domain;

namespace FKNRTD.Services;

public sealed class AgentRunner
{
    private readonly StateStore _store;
    private readonly ProcessRunner _processRunner;

    public AgentRunner(StateStore store, ProcessRunner processRunner)
    {
        _store = store;
        _processRunner = processRunner;
    }

    public async Task<AgentRunResult> RunAsync(
        AgentDefinition definition,
        string profileName,
        WorkflowTask task,
        AgentRole role,
        string prompt,
        string workingDirectory,
        WorkflowStage stage,
        int attempt,
        CancellationToken cancellationToken = default)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var cancellationWatcher = WatchCancellationAsync(task.Id, linked, cancellationToken);
        AgentRuntimeState? runtime = null;

        try
        {
            await using var agentLease = await _store.AcquireAgentLeaseAsync(definition.Id, linked.Token)
                .ConfigureAwait(false);
            var executable = ExecutableLocator.Find(definition.Executable)
                ?? throw new FileNotFoundException(
                    $"The {definition.DisplayName} executable '{definition.Executable}' was not found on PATH.");
            var profile = ResolveProfile(definition, profileName);
            var arguments = ExpandArguments(profile, prompt, task, workingDirectory);
            var standardInput = profile.PromptDelivery == PromptDelivery.StandardInput ? prompt : null;
            var logPath = _store.TaskLogPath(task.Id, stage, attempt);
            runtime = new AgentRuntimeState
            {
                AgentId = definition.Id,
                State = role == AgentRole.Auditor ? AgentActivityState.Reviewing :
                    role == AgentRole.Lead ? AgentActivityState.Planning : AgentActivityState.Running,
                Role = role,
                TaskId = task.Id,
                Intent = StageIntent(stage, task.Title),
                WorkingDirectory = workingDirectory,
                Worktree = workingDirectory,
                Branch = task.BranchName,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            await _store.SaveAgentRuntimeAsync(runtime, linked.Token).ConfigureAwait(false);

            var observer = new AgentOutputObserver(_store, runtime);
            var started = DateTimeOffset.UtcNow;
            var result = await _processRunner.RunAsync(
                    executable,
                    arguments,
                    workingDirectory,
                    definition.Environment,
                    standardInput,
                    logPath,
                    observer.ObserveAsync,
                    processId => runtime.ProcessId = processId,
                    linked.Token)
                .ConfigureAwait(false);

            runtime.State = result.Success ? AgentActivityState.Completed : AgentActivityState.Failed;
            runtime.LastExitCode = result.ExitCode;
            runtime.ProcessId = null;
            runtime.Intent = result.Success ? $"Completed {stage}" : $"Failed {stage}";
            runtime.UpdatedAt = DateTimeOffset.UtcNow;
            await _store.SaveAgentRuntimeAsync(runtime, linked.Token).ConfigureAwait(false);

            var combined = result.StandardOutput +
                           (result.StandardError.Length > 0 ? Environment.NewLine + result.StandardError : string.Empty);
            return new AgentRunResult
            {
                ExitCode = result.ExitCode,
                OutputPath = logPath,
                CombinedOutput = combined,
                Duration = DateTimeOffset.UtcNow - started
            };
        }
        catch (OperationCanceledException)
        {
            if (runtime is not null)
            {
                runtime.State = AgentActivityState.Waiting;
                runtime.ProcessId = null;
                runtime.Intent = "Cancelled by coordinator";
                runtime.UpdatedAt = DateTimeOffset.UtcNow;
                await _store.SaveAgentRuntimeAsync(runtime, CancellationToken.None).ConfigureAwait(false);
            }

            throw;
        }
        finally
        {
            linked.Cancel();
            try
            {
                await cancellationWatcher.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Expected when the agent process finishes before a cancellation request.
            }
        }
    }

    private async Task WatchCancellationAsync(
        string taskId,
        CancellationTokenSource linked,
        CancellationToken outerCancellation)
    {
        while (!linked.IsCancellationRequested && !outerCancellation.IsCancellationRequested)
        {
            if (File.Exists(_store.CancelPath(taskId)))
            {
                linked.Cancel();
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(500), linked.Token).ConfigureAwait(false);
        }
    }

    private static AgentCommandProfile ResolveProfile(AgentDefinition definition, string name)
    {
        if (definition.Profiles.TryGetValue(name, out var profile))
        {
            return profile;
        }

        if (definition.Profiles.TryGetValue("default", out profile))
        {
            return profile;
        }

        throw new InvalidOperationException(
            $"Agent '{definition.Id}' does not define the '{name}' or 'default' command profile.");
    }

    private static IReadOnlyList<string> ExpandArguments(
        AgentCommandProfile profile,
        string prompt,
        WorkflowTask task,
        string workingDirectory)
    {
        var sawPrompt = false;
        var arguments = new List<string>();
        foreach (var template in profile.Arguments)
        {
            sawPrompt |= template.Contains("{prompt}", StringComparison.Ordinal);
            arguments.Add(template
                .Replace("{prompt}", prompt, StringComparison.Ordinal)
                .Replace("{taskId}", task.Id, StringComparison.Ordinal)
                .Replace("{workspace}", workingDirectory, StringComparison.Ordinal)
                .Replace("{branch}", task.BranchName, StringComparison.Ordinal));
        }

        if (!sawPrompt && profile.PromptDelivery == PromptDelivery.Argument)
        {
            arguments.Add(prompt);
        }

        return arguments;
    }

    private static string StageIntent(WorkflowStage stage, string title) => stage switch
    {
        WorkflowStage.Plan => $"Planning {title}",
        WorkflowStage.Implement => $"Implementing {title}",
        WorkflowStage.Audit => $"Auditing {title}",
        _ => $"Working on {title}"
    };
}
