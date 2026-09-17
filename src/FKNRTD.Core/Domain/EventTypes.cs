namespace FKNRTD.Domain;

/// <summary>
/// Every kind of event this product records, in one place.
/// </summary>
/// <remarks>
/// They were string literals scattered across four files, which had two costs: a typo produced a
/// silently unsearchable event, and nothing could state the recorded vocabulary without reading
/// every call site — so the history's own description had drifted twice, claiming per-stage events
/// and agent check-ins that are not recorded at all.
/// </remarks>
public static class EventTypes
{
    /// <summary>A workspace was set up.</summary>
    public const string ProjectInitialized = "project.initialized";

    /// <summary>A task was recorded. Nothing has run yet.</summary>
    public const string TaskCreated = "task.created";

    /// <summary>A queued task was cancelled before it ever started.</summary>
    public const string TaskCancelled = "task.cancelled";

    /// <summary>A running task was asked to stop.</summary>
    public const string TaskCancelRequested = "task.cancel.requested";

    /// <summary>A task began running its pipeline.</summary>
    public const string WorkflowStarted = "workflow.started";

    /// <summary>Verification or the audit failed, and the work went back for another attempt.</summary>
    public const string WorkflowRepair = "workflow.repair";

    /// <summary>A task passed verification and its audit, and is waiting to be landed.</summary>
    public const string WorkflowReady = "workflow.ready";

    /// <summary>A task stopped because a stage failed and no repair budget remained.</summary>
    public const string WorkflowFailed = "workflow.failed";

    /// <summary>A running task stopped on request.</summary>
    public const string WorkflowCancelled = "workflow.cancelled";

    /// <summary>A task was merged into its base branch, or recorded as final in a standalone workspace.</summary>
    public const string WorkflowLanded = "workflow.landed";

    /// <summary>One agent recorded a note for another on the message bus.</summary>
    public const string MessageSent = "message.sent";

    /// <summary>The complete vocabulary, for anything that needs to describe or validate it.</summary>
    public static IReadOnlyList<string> All { get; } =
    [
        ProjectInitialized,
        TaskCreated,
        TaskCancelled,
        TaskCancelRequested,
        WorkflowStarted,
        WorkflowRepair,
        WorkflowReady,
        WorkflowFailed,
        WorkflowCancelled,
        WorkflowLanded,
        MessageSent
    ];
}
