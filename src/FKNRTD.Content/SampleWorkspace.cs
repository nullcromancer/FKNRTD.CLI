using FKNRTD.Domain;

namespace FKNRTD.Help;

/// <summary>
/// An illustrative workspace, used to draw real screens into the offline guide.
/// </summary>
/// <remarks>
/// The guide explained every key and every term in words and never showed anybody a screen. These
/// frames are produced by the same renderer the product runs, from this data, at generation time -
/// so a screen in the guide cannot show a panel that no longer exists or a key that was renamed.
/// It is deliberately small: enough to put every panel in a recognisable state, and no more, since
/// a picture of a busy workspace teaches less than a picture of a legible one.
/// </remarks>
internal static class SampleWorkspace
{
    private static readonly DateTimeOffset At = new(2026, 9, 17, 10, 15, 0, TimeSpan.Zero);

    public static FknrtdConfig Config { get; } = new()
    {
        ProjectName = "aurora-api",
        Mode = WorkspaceMode.Git,
        DefaultBaseRef = "main",
        DefaultVerificationCommands = ["dotnet build", "dotnet test --no-build"],
        Agents = BuiltInAgents.CreateDefaults().ToList()
    };

    public static DashboardSnapshot Snapshot { get; } = new()
    {
        Config = Config,
        CapturedAt = At,
        Git = new GitSnapshot
        {
            RepositoryName = "aurora-api",
            RepositoryRoot = "/src/aurora-api",
            IsRepository = true,
            Branch = "main",
            ChangedFiles = 2,
            Ahead = 1
        },
        Tasks =
        [
            Task("FKN-20260917-101500-a1b2", "Add rate limiting to the login endpoint",
                WorkflowStatus.ReadyToLand, WorkflowStage.ReadyToLand),
            Task("FKN-20260917-094212-c3d4", "Replace the hand-rolled CSV parser",
                WorkflowStatus.Running, WorkflowStage.Implement),
            Task("FKN-20260916-221030-e5f6", "Fix the timezone drift in scheduled reports",
                WorkflowStatus.Failed, WorkflowStage.Verify),
            Task("FKN-20260916-180422-g7h8", "Document the webhook retry policy",
                WorkflowStatus.Landed, WorkflowStage.Land),
            Task("FKN-20260916-143355-i9j0", "Cache the currency conversion table",
                WorkflowStatus.Queued, WorkflowStage.Brief)
        ],
        Agents =
        [
            new AgentRuntimeState
            {
                AgentId = "claude",
                State = AgentActivityState.Reviewing,
                Role = AgentRole.Auditor,
                Intent = "Auditing the rate-limiting change",
                UpdatedAt = At
            },
            new AgentRuntimeState
            {
                AgentId = "codex",
                State = AgentActivityState.Running,
                Role = AgentRole.Implementer,
                Intent = "Rewriting CsvReader.Parse",
                UpdatedAt = At
            }
        ],
        Usage =
        [
            new UsageSnapshot
            {
                AgentId = "claude", ContextRemainingPercent = 62, FiveHourRemainingPercent = 71,
                WeeklyRemainingPercent = 48, Source = "statusline", UpdatedAt = At
            },
            new UsageSnapshot
            {
                AgentId = "codex", FiveHourRemainingPercent = 34, WeeklyRemainingPercent = 80,
                Source = "codex", UpdatedAt = At
            }
        ],
        Messages =
        [
            new AgentMessage
            {
                FromAgentId = "codex",
                ToAgentId = "claude",
                Text = "Ready for audit - the failing snapshot test was stale, not wrong.",
                Delivery = MessageDelivery.Delivered,
                CreatedAt = At.AddMinutes(-4)
            }
        ],
        Events =
        [
            new FknrtdEvent
            {
                Severity = EventSeverity.Success,
                Type = EventTypes.WorkflowReady,
                Message = "Task is verified, audited, and ready to land.",
                Timestamp = At.AddMinutes(-2)
            },
            new FknrtdEvent
            {
                Severity = EventSeverity.Error,
                Type = EventTypes.WorkflowRepair,
                Message = "dotnet test exited 1; handed back for repair.",
                Timestamp = At.AddMinutes(-31)
            }
        ]
    };

    private static WorkflowTask Task(string id, string title, WorkflowStatus status, WorkflowStage stage)
    {
        var task = new WorkflowTask
        {
            Id = id,
            Title = title,
            Brief = "Describe the finished state here.",
            Status = status,
            LeadAgentId = "claude",
            ImplementerAgentId = "codex",
            AuditorAgentId = "claude",
            BaseRef = "main",
            BranchName = "fknrtd/" + id[4..],
            WorktreePath = "/src/aurora-api/.fknrtd/worktrees/" + id[^4..],
            VerificationCommands = ["dotnet build", "dotnet test --no-build"],
            MaxRepairRounds = 1,
            CreatedAt = At.AddHours(-1),
            UpdatedAt = At
        };

        // Every stage before the one it is on has passed, which is what makes the progress bar and
        // the stage strip mean anything in a picture.
        foreach (var record in task.Stages)
        {
            if (record.Stage < stage)
            {
                record.State = StageState.Passed;
            }
            else if (record.Stage == stage)
            {
                record.State = status switch
                {
                    WorkflowStatus.Failed => StageState.Failed,
                    WorkflowStatus.Running => StageState.Running,
                    WorkflowStatus.Queued => StageState.Pending,
                    _ => StageState.Passed
                };
            }
        }

        return task;
    }
}
