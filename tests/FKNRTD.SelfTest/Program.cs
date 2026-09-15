using System.Reflection;
using System.Text;
using System.Text.Json;
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
    ("Claude usage parsing", TestClaudeUsageAsync),
    ("Codex rate-limit parsing", TestCodexUsageAsync),
    ("Collision classification", TestConflictDetectionAsync),
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
            if (!File.Exists("feature.txt"))
            {
                Console.WriteLine("FKNRTD_VERDICT: FAIL");
                return 1;
            }

            Console.WriteLine("Independent test audit passed.");
            Console.WriteLine("FKNRTD_VERDICT: PASS");
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
            DefaultBaseRef = "main",
            Agents = [fake]
        }).ConfigureAwait(false);
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

        task = await orchestrator.RunAsync(task.Id).ConfigureAwait(false);
        Equal(WorkflowStatus.ReadyToLand, task.Status, "Workflow ready state");
        Equal(StageState.Passed, task.Stage(WorkflowStage.Verify).State, "Verification state");
        Equal(StageState.Passed, task.Stage(WorkflowStage.Audit).State, "Audit state");
        Equal(Path.GetFullPath(root), WorkspaceLocator.Find(task.WorktreePath).Root,
            "Linked worktree resolves primary FKNRTD.CLI state");
        task = await orchestrator.LandAsync(task.Id).ConfigureAwait(false);
        Equal(WorkflowStatus.Landed, task.Status, "Landed state");
        True(File.Exists(Path.Combine(root, "feature.txt")), "Landed feature file");
        await worktrees.RemoveAsync(task, force: false).ConfigureAwait(false);
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
