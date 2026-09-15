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
            throw new ArgumentException("A task title is required.", nameof(title));
        }

        if (string.IsNullOrWhiteSpace(brief))
        {
            throw new ArgumentException("A task brief is required.", nameof(brief));
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
            Type = "task.created",
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
            throw new InvalidOperationException($"Task {taskId} has already been landed and cannot be cancelled.");
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
                Type = "task.cancelled",
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
            Type = "task.cancel.requested",
            TaskId = taskId,
            Message = $"Cancellation requested for {taskId}."
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task ResetFailedStagesAsync(string taskId, CancellationToken cancellationToken = default)
    {
        var task = await _store.LoadTaskAsync(taskId, cancellationToken).ConfigureAwait(false);
        if (task.Status is WorkflowStatus.Landed or WorkflowStatus.Running)
        {
            throw new InvalidOperationException($"Task {taskId} cannot be reset while it is {task.Status}.");
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
            throw new InvalidOperationException($"The configured {role} agent '{agentId}' does not exist.");
        }

        if (!agent.Enabled)
        {
            throw new InvalidOperationException($"The configured {role} agent '{agentId}' is disabled.");
        }

        var profile = agent.Profiles.TryGetValue(profileName, out var configured)
            ? configured
            : agent.Profiles.TryGetValue("default", out configured)
                ? configured
                : throw new InvalidOperationException(
                    $"The configured {role} agent '{agentId}' has no '{profileName}' or 'default' profile.");
        if (requireVerdict &&
            (string.IsNullOrWhiteSpace(profile.SuccessMarker) || string.IsNullOrWhiteSpace(profile.FailureMarker)))
        {
            throw new InvalidOperationException(
                $"The configured auditor '{agentId}' needs successMarker and failureMarker values.");
        }
    }
}
