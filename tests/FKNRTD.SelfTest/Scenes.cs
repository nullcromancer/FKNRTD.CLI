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
        "overview", "empty", "wizard", "wizard-brief", "wizard-review", "wizard-auditor", "message", "land", "land-landed", "remove", "remove-landed",
        "help", "help-search", "inspect", "agents", "agents-empty", "agents-nothing-installed", "agents-remove", "doctor", "welcome", "welcome-standalone", "setup", "setup-no-git", "setup-no-repo", "palette", "palette-search", "logs", "logs-plain", "logs-json", "agent", "events", "events-empty", "coordination", "settings", "settings-reference", "settings-edit", "settings-number", "usage", "usage-missing", "find", "find-search", "diff", "diff-empty", "diff-standalone", "prompts", "standalone", "standalone-inspect", "inspect-missing-agent", "quit-while-running",
        "tasks-unreadable", "tasks-all-unreadable", "wide-glyphs"
    ];

    /// <summary>
    /// Renders an arbitrary snapshot, for a test that needs a workspace the named scenes do not
    /// cover - a standalone one, say, with one task in a particular state.
    /// </summary>
    public static string RenderWith(DashboardSnapshot snapshot, int width, int height)
    {
        var renderer = new DashboardApp(null!, null!, null!, null!, null!, new StateStore(
            WorkspaceLocator.ForRoot(Path.GetTempPath())), null!, null!, null!);
        return renderer.Render(snapshot, width, height, useColor: false);
    }

    public static string Render(string name, int width, int height, bool colour)
    {
        // A standalone workspace has no branch, no worktrees and nothing to merge, and every
        // surface that mentions one has to say something different. It was rendered nowhere except
        // one diff panel, which is why three separate pieces of advice went on telling a
        // standalone operator to clean up a worktree they do not have.
        var snapshot = name switch
        {
            "empty" => EmptySnapshot(),
            "standalone" or "standalone-inspect" => Standalone(),
            _ => Populated()
        };
        var renderer = new DashboardApp(null!, null!, null!, null!, null!, new StateStore(
            WorkspaceLocator.ForRoot(Path.GetTempPath())), null!, null!, null!);
        if (name == "logs")
        {
            // Deliberately without a log on disk: "nothing has been written yet" is one of several
            // different situations and saying which one applies is the point of the panel.
            return renderer.RenderLog(snapshot, width, height, selectedTaskIndex: 2, colour);
        }

        if (name is "logs-plain" or "logs-json")
        {
            // Index 2 is the failed task, which is the state the log view exists to serve. The log
            // view had never been rendered with a log in it, so the whole reading path - the part
            // that turns an agent's JSON stream into sentences - was drawn nowhere.
            // Its own directory, not the shared temp root every other scene borrows: writing a
            // sample log into that root made a real log appear for a test that was checking what an
            // absent one says.
            var root = Path.Combine(Path.GetTempPath(), "fknrtd-scene-logs");
            var store = new StateStore(WorkspaceLocator.ForRoot(root));
            WriteSampleLog(store, snapshot.Tasks[2], name == "logs-json");
            var reader = new DashboardApp(null!, null!, null!, null!, null!, store, null!, null!, null!);
            return reader.RenderLog(snapshot, width, height, selectedTaskIndex: 2, colour);
        }

        if (name is "land" or "remove")
        {
            // The product's own dialog, opened by the key that opens it. These scenes used to hold
            // a copy of its words, which is how the removal dialog went on promising that a landed
            // task's branch is kept after the product had stopped doing that.
            var app = new DashboardApp(null!, null!, null!, null!, null!, new StateStore(
                WorkspaceLocator.ForRoot(Path.GetTempPath())), null!, null!, null!);
            app.HandleKeyAsync(
                    new ConsoleKeyInfo((char)0, name == "land" ? ConsoleKey.G : ConsoleKey.X,
                        false, false, false),
                    snapshot, CancellationToken.None)
                .GetAwaiter().GetResult();
            return app.RenderLive(snapshot, width, height, colour);
        }

        if (name is "land-landed" or "remove-landed")
        {
            // The same two dialogs for a task that has already landed, which is the case whose
            // wording differs and the one nobody had rendered.
            var landed = snapshot with { Tasks = [snapshot.Tasks.First(task => task.Status == WorkflowStatus.Landed)] };
            var app = new DashboardApp(null!, null!, null!, null!, null!, new StateStore(
                WorkspaceLocator.ForRoot(Path.GetTempPath())), null!, null!, null!);
            app.HandleKeyAsync(
                    new ConsoleKeyInfo((char)0, name == "land-landed" ? ConsoleKey.G : ConsoleKey.X,
                        false, false, false),
                    landed, CancellationToken.None)
                .GetAwaiter().GetResult();
            return app.RenderLive(landed, width, height, colour);
        }

        if (name == "wide-glyphs")
        {
            // Every panel, with text that is two columns per character. The renderer measures in
            // terminal columns rather than characters, and the one test that covered that used one
            // snapshot at a handful of sizes - so the sweep, which is where a shearing border would
            // actually show up, walked fifty-one scenes of pure ASCII.
            var wide = snapshot with
            {
                Tasks =
                [
                    snapshot.Tasks[0] with { Title = "ログインのレート制限を追加する" },
                    snapshot.Tasks[1] with { Title = "Emoji 🚀 in a title 🎯 here" },
                    snapshot.Tasks[2] with { Title = "混合 mixed 宽度 widths 🧪 test" },
                    .. snapshot.Tasks.Skip(3)
                ],
                Agents = snapshot.Agents
                    .Select(agent => agent with { Intent = "ファイルを編集中 🛠" })
                    .ToArray(),
                Messages = snapshot.Messages
                    .Select(message => message with { Text = "監査の準備ができました ✓" })
                    .ToArray()
            };
            return renderer.Render(wide, width, height, colour);
        }

        if (name is "tasks-unreadable" or "tasks-all-unreadable")
        {
            // A task file that will not parse. The task used to disappear, and the empty state
            // then reported that the workspace had nothing in it - a wrong answer rather than an
            // unhelpful one.
            var broken = snapshot with
            {
                Tasks = name == "tasks-all-unreadable" ? [] : [snapshot.Tasks[0]],
                UnreadableTasks = ["/src/aurora-api/.fknrtd/tasks/FKN-20260916-221030-e5f6.json"]
            };
            return renderer.Render(broken, width, height, colour);
        }

        if (name == "quit-while-running")
        {
            var app = new DashboardApp(null!, null!, null!, null!, null!, new StateStore(
                WorkspaceLocator.ForRoot(Path.GetTempPath())), null!, null!, null!);
            app.PretendTaskIsRunning(snapshot.Tasks[1].Id);
            app.HandleKeyAsync(new ConsoleKeyInfo((char)0, ConsoleKey.Q, false, false, false),
                snapshot, CancellationToken.None).GetAwaiter().GetResult();
            return app.RenderLive(snapshot, width, height, colour);
        }

        if (name == "inspect-missing-agent")
        {
            // A task whose lead was removed from the roster and whose auditor was disabled, which
            // is what an operator finds after tidying up agents and forgetting what named them.
            var orphan = snapshot.Tasks[0] with
            {
                LeadAgentId = "gemini",
                AuditorAgentId = "codex"
            };
            var config = snapshot.Config with
            {
                Agents = snapshot.Config.Agents
                    .Select(agent => agent.Id == "codex" ? agent with { Enabled = false } : agent)
                    .ToList()
            };
            return renderer.Render(snapshot with { Config = config }, width, height, colour,
                Reference.Task(orphan, config));
        }

        if (name == "standalone-inspect")
        {
            return renderer.Render(snapshot, width, height, colour,
                Reference.Task(snapshot.Tasks[0], snapshot.Config));
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
            case "wizard-review":
            {
                var wizard = TaskWizard.Create(config);
                Type(wizard, "Add rate limiting to the login endpoint");
                Press(wizard, ConsoleKey.Enter);
                Type(wizard, "Requests to POST /login from one IP are limited to 5 a minute.");
                Press(wizard, ConsoleKey.Enter);
                return wizard;
            }
            case "wizard-auditor":
            {
                var wizard = TaskWizard.Create(config);
                Type(wizard, "Add rate limiting to the login endpoint");
                Press(wizard, ConsoleKey.Enter);
                Type(wizard, "Requests to POST /login from one IP are limited to 5 a minute.");
                Press(wizard, ConsoleKey.Enter);
                Press(wizard, ConsoleKey.DownArrow);   // review: "let me look"
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
                return AgentManager.Create(Populated() with { Config = RosterConfig(config) });
            case "agents-nothing-installed":
                // What a first-time operator meets: agents configured, none of them installed.
                // What is on the machine running this suite is not something a test can arrange,
                // so the answer is supplied rather than looked up.
                return new AgentManager(
                    RosterConfig(config).Agents,
                    new Dictionary<string, int>(),
                    RosterConfig(config).Agents.ToDictionary(
                        agent => agent.Executable,
                        _ => false,
                        StringComparer.OrdinalIgnoreCase));
            case "agents-empty":
                return new AgentManager([], new Dictionary<string, int>());
            case "agents-remove":
                return new Confirmation(
                    "REMOVE THIS AGENT",
                    Theme.Red,
                    "gemini - Gemini",
                    "This deletes its command profiles, its arguments and its environment from this " +
                    "workspace's configuration. Nothing else stores them, so they cannot be restored " +
                    "except by configuring the agent again. One task already names it, and would fail " +
                    "on the stage that needed it.",
                    "REMOVE",
                    "agent",
                    "If you only want it out of the task builder, press Esc and disable it with Space " +
                    "instead. That is reversible.");
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
            case "welcome-standalone":
                // The welcome panel's standalone branch, which said the folder is not a Git
                // repository - true only when standalone was forced rather than chosen.
                return Reference.Welcome(config with
                {
                    Mode = WorkspaceMode.Standalone,
                    DefaultBaseRef = string.Empty
                });
            case "welcome":
                return Reference.Welcome(config);
            case "setup-no-git":
                // Git is not installed at all. This is the situation where the operator has the
                // least idea what is happening and the fewest options, so it is the one most worth
                // explaining - and it used to skip the question silently.
                return SetupWizard.Create(new SetupWizard.Detected(
                    "/src/notes", IsRepository: false, GitInstalled: false, string.Empty,
                    [], HasClaude: true));
            case "setup-no-repo":
                // Git is installed; this folder is simply not a repository. Same lack of choice,
                // completely different fix.
                return SetupWizard.Create(new SetupWizard.Detected(
                    "/src/notes", IsRepository: false, GitInstalled: true, string.Empty,
                    [], HasClaude: true));
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
                return new SettingsBrowser(SampleConfig(), "/src/aurora-api/.fknrtd/config.json");
            case "settings-reference":
                return Reference.Settings(SampleConfig(), "/src/aurora-api/.fknrtd/config.json");
            case "settings-edit":
                return SettingsBrowser.Form(SampleConfig(), "requireCleanTreeForLanding")!;
            case "settings-number":
                return SettingsBrowser.Form(SampleConfig(), "maxParallelAgents")!;
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
            default:
                return null;
        }
    }

    /// <summary>How many option rows a rendered frame is showing, by its selection markers.</summary>
    public static int OptionsOnScreen(string frame) =>
        frame.Split((char)10).Count(line => line.Contains((char)0x25b8));

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

    /// <summary>
    /// A roster that exercises every row the agent manager can draw: enabled, disabled, and one
    /// that has no audit markers and so can never be an auditor. The built-in defaults are all
    /// enabled and all able to audit, which would leave two of the three styles unrendered.
    /// </summary>
    private static FknrtdConfig RosterConfig(FknrtdConfig config) => config with
    {
        Agents =
        [
            .. config.Agents,
            new AgentDefinition
            {
                Id = "gemini",
                DisplayName = "Gemini",
                Executable = "gemini",
                Enabled = false,
                Profiles = new Dictionary<string, AgentCommandProfile>(StringComparer.OrdinalIgnoreCase)
                {
                    ["default"] = new() { Arguments = ["-p", "{prompt}"] }
                }
            }
        ]
    };

    /// <summary>
    /// The real conflict detector, so a scene shows the classification the product would actually
    /// produce for those claims rather than one written out by hand beside it.
    /// </summary>
    private static ClaimService Detector() =>
        new(new StateStore(WorkspaceLocator.ForRoot(Path.GetTempPath())));

    /// <summary>
    /// The same workspace without Git: no branch, no worktrees, agents editing the folder itself.
    /// Its tasks are landed and cancelled, which are the two states whose advice was wrong.
    /// </summary>
    private static DashboardSnapshot Standalone()
    {
        var populated = Populated();
        return populated with
        {
            Config = populated.Config with
            {
                Mode = WorkspaceMode.Standalone,
                DefaultBaseRef = string.Empty
            },
            Git = populated.Git with { IsRepository = false, Branch = string.Empty, Ahead = 0 },
            Tasks = populated.Tasks.Select(Restate).ToArray()
        };
    }

    /// <summary>
    /// The same task as a standalone workspace would really have recorded it: no branch, no
    /// separate checkout, and a worktree stage that was skipped rather than passed. Carrying the
    /// Git-mode stage states over would have drawn a tick beside "a branch and an isolated
    /// checkout are created", which is the opposite of what happened.
    /// </summary>
    private static WorkflowTask Restate(WorkflowTask task)
    {
        var restated = task with
        {
            BaseRef = string.Empty,
            BranchName = string.Empty,
            WorktreePath = "/src/aurora-api"
        };

        var worktree = restated.Stage(WorkflowStage.Worktree);
        if (worktree.State == StageState.Passed)
        {
            worktree.State = StageState.Skipped;
            worktree.Summary = "Standalone workspace: agents work in place at /src/aurora-api.";
        }

        return restated;
    }

    /// <summary>
    /// Writes a stage log for a scene: either an agent's real machine-readable stream, or the plain
    /// output a shell verification command produces. Both have to be readable on screen.
    /// </summary>
    private static void WriteSampleLog(StateStore store, WorkflowTask task, bool asJson)
    {
        var path = store.TaskLogPath(task.Id, task.CurrentStage, task.RepairRound);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        // Exactly the shapes the shipped agents emit, including the housekeeping lines that make up
        // most of a real log and say the least.
        var json = new[]
        {
            """{"type":"system","subtype":"init","session_id":"3f9c","tools":["Read","Edit","Bash"]}""",
            """{"type":"assistant","message":{"content":[{"type":"text","text":"Reading Schedule.cs to find where the timezone is applied."}]}}""",
            """{"type":"assistant","message":{"content":[{"type":"tool_use","name":"Read","input":{"file_path":"src/Reports/Schedule.cs"}}]}}""",
            """{"type":"user","message":{"content":[{"type":"tool_result","content":"..."}]}}""",
            """{"type":"assistant","message":{"content":[{"type":"text","text":"ToLocalTime uses the machine zone. The workspace zone should be used instead."}]}}""",
            """{"type":"assistant","message":{"content":[{"type":"tool_use","name":"Edit","input":{"file_path":"src/Reports/Schedule.cs","old_string":"ToLocalTime()"}}]}}""",
            """{"type":"assistant","message":{"content":[{"type":"tool_use","name":"Bash","input":{"command":"dotnet test --no-build"}}]}}""",
            """{"type":"error","message":"ScheduleTests.RespectsWorkspaceZone failed: expected 09:00, got 04:00"}""",
            """{"type":"result","subtype":"success","result":"Changed Schedule.cs to use the workspace zone. One test still fails."}"""
        };

        var plain = new[]
        {
            "  Determining projects to restore...",
            "  All projects are up-to-date for restore.",
            "  aurora-api -> /src/aurora-api/bin/Debug/net10.0/aurora-api.dll",
            "Test run for /src/aurora-api/bin/Debug/net10.0/aurora-api.Tests.dll",
            "  Failed ScheduleTests.RespectsWorkspaceZone [4 ms]",
            "  Error Message:",
            "   Assert.Equal() Failure: Values differ",
            "   Expected: 09:00",
            "   Actual:   04:00",
            "Failed!  - Failed: 1, Passed: 42, Skipped: 0, Total: 43"
        };

        File.WriteAllLines(path, asJson ? json : plain, new System.Text.UTF8Encoding(false));
    }

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

    /// <summary>A workspace with nothing in it, for a test that needs one.</summary>
    public static DashboardSnapshot EmptySnapshotForTests() => EmptySnapshot();

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
                WorkflowStatus.Landed, WorkflowStage.Land),
            // The remaining three statuses, so every marker the renderer can draw is drawn by a
            // scene and therefore checked by the frame and colour-parity invariants.
            Task("FKN-20260916-143355-i9j0", "Cache the currency conversion table",
                WorkflowStatus.Queued, WorkflowStage.Brief),
            Task("FKN-20260916-091807-k1l2", "Split the notification worker",
                WorkflowStatus.Cancelled, WorkflowStage.Implement),
            Task("FKN-20260915-234410-m3n4", "Retire the legacy export endpoint",
                WorkflowStatus.Waiting, WorkflowStage.Verify)
        };

        var claims = new List<FileClaim>
        {
            new()
            {
                Id = "claim-live", AgentId = "codex", TaskId = "FKN-20260917-094212-c3d4",
                Mode = ClaimMode.Write, WorktreePath = "/src/aurora-api/.fknrtd/worktrees/c3d4",
                Paths = ["src/Csv/CsvReader.cs"],
                CreatedAt = captured.AddMinutes(-2), UpdatedAt = captured.AddMinutes(-2),
                ExpiresAt = captured.AddMinutes(3)
            },
            new()
            {
                Id = "claim-stale", AgentId = "claude", TaskId = "FKN-20260916-221030-e5f6",
                Mode = ClaimMode.Write, WorktreePath = "/src/aurora-api/.fknrtd/worktrees/e5f6",
                Paths = ["src/Csv/CsvReader.cs", "src/Csv/Schedule.cs"],
                CreatedAt = captured.AddMinutes(-40), UpdatedAt = captured.AddMinutes(-40),
                ExpiresAt = captured.AddMinutes(-35)
            }
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
            // Two reservations, one of them past its expiry, and the conflicts the real detector
            // makes of them. The coordination panel's whole job is showing an overlap, and it had
            // only ever been rendered with nothing to show.
            Claims = claims,
            Conflicts = Detector().Detect(claims, [], captured),
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
            // Types from EventTypes, not invented ones. These two were "stage.passed" and
            // "stage.failed", which this product has never recorded - so the history screen was
            // teaching a vocabulary that does not exist, which is the exact drift EventTypes was
            // created to stop.
            Events =
            [
                new FknrtdEvent
                {
                    Severity = EventSeverity.Success, Type = EventTypes.WorkflowReady,
                    Message = "Task is verified, audited, and ready to land.",
                    TaskId = "FKN-20260917-101500-a1b2",
                    Timestamp = captured.AddMinutes(-2)
                },
                new FknrtdEvent
                {
                    Severity = EventSeverity.Error, Type = EventTypes.WorkflowRepair,
                    Message = "dotnet test exited 1; handed back for repair.",
                    TaskId = "FKN-20260916-221030-e5f6",
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
