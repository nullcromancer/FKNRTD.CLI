using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Text.Json;
using FKNRTD.Commands;
using FKNRTD.Dashboard;
using FKNRTD.Domain;
using FKNRTD.Services;
using FKNRTD.Telemetry;

if (args.FirstOrDefault() == "fake-agent")
{
    return await RunFakeAgentAsync(args.Skip(1).ToArray()).ConfigureAwait(false);
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
    ("End-to-end isolated workflow", TestWorkflowAsync)
};

var failures = new List<string>();
foreach (var test in tests)
{
    try
    {
        await test.Run().ConfigureAwait(false);
        Console.WriteLine($"✓ {test.Name}");
    }
    catch (Exception exception)
    {
        failures.Add(test.Name + ": " + exception.Message);
        Console.WriteLine($"✖ {test.Name}: {exception.Message}");
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
    var renderer = new DashboardApp(null!, null!, null!, null!, null!, null!);
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

        var logRenderer = new DashboardApp(null!, null!, null!, null!, null!, store);
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
        var fake = new AgentDefinition
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
        await store.InitializeAsync(new FknrtdConfig
        {
            ProjectName = "workflow-test",
            Agents = [fake]
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
        await worktrees.RemoveAsync(task, force: false).ConfigureAwait(false);
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
