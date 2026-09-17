using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Text.Json;
using FKNRTD.Commands;
using FKNRTD.Dashboard;
using FKNRTD.Domain;
using FKNRTD.Help;
using FKNRTD.Portal;
using FKNRTD.Services;
using FKNRTD.Telemetry;

if (args.FirstOrDefault() == "fake-agent")
{
    return await RunFakeAgentAsync(args.Skip(1).ToArray()).ConfigureAwait(false);
}

// A developer affordance: print one named frame so a rendering change can be looked at rather than
// only asserted about. It is also how the documentation's captured frames are produced.
//   dotnet run --project tests/FKNRTD.SelfTest -c Release -- render wizard 110 34
if (args.FirstOrDefault() == "render")
{
    Console.OutputEncoding = new UTF8Encoding(false);
    Console.WriteLine(Scenes.Render(
        args.ElementAtOrDefault(1) ?? "overview",
        int.TryParse(args.ElementAtOrDefault(2), out var sceneWidth) ? sceneWidth : 120,
        int.TryParse(args.ElementAtOrDefault(3), out var sceneHeight) ? sceneHeight : 34,
        colour: args.Contains("-color")));
    return 0;
}

// A developer sweep: every scene at every interesting size, in process. Far wider than the suite
// samples, and too slow to keep in it, but it is what finds a crash the five sampled widths never
// would.  dotnet run --project tests/FKNRTD.SelfTest -c Release -- fuzz
if (args.FirstOrDefault() == "fuzz")
{
    var widths = new[] { 1, 2, 10, 20, 40, 59, 60, 61, 62, 70, 83, 84, 85, 100, 119, 120, 121, 160, 200, 400 };
    var heights = new[] { 1, 2, 5, 10, 19, 20, 21, 24, 30, 40, 80 };
    var problems = new List<string>();
    var checkedRenders = 0;

    foreach (var scene in Scenes.Names)
    {
        foreach (var width in widths)
        {
            foreach (var height in heights)
            {
                checkedRenders++;
                var expectedWidth = Math.Max(60, width);
                var expectedHeight = Math.Max(20, height);
                try
                {
                    var lines = Scenes.Render(scene, width, height, colour: false)
                        .Split([(char)13, (char)10], StringSplitOptions.RemoveEmptyEntries);
                    if (lines.Length != expectedHeight)
                    {
                        problems.Add($"{scene} {width}x{height}: {lines.Length} rows, expected {expectedHeight}");
                    }
                    else if (lines.FirstOrDefault(line => Text.DisplayWidth(line) != expectedWidth) is { } bad)
                    {
                        problems.Add(
                            $"{scene} {width}x{height}: a row is {Text.DisplayWidth(bad)} wide, " +
                            $"expected {expectedWidth}");
                    }
                }
                catch (Exception exception)
                {
                    problems.Add($"{scene} {width}x{height}: {exception.GetType().Name}: {exception.Message}");
                }
            }
        }
    }

    Console.WriteLine($"checked {checkedRenders} renders across {Scenes.Names.Length} scenes");
    foreach (var problem in problems.Take(40))
    {
        Console.WriteLine("  " + problem);
    }

    Console.WriteLine(problems.Count == 0 ? "no failures" : $"FAILURES: {problems.Count}");
    return problems.Count == 0 ? 0 : 1;
}

var tests = new (string Name, Func<Task> Run)[]
{
    ("JSONL state round trip", TestJsonLinesAsync),
    ("Process timeout kills and reports", TestProcessTimeoutAsync),
    ("Post-exit output drain is bounded", TestPostExitOutputDrainAsync),
    ("Bad executable reports start failure", TestBadExecutableAsync),
    ("File lease acquisition is bounded", TestExclusiveFileLeaseAsync),
    ("JSONL tail tolerates concurrent append", TestConcurrentJsonLineTailAsync),
    ("JSONL rotation preserves recent entries", TestJsonLineRotationAsync),
    ("Windows PATHEXT beats extensionless shim", TestExecutableLocatorAsync),
    ("Legacy config receives timeout defaults", TestConfigTimeoutDefaultsAsync),
    ("Doctor renders unlaunchable agent", TestDoctorFailureAsync),
    ("Claude usage parsing", TestClaudeUsageAsync),
    ("Codex rate-limit parsing", TestCodexUsageAsync),
    ("Collision classification", TestConflictDetectionAsync),
    ("Audit verdict ignores echoed prompt", TestAuditVerdictAsync),
    ("Git paths round trip verbatim", TestGitPathRoundTripAsync),
    ("Dashboard frames preserve display width and topology", TestDashboardRendererAsync),
    ("Dashboard CLI dimensions override detection", TestDashboardDimensionsAsync),
    ("Colour forcing beats redirection but not suppression", TestColourForcingAsync),
    ("End-to-end isolated workflow", TestWorkflowAsync),
    ("Standalone workflow runs without Git", TestStandaloneWorkflowAsync),
    ("Bare invocation opens the current folder", TestDefaultInvocationAsync),
    ("Bare invocation in a repository roots at the top level", TestDefaultInvocationInRepositoryAsync),
    ("Glossary explains every marker the dashboard draws", TestGlossaryIsCompleteAsync),
    ("Every guided step carries its own explanation", TestWizardStepsAreExplainedAsync),
    ("Overlay frames preserve display width and topology", TestOverlayFramesAsync),
    ("A refused answer says what was wrong with it", TestWizardValidationExplainsItselfAsync),
    ("Destructive actions take the whole word and nothing else", TestConfirmationRequiresTheWordAsync),
    ("Text field edits and maps the caret through a wrap", TestTextFieldEditingAsync),
    ("An empty workspace tells you what to do", TestEmptyWorkspaceGuidesAsync),
    ("Every documented command is a real command", TestCommandCatalogAsync),
    ("The portal is offline, deterministic and escaped", TestPortalAsync),
    ("A mistyped command names the one you meant", TestMistypedCommandAsync),
    ("Help and explain render every entry they claim", TestHelpSurfacesAsync),
    ("The palette says why an action cannot be run", TestPaletteExplainsRefusalsAsync),
    ("Scrolling back through a log lands on the right lines", TestLogScrollbackAsync),
    ("Overlay review regressions stay fixed", TestOverlayReviewRegressionsAsync),
    ("The diff view keeps a diff readable", TestDiffViewAsync),
    ("Advice is phrased for the surface asking", TestNextStepIsSurfaceAwareAsync),
    ("Doctor reports rather than throws on a broken workspace", TestDoctorSurvivesABrokenWorkspaceAsync),
    ("The prompt preview shows what is actually sent", TestPromptPreviewAsync),
    ("A failing key becomes a message, not an exit", TestAFailingActionDoesNotCrashAsync),
    ("The task-reading commands work end to end", TestTaskReadingCommandsAsync),
    ("The statusline agrees with the dashboard", TestStatusLineAgreesWithTheDashboardAsync),
    ("Every recorded event type is in the vocabulary", TestEventVocabularyAsync),
    ("The standalone overlay host draws a usable frame", TestOverlayHostFrameAsync),
    ("The agent roster lists and changes the roster", TestAgentManagerAsync),
    ("The settings screen can change what it explains", TestSettingsBrowserAsync),
    ("Coordination can clear the reservations it reports", TestCoordinationReleaseAsync),
    ("A landing Git refuses is reported as a refusal", TestRefusedLandingAsync),
    ("An empty list explains itself", TestEmptyListsExplainThemselvesAsync),
    ("Scrolling a log stays inside the log", TestLogScrollStaysInTheFileAsync),
    ("The help screen writes itself out as a page", TestHelpWritesThePageAsync),
    ("What to do next fits the workspace it is in", TestNextStepFitsTheWorkspaceAsync),
    ("A task naming a missing agent says so", TestOrphanedAgentIsFlaggedAsync),
    ("The busy repaint stays inside the dashboard loop", TestBusyRepaintStaysInsideTheLoopAsync),
    ("Nothing on screen tells you to leave the dashboard", TestNothingTellsYouToLeaveAsync),
    ("Quitting asks when work is running", TestQuittingAsksWhenWorkIsRunningAsync),
    ("A stage log reads as sentences, not JSON", TestLogIsReadableAsync),
    ("A panel never names a key that does nothing there", TestPanelsDoNotNameDeadKeysAsync),
    ("Every state the dashboard draws is explained", TestEveryDrawnStateIsExplainedAsync),
    ("The offline guide shows real screens", TestPortalShowsRealScreensAsync),
    ("The agent builder can describe a piped agent", TestAgentBuilderAsync),
    ("Setup explains why there is no choice of mode", TestSetupExplainsWhyThereIsNoChoiceAsync),
    ("A confirmation finishes its sentences at any width", TestConfirmationFinishesItsSentencesAsync),
    ("Doctor says where it got to", TestDoctorReadsWellAsync),
    ("Corrected claims stay corrected", TestCorrectedClaimsStayCorrectedAsync),
    ("The quieter commands work", TestTheQuieterCommandsWorkAsync),
    ("The welcome panel tells the truth", TestWelcomeIsTrueAsync),
    ("Every command the product names exists", TestNamedCommandsExistAsync),
    ("Every key a screen names can be pressed there", TestNamedKeysArePressableAsync),
    ("Every question can explain itself", TestEveryQuestionCanExplainItselfAsync),
    ("No retired claim survives anywhere in the source", TestNoRetiredClaimInAnySourceFileAsync),
    ("An unreadable task file is reported, not hidden", TestUnreadableTasksAreReportedAsync),
    ("An unreadable configuration explains itself", TestUnreadableConfigExplainsItselfAsync),
    ("A missing worktree is not a missing stage", TestMissingWorktreeIsDistinguishedAsync),
    ("A blank task field says what it is", TestBlankTaskFieldsAreExplainedAsync)
};

var failures = new List<string>();
foreach (var test in tests)
{
    try
    {
        await test.Run().ConfigureAwait(false);
        Console.WriteLine($"√ {test.Name}");
    }
    catch (Exception exception)
    {
        failures.Add(test.Name + ": " + exception.Message);
        Console.WriteLine($"× {test.Name}: {exception.Message}");
    }
}

Console.WriteLine($"{tests.Length - failures.Count}/{tests.Length} self-tests passed");
return failures.Count == 0 ? 0 : 1;

static async Task<int> RunFakeAgentAsync(string[] input)
{
    var role = input.FirstOrDefault() ?? string.Empty;
    switch (role)
    {
        case "plan":
            Console.WriteLine("Plan: create feature.txt, then verify it exists.");
            return 0;
        case "implement":
            await File.WriteAllTextAsync("feature.txt", "implemented by FKNRTD.CLI self-test\n", new UTF8Encoding(false))
                .ConfigureAwait(false);
            Console.WriteLine("Implemented feature.txt");
            return 0;
        case "audit":
            Console.WriteLine(input.ElementAtOrDefault(1) ?? string.Empty);
            if (!File.Exists("feature.txt"))
            {
                Console.WriteLine("FKNRTD_VERDICT: FAIL");
                return 1;
            }

            Console.WriteLine("Independent test audit passed.");
            Console.WriteLine("```");
            Console.WriteLine("**FKNRTD_VERDICT: PASS.**");
            Console.WriteLine("```");
            return 0;
        case "hang":
            Console.WriteLine("before-timeout");
            await Console.Out.FlushAsync().ConfigureAwait(false);
            await Task.Delay(TimeSpan.FromMinutes(1)).ConfigureAwait(false);
            return 0;
        case "spawn-pipe-holder":
            Console.WriteLine("before-parent-exit");
            await Console.Out.FlushAsync().ConfigureAwait(false);
            var executable = Environment.ProcessPath
                ?? throw new InvalidOperationException("The self-test process path is unavailable.");
            var startInfo = new ProcessStartInfo
            {
                FileName = executable,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            foreach (var argument in SelfInvocationArguments("fake-agent", "hold-pipes"))
            {
                startInfo.ArgumentList.Add(argument);
            }

            using (var child = Process.Start(startInfo))
            {
                True(child is not null, "Pipe-holder process start");
            }

            return 0;
        case "hold-pipes":
            await Task.Delay(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
            return 0;
        default:
            Console.Error.WriteLine("Unknown fake-agent role: " + role);
            return 2;
    }
}

static async Task TestJsonLinesAsync()
{
    await WithTemporaryDirectoryAsync(async root =>
    {
        var store = new StateStore(WorkspaceLocator.ForRoot(root));
        await store.InitializeAsync(new FknrtdConfig { ProjectName = "state-test" }).ConfigureAwait(false);
        await store.AppendEventAsync(new FknrtdEvent { Type = "one", Message = "first" }).ConfigureAwait(false);
        await store.AppendEventAsync(new FknrtdEvent { Type = "two", Message = "second" }).ConfigureAwait(false);
        var events = await store.LoadEventsAsync().ConfigureAwait(false);
        Equal(2, events.Count, "Event count");
        Equal("two", events[1].Type, "Second event type");
        var physicalLines = await File.ReadAllLinesAsync(store.Paths.Events).ConfigureAwait(false);
        Equal(2, physicalLines.Length, "JSONL physical line count");
    }).ConfigureAwait(false);
}

static Task TestDashboardRendererAsync()
{
    var tasks = Enumerable.Range(0, 12)
        .Select(index => new WorkflowTask
        {
            Id = $"FKN-RENDER-{index:00}",
            Title = index == 10 ? "Selected 任务 🚀" : $"Renderer task {index:00}",
            LeadAgentId = "claude",
            ImplementerAgentId = "codex",
            AuditorAgentId = "claude",
            Status = index == 10 ? WorkflowStatus.Running : WorkflowStatus.Queued
        })
        .ToArray();
    var snapshot = new DashboardSnapshot
    {
        Config = new FknrtdConfig
        {
            ProjectName = "renderer",
            Agents =
            [
                new AgentDefinition { Id = "claude", DisplayName = "Claude", Executable = "claude" },
                new AgentDefinition { Id = "codex", DisplayName = "Codex", Executable = "codex" }
            ]
        },
        Git = new GitSnapshot
        {
            RepositoryName = "测试-repo-😀",
            Branch = "feature/é-render"
        },
        Tasks = tasks,
        CapturedAt = new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero)
    };
    var renderer = new DashboardApp(null!, null!, null!, null!, null!, null!, null!, null!, null!);
    var widths = new[] { 60, 72, 84, 100, 119, 120, 140, 200 };
    var heights = new[] { 20, 32, 48 };

    foreach (var width in widths)
    {
        foreach (var height in heights)
        {
            var frame = renderer.Render(snapshot, width, height, useColor: false);
            var lines = FrameLines(frame);
            Equal(height, lines.Length, $"Frame line count at {width}x{height}");
            True(!frame.Contains('\u001b'), $"Colorless frame ESC at {width}x{height}");
            foreach (var line in lines)
            {
                Equal(width, Text.DisplayWidth(line), $"Frame display width at {width}x{height}");
            }

            Equal("┌", DisplayCell(lines[0], 0), $"Header upper-left at {width}x{height}");
            Equal("┐", DisplayCell(lines[0], width - 1), $"Header upper-right at {width}x{height}");
            True(!frame.Contains("┐┌", StringComparison.Ordinal), $"Doubled upper seam at {width}x{height}");
            True(!frame.Contains("┘└", StringComparison.Ordinal), $"Doubled lower seam at {width}x{height}");
        }
    }

    var narrow = FrameLines(renderer.Render(snapshot, 72, 32, useColor: false));
    var narrowDivider = DividerRow(narrow);
    True(narrowDivider > 5, "Narrow layout divides its body somewhere below the panel title");
    Equal("├", DisplayCell(narrow[narrowDivider], 0), "Narrow joined left border");
    Equal("┤", DisplayCell(narrow[narrowDivider], 71), "Narrow joined right border");

    // The row the upper and lower strips meet on depends on what the lower strip has to show, so
    // the divider is found rather than assumed. Pinning it to a fixed row only pinned the layout's
    // old habit of splitting the body exactly in half whatever was in it.
    static int DividerRow(string[] lines) =>
        Array.FindIndex(lines, 5, line => DisplayCell(line, 0) == "├");

    var medium = FrameLines(renderer.Render(snapshot, 100, 32, useColor: false));
    var mediumDivider = DividerRow(medium);
    True(mediumDivider > 5, "Medium layout divides its body somewhere below the panel titles");
    Equal("┬", DisplayCell(medium[4], 49), "Medium upper junction");
    Equal("┼", DisplayCell(medium[mediumDivider], 49), "Medium center junction");

    var wide = FrameLines(renderer.Render(snapshot, 120, 32, useColor: false));
    var wideDivider = DividerRow(wide);
    True(wideDivider > 5, "Wide layout divides its body somewhere below the panel titles");
    Equal("┬", DisplayCell(wide[4], 35), "Wide first upper junction");
    Equal("┬", DisplayCell(wide[4], 80), "Wide second upper junction");
    Equal("┼", DisplayCell(wide[wideDivider], 35), "Wide first center junction");
    Equal("┼", DisplayCell(wide[wideDivider], 80), "Wide second center junction");

    // And the lower strip is sized by what it holds rather than by half the body: an overview with
    // very little to report below leaves the pipeline more room than one with a lot.
    var busy = snapshot with
    {
        Events = Enumerable.Range(0, 30)
            .Select(index => new FknrtdEvent { Type = "task.created", Message = "event " + index })
            .ToArray()
    };
    var busyDivider = DividerRow(FrameLines(renderer.Render(busy, 120, 32, useColor: false)));
    True(busyDivider < wideDivider,
        $"A fuller lower strip takes more room (quiet divider {wideDivider}, busy {busyDivider})");

    var selected = renderer.Render(snapshot, 60, 20, useColor: false, selectedTaskIndex: 10);
    True(selected.Contains("FKN-RENDER-10", StringComparison.Ordinal), "Scrolled pipeline selected task");
    True(selected.Contains("任务", StringComparison.Ordinal), "Unicode selected task title");
    True(selected.Contains('↑') && selected.Contains('↓'), "Scrolled pipeline overflow indicators");
    True(FrameLines(selected).All(line => Text.DisplayWidth(line) == 60), "Unicode selected frame width");

    Equal(3, Text.DisplayWidth("é😀"), "Combining and emoji display width");
    Equal(2, Text.DisplayWidth("❤️"), "Emoji presentation sequence width");
    Equal("é", Text.Truncate("é", 1), "Combining sequence preservation");
    Equal("…", Text.Truncate("😀x", 2), "Surrogate-safe truncation");
    True(!Text.Truncate("ab😀", 3).EndsWith('\ud83d'), "Truncation has no dangling high surrogate");

    var logRoot = Path.Combine(Path.GetTempPath(), "fknrtd-render-log-" + Guid.NewGuid().ToString("N"));
    try
    {
        var store = new StateStore(WorkspaceLocator.ForRoot(logRoot));
        var logPath = store.TaskLogPath(tasks[10].Id, tasks[10].CurrentStage, tasks[10].RepairRound);
        Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
        using var writer = new FileStream(
            logPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.ReadWrite | FileShare.Delete);
        using (var textWriter = new StreamWriter(writer, new UTF8Encoding(false), leaveOpen: true))
        {
            textWriter.WriteLine("active agent log line");
        }

        var logRenderer = new DashboardApp(null!, null!, null!, null!, null!, store, null!, null!, null!);
        var logFrame = logRenderer.RenderLog(snapshot, 72, 20, selectedTaskIndex: 10);
        True(logFrame.Contains("active agent log line", StringComparison.Ordinal), "Shared active task log rendering");
    }
    finally
    {
        if (Directory.Exists(logRoot))
        {
            Directory.Delete(logRoot, recursive: true);
        }
    }

    return Task.CompletedTask;
}

static Task TestColourForcingAsync()
{
    // UseColor reads NO_COLOR from the ambient environment, so pin it rather than inherit
    // whatever the invoking terminal happens to set.
    var originalNoColor = Environment.GetEnvironmentVariable("NO_COLOR");
    try
    {
        Environment.SetEnvironmentVariable("NO_COLOR", null);

        // Redirected output is colourless by default so piped text stays clean.
        True(!CommandDispatcher.UseColor(new CliArguments(["status"]), outputRedirected: true),
            "Redirected output defaults to no colour");
        True(CommandDispatcher.UseColor(new CliArguments(["status"]), outputRedirected: false),
            "A real console defaults to colour");

        // -color forces colour through redirection, which is what makes a captured frame possible.
        True(CommandDispatcher.UseColor(new CliArguments(["status", "-color"]), outputRedirected: true),
            "-color forces colour through redirection");

        // Explicit suppression still wins over forcing.
        True(!CommandDispatcher.UseColor(new CliArguments(["status", "-color", "-no-color"]), outputRedirected: false),
            "-no-color overrides -color");

        // NO_COLOR is honoured even against an explicit -color.
        Environment.SetEnvironmentVariable("NO_COLOR", "1");
        True(!CommandDispatcher.UseColor(new CliArguments(["status", "-color"]), outputRedirected: false),
            "NO_COLOR overrides -color");
    }
    finally
    {
        Environment.SetEnvironmentVariable("NO_COLOR", originalNoColor);
    }

    return Task.CompletedTask;
}

static async Task TestDashboardDimensionsAsync()
{
    await WithTemporaryDirectoryAsync(async root =>
    {
        var store = new StateStore(WorkspaceLocator.ForRoot(root));
        await store.InitializeAsync(new FknrtdConfig { ProjectName = "dimension-test" }).ConfigureAwait(false);
        var originalOutput = Console.Out;
        using var output = new StringWriter();
        try
        {
            Console.SetOut(output);
            var exitCode = await CommandDispatcher.ExecuteAsync(
                    new CliArguments(["status", "-root", root, "-width", "72", "-height", "20", "-no-color"]),
                    CancellationToken.None)
                .ConfigureAwait(false);
            Equal(0, exitCode, "Dimension override command exit code");
        }
        finally
        {
            Console.SetOut(originalOutput);
        }

        var lines = FrameLines(output.ToString());
        if (lines.Length > 0 && lines[^1].Length == 0)
        {
            lines = lines[..^1];
        }

        Equal(20, lines.Length, "Dimension override frame height");
        True(lines.All(line => Text.DisplayWidth(line) == 72), "Dimension override frame width");
        True(!output.ToString().Contains('\u001b'), "Dimension override colorless output");
    }).ConfigureAwait(false);
}

static string[] FrameLines(string frame) =>
    frame.Split(["\r\n", "\n"], StringSplitOptions.None);

static string DisplayCell(string line, int column)
{
    var cursor = 0;
    foreach (var element in Text.Elements(line))
    {
        var width = Text.DisplayWidth(element);
        if (cursor == column)
        {
            return element;
        }

        if (cursor < column && cursor + width > column)
        {
            return string.Empty;
        }

        cursor += width;
    }

    throw new InvalidOperationException($"Display column {column} is outside line width {cursor}.");
}

static async Task TestProcessTimeoutAsync()
{
    var executable = Environment.ProcessPath
        ?? throw new InvalidOperationException("The self-test process path is unavailable.");
    var result = await new ProcessRunner().RunAsync(
            executable,
            SelfInvocationArguments("fake-agent", "hang"),
            Environment.CurrentDirectory,
            timeout: TimeSpan.FromMilliseconds(300))
        .ConfigureAwait(false);
    True(result.TimedOut, "Timed-out marker");
    True(!result.Success, "Timed-out process failure");
    True(result.StandardOutput.Contains("before-timeout", StringComparison.Ordinal), "Partial timeout output");
    True(result.StandardError.Contains("timed out", StringComparison.OrdinalIgnoreCase), "Timeout detail");
}

static async Task TestBadExecutableAsync()
{
    await WithTemporaryDirectoryAsync(async root =>
    {
        var missing = Path.Combine(root, "definitely-missing-executable");
        var result = await new ProcessRunner().RunAsync(missing, [], root).ConfigureAwait(false);
        True(result.StartFailed, "Start-failure marker");
        True(!result.Success, "Start failure result");
        True(result.StandardError.Contains(missing, StringComparison.Ordinal), "Start failure executable path");
    }).ConfigureAwait(false);
}

static async Task TestPostExitOutputDrainAsync()
{
    var executable = Environment.ProcessPath
        ?? throw new InvalidOperationException("The self-test process path is unavailable.");
    var result = await new ProcessRunner().RunAsync(
            executable,
            SelfInvocationArguments("fake-agent", "spawn-pipe-holder"),
            Environment.CurrentDirectory,
            timeout: TimeSpan.FromMilliseconds(300))
        .ConfigureAwait(false);
    True(result.TimedOut, "Post-exit drain timeout marker");
    True(result.OutputTruncated, "Post-exit truncated-output marker");
    True(!result.Success, "Post-exit drain failure");
    True(result.StandardOutput.Contains("before-parent-exit", StringComparison.Ordinal), "Post-exit partial output");
    True(result.StandardError.Contains("output drain timed out", StringComparison.OrdinalIgnoreCase),
        "Post-exit drain timeout detail");
    True(result.Duration < TimeSpan.FromSeconds(2), "Post-exit drain duration bound");
}

static async Task TestExclusiveFileLeaseAsync()
{
    await WithTemporaryDirectoryAsync(async root =>
    {
        var path = Path.Combine(root, "held.lock");
        var lease = await ExclusiveFileLease.AcquireAsync(path, "lease busy", maximumAttempts: null)
            .ConfigureAwait(false);
        try
        {
            try
            {
                await ExclusiveFileLease.AcquireAsync(
                        path,
                        "lease busy",
                        maximumAttempts: null,
                        acquisitionTimeout: TimeSpan.FromMilliseconds(200))
                    .ConfigureAwait(false);
                throw new InvalidOperationException("Contended lease unexpectedly succeeded.");
            }
            catch (InvalidOperationException exception)
            {
                True(exception.Message.Contains("lease busy", StringComparison.Ordinal), "Lease busy detail");
                True(
                    exception.Message.Contains(Environment.ProcessId.ToString(), StringComparison.Ordinal),
                    "Lease owner PID");
            }
        }
        finally
        {
            await lease.DisposeAsync().ConfigureAwait(false);
        }

        True(!File.Exists(path), "Disposed lease file deletion");
    }).ConfigureAwait(false);
}

static async Task TestConcurrentJsonLineTailAsync()
{
    await WithTemporaryDirectoryAsync(async root =>
    {
        var paths = WorkspaceLocator.ForRoot(root);
        var writer = new StateStore(paths);
        var reader = new StateStore(paths);
        await writer.InitializeAsync(new FknrtdConfig { ProjectName = "tail-test" }).ConfigureAwait(false);
        for (var index = 0; index < 50; index++)
        {
            await writer.AppendEventAsync(new FknrtdEvent { Type = $"event-{index}" }).ConfigureAwait(false);
        }

        var append = Task.Run(async () =>
        {
            for (var index = 50; index < 100; index++)
            {
                await writer.AppendEventAsync(new FknrtdEvent { Type = $"event-{index}" }).ConfigureAwait(false);
            }
        });
        while (!append.IsCompleted)
        {
            var snapshot = await reader.LoadEventsAsync(10).ConfigureAwait(false);
            True(snapshot.Count <= 10, "Concurrent tail limit");
        }

        await append.ConfigureAwait(false);
        var events = await reader.LoadEventsAsync(10).ConfigureAwait(false);
        Equal(10, events.Count, "Final tail count");
        Equal("event-90", events[0].Type, "Final tail first event");
        Equal("event-99", events[^1].Type, "Final tail last event");
    }).ConfigureAwait(false);
}

static async Task TestJsonLineRotationAsync()
{
    await WithTemporaryDirectoryAsync(async root =>
    {
        var store = new StateStore(WorkspaceLocator.ForRoot(root), jsonLineFileByteCap: 900);
        await store.InitializeAsync(new FknrtdConfig { ProjectName = "rotation-test" }).ConfigureAwait(false);
        for (var index = 0; index < 30; index++)
        {
            await store.AppendEventAsync(new FknrtdEvent
            {
                Type = $"rot-{index}",
                Message = new string('x', 80)
            }).ConfigureAwait(false);
            await store.AppendMessageAsync(new AgentMessage
            {
                FromAgentId = "sender",
                ToAgentId = "receiver",
                Text = $"message-{index}-" + new string('x', 80)
            }).ConfigureAwait(false);
        }

        True(File.Exists(store.Paths.Events + ".1"), "Rotation archive");
        True(File.Exists(store.Paths.Messages + ".1"), "Message rotation archive");
        var events = await store.LoadEventsAsync(3).ConfigureAwait(false);
        Equal(3, events.Count, "Rotated recent count");
        Equal("rot-27", events[0].Type, "Rotated first recent event");
        Equal("rot-29", events[^1].Type, "Rotated last recent event");
        var messages = await store.LoadMessagesAsync(3).ConfigureAwait(false);
        Equal(3, messages.Count, "Rotated recent message count");
        True(messages[0].Text.StartsWith("message-27-", StringComparison.Ordinal), "Rotated first recent message");
        True(messages[^1].Text.StartsWith("message-29-", StringComparison.Ordinal), "Rotated last recent message");
    }).ConfigureAwait(false);
}

static async Task TestExecutableLocatorAsync()
{
    if (!OperatingSystem.IsWindows())
    {
        return;
    }

    await WithTemporaryDirectoryAsync(root =>
    {
        var extensionless = Path.Combine(root, "codex");
        var command = Path.Combine(root, "codex.CMD");
        File.WriteAllText(extensionless, "shim");
        File.WriteAllText(command, "@exit /b 0");
        var originalPath = Environment.GetEnvironmentVariable("PATH");
        var originalPathExt = Environment.GetEnvironmentVariable("PATHEXT");
        try
        {
            Environment.SetEnvironmentVariable("PATH", root);
            Environment.SetEnvironmentVariable("PATHEXT", ".EXE;.CMD;.BAT;.COM");
            Equal(Path.GetFullPath(command), ExecutableLocator.Find("codex"), "PATHEXT resolution");
        }
        finally
        {
            Environment.SetEnvironmentVariable("PATH", originalPath);
            Environment.SetEnvironmentVariable("PATHEXT", originalPathExt);
        }

        return Task.CompletedTask;
    }).ConfigureAwait(false);
}

static Task TestConfigTimeoutDefaultsAsync()
{
    var config = JsonSerializer.Deserialize<FknrtdConfig>(
        "{\"projectName\":\"legacy\"}",
        JsonSupport.Options) ?? throw new InvalidOperationException("Legacy config did not deserialize.");
    Equal(3600, config.AgentTimeoutSeconds, "Default agent timeout");
    Equal(600, config.VerificationTimeoutSeconds, "Default verification timeout");
    return Task.CompletedTask;
}

static async Task TestDoctorFailureAsync()
{
    await WithTemporaryDirectoryAsync(async root =>
    {
        var process = new ProcessRunner();
        var store = new StateStore(WorkspaceLocator.ForRoot(root));
        var doctor = new DoctorService(store, new GitService(process), process);
        var missingConfigChecks = await doctor.RunAsync().ConfigureAwait(false);
        True(
            !missingConfigChecks.Single(check => check.Name == "Configuration").Passed,
            "Missing configuration doctor result");

        var invalidExecutable = Path.Combine(root, OperatingSystem.IsWindows() ? "broken.exe" : "broken");
        await File.WriteAllTextAsync(invalidExecutable, "not an executable").ConfigureAwait(false);
        await store.InitializeAsync(new FknrtdConfig
        {
            ProjectName = "doctor-test",
            AgentTimeoutSeconds = 1,
            Agents =
            [
                new AgentDefinition
                {
                    Id = "broken",
                    DisplayName = "Broken",
                    Executable = invalidExecutable
                }
            ]
        }).ConfigureAwait(false);
        var checks = await doctor.RunAsync().ConfigureAwait(false);
        var agent = checks.Single(check => check.Name == "Agent: Broken");
        True(!agent.Passed, "Unlaunchable agent doctor result");
        True(agent.Detail.Contains(invalidExecutable, StringComparison.Ordinal), "Unlaunchable agent detail");
    }).ConfigureAwait(false);
}

static Task TestClaudeUsageAsync()
{
    using var document = JsonDocument.Parse("""
        {
          "context_window": { "used_percentage": 37 },
          "rate_limits": {
            "five_hour": { "used_percentage": 18, "resets_at": 1893456000 },
            "seven_day": { "used_percentage": 29, "resets_at": 1893542400 }
          }
        }
        """);
    var usage = UsageService.ParseClaudeStatusLine(document.RootElement);
    Equal(63d, usage.ContextRemainingPercent, "Claude context remaining");
    Equal(82d, usage.FiveHourRemainingPercent, "Claude five-hour remaining");
    Equal(71d, usage.WeeklyRemainingPercent, "Claude weekly remaining");
    return Task.CompletedTask;
}

static Task TestCodexUsageAsync()
{
    using var document = JsonDocument.Parse("""
        {
          "rateLimits": {
            "primary": { "usedPercent": 25, "windowDurationMins": 300, "resetsAt": 1893456000 },
            "secondary": { "usedPercent": 40, "windowDurationMins": 10080, "resetsAt": 1893542400 }
          }
        }
        """);
    var usage = UsageService.ParseCodexRateLimits(document.RootElement);
    Equal(75d, usage.FiveHourRemainingPercent, "Codex five-hour remaining");
    Equal(60d, usage.WeeklyRemainingPercent, "Codex weekly remaining");

    using var missing = JsonDocument.Parse("{\"rateLimits\":{}}");
    var unavailable = UsageService.ParseCodexRateLimits(missing.RootElement);
    Equal<double?>(null, unavailable.FiveHourRemainingPercent, "Missing Codex bucket");
    return Task.CompletedTask;
}

static async Task TestConflictDetectionAsync()
{
    await WithTemporaryDirectoryAsync(root =>
    {
        var service = new ClaimService(new StateStore(WorkspaceLocator.ForRoot(root)));
        var now = DateTimeOffset.UtcNow;
        var sameTree = new[]
        {
            Claim("claude", root, "src/auth.cs", now.AddMinutes(1)),
            Claim("codex", root, "src/auth.cs", now.AddMinutes(1))
        };
        Equal(ConflictKind.Collision, service.Detect(sameTree, [], now).Single().Kind, "Same-tree conflict");

        var other = Path.Combine(root, "other");
        Directory.CreateDirectory(other);
        var splitTrees = new[]
        {
            Claim("claude", root, "src/auth.cs", now.AddMinutes(1)),
            Claim("codex", other, "src/auth.cs", now.AddMinutes(1))
        };
        Equal(ConflictKind.MergeRisk, service.Detect(splitTrees, [], now).Single().Kind, "Split-tree conflict");

        var stale = new[] { Claim("cline", root, "src/old.cs", now.AddSeconds(-1)) };
        Equal(ConflictKind.StaleClaim, service.Detect(stale, [], now).Single().Kind, "Stale claim");
        return Task.CompletedTask;
    }).ConfigureAwait(false);
}

static Task TestAuditVerdictAsync()
{
    const string success = "FKNRTD_VERDICT: PASS";
    const string failure = "FKNRTD_VERDICT: FAIL";
    const string prompt = """
        Inspect the diff.
        Finish with exactly one verdict marker on its own line:
        FKNRTD_VERDICT: PASS
        or
        FKNRTD_VERDICT: FAIL
        """;
    var echoedPass = prompt + Environment.NewLine + "```" + Environment.NewLine +
                     "**FKNRTD_VERDICT: PASS.**" + Environment.NewLine + "```";
    True(
        Orchestrator.IsPassingAuditVerdict(echoedPass, prompt, success, failure),
        "Echoed prompt plus trailing markdown PASS verdict");

    var ambiguous = prompt + Environment.NewLine + success + Environment.NewLine + failure;
    True(
        !Orchestrator.IsPassingAuditVerdict(ambiguous, prompt, success, failure),
        "Ambiguous own verdict");
    True(
        !Orchestrator.IsPassingAuditVerdict(
            prompt + Environment.NewLine + "Result is FKNRTD_VERDICT: PASS today.",
            prompt,
            success,
            failure),
        "Mid-sentence verdict mention");
    return Task.CompletedTask;
}

static async Task TestGitPathRoundTripAsync()
{
    if (ExecutableLocator.Find("git") is null)
    {
        throw new InvalidOperationException("Git is required for the path round-trip self-test.");
    }

    await WithTemporaryDirectoryAsync(async root =>
    {
        var git = new GitService(new ProcessRunner());
        MustSucceed(await git.GitAsync(root, ["init", "-b", "main"]).ConfigureAwait(false), "git init");
        var expected = new[] { "file with space.txt", "naïve-文件.txt" };
        foreach (var path in expected)
        {
            await File.WriteAllTextAsync(Path.Combine(root, path), path, new UTF8Encoding(false))
                .ConfigureAwait(false);
        }

        var changed = await git.GetChangedPathsAsync(root).ConfigureAwait(false);
        foreach (var path in expected)
        {
            True(changed.Contains(path, StringComparer.Ordinal), $"Verbatim changed path '{path}'");
        }
    }).ConfigureAwait(false);
}

static async Task TestWorkflowAsync()
{
    if (ExecutableLocator.Find("git") is null)
    {
        throw new InvalidOperationException("Git is required for the workflow self-test.");
    }

    await WithTemporaryDirectoryAsync(async root =>
    {
        var process = new ProcessRunner();
        var git = new GitService(process);
        MustSucceed(await git.GitAsync(root, ["init", "-b", "main"]).ConfigureAwait(false), "git init");
        MustSucceed(await git.GitAsync(root, ["config", "user.email", "fknrtd-self-test@example.invalid"])
            .ConfigureAwait(false), "git config email");
        MustSucceed(await git.GitAsync(root, ["config", "user.name", "FKNRTD.CLI Self Test"])
            .ConfigureAwait(false), "git config name");
        await File.WriteAllTextAsync(Path.Combine(root, "README.md"), "# Test\n", new UTF8Encoding(false))
            .ConfigureAwait(false);

        var paths = WorkspaceLocator.ForRoot(root);
        var store = new StateStore(paths);
        await store.InitializeAsync(new FknrtdConfig
        {
            ProjectName = "workflow-test",
            Agents = [CreateFakeAgent()]
        }).ConfigureAwait(false);
        Equal(string.Empty, new FknrtdConfig().DefaultBaseRef, "Default config base ref");
        MustSucceed(await git.GitAsync(root, ["add", "README.md", ".fknrtd/config.json", ".fknrtd/.gitignore"])
            .ConfigureAwait(false), "git add");
        MustSucceed(await git.GitAsync(root, ["commit", "-m", "initial"])
            .ConfigureAwait(false), "initial commit");

        var worktrees = new WorktreeService(git, store);
        var tasks = new TaskService(store, git);
        var agentRunner = new AgentRunner(store, process);
        var orchestrator = new Orchestrator(store, git, worktrees, agentRunner, process);
        var verification = OperatingSystem.IsWindows()
            ? "if exist feature.txt (exit /b 0) else (exit /b 1)"
            : "test -f feature.txt";
        var task = await tasks.CreateAsync(
                "Create a feature marker",
                "Create feature.txt with a short marker.",
                "fake",
                "fake",
                "fake",
                [verification])
            .ConfigureAwait(false);
        Equal("main", task.BaseRef, "Task creation base branch");
        task.BaseRef = "HEAD";
        await store.SaveTaskAsync(task).ConfigureAwait(false);

        MustSucceed(await git.GitAsync(root, ["config", "user.name", ""]).ConfigureAwait(false),
            "clear git config name");
        MustSucceed(await git.GitAsync(root, ["config", "user.email", ""]).ConfigureAwait(false),
            "clear git config email");
        try
        {
            await orchestrator.RunAsync(task.Id).ConfigureAwait(false);
            throw new InvalidOperationException("Workflow unexpectedly committed without committer identity.");
        }
        catch (InvalidOperationException exception)
        {
            True(exception.Message.Contains("user.name", StringComparison.Ordinal), "Missing user.name guidance");
            True(exception.Message.Contains("user.email", StringComparison.Ordinal), "Missing user.email guidance");
        }

        task = await store.LoadTaskAsync(task.Id).ConfigureAwait(false);
        Equal("main", task.BaseRef, "Legacy HEAD base repair");
        Equal(StageState.Passed, task.Stage(WorkflowStage.Audit).State, "Echo-safe audit state");
        MustSucceed(await git.GitAsync(root, ["config", "user.email", "fknrtd-self-test@example.invalid"])
            .ConfigureAwait(false), "restore git config email");
        MustSucceed(await git.GitAsync(root, ["config", "user.name", "FKNRTD.CLI Self Test"])
            .ConfigureAwait(false), "restore git config name");
        await tasks.ResetFailedStagesAsync(task.Id).ConfigureAwait(false);

        task = await orchestrator.RunAsync(task.Id).ConfigureAwait(false);
        Equal(WorkflowStatus.ReadyToLand, task.Status, "Workflow ready state");
        Equal(StageState.Passed, task.Stage(WorkflowStage.Verify).State, "Verification state");
        Equal(StageState.Passed, task.Stage(WorkflowStage.Audit).State, "Audit state");
        Equal(Path.GetFullPath(root), WorkspaceLocator.Find(task.WorktreePath).Root,
            "Linked worktree resolves primary FKNRTD.CLI state");

        MustSucceed(await git.GitAsync(root, ["switch", "-c", "landing-mismatch"]).ConfigureAwait(false),
            "switch to landing mismatch branch");
        try
        {
            await orchestrator.LandAsync(task.Id).ConfigureAwait(false);
            throw new InvalidOperationException("Landing unexpectedly accepted the wrong primary branch.");
        }
        catch (InvalidOperationException exception)
        {
            True(exception.Message.Contains("'landing-mismatch'", StringComparison.Ordinal),
                "Landing actual branch mismatch");
            True(exception.Message.Contains("'main'", StringComparison.Ordinal), "Landing expected branch mismatch");
        }

        MustSucceed(await git.GitAsync(root, ["switch", "main"]).ConfigureAwait(false), "switch to main");
        MustSucceed(await git.GitAsync(root, ["branch", "-D", "landing-mismatch"]).ConfigureAwait(false),
            "remove landing mismatch branch");
        task = await orchestrator.LandAsync(task.Id).ConfigureAwait(false);
        Equal(WorkflowStatus.Landed, task.Status, "Landed state");
        True(File.Exists(Path.Combine(root, "feature.txt")), "Landed feature file");
        var taskWorktree = task.WorktreePath;
        var taskBranch = task.BranchName;
        await worktrees.RemoveAsync(task, force: false, WorkspaceMode.Git).ConfigureAwait(false);
        True(!Directory.Exists(taskWorktree), "Landed worktree directory removed");
        True(!await git.BranchExistsAsync(root, taskBranch).ConfigureAwait(false), "Landed task branch removed");
        var worktreeList = await git.GitAsync(root, ["worktree", "list", "--porcelain"]).ConfigureAwait(false);
        MustSucceed(worktreeList, "git worktree list");
        True(!worktreeList.StandardOutput.Contains(taskWorktree, StringComparison.OrdinalIgnoreCase),
            "Landed worktree metadata pruned");
    }).ConfigureAwait(false);
}

static FileClaim Claim(
    string agent,
    string worktree,
    string path,
    DateTimeOffset expiry) => new()
{
    AgentId = agent,
    WorktreePath = worktree,
    Paths = [path],
    ExpiresAt = expiry
};

static IReadOnlyList<string> SelfInvocationArguments(params string[] arguments)
{
    var executable = Environment.ProcessPath
        ?? throw new InvalidOperationException("The self-test process path is unavailable.");
    return Path.GetFileNameWithoutExtension(executable).Equals("dotnet", StringComparison.OrdinalIgnoreCase)
        ? [Assembly.GetEntryAssembly()?.Location ?? throw new InvalidOperationException("Assembly path missing."), .. arguments]
        : arguments;
}

static void MustSucceed(CommandResult result, string operation)
{
    if (!result.Success)
    {
        throw new InvalidOperationException($"{operation} failed: {result.StandardError}");
    }
}

static void Equal<T>(T expected, T actual, string label)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
    {
        throw new InvalidOperationException($"{label}: expected {expected}, got {actual}.");
    }
}

static void True(bool value, string label)
{
    if (!value)
    {
        throw new InvalidOperationException(label + " was false.");
    }
}

static AgentDefinition CreateFakeAgent()
{
    var executable = Environment.ProcessPath
        ?? throw new InvalidOperationException("The self-test process path is unavailable.");
    var profilePrefix = Path.GetFileNameWithoutExtension(executable)
        .Equals("dotnet", StringComparison.OrdinalIgnoreCase)
        ? new[] { Assembly.GetEntryAssembly()?.Location ?? throw new InvalidOperationException("Assembly path missing.") }
        : Array.Empty<string>();
    AgentCommandProfile Profile(string role, bool audit = false) => new()
    {
        Arguments = profilePrefix.Concat(["fake-agent", role, "{prompt}"]).ToList(),
        SuccessMarker = audit ? "FKNRTD_VERDICT: PASS" : null,
        FailureMarker = audit ? "FKNRTD_VERDICT: FAIL" : null
    };
    return new AgentDefinition
    {
        Id = "fake",
        DisplayName = "Fake Agent",
        Executable = executable,
        Profiles = new Dictionary<string, AgentCommandProfile>(StringComparer.OrdinalIgnoreCase)
        {
            ["plan"] = Profile("plan"),
            ["implement"] = Profile("implement"),
            ["audit"] = Profile("audit", audit: true)
        }
    };
}

static async Task TestStandaloneWorkflowAsync()
{
    await WithTemporaryDirectoryAsync(async root =>
    {
        var paths = WorkspaceLocator.ForRoot(root);
        var store = new StateStore(paths);
        Equal(0, await QuietlyAsync(["init", "-root", root, "-yes"]).ConfigureAwait(false), "Standalone init exit code");

        var config = await store.LoadConfigAsync().ConfigureAwait(false);
        Equal(WorkspaceMode.Standalone, config.Mode, "Standalone init mode outside a repository");
        Equal(string.Empty, config.DefaultBaseRef, "Standalone init base ref");
        Equal(false, config.AutoCommitAgentChanges, "Standalone init disables auto-commit");
        await store.SaveConfigAsync(config with { Agents = [CreateFakeAgent()] }).ConfigureAwait(false);

        var process = new ProcessRunner();
        var git = new GitService(process);
        var worktrees = new WorktreeService(git, store);
        var tasks = new TaskService(store, git);
        var orchestrator = new Orchestrator(store, git, worktrees, new AgentRunner(store, process), process);
        var verification = OperatingSystem.IsWindows()
            ? "if exist feature.txt (exit /b 0) else (exit /b 1)"
            : "test -f feature.txt";
        var task = await tasks.CreateAsync(
                "Create a feature marker",
                "Create feature.txt with a short marker.",
                "fake",
                "fake",
                "fake",
                [verification])
            .ConfigureAwait(false);
        Equal(string.Empty, task.BaseRef, "Standalone task base ref");

        task = await orchestrator.RunAsync(task.Id).ConfigureAwait(false);
        Equal(WorkflowStatus.ReadyToLand, task.Status, "Standalone ready state");
        Equal(StageState.Skipped, task.Stage(WorkflowStage.Worktree).State, "Standalone worktree stage");
        Equal(Path.GetFullPath(root), task.WorktreePath, "Standalone work directory");
        Equal(string.Empty, task.BranchName, "Standalone branch name");
        Equal(StageState.Passed, task.Stage(WorkflowStage.Verify).State, "Standalone verification state");
        Equal(StageState.Passed, task.Stage(WorkflowStage.Audit).State, "Standalone audit state");
        True(File.Exists(Path.Combine(root, "feature.txt")), "Standalone feature file");
        True(!Directory.EnumerateFileSystemEntries(paths.Worktrees).Any(), "Standalone run creates no worktree");

        task = await orchestrator.LandAsync(task.Id).ConfigureAwait(false);
        Equal(WorkflowStatus.Landed, task.Status, "Standalone landed state");

        // Cleanup must be a no-op rather than a Git failure.
        await worktrees.RemoveAsync(task, force: false, WorkspaceMode.Standalone).ConfigureAwait(false);

        // Doctor must not fail a standalone workspace over the Git checks it cannot satisfy.
        var checks = await new DoctorService(store, git, process).RunAsync().ConfigureAwait(false);
        foreach (var name in new[] { "Git executable", "Git repository" })
        {
            True(!checks.Single(check => check.Name == name).Required, $"Standalone doctor relaxes: {name}");
        }

        True(checks.Single(check => check.Name == "Git repository").Passed, "Standalone doctor passes Git repository");
        True(checks.Single(check => check.Name == "Workspace mode").Detail
            .Contains("Standalone", StringComparison.Ordinal), "Standalone doctor reports the mode");
    }).ConfigureAwait(false);
}

static async Task TestDefaultInvocationAsync()
{
    await WithTemporaryDirectoryAsync(async root =>
    {
        var project = Path.Combine(root, "project");
        var nested = Path.Combine(project, "src");
        Directory.CreateDirectory(nested);
        var paths = WorkspaceLocator.ForRoot(project);
        var originalDirectory = Environment.CurrentDirectory;
        string firstFrame;
        try
        {
            // An explicit '.' opens the current folder itself.
            Environment.CurrentDirectory = project;
            var output = new StringWriter();
            Equal(0, await QuietlyAsync([".", "-once", "-no-color", "-width", "96", "-height", "24"], output)
                .ConfigureAwait(false), "Current-folder invocation exit code");
            firstFrame = output.ToString();

            // A bare invocation from a subdirectory reuses that workspace instead of nesting a new one.
            Environment.CurrentDirectory = nested;
            Equal(0, await QuietlyAsync(["-once", "-no-color", "-width", "96", "-height", "24"])
                .ConfigureAwait(false), "Bare invocation exit code");
        }
        finally
        {
            Environment.CurrentDirectory = originalDirectory;
        }

        True(File.Exists(paths.Config), "Current-folder invocation provisions the folder");
        True(!Directory.Exists(Path.Combine(nested, ".fknrtd")), "Bare invocation reuses the enclosing workspace");

        var config = await new StateStore(paths).LoadConfigAsync().ConfigureAwait(false);
        Equal(WorkspaceMode.Standalone, config.Mode, "Default loading mode outside a repository");
        Equal("project", config.ProjectName, "Default loading project name");
        True(firstFrame.Contains("FKNRTD COMMAND CENTER", StringComparison.Ordinal),
            "Default loading renders the dashboard");
        True(firstFrame.Contains("standalone", StringComparison.Ordinal),
            "Default loading header reports the standalone workspace");
    }).ConfigureAwait(false);
}

static async Task TestDefaultInvocationInRepositoryAsync()
{
    if (ExecutableLocator.Find("git") is null)
    {
        throw new InvalidOperationException("Git is required for the repository rooting self-test.");
    }

    await WithTemporaryDirectoryAsync(async root =>
    {
        var git = new GitService(new ProcessRunner());
        MustSucceed(await git.GitAsync(root, ["init", "-b", "main"]).ConfigureAwait(false), "git init");
        var nested = Path.Combine(root, "scripts");
        Directory.CreateDirectory(nested);

        var originalDirectory = Environment.CurrentDirectory;
        try
        {
            // A bare invocation from a subdirectory of an uninitialized repository must provision
            // the repository root, not the subdirectory it happened to be run from.
            Environment.CurrentDirectory = nested;
            Equal(0, await QuietlyAsync(["-once", "-no-color", "-width", "96", "-height", "24"])
                .ConfigureAwait(false), "Repository rooting exit code");
        }
        finally
        {
            Environment.CurrentDirectory = originalDirectory;
        }

        var paths = WorkspaceLocator.ForRoot(root);
        True(File.Exists(paths.Config), "Repository rooting provisions the repository root");
        True(!Directory.Exists(Path.Combine(nested, ".fknrtd")), "Repository rooting skips the subdirectory");

        var config = await new StateStore(paths).LoadConfigAsync().ConfigureAwait(false);
        Equal(WorkspaceMode.Git, config.Mode, "Repository rooting mode");
        Equal("main", config.DefaultBaseRef, "Repository rooting base ref");
    }).ConfigureAwait(false);
}

/// <summary>Runs a dispatcher command with console output captured so the suite stays readable.</summary>
static async Task<int> QuietlyAsync(string[] arguments, StringWriter? output = null)
{
    // Standard error is captured too. Several cases here deliberately exercise failure paths, and
    // their diagnostics would otherwise be interleaved with the suite's own results.
    var originalOutput = Console.Out;
    var originalError = Console.Error;
    output ??= new StringWriter();
    try
    {
        Console.SetOut(output);
        Console.SetError(output);
        return await CommandDispatcher.ExecuteAsync(new CliArguments(arguments), CancellationToken.None)
            .ConfigureAwait(false);
    }
    finally
    {
        Console.SetOut(originalOutput);
        Console.SetError(originalError);
    }
}

static async Task WithTemporaryDirectoryAsync(Func<string, Task> action)
{
    var root = Path.Combine(Path.GetTempPath(), "fknrtd-self-test-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(root);
    try
    {
        await action(root).ConfigureAwait(false);
    }
    finally
    {
        try
        {
            Directory.Delete(root, recursive: true);
        }
        catch (IOException)
        {
            // Keep failed cleanup recoverable in the operating system's temporary directory.
        }
        catch (UnauthorizedAccessException)
        {
            // Keep failed cleanup recoverable in the operating system's temporary directory.
        }
    }
}

// ── Usability layer ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// The invariant the whole guided experience rests on: a field the operator is asked to fill in
/// must have an explanation to show under it. A step naming a term that does not exist would render
/// a bare question, which is the exact failure this layer was built to remove.
/// </summary>
static Task TestWizardStepsAreExplainedAsync()
{
    var config = Scenes.SampleConfig();
    foreach (var wizard in new[]
             {
                 TaskWizard.Create(config),
                 TaskWizard.Message(config),
                 SetupWizard.Create(Scenes.SampleDetection()),
                 AgentWizard.Create(Scenes.SampleConfig())
             })
    {
        // Walk the whole form by accepting each default, and require an explanation at every step.
        for (var guard = 0; guard < 20; guard++)
        {
            var frame = new DashboardApp(null!, null!, null!, null!, null!, null!, null!, null!, null!)
                .Render(Scenes.EmptySnapshot(), 110, 40, useColor: false, wizard);
            True(frame.Contains("WHAT THIS IS", StringComparison.Ordinal),
                "Every wizard step shows an explanation");
            True(frame.Contains("Esc cancel", StringComparison.Ordinal),
                "Every wizard step says how to leave it");
            // Every option on a choice step must be drawn. Hiding one on a two-option step hides
            // that there was a choice at all, which is worse than any amount of scrolling.
            var options = Scenes.OptionsOnScreen(frame);
            if (options > 0)
            {
                True(frame.Contains("▸", StringComparison.Ordinal),
                    "A choice step marks the selected option");
            }

            if (Scenes.Press(wizard, ConsoleKey.Enter) == OverlayResult.Submit)
            {
                break;
            }
        }
    }

    return Task.CompletedTask;
}

/// <summary>
/// Every glossary term a wizard step names must resolve, and every entry must actually say
/// something. A summary that does not fit one line breaks the inline hint it is sized for.
/// </summary>
static Task TestGlossaryIsCompleteAsync()
{
    foreach (var entry in Glossary.All)
    {
        // A summary is an inline hint under a form field, so it has to fit one line on an 80-column
        // terminal. Marker entries — the single glyphs in a legend — are legitimately terser than a
        // concept the operator has to reason about.
        var marker = entry.Category is Glossary.StageStates or Glossary.AgentStates;
        True(entry.Summary.Length > (marker ? 10 : 20), $"Glossary summary is substantive for '{entry.Term}'");
        True(entry.Summary.Length <= 96, $"Glossary summary fits one line for '{entry.Term}'");
        True(entry.Detail.Length > 40, $"Glossary detail present for '{entry.Term}'");
        True(Glossary.Find(entry.Term) is not null, $"Glossary lookup for '{entry.Term}'");
    }

    Equal(Glossary.All.Count, Glossary.All.Select(entry => entry.Term).Distinct(StringComparer.Ordinal).Count(),
        "Glossary terms are unique");

    // Every marker the dashboard can draw has to be explainable, or the legend lies by omission.
    foreach (var status in Enum.GetValues<WorkflowStatus>())
    {
        True(Glossary.Find("status." + status.ToString().ToLowerInvariant()) is not null,
            $"Glossary covers status {status}");
    }

    foreach (var stage in Enum.GetValues<WorkflowStage>())
    {
        True(Glossary.Find("stage." + stage.ToString().ToLowerInvariant()) is not null,
            $"Glossary covers stage {stage}");
    }

    foreach (var state in Enum.GetValues<StageState>())
    {
        True(Glossary.Find("stagestate." + state.ToString().ToLowerInvariant()) is not null,
            $"Glossary covers stage state {state}");
    }

    foreach (var state in Enum.GetValues<AgentActivityState>().Where(value => value != AgentActivityState.Unknown))
    {
        True(Glossary.Find("agentstate." + state.ToString().ToLowerInvariant()) is not null,
            $"Glossary covers agent state {state}");
    }

    foreach (var binding in Keymap.All)
    {
        True(binding.Detail.Length > 40, $"Keymap explains '{binding.Key}'");
    }

    // Every settable field has to say what it controls and what moving it costs. A setting whose
    // consequence is unstated is one an operator changes by guessing.
    foreach (var setting in SettingsCatalog.All)
    {
        True(setting.Summary.Length is > 20 and <= 96, $"Setting summary for '{setting.Key}'");
        True(setting.Detail.Length > 40, $"Setting detail for '{setting.Key}'");
        True(setting.IfYouChangeIt.Length > 50, $"Setting '{setting.Key}' states a consequence");
        True(SettingsCatalog.Find(setting.Key) is not null, $"Setting lookup for '{setting.Key}'");
        True(SettingsCatalog.Find(setting.Title) is not null, $"Setting lookup by title for '{setting.Key}'");
        if (setting.GlossaryTerm is not null)
        {
            True(Glossary.Find(setting.GlossaryTerm) is not null,
                $"Setting '{setting.Key}' references glossary term '{setting.GlossaryTerm}'");
        }
    }

    // Every field the configuration file can carry has to be documented, nested ones included — an
    // independent review found three on AgentDefinition that a top-level-only check had missed.
    foreach (var property in typeof(FknrtdConfig).GetProperties())
    {
        var name = char.ToLowerInvariant(property.Name[0]) + property.Name[1..];
        True(SettingsCatalog.Find(name) is not null, $"The settings catalog documents '{name}'");
    }

    foreach (var property in typeof(AgentDefinition).GetProperties())
    {
        var name = "agents[]." + char.ToLowerInvariant(property.Name[0]) + property.Name[1..];
        True(SettingsCatalog.Find(name) is not null, $"The settings catalog documents '{name}'");
    }

    Equal(Keymap.All.Count, Keymap.All.Select(binding => binding.Key).Distinct(StringComparer.Ordinal).Count(),
        "Keymap keys are unique");
    True(Keymap.Footer.Length is > 3 and < 10, "Keymap footer is a usable size");
    True(Keymap.EssentialFooter.Length is > 0 and < 4, "A few footer keys are marked essential");
    Equal(Keymap.Footer.Length, Keymap.EssentialFooter.Length + Keymap.OptionalFooter.Length,
        "Every footer key is either essential or optional");

    // Help and quit are the two keys an operator needs at the moment they cannot find anything, so
    // they have to survive every width the dashboard supports — they used to be the first dropped.
    foreach (var width in new[] { 60, 62, 70, 84, 100, 120, 200 })
    {
        var strip = FrameLines(Scenes.Render("overview", width, 20, colour: false))[^2];
        True(strip.Contains("? help", StringComparison.Ordinal), $"Help stays in the footer at {width}");
        True(strip.Contains("Q quit", StringComparison.Ordinal), $"Quit stays in the footer at {width}");
        Equal(width, Text.DisplayWidth(strip), $"The footer strip is exactly {width} wide");
    }
    return Task.CompletedTask;
}

/// <summary>
/// Overlays are drawn onto the same canvas as the dashboard, so the alignment guarantee has to hold
/// with one open. A modal that shifts a box border by a column is the bug this suite exists to catch.
/// </summary>
static Task TestOverlayFramesAsync()
{
    foreach (var scene in Scenes.Names)
    {
        foreach (var width in new[] { 60, 84, 100, 120, 160 })
        {
            foreach (var height in new[] { 20, 30, 44 })
            {
                var frame = Scenes.Render(scene, width, height, colour: false);
                var lines = FrameLines(frame);
                Equal(height, lines.Length, $"Scene '{scene}' line count at {width}x{height}");
                True(lines.All(line => Text.DisplayWidth(line) == width),
                    $"Scene '{scene}' line width at {width}x{height}");
                True(!frame.Contains('\u001b'), $"Scene '{scene}' emits no escapes without colour");
            }
        }

        // The same frame in colour must still be the same shape once the escapes are stripped, must
        // use only well-formed sequences, and must reset at the end of every row - otherwise a panel
        // background bleeds across the rest of the terminal and stays there after the frame ends.
        var coloured = Scenes.Render(scene, 120, 34, colour: true);
        True(coloured.Contains('\u001b'), $"Scene '{scene}' emits colour when asked");
        foreach (var sequence in Ansi.Sequence.Matches(coloured).Select(match => match.Value))
        {
            True(Ansi.WellFormed.IsMatch(sequence), $"Scene '{scene}' emits only well-formed colour");
        }

        foreach (var line in FrameLines(coloured))
        {
            Equal(120, Text.DisplayWidth(Ansi.Sequence.Replace(line, string.Empty)),
                $"Scene '{scene}' keeps its width once colour is stripped");
            True(!line.Contains('\u001b') || line.EndsWith("\u001b[0m", StringComparison.Ordinal),
                $"Scene '{scene}' resets colour at the end of every row");
        }

        // Colour may only ever be an enhancement. Stripping it has to leave exactly the frame the
        // renderer produces with colour off — so nothing is distinguished by colour alone, and a
        // monochrome terminal, a redirected pipe and a colour-blind reader all lose nothing.
        Equal(
            Scenes.Render(scene, 120, 34, colour: false),
            Ansi.Sequence.Replace(coloured, string.Empty),
            $"Scene '{scene}' carries no information in colour alone");
    }

    return Task.CompletedTask;
}

/// <summary>A refused answer has to say what was wrong with it, not merely refuse.</summary>
static Task TestWizardValidationExplainsItselfAsync()
{
    var wizard = TaskWizard.Create(Scenes.SampleConfig());
    var renderer = new DashboardApp(null!, null!, null!, null!, null!, null!, null!, null!, null!);

    // An empty title cannot advance the form.
    Equal(OverlayResult.Continue, Scenes.Press(wizard, ConsoleKey.Enter), "Empty title does not advance");
    var frame = renderer.Render(Scenes.EmptySnapshot(), 110, 40, useColor: false, wizard);
    True(frame.Contains("A title is required", StringComparison.Ordinal), "Empty title explains itself");
    True(frame.Contains("Step 1 of", StringComparison.Ordinal), "Refused step stays on step 1");

    // A title advances; a one-word brief does not.
    Scenes.Type(wizard, "Add rate limiting");
    Equal(OverlayResult.Continue, Scenes.Press(wizard, ConsoleKey.Enter), "Valid title advances");
    Scenes.Type(wizard, "do it");
    Equal(OverlayResult.Continue, Scenes.Press(wizard, ConsoleKey.Enter), "Too-short brief does not advance");
    frame = renderer.Render(Scenes.EmptySnapshot(), 110, 40, useColor: false, wizard);
    True(frame.Contains("too short to act on", StringComparison.Ordinal), "Short brief explains itself");

    // Going back restores what was already typed rather than discarding it.
    Scenes.Press(wizard, ConsoleKey.Tab, shift: true);
    frame = renderer.Render(Scenes.EmptySnapshot(), 110, 40, useColor: false, wizard);
    True(frame.Contains("Add rate limiting", StringComparison.Ordinal), "Stepping back keeps the earlier answer");

    // A highlighted choice survives navigating away and back too. Reaching the lead step, moving the
    // highlight off the default, stepping back and returning must show the moved highlight.
    var choices = TaskWizard.Create(Scenes.SampleConfig());
    Scenes.Type(choices, "Title");
    Scenes.Press(choices, ConsoleKey.Enter);
    Scenes.Type(choices, "A brief long enough to be accepted by the form.");
    Scenes.Press(choices, ConsoleKey.Enter);
    Scenes.Press(choices, ConsoleKey.DownArrow);   // review: "let me look"
    Scenes.Press(choices, ConsoleKey.Enter);
    Scenes.Press(choices, ConsoleKey.DownArrow);   // lead: move off the default
    Scenes.Press(choices, ConsoleKey.Tab, shift: true);
    Scenes.Press(choices, ConsoleKey.Enter);
    Scenes.Press(choices, ConsoleKey.Enter);
    Equal("codex", choices.Value("lead"), "A moved choice survives stepping back and returning");

    // The common path is title, brief, Enter: everything after the brief already has a default that
    // is correct for this workspace, and a step skipped for that reason must still carry its answer.
    var quick = TaskWizard.Create(Scenes.SampleConfig());
    Scenes.Type(quick, "Quick task");
    Scenes.Press(quick, ConsoleKey.Enter);
    Scenes.Type(quick, "A brief long enough to be accepted by the form.");
    Scenes.Press(quick, ConsoleKey.Enter);
    Equal(OverlayResult.Continue, Scenes.Press(quick, ConsoleKey.Enter), "Accepting the defaults advances");
    Equal(OverlayResult.Submit, Scenes.Press(quick, ConsoleKey.Enter), "Three answers create a task");

    Equal("Quick task", quick.Value("title"), "The quick path keeps the title");
    Equal("claude", quick.Value("lead"), "A skipped step still carries its default");
    Equal("codex", quick.Value("implementer"), "A skipped default can depend on an earlier one");
    Equal("claude", quick.Value("auditor"), "The auditor defaults to the lead");
    Equal("main", quick.Value("base"), "The base branch defaults to the workspace's");
    Equal(2, quick.Lines("verify").Count, "The verification commands default to the workspace's");
    Equal("1", quick.Value("repairs"), "The repair budget defaults to the workspace's");
    return Task.CompletedTask;
}

/// <summary>A destructive action must take the whole word and nothing else.</summary>
static Task TestConfirmationRequiresTheWordAsync()
{
    var confirmation = new Confirmation("LAND THIS TASK", Theme.Green, "FKN-1 — Subject",
        "This merges the task branch into main and cannot be undone from here.", "LAND", "land");
    var renderer = new DashboardApp(null!, null!, null!, null!, null!, null!, null!, null!, null!);

    Equal(OverlayResult.Continue, Scenes.Press(confirmation, ConsoleKey.Enter), "Bare Enter does not confirm");
    Scenes.Type(confirmation, "y");
    Equal(OverlayResult.Continue, Scenes.Press(confirmation, ConsoleKey.Enter), "'y' does not confirm");
    Scenes.Type(confirmation, "es");
    Equal(OverlayResult.Continue, Scenes.Press(confirmation, ConsoleKey.Enter), "'yes' does not confirm");
    var frame = renderer.Render(Scenes.EmptySnapshot(), 110, 40, useColor: false, confirmation);
    True(frame.Contains("That is not the word", StringComparison.Ordinal), "A wrong word explains itself");

    for (var index = 0; index < 3; index++)
    {
        Scenes.Press(confirmation, ConsoleKey.Backspace);
    }

    Scenes.Type(confirmation, "LAND");
    Equal(OverlayResult.Submit, Scenes.Press(confirmation, ConsoleKey.Enter), "The exact word confirms");
    Equal(OverlayResult.Cancel, Scenes.Press(confirmation, ConsoleKey.Escape), "Escape backs out");
    return Task.CompletedTask;
}

/// <summary>
/// The caret's character index has to map back to the row and column the wrap put it on, or typing
/// into a wrapped brief draws the caret somewhere the operator is not.
/// </summary>
static Task TestTextFieldEditingAsync()
{
    var field = new TextField(multiline: false);
    foreach (var character in "hello world")
    {
        field.HandleKey(new ConsoleKeyInfo(character, ConsoleKey.NoName, false, false, false));
    }

    Equal("hello world", field.Value, "Typed value");
    Equal(11, field.Cursor, "Caret after typing");

    field.HandleKey(new ConsoleKeyInfo('\0', ConsoleKey.LeftArrow, false, false, true));
    Equal(6, field.Cursor, "Ctrl+Left jumps a word");
    field.HandleKey(new ConsoleKeyInfo('\0', ConsoleKey.Home, false, false, false));
    Equal(0, field.Cursor, "Home");
    field.HandleKey(new ConsoleKeyInfo('\0', ConsoleKey.Delete, false, false, false));
    Equal("ello world", field.Value, "Delete removes forwards");
    field.HandleKey(new ConsoleKeyInfo('\0', ConsoleKey.End, false, false, false));
    field.HandleKey(new ConsoleKeyInfo('\b', ConsoleKey.Backspace, false, false, true));
    Equal("ello ", field.Value, "Ctrl+Backspace removes a word");

    // Enter belongs to the form, not to a single-line field.
    True(!field.HandleKey(new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false)),
        "Enter is not consumed by a single-line field");

    // A multi-line field takes a modified Enter for a paragraph break and leaves plain Enter alone.
    var brief = new TextField(multiline: true);
    True(brief.HandleKey(new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, true, false)),
        "Alt+Enter is consumed by a multi-line field");
    Equal("\n", brief.Value, "Alt+Enter inserts a newline");
    True(!brief.HandleKey(new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false)),
        "Plain Enter still belongs to the form");

    // Wrapping must cover every character exactly once, with no gaps and no overlaps.
    const string paragraph = "The brief is the prompt.\nEvery agent on the task reads it, so say what done looks like.";
    foreach (var width in new[] { 8, 17, 40, 200 })
    {
        var lines = TextField.Layout(paragraph, width);
        True(lines.All(line => Text.DisplayWidth(paragraph.Substring(line.Start, line.Length)) <= width),
            $"Wrapped line fits {width} columns");
        var rebuilt = string.Concat(lines.Select(line => paragraph.Substring(line.Start, line.Length)));
        Equal(paragraph.Replace("\n", string.Empty).Replace(" ", string.Empty),
            rebuilt.Replace(" ", string.Empty), $"Wrap at {width} loses no characters");
    }

    return Task.CompletedTask;
}

/// <summary>An empty workspace must tell a first-time operator what to do, not just show nothing.</summary>
static Task TestEmptyWorkspaceGuidesAsync()
{
    var frame = Scenes.Render("empty", 120, 34, colour: false);
    True(frame.Contains("Press N", StringComparison.Ordinal), "Empty workspace names the next key");
    True(frame.Contains("explained", StringComparison.Ordinal), "Empty workspace promises the explanation");

    // The log view is where an operator lands when something has failed. Absence of output is one
    // of several different situations, and saying which one applies is the whole point.
    var logs = Scenes.Render("logs", 100, 24, colour: false);
    True(logs.Contains("Verify", StringComparison.Ordinal), "The log view names the stage");
    True(logs.Contains("Every one must exit 0", StringComparison.Ordinal),
        "The log view says what the stage is for");
    True(Prose(logs).Contains("Press I to see the full record", StringComparison.Ordinal),
        "An absent log explains itself and names a next step");

    // And it must not tell the operator to open the view they are already looking at.
    True(!Prose(logs).Contains("Press L to read", StringComparison.Ordinal),
        "The log view does not point at itself");
    return Task.CompletedTask;
}

// ── Command catalog and the generated portal ────────────────────────────────────────────────────

/// <summary>
/// The catalog is the CLI's half of the explanation contract. Every documented command must name a
/// command the dispatcher actually has, must resolve however an operator spells it, and must point
/// at glossary terms that exist.
/// </summary>
static Task TestCommandCatalogAsync()
{
    // The dispatcher's top-level verbs. This list is the contract: adding a command to
    // CommandDispatcher without documenting it here and in the catalog fails this test.
    string[] dispatched =
    [
        "fknrtd", "init", "doctor", "dashboard", "status", "task", "run", "land", "agent", "message",
        "claim", "usage", "events", "config", "telemetry", "integration", "explain", "portal", "help",
        "version"
    ];

    var documented = CommandCatalog.All
        .Select(entry => entry.Name.Split(' ')[0])
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();

    foreach (var verb in dispatched)
    {
        True(documented.Contains(verb, StringComparer.OrdinalIgnoreCase),
            $"The catalog documents the '{verb}' command");
    }

    // Every subcommand the dispatcher switches on, group by group. Only the top-level verbs were
    // checked before, so a subcommand could be added — and two were — without an entry.
    // "ls" and "acknowledge" are aliases of list and ack and are deliberately not separate entries.
    var subcommands = new Dictionary<string, string[]>(StringComparer.Ordinal)
    {
        ["task"] = ["create", "new", "list", "show", "diff", "prompts", "run", "retry", "cancel",
                    "land", "cleanup"],
        ["agent"] = ["list", "new", "add", "enable", "disable", "remove"],
        ["message"] = ["send", "ack", "list"],
        ["claim"] = ["add", "renew", "release", "list"],
        ["usage"] = ["refresh", "set", "list"],
        ["config"] = ["path", "show", "validate"]
    };

    foreach (var (group, names) in subcommands)
    {
        foreach (var name in names)
        {
            True(CommandCatalog.Find($"{group} {name}") is not null,
                $"The catalog documents '{group} {name}'");
        }
    }

    foreach (var entry in CommandCatalog.All)
    {
        True(entry.Summary.Length > 20, $"Catalog summary is substantive for '{entry.Name}'");
        True(entry.Detail.Length > 60, $"Catalog detail is substantive for '{entry.Name}'");
        True(entry.WhatHappensNext.Length > 15, $"Catalog names a next step for '{entry.Name}'");
        True(entry.Invocation.StartsWith("fknrtd", StringComparison.Ordinal),
            $"Catalog invocation is runnable for '{entry.Name}'");

        // A command may only point at explanations that exist, or the portal links into nothing.
        foreach (var term in entry.GlossaryTerms)
        {
            True(Glossary.Find(term) is not null, $"'{entry.Name}' references glossary term '{term}'");
        }

        // However it is spelled, a command has to be findable.
        True(CommandCatalog.Find(entry.Name) is not null, $"Catalog finds '{entry.Name}' verbatim");
        True(CommandCatalog.Find(entry.Name.Replace(' ', '-')) is not null,
            $"Catalog finds '{entry.Name}' hyphenated");
        True(CommandCatalog.Find(entry.Name.ToUpperInvariant()) is not null,
            $"Catalog finds '{entry.Name}' case-insensitively");
    }

    True(CommandCatalog.Search("audit").Count > 0, "Catalog search finds something for 'audit'");
    True(CommandCatalog.Groups.Count > 2, "Catalog is grouped");
    return Task.CompletedTask;
}

/// <summary>
/// The portal is a single file an operator may open from a USB stick on a plane. It must carry
/// everything it needs, escape everything it is given, and render identically every time.
/// </summary>
static Task TestPortalAsync()
{
    var generatedAt = new DateTimeOffset(2026, 9, 17, 10, 15, 0, TimeSpan.Zero);
    var html = PortalCommand.Render(generatedAt);

    // Deterministic: same input, same bytes. Documentation that churns on every run is unreviewable.
    Equal(html, PortalCommand.Render(generatedAt), "Portal render is deterministic");

    // Offline: the only permitted absolute URL is the SVG namespace, which is an identifier and not
    // a fetch. Anything else would make the guide fail exactly when it is needed most.
    foreach (var reference in System.Text.RegularExpressions.Regex
                 .Matches(html, @"(?:src|href)\s*=\s*[""']?(https?:)?//[^""'\s>]+")
                 .Select(match => match.Value))
    {
        True(false, "Portal references something off the page: " + reference);
    }

    True(html.Contains("<!doctype html>", StringComparison.OrdinalIgnoreCase), "Portal is a document");
    True(html.Contains("prefers-color-scheme", StringComparison.Ordinal), "Portal has a light mode");
    True(html.Contains("width=device-width", StringComparison.Ordinal), "Portal is sized for a phone");
    True(html.Contains("<svg", StringComparison.Ordinal), "Portal draws the pipeline");

    // Every term and every command reaches the page.
    foreach (var entry in Glossary.All)
    {
        True(html.Contains(Escape(entry.Title), StringComparison.Ordinal),
            $"Portal includes glossary title '{entry.Title}'");
    }

    foreach (var command in CommandCatalog.All)
    {
        True(html.Contains(Escape(command.Name), StringComparison.Ordinal),
            $"Portal includes command '{command.Name}'");
    }

    foreach (var binding in Keymap.All)
    {
        True(html.Contains(Escape(binding.Action), StringComparison.Ordinal),
            $"Portal includes key '{binding.Key}'");
    }

    foreach (var setting in SettingsCatalog.All)
    {
        True(html.Contains(Escape(setting.Key), StringComparison.Ordinal),
            $"Portal includes setting '{setting.Key}'");
    }

    // The build log explains why the pieces fit together the way they do, which a list of features
    // cannot. Every entry has to reach the page, and every entry has to actually say something.
    foreach (var milestone in Milestones.All)
    {
        True(html.Contains(Escape(milestone.Title), StringComparison.Ordinal),
            $"Portal includes milestone '{milestone.Title}'");
        True(milestone.Problem.Length > 60, $"Milestone '{milestone.Title}' states a real problem");
        True(milestone.Why.Length > 60, $"Milestone '{milestone.Title}' gives its reasoning");
        True(System.DateOnly.TryParse(milestone.Date, System.Globalization.CultureInfo.InvariantCulture,
            out _), $"Milestone '{milestone.Title}' has an ISO date");
    }

    // Hostile content in any model field has to arrive as text, never as markup.
    const string hostile = "</script><img src=x onerror=alert(1)>\"'&";
    var attacked = PortalWriter.Render(new PortalModel(
        [new GlossaryEntry(hostile, hostile, hostile, hostile, hostile, hostile)],
        [new CommandEntry(hostile, hostile, hostile, hostile, hostile, hostile,
            [new CommandOption(hostile, hostile, hostile)], [hostile], [])],
        [new KeyBinding(hostile, hostile, hostile)],
        hostile,
        generatedAt,
        [new Milestone(hostile, hostile, hostile, hostile, hostile)],
        [new SettingEntry(hostile, hostile, hostile, hostile, hostile, hostile, hostile, "brief")]));
    // The property that matters is that nothing supplied can become an element or an attribute.
    // The characters of the payload still appear — as visible text, which is the correct outcome.
    True(!attacked.Contains("<img", StringComparison.Ordinal), "Portal never emits an injected element");
    True(attacked.Contains("&lt;img src=x onerror=alert(1)&gt;", StringComparison.Ordinal),
        "Portal renders injected markup as text");
    Equal(1, System.Text.RegularExpressions.Regex.Matches(attacked, "</script>").Count,
        "Portal escapes an injected script terminator");

    // A guide whose own navigation is broken is worse than no guide: every internal link has to
    // point at an element that exists, and the structural tags have to balance.
    var identifiers = System.Text.RegularExpressions.Regex.Matches(html, """\bid=(?:"([^"]+)"|([A-Za-z0-9_-]+))""")
        .Select(match => match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value)
        .ToHashSet(StringComparer.Ordinal);
    foreach (var link in System.Text.RegularExpressions.Regex
                 .Matches(html, "href=(?:\"#([^\"]+)\"|#([A-Za-z0-9_-]+))")
                 .Select(match => match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value))
    {
        True(identifiers.Contains(link), $"Portal link #{link} points at an element that exists");
    }

    foreach (var tag in new[] { "section", "article", "div", "main", "aside", "nav", "style", "script", "svg" })
    {
        Equal(
            System.Text.RegularExpressions.Regex.Matches(html, $@"<{tag}\b").Count,
            System.Text.RegularExpressions.Regex.Matches(html, $"</{tag}>").Count,
            $"Portal balances its <{tag}> elements");
    }

    // Empty input must still produce a valid page rather than throwing.
    var empty = PortalWriter.Render(new PortalModel([], [], [], "0.0.0", generatedAt));
    True(empty.Contains("</html>", StringComparison.Ordinal), "Portal renders with nothing to say");
    return Task.CompletedTask;
}

static string Escape(string value) => System.Net.WebUtility.HtmlEncode(value);

/// <summary>
/// A mistyped command is the most common thing a new operator does. It has to be told which command
/// it meant, and it must not be sent off to fix a workspace that was never the problem.
/// </summary>
static async Task TestMistypedCommandAsync()
{
    // Transposition is the typo people actually make, so it must cost one edit rather than two.
    Equal(1, HelpCommand.Distance("taks", "task"), "Transposition is one edit");
    Equal(1, HelpCommand.Distance("lnad", "land"), "Leading transposition is one edit");
    Equal(0, HelpCommand.Distance("TASK", "task"), "Distance ignores case");
    Equal(4, HelpCommand.Distance("", "task"), "Distance from nothing is the length");

    foreach (var (typed, expected) in new[]
             {
                 ("taks", "task"), ("lnad", "land"), ("docter", "doctor"),
                 ("agnet", "agent"), ("explian", "explain"), ("prtal", "portal")
             })
    {
        var nearest = HelpCommand.Nearest(typed);
        True(nearest.Any(entry => entry.Name.StartsWith(expected, StringComparison.Ordinal)),
            $"'{typed}' suggests '{expected}'");
    }

    // Outside a workspace, a typo must still be reported as a typo. Before this was checked, the
    // runtime was built first and the operator was told no workspace existed.
    await WithTemporaryDirectoryAsync(async root =>
    {
        var original = Environment.CurrentDirectory;
        try
        {
            Environment.CurrentDirectory = root;
            Equal(2, await QuietlyAsync(["taks", "-no-color"]).ConfigureAwait(false),
                "A mistyped command exits 2 without a workspace");
        }
        finally
        {
            Environment.CurrentDirectory = original;
        }
    }).ConfigureAwait(false);
}

/// <summary>
/// Help and explain are the surfaces an operator reaches for when they are already stuck. Every
/// entry must render, and the overview must name the commands a first-time operator needs.
/// </summary>
static async Task TestHelpSurfacesAsync()
{
    var overview = new StringWriter();
    Equal(0, await QuietlyAsync(["help", "-no-color"], overview).ConfigureAwait(false), "help exits 0");
    var text = overview.ToString();
    foreach (var expected in new[] { "fknrtd doctor", "fknrtd task new", "fknrtd explain", "EXIT CODES" })
    {
        True(text.Contains(expected, StringComparison.Ordinal), $"The help overview names '{expected}'");
    }

    // Every exit code the product can return has to appear. 1 was missing, which is the one a script
    // is most likely to hit, because it is what an unhandled diagnostic becomes.
    foreach (var code in new[] { "  0 ", "  1 ", "  2 ", "  3 ", "  130 " })
    {
        True(text.Contains(code, StringComparison.Ordinal), $"The help overview documents exit code{code}");
    }

    // Every catalog entry has to render through the detail view without throwing.
    foreach (var entry in CommandCatalog.All)
    {
        var detail = new StringWriter();
        Equal(0, await QuietlyAsync(["help", .. entry.Name.Split(' '), "-no-color"], detail).ConfigureAwait(false),
            $"help renders '{entry.Name}'");
        True(detail.ToString().Contains("WHAT HAPPENS NEXT", StringComparison.Ordinal),
            $"help for '{entry.Name}' says what happens next");
    }

    // Every glossary entry has to render through explain.
    foreach (var entry in Glossary.All)
    {
        var explained = new StringWriter();
        Equal(0, await QuietlyAsync(["explain", entry.Term, "-no-color"], explained).ConfigureAwait(false),
            $"explain renders '{entry.Term}'");
        True(explained.ToString().Contains(entry.Detail[..40], StringComparison.Ordinal),
            $"explain prints the detail for '{entry.Term}'");
    }

    // Asking help about a concept rather than a command redirects instead of refusing.
    var concept = new StringWriter();
    Equal(0, await QuietlyAsync(["help", "brief", "-no-color"], concept).ConfigureAwait(false),
        "help redirects a concept");
    True(concept.ToString().Contains("fknrtd explain brief", StringComparison.Ordinal),
        "help points a concept at explain");
}

/// <summary>
/// An action that cannot be taken has to say why. Silently doing nothing when a key is pressed is
/// how an operator concludes the tool is broken rather than that the task is not ready.
/// </summary>
static Task TestPaletteExplainsRefusalsAsync()
{
    var snapshot = Scenes.PopulatedSnapshot();
    var ready = snapshot.Tasks.First(task => task.Status == WorkflowStatus.ReadyToLand);
    var running = snapshot.Tasks.First(task => task.Status == WorkflowStatus.Running);
    var renderer = new DashboardApp(null!, null!, null!, null!, null!, null!, null!, null!, null!);

    // With nothing selected, an action that needs a task is listed rather than hidden, marked so
    // that it reads as unavailable even with colour off.
    var empty = new Palette(() => Palette.Build(snapshot, selected: null, running: 0));
    var frame = renderer.Render(Scenes.EmptySnapshot(), 110, 40, useColor: false, empty);
    True(frame.Contains("(not now)", StringComparison.Ordinal),
        "The palette marks an unavailable action without relying on colour");

    // The palette opens on something that can be done, so the first Enter is never a no-op.
    True(empty.Actions[empty.Actions.ToList().FindIndex(action => action.Unavailable is null)].Unavailable is null,
        "The palette has something available to start on");

    // Selecting one that cannot be done explains what is missing, and Enter leaves that on screen.
    Scenes.Type(empty, "run");
    frame = renderer.Render(Scenes.EmptySnapshot(), 110, 40, useColor: false, empty);
    True(frame.Contains("no task is selected", StringComparison.Ordinal),
        "The palette says why an action needs a task");
    Equal(OverlayResult.Continue, Scenes.Press(empty, ConsoleKey.Enter), "An unavailable action does not run");
    True(empty.Chosen is null, "An unavailable action is never chosen");

    // A running task cannot be landed, and the palette says so in those words. The action is found
    // by its identifier rather than by filtering: more than one action's description can legitimately
    // mention landing, and row order is not what is under test here.
    var mid = new Palette(() => Palette.Build(snapshot, running, running: 1));
    True(mid.Actions.Single(action => action.Id == "G").Unavailable?
            .Contains("verified, audited", StringComparison.Ordinal) == true,
        "The palette explains why a running task cannot land");

    // Selecting that action and pressing Enter must not submit; the refusal stays on screen.
    Scenes.Type(mid, "Land");
    Equal(OverlayResult.Continue, Scenes.Press(mid, ConsoleKey.Enter),
        "Landing a running task is refused");
    True(mid.Chosen is null, "A refused action is never chosen");

    // The same action on a ready task is available and returns the identifier the dashboard routes on.
    var landable = new Palette(() => Palette.Build(snapshot, ready, running: 0));
    Scenes.Type(landable, "land");
    Equal(OverlayResult.Submit, Scenes.Press(landable, ConsoleKey.Enter), "Landing a ready task is offered");
    Equal("G", landable.Chosen?.Id, "The palette returns the key the action is bound to");

    // An action the code would refuse must be refused by the palette too, with the reason. Offering
    // one that throws the moment it is taken is worse than not offering it.
    var cancelled = snapshot.Tasks.First(task => task.Status == WorkflowStatus.Failed) with
    {
        Status = WorkflowStatus.Cancelled
    };
    var afterCancel = Palette.Build(snapshot, cancelled, running: 0);
    True(afterCancel.Single(action => action.Id == "Enter").Unavailable?.Contains("cancelled",
            StringComparison.Ordinal) == true,
        "Running a cancelled task is refused, and says to reset it first");

    // And with no enabled agents there is nobody to give work to, so the builder says so up front.
    var agentless = snapshot with
    {
        Config = snapshot.Config with
        {
            Agents = snapshot.Config.Agents.Select(agent => agent with { Enabled = false }).ToList()
        }
    };
    True(Palette.Build(agentless, ready, running: 0).Single(action => action.Id == "N").Unavailable is not null,
        "Creating a task is refused when no agent is enabled");

    // Every palette action must correspond to a documented key, or the palette could offer
    // something the help reference has never heard of.
    foreach (var action in new Palette(() => Palette.Build(snapshot, ready, running: 0)).Actions)
    {
        True(Keymap.All.Any(binding => binding.Key == action.Id),
            $"Palette action '{action.Id}' is a documented key");
    }

    return Task.CompletedTask;
}

/// <summary>
/// Scrolling back through a log has to land on the right lines and report the right position. This
/// is the screen an operator reads when something has failed, and a window that is off by a line
/// sends them to the wrong place in the output.
/// </summary>
static Task TestLogScrollbackAsync()
{
    var log = string.Join("\n", Enumerable.Range(1, 100).Select(number => $"line {number}"));

    // Following the tail shows the last lines and reports the file's real length.
    var (tail, total) = DashboardApp.ReadWindow(new StringReader(log), skipFromEnd: 0, count: 10);
    Equal(100, total, "Window reports the whole file's length");
    Equal(10, tail.Length, "Tail window size");
    Equal("line 91", tail[0], "Tail window first line");
    Equal("line 100", tail[^1], "Tail window last line");

    // Scrolling back ten lines moves the window by exactly ten.
    var (back, _) = DashboardApp.ReadWindow(new StringReader(log), skipFromEnd: 10, count: 10);
    Equal("line 81", back[0], "Scrolled window first line");
    Equal("line 90", back[^1], "Scrolled window last line");

    // Scrolling past the top clamps to the first line rather than emptying the view.
    var (top, _) = DashboardApp.ReadWindow(new StringReader(log), skipFromEnd: int.MaxValue / 2, count: 10);
    Equal("line 1", top[0], "Scrolling past the top clamps to the first line");
    Equal(10, top.Length, "Clamped window is still full");

    // A window larger than the file shows the whole file, not a padded one.
    var (all, allTotal) = DashboardApp.ReadWindow(new StringReader("only\nthree\nlines"), 0, 50);
    Equal(3, allTotal, "Short file line count");
    Equal(3, all.Length, "Short file window");

    // Degenerate inputs return nothing rather than throwing.
    Equal(0, DashboardApp.ReadWindow(new StringReader(log), 0, 0).Lines.Length, "Zero-height window");
    Equal(0, DashboardApp.ReadWindow(new StringReader(string.Empty), 0, 10).Lines.Length, "Empty file");
    return Task.CompletedTask;
}

/// <summary>
/// Regressions for the findings of an independent read-only review of the overlay layer. Each was a
/// real defect: none showed up in a screenshot, and all of them would have been reported as the
/// dashboard behaving oddly rather than as a bug anyone could name.
/// </summary>
static Task TestOverlayReviewRegressionsAsync()
{
    var snapshot = Scenes.PopulatedSnapshot();
    var ready = snapshot.Tasks.First(task => task.Status == WorkflowStatus.ReadyToLand);
    var running = snapshot.Tasks.First(task => task.Status == WorkflowStatus.Running);

    // A palette left open while a task finishes must stop refusing to land it. Availability is read
    // from a live source rather than frozen when the overlay opened.
    var selected = running;
    var live = new Palette(() => Palette.Build(snapshot, selected, running: 0));
    True(live.Actions.Single(action => action.Id == "G").Unavailable is not null,
        "Landing a running task is refused");
    selected = ready;
    True(live.Actions.Single(action => action.Id == "G").Unavailable is null,
        "The same palette offers landing once the task is ready");

    // Moving the caret inside the filter is consumed by the field but must not discard a selection
    // the operator made deliberately with the arrow keys.
    var palette = new Palette(() => Palette.Build(snapshot, ready, running: 0));
    Scenes.Press(palette, ConsoleKey.DownArrow);
    Scenes.Press(palette, ConsoleKey.DownArrow);
    Scenes.Press(palette, ConsoleKey.Home);
    Scenes.Press(palette, ConsoleKey.End);
    Scenes.Press(palette, ConsoleKey.Enter);
    True(palette.Chosen is not null, "Caret movement in the filter keeps the highlight");
    True(palette.Chosen!.Id != palette.Actions.First(action => action.Unavailable is null).Id,
        "Caret movement does not reset to the first action");

    var picker = Picker.Tasks(snapshot);
    Scenes.Press(picker, ConsoleKey.DownArrow);
    Scenes.Press(picker, ConsoleKey.Home);
    Scenes.Press(picker, ConsoleKey.Enter);
    Equal(snapshot.Tasks[1].Id, picker.Chosen, "Caret movement in the picker keeps the highlight");

    // Escape must back out of a form even when no step applies, rather than reporting success.
    var vacuous = new Wizard("EMPTY", Theme.Blue, new List<WizardStep>
    {
        new()
        {
            Key = "never",
            Question = "Does not apply",
            GlossaryTerm = "task",
            Applies = _ => false
        }
    });
    Equal(OverlayResult.Cancel, Scenes.Press(vacuous, ConsoleKey.Escape),
        "Escape cancels a form with no applicable steps");

    // Wrapping must terminate and must consume the whole string, even when a single glyph is wider
    // than the field it is being drawn into. Before this, a two-column character in a one-column
    // space appended empty lines until the process ran out of memory.
    foreach (var value in new[] { "界", "😀", "a界b", "界界界", "ab😀cd" })
    {
        foreach (var width in new[] { 1, 2, 3 })
        {
            var laid = TextField.Layout(value, width);
            True(laid.Count <= value.Length + 2, $"Wrapping '{value}' at {width} terminates");
            Equal(value.Length, laid.Sum(line => line.Length) + CountBreaks(value),
                $"Wrapping '{value}' at {width} consumes every character");
        }
    }

    // Wrapping must not split a surrogate pair: half an emoji renders as a replacement character.
    const string emoji = "ab\U0001F600cd";
    foreach (var width in new[] { 1, 2, 3, 4, 5, 8 })
    {
        foreach (var line in TextField.Layout(emoji, width))
        {
            var text = emoji.Substring(line.Start, line.Length);
            True(text.Length == 0 || !char.IsLowSurrogate(text[0]),
                $"Wrap at {width} does not start a line on a low surrogate");
            True(text.Length == 0 || !char.IsHighSurrogate(text[^1]),
                $"Wrap at {width} does not end a line on a high surrogate");
        }
    }

    // A panel may never be larger than the area it is centred in, at any terminal size.
    foreach (var width in new[] { 20, 40, 60, 80, 200 })
    {
        foreach (var height in new[] { 4, 8, 12, 20, 60 })
        {
            var area = new Rect(0, 0, width, height);
            var panel = Overlays.Centre(area, 104, 40);
            True(panel.Width <= width && panel.Height <= height,
                $"A centred panel fits inside {width}x{height}");
            True(panel.X >= 0 && panel.Y >= 0, $"A centred panel starts inside {width}x{height}");
        }
    }

    // And drawing into a small area must render rather than throw.
    foreach (var overlay in new IOverlay[]
             {
                 new Palette(() => Palette.Build(snapshot, ready, running: 0)),
                 Picker.Tasks(snapshot),
                 Reference.Agents(snapshot.Config),
                 TaskWizard.Create(snapshot.Config)
             })
    {
        foreach (var height in new[] { 4, 6, 8, 9, 10, 11 })
        {
            var canvas = new Canvas(80, height);
            overlay.Draw(canvas, new Rect(0, 0, 80, height));
            Equal(height, FrameLines(canvas.Render(false)).Length, $"Overlay draws into an 80x{height} area");
        }
    }

    return Task.CompletedTask;
}

/// <summary>Newlines are consumed by the wrap rather than kept in a span, so they are counted back.</summary>
static int CountBreaks(string value) => value.Count(character => character == (char)10);

/// <summary>
/// The dashboard and the command line must agree about what to do next, and must each say it in
/// terms of the surface being used. "Press Enter" is no use in a shell, and naming a shell command
/// is no use with the dashboard already open.
/// </summary>
static Task TestNextStepIsSurfaceAwareAsync()
{
    var config = Scenes.SampleConfig();
    foreach (var task in Scenes.PopulatedSnapshot().Tasks)
    {
        var onDashboard = Reference.NextStep(task, config);
        var inShell = Reference.NextStep(task, config, onDashboard: false);
        True(onDashboard.Length > 30, $"The dashboard has advice for a {task.Status} task");
        True(inShell.Length > 30, $"The shell has advice for a {task.Status} task");

        // The shell is never told to press a dashboard key.
        True(!inShell.Contains("Press ", StringComparison.Ordinal),
            $"Shell advice for a {task.Status} task names no keystroke");

        // And whatever command it does name has to be a real one.
        foreach (var quoted in System.Text.RegularExpressions.Regex.Matches(inShell, "'(fknrtd [^']+)'")
                     .Select(match => match.Groups[1].Value))
        {
            var name = string.Join(' ', quoted.Split(' ').Skip(1).TakeWhile(part => !part.StartsWith('-') &&
                !part.StartsWith("FKN-", StringComparison.Ordinal)));
            True(CommandCatalog.Find(name) is not null,
                $"Shell advice for a {task.Status} task names the real command '{name}'");
        }
    }

    return Task.CompletedTask;
}

/// <summary>
/// Every surface in this product tells the operator to read the diff before landing. The view that
/// finally shows it has to keep a diff readable: lines rendered verbatim rather than wrapped, and a
/// search that keeps the file and hunk headers a match belongs to.
/// </summary>
static Task TestDiffViewAsync()
{
    var config = Scenes.SampleConfig();
    var task = Scenes.PopulatedSnapshot().Tasks.First(item => item.Status == WorkflowStatus.Failed);
    var renderer = new DashboardApp(null!, null!, null!, null!, null!, null!, null!, null!, null!);

    // A diff line must survive verbatim: a wrap that moves a leading + or - off the start of the
    // row turns an addition into a removal at a glance.
    var frame = Scenes.Render("diff", 104, 30, colour: false);
    True(frame.Contains("+        var local = TimeZoneInfo.ConvertTime", StringComparison.Ordinal),
        "An added line is rendered with its marker at the start of the row");
    True(frame.Contains("-        var local = after.ToLocalTime();", StringComparison.Ordinal),
        "A removed line is rendered with its marker at the start of the row");

    // Filtering keeps the file and hunk a match came from, or the operator is reading a line with
    // no idea which file it is in.
    var filtered = Reference.Diff(task, config, Scenes.SampleDiff(), truncated: false);
    Scenes.Type(filtered, "ScheduleTests");
    frame = renderer.Render(Scenes.PopulatedSnapshot(), 104, 30, useColor: false, filtered);
    True(frame.Contains("tests/Reports/ScheduleTests.cs", StringComparison.Ordinal),
        "A filtered diff keeps the file the match came from");
    True(!frame.Contains("after.ToLocalTime", StringComparison.Ordinal),
        "A filtered diff drops the files that do not match");

    // Git puts the enclosing function in the hunk header, so a method name often appears only
    // there. Searching for one has to find the hunk it names rather than nothing.
    // "public sealed class Schedule" appears only in the hunk header, nowhere in the body.
    var byFunction = Reference.Diff(task, config, Scenes.SampleDiff(), truncated: false);
    Scenes.Type(byFunction, "public sealed class Schedule");
    frame = renderer.Render(Scenes.PopulatedSnapshot(), 104, 34, useColor: false, byFunction);
    True(frame.Contains("public sealed class Schedule", StringComparison.Ordinal),
        "A match in a hunk header keeps that hunk");
    True(frame.Contains("TimeZoneInfo.ConvertTime", StringComparison.Ordinal),
        "A match in a hunk header keeps every line under it");
    True(!frame.Contains("NextRunUsesTheWorkspaceZone", StringComparison.Ordinal),
        "A match in one hunk header does not keep the other file");

    // A match on a body line keeps that line with its headers, not the whole hunk around it.
    var byLine = Reference.Diff(task, config, Scenes.SampleDiff(), truncated: false);
    Scenes.Type(byLine, "ToLocalTime");
    frame = renderer.Render(Scenes.PopulatedSnapshot(), 104, 34, useColor: false, byLine);
    True(frame.Contains("-        var local = after.ToLocalTime();", StringComparison.Ordinal),
        "A matching line is kept");
    True(!frame.Contains("TimeZoneInfo.ConvertTime", StringComparison.Ordinal),
        "A non-matching line in the same hunk is not");

    // The two states that are not a diff each explain themselves.
    True(Scenes.Render("diff-empty", 104, 24, colour: false)
            .Contains("Nothing has changed against", StringComparison.Ordinal),
        "An empty diff says so, and says what that might mean");
    True(Scenes.Render("diff-standalone", 104, 24, colour: false)
            .Contains("no branch to compare against", StringComparison.Ordinal),
        "A standalone workspace explains why there is no diff");

    // A change too large to show says so rather than quietly ending.
    var big = Reference.Diff(task, config, Scenes.SampleDiff(), truncated: true);
    frame = renderer.Render(Scenes.PopulatedSnapshot(), 104, 40, useColor: false, big);
    True(frame.Contains("larger than this view will hold", StringComparison.Ordinal),
        "A truncated diff says it was cut off");
    return Task.CompletedTask;
}

/// <summary>
/// Doctor's contract is that it reports rather than throws. A workspace whose configuration cannot
/// be read is exactly when an operator needs it most, and exactly when a check that assumes the
/// configuration loaded would take the whole diagnostic down with it.
/// </summary>
static async Task TestDoctorSurvivesABrokenWorkspaceAsync()
{
    await WithTemporaryDirectoryAsync(async root =>
    {
        Directory.CreateDirectory(Path.Combine(root, ".fknrtd"));
        await File.WriteAllTextAsync(Path.Combine(root, ".fknrtd", "config.json"), "not json at all")
            .ConfigureAwait(false);

        var checks = await new DoctorService(
                new StateStore(WorkspaceLocator.ForRoot(root)),
                new GitService(new ProcessRunner()),
                new ProcessRunner())
            .RunAsync()
            .ConfigureAwait(false);

        True(checks.Count > 5, "Doctor reports every check even with an unreadable configuration");
        True(checks.Any(check => check.Name == "Configuration" && !check.Passed),
            "Doctor reports the unreadable configuration as a failure");
        True(checks.Any(check => check.Name == "An agent can audit"),
            "Doctor still reaches the auditor check");
        True(checks.Any(check => check.Name == "Work gets verified"),
            "Doctor still reaches the verification check");
        True(checks.All(check => check.Detail.Length > 0), "Every check carries a detail");
    }).ConfigureAwait(false);

    // Every task status has a marker the renderer can draw, a glossary entry, and next-step advice
    // on both surfaces. A status added without one of those is a task nobody can read.
    var config = Scenes.SampleConfig();
    foreach (var status in Enum.GetValues<WorkflowStatus>())
    {
        var task = Scenes.PopulatedSnapshot().Tasks[0] with { Status = status };
        True(Glossary.Find("status." + status.ToString().ToLowerInvariant()) is not null,
            $"Status {status} is in the glossary");
        True(Reference.NextStep(task, config).Length > 25, $"Status {status} has dashboard advice");
        True(Reference.NextStep(task, config, onDashboard: false).Length > 25,
            $"Status {status} has shell advice");
    }

    // The sample workspace carries one task in each status, so the picker — which spells the status
    // out in words rather than drawing a marker — has to name every one of them.
    var listed = Scenes.Render("find", 120, 40, colour: false);
    foreach (var status in Enum.GetValues<WorkflowStatus>())
    {
        var title = Glossary.Find("status." + status.ToString().ToLowerInvariant())!.Title;
        True(listed.Contains(title, StringComparison.Ordinal),
            $"The task picker names the {status} status as '{title}'");
    }

    // And in a healthy workspace the two new checks answer the questions they exist for.
    await WithTemporaryDirectoryAsync(async root =>
    {
        var paths = WorkspaceLocator.ForRoot(root);
        var store = new StateStore(paths);
        await store.InitializeAsync(new FknrtdConfig
        {
            ProjectName = "doctor-test",
            Mode = WorkspaceMode.Standalone,
            DefaultVerificationCommands = ["dotnet build"],
            Agents = BuiltInAgents.CreateDefaults().ToList()
        }).ConfigureAwait(false);

        var checks = await new DoctorService(store, new GitService(new ProcessRunner()), new ProcessRunner())
            .RunAsync()
            .ConfigureAwait(false);
        True(checks.Single(check => check.Name == "An agent can audit").Passed,
            "The built-in agents can audit");
        True(checks.Single(check => check.Name == "Work gets verified").Passed,
            "Configured verification commands satisfy the verification check");
        True(!checks.Single(check => check.Name == "Work gets verified").Required,
            "The verification check is advisory, not required");
    }).ConfigureAwait(false);
}

/// <summary>
/// The prompt preview has to show the text an agent will actually receive, not a paraphrase of it.
/// The whole value of the surface is that it is the same string the orchestrator sends.
/// </summary>
static Task TestPromptPreviewAsync()
{
    var config = Scenes.SampleConfig();
    var task = Scenes.PopulatedSnapshot().Tasks.First(item => item.Status == WorkflowStatus.Failed);

    // Every prompt carries the brief. An agent that is not sent the brief cannot do the task, and a
    // preview that omits it would be reassuring about the wrong thing.
    foreach (var prompt in new[]
             {
                 AgentPrompts.Plan(task),
                 AgentPrompts.Implement(task, config.Mode, "a plan", string.Empty),
                 AgentPrompts.Audit(task, config.Mode, "all commands passed")
             })
    {
        True(prompt.Contains(task.Brief, StringComparison.Ordinal), "Every prompt carries the brief verbatim");
        True(prompt.Contains(task.Id, StringComparison.Ordinal), "Every prompt names the task");
    }

    // The two read-only roles say so, and only the implementer is told it may write.
    True(AgentPrompts.Plan(task).Contains("read-only", StringComparison.OrdinalIgnoreCase),
        "The lead is told it is read-only");
    True(AgentPrompts.Audit(task, config.Mode, "x").Contains("Do not edit any file", StringComparison.Ordinal),
        "The auditor is told not to edit");
    True(AgentPrompts.Audit(task, config.Mode, "x").Contains("FKNRTD_VERDICT: PASS", StringComparison.Ordinal),
        "The auditor is told the verdict markers");

    // Isolation is stated in terms of the mode the workspace is actually in.
    True(AgentPrompts.Isolation(task, WorkspaceMode.Git)
            .Contains("isolated Git worktree", StringComparison.Ordinal),
        "A Git task is told it is isolated");
    True(AgentPrompts.Isolation(task, WorkspaceMode.Standalone)
            .Contains("no Git isolation", StringComparison.Ordinal),
        "A standalone task is told it is not");

    // Previewed before the worktree exists, an unset branch must still read as a sentence.
    var unstarted = task with { BranchName = string.Empty, WorktreePath = string.Empty };
    True(!AgentPrompts.Isolation(unstarted, WorkspaceMode.Git).Contains("branch .", StringComparison.Ordinal),
        "An unset branch does not render as an empty name");

    // And the panel shows the real text rather than a summary of it.
    var frame = Scenes.Render("prompts", 110, 44, colour: false);
    True(frame.Contains("You are the lead developer", StringComparison.Ordinal),
        "The preview shows the lead's actual instruction");
    True(frame.Contains("Nothing else is sent", StringComparison.Ordinal),
        "The preview says this is all that is sent");
    return Task.CompletedTask;
}

/// <summary>
/// The statusline is the surface a Claude Code user looks at constantly, and it renders the same
/// state the dashboard does. The two must not describe the same agent differently.
/// </summary>
static async Task TestStatusLineAgreesWithTheDashboardAsync()
{
    await WithTemporaryDirectoryAsync(async root =>
    {
        var paths = WorkspaceLocator.ForRoot(root);
        var store = new StateStore(paths);
        await store.InitializeAsync(new FknrtdConfig
        {
            ProjectName = "statusline-test",
            Mode = WorkspaceMode.Standalone,
            Agents = BuiltInAgents.CreateDefaults().ToList()
        }).ConfigureAwait(false);

        var payload = new StringWriter();
        var original = Console.In;
        try
        {
            Console.SetIn(new StringReader(
                "{\"workspace\":{\"current_dir\":" +
                System.Text.Json.JsonSerializer.Serialize(root) +
                "},\"context_window\":{\"used_percentage\":38}}"));
            Equal(0, await QuietlyAsync(["telemetry", "claude-statusline", "-no-color"], payload)
                    .ConfigureAwait(false),
                "The statusline renders");
        }
        finally
        {
            Console.SetIn(original);
        }

        var line = payload.ToString();
        True(line.Contains("FKN", StringComparison.Ordinal), "The statusline carries its badge");

        // An agent that has never reported is offline. "? idle" said unknown and idle at once, and
        // disagreed with the dashboard, which draws the same agent as a plain circle.
        True(!line.Contains("?", StringComparison.Ordinal),
            "The statusline never shows an unexplained state glyph");
        True(!line.Contains("  ", StringComparison.Ordinal), "The statusline has no doubled spaces");
        True(!line.Contains((char)27), "The statusline honours -no-color");
    }).ConfigureAwait(false);
}

/// <summary>
/// A key that fails must become a message, never an exit. Several actions read files, query Git or
/// launch a process, and any of those can fail for reasons that have nothing to do with the
/// operator. A dashboard that exits to a stack trace because a log was briefly locked is one nobody
/// leaves running.
/// </summary>
static async Task TestAFailingActionDoesNotCrashAsync()
{
    // Every service is null, so any action that reaches one throws immediately — which is exactly
    // the condition under test.
    var app = new DashboardApp(null!, null!, null!, null!, null!, null!, null!, null!, null!);
    var snapshot = Scenes.PopulatedSnapshot();

    foreach (var key in new[]
             {
                 ConsoleKey.D,      // runs the pre-flight checks
                 ConsoleKey.E,      // reads the event log
                 ConsoleKey.U,      // asks Codex for its budget
                 ConsoleKey.C,      // requests cancellation
                 ConsoleKey.R,      // resets failed stages
                 ConsoleKey.V,      // reads the finished change
                 ConsoleKey.P       // reads the plan artifact
             })
    {
        await app.HandleKeyAsync(
                Key(key), snapshot, CancellationToken.None)
            .ConfigureAwait(false);
        True(app.Toast.Length > 0, $"Pressing {key} against a broken workspace leaves a message");
    }

    // And the frame still renders afterwards rather than being left in a half-drawn state.
    var frame = app.Render(snapshot, 110, 34, useColor: false);
    Equal(34, FrameLines(frame).Length, "The dashboard still renders after a failed action");
}

/// <summary>
/// The task-reading commands, exercised end to end against a real workspace. They were verified by
/// hand while they were written; this is what keeps them working.
/// </summary>
static async Task TestTaskReadingCommandsAsync()
{
    await WithTemporaryDirectoryAsync(async root =>
    {
        Equal(0, await QuietlyAsync(["init", "-root", root, "-yes"]).ConfigureAwait(false),
            "Setting up the workspace");

        var store = new StateStore(WorkspaceLocator.ForRoot(root));
        var config = await store.LoadConfigAsync().ConfigureAwait(false);
        var task = await new TaskService(store, new GitService(new ProcessRunner()))
            .CreateAsync(
                "Read-back test",
                "A brief long enough to be accepted, describing a finished state to aim at.",
                config.Agents[0].Id,
                config.Agents[^1].Id,
                config.Agents[0].Id,
                ["dotnet build"])
            .ConfigureAwait(false);

        // show explains rather than dumps, and phrases its advice for a shell.
        var shown = new StringWriter();
        Equal(0, await QuietlyAsync(["task", "show", task.Id, "-root", root], shown).ConfigureAwait(false),
            "task show exits 0");
        var text = shown.ToString();
        foreach (var expected in new[]
                 {
                     "WHAT WAS ASKED FOR", "WHO IS ON IT", "WHERE THE WORK HAPPENS",
                     "HOW CORRECTNESS IS DECIDED", "PIPELINE", "WHAT TO DO NEXT"
                 })
        {
            True(text.Contains(expected, StringComparison.Ordinal), $"task show includes '{expected}'");
        }

        True(text.Contains(task.Brief, StringComparison.Ordinal), "task show prints the brief");
        True(text.Contains("dotnet build", StringComparison.Ordinal), "task show prints the verification");
        True(text.Contains("fknrtd task run", StringComparison.Ordinal),
            "task show gives shell advice, not a keystroke");

        // prompts prints the real text, not a description of it.
        var prompts = new StringWriter();
        Equal(0, await QuietlyAsync(["task", "prompts", task.Id, "-root", root], prompts).ConfigureAwait(false),
            "task prompts exits 0");
        var sent = prompts.ToString();
        True(sent.Contains(AgentPrompts.Plan(task), StringComparison.Ordinal),
            "task prompts prints the lead's instruction verbatim");
        True(sent.Contains("FKNRTD_VERDICT: PASS", StringComparison.Ordinal),
            "task prompts shows the auditor its verdict markers");
        True(sent.Contains("Nothing else is sent", StringComparison.Ordinal),
            "task prompts says that is all that is sent");

        // This workspace is not a repository, so it was set up standalone — and diff has to say that
        // rather than print nothing or complain about a missing worktree it was never going to have.
        Equal(WorkspaceMode.Standalone, config.Mode, "A folder with no repository is standalone");
        var diff = new StringWriter();
        Equal(0, await QuietlyAsync(["task", "diff", task.Id, "-root", root], diff).ConfigureAwait(false),
            "task diff exits 0 in a standalone workspace");
        True(diff.ToString().Contains("standalone", StringComparison.OrdinalIgnoreCase),
            "task diff explains that a standalone workspace has nothing to compare against");

        // A task id that does not exist is reported as that, rather than as a damaged workspace.
        // The dispatcher lets the exception out; Program.cs is what turns it into an exit code, so
        // the message is what this asserts on.
        try
        {
            await QuietlyAsync(["task", "show", "FKN-nope", "-root", root]).ConfigureAwait(false);
            True(false, "An unknown task id is refused");
        }
        catch (FileNotFoundException exception)
        {
            True(exception.Message.Contains("no task 'FKN-nope'", StringComparison.Ordinal),
                "An unknown task id names the task rather than the file");
            True(exception.Message.Contains("fknrtd task list", StringComparison.Ordinal),
                "An unknown task id says how to find the real one");
        }
    }).ConfigureAwait(false);

    if (!GitService.IsInstalled())
    {
        return;
    }

    // And in a Git workspace, a task that has not reached its worktree stage says so instead.
    await WithTemporaryDirectoryAsync(async root =>
    {
        var git = new GitService(new ProcessRunner());
        MustSucceed(await git.GitAsync(root, ["init", "-b", "main"]).ConfigureAwait(false), "git init");
        await File.WriteAllTextAsync(Path.Combine(root, "a.txt"), "one").ConfigureAwait(false);
        MustSucceed(await git.GitAsync(root, ["add", "-A"]).ConfigureAwait(false), "git add");
        MustSucceed(
            await git.GitAsync(root,
                    ["-c", "user.email=a@b", "-c", "user.name=n", "commit", "-m", "init"])
                .ConfigureAwait(false),
            "git commit");

        Equal(0, await QuietlyAsync(["init", "-root", root, "-yes"]).ConfigureAwait(false),
            "Setting up a Git workspace");
        var store = new StateStore(WorkspaceLocator.ForRoot(root));
        var config = await store.LoadConfigAsync().ConfigureAwait(false);
        Equal(WorkspaceMode.Git, config.Mode, "A repository is set up Git-backed");

        var task = await new TaskService(store, git)
            .CreateAsync("Git read-back", "A brief long enough to be accepted by the form.",
                config.Agents[0].Id, config.Agents[^1].Id, config.Agents[0].Id, [])
            .ConfigureAwait(false);

        var diff = new StringWriter();
        Equal(0, await QuietlyAsync(["task", "diff", task.Id, "-root", root], diff).ConfigureAwait(false),
            "task diff exits 0 before the worktree stage");
        True(diff.ToString().Contains("worktree", StringComparison.OrdinalIgnoreCase),
            "task diff says the task has no worktree yet");

        // The prompt preview must not leave an empty branch name in the implementer's instruction.
        var prompts = new StringWriter();
        Equal(0, await QuietlyAsync(["task", "prompts", task.Id, "-root", root], prompts).ConfigureAwait(false),
            "task prompts exits 0 before the worktree stage");
        True(!prompts.ToString().Contains("branch .", StringComparison.Ordinal),
            "The prompt preview never shows an empty branch name");
    }).ConfigureAwait(false);
}

/// <summary>
/// The recorded event vocabulary, pinned. It used to be string literals in four files, and the
/// history's own description drifted twice — claiming per-stage events and agent check-ins that this
/// product does not record at all. A description can only be checked against a list that exists.
/// </summary>
static Task TestEventVocabularyAsync()
{
    // Including the events the scenes draw. The history screen was showing "stage.passed" and
    // "stage.failed", which this product has never recorded, so the one screen whose job is to
    // teach the vocabulary was teaching two words that are not in it.
    foreach (var recorded in Scenes.PopulatedSnapshot().Events)
    {
        True(EventTypes.All.Contains(recorded.Type),
            $"The history screen shows '{recorded.Type}', which is a type this product records");
    }

    // Nor may it suggest one. The empty history told the reader events are recorded "as stages run
    // and agents report in" - neither of which writes one - and the no-match hint suggested
    // searching for "stage.failed", a type that has never existed.
    foreach (var scene in new[] { "events", "events-empty", "empty" })
    {
        var prose = Prose(Scenes.Render(scene, 110, 40, colour: false));
        foreach (var invented in new[]
                 {
                     "stage.failed", "stage.passed", "stages run", "agents report in",
                     "agent check-in"
                 })
        {
            True(!prose.Contains(invented, StringComparison.Ordinal),
                $"The {scene} screen does not offer '{invented}', which this product never records");
        }
    }

    Equal(EventTypes.All.Count, EventTypes.All.Distinct(StringComparer.Ordinal).Count(),
        "Event types are unique");
    foreach (var type in EventTypes.All)
    {
        True(type.Contains('.', StringComparison.Ordinal), $"'{type}' is namespaced");
        True(type.ToLowerInvariant() == type, $"'{type}' is lower case");
    }

    // Nothing may record a type outside the vocabulary. A literal here is how an event becomes
    // silently unsearchable, because nothing else in the product spells it that way.
    foreach (var path in Directory.EnumerateFiles(
                 Path.Combine(RepositoryRoot(), "src"), "*.cs", SearchOption.AllDirectories))
    {
        if (path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
                StringComparison.Ordinal) ||
            path.EndsWith("EventTypes.cs", StringComparison.Ordinal))
        {
            continue;
        }

        foreach (var literal in System.Text.RegularExpressions.Regex
                     .Matches(File.ReadAllText(path), "Type = " + (char)34 + "([^" + (char)34 + "]+)" + (char)34)
                     .Select(match => match.Groups[1].Value))
        {
            True(false, $"{Path.GetFileName(path)} records the event type '{literal}' as a literal");
        }
    }

    return Task.CompletedTask;
}

/// <summary>Walks up to the repository root, so the test works from any build output location.</summary>
static string RepositoryRoot()
{
    var directory = new DirectoryInfo(AppContext.BaseDirectory);
    while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "FKNRTD.CLI.sln")))
    {
        directory = directory.Parent;
    }

    return directory?.FullName ?? Directory.GetCurrentDirectory();
}

/// <summary>
/// The standalone overlay host, which is what `fknrtd task new`, `agent new` and an interactive
/// `init` all draw into. It has no dashboard behind it, so its backdrop is the only thing telling
/// the operator what they are configuring — and none of it was covered.
/// </summary>
static Task TestOverlayHostFrameAsync()
{
    var config = Scenes.SampleConfig();
    foreach (var overlay in new IOverlay[]
             {
                 TaskWizard.Create(config),
                 AgentWizard.Create(config),
                 SetupWizard.Create(Scenes.SampleDetection())
             })
    {
        foreach (var width in new[] { 40, 60, 80, 120, 200 })
        {
            foreach (var height in new[] { 10, 20, 32, 60 })
            {
                var frame = OverlayHost.Frame(overlay, "aurora-api - a caption", width, height, useColor: false);
                var lines = FrameLines(frame);
                var expectedWidth = Math.Max(60, width);
                Equal(Math.Max(20, height), lines.Length, $"Host frame rows at {width}x{height}");
                True(lines.All(line => Text.DisplayWidth(line) == expectedWidth),
                    $"Host frame width at {width}x{height}");
            }
        }

        // The backdrop names the product and the caption, so a form opened from a bare shell still
        // says what it belongs to.
        var readable = OverlayHost.Frame(overlay, "aurora-api - a caption", 110, 34, useColor: false);
        True(readable.Contains("FKNRTD COMMAND CENTER", StringComparison.Ordinal),
            "The host frame names the product");
        True(readable.Contains("aurora-api - a caption", StringComparison.Ordinal),
            "The host frame carries its caption");
        True(readable.Contains("Esc", StringComparison.Ordinal),
            "The host frame says how to leave");
    }

    return Task.CompletedTask;
}

/// <summary>
/// The agent roster. Before it existed, A answered "why is this name not offered?" with a list and
/// no way to act on the answer; the operator had to leave the dashboard and remember a command.
/// </summary>
/// <summary>
/// A keystroke with no character behind it, which is what a bare function or arrow key delivers.
/// Spelling it out at every call site meant a literal NUL character sitting invisibly in the source.
/// </summary>
/// <summary>
/// A frame's prose, with the panel borders removed and the wrapping undone, so a test can look for
/// a sentence without caring where the renderer happened to break it. Searching a raw frame for a
/// phrase silently stops working the moment the phrase grows past the panel width.
/// </summary>
static string Prose(string frame)
{
    // Trimmed by character class rather than by a list. A list stops at the first character it does
    // not know, and the panel behind a modal leaves its own truncation ellipsis in the right margin
    // - so trimming stopped there and left a box character glued to the end of the sentence.
    static bool Decoration(char character) =>
        character is ' ' or '…' or '›' or '·' ||
        character is >= '─' and <= '╿';

    static string Strip(string line)
    {
        var start = 0;
        var end = line.Length;
        while (start < end && Decoration(line[start]))
        {
            start++;
        }

        while (end > start && Decoration(line[end - 1]))
        {
            end--;
        }

        return line[start..end];
    }

    // A modal is drawn over the frame, so a row can hold the panel behind it as well - the pipeline
    // panel's "Stages" label sits immediately left of the modal's border and would otherwise be
    // spliced into the middle of the modal's sentence. The heavy border marks where the modal
    // starts and ends, so when a row has one, only what is between them counts.
    static string Interior(string line)
    {
        var first = line.IndexOf('┃');
        var last = line.LastIndexOf('┃');
        return first >= 0 && last > first ? line[(first + 1)..last] : line;
    }

    var words = FrameLines(frame).Select(Interior).Select(Strip).Where(line => line.Length > 0);
    var joined = string.Join(' ', words);
    while (joined.Contains("  ", StringComparison.Ordinal))
    {
        joined = joined.Replace("  ", " ", StringComparison.Ordinal);
    }

    return joined;
}

static ConsoleKeyInfo Key(ConsoleKey key) => new((char)0, key, false, false, false);

static Task TestAgentManagerAsync()
{
    var snapshot = Scenes.PopulatedSnapshot();
    var manager = AgentManager.Create(snapshot);
    var frame = Scenes.Render("agents", 120, 40, colour: false);

    foreach (var agent in snapshot.Config.Agents)
    {
        True(frame.Contains(agent.Id, StringComparison.Ordinal), $"The roster lists {agent.Id}");
    }

    // The consequence paragraph is the one whose whole job is to say what a keystroke costs, so it
    // finishing its sentence is the point of measuring the panel rather than guessing at it.
    True(frame.Contains("IF YOU TURN IT OFF", StringComparison.Ordinal),
        "The roster says what the toggle would cost");
    True(frame.Contains("configured for it.", StringComparison.Ordinal),
        "The consequence paragraph reaches its final word");

    // The status columns line up, which is the only reason a roster beats reading config.json.
    var rows = FrameLines(frame)
        .Where(line => line.Contains("enabled", StringComparison.Ordinal) ||
                       line.Contains("disabled", StringComparison.Ordinal))
        .Where(line => line.Contains("audit", StringComparison.Ordinal))
        .ToArray();
    True(rows.Length >= 2, "More than one agent row is drawn");
    var columns = rows.Select(line => line.IndexOf("can", StringComparison.Ordinal)).Distinct().Count();
    Equal(1, columns, "The audit column starts at the same offset on every row");

    // Every key the footer offers produces the action it names.
    Equal(OverlayResult.Submit, manager.HandleKey(Key(ConsoleKey.Spacebar)), "Space submits");
    Equal(AgentAction.Toggle, manager.Action, "Space toggles");
    Equal(snapshot.Config.Agents[0].Id, manager.AgentId, "Space acts on the highlighted agent");

    manager = AgentManager.Create(snapshot);
    manager.HandleKey(Key(ConsoleKey.DownArrow));
    Equal(OverlayResult.Submit, manager.HandleKey(Key(ConsoleKey.Delete)), "Delete submits");
    Equal(AgentAction.Remove, manager.Action, "Delete removes");
    Equal(snapshot.Config.Agents[1].Id, manager.AgentId, "Delete acts on the highlighted agent");

    manager = AgentManager.Create(snapshot);
    Equal(OverlayResult.Submit, manager.HandleKey(Key(ConsoleKey.N)), "N submits");
    Equal(AgentAction.Add, manager.Action, "N adds");
    Equal(string.Empty, manager.AgentId, "Adding names no existing agent");

    manager = AgentManager.Create(snapshot);
    Equal(OverlayResult.Cancel, manager.HandleKey(Key(ConsoleKey.Escape)), "Escape leaves it alone");

    // Backspace used to remove an agent too. Backspace is the key people press to go back, and a
    // destructive confirmation behind it was a trap with nothing to recommend it.
    manager = AgentManager.Create(snapshot);
    Equal(OverlayResult.Continue, manager.HandleKey(Key(ConsoleKey.Backspace)),
        "Backspace does not start removing an agent");

    // F1 reaches the full per-agent reference. Without it that screen became unreachable the
    // moment A started opening the roster, and only this suite could still see it.
    // E repoints the highlighted agent. An executable that is not on PATH is the commonest fault
    // this screen reports, and until it had this key the only thing it could do was name a command
    // to go and type somewhere else.
    manager = AgentManager.Create(snapshot);
    Equal(OverlayResult.Submit, manager.HandleKey(Key(ConsoleKey.E)), "E submits");
    Equal(AgentAction.Repoint, manager.Action, "E changes what the agent runs");
    Equal(snapshot.Config.Agents[0].Id, manager.AgentId, "E acts on the highlighted agent");

    var missing = Prose(Scenes.Render("agents-nothing-installed", 110, 36, colour: false));
    True(missing.Contains("NOT on PATH", StringComparison.Ordinal),
        "A missing executable is reported");
    True(missing.Contains("press E to point this agent at a program that is there",
            StringComparison.Ordinal),
        "And the screen offers to fix it rather than naming a command to go and type");
    True(missing.Contains("E change its command", StringComparison.Ordinal),
        "The footer offers the key too");

    manager = AgentManager.Create(snapshot);
    Equal(OverlayResult.Submit, manager.HandleKey(Key(ConsoleKey.F1)), "F1 submits");
    Equal(AgentAction.Explain, manager.Action, "F1 asks for the full detail");
    True(frame.Contains("F1 full detail", StringComparison.Ordinal),
        "And the footer offers it");

    // An empty roster cannot toggle or remove anything, and must not claim it can.
    var empty = new AgentManager([], new Dictionary<string, int>());
    Equal(OverlayResult.Continue, empty.HandleKey(Key(ConsoleKey.Spacebar)),
        "Space does nothing with no agents");
    Equal(OverlayResult.Continue, empty.HandleKey(Key(ConsoleKey.Delete)),
        "Delete does nothing with no agents");
    Equal(OverlayResult.Submit, empty.HandleKey(Key(ConsoleKey.N)), "N still adds the first agent");
    var emptyFrame = Scenes.Render("agents-empty", 100, 30, colour: false);
    True(!emptyFrame.Contains("Del remove", StringComparison.Ordinal),
        "The empty roster does not offer a key that would do nothing");

    return Task.CompletedTask;
}

/// <summary>
/// The settings screen. It explained a file the operator then had to leave and edit by hand; the
/// point of the table is that every field it offers to change is one it can also explain.
/// </summary>
static async Task TestSettingsBrowserAsync()
{
    var config = Scenes.SampleConfig();

    // Every field this screen will change has an explanation, and every top-level field in the
    // catalog is either changeable or carries the reason it is not. Silence would read as an
    // oversight, and an editable field with no explanation is the exact defect this work exists
    // to remove.
    foreach (var setting in SettingsBrowser.Editable)
    {
        True(SettingsCatalog.Find(setting.Key) is not null,
            $"{setting.Key} can be changed and is explained");
        True(!SettingsBrowser.ReadOnly.ContainsKey(setting.Key),
            $"{setting.Key} is not both editable and read-only");
    }

    foreach (var entry in SettingsCatalog.All.Where(item => !item.Key.Contains('[') && !item.Key.Contains('.')))
    {
        True(SettingsBrowser.Editor(entry.Key) is not null || SettingsBrowser.ReadOnly.ContainsKey(entry.Key),
            $"{entry.Key} is either changeable or says why not");
    }

    // Reading a value and writing it straight back leaves the configuration alone, which is what
    // makes opening a field and pressing Enter safe.
    foreach (var setting in SettingsBrowser.Editable)
    {
        var unchanged = setting.Write(config, setting.Read(config));
        Equal(setting.Read(config), setting.Read(unchanged), $"{setting.Key} round-trips");
    }

    // Each validator rejects what the type cannot hold, rather than throwing on the write.
    foreach (var setting in SettingsBrowser.Editable.Where(item => item.Input == WizardInput.Number))
    {
        foreach (var bad in new[] { "", "-1", "not a number", "999999999999" })
        {
            True(setting.Validate?.Invoke(bad) is not null, $"{setting.Key} rejects '{bad}'");
        }
    }

    // The form for a field carries that field's own explanation, not the general term it belongs to.
    var form = SettingsBrowser.Form(config, "maxParallelAgents");
    True(form is not null, "A changeable field has a form");
    var frame = Scenes.Render("settings-number", 100, 30, colour: false);
    True(frame.Contains("IF YOU CHANGE IT", StringComparison.Ordinal),
        "The form says what changing it would cost");
    True(frame.Contains("rate-limit failures.", StringComparison.Ordinal),
        "That consequence reaches its final word");
    True(frame.Contains("Enter save", StringComparison.Ordinal),
        "The form says it will save rather than create");

    // A read-only field has no form at all, so nothing can route an edit to one by accident.
    foreach (var key in SettingsBrowser.ReadOnly.Keys)
    {
        True(SettingsBrowser.Form(config, key) is null, $"{key} has no editor");
    }

    await Task.CompletedTask;
}

/// <summary>
/// The coordination screen, and the one action it offers. An expired reservation is reported as
/// stale for as long as its record exists, and nothing deletes the record - so a panel that listed
/// them without a way to clear them was describing a problem it had decided not to solve.
/// </summary>
static Task TestCoordinationReleaseAsync()
{
    var snapshot = Scenes.PopulatedSnapshot();
    var expired = snapshot.Claims.Where(claim => claim.ExpiresAt <= snapshot.CapturedAt).ToArray();
    True(expired.Length > 0, "The sample workspace has a reservation to clear");

    var frame = Scenes.Render("coordination", 110, 44, colour: false);
    True(frame.Contains("R release", StringComparison.Ordinal),
        "The panel offers the key that clears them");
    True(Prose(frame).Contains("does not go away on its own", StringComparison.Ordinal),
        "It says why they need clearing");

    // The two entries on this screen used to contradict each other: one said claims expire on
    // their own, the other said an expired claim is reported until somebody releases it.
    True(!Prose(frame).Contains("Claims expire on their own", StringComparison.Ordinal),
        "Nothing on the screen claims a reservation clears itself");

    var panel = Reference.Coordination(snapshot);
    Equal(OverlayResult.Submit, panel.HandleKey(Key(ConsoleKey.R)), "R asks for the release");
    True(panel.ActionRequested, "The panel records that it was asked");

    // With nothing expired there is no key, because a key that does nothing is worse than no key.
    var clean = snapshot with
    {
        Claims = snapshot.Claims.Where(claim => claim.ExpiresAt > snapshot.CapturedAt).ToArray()
    };
    var quiet = Reference.Coordination(clean);
    Equal(OverlayResult.Continue, quiet.HandleKey(Key(ConsoleKey.R)),
        "R does nothing when nothing has expired");
    True(!quiet.ActionRequested, "And the panel does not claim it was asked");

    return Task.CompletedTask;
}

/// <summary>
/// A landing that Git refuses. LandAsync records the refusal on the task and returns it, the same
/// way a failed run does, rather than throwing - and both callers used to ignore that and announce
/// a success. The operator was told their work was on the base branch when Git had declined to put
/// it there, and the command exited 0, so a script would have believed it too.
/// </summary>
static async Task TestRefusedLandingAsync()
{
    if (ExecutableLocator.Find("git") is null)
    {
        throw new InvalidOperationException("Git is required for the refused-landing self-test.");
    }

    await WithTemporaryDirectoryAsync(async root =>
    {
        var process = new ProcessRunner();
        var git = new GitService(process);
        MustSucceed(await git.GitAsync(root, ["init", "-b", "main"]).ConfigureAwait(false), "git init");
        MustSucceed(await git.GitAsync(root, ["config", "user.email", "fknrtd-self-test@example.invalid"])
            .ConfigureAwait(false), "git config email");
        MustSucceed(await git.GitAsync(root, ["config", "user.name", "FKNRTD.CLI Self Test"])
            .ConfigureAwait(false), "git config name");
        await File.WriteAllTextAsync(Path.Combine(root, "README.md"), "# Test" + (char)10,
            new UTF8Encoding(false)).ConfigureAwait(false);

        var paths = WorkspaceLocator.ForRoot(root);
        var store = new StateStore(paths);
        await store.InitializeAsync(new FknrtdConfig
        {
            ProjectName = "refused-landing",
            Agents = [CreateFakeAgent()]
        }).ConfigureAwait(false);
        MustSucceed(await git.GitAsync(root, ["add", "README.md", ".fknrtd/config.json", ".fknrtd/.gitignore"])
            .ConfigureAwait(false), "git add");
        MustSucceed(await git.GitAsync(root, ["commit", "-m", "initial"]).ConfigureAwait(false),
            "initial commit");

        var worktrees = new WorktreeService(git, store);
        var tasks = new TaskService(store, git);
        var orchestrator = new Orchestrator(store, git, worktrees, new AgentRunner(store, process), process);
        var verification = OperatingSystem.IsWindows()
            ? "if exist feature.txt (exit /b 0) else (exit /b 1)"
            : "test -f feature.txt";
        var task = await tasks.CreateAsync(
                "Create a feature marker",
                "Create feature.txt with a short marker.",
                "fake", "fake", "fake",
                [verification])
            .ConfigureAwait(false);

        task = await orchestrator.RunAsync(task.Id).ConfigureAwait(false);
        Equal(WorkflowStatus.ReadyToLand, task.Status, "The task reached ready-to-land");

        // Now put a different feature.txt on main, so merging the task branch cannot succeed.
        await File.WriteAllTextAsync(Path.Combine(root, "feature.txt"),
            "a different marker, written on main" + (char)10, new UTF8Encoding(false)).ConfigureAwait(false);
        MustSucceed(await git.GitAsync(root, ["add", "feature.txt"]).ConfigureAwait(false),
            "stage the conflicting file");
        MustSucceed(await git.GitAsync(root, ["commit", "-m", "conflicting change on main"])
            .ConfigureAwait(false), "commit the conflicting change");

        var landed = await orchestrator.LandAsync(task.Id).ConfigureAwait(false);

        // The contract: a refused merge comes back as a failed task, not an exception.
        Equal(WorkflowStatus.Failed, landed.Status, "A refused merge leaves the task failed");
        Equal(StageState.Failed, landed.Stage(WorkflowStage.Land).State, "The land stage records it");
        True(!string.IsNullOrWhiteSpace(landed.LastError), "And it says why");

        // And nothing reached the base branch.
        var head = await git.GitAsync(root, ["log", "-1", "--pretty=%s"]).ConfigureAwait(false);
        MustSucceed(head, "read the base branch tip");
        Equal("conflicting change on main", head.StandardOutput.Trim(),
            "The base branch is exactly where it was");

        var merged = await File.ReadAllTextAsync(Path.Combine(root, "feature.txt")).ConfigureAwait(false);
        True(merged.Contains("written on main", StringComparison.Ordinal),
            "The base branch's own file was not overwritten");
        True(!merged.Contains("<<<<<<<", StringComparison.Ordinal),
            "No conflict markers were left in the working copy");
    }).ConfigureAwait(false);
}

/// <summary>
/// What every list command says when there is nothing to list. Three of them used to print
/// literally nothing, which is the one answer an operator cannot act on: it is indistinguishable
/// from a command that failed quietly, from one that is still running, and from a workspace that
/// did not load. An empty list is a fact, and it has a next step.
/// </summary>
static async Task TestEmptyListsExplainThemselvesAsync()
{
    await WithTemporaryDirectoryAsync(async root =>
    {
        Equal(0, await QuietlyAsync(["init", "-root", root, "-yes"]).ConfigureAwait(false),
            "Setting up an empty workspace");

        // Each command, and a word that must appear in what it says about being empty.
        var commands = new (string[] Arguments, string Subject)[]
        {
            (["task", "list"], "task"),
            (["claim", "list"], "reserv"),
            (["message", "list"], "message bus"),
            (["usage", "list"], "reported")
        };

        foreach (var (arguments, subject) in commands)
        {
            var writer = new StringWriter();
            var name = string.Join(' ', arguments);
            Equal(0, await QuietlyAsync([.. arguments, "-root", root], writer).ConfigureAwait(false),
                $"{name} exits 0 on an empty workspace");

            var text = writer.ToString();
            True(text.Trim().Length > 0, $"{name} says something rather than nothing");
            True(text.Contains(subject, StringComparison.OrdinalIgnoreCase),
                $"{name} names what is missing");

            // And it names a command that would produce one, so the answer is actionable.
            True(text.Contains("fknrtd ", StringComparison.Ordinal),
                $"{name} names a command that would create one");
        }

        // The same commands with -json stay machine-readable: an explanation printed into a JSON
        // stream would break every script reading it.
        foreach (var (arguments, _) in commands)
        {
            var writer = new StringWriter();
            var name = string.Join(' ', arguments);
            Equal(0, await QuietlyAsync([.. arguments, "-json", "-root", root], writer).ConfigureAwait(false),
                $"{name} -json exits 0");
            var text = writer.ToString().Trim();
            True(text.StartsWith('[') || text.StartsWith('{'),
                $"{name} -json emits JSON and not prose");
        }
    }).ConfigureAwait(false);
}

/// <summary>
/// Scrolling a stage log. The frame clamps its own copy of the scroll position, so the picture was
/// always right and the stored position was free to wander anywhere: Home set it to a sentinel a
/// billion lines past the end of the file, and PgDn, which steps back ten lines at a time, would
/// have taken about a hundred million presses to return. Only End recovered.
/// </summary>
static async Task TestLogScrollStaysInTheFileAsync()
{
    await WithTemporaryDirectoryAsync(async root =>
    {
        var store = new StateStore(WorkspaceLocator.ForRoot(root));
        await store.InitializeAsync(new FknrtdConfig
        {
            ProjectName = "scroll-test",
            Agents = [CreateFakeAgent()]
        }).ConfigureAwait(false);

        var task = new WorkflowTask
        {
            Id = "FKN-20260917-000000-scroll",
            Title = "A task with a long log",
            Brief = "Long enough to scroll through.",
            Status = WorkflowStatus.Running,
            LeadAgentId = "fake",
            ImplementerAgentId = "fake",
            AuditorAgentId = "fake"
        };
        task.Stage(WorkflowStage.Implement).State = StageState.Running;
        await store.SaveTaskAsync(task).ConfigureAwait(false);

        var logPath = store.TaskLogPath(task.Id, WorkflowStage.Implement, task.RepairRound);
        Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
        await File.WriteAllLinesAsync(logPath,
            Enumerable.Range(1, 120).Select(index => "line " + index)).ConfigureAwait(false);

        var app = new DashboardApp(null!, null!, null!, null!, null!, store, null!, null!, null!);
        var snapshot = new DashboardSnapshot { Config = await store.LoadConfigAsync().ConfigureAwait(false), Tasks = [task] };

        async Task PressAsync(ConsoleKey key) =>
            await app.HandleKeyAsync(Key(key), snapshot, CancellationToken.None).ConfigureAwait(false);

        // Into the log view, then to the top of the file.
        await PressAsync(ConsoleKey.L).ConfigureAwait(false);
        await PressAsync(ConsoleKey.Home).ConfigureAwait(false);
        True(app.LogScroll <= 120,
            $"Home stays inside the file rather than jumping past its end (was {app.LogScroll})");
        True(app.LogScroll > 0, "Home actually scrolls back");

        // One PgDn from the top moves by ten, not by a rounding error on a billion.
        var atTop = app.LogScroll;
        await PressAsync(ConsoleKey.PageDown).ConfigureAwait(false);
        Equal(Math.Max(0, atTop - 10), app.LogScroll, "PgDn from the top steps back ten lines");

        // Walking down with PgDn reaches the live tail in a sane number of presses.
        var presses = 0;
        while (app.LogScroll > 0 && presses < 40)
        {
            await PressAsync(ConsoleKey.PageDown).ConfigureAwait(false);
            presses++;
        }

        Equal(0, app.LogScroll, $"PgDn reaches the live tail (took {presses} more presses)");

        // PgUp cannot run past the end of the file either, which was the same defect in milder form.
        for (var index = 0; index < 60; index++)
        {
            await PressAsync(ConsoleKey.PageUp).ConfigureAwait(false);
        }

        True(app.LogScroll <= 120,
            $"PgUp stops at the top of the file rather than running away (was {app.LogScroll})");

        await PressAsync(ConsoleKey.PageDown).ConfigureAwait(false);
        True(app.LogScroll < 120, "And one PgDn after that moves back down");
    }).ConfigureAwait(false);
}

/// <summary>
/// The one action the help screen offers, and the filter it must not break. An action key on a
/// panel that filters would normally steal a character the operator meant to type, which is why
/// only a function key is allowed there.
/// </summary>
static Task TestHelpWritesThePageAsync()
{
    var help = Reference.Help();

    // Typing still searches. If the action had taken a letter, this would have fired instead.
    foreach (var character in "found")
    {
        Equal(OverlayResult.Continue,
            help.HandleKey(new ConsoleKeyInfo(character, ConsoleKey.NoName, false, false, false)),
            $"Typing '{character}' searches rather than acting");
    }

    True(!help.ActionRequested, "Typing never asks for the page to be written");

    Equal(OverlayResult.Submit, help.HandleKey(Key(ConsoleKey.F2)), "F2 asks for the page");
    True(help.ActionRequested, "And the panel records it");

    // The footer has to say so, or the key may as well not exist.
    var frame = Scenes.Render("help", 100, 30, colour: false);
    True(frame.Contains("F2 save all of this as a web page", StringComparison.Ordinal),
        "The help footer offers the key");

    // A panel that filters refuses a letter action outright, which is the rule that makes the
    // function key the only safe kind there.
    var refused = new InfoPanel("T", Theme.Cyan, _ => [new InfoLine("a", "b")], filterHint: "x",
        action: (ConsoleKey.R, "R", "do a thing"));
    Equal(OverlayResult.Continue, refused.HandleKey(Key(ConsoleKey.R)),
        "A letter action is not bound on a panel that filters");
    True(!refused.ActionRequested, "And it does not fire");

    // An action bound to Escape would shadow the only way out of a modal, so it is refused
    // whether or not the panel filters.
    foreach (var filterHint in new string?[] { null, "search me" })
    {
        var trapped = new InfoPanel("T", Theme.Cyan, _ => [new InfoLine("a", "b")], filterHint,
            action: (ConsoleKey.Escape, "Esc", "do a thing"));
        Equal(OverlayResult.Cancel, trapped.HandleKey(Key(ConsoleKey.Escape)),
            "Escape still closes the panel");
        True(!trapped.ActionRequested, "And never fires an action bound to it");
    }

    // The document it writes is the real one, with every table in it.
    var page = PortalCommand.Render(DateTimeOffset.UnixEpoch);
    True(page.Contains("FKNRTD", StringComparison.Ordinal), "The page names the product");
    foreach (var binding in Keymap.All)
    {
        True(page.Contains(binding.Action, StringComparison.Ordinal),
            $"The page documents the {binding.Key} key");
    }

    return Task.CompletedTask;
}

/// <summary>
/// The advice under "what to do next", for every status and both workspace modes. It is the most
/// action-guiding text the product has - `task show` and the inspector both print it - and three of
/// its seven branches had been written as though every workspace were Git-backed. A standalone
/// workspace has no worktree and no branch, so advice that names either is telling somebody to do
/// something that is not available to them.
/// </summary>
static Task TestNextStepFitsTheWorkspaceAsync()
{
    var git = Scenes.SampleConfig();
    var standalone = git with { Mode = WorkspaceMode.Standalone, DefaultBaseRef = string.Empty };

    // Phrases that point the operator at something a standalone workspace does not have. Naming
    // one in order to deny it is fine and useful - "there is no worktree to clean up either" is
    // exactly what somebody needs to read - so what is checked is the directing forms.
    var pointsAtGit = new[]
    {
        "its worktree", "in the worktree", "task cleanup", "Press X", "and merged", "its branch"
    };

    foreach (var status in Enum.GetValues<WorkflowStatus>())
    {
        var task = new WorkflowTask
        {
            Id = "FKN-20260917-000000-next",
            Title = "A task",
            Brief = "Something to do.",
            Status = status,
            BaseRef = "main",
            BranchName = "fknrtd/next",
            WorktreePath = "/src/aurora-api/.fknrtd/worktrees/next"
        };

        foreach (var onDashboard in new[] { true, false })
        {
            var advice = Reference.NextStep(task, standalone, onDashboard);
            True(advice.Trim().Length > 0, $"{status} has advice in a standalone workspace");
            foreach (var phrase in pointsAtGit)
            {
                True(!advice.Contains(phrase, StringComparison.OrdinalIgnoreCase),
                    $"Standalone advice for {status} (onDashboard: {onDashboard}) does not point at " +
                    $"'{phrase}': {advice}");
            }

            // Some statuses genuinely give the same advice either way - a running task is watched
            // with L and stopped with C whatever the workspace is - so the two are not required to
            // differ. What is required is that the Git one is there and says something.
            var gitAdvice = Reference.NextStep(task, git, onDashboard);
            True(gitAdvice.Trim().Length > 0, $"{status} has advice in a Git workspace");
        }
    }

    // The footer hint has the same job in one line, and had the same defect. It is reached through
    // a rendered frame, so the scene is what proves it.
    foreach (var status in new[] { WorkflowStatus.Landed, WorkflowStatus.ReadyToLand })
    {
        var one = new WorkflowTask
        {
            Id = "FKN-20260917-000000-hint",
            Title = "A task",
            Brief = "Something to do.",
            Status = status
        };
        var frame = Scenes.RenderWith(
            Scenes.PopulatedSnapshot() with { Config = standalone, Tasks = [one] },
            110, 30);
        foreach (var phrase in pointsAtGit)
        {
            True(!frame.Contains(phrase, StringComparison.OrdinalIgnoreCase),
                $"The standalone footer hint for {status} does not point at '{phrase}'");
        }
    }

    // The two that matter most, spelled out: a landed standalone task merged nothing, and a
    // cancelled one left its edits in the operator's own folder.
    var landed = new WorkflowTask { Id = "x", Status = WorkflowStatus.Landed };
    True(Reference.NextStep(landed, standalone).Contains("Nothing was merged", StringComparison.Ordinal),
        "A landed standalone task says nothing was merged");

    var cancelled = new WorkflowTask { Id = "x", Status = WorkflowStatus.Cancelled };
    True(Reference.NextStep(cancelled, standalone).Contains("in this folder", StringComparison.Ordinal),
        "A cancelled standalone task says where the half-finished edits are");

    return Task.CompletedTask;
}

/// <summary>
/// A task naming an agent that is no longer there. A task keeps the agent names it was created
/// with, and the roster can disable or remove one afterwards - it warns you when you do, and then
/// the task screen said nothing at all, so the next news was a stage failing to launch.
/// </summary>
static Task TestOrphanedAgentIsFlaggedAsync()
{
    var frame = Scenes.Render("inspect-missing-agent", 110, 34, colour: false);

    True(frame.Contains("NOT CONFIGURED", StringComparison.Ordinal),
        "A removed agent is named as missing");
    True(Prose(frame).Contains("this stage cannot run", StringComparison.Ordinal),
        "And the screen says what that costs");
    True(Prose(frame).Contains("Press A to add it back", StringComparison.Ordinal),
        "And what to do about it");

    // A disabled agent is a different case and must not be reported as the same one: the
    // orchestrator looks agents up by id and never reads Enabled, so an existing task still runs.
    True(Prose(frame).Contains("it will still run here", StringComparison.Ordinal),
        "A disabled agent is distinguished from a removed one");

    // An ordinary task says none of this.
    var healthy = Scenes.Render("inspect", 110, 34, colour: false);
    foreach (var alarm in new[] { "NOT CONFIGURED", "this stage cannot run", "it will still run here" })
    {
        True(!healthy.Contains(alarm, StringComparison.Ordinal),
            $"A task whose agents are all present does not say '{alarm}'");
    }

    return Task.CompletedTask;
}

/// <summary>
/// A key that starts slow work now repaints before it blocks, so the message saying what it is
/// doing actually reaches the screen. That repaint must stay inert everywhere the dashboard loop
/// is not running - this suite, and every scriptable command - or it would write a whole frame
/// into somebody's piped output.
/// </summary>
static async Task TestBusyRepaintStaysInsideTheLoopAsync()
{
    await WithTemporaryDirectoryAsync(async root =>
    {
        var store = new StateStore(WorkspaceLocator.ForRoot(root));
        await store.InitializeAsync(new FknrtdConfig { ProjectName = "paint-test" }).ConfigureAwait(false);
        var snapshot = new DashboardSnapshot { Config = await store.LoadConfigAsync().ConfigureAwait(false) };

        var app = new DashboardApp(null!, null!, null!, null!, null!, store, null!, null!, null!);
        var captured = new StringWriter();
        var original = Console.Out;
        try
        {
            Console.SetOut(captured);
            foreach (var key in new[] { ConsoleKey.D, ConsoleKey.U, ConsoleKey.V, ConsoleKey.P })
            {
                await app.HandleKeyAsync(Key(key), snapshot, CancellationToken.None).ConfigureAwait(false);
            }
        }
        finally
        {
            Console.SetOut(original);
        }

        Equal(string.Empty, captured.ToString(),
            "No frame is painted when the dashboard loop has never painted one");
    }).ConfigureAwait(false);
}

/// <summary>
/// Nothing on screen tells the operator to leave the dashboard for something the dashboard now
/// does. Five messages did: they were all true when they were written, and the roster and the
/// settings editor that made them untrue arrived the same morning. This is the kind of claim that
/// rots silently, because it keeps making sense right up until somebody tries to follow it.
/// </summary>
static Task TestNothingTellsYouToLeaveAsync()
{
    // The imperative forms. A screen naming the config file's path, or naming the shell command
    // that does the same job, is useful and stays allowed.
    var sendsYouAway = new[] { "Quit the dashboard", "Quit and run", "quit the dashboard and run" };

    foreach (var scene in Scenes.Names)
    {
        var frame = Scenes.Render(scene, 120, 44, colour: false);
        foreach (var phrase in sendsYouAway)
        {
            True(!frame.Contains(phrase, StringComparison.OrdinalIgnoreCase),
                $"The {scene} scene does not say '{phrase}'");
        }
    }

    // And the roster's own empty state points at its own key rather than at a shell.
    var empty = Scenes.Render("agents-empty", 110, 30, colour: false);
    True(empty.Contains("N to describe one", StringComparison.Ordinal) ||
         empty.Contains("Press N", StringComparison.Ordinal),
        "An empty roster names the key that fills it");

    return Task.CompletedTask;
}

/// <summary>
/// Quitting with work in flight. Leaving cancels the session, and cancelling a session kills every
/// running agent's process tree - which is a great deal to do for one unconfirmed keystroke from
/// somebody who may only have meant "put this away for a minute".
/// </summary>
static async Task TestQuittingAsksWhenWorkIsRunningAsync()
{
    await WithTemporaryDirectoryAsync(async root =>
    {
        var store = new StateStore(WorkspaceLocator.ForRoot(root));
        await store.InitializeAsync(new FknrtdConfig { ProjectName = "quit-test" }).ConfigureAwait(false);
        var config = await store.LoadConfigAsync().ConfigureAwait(false);
        var snapshot = new DashboardSnapshot { Config = config };

        // Nothing running: Q leaves at once, because there is nothing to lose by leaving.
        var idle = new DashboardApp(null!, null!, null!, null!, null!, store, null!, null!, null!);
        await idle.HandleKeyAsync(Key(ConsoleKey.Q), snapshot, CancellationToken.None).ConfigureAwait(false);
        True(idle.WantsToQuit, "Q leaves at once when nothing is running");

        // Something running: Q asks, and the frame says what leaving would cost.
        var busy = new DashboardApp(null!, null!, null!, null!, null!, store, null!, null!, null!);
        busy.PretendTaskIsRunning("FKN-20260917-000000-busy");
        await busy.HandleKeyAsync(Key(ConsoleKey.Q), snapshot, CancellationToken.None).ConfigureAwait(false);
        True(!busy.WantsToQuit, "Q does not leave straight away while a task is running");

        var frame = busy.RenderLive(snapshot, 110, 34);
        True(frame.Contains("LEAVE WHILE WORK IS RUNNING?", StringComparison.Ordinal),
            "It asks rather than leaving");
        True(Prose(frame).Contains("killed where it stands", StringComparison.Ordinal),
            "And says what leaving would do to the agent");

        // Staying is the default, so Enter on the untouched form keeps the session.
        await busy.HandleKeyAsync(Key(ConsoleKey.Enter), snapshot, CancellationToken.None).ConfigureAwait(false);
        True(!busy.WantsToQuit, "Enter takes the recommended answer, which is to stay");

        // Choosing to leave does leave.
        await busy.HandleKeyAsync(Key(ConsoleKey.Q), snapshot, CancellationToken.None).ConfigureAwait(false);
        await busy.HandleKeyAsync(Key(ConsoleKey.DownArrow), snapshot, CancellationToken.None).ConfigureAwait(false);
        await busy.HandleKeyAsync(Key(ConsoleKey.Enter), snapshot, CancellationToken.None).ConfigureAwait(false);
        True(busy.WantsToQuit, "Choosing to stop them and quit does quit");
    }).ConfigureAwait(false);
}

/// <summary>
/// Reading a stage log. The shipped agents are launched with machine-readable output so their
/// progress can be followed, which makes the file on disk a stream of JSON objects. Keeping that is
/// right; putting it on a screen is not, and pressing L on a running task used to show a wall of it
/// with the sentence the agent had just written quoted somewhere past column ninety.
/// </summary>
static Task TestLogIsReadableAsync()
{
    // What the agent said, did, and concluded, from the shapes it really emits.
    var cases = new (string Raw, string Expected, LogKind Kind)[]
    {
        ("""{"type":"assistant","message":{"content":[{"type":"text","text":"Reading Schedule.cs."}]}}""",
            "Reading Schedule.cs.", LogKind.Said),
        ("""{"type":"assistant","message":{"content":[{"type":"tool_use","name":"Edit","input":{"file_path":"src/A.cs"}}]}}""",
            "> Edit  src/A.cs", LogKind.Did),
        ("""{"type":"assistant","message":{"content":[{"type":"tool_use","name":"Bash","input":{"command":"dotnet test"}}]}}""",
            "> Bash  dotnet test", LogKind.Did),
        ("""{"type":"result","subtype":"success","result":"Done."}""", "√ Done.", LogKind.Finished),
        ("""{"type":"error","message":"it broke"}""", "× it broke", LogKind.Failed),
        ("""{"type":"system","subtype":"init","session_id":"3f9c"}""", "session init", LogKind.Noise),
        ("""{"type":"item.completed","item":{"type":"command_execution","command":"ls -la"}}""",
            "> ls -la", LogKind.Did),
        ("""{"type":"item.completed","item":{"type":"agent_message","text":"Finished the edit."}}""",
            "Finished the edit.", LogKind.Said)
    };

    foreach (var (raw, expected, kind) in cases)
    {
        var line = LogFormat.Read(raw);
        Equal(expected, line.Text, "Reading " + Text.Truncate(raw, 60));
        Equal(kind, line.Kind, "Kind of " + Text.Truncate(raw, 60));
    }

    // Anything not recognised is shown exactly as it arrived, including its indentation: build and
    // test tools indent to show structure, and an assertion's expected and actual line up under
    // each other. An agent whose format is unknown must be no worse off than before.
    foreach (var plain in new[]
             {
                 "  Determining projects to restore...",
                 "   Expected: 09:00",
                 "Failed!  - Failed: 1, Passed: 42",
                 "{ this is not json",
                 "[not json either"
             })
    {
        var line = LogFormat.Read(plain);
        Equal(plain, line.Text, "Plain output survives unchanged");
        Equal(LogKind.Plain, line.Kind, "And is treated as plain");
    }

    // Never throws, whatever it is handed. A log is read while it is being written, so half a line
    // is a normal thing to meet.
    foreach (var awkward in new[]
             {
                 "", "   ", "{", "{}", "[]", "null", """{"type":"assistant","message":{"content":[]}}""",
                 """{"type":"assistant","message":{"content":"a bare string"}}""",
                 """{"result":42}""", """{"type":"assistant","message":{"content":[{"type":"tool_use"}]}}"""
             })
    {
        var line = LogFormat.Read(awkward);
        True(line.Text.Length <= 2100, "A read line is bounded: " + awkward);
    }

    // A multi-line message becomes one row, because a row is one line by definition. The JSON is
    // built rather than written out, so the newlines in it are real ones.
    var multiLine = "one" + (char)10 + "two" + (char)10 + "three";
    var wrapped = LogFormat.Read(System.Text.Json.JsonSerializer.Serialize(new
    {
        type = "assistant",
        message = new { content = new[] { new { type = "text", text = multiLine } } }
    }));
    Equal("one two three", wrapped.Text, "A multi-line message is flattened onto its row");

    // And the whole thing is rendered somewhere, so the path is exercised at every size.
    var frame = Scenes.Render("logs-json", 100, 26, colour: false);
    True(frame.Contains("> Edit  src/Reports/Schedule.cs", StringComparison.Ordinal),
        "The log view shows what the agent did");
    True(!frame.Contains("\"type\":\"assistant\"", StringComparison.Ordinal),
        "And does not show the JSON it was written as");

    return Task.CompletedTask;
}

/// <summary>
/// A panel that names a key must be a panel where that key does something. The budget screen told
/// the operator to "press U to ask Codex directly" - while U was the key that had opened it, and
/// does nothing once it is open.
/// </summary>
static Task TestPanelsDoNotNameDeadKeysAsync()
{
    var usage = Reference.Usage(Scenes.EmptySnapshotForTests(), refreshError: null);
    Equal(OverlayResult.Submit, usage.HandleKey(Key(ConsoleKey.R)), "R asks again from inside");
    True(usage.ActionRequested, "And the panel records the request");

    var frame = Scenes.Render("usage-missing", 100, 30, colour: false);
    True(frame.Contains("R ask Codex again", StringComparison.Ordinal),
        "The footer offers the key the body names");
    True(!frame.Contains("Press U to ask", StringComparison.Ordinal),
        "And the body no longer names the key that opened it");

    return Task.CompletedTask;
}

/// <summary>
/// Every value of every enum the dashboard draws has an entry in the glossary. Nine of the ten
/// agent states did; the missing one was Unknown, which is the state FKNRTD.CLI itself assigns when
/// an agent stops reporting - so the one condition the product invents was the one it could not
/// explain. A value nobody can look up is a value nobody can act on.
/// </summary>
static Task TestEveryDrawnStateIsExplainedAsync()
{
    var families = new (string Prefix, string[] Values)[]
    {
        ("agentstate.", Enum.GetNames<AgentActivityState>()),
        ("status.", Enum.GetNames<WorkflowStatus>()),
        ("stagestate.", Enum.GetNames<StageState>()),
        ("stage.", Enum.GetNames<WorkflowStage>()),
        ("conflict.", Enum.GetNames<ConflictKind>()),
        ("claim-mode.", Enum.GetNames<ClaimMode>())
    };

    foreach (var (prefix, values) in families)
    {
        // A family is either explained value by value or not used as a family at all; the ones that
        // are must be complete, because a half-covered family is worse than an uncovered one.
        var covered = values.Count(value => Glossary.Find(prefix + value.ToLowerInvariant()) is not null);
        if (covered == 0)
        {
            continue;
        }

        foreach (var value in values)
        {
            True(Glossary.Find(prefix + value.ToLowerInvariant()) is not null,
                $"{prefix}{value.ToLowerInvariant()} is explained ({covered} of {values.Length} in " +
                "this family already are)");
        }
    }

    return Task.CompletedTask;
}

/// <summary>
/// The screens embedded in the offline guide. It explained every key and every term in words and
/// never showed anybody a screen, which is a strange way to document something whose whole problem
/// was that people could not tell what it was asking them. They are generated by the same renderer
/// the product runs, so a picture in the manual cannot show a panel that has been removed.
/// </summary>
static Task TestPortalShowsRealScreensAsync()
{
    var page = PortalCommand.Render(DateTimeOffset.UnixEpoch);

    True(page.Contains("id=screens", StringComparison.Ordinal), "The guide has a screens section");
    True(page.Contains("What it looks like", StringComparison.Ordinal), "And it is in the navigation");

    var frames = System.Text.RegularExpressions.Regex
        .Matches(page, "<pre class=screen><code>(.*?)</code></pre>",
            System.Text.RegularExpressions.RegexOptions.Singleline)
        .Select(match => System.Net.WebUtility.HtmlDecode(match.Groups[1].Value))
        .ToArray();

    True(frames.Length >= 4, $"Every screen made it into the page (found {frames.Length})");

    foreach (var frame in frames)
    {
        var lines = frame.Split((char)10).Where(line => line.Length > 0).ToArray();
        True(lines.Length > 10, "A screen is a whole frame rather than a fragment");

        // Every row of a real frame is the same display width. If that is not true here, the page
        // is showing something the renderer did not produce.
        var widths = lines.Select(Text.DisplayWidth).Distinct().ToArray();
        Equal(1, widths.Length,
            $"Every row of an embedded screen is the same width (saw {string.Join(", ", widths)})");

        // The guide is a single self-contained file of plain text. Escape sequences in it would
        // print as rubbish rather than as colour.
        True(!frame.Contains((char)27), "An embedded screen carries no escape sequences");
    }

    // The frames are escaped, so a panel border or a < in a value cannot break the document.
    True(!page.Contains("<pre class=screen><code><", StringComparison.Ordinal),
        "An embedded screen cannot open a tag");

    // And the screens are of the things a newcomer needs: the four surfaces this work added or
    // rebuilt, named in the page so somebody can find the one they are looking at.
    foreach (var caption in new[]
             {
                 "The command center", "Describing a piece of work", "The agent roster",
                 "Every setting, and what changing it costs"
             })
    {
        True(page.Contains(caption, StringComparison.Ordinal), $"The guide shows '{caption}'");
    }

    return Task.CompletedTask;
}

/// <summary>
/// The agent builder. Registering a coding CLI is the most cryptic thing this product asks for, and
/// the form had a hole in the middle of it: choosing to send the prompt on standard input skipped
/// the arguments question entirely, so a program that reads its prompt from a pipe could only ever
/// be launched with no flags at all.
/// </summary>
static Task TestAgentBuilderAsync()
{
    var config = Scenes.SampleConfig();

    static Wizard Answer(FknrtdConfig config, string id, string exe, string delivery, string args, string audit)
    {
        var wizard = AgentWizard.Create(config);
        foreach (var value in new[] { id, exe })
        {
            foreach (var character in value)
            {
                wizard.HandleKey(new ConsoleKeyInfo(character, ConsoleKey.NoName, false, false, false));
            }

            wizard.HandleKey(Key(ConsoleKey.Enter));
        }

        // Delivery is a choice: the second option needs one press of Down.
        if (delivery == "stdin")
        {
            wizard.HandleKey(Key(ConsoleKey.DownArrow));
        }

        wizard.HandleKey(Key(ConsoleKey.Enter));

        foreach (var character in args)
        {
            wizard.HandleKey(character == (char)10
                ? new ConsoleKeyInfo((char)13, ConsoleKey.Enter, false, true, false)
                : new ConsoleKeyInfo(character, ConsoleKey.NoName, false, false, false));
        }

        wizard.HandleKey(Key(ConsoleKey.Enter));

        if (audit == "no")
        {
            wizard.HandleKey(Key(ConsoleKey.DownArrow));
        }

        wizard.HandleKey(Key(ConsoleKey.Enter));
        return wizard;
    }

    // A program that takes its prompt on standard input can still be given flags. It could not
    // before: the step was skipped and Build wrote an empty argument list.
    var piped = AgentWizard.Build(Answer(config, "piped", "mytool", "stdin", "--json", "yes"));
    Equal("piped", piped.Id, "The id is what was typed");
    Equal(PromptDelivery.StandardInput, piped.Profiles["default"].PromptDelivery, "Delivery is stdin");
    Equal(1, piped.Profiles["default"].Arguments.Count, "A stdin agent keeps the flags it was given");
    Equal("--json", piped.Profiles["default"].Arguments[0], "And they are the ones typed");

    // An agent that audits gets both verdict markers, and only on its audit profile.
    True(piped.Profiles["audit"].SuccessMarker is not null, "An auditing agent has a pass marker");
    True(piped.Profiles["audit"].FailureMarker is not null, "And a fail marker");
    True(piped.Profiles["implement"].SuccessMarker is null,
        "Its other profiles expect no verdict, because only the audit stage reads one");

    // An agent that will not audit gets neither, which is what CanAudit reads to keep it out of the
    // auditor list rather than letting it be chosen and never approve anything.
    // Nothing typed at the arguments step, so it keeps the default the form offers - which is the
    // path almost everybody takes and therefore the one worth checking.
    var quiet = AgentWizard.Build(Answer(config, "quiet", "mytool", "argument", string.Empty, "no"));
    True(quiet.Profiles["audit"].SuccessMarker is null, "A non-auditing agent has no pass marker");
    True(!TaskWizard.CanAudit([quiet], "quiet"), "So the task builder will not offer it as an auditor");
    Equal(2, quiet.Profiles["default"].Arguments.Count, "Its arguments are a list, not a line of text");
    Equal("-p", quiet.Profiles["default"].Arguments[0], "The offered default is -p");
    Equal("{prompt}", quiet.Profiles["default"].Arguments[1], "followed by the prompt placeholder");

    // Build is reachable from anywhere, and an empty name used to be discovered by indexing into it.
    var bare = AgentWizard.Build(new Wizard("T", Theme.Violet, [new WizardStep
    {
        Key = "id", Question = "?", GlossaryTerm = "agent"
    }]));
    Equal(string.Empty, bare.Id, "An unanswered form builds an empty id rather than throwing");

    return Task.CompletedTask;
}

/// <summary>
/// Setting up a folder that cannot be Git-backed. The mode question was skipped entirely when there
/// was no repository, which meant the decision that most changes how safe this is got made in
/// silence - and the two reasons it can be forced look identical from outside while having
/// completely different fixes. GitInstalled existed on the input record for exactly this and was
/// never read.
/// </summary>
static Task TestSetupExplainsWhyThereIsNoChoiceAsync()
{
    var noGit = Prose(Scenes.Render("setup-no-git", 100, 28, colour: false));
    var noRepo = Prose(Scenes.Render("setup-no-repo", 100, 28, colour: false));

    // Both say the same thing about what will happen.
    foreach (var frame in new[] { noGit, noRepo })
    {
        True(frame.Contains("Agents will edit this folder", StringComparison.Ordinal),
            "It says what will happen to the folder");
        True(frame.Contains("UNTIL THEN", StringComparison.Ordinal),
            "And what to do about it in the meantime");
        True(frame.Contains("Yes, edit this folder", StringComparison.Ordinal),
            "The single option is not truncated");
    }

    // And each says the thing that is true only of it, because the fixes are different.
    True(noGit.Contains("Git is not installed on this machine", StringComparison.Ordinal),
        "A machine without Git is told that Git is missing");
    True(noGit.Contains("until Git is installed", StringComparison.Ordinal),
        "And what would change it");

    True(noRepo.Contains("There is no Git repository at /src/notes", StringComparison.Ordinal),
        "A folder that is not a repository is told that instead");
    True(noRepo.Contains("git init", StringComparison.Ordinal), "And what would change it");
    True(!noRepo.Contains("Git is not installed", StringComparison.Ordinal),
        "It does not blame a missing Git that is in fact present");

    // A repository still gets the real choice rather than a one-option formality.
    var repo = Prose(Scenes.Render("setup", 100, 28, colour: false));
    True(repo.Contains("Git-backed", StringComparison.Ordinal), "A repository is offered Git mode");
    True(repo.Contains("Standalone", StringComparison.Ordinal), "and standalone");

    return Task.CompletedTask;
}

/// <summary>
/// A confirmation panel finishes its sentences at every width. It measured itself faithfully and
/// then drew each paragraph through a fixed row cap, so anything needing more rows than its
/// constant was clipped - at 62 columns the "here is the reversible thing to do instead" line
/// stopped at "That is". Measure and Draw agreed with each other perfectly; they were both wrong in
/// the same way, which is exactly why mirroring one against the other had not caught it.
/// </summary>
static Task TestConfirmationFinishesItsSentencesAsync()
{
    // The last words of each paragraph on the two confirmation scenes. If a cap comes back, the
    // panel will still look plausible and one of these will be missing.
    var endings = new (string Scene, string[] Endings)[]
    {
        ("agents-remove", ["configuring the agent again.", "the stage that needed it.", "That is reversible."]),
        ("land", ["cannot undo it afterwards.", "if you would rather use your own tools."]),
        ("remove", ["because this task has not landed.", "worktree directory that no longer exists."]),
        ("remove-landed", ["not already merged.", "worktree directory that no longer exists."])
    };

    foreach (var (scene, expected) in endings)
    {
        foreach (var width in new[] { 62, 70, 84, 100, 140 })
        {
            var prose = Prose(Scenes.Render(scene, width, 40, colour: false));
            foreach (var ending in expected)
            {
                True(prose.Contains(ending, StringComparison.Ordinal),
                    $"The {scene} panel reaches '{ending}' at {width} columns");
            }
        }
    }

    // The dialog that asks permission to delete things has to be right about what it deletes. A
    // landed task's branch goes with its worktree; an unlanded one's does not, and the dialog said
    // the branch was kept in both cases long after that stopped being true.
    var landed = Prose(Scenes.Render("remove-landed", 100, 40, colour: false));
    True(landed.Contains("it also deletes its branch", StringComparison.Ordinal),
        "Removing a landed task's worktree says the branch goes too");
    True(!landed.Contains("its Git branch are all kept", StringComparison.Ordinal),
        "and does not also claim it is kept");

    var unlanded = Prose(Scenes.Render("remove", 100, 40, colour: false));
    True(unlanded.Contains("its Git branch are all kept", StringComparison.Ordinal),
        "Removing an unlanded task's worktree keeps the branch and says so");

    return Task.CompletedTask;
}

/// <summary>
/// What `fknrtd doctor` reads like. It listed its checks and stopped: a wall of ticks with one mark
/// in it is easy to scan past, and the one check that had something to say said only what was wrong
/// and not what to do. A long detail also ran off the right edge of the window, which lost the text
/// of exactly the check that had most to say.
/// </summary>
static async Task TestDoctorReadsWellAsync()
{
    await WithTemporaryDirectoryAsync(async root =>
    {
        Equal(0, await QuietlyAsync(["init", "-root", root, "-yes"]).ConfigureAwait(false),
            "Setting up the workspace");

        var writer = new StringWriter();
        await QuietlyAsync(["doctor", "-root", root], writer).ConfigureAwait(false);
        var text = writer.ToString();
        var lines = text.Split((char)10).Select(line => line.TrimEnd((char)13)).ToArray();

        // Nothing runs off the edge: every line fits the window the wrapper was told about.
        foreach (var line in lines)
        {
            True(Text.DisplayWidth(line) <= 110, $"A doctor line fits the window: {line}");
        }

        // It ends with a sentence rather than trailing off after the last check. The summary is one
        // wrapped paragraph after a blank line, so the whole closing block is what to look at - the
        // first attempt at this checked the final line and found the tail of the wrap.
        // Written out rather than with FindLastIndex: every blank line is the same string, so an
        // IndexOf inside the predicate answers about the first one and not the one being tested.
        var blank = -1;
        for (var index = 0; index < lines.Length - 1; index++)
        {
            if (lines[index].Trim().Length == 0)
            {
                blank = index;
            }
        }

        True(blank > 0, "Doctor separates its summary from its checks with a blank line");
        var closing = string.Join(' ', lines.Skip(blank + 1)).Trim();
        True(closing.Contains("passed", StringComparison.OrdinalIgnoreCase),
            $"Doctor ends by saying where it got to, not with its last check: {closing}");
        True(!closing.StartsWith((char)8730) && !closing.StartsWith((char)8710),
            "The closing block is a sentence rather than another check row");

        // A workspace with no verification commands is told what to do about it, not only that it
        // is a risk.
        // Searched in the de-wrapped text: the detail now wraps under its name, so a phrase longer
        // than the column can be split across two lines and a raw Contains would miss it.
        var flowed = string.Join(' ', lines.Select(line => line.Trim()));
        while (flowed.Contains("  ", StringComparison.Ordinal))
        {
            flowed = flowed.Replace("  ", " ", StringComparison.Ordinal);
        }

        True(flowed.Contains("Set defaultVerificationCommands", StringComparison.Ordinal),
            "The verification warning names the fix");

        // The -json form stays machine-readable: the summary is prose and must not reach it.
        var json = new StringWriter();
        Equal(0, await QuietlyAsync(["doctor", "-json", "-root", root], json).ConfigureAwait(false),
            "doctor -json exits 0");
        var payload = json.ToString().Trim();
        True(payload.StartsWith('['), "doctor -json emits JSON");
        True(!payload.Contains("Everything required passed", StringComparison.Ordinal),
            "and no prose summary leaks into it");
    }).ConfigureAwait(false);
}

/// <summary>
/// Claims that were found to be false and corrected, checked against everything the product says.
/// </summary>
/// <remarks>
/// Three times in one session a claim was corrected in one place and left standing in another: the
/// branch-is-kept promise survived in the confirmation dialog that asks permission to delete it and
/// again in the shell command's refusal message, and the every-field-has-a-default claim survived
/// in the README and the changelog. Each copy read perfectly well on its own, which is why reading
/// them did not help. This is the list, and it is checked against the generated guide - which
/// carries the glossary, the command catalog, the keymap and the settings table - and against every
/// rendered scene.
/// </remarks>
static Task TestCorrectedClaimsStayCorrectedAsync()
{
    var retired = new (string Claim, string Why)[]
    {
        ("Claims expire on their own",
            "expiring marks a claim stale; nothing deletes the record"),
        // "its Git branch are all kept" is deliberately not here: it is true of a task that has not
        // landed and false of one that has, so a blanket ban would be as wrong as the claim was.
        // TestConfirmationFinishesItsSentences checks each case says the right one.
        ("cannot write to the worktree",
            "the shipped profiles ask an agent not to edit; nothing enforces it"),
        ("the only agent allowed to write",
            "the same - it is the agent whose profile asks it to"),
        ("The only agent that may write files",
            "the same claim on the prompts screen"),
        ("It proposes; it changes nothing",
            "the same claim about the lead, which nothing enforces"),
        ("will not get through the pipeline until",
            "doctor reports and does not gate; a task starts and fails part-way through"),
        ("logs may already have been cleaned up",
            "nothing deletes stage logs - not landing, and not cleanup"),
        ("Every stage, conflict and agent check-in is recorded",
            "stages record nothing, conflicts are computed live, telemetry writes no history"),
        ("no single agent both writes",
            "nothing stops one agent filling all three roles"),
        ("Safe means no overlap at all",
            "same-agent and read/read overlaps are not conflicts"),
        ("only after verification passed",
            "a task with no verification commands skips that stage and lands on the audit"),
        ("Stale runtime state",
            "it names an internal condition rather than what happened")
    };

    var page = PortalCommand.Render(DateTimeOffset.UnixEpoch);
    foreach (var (claim, why) in retired)
    {
        True(!page.Contains(claim, StringComparison.OrdinalIgnoreCase),
            $"The guide no longer says '{claim}' - {why}");
    }

    // And no screen says it either. Searched through Prose, because a claim that came back wrapped
    // would otherwise slip past exactly the check meant to catch it.
    foreach (var scene in Scenes.Names)
    {
        var prose = Prose(Scenes.Render(scene, 120, 44, colour: false));
        foreach (var (claim, why) in retired)
        {
            True(!prose.Contains(claim, StringComparison.OrdinalIgnoreCase),
                $"The {scene} scene no longer says '{claim}' - {why}");
        }
    }

    return Task.CompletedTask;
}

/// <summary>
/// The commands nothing in this suite had ever run. Several of them were only ever exercised by a
/// person typing them, which is to say by nobody since they were written.
/// </summary>
static async Task TestTheQuieterCommandsWorkAsync()
{
    await WithTemporaryDirectoryAsync(async root =>
    {
        Equal(0, await QuietlyAsync(["init", "-root", root, "-yes"]).ConfigureAwait(false), "init");

        async Task<int> Run(params string[] arguments) =>
            await QuietlyAsync([.. arguments, "-root", root]).ConfigureAwait(false);

        // The agent lifecycle, end to end.
        Equal(0, await Run("agent", "add", "-id", "gem", "-exe", "gemini", "-arg=-p", "-arg", "{prompt}"),
            "agent add");
        Equal(0, await Run("agent", "disable", "gem"), "agent disable");
        Equal(0, await Run("agent", "enable", "gem"), "agent enable");
        // ExecuteAsync throws on a refused command; it is Main that turns that into exit 1. At this
        // level the contract is the exception, and what matters is that the message says the fix.
        async Task<string> RefusedAsync(params string[] arguments)
        {
            try
            {
                await Run(arguments).ConfigureAwait(false);
                return string.Empty;
            }
            catch (Exception exception)
            {
                return exception.Message;
            }
        }

        var refused = await RefusedAsync("agent", "remove", "gem");
        True(refused.Contains("-confirm REMOVE", StringComparison.Ordinal),
            $"agent remove refuses without the confirmation and names it: {refused}");

        Equal(0, await Run("agent", "remove", "gem", "-confirm", "REMOVE"), "agent remove");

        var gone = await RefusedAsync("agent", "enable", "gem");
        True(gone.Contains("not configured", StringComparison.Ordinal),
            $"and it is really gone: {gone}");

        // Reservations, and the message bus.
        Equal(0, await Run("claim", "add", "-agent", "claude", "-path", "src/x.cs", "-mode", "write"),
            "claim add");
        Equal(0, await Run("message", "send", "-from", "claude", "-to", "codex", "-text", "over to you"),
            "message send");
        Equal(0, await Run("config", "show"), "config show");
        Equal(0, await Run("config", "path"), "config path");

        // usage set replaces the whole snapshot. That is right for the hooks, which report
        // everything they know each time, and a trap for somebody correcting one number by hand -
        // so it has to say what it dropped.
        Equal(0, await Run("usage", "set", "claude", "-context", "70", "-five-hour", "80", "-weekly", "60"),
            "usage set");
        var writer = new StringWriter();
        Equal(0, await QuietlyAsync(["usage", "set", "claude", "-five-hour", "55", "-root", root], writer)
            .ConfigureAwait(false), "usage set with one figure");

        var said = writer.ToString();
        True(said.Contains("-context", StringComparison.Ordinal), "It names the figure it dropped");
        True(said.Contains("-weekly", StringComparison.Ordinal), "and the other one");
        True(said.Contains("replaced", StringComparison.Ordinal), "and says why they went");

        // And it says nothing of the kind when nothing was dropped.
        var quiet = new StringWriter();
        await QuietlyAsync(["usage", "set", "claude", "-context", "9", "-five-hour", "9", "-weekly", "9",
            "-root", root], quiet).ConfigureAwait(false);
        True(!quiet.ToString().Contains("now unknown", StringComparison.Ordinal),
            "A complete report says nothing about dropping anything");
    }).ConfigureAwait(false);
}

/// <summary>
/// The welcome panel, which is the first thing a new operator reads and the last part of the
/// reference nobody had fact-checked. Three of its sentences asserted things the workspace does not
/// guarantee.
/// </summary>
static Task TestWelcomeIsTrueAsync()
{
    var git = Prose(Scenes.Render("welcome", 118, 40, colour: false));
    var standalone = Prose(Scenes.Render("welcome-standalone", 118, 40, colour: false));

    // Standalone is a choice the setup form offers inside a repository, so the panel must not
    // assert that a standalone workspace is not one - and must not then advise making it one.
    True(!standalone.Contains("it is not a Git repository", StringComparison.Ordinal),
        "The standalone welcome does not assert why the workspace is standalone");
    True(standalone.Contains("or because standalone was chosen", StringComparison.Ordinal),
        "It names both reasons");
    True(standalone.Contains("commit anything you care about", StringComparison.Ordinal),
        "and says what to do either way");

    // Nothing requires the auditor to be a different agent from the implementer, so the summary
    // cannot promise a third one.
    foreach (var frame in new[] { git, standalone })
    {
        True(!frame.Contains("A third agent audits", StringComparison.Ordinal),
            "The summary does not promise three distinct agents");
        True(frame.Contains("By default that is not the agent which wrote the change",
                StringComparison.Ordinal),
            "It says the separation is a default");
        True(frame.Contains("while the repair budget lasts", StringComparison.Ordinal),
            "and that failed work only goes back while there is budget for it");
    }

    // The last step is about merging in one mode and not the other.
    True(git.Contains("Nothing merges without that", StringComparison.Ordinal),
        "A Git workspace is told nothing merges without LAND");
    True(standalone.Contains("Nothing is recorded as finished without that", StringComparison.Ordinal),
        "A standalone workspace is told what LAND does there instead");
    True(!standalone.Contains("Nothing merges without that", StringComparison.Ordinal),
        "and is not told about a merge that will not happen");

    return Task.CompletedTask;
}

/// <summary>
/// Every command this product names in its own explanations is a command it has. Telling somebody
/// to run something that does not exist is the most expensive kind of wrong sentence: they will
/// type it, and the tool will tell them it is a typo.
/// </summary>
static Task TestNamedCommandsExistAsync()
{
    var known = CommandCatalog.All.Select(entry => entry.Name).ToHashSet(StringComparer.Ordinal);

    // Everything the four tables and the build log say, in one bag.
    var texts = new List<string>();
    foreach (var entry in Glossary.All)
    {
        texts.AddRange([entry.Summary, entry.Detail, entry.Example]);
    }

    foreach (var entry in CommandCatalog.All)
    {
        texts.AddRange([entry.Summary, entry.Detail, entry.WhatHappensNext]);
        texts.AddRange(entry.Options.Select(option => option.Meaning));
    }

    texts.AddRange(Keymap.All.Select(binding => binding.Detail));
    foreach (var entry in SettingsCatalog.All)
    {
        texts.AddRange([entry.Summary, entry.Detail, entry.IfYouChangeIt]);
    }

    foreach (var entry in Milestones.All)
    {
        texts.AddRange([entry.Problem, entry.Change, entry.Why]);
    }

    // "fknrtd " followed by up to two lowercase words. Prose continues after the word too - "the
    // fknrtd in git" - so a phrase counts as naming a command only when its first word is one.
    var pattern = new System.Text.RegularExpressions.Regex(
        "fknrtd ([a-z][a-z-]*)(?: ([a-z][a-z-]*))?");

    var checkedAny = false;
    foreach (var text in texts.Where(item => !string.IsNullOrEmpty(item)))
    {
        foreach (System.Text.RegularExpressions.Match match in pattern.Matches(text))
        {
            var first = match.Groups[1].Value;
            if (!known.Contains(first))
            {
                // Not a command at all: prose that happens to follow the product's name, or the
                // deliberate typo in the milestone about typo suggestions.
                continue;
            }

            checkedAny = true;
            var second = match.Groups[2].Value;
            if (second.Length == 0)
            {
                continue;
            }

            // A subcommand was named. Either it is a command, or the first word stands alone and
            // what follows is prose.
            var pair = first + " " + second;
            True(known.Contains(pair) || known.Contains(first),
                $"'fknrtd {pair}' names a command that exists");
        }
    }

    True(checkedAny, "The tables do name commands, so this test is looking at something");

    // Every catalogued command reaches the help surface by its own name, which is what makes
    // naming one in a sentence safe advice.
    foreach (var entry in CommandCatalog.All)
    {
        True(CommandCatalog.Find(entry.Name) is not null,
            $"'{entry.Name}' can be looked up by the name it is referred to by");
    }

    return Task.CompletedTask;
}

/// <summary>
/// Every key a screen tells you to press is one that does something on that screen.
/// </summary>
/// <remarks>
/// An open modal owns every keystroke, so the dashboard's own keys are dead while one is up. A
/// panel saying "press U" is therefore only honest when the panel itself handles U, or when it says
/// to leave first. The budget panel said "press U to ask Codex directly" while U was the key that
/// had opened it, and that came to light because somebody read the screen rather than the code.
/// </remarks>
static Task TestNamedKeysArePressableAsync()
{
    var pattern = new System.Text.RegularExpressions.Regex("[Pp]ress ([A-Z]) ");
    var checkedAny = false;

    foreach (var scene in Scenes.Names)
    {
        var frame = Scenes.Render(scene, 120, 44, colour: false);

        // Only scenes with a modal up: the heavy border is what draws one. On the bare overview
        // every dashboard key is live, so naming one is always fair.
        if (!frame.Contains('\u2503'))
        {
            continue;
        }

        var prose = Prose(frame);
        foreach (System.Text.RegularExpressions.Match match in pattern.Matches(prose))
        {
            var key = match.Groups[1].Value;
            checkedAny = true;

            // Either the panel says how to leave before it starts naming dashboard keys - one
            // lead-in covers a whole block of advice, which is how it is actually written - or it
            // handles the key itself, which it advertises in its own footer.
            var escapeAt = prose.IndexOf("Esc", StringComparison.Ordinal);
            var saysLeaveFirst = escapeAt >= 0 && escapeAt < match.Index;
            var offered = FrameLines(frame).Any(line =>
                line.Contains("Esc close", StringComparison.Ordinal) &&
                line.Contains(" " + key + " ", StringComparison.Ordinal));

            True(saysLeaveFirst || offered,
                $"The {scene} screen says 'press {key}' while a panel is up, and either handles " +
                $"{key} or says how to leave before naming it");
        }
    }

    True(checkedAny, "Some panel does name a key, so this test is looking at something");

    // The one that started it.
    var budget = Prose(Scenes.Render("usage-missing", 110, 40, colour: false));
    True(!budget.Contains("Press U", StringComparison.Ordinal),
        "The budget panel does not tell you to press the key that opened it");

    return Task.CompletedTask;
}

/// <summary>
/// Every question a form asks names a glossary term that exists. A step's term is what F1 opens and
/// what the inline "what this is" block reads from, so a term that does not resolve leaves the one
/// field somebody stopped at with nothing to explain it - which is the exact complaint this whole
/// programme started from.
/// </summary>
static Task TestEveryQuestionCanExplainItselfAsync()
{
    var config = Scenes.SampleConfig();
    var forms = new (string Name, Wizard Form)[]
    {
        ("the task builder", TaskWizard.Create(config)),
        ("the message form", TaskWizard.Message(config)),
        ("the agent builder", AgentWizard.Create(config)),
        ("setup, with a repository", SetupWizard.Create(Scenes.SampleDetection())),
        ("setup, without one", SetupWizard.Create(new SetupWizard.Detected(
            "/src/notes", IsRepository: false, GitInstalled: false, string.Empty, [], HasClaude: true)))
    };

    var seen = 0;
    foreach (var (name, form) in forms)
    {
        foreach (var step in form.Steps)
        {
            seen++;
            True(Glossary.Find(step.GlossaryTerm) is not null,
                $"In {name}, the '{step.Key}' step names glossary term '{step.GlossaryTerm}'");

            // And the question is a question, not a label. "Lead agent:" is what this replaced.
            True(step.Question.Trim().Length > 0, $"In {name}, the '{step.Key}' step asks something");
            True(step.Question.Contains('?') || step.Input == WizardInput.Commands,
                $"In {name}, the '{step.Key}' step asks rather than labels: {step.Question}");
        }
    }

    // Every settings field's form too, which is built one step at a time from the catalog.
    foreach (var setting in SettingsBrowser.Editable)
    {
        var form = SettingsBrowser.Form(config, setting.Key);
        True(form is not null, $"'{setting.Key}' has a form");
        foreach (var step in form!.Steps)
        {
            seen++;
            True(Glossary.Find(step.GlossaryTerm) is not null,
                $"The '{setting.Key}' editor names glossary term '{step.GlossaryTerm}'");
        }
    }

    True(seen > 20, $"There are forms to check, and {seen} steps were checked");
    return Task.CompletedTask;
}

/// <summary>
/// The retired claims, checked against every line of source rather than only against what the
/// tables say.
/// </summary>
/// <remarks>
/// Two of the stale claims this session hid in the dispatcher's runtime messages, which no rendered
/// scene and no generated document covers: the shell's refusal message for `task cleanup` promised
/// a landed task's branch is kept, and the overview's events panel described events the product
/// never records. A guard that only reads the tables cannot see either.
///
/// This walks the source instead. When it cannot find the source - a packaged binary, a different
/// layout - it says so and passes, because a lint that fails on somebody else's machine for reasons
/// unrelated to their change teaches them to ignore it.
/// </remarks>
static Task TestNoRetiredClaimInAnySourceFileAsync()
{
    var root = AppContext.BaseDirectory;
    while (root is not null && !File.Exists(Path.Combine(root, "FKNRTD.CLI.sln")))
    {
        root = Path.GetDirectoryName(root.TrimEnd(Path.DirectorySeparatorChar));
    }

    if (root is null)
    {
        Console.WriteLine("  (source tree not found; the retired-claim sweep is skipped)");
        return Task.CompletedTask;
    }

    // Phrases that are false wherever they appear. Claims that are true in one case and false in
    // another are checked by the test for that case, not here.
    var retired = new[]
    {
        "Claims expire on their own",
        "cannot write to the worktree",
        "the only agent allowed to write",
        "The only agent that may write files",
        "It proposes; it changes nothing",
        "no single agent both writes",
        "Safe means no overlap at all",
        "only after verification passed",
        "Stale runtime state",
        "will not get through the pipeline until",
        "logs may already have been cleaned up",
        "Every stage, conflict and agent check-in is recorded",
        "stage.passed",
        "stage.failed"
    };

    var files = Directory
        .EnumerateFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories)
        .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") &&
                       !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
        .ToArray();

    True(files.Length > 20, $"The sweep found the source ({files.Length} files)");

    foreach (var file in files)
    {
        var lines = File.ReadAllLines(file);
        for (var number = 0; number < lines.Length; number++)
        {
            var line = lines[number];

            // A comment may quote a retired claim to explain why it was retired, which is exactly
            // the kind of note worth keeping.
            var trimmed = line.TrimStart();
            if (trimmed.StartsWith("//", StringComparison.Ordinal))
            {
                continue;
            }

            foreach (var claim in retired)
            {
                True(!line.Contains(claim, StringComparison.OrdinalIgnoreCase),
                    $"{Path.GetFileName(file)}:{number + 1} carries the retired claim '{claim}'");
            }
        }
    }

    return Task.CompletedTask;
}

/// <summary>
/// A task file that will not parse. The reader has to keep going - one bad file must not take the
/// command center down - but a task is the operator's work rather than a runtime snapshot that is
/// rebuilt in a second, and swallowing it meant `task list` printed "No tasks exist in this
/// workspace yet" for a workspace that had one. That is a wrong answer, not an unhelpful one, and
/// it would send somebody off to write the task again.
/// </summary>
static async Task TestUnreadableTasksAreReportedAsync()
{
    await WithTemporaryDirectoryAsync(async root =>
    {
        var store = new StateStore(WorkspaceLocator.ForRoot(root));
        await store.InitializeAsync(new FknrtdConfig
        {
            ProjectName = "corrupt-test",
            // Standalone, so creating a task needs no repository: this test is about a file that
            // will not parse, and a base branch has nothing to do with it.
            Mode = WorkspaceMode.Standalone,
            Agents = [CreateFakeAgent()]
        }).ConfigureAwait(false);

        var tasks = new TaskService(store, new GitService(new ProcessRunner()));
        var good = await tasks.CreateAsync("A real task",
            "A brief long enough to be accepted by the validator.",
            "fake", "fake", "fake", []).ConfigureAwait(false);
        var doomed = await tasks.CreateAsync("The one that breaks",
            "Another brief long enough to be accepted by the validator.",
            "fake", "fake", "fake", []).ConfigureAwait(false);

        // Exactly what an interrupted write leaves behind.
        var path = Directory.EnumerateFiles(store.Paths.Tasks, "*.json")
            .First(file => Path.GetFileNameWithoutExtension(file) == doomed.Id);
        await File.WriteAllTextAsync(path, "{ \"id\": \"FKN-", new UTF8Encoding(false)).ConfigureAwait(false);

        // The reader keeps going and says what it could not read.
        var (loaded, unreadable) = await store.LoadTasksAndProblemsAsync().ConfigureAwait(false);
        Equal(1, loaded.Count, "The readable task still loads");
        Equal(good.Id, loaded[0].Id, "and it is the right one");
        Equal(1, unreadable.Count, "The unreadable one is reported");
        True(unreadable[0].Contains(doomed.Id, StringComparison.Ordinal), "by its path");

        // The shell says so, and does not list the broken one as if it were fine.
        var writer = new StringWriter();
        Equal(0, await QuietlyAsync(["task", "list", "-root", root], writer).ConfigureAwait(false),
            "task list still exits 0");
        var text = writer.ToString();
        True(text.Contains("could not be read", StringComparison.Ordinal),
            "task list reports the unreadable file");
        True(text.Contains(good.Id, StringComparison.Ordinal), "and still lists the good task");
        True(!text.Contains("No tasks exist", StringComparison.Ordinal),
            "and never claims the workspace is empty when it is not");
    }).ConfigureAwait(false);

    // On the dashboard, the panel title counts them and the empty state does not claim the
    // workspace has nothing in it.
    var some = Prose(Scenes.Render("tasks-unreadable", 100, 30, colour: false));
    True(some.Contains("1 unreadable", StringComparison.Ordinal),
        "The pipeline title counts what it could not read");

    var none = Prose(Scenes.Render("tasks-all-unreadable", 100, 30, colour: false));
    True(!none.Contains("Nothing to do yet", StringComparison.Ordinal),
        "A workspace whose only task file is broken is not described as empty");
    True(none.Contains("could not be read", StringComparison.Ordinal), "It says what happened");
    True(none.Contains("Press E for the history", StringComparison.Ordinal),
        "and where the record of the task still is");
}

/// <summary>
/// What the product says when a file it depends on will not parse. The parser's own message is
/// accurate and useless to the person reading it - "'n' is an invalid start of a property name.
/// Expected a '"'." names no file, no cause and no remedy - and it was reaching the operator
/// unchanged, on one unwrapped line.
/// </summary>
static async Task TestUnreadableConfigExplainsItselfAsync()
{
    await WithTemporaryDirectoryAsync(async root =>
    {
        var store = new StateStore(WorkspaceLocator.ForRoot(root));
        await store.InitializeAsync(new FknrtdConfig { ProjectName = "broken-config" }).ConfigureAwait(false);
        await File.WriteAllTextAsync(store.Paths.Config, "{ not json", new UTF8Encoding(false))
            .ConfigureAwait(false);

        var message = string.Empty;
        try
        {
            await store.LoadConfigAsync().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            message = exception.Message;
        }

        True(message.Length > 0, "Reading a broken configuration fails rather than returning nonsense");
        True(message.Contains("config.json", StringComparison.Ordinal),
            $"The message names the file: {message}");
        True(message.Contains("not valid JSON", StringComparison.Ordinal),
            "and says what is wrong with it");
        True(message.Contains("init -force", StringComparison.Ordinal),
            "and how to rebuild it");
        True(message.Contains("backups", StringComparison.Ordinal),
            "and where the previous one may be");

        // The parser's position is worth keeping: it is the only part of its message that helps
        // somebody who is going to open the file and fix it.
        True(message.Contains("LineNumber", StringComparison.Ordinal),
            "and keeps the parser's position");
    }).ConfigureAwait(false);
}

/// <summary>
/// A task whose worktree is gone is not a task that never had one. Both surfaces had two cases -
/// landed, and everything else - so a task that was ready to land and had lost its directory was
/// told it "has not reached its worktree stage", which reads as "your finished work never started".
/// </summary>
static async Task TestMissingWorktreeIsDistinguishedAsync()
{
    await WithTemporaryDirectoryAsync(async root =>
    {
        var store = new StateStore(WorkspaceLocator.ForRoot(root));
        await store.InitializeAsync(new FknrtdConfig
        {
            ProjectName = "worktree-test",
            Mode = WorkspaceMode.Standalone,
            Agents = [CreateFakeAgent()]
        }).ConfigureAwait(false);

        var tasks = new TaskService(store, new GitService(new ProcessRunner()));
        var task = await tasks.CreateAsync("Worktree test",
            "A brief long enough to be accepted by the validator.",
            "fake", "fake", "fake", []).ConfigureAwait(false);

        // Both tasks are created while the workspace is standalone, because creating one in Git
        // mode resolves a base branch and this temporary folder is not a repository.
        var fresh = await tasks.CreateAsync("Never run",
            "Another brief long enough to be accepted by the validator.",
            "fake", "fake", "fake", []).ConfigureAwait(false);

        // What is left after somebody deletes the directory by hand, or after a cleanup: the record
        // still names a path, and nothing is there.
        task.Status = WorkflowStatus.ReadyToLand;
        task.WorktreePath = Path.Combine(root, ".fknrtd", "worktrees", "gone");
        task.BranchName = "fknrtd/gone";
        await store.SaveTaskAsync(task).ConfigureAwait(false);

        // The shell version has to be Git-mode to reach the branch, so the message is checked
        // through a Git workspace's own wording rather than the standalone shortcut.
        var saved = await store.LoadConfigAsync().ConfigureAwait(false);
        await store.SaveConfigAsync(saved with { Mode = WorkspaceMode.Git }).ConfigureAwait(false);

        var writer = new StringWriter();
        Equal(0, await QuietlyAsync(["task", "diff", task.Id, "-root", root], writer).ConfigureAwait(false),
            "task diff exits 0");
        var said = writer.ToString().Replace((char)10, ' ').Replace((char)13, ' ');
        while (said.Contains("  ", StringComparison.Ordinal))
        {
            said = said.Replace("  ", " ", StringComparison.Ordinal);
        }

        True(!said.Contains("has not reached its worktree stage", StringComparison.Ordinal),
            $"A ready-to-land task is not told it never started: {said}");
        True(said.Contains("which is not there", StringComparison.Ordinal),
            "It says the directory is missing");
        True(said.Contains("fknrtd/gone", StringComparison.Ordinal),
            "and names the branch that may still have the work");
        True(said.Contains("rebuilds the worktree", StringComparison.Ordinal),
            "and says what running it again would do");

        // A task that genuinely never got one still gets the original wording.
        var second = new StringWriter();
        await QuietlyAsync(["task", "diff", fresh.Id, "-root", root], second).ConfigureAwait(false);
        // De-wrapped, like the assertion above: WriteParagraph breaks at the window width, and a
        // phrase that straddles the break is invisible to a raw Contains.
        var freshSaid = second.ToString().Replace((char)10, ' ').Replace((char)13, ' ');
        while (freshSaid.Contains("  ", StringComparison.Ordinal))
        {
            freshSaid = freshSaid.Replace("  ", " ", StringComparison.Ordinal);
        }

        True(freshSaid.Contains("has not reached its worktree stage", StringComparison.Ordinal),
            $"A task that never had a worktree is told exactly that: {freshSaid.Trim()}");
    }).ConfigureAwait(false);
}

/// <summary>
/// A task record whose title or brief is blank. Creating a task refuses both, so this can only
/// arrive by a hand edit or a half-finished write - at which point the pipeline drew a row with an
/// identifier and nothing beside it, which reads as a bug in the renderer rather than as a fact
/// about the task.
/// </summary>
static Task TestBlankTaskFieldsAreExplainedAsync()
{
    var blank = new WorkflowTask { Id = "FKN-20260917-000000-bare" };

    True(TaskText.Title(blank).Contains("no title", StringComparison.Ordinal),
        "A blank title is named as missing");
    True(TaskText.Title(blank).Contains("edited by hand", StringComparison.Ordinal),
        "and the only way it can have happened is said");
    True(TaskText.Brief(blank).Contains("no brief", StringComparison.Ordinal),
        "A blank brief is named as missing");
    True(TaskText.Brief(blank).Contains("nothing to act on", StringComparison.Ordinal),
        "and what running it would then do is said");

    // A real task is untouched: the fallback must not decorate anything that has a title.
    var real = new WorkflowTask { Id = "x", Title = "Add rate limiting", Brief = "Do the thing." };
    Equal("Add rate limiting", TaskText.Title(real), "A real title passes through unchanged");
    Equal("Do the thing.", TaskText.Brief(real), "and so does a real brief");

    // Whitespace counts as blank: a title of three spaces draws as an empty row just the same.
    var spaces = new WorkflowTask { Id = "y", Title = "   ", Brief = "  " };
    True(TaskText.Title(spaces).Contains("no title", StringComparison.Ordinal),
        "Whitespace is blank too");

    return Task.CompletedTask;
}
