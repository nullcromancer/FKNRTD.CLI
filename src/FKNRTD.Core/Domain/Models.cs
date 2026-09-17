namespace FKNRTD.Domain;

public sealed record WorkflowTask
{
    public string Id { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Brief { get; init; } = string.Empty;
    public string LeadAgentId { get; init; } = "claude";
    public string ImplementerAgentId { get; init; } = "codex";
    public string AuditorAgentId { get; init; } = "claude";
    public string BaseRef { get; set; } = string.Empty;
    public string BranchName { get; set; } = string.Empty;
    public string WorktreePath { get; set; } = string.Empty;
    public List<string> VerificationCommands { get; init; } = [];
    public int MaxRepairRounds { get; init; } = 1;
    public int RepairRound { get; set; }
    public WorkflowStatus Status { get; set; } = WorkflowStatus.Queued;
    public WorkflowStage CurrentStage { get; set; } = WorkflowStage.Brief;
    public List<StageRecord> Stages { get; init; } = WorkflowStages.Create();
    public QualitySnapshot Quality { get; set; } = new();
    public string? LastError { get; set; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedAt { get; set; }

    public StageRecord Stage(WorkflowStage stage)
    {
        var record = Stages.FirstOrDefault(item => item.Stage == stage);
        if (record is not null)
        {
            return record;
        }

        record = new StageRecord { Stage = stage };
        Stages.Add(record);
        return record;
    }
}

public static class WorkflowStages
{
    public static List<StageRecord> Create() =>
    [
        new() { Stage = WorkflowStage.Brief },
        new() { Stage = WorkflowStage.Worktree },
        new() { Stage = WorkflowStage.Plan },
        new() { Stage = WorkflowStage.Implement },
        new() { Stage = WorkflowStage.Verify },
        new() { Stage = WorkflowStage.Audit },
        new() { Stage = WorkflowStage.ReadyToLand },
        new() { Stage = WorkflowStage.Land }
    ];
}

public sealed record StageRecord
{
    public WorkflowStage Stage { get; init; }
    public StageState State { get; set; } = StageState.Pending;
    public string? OwnerAgentId { get; set; }
    public string? Summary { get; set; }
    public int? ExitCode { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
}

public sealed record AgentRuntimeState
{
    public string AgentId { get; init; } = string.Empty;
    public AgentActivityState State { get; set; } = AgentActivityState.Unknown;
    public AgentRole Role { get; set; } = AgentRole.Observer;
    public string? TaskId { get; set; }
    public string Intent { get; set; } = string.Empty;
    public string IntentSource { get; set; } = "orchestrator";
    public string WorkingDirectory { get; set; } = string.Empty;
    public string Branch { get; set; } = string.Empty;
    public string Worktree { get; set; } = string.Empty;
    public int? ProcessId { get; set; }
    public int? ProgressPercent { get; set; }
    public string? ProgressBasis { get; set; }
    public int? LastExitCode { get; set; }
    public List<string> TouchedPaths { get; set; } = [];
    public List<string> PlannedPaths { get; set; } = [];
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed record FileClaim
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string AgentId { get; init; } = string.Empty;
    public string? TaskId { get; init; }
    public ClaimMode Mode { get; init; } = ClaimMode.Write;
    public string WorktreePath { get; init; } = string.Empty;
    public List<string> Paths { get; init; } = [];
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset ExpiresAt { get; set; } = DateTimeOffset.UtcNow.AddMinutes(5);
}

public sealed record ConflictRecord
{
    public ConflictKind Kind { get; init; }
    public int RiskScore { get; init; }
    public string Summary { get; init; } = string.Empty;
    public List<string> AgentIds { get; init; } = [];
    public List<string> Paths { get; init; } = [];
}

public sealed record AgentMessage
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string FromAgentId { get; init; } = string.Empty;
    public string ToAgentId { get; init; } = string.Empty;
    public string? TaskId { get; init; }
    public string Text { get; init; } = string.Empty;
    public MessageDelivery Delivery { get; set; } = MessageDelivery.Delivered;
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? AcknowledgedAt { get; set; }
}

public sealed record QualitySnapshot
{
    public StageState Build { get; set; } = StageState.Pending;
    public StageState Tests { get; set; } = StageState.Pending;
    public StageState Lint { get; set; } = StageState.Pending;
    public StageState Types { get; set; } = StageState.Pending;
    public StageState Security { get; set; } = StageState.Pending;
    public int TestsPassed { get; set; }
    public int TestsFailed { get; set; }
    public int LintIssues { get; set; }
    public List<VerificationResult> Commands { get; set; } = [];
    public DateTimeOffset? UpdatedAt { get; set; }
}

public sealed record VerificationResult
{
    public string Command { get; init; } = string.Empty;
    public int ExitCode { get; init; }
    public TimeSpan Duration { get; init; }
    public string OutputTail { get; init; } = string.Empty;
    public bool Passed => ExitCode == 0;
}

public sealed record UsageSnapshot
{
    public string AgentId { get; init; } = string.Empty;
    public double? ContextRemainingPercent { get; set; }
    public double? FiveHourRemainingPercent { get; set; }
    public double? WeeklyRemainingPercent { get; set; }
    public DateTimeOffset? FiveHourResetsAt { get; set; }
    public DateTimeOffset? WeeklyResetsAt { get; set; }
    public string Source { get; set; } = "unknown";
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed record GitSnapshot
{
    public string RepositoryName { get; init; } = string.Empty;
    public string RepositoryRoot { get; init; } = string.Empty;
    public bool IsRepository { get; init; }
    public string Branch { get; init; } = string.Empty;
    public string Remote { get; init; } = string.Empty;
    public int ChangedFiles { get; init; }
    public int Ahead { get; init; }
    public int Behind { get; init; }
    public bool IsClean => ChangedFiles == 0;
}

public sealed record ResourceSnapshot
{
    public double ProcessCpuPercent { get; init; }
    public long WorkingSetBytes { get; init; }
    public int ProcessorCount { get; init; }
    public DateTimeOffset CapturedAt { get; init; } = DateTimeOffset.UtcNow;
}

public sealed record FknrtdEvent
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public EventSeverity Severity { get; init; } = EventSeverity.Information;
    public string Type { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public string? AgentId { get; init; }
    public string? TaskId { get; init; }
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
}

public sealed record CommandResult
{
    public int ExitCode { get; init; }
    public string StandardOutput { get; init; } = string.Empty;
    public string StandardError { get; init; } = string.Empty;
    public TimeSpan Duration { get; init; }
    public bool TimedOut { get; init; }
    public bool StartFailed { get; init; }
    public bool OutputTruncated { get; init; }
    public bool Success => ExitCode == 0 && !TimedOut && !StartFailed;
}

public sealed record AgentRunResult
{
    public int ExitCode { get; init; }
    public string OutputPath { get; init; } = string.Empty;
    public string CombinedOutput { get; init; } = string.Empty;
    public TimeSpan Duration { get; init; }
    public bool Success => ExitCode == 0;
}

public sealed record DashboardSnapshot
{
    public FknrtdConfig Config { get; init; } = new();
    public GitSnapshot Git { get; init; } = new();
    public IReadOnlyList<WorkflowTask> Tasks { get; init; } = [];

    /// <summary>
    /// Task files that are on disk and could not be read. A task is the operator's work rather than
    /// a runtime snapshot that will be rebuilt in a second, so one that will not parse has to be
    /// reported rather than skipped: a pipeline that quietly shows four of five tasks is worse than
    /// one that shows four and says so.
    /// </summary>
    public IReadOnlyList<string> UnreadableTasks { get; init; } = [];
    public IReadOnlyList<AgentRuntimeState> Agents { get; init; } = [];
    public IReadOnlyList<UsageSnapshot> Usage { get; init; } = [];
    public IReadOnlyList<FileClaim> Claims { get; init; } = [];
    public IReadOnlyList<ConflictRecord> Conflicts { get; init; } = [];
    public IReadOnlyList<AgentMessage> Messages { get; init; } = [];
    public IReadOnlyList<FknrtdEvent> Events { get; init; } = [];
    public ResourceSnapshot Resources { get; init; } = new();
    public DateTimeOffset CapturedAt { get; init; } = DateTimeOffset.UtcNow;
}

public sealed record DoctorCheck
{
    public string Name { get; init; } = string.Empty;
    public bool Passed { get; init; }
    public bool Required { get; init; } = true;
    public string Detail { get; init; } = string.Empty;
}
