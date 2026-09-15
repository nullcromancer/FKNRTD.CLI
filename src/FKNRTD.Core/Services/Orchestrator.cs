using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using FKNRTD.Domain;

namespace FKNRTD.Services;

public sealed class Orchestrator
{
    private readonly StateStore _store;
    private readonly GitService _git;
    private readonly WorktreeService _worktrees;
    private readonly AgentRunner _agents;
    private readonly ProcessRunner _processRunner;

    public Orchestrator(
        StateStore store,
        GitService git,
        WorktreeService worktrees,
        AgentRunner agents,
        ProcessRunner processRunner)
    {
        _store = store;
        _git = git;
        _worktrees = worktrees;
        _agents = agents;
        _processRunner = processRunner;
    }

    public async Task<WorkflowTask> RunAsync(string taskId, CancellationToken cancellationToken = default)
    {
        await using var lease = await _store.AcquireTaskLeaseAsync(taskId, cancellationToken).ConfigureAwait(false);
        var cancelPath = _store.CancelPath(taskId);
        if (File.Exists(cancelPath))
        {
            File.Delete(cancelPath);
        }

        var config = await _store.LoadConfigAsync(cancellationToken).ConfigureAwait(false);
        var task = await _store.LoadTaskAsync(taskId, cancellationToken).ConfigureAwait(false);
        if (task.Status == WorkflowStatus.Landed)
        {
            throw new InvalidOperationException($"Task {task.Id} has already been landed.");
        }

        if (task.Status == WorkflowStatus.Cancelled)
        {
            throw new InvalidOperationException($"Task {task.Id} is cancelled. Use 'fknrtd task retry {task.Id}' to resume it.");
        }

        task.Status = WorkflowStatus.Running;
        task.LastError = null;
        await SaveTaskAsync(task, cancellationToken).ConfigureAwait(false);
        await EventAsync(task, EventSeverity.Information, "workflow.started", "Workflow started.", cancellationToken)
            .ConfigureAwait(false);
        using var workflowCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var cancellationWatcher = WatchCancellationAsync(task.Id, workflowCancellation);
        var workflowToken = workflowCancellation.Token;

        try
        {
            await RunBriefStageAsync(task, workflowToken).ConfigureAwait(false);
            await RunWorktreeStageAsync(task, workflowToken).ConfigureAwait(false);
            await RunPlanStageAsync(config, task, workflowToken).ConfigureAwait(false);

            while (true)
            {
                await RunImplementationStageAsync(config, task, workflowToken).ConfigureAwait(false);
                var verified = await RunVerificationStageAsync(task, workflowToken).ConfigureAwait(false);
                if (!verified)
                {
                    if (await PrepareRepairAsync(task, "Verification failed.", workflowToken).ConfigureAwait(false))
                    {
                        continue;
                    }

                    return await FailAsync(task, "Verification failed and the repair budget is exhausted.", workflowToken)
                        .ConfigureAwait(false);
                }

                var audited = await RunAuditStageAsync(config, task, workflowToken).ConfigureAwait(false);
                if (!audited)
                {
                    if (await PrepareRepairAsync(task, "Independent audit failed.", workflowToken).ConfigureAwait(false))
                    {
                        continue;
                    }

                    return await FailAsync(task, "Audit failed and the repair budget is exhausted.", workflowToken)
                        .ConfigureAwait(false);
                }

                break;
            }

            await EnsureCommittedAsync(config, task, workflowToken).ConfigureAwait(false);
            var ready = BeginStage(task, WorkflowStage.ReadyToLand, null);
            CompleteStage(ready, StageState.Passed, "Verified and audited. Explicit landing is required.");
            task.Status = WorkflowStatus.ReadyToLand;
            task.CompletedAt = DateTimeOffset.UtcNow;
            await SaveTaskAsync(task, workflowToken).ConfigureAwait(false);
            await EventAsync(
                    task,
                    EventSeverity.Success,
                    "workflow.ready",
                    "Task is verified, audited, and ready to land.",
                    workflowToken)
                .ConfigureAwait(false);
            return task;
        }
        catch (OperationCanceledException)
        {
            task.Status = WorkflowStatus.Cancelled;
            task.LastError = "Cancelled by request.";
            task.UpdatedAt = DateTimeOffset.UtcNow;
            await _store.SaveTaskAsync(task, CancellationToken.None).ConfigureAwait(false);
            await EventAsync(
                    task,
                    EventSeverity.Warning,
                    "workflow.cancelled",
                    "Workflow cancelled.",
                    CancellationToken.None)
                .ConfigureAwait(false);
            throw;
        }
        catch (Exception exception)
        {
            await FailAsync(task, exception.Message, CancellationToken.None).ConfigureAwait(false);
            throw;
        }
        finally
        {
            workflowCancellation.Cancel();
            try
            {
                await cancellationWatcher.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Expected when the workflow finishes before a cancellation request.
            }
        }
    }

    public async Task<WorkflowTask> LandAsync(string taskId, CancellationToken cancellationToken = default)
    {
        await using var lease = await _store.AcquireTaskLeaseAsync(taskId, cancellationToken).ConfigureAwait(false);
        var config = await _store.LoadConfigAsync(cancellationToken).ConfigureAwait(false);
        var task = await _store.LoadTaskAsync(taskId, cancellationToken).ConfigureAwait(false);
        if (task.Status != WorkflowStatus.ReadyToLand)
        {
            throw new InvalidOperationException(
                $"Task {task.Id} must be ReadyToLand before it can be merged. Current status: {task.Status}.");
        }

        var stage = BeginStage(task, WorkflowStage.Land, null);
        await SaveTaskAsync(task, cancellationToken).ConfigureAwait(false);
        var result = await _worktrees.LandAsync(task, config.RequireCleanTreeForLanding, cancellationToken)
            .ConfigureAwait(false);
        if (!result.Success)
        {
            CompleteStage(stage, StageState.Failed, Tail(result.StandardError, 500), result.ExitCode);
            return await FailAsync(task, "Git could not merge the task branch.", cancellationToken)
                .ConfigureAwait(false);
        }

        CompleteStage(stage, StageState.Passed, "Branch merged into the primary worktree.", result.ExitCode);
        task.Status = WorkflowStatus.Landed;
        task.CompletedAt = DateTimeOffset.UtcNow;
        await SaveTaskAsync(task, cancellationToken).ConfigureAwait(false);
        await EventAsync(task, EventSeverity.Success, "workflow.landed", "Task branch landed.", cancellationToken)
            .ConfigureAwait(false);
        return task;
    }

    private async Task RunBriefStageAsync(WorkflowTask task, CancellationToken cancellationToken)
    {
        if (IsComplete(task, WorkflowStage.Brief))
        {
            return;
        }

        var stage = BeginStage(task, WorkflowStage.Brief, task.LeadAgentId);
        await SaveTaskAsync(task, cancellationToken).ConfigureAwait(false);
        var path = _store.TaskArtifactPath(task.Id, "brief.md");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var content = $"# {task.Title}\n\n{task.Brief}\n\n## Acceptance evidence\n\n" +
                      (task.VerificationCommands.Count == 0
                          ? "No deterministic verification commands were supplied.\n"
                          : string.Join('\n', task.VerificationCommands.Select(command => $"* `{command}`")) + "\n");
        await File.WriteAllTextAsync(path, content, new UTF8Encoding(false), cancellationToken)
            .ConfigureAwait(false);
        CompleteStage(stage, StageState.Passed, path);
        await SaveTaskAsync(task, cancellationToken).ConfigureAwait(false);
    }

    private async Task RunWorktreeStageAsync(WorkflowTask task, CancellationToken cancellationToken)
    {
        if (IsComplete(task, WorkflowStage.Worktree) && Directory.Exists(task.WorktreePath))
        {
            return;
        }

        var stage = BeginStage(task, WorkflowStage.Worktree, task.ImplementerAgentId);
        await SaveTaskAsync(task, cancellationToken).ConfigureAwait(false);
        try
        {
            var worktree = await _worktrees.CreateAsync(task, cancellationToken).ConfigureAwait(false);
            task.WorktreePath = worktree.Path;
            task.BranchName = worktree.Branch;
            CompleteStage(stage, StageState.Passed, $"{worktree.Branch} at {worktree.Path}");
            await SaveTaskAsync(task, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            CompleteStage(stage, StageState.Failed, exception.Message);
            await SaveTaskAsync(task, cancellationToken).ConfigureAwait(false);
            throw;
        }
    }

    private async Task RunPlanStageAsync(
        FknrtdConfig config,
        WorkflowTask task,
        CancellationToken cancellationToken)
    {
        if (IsComplete(task, WorkflowStage.Plan))
        {
            return;
        }

        var stage = BeginStage(task, WorkflowStage.Plan, task.LeadAgentId);
        await SaveTaskAsync(task, cancellationToken).ConfigureAwait(false);
        var prompt = $"""
            You are the lead developer for FKNRTD.CLI task {task.Id}.
            Work in read-only planning mode. Inspect the repository and create a concise implementation plan for this brief:

            {task.Brief}

            Include exact files likely to change, risks, acceptance criteria, and the verification approach.
            Do not edit files and do not claim implementation is complete.
            """;
        var result = await RunAgentAsync(
                config,
                task.LeadAgentId,
                "plan",
                task,
                AgentRole.Lead,
                prompt,
                WorkflowStage.Plan,
                task.RepairRound,
                cancellationToken)
            .ConfigureAwait(false);
        if (!result.Success)
        {
            CompleteStage(stage, StageState.Failed, $"Lead exited with code {result.ExitCode}.", result.ExitCode);
            await SaveTaskAsync(task, cancellationToken).ConfigureAwait(false);
            throw new InvalidOperationException("The lead agent could not produce a plan.");
        }

        var planPath = _store.TaskArtifactPath(task.Id, "plan.md");
        Directory.CreateDirectory(Path.GetDirectoryName(planPath)!);
        await File.WriteAllTextAsync(
                planPath,
                ExtractFinalText(result.CombinedOutput),
                new UTF8Encoding(false),
                cancellationToken)
            .ConfigureAwait(false);
        CompleteStage(stage, StageState.Passed, planPath, result.ExitCode);
        await SaveTaskAsync(task, cancellationToken).ConfigureAwait(false);
    }

    private async Task RunImplementationStageAsync(
        FknrtdConfig config,
        WorkflowTask task,
        CancellationToken cancellationToken)
    {
        if (IsComplete(task, WorkflowStage.Implement))
        {
            return;
        }

        var stage = BeginStage(task, WorkflowStage.Implement, task.ImplementerAgentId);
        await SaveTaskAsync(task, cancellationToken).ConfigureAwait(false);
        var planPath = _store.TaskArtifactPath(task.Id, "plan.md");
        var repairContext = task.RepairRound == 0
            ? string.Empty
            : $"""

              This is repair round {task.RepairRound}. Correct the failures recorded here:
              {BuildRepairContext(task)}
              """;
        var prompt = $"""
            You are the implementing developer for FKNRTD.CLI task {task.Id}.
            You are already inside an isolated Git worktree on branch {task.BranchName}.

            Brief:
            {task.Brief}

            Lead plan:
            {await File.ReadAllTextAsync(planPath, cancellationToken).ConfigureAwait(false)}
            {repairContext}

            Implement the requested change completely in this worktree. Keep unrelated files untouched.
            You may run focused checks. Do not merge this branch and do not edit outside this worktree.
            Report the changed files and the checks you ran when finished.
            """;
        var result = await RunAgentAsync(
                config,
                task.ImplementerAgentId,
                "implement",
                task,
                AgentRole.Implementer,
                prompt,
                WorkflowStage.Implement,
                task.RepairRound,
                cancellationToken)
            .ConfigureAwait(false);
        var changedPaths = await _git.GetDiffPathsAsync(task.WorktreePath, task.BaseRef, cancellationToken)
            .ConfigureAwait(false);
        await UpdateTouchedPathsAsync(task.ImplementerAgentId, changedPaths, cancellationToken).ConfigureAwait(false);

        if (!result.Success)
        {
            CompleteStage(stage, StageState.Failed, $"Implementer exited with code {result.ExitCode}.", result.ExitCode);
            await SaveTaskAsync(task, cancellationToken).ConfigureAwait(false);
            throw new InvalidOperationException("The implementing agent failed.");
        }

        CompleteStage(
            stage,
            StageState.Passed,
            changedPaths.Count == 0 ? "Agent completed with no uncommitted paths." :
                $"Changed {changedPaths.Count} path(s).",
            result.ExitCode);
        await SaveTaskAsync(task, cancellationToken).ConfigureAwait(false);
    }

    private async Task<bool> RunVerificationStageAsync(WorkflowTask task, CancellationToken cancellationToken)
    {
        if (IsComplete(task, WorkflowStage.Verify))
        {
            return true;
        }

        var stage = BeginStage(task, WorkflowStage.Verify, null);
        task.Quality = new QualitySnapshot();
        await SaveTaskAsync(task, cancellationToken).ConfigureAwait(false);

        if (task.VerificationCommands.Count == 0)
        {
            CompleteStage(stage, StageState.Skipped, "No deterministic verification commands were configured.");
            task.Quality.Tests = StageState.Skipped;
            task.Quality.UpdatedAt = DateTimeOffset.UtcNow;
            await SaveTaskAsync(task, cancellationToken).ConfigureAwait(false);
            return true;
        }

        var allPassed = true;
        for (var index = 0; index < task.VerificationCommands.Count; index++)
        {
            var command = task.VerificationCommands[index];
            var logPath = Path.Combine(
                _store.Paths.Logs,
                StateStore.SafeName(task.Id),
                $"verify-{task.RepairRound}-{index + 1}.log");
            var result = await _processRunner.RunShellAsync(
                    command,
                    task.WorktreePath,
                    logPath,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            task.Quality.Commands.Add(new VerificationResult
            {
                Command = command,
                ExitCode = result.ExitCode,
                Duration = result.Duration,
                OutputTail = Tail(result.StandardOutput + Environment.NewLine + result.StandardError, 2000)
            });
            ApplyQualityResult(task.Quality, command, result);
            allPassed &= result.Success;
            await SaveTaskAsync(task, cancellationToken).ConfigureAwait(false);
        }

        task.Quality.UpdatedAt = DateTimeOffset.UtcNow;
        CompleteStage(
            stage,
            allPassed ? StageState.Passed : StageState.Failed,
            allPassed ? $"{task.VerificationCommands.Count} verification command(s) passed." :
                $"{task.Quality.Commands.Count(result => !result.Passed)} verification command(s) failed.");
        await SaveTaskAsync(task, cancellationToken).ConfigureAwait(false);
        return allPassed;
    }

    private async Task<bool> RunAuditStageAsync(
        FknrtdConfig config,
        WorkflowTask task,
        CancellationToken cancellationToken)
    {
        if (IsComplete(task, WorkflowStage.Audit))
        {
            return true;
        }

        var stage = BeginStage(task, WorkflowStage.Audit, task.AuditorAgentId);
        await SaveTaskAsync(task, cancellationToken).ConfigureAwait(false);
        var prompt = $"""
            You are the independent auditor for FKNRTD.CLI task {task.Id}.
            Review the implementation in this worktree against base ref {task.BaseRef} and this brief:

            {task.Brief}

            Verification results:
            {BuildVerificationSummary(task)}

            Inspect the actual diff and evidence. Do not edit any file.
            Finish with exactly one verdict marker on its own line:
            FKNRTD_VERDICT: PASS
            or
            FKNRTD_VERDICT: FAIL
            A PASS means the implementation satisfies the brief and has no blocking correctness, safety, or maintainability issue.
            """;
        var result = await RunAgentAsync(
                config,
                task.AuditorAgentId,
                "audit",
                task,
                AgentRole.Auditor,
                prompt,
                WorkflowStage.Audit,
                task.RepairRound,
                cancellationToken)
            .ConfigureAwait(false);
        var report = ExtractFinalText(result.CombinedOutput);
        var auditPath = _store.TaskArtifactPath(task.Id, $"audit-{task.RepairRound}.md");
        Directory.CreateDirectory(Path.GetDirectoryName(auditPath)!);
        await File.WriteAllTextAsync(auditPath, report, new UTF8Encoding(false), cancellationToken)
            .ConfigureAwait(false);

        var definition = FindAgent(config, task.AuditorAgentId);
        var profile = ResolveProfile(definition, "audit");
        var successVerdicts = CountVerdictLines(report, profile.SuccessMarker);
        var failureVerdicts = CountVerdictLines(report, profile.FailureMarker);
        var passed = result.Success && successVerdicts == 1 && failureVerdicts == 0;
        CompleteStage(
            stage,
            passed ? StageState.Passed : StageState.Failed,
            passed ? auditPath : $"Audit did not return a passing verdict. See {auditPath}.",
            result.ExitCode);
        await SaveTaskAsync(task, cancellationToken).ConfigureAwait(false);
        return passed;
    }

    private async Task<bool> PrepareRepairAsync(
        WorkflowTask task,
        string reason,
        CancellationToken cancellationToken)
    {
        if (task.RepairRound >= task.MaxRepairRounds)
        {
            return false;
        }

        task.RepairRound++;
        foreach (var stage in task.Stages.Where(stage =>
                     stage.Stage is WorkflowStage.Implement or WorkflowStage.Verify or WorkflowStage.Audit))
        {
            stage.State = StageState.Pending;
            stage.Summary = null;
            stage.ExitCode = null;
            stage.StartedAt = null;
            stage.CompletedAt = null;
        }

        await SaveTaskAsync(task, cancellationToken).ConfigureAwait(false);
        await EventAsync(
                task,
                EventSeverity.Warning,
                "workflow.repair",
                $"{reason} Starting repair round {task.RepairRound} of {task.MaxRepairRounds}.",
                cancellationToken)
            .ConfigureAwait(false);
        return true;
    }

    private async Task EnsureCommittedAsync(
        FknrtdConfig config,
        WorkflowTask task,
        CancellationToken cancellationToken)
    {
        var changed = await _git.GetChangedPathsAsync(task.WorktreePath, cancellationToken).ConfigureAwait(false);
        if (changed.Count == 0)
        {
            return;
        }

        if (!config.AutoCommitAgentChanges)
        {
            throw new InvalidOperationException(
                "The task passed, but the worktree still has uncommitted changes and auto-commit is disabled.");
        }

        var add = await _git.GitAsync(task.WorktreePath, ["add", "-A"], cancellationToken).ConfigureAwait(false);
        if (!add.Success)
        {
            throw new InvalidOperationException("Unable to stage verified agent changes: " + add.StandardError.Trim());
        }

        var message = $"FKNRTD.CLI {task.Id}: {task.Title}";
        var commit = await _git.GitAsync(task.WorktreePath, ["commit", "-m", message], cancellationToken)
            .ConfigureAwait(false);
        if (!commit.Success)
        {
            throw new InvalidOperationException("Unable to commit verified agent changes: " + commit.StandardError.Trim());
        }
    }

    private Task<AgentRunResult> RunAgentAsync(
        FknrtdConfig config,
        string agentId,
        string profile,
        WorkflowTask task,
        AgentRole role,
        string prompt,
        WorkflowStage stage,
        int attempt,
        CancellationToken cancellationToken)
    {
        var definition = FindAgent(config, agentId);
        return _agents.RunAsync(
            definition,
            profile,
            task,
            role,
            prompt,
            task.WorktreePath,
            stage,
            attempt,
            cancellationToken);
    }

    private async Task UpdateTouchedPathsAsync(
        string agentId,
        IReadOnlyList<string> paths,
        CancellationToken cancellationToken)
    {
        var states = await _store.LoadAgentRuntimesAsync(cancellationToken).ConfigureAwait(false);
        var state = states.FirstOrDefault(candidate =>
            candidate.AgentId.Equals(agentId, StringComparison.OrdinalIgnoreCase));
        if (state is null)
        {
            return;
        }

        state.TouchedPaths = paths.ToList();
        state.UpdatedAt = DateTimeOffset.UtcNow;
        await _store.SaveAgentRuntimeAsync(state, cancellationToken).ConfigureAwait(false);
    }

    private async Task WatchCancellationAsync(string taskId, CancellationTokenSource linked)
    {
        while (!linked.IsCancellationRequested)
        {
            if (File.Exists(_store.CancelPath(taskId)))
            {
                linked.Cancel();
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(500), linked.Token).ConfigureAwait(false);
        }
    }

    private async Task<WorkflowTask> FailAsync(
        WorkflowTask task,
        string error,
        CancellationToken cancellationToken)
    {
        task.Status = WorkflowStatus.Failed;
        task.LastError = error;
        task.UpdatedAt = DateTimeOffset.UtcNow;
        await _store.SaveTaskAsync(task, cancellationToken).ConfigureAwait(false);
        await EventAsync(task, EventSeverity.Error, "workflow.failed", error, cancellationToken)
            .ConfigureAwait(false);
        return task;
    }

    private async Task SaveTaskAsync(WorkflowTask task, CancellationToken cancellationToken)
    {
        task.UpdatedAt = DateTimeOffset.UtcNow;
        await _store.SaveTaskAsync(task, cancellationToken).ConfigureAwait(false);
    }

    private Task EventAsync(
        WorkflowTask task,
        EventSeverity severity,
        string type,
        string message,
        CancellationToken cancellationToken) =>
        _store.AppendEventAsync(new FknrtdEvent
        {
            Severity = severity,
            Type = type,
            TaskId = task.Id,
            Message = message
        }, cancellationToken);

    private static StageRecord BeginStage(WorkflowTask task, WorkflowStage stage, string? owner)
    {
        task.CurrentStage = stage;
        var record = task.Stage(stage);
        record.State = StageState.Running;
        record.OwnerAgentId = owner;
        record.Summary = null;
        record.ExitCode = null;
        record.StartedAt = DateTimeOffset.UtcNow;
        record.CompletedAt = null;
        return record;
    }

    private static void CompleteStage(StageRecord stage, StageState state, string summary, int? exitCode = null)
    {
        stage.State = state;
        stage.Summary = summary;
        stage.ExitCode = exitCode;
        stage.CompletedAt = DateTimeOffset.UtcNow;
    }

    private static bool IsComplete(WorkflowTask task, WorkflowStage stage) =>
        task.Stage(stage).State is StageState.Passed or StageState.Skipped;

    private static AgentDefinition FindAgent(FknrtdConfig config, string agentId) =>
        config.Agents.FirstOrDefault(agent => agent.Id.Equals(agentId, StringComparison.OrdinalIgnoreCase))
        ?? throw new InvalidOperationException($"Agent '{agentId}' is not configured.");

    private static AgentCommandProfile ResolveProfile(AgentDefinition definition, string name) =>
        definition.Profiles.TryGetValue(name, out var profile)
            ? profile
            : definition.Profiles.TryGetValue("default", out profile)
                ? profile
                : throw new InvalidOperationException($"Agent '{definition.Id}' has no '{name}' profile.");

    private static int CountVerdictLines(string report, string? marker)
    {
        if (string.IsNullOrWhiteSpace(marker))
        {
            return 0;
        }

        return report.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Count(line => line.Equals(marker, StringComparison.OrdinalIgnoreCase));
    }

    internal static string ExtractFinalText(string output)
    {
        string? last = null;
        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                using var document = JsonDocument.Parse(line);
                var root = document.RootElement;
                if (TryGetString(root, "result", out var result))
                {
                    last = result;
                }

                if (TryGetProperty(root, "item", out var item) &&
                    TryGetString(item, "type", out var itemType) &&
                    itemType.Equals("agent_message", StringComparison.OrdinalIgnoreCase) &&
                    TryGetString(item, "text", out var text))
                {
                    last = text;
                }

                if (TryGetProperty(root, "message", out var message) &&
                    TryGetProperty(message, "content", out var content) &&
                    content.ValueKind == JsonValueKind.Array)
                {
                    foreach (var block in content.EnumerateArray())
                    {
                        if (TryGetString(block, "type", out var type) &&
                            type.Equals("text", StringComparison.OrdinalIgnoreCase) &&
                            TryGetString(block, "text", out text))
                        {
                            last = text;
                        }
                    }
                }
            }
            catch (JsonException)
            {
                if (line.Trim().Length > 0)
                {
                    last = (last is null ? string.Empty : last + Environment.NewLine) + line.Trim();
                }
            }
        }

        return string.IsNullOrWhiteSpace(last) ? output.Trim() : last.Trim();
    }

    private static void ApplyQualityResult(QualitySnapshot quality, string command, CommandResult result)
    {
        var state = result.Success ? StageState.Passed : StageState.Failed;
        if (command.Contains("build", StringComparison.OrdinalIgnoreCase) ||
            command.Contains("compile", StringComparison.OrdinalIgnoreCase))
        {
            quality.Build = MergeQualityState(quality.Build, state);
        }
        else if (command.Contains("lint", StringComparison.OrdinalIgnoreCase) ||
                 command.Contains("format", StringComparison.OrdinalIgnoreCase))
        {
            quality.Lint = MergeQualityState(quality.Lint, state);
        }
        else if (command.Contains("type", StringComparison.OrdinalIgnoreCase))
        {
            quality.Types = MergeQualityState(quality.Types, state);
        }
        else if (command.Contains("security", StringComparison.OrdinalIgnoreCase) ||
                 command.Contains("audit", StringComparison.OrdinalIgnoreCase))
        {
            quality.Security = MergeQualityState(quality.Security, state);
        }
        else
        {
            quality.Tests = MergeQualityState(quality.Tests, state);
        }

        ParseTestCounts(result.StandardOutput + Environment.NewLine + result.StandardError, quality);
    }

    private static StageState MergeQualityState(StageState current, StageState next) =>
        current == StageState.Failed || next == StageState.Failed ? StageState.Failed : next;

    private static void ParseTestCounts(string output, QualitySnapshot quality)
    {
        var passed = Regex.Match(output, @"(?i)\bpassed\s*[:=]?\s*(\d+)");
        var failed = Regex.Match(output, @"(?i)\bfailed\s*[:=]?\s*(\d+)");
        if (passed.Success && int.TryParse(passed.Groups[1].Value, out var passedCount))
        {
            quality.TestsPassed = passedCount;
        }

        if (failed.Success && int.TryParse(failed.Groups[1].Value, out var failedCount))
        {
            quality.TestsFailed = failedCount;
        }
    }

    private static string BuildVerificationSummary(WorkflowTask task) => task.Quality.Commands.Count == 0
        ? "No deterministic commands were configured."
        : string.Join(Environment.NewLine, task.Quality.Commands.Select(result =>
            $"{(result.Passed ? "PASS" : "FAIL")} [{result.ExitCode}] {result.Command}"));

    private string BuildRepairContext(WorkflowTask task)
    {
        var failedCommands = task.Quality.Commands.Where(result => !result.Passed).ToArray();
        if (failedCommands.Length > 0)
        {
            return string.Join(Environment.NewLine + Environment.NewLine, failedCommands.Select(result =>
                $"Command: {result.Command}\nExit code: {result.ExitCode}\nOutput:\n{Tail(result.OutputTail, 3000)}"));
        }

        var previousAudit = task.RepairRound > 0
            ? task.RepairRound - 1
            : 0;
        var auditPath = _store.TaskArtifactPath(task.Id, $"audit-{previousAudit}.md");
        return File.Exists(auditPath) ? File.ReadAllText(auditPath) : task.LastError ?? "Review the task requirements again.";
    }

    private static string Tail(string value, int maxCharacters) =>
        value.Length <= maxCharacters ? value.Trim() : value[^maxCharacters..].Trim();

    private static bool TryGetString(JsonElement element, string name, out string result)
    {
        result = string.Empty;
        if (!TryGetProperty(element, name, out var property) || property.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        result = property.GetString() ?? string.Empty;
        return true;
    }

    private static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (property.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }
        }

        value = default;
        return false;
    }
}
