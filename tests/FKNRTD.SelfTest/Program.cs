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
    ("Overlay review regressions stay fixed", TestOverlayReviewRegressionsAsync)
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
    var renderer = new DashboardApp(null!, null!, null!, null!, null!, null!, null!, null!);
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
    Equal("├", DisplayCell(narrow[16], 0), "Narrow joined left border");
    Equal("┤", DisplayCell(narrow[16], 71), "Narrow joined right border");

    var medium = FrameLines(renderer.Render(snapshot, 100, 32, useColor: false));
    Equal("┬", DisplayCell(medium[4], 49), "Medium upper junction");
    Equal("┼", DisplayCell(medium[16], 49), "Medium center junction");

    var wide = FrameLines(renderer.Render(snapshot, 120, 32, useColor: false));
    Equal("┬", DisplayCell(wide[4], 35), "Wide first upper junction");
    Equal("┬", DisplayCell(wide[4], 80), "Wide second upper junction");
    Equal("┼", DisplayCell(wide[16], 35), "Wide first center junction");
    Equal("┼", DisplayCell(wide[16], 80), "Wide second center junction");

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

        var logRenderer = new DashboardApp(null!, null!, null!, null!, null!, store, null!, null!);
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
            var frame = new DashboardApp(null!, null!, null!, null!, null!, null!, null!, null!)
                .Render(Scenes.EmptySnapshot(), 110, 40, useColor: false, wizard);
            True(frame.Contains("WHAT THIS IS", StringComparison.Ordinal),
                "Every wizard step shows an explanation");
            True(frame.Contains("Esc cancel", StringComparison.Ordinal),
                "Every wizard step says how to leave it");
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
    True(Keymap.Footer.Length is > 3 and < 9, "Keymap footer is a usable size");
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
    }

    return Task.CompletedTask;
}

/// <summary>A refused answer has to say what was wrong with it, not merely refuse.</summary>
static Task TestWizardValidationExplainsItselfAsync()
{
    var wizard = TaskWizard.Create(Scenes.SampleConfig());
    var renderer = new DashboardApp(null!, null!, null!, null!, null!, null!, null!, null!);

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
    Scenes.Press(choices, ConsoleKey.DownArrow);
    Scenes.Press(choices, ConsoleKey.Tab, shift: true);
    Scenes.Press(choices, ConsoleKey.Enter);
    Scenes.Press(choices, ConsoleKey.Enter);
    Equal("codex", choices.Value("lead"), "A moved choice survives stepping back and returning");
    return Task.CompletedTask;
}

/// <summary>A destructive action must take the whole word and nothing else.</summary>
static Task TestConfirmationRequiresTheWordAsync()
{
    var confirmation = new Confirmation("LAND THIS TASK", Theme.Green, "FKN-1 — Subject",
        "This merges the task branch into main and cannot be undone from here.", "LAND", "land");
    var renderer = new DashboardApp(null!, null!, null!, null!, null!, null!, null!, null!);

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
    True(logs.Contains("Press I to see the full record", StringComparison.Ordinal),
        "An absent log explains itself and names a next step");

    // And it must not tell the operator to open the view they are already looking at.
    True(!logs.Contains("Press L to read", StringComparison.Ordinal),
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
    var renderer = new DashboardApp(null!, null!, null!, null!, null!, null!, null!, null!);

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

    // A running task cannot be landed, and the palette says so in those words.
    var mid = new Palette(() => Palette.Build(snapshot, running, running: 1));
    Scenes.Type(mid, "land");
    frame = renderer.Render(snapshot, 110, 40, useColor: false, mid);
    True(frame.Contains("verified, audited", StringComparison.Ordinal),
        "The palette explains why a running task cannot land");
    Equal(OverlayResult.Continue, Scenes.Press(mid, ConsoleKey.Enter), "Landing a running task is refused");

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
