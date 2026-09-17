using FKNRTD.Domain;

namespace FKNRTD.Services;

public sealed class TaskService
{
    private readonly StateStore _store;
    private readonly GitService _git;

    public TaskService(StateStore store, GitService git)
    {
        _store = store;
        _git = git;
    }

    public async Task<WorkflowTask> CreateAsync(
        string title,
        string brief,
        string leadAgentId,
        string implementerAgentId,
        string auditorAgentId,
        IEnumerable<string> verificationCommands,
        string? baseRef = null,
        int? maxRepairRounds = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            throw new ArgumentException(
                "A task needs a title: it is how you will recognise this task in a list of thirty. A few " +
                "words describing the outcome is enough. 'fknrtd task new' asks for it with an example.",
                nameof(title));
        }

        if (string.IsNullOrWhiteSpace(brief))
        {
            throw new ArgumentException(
                "A task needs a brief: it is the instruction every agent on this task reads, and the " +
                "thing the auditor judges the finished work against. 'fknrtd explain brief' says what " +
                "to put in one.",
                nameof(brief));
        }

        var config = await _store.LoadConfigAsync(cancellationToken).ConfigureAwait(false);
        ValidateAgent(config, leadAgentId, "lead", "plan", requireVerdict: false);
        ValidateAgent(config, implementerAgentId, "implementer", "implement", requireVerdict: false);
        ValidateAgent(config, auditorAgentId, "auditor", "audit", requireVerdict: true);

        var requestedBase = string.IsNullOrWhiteSpace(baseRef) ? config.DefaultBaseRef : baseRef;
        var resolvedBase = config.Mode == WorkspaceMode.Standalone
            ? string.Empty
            : await _git.ResolveBaseBranchAsync(_store.Paths.Root, requestedBase, cancellationToken)
                .ConfigureAwait(false);
        var id = $"FKN-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}"[..28];
        var task = new WorkflowTask
        {
            Id = id,
            Title = title.Trim(),
            Brief = brief.Trim(),
            LeadAgentId = leadAgentId,
            ImplementerAgentId = implementerAgentId,
            AuditorAgentId = auditorAgentId,
            BaseRef = resolvedBase,
            VerificationCommands = verificationCommands
                .Where(command => !string.IsNullOrWhiteSpace(command))
                .Select(command => command.Trim())
                .Distinct(StringComparer.Ordinal)
                .ToList(),
            MaxRepairRounds = Math.Max(0, maxRepairRounds ?? config.DefaultMaxRepairRounds)
        };

        await _store.SaveTaskAsync(task, cancellationToken).ConfigureAwait(false);
        await _store.AppendEventAsync(new FknrtdEvent
        {
            Severity = EventSeverity.Success,
            Type = EventTypes.TaskCreated,
            TaskId = task.Id,
            Message = $"Created task {task.Id}: {task.Title}"
        }, cancellationToken).ConfigureAwait(false);
        return task;
    }

    public async Task RequestCancellationAsync(string taskId, CancellationToken cancellationToken = default)
    {
        var task = await _store.LoadTaskAsync(taskId, cancellationToken).ConfigureAwait(false);
        if (task.Status == WorkflowStatus.Landed)
        {
            throw new InvalidOperationException(
                $"Task {taskId} has already landed, so there is nothing left to cancel — its work is in " +
                "the base branch. Revert the merge with Git if that is what you meant.");
        }

        if (task.Status != WorkflowStatus.Running)
        {
            task.Status = WorkflowStatus.Cancelled;
            task.LastError = "Cancelled before execution.";
            task.UpdatedAt = DateTimeOffset.UtcNow;
            await _store.SaveTaskAsync(task, cancellationToken).ConfigureAwait(false);
            await _store.AppendEventAsync(new FknrtdEvent
            {
                Severity = EventSeverity.Warning,
                Type = EventTypes.TaskCancelled,
                TaskId = taskId,
                Message = $"Cancelled queued task {taskId}."
            }, cancellationToken).ConfigureAwait(false);
            return;
        }

        Directory.CreateDirectory(_store.Paths.Cancels);
        await File.WriteAllTextAsync(
                _store.CancelPath(taskId),
                DateTimeOffset.UtcNow.ToString("O"),
                cancellationToken)
            .ConfigureAwait(false);
        await _store.AppendEventAsync(new FknrtdEvent
        {
            Severity = EventSeverity.Warning,
            Type = EventTypes.TaskCancelRequested,
            TaskId = taskId,
            Message = $"Cancellation requested for {taskId}."
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task ResetFailedStagesAsync(string taskId, CancellationToken cancellationToken = default)
    {
        var task = await _store.LoadTaskAsync(taskId, cancellationToken).ConfigureAwait(false);
        if (task.Status is WorkflowStatus.Landed or WorkflowStatus.Running)
        {
            throw new InvalidOperationException(
                $"Task {taskId} is {task.Status}, and only a failed or cancelled task has stages worth " +
                "resetting. A running task can be stopped first with 'fknrtd task cancel " + taskId + "'.");
        }

        foreach (var stage in task.Stages.Where(stage => stage.State == StageState.Failed))
        {
            stage.State = StageState.Pending;
            stage.Summary = null;
            stage.ExitCode = null;
            stage.StartedAt = null;
            stage.CompletedAt = null;
        }

        task.Status = WorkflowStatus.Queued;
        task.LastError = null;
        task.UpdatedAt = DateTimeOffset.UtcNow;
        await _store.SaveTaskAsync(task, cancellationToken).ConfigureAwait(false);
    }

    private static void ValidateAgent(
        FknrtdConfig config,
        string agentId,
        string role,
        string profileName,
        bool requireVerdict)
    {
        var agent = config.Agents.FirstOrDefault(candidate =>
            candidate.Id.Equals(agentId, StringComparison.OrdinalIgnoreCase));
        if (agent is null)
        {
            throw new InvalidOperationException(
                $"There is no agent called '{agentId}' to act as the {role}. Run 'fknrtd agent list' to " +
                "see what this workspace has, or 'fknrtd agent new' to register another.");
        }

        if (!agent.Enabled)
        {
            throw new InvalidOperationException(
                $"The agent '{agentId}' is disabled, so it cannot be given the {role} role. Re-enable it " +
                $"with 'fknrtd agent enable {agentId}', or choose a different agent.");
        }

        var profile = agent.Profiles.TryGetValue(profileName, out var configured)
            ? configured
            : agent.Profiles.TryGetValue("default", out configured)
                ? configured
                : throw new InvalidOperationException(
                    $"The agent '{agentId}' cannot act as the {role}: it has neither a '{profileName}' " +
                    "profile nor a 'default' one, so there are no arguments to launch it with for that " +
                    "stage. Add one under this agent in .fknrtd/config.json.");
        if (requireVerdict &&
            (string.IsNullOrWhiteSpace(profile.SuccessMarker) || string.IsNullOrWhiteSpace(profile.FailureMarker)))
        {
            throw new InvalidOperationException(
                $"The agent '{agentId}' cannot act as the auditor because its audit profile has no " +
                "successMarker and failureMarker, so it would have no way to signal a pass or a fail — " +
                "and a missing verdict is treated as a failure. Add both to its audit profile in " +
                ".fknrtd/config.json, or pick an agent that already has them.");
        }
    }
}
