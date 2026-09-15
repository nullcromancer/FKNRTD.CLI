namespace FKNRTD.Domain;

public enum WorkspaceMode
{
    Git,
    Standalone
}

public enum AgentActivityState
{
    Unknown,
    Offline,
    Idle,
    Planning,
    Running,
    Reviewing,
    Waiting,
    Blocked,
    Failed,
    Completed
}

public enum AgentRole
{
    Observer,
    Lead,
    Implementer,
    Auditor
}

public enum WorkflowStatus
{
    Queued,
    Running,
    Waiting,
    Failed,
    ReadyToLand,
    Landed,
    Cancelled
}

public enum WorkflowStage
{
    Brief,
    Worktree,
    Plan,
    Implement,
    Verify,
    Audit,
    ReadyToLand,
    Land
}

public enum StageState
{
    Pending,
    Running,
    Passed,
    Failed,
    Skipped
}

public enum ClaimMode
{
    Read,
    Write
}

public enum MessageDelivery
{
    Pending,
    Delivered,
    Acknowledged,
    Failed
}

public enum EventSeverity
{
    Trace,
    Information,
    Success,
    Warning,
    Error,
    Critical
}

public enum PromptDelivery
{
    Argument,
    StandardInput
}

public enum ConflictKind
{
    Safe,
    MergeRisk,
    Collision,
    StaleClaim
}
