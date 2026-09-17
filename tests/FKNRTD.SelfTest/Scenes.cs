using FKNRTD.Dashboard;
using FKNRTD.Domain;
using FKNRTD.Services;

/// <summary>
/// Named dashboard frames, built from one sample workspace. Two jobs: the self-tests assert against
/// them, and a developer can print one to look at a rendering change rather than only assert about
/// it. Because the overlays here are the same objects the dashboard opens, a scene cannot drift away
/// from what the operator actually sees.
/// </summary>
internal static class Scenes
{
    public static readonly string[] Names =
    [
        "overview", "empty", "wizard", "wizard-brief", "wizard-auditor", "message", "land", "remove",
        "help", "help-search", "inspect", "agents", "doctor", "welcome", "setup", "palette", "palette-search", "logs", "agent", "events", "events-empty", "coordination", "settings", "usage", "usage-missing", "find", "find-search", "diff", "diff-empty", "diff-standalone", "prompts"
    ];

    public static string Render(string name, int width, int height, bool colour)
    {
        var snapshot = name == "empty" ? EmptySnapshot() : Populated();
        var renderer = new DashboardApp(null!, null!, null!, null!, null!, new StateStore(
            WorkspaceLocator.ForRoot(Path.GetTempPath())), null!, null!, null!);
        if (name == "logs")
        {
            // Index 2 is the failed task, which is the state the log view exists to serve.
            return renderer.RenderLog(snapshot, width, height, selectedTaskIndex: 2, colour);
        }

        var overlay = Overlay(name, snapshot.Config);
        return overlay is null
            ? renderer.Render(snapshot, width, height, colour)
            : renderer.Render(snapshot, width, height, colour, overlay);
    }

    /// <summary>The overlay a scene opens, already advanced to the step worth looking at.</summary>
    private static IOverlay? Overlay(string name, FknrtdConfig config)
    {
        switch (name)
        {
            case "wizard":
                return TaskWizard.Create(config);
            case "wizard-brief":
            {
                var wizard = TaskWizard.Create(config);
                Type(wizard, "Add rate limiting to the login endpoint");
                Press(wizard, ConsoleKey.Enter);
                Type(wizard, "Requests to POST /login from one IP are limited to 5 a minute.");
                return wizard;
            }
            case "wizard-auditor":
            {
                var wizard = TaskWizard.Create(config);
                Type(wizard, "Add rate limiting to the login endpoint");
                Press(wizard, ConsoleKey.Enter);
                Type(wizard, "Requests to POST /login from one IP are limited to 5 a minute.");
                Press(wizard, ConsoleKey.Enter);
                Press(wizard, ConsoleKey.Enter);
                Press(wizard, ConsoleKey.Enter);
                return wizard;
            }
            case "message":
                return TaskWizard.Message(config);
            case "help":
                return Reference.Help();
            case "help-search":
            {
                var help = Reference.Help();
                Type(help, "audit");
                return help;
            }
            case "inspect":
                return Reference.Task(FailedTask(), config);
            case "agents":
                return Reference.Agents(config);
            case "doctor":
                return Reference.Doctor(
                [
                    new DoctorCheck { Name = "Workspace", Passed = true, Detail = "/src/aurora-api/.fknrtd" },
                    new DoctorCheck { Name = "Git", Passed = true, Detail = "git version 2.46.0" },
                    new DoctorCheck { Name = "Agent claude", Passed = true, Detail = "found on PATH" },
                    new DoctorCheck
                    {
                        Name = "Agent codex", Passed = false,
                        Detail = "'codex' is not on PATH. Install it, or disable the agent."
                    },
                    new DoctorCheck
                    {
                        Name = "Claude statusline", Passed = false, Required = false,
                        Detail = "not installed. Usage figures for Claude will stay blank."
                    }
                ]);
            case "welcome":
                return Reference.Welcome(config);
            case "setup":
                return SetupWizard.Create(SampleDetection());
            case "agent":
                return AgentWizard.Create(config);
            case "events":
                return Reference.Events(Populated().Events, Populated().CapturedAt);
            case "events-empty":
                return Reference.Events([], Populated().CapturedAt);
            case "coordination":
                return Reference.Coordination(Populated());
            case "settings":
                return Reference.Settings(SampleConfig(), "/src/aurora-api/.fknrtd/config.json");
            case "prompts":
                return Reference.Prompts(FailedTask(), config, string.Join((char)10,
                [
                    "1. Read Schedule.cs and find where the zone is applied.",
                    "2. Replace ToLocalTime with the workspace zone.",
                    "3. Add a test that fails before the change."
                ]));
            case "diff":
                return Reference.Diff(FailedTask(), config, SampleDiff(), truncated: false);
            case "diff-empty":
                return Reference.Diff(FailedTask(), config, [], truncated: false);
            case "diff-standalone":
                return Reference.Diff(FailedTask(), config with { Mode = WorkspaceMode.Standalone },
                    SampleDiff(), truncated: false);
            case "find":
                return Picker.Tasks(Populated());
            case "find-search":
            {
                var picker = Picker.Tasks(Populated());
                Type(picker, "failed");
                return picker;
            }
            case "usage":
                return Reference.Usage(Populated(), refreshError: null);
            case "usage-missing":
                return Reference.Usage(EmptySnapshot(), "codex exited 1: not logged in");
            case "palette":
                return new Palette(() => Palette.Build(Populated(), null, running: 0));
            case "palette-search":
            {
                var palette = new Palette(() => Palette.Build(Populated(), Populated().Tasks[1], running: 1));
                Type(palette, "land");
                return palette;
            }
            case "land":
                return new Confirmation(
                    "LAND THIS TASK",
                    Theme.Green,
                    "FKN-20260917-101500-a1b2 — Add rate limiting to the login endpoint",
                    "This merges the branch fknrtd/rate-limiting into main. It is the only action that " +
                    "changes your base branch, and the dashboard cannot undo it afterwards.",
                    "LAND",
                    "land",
                    "The finished diff is in .fknrtd/worktrees/FKN-20260917-101500-a1b2 if you want to " +
                    "read it before you decide.");
            case "remove":
                return new Confirmation(
                    "REMOVE THE WORKTREE",
                    Theme.Amber,
                    "FKN-20260917-101500-a1b2 — Add rate limiting to the login endpoint",
                    "This deletes the directory .fknrtd/worktrees/FKN-20260917-101500-a1b2 and nothing " +
                    "else. The task record, its stage logs and its Git branch are all kept.",
                    "REMOVE",
                    "cleanup");
            default:
                return null;
        }
    }

    public static void Type(IOverlay overlay, string text)
    {
        foreach (var character in text)
        {
            overlay.HandleKey(new ConsoleKeyInfo(character, ConsoleKey.NoName, false, false, false));
        }
    }

    public static OverlayResult Press(IOverlay overlay, ConsoleKey key, bool shift = false, bool alt = false) =>
        overlay.HandleKey(new ConsoleKeyInfo(
            key switch
            {
                ConsoleKey.Enter => '\r',
                ConsoleKey.Backspace => '\b',
                ConsoleKey.Tab => '\t',
                _ => '\0'
            },
            key,
            shift,
            alt,
            control: false));

    /// <summary>What a typical .NET repository looks like to the setup wizard.</summary>
    public static SetupWizard.Detected SampleDetection() => new(
        "/src/aurora-api",
        IsRepository: true,
        GitInstalled: true,
        "main",
        ["dotnet build", "dotnet test --no-build"],
        HasClaude: true);

    public static FknrtdConfig SampleConfig() => new()
    {
        ProjectName = "aurora-api",
        Mode = WorkspaceMode.Git,
        DefaultBaseRef = "main",
        DefaultVerificationCommands = ["dotnet build", "dotnet test --no-build"],
        Agents = BuiltInAgents.CreateDefaults().ToList()
    };

    public static DashboardSnapshot EmptySnapshot() => new()
    {
        Config = SampleConfig(),
        Git = new GitSnapshot
        {
            RepositoryName = "aurora-api",
            RepositoryRoot = "/src/aurora-api",
            IsRepository = true,
            Branch = "main"
        },
        CapturedAt = new DateTimeOffset(2026, 9, 17, 10, 15, 0, TimeSpan.Zero)
    };

    public static DashboardSnapshot PopulatedSnapshot() => Populated();

    private static DashboardSnapshot Populated()
    {
        var captured = new DateTimeOffset(2026, 9, 17, 10, 15, 0, TimeSpan.Zero);
        var tasks = new[]
        {
            Task("FKN-20260917-101500-a1b2", "Add rate limiting to the login endpoint",
                WorkflowStatus.ReadyToLand, WorkflowStage.ReadyToLand),
            Task("FKN-20260917-094212-c3d4", "Replace the hand-rolled CSV parser",
                WorkflowStatus.Running, WorkflowStage.Implement),
            Task("FKN-20260916-221030-e5f6", "Fix the timezone drift in scheduled reports",
                WorkflowStatus.Failed, WorkflowStage.Verify),
            Task("FKN-20260916-180422-g7h8", "Document the webhook retry policy",
                WorkflowStatus.Landed, WorkflowStage.Land)
        };

        return new DashboardSnapshot
        {
            Config = SampleConfig(),
            Git = new GitSnapshot
            {
                RepositoryName = "aurora-api",
                RepositoryRoot = "/src/aurora-api",
                IsRepository = true,
                Branch = "main",
                ChangedFiles = 2,
                Ahead = 1
            },
            Tasks = tasks,
            Agents =
            [
                new AgentRuntimeState
                {
                    AgentId = "claude",
                    State = AgentActivityState.Reviewing,
                    Role = AgentRole.Auditor,
                    Intent = "Auditing the rate-limiting change",
                    UpdatedAt = captured
                },
                new AgentRuntimeState
                {
                    AgentId = "codex",
                    State = AgentActivityState.Running,
                    Role = AgentRole.Implementer,
                    Intent = "Rewriting CsvReader.Parse",
                    UpdatedAt = captured
                }
            ],
            Usage =
            [
                new UsageSnapshot
                {
                    AgentId = "claude", ContextRemainingPercent = 62, FiveHourRemainingPercent = 71,
                    WeeklyRemainingPercent = 48, Source = "statusline", UpdatedAt = captured
                },
                new UsageSnapshot
                {
                    AgentId = "codex", FiveHourRemainingPercent = 34, WeeklyRemainingPercent = 80,
                    Source = "codex", UpdatedAt = captured
                }
            ],
            Messages =
            [
                new AgentMessage
                {
                    FromAgentId = "codex", ToAgentId = "claude", Text = "Ready for audit",
                    Delivery = MessageDelivery.Delivered, CreatedAt = captured.AddMinutes(-4)
                }
            ],
            Events =
            [
                new FknrtdEvent
                {
                    Severity = EventSeverity.Success, Type = "stage.passed",
                    Message = "Verification passed for FKN-20260917-101500-a1b2",
                    Timestamp = captured.AddMinutes(-2)
                },
                new FknrtdEvent
                {
                    Severity = EventSeverity.Error, Type = "stage.failed",
                    Message = "dotnet test exited 1 for FKN-20260916-221030-e5f6",
                    Timestamp = captured.AddMinutes(-31)
                }
            ],
            Resources = new ResourceSnapshot
            {
                ProcessCpuPercent = 3, WorkingSetBytes = 48L * 1024 * 1024, ProcessorCount = 16,
                CapturedAt = captured
            },
            CapturedAt = captured
        };
    }

    /// <summary>A small but realistic diff, in the shape git actually produces.</summary>
    public static IReadOnlyList<string> SampleDiff() =>
    [
        "diff --git a/src/Reports/Schedule.cs b/src/Reports/Schedule.cs",
        "index 4e1a9c2..b7d3f08 100644",
        "--- a/src/Reports/Schedule.cs",
        "+++ b/src/Reports/Schedule.cs",
        "@@ -14,7 +14,7 @@ public sealed class Schedule",
        "     public DateTimeOffset NextRun(DateTimeOffset after)",
        "     {",
        "-        var local = after.ToLocalTime();",
        "+        var local = TimeZoneInfo.ConvertTime(after, _workspaceZone);",
        "         return local.Date.AddDays(1).Add(_timeOfDay);",
        "     }",
        "diff --git a/tests/Reports/ScheduleTests.cs b/tests/Reports/ScheduleTests.cs",
        "@@ -3,6 +3,14 @@ public class ScheduleTests",
        "+    [Fact]",
        "+    public void NextRunUsesTheWorkspaceZone()",
        "+    {",
        "+        Assert.Equal(expected, new Schedule(Utc).NextRun(midnight));",
        "+    }",
        "+"
    ];

    /// <summary>A task mid-pipeline with a real failure on it, for the inspection scene.</summary>
    private static WorkflowTask FailedTask()
    {
        var task = Task("FKN-20260916-221030-e5f6", "Fix the timezone drift in scheduled reports",
            WorkflowStatus.Failed, WorkflowStage.Verify,
            "Scheduled reports render timestamps in the server's local zone instead of the workspace's " +
            "configured zone, so an overnight run is dated a day early for anyone west of UTC. Reports " +
            "should use the workspace zone everywhere, including the filename stamp. Existing stored " +
            "reports must not be rewritten.");
        task.RepairRound = 1;
        return task;
    }

    private static WorkflowTask Task(
        string id,
        string title,
        WorkflowStatus status,
        WorkflowStage stage,
        string brief = "Describe the finished state here.")
    {
        var task = new WorkflowTask
        {
            Id = id,
            Title = title,
            Brief = brief,
            LeadAgentId = "claude",
            ImplementerAgentId = "codex",
            AuditorAgentId = "claude",
            BaseRef = "main",
            BranchName = "fknrtd/" + id.ToLowerInvariant(),
            WorktreePath = ".fknrtd/worktrees/" + id,
            VerificationCommands = ["dotnet build", "dotnet test --no-build"],
            Status = status,
            CurrentStage = stage
        };

        foreach (var record in task.Stages)
        {
            record.State = record.Stage < stage
                ? StageState.Passed
                : record.Stage == stage
                    ? status == WorkflowStatus.Failed ? StageState.Failed : StageState.Running
                    : StageState.Pending;
        }

        if (status == WorkflowStatus.Failed)
        {
            task.LastError = "dotnet test --no-build exited 1: 2 of 418 tests failed.";
        }

        return task;
    }
}

/// <summary>
/// The colour sequences the renderer is allowed to emit. Kept here rather than in the top-level
/// program because a file-scoped statement list cannot hold a compiled pattern.
/// </summary>
internal static class Ansi
{
    /// <summary>Any SGR sequence, for stripping colour before a frame's width is measured.</summary>
    public static readonly System.Text.RegularExpressions.Regex Sequence =
        new("\u001b\\[[0-9;]*m");

    /// <summary>
    /// Reset, bold, and 24-bit foreground or background. Anything else means a colour was assembled
    /// by hand somewhere it should not have been.
    /// </summary>
    public static readonly System.Text.RegularExpressions.Regex WellFormed =
        new(@"^\u001b\[(0|1|38;2;\d{1,3};\d{1,3};\d{1,3}|48;2;\d{1,3};\d{1,3};\d{1,3})m$");
}
