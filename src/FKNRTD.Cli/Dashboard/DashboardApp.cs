using FKNRTD.Domain;
using FKNRTD.Services;
using FKNRTD.Telemetry;

namespace FKNRTD.Dashboard;

internal sealed class DashboardApp
{
    private readonly DashboardSnapshotService _snapshots;
    private readonly Orchestrator _orchestrator;
    private readonly TaskService _tasks;
    private readonly MessageService _messages;
    private readonly UsageService _usage;
    private readonly StateStore _store;
    private readonly Dictionary<string, Task> _running = new(StringComparer.OrdinalIgnoreCase);
    private int _selectedTask;
    private bool _quit;
    private string _toast = "Ready";
    private DashboardView _view = DashboardView.Overview;
    private CancellationTokenSource? _sessionCancellation;

    public DashboardApp(
        DashboardSnapshotService snapshots,
        Orchestrator orchestrator,
        TaskService tasks,
        MessageService messages,
        UsageService usage,
        StateStore store)
    {
        _snapshots = snapshots;
        _orchestrator = orchestrator;
        _tasks = tasks;
        _messages = messages;
        _usage = usage;
        _store = store;
    }

    public async Task RunAsync(bool once, bool useColor, CancellationToken cancellationToken)
    {
        if (once || Console.IsOutputRedirected || Console.IsInputRedirected)
        {
            var snapshot = await _snapshots.CaptureAsync(cancellationToken).ConfigureAwait(false);
            Console.WriteLine(Render(snapshot, GetWidth(140), GetHeight(40), useColor));
            return;
        }

        using var sessionCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _sessionCancellation = sessionCancellation;
        EnterScreen();
        try
        {
            while (!_quit && !cancellationToken.IsCancellationRequested)
            {
                RemoveCompletedRuns();
                var snapshot = await _snapshots.CaptureAsync(cancellationToken).ConfigureAwait(false);
                ClampSelection(snapshot);
                Console.Write("\u001b[H");
                Console.Write(Render(snapshot, GetWidth(120), GetHeight(32), useColor));

                var refresh = TimeSpan.FromMilliseconds(snapshot.Config.DashboardRefreshMilliseconds);
                var until = DateTimeOffset.UtcNow + refresh;
                while (!_quit && DateTimeOffset.UtcNow < until && !cancellationToken.IsCancellationRequested)
                {
                    if (Console.KeyAvailable)
                    {
                        var key = Console.ReadKey(intercept: true);
                        await HandleKeyAsync(key, snapshot, cancellationToken).ConfigureAwait(false);
                        break;
                    }

                    await Task.Delay(50, cancellationToken).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Normal application shutdown.
        }
        finally
        {
            sessionCancellation.Cancel();
            try
            {
                await Task.WhenAll(_running.Values).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is OperationCanceledException or InvalidOperationException)
            {
                // Active task processes are cancelled before the dashboard releases the terminal.
            }

            _sessionCancellation = null;
            ExitScreen();
        }
    }

    internal string Render(DashboardSnapshot snapshot, int width, int height, bool useColor)
    {
        width = Math.Max(60, width);
        height = Math.Max(20, height);
        var canvas = new Canvas(width, height);
        RenderHeader(canvas, snapshot, new Rect(0, 0, width, 4));
        var footer = new Rect(0, height - 2, width, 2);
        var body = new Rect(0, 4, width, height - 6);

        if (_view == DashboardView.Logs)
        {
            RenderLogs(canvas, snapshot, body);
        }
        else if (width >= 120 && height >= 28)
        {
            RenderWide(canvas, snapshot, body);
        }
        else if (width >= 84)
        {
            RenderMedium(canvas, snapshot, body);
        }
        else
        {
            RenderNarrow(canvas, snapshot, body);
        }

        RenderFooter(canvas, footer);
        return canvas.Render(useColor);
    }

    private void RenderWide(Canvas canvas, DashboardSnapshot snapshot, Rect body)
    {
        var topHeight = Math.Max(10, body.Height / 2);
        var bottomHeight = body.Height - topHeight;
        var firstWidth = body.Width * 30 / 100;
        var secondWidth = body.Width * 38 / 100;
        var thirdWidth = body.Width - firstWidth - secondWidth;

        RenderAgents(canvas, snapshot, new Rect(body.X, body.Y, firstWidth, topHeight));
        RenderPipeline(canvas, snapshot, new Rect(body.X + firstWidth, body.Y, secondWidth, topHeight));
        RenderQuality(canvas, snapshot, new Rect(body.X + firstWidth + secondWidth, body.Y, thirdWidth, topHeight));

        RenderMessages(canvas, snapshot, new Rect(body.X, body.Y + topHeight, firstWidth, bottomHeight));
        RenderConflicts(canvas, snapshot, new Rect(body.X + firstWidth, body.Y + topHeight, secondWidth, bottomHeight));
        RenderEvents(canvas, snapshot, new Rect(body.X + firstWidth + secondWidth, body.Y + topHeight, thirdWidth, bottomHeight));
    }

    private void RenderMedium(Canvas canvas, DashboardSnapshot snapshot, Rect body)
    {
        var leftWidth = body.Width / 2;
        var topHeight = body.Height / 2;
        RenderPipeline(canvas, snapshot, new Rect(body.X, body.Y, leftWidth, topHeight));
        RenderAgents(canvas, snapshot, new Rect(body.X + leftWidth, body.Y, body.Width - leftWidth, topHeight));
        RenderConflicts(canvas, snapshot, new Rect(body.X, body.Y + topHeight, leftWidth, body.Height - topHeight));
        RenderEvents(canvas, snapshot,
            new Rect(body.X + leftWidth, body.Y + topHeight, body.Width - leftWidth, body.Height - topHeight));
    }

    private void RenderNarrow(Canvas canvas, DashboardSnapshot snapshot, Rect body)
    {
        var pipelineHeight = Math.Max(8, body.Height / 2);
        RenderPipeline(canvas, snapshot, new Rect(body.X, body.Y, body.Width, pipelineHeight));
        RenderAgents(canvas, snapshot,
            new Rect(body.X, body.Y + pipelineHeight, body.Width, body.Height - pipelineHeight));
    }

    private static void RenderHeader(Canvas canvas, DashboardSnapshot snapshot, Rect rect)
    {
        canvas.DrawBox(rect, "◉ FKNRTD COMMAND CENTER", Theme.Cyan);
        var inner = rect.Inset();
        var risk = snapshot.Conflicts.FirstOrDefault();
        var riskText = risk is null
            ? "✓ SAFE"
            : risk.Kind == ConflictKind.Collision
                ? "✖ COLLISION"
                : "△ " + risk.Kind.ToString().ToUpperInvariant();
        var riskColor = risk is null ? Theme.Green : risk.Kind == ConflictKind.Collision ? Theme.Red : Theme.Amber;

        canvas.DrawText(inner.X, inner.Y,
            $"▣ {snapshot.Git.RepositoryName}  ⎇ {snapshot.Git.Branch}", Theme.Blue, bold: true,
            maxWidth: Math.Max(10, inner.Width - 32));
        var gitText = snapshot.Git.IsClean ? "✓ clean" : $"△ {snapshot.Git.ChangedFiles} changed";
        var right = $"{gitText}  ↑{snapshot.Git.Ahead}↓{snapshot.Git.Behind}  {riskText}";
        canvas.DrawText(Math.Max(inner.X, rect.Right - right.Length - 2), inner.Y, right, riskColor, bold: risk is not null,
            maxWidth: right.Length);

        var claude = snapshot.Usage.FirstOrDefault(item => item.AgentId.Equals("claude", StringComparison.OrdinalIgnoreCase));
        var codex = snapshot.Usage.FirstOrDefault(item => item.AgentId.Equals("codex", StringComparison.OrdinalIgnoreCase));
        var line = $"CTX {Percent(claude?.ContextRemainingPercent)} left  |  Claude 5h {Percent(claude?.FiveHourRemainingPercent)} 7d {Percent(claude?.WeeklyRemainingPercent)}  |  Codex 5h {Percent(codex?.FiveHourRemainingPercent)} 7d {Percent(codex?.WeeklyRemainingPercent)}";
        canvas.DrawText(inner.X, inner.Y + 1, Text.Truncate(line, inner.Width), Theme.Foreground,
            maxWidth: inner.Width);
    }

    private void RenderAgents(Canvas canvas, DashboardSnapshot snapshot, Rect rect)
    {
        canvas.DrawBox(rect, "AGENT RADAR", Theme.Violet);
        var inner = rect.Inset();
        var row = inner.Y;
        foreach (var definition in snapshot.Config.Agents.Take(inner.Height))
        {
            var runtime = snapshot.Agents.FirstOrDefault(item =>
                item.AgentId.Equals(definition.Id, StringComparison.OrdinalIgnoreCase));
            var state = runtime?.State ??
                        (ExecutableLocator.Find(definition.Executable) is null
                            ? AgentActivityState.Offline
                            : AgentActivityState.Idle);
            var role = runtime is null || runtime.Role == AgentRole.Observer ? string.Empty : $" {runtime.Role}";
            var prefix = $"{StateIcon(state)} {definition.DisplayName}{role}";
            canvas.DrawText(inner.X, row, Text.Truncate(prefix, Math.Min(24, inner.Width)),
                Theme.Agent(definition.Kind), bold: state is AgentActivityState.Running or AgentActivityState.Blocked,
                maxWidth: inner.Width);
            if (inner.Width > 34)
            {
                var intentWidth = inner.Width - Math.Min(24, inner.Width) - 1;
                canvas.DrawText(inner.X + Math.Min(24, inner.Width) + 1, row,
                    Text.Truncate(runtime?.Intent ?? state.ToString(), intentWidth),
                    state == AgentActivityState.Blocked ? Theme.Red : Theme.Muted, maxWidth: intentWidth);
            }

            row++;
            if (row >= inner.Bottom)
            {
                break;
            }
        }

        if (row < inner.Bottom)
        {
            var active = snapshot.Agents.Count(item => item.State is AgentActivityState.Running or
                AgentActivityState.Planning or AgentActivityState.Reviewing);
            canvas.DrawText(inner.X, row, $"Team active: {active}/{snapshot.Config.Agents.Count}", Theme.Muted,
                maxWidth: inner.Width);
        }
    }

    private void RenderPipeline(Canvas canvas, DashboardSnapshot snapshot, Rect rect)
    {
        canvas.DrawBox(rect, "PIPELINE", Theme.Blue);
        var inner = rect.Inset();
        if (snapshot.Tasks.Count == 0)
        {
            canvas.DrawText(inner.X, inner.Y, "No tasks. Press N to create one.", Theme.Muted, maxWidth: inner.Width);
            return;
        }

        var selected = SelectedTask(snapshot);
        var row = inner.Y;
        var maxTaskRows = Math.Max(1, Math.Min(4, inner.Height - 4));
        for (var index = 0; index < Math.Min(snapshot.Tasks.Count, maxTaskRows); index++)
        {
            var task = snapshot.Tasks[index];
            var selectedMarker = index == _selectedTask ? "›" : " ";
            var line = $"{selectedMarker} {StatusIcon(task.Status)} {task.Id} {task.Title}";
            canvas.DrawText(inner.X, row++, Text.Truncate(line, inner.Width),
                index == _selectedTask ? Theme.Foreground : Theme.Muted,
                bold: index == _selectedTask,
                maxWidth: inner.Width);
        }

        if (selected is null || row >= inner.Bottom)
        {
            return;
        }

        row++;
        var stages = string.Join(' ', selected.Stages.Select(stage =>
            $"{StageAbbreviation(stage.Stage)}{StageIcon(stage.State)}"));
        canvas.DrawText(inner.X, row++, Text.Truncate(stages, inner.Width), Theme.Foreground, maxWidth: inner.Width);
        if (row < inner.Bottom)
        {
            var progress = selected.Stages.Count == 0
                ? 0
                : selected.Stages.Count(stage => stage.State is StageState.Passed or StageState.Skipped) * 100.0 /
                  selected.Stages.Count;
            canvas.DrawGauge(inner.X, row++, Math.Min(inner.Width, 30), progress, Theme.Blue);
        }

        if (row < inner.Bottom)
        {
            canvas.DrawText(inner.X, row,
                Text.Truncate($"Lead {selected.LeadAgentId}  Implement {selected.ImplementerAgentId}  Audit {selected.AuditorAgentId}  Repair {selected.RepairRound}/{selected.MaxRepairRounds}", inner.Width),
                Theme.Muted,
                maxWidth: inner.Width);
        }
    }

    private void RenderQuality(Canvas canvas, DashboardSnapshot snapshot, Rect rect)
    {
        canvas.DrawBox(rect, "CI + USAGE", Theme.Green);
        var inner = rect.Inset();
        var task = SelectedTask(snapshot);
        var row = inner.Y;
        if (task is not null)
        {
            var quality = task.Quality;
            canvas.DrawText(inner.X, row++, $"BUILD {StageIcon(quality.Build)}  TEST {StageIcon(quality.Tests)}  LINT {StageIcon(quality.Lint)}", Theme.Foreground,
                maxWidth: inner.Width);
            if (quality.TestsPassed + quality.TestsFailed > 0 && row < inner.Bottom)
            {
                canvas.DrawText(inner.X, row++, $"Tests {quality.TestsPassed}/{quality.TestsPassed + quality.TestsFailed}  Fail {quality.TestsFailed}",
                    quality.TestsFailed == 0 ? Theme.Green : Theme.Red, maxWidth: inner.Width);
            }
        }

        foreach (var agentId in new[] { "claude", "codex" })
        {
            if (row >= inner.Bottom)
            {
                break;
            }

            var usage = snapshot.Usage.FirstOrDefault(item => item.AgentId.Equals(agentId, StringComparison.OrdinalIgnoreCase));
            canvas.DrawText(inner.X, row, char.ToUpperInvariant(agentId[0]) + agentId[1..] + " 5h", Theme.Agent(agentId),
                maxWidth: Math.Min(12, inner.Width));
            if (inner.Width > 14)
            {
                canvas.DrawGauge(inner.X + 12, row, inner.Width - 12, usage?.FiveHourRemainingPercent,
                    UsageColor(usage?.FiveHourRemainingPercent));
            }

            row++;
            if (row < inner.Bottom)
            {
                canvas.DrawText(inner.X, row, "        7d", Theme.Agent(agentId), maxWidth: Math.Min(12, inner.Width));
                if (inner.Width > 14)
                {
                    canvas.DrawGauge(inner.X + 12, row, inner.Width - 12, usage?.WeeklyRemainingPercent,
                        UsageColor(usage?.WeeklyRemainingPercent));
                }

                row++;
            }
        }

        if (row < inner.Bottom)
        {
            canvas.DrawText(inner.X, row,
                $"FKNRTD.CLI CPU {snapshot.Resources.ProcessCpuPercent:0}%  RAM {snapshot.Resources.WorkingSetBytes / 1024d / 1024d:0} MB",
                Theme.Muted,
                maxWidth: inner.Width);
        }
    }

    private static void RenderMessages(Canvas canvas, DashboardSnapshot snapshot, Rect rect)
    {
        canvas.DrawBox(rect, "MESSAGE BUS", Theme.Orange);
        var inner = rect.Inset();
        if (snapshot.Messages.Count == 0)
        {
            canvas.DrawText(inner.X, inner.Y, "No messages.", Theme.Muted, maxWidth: inner.Width);
            return;
        }

        var row = inner.Y;
        foreach (var message in snapshot.Messages.Take(inner.Height))
        {
            var delivery = message.Delivery == MessageDelivery.Acknowledged ? "✓" : "↪";
            var line = $"{delivery} {message.FromAgentId}>{message.ToAgentId} {Text.Truncate(message.Text, Math.Max(1, inner.Width - 20))} {Text.Age(message.CreatedAt, snapshot.CapturedAt)}";
            canvas.DrawText(inner.X, row++, Text.Truncate(line, inner.Width), Theme.Foreground, maxWidth: inner.Width);
        }
    }

    private static void RenderConflicts(Canvas canvas, DashboardSnapshot snapshot, Rect rect)
    {
        var worst = snapshot.Conflicts.FirstOrDefault();
        var color = worst is null ? Theme.Green : worst.Kind == ConflictKind.Collision ? Theme.Red : Theme.Amber;
        canvas.DrawBox(rect, "CONFLICT SENTINEL", color);
        var inner = rect.Inset();
        if (worst is null)
        {
            canvas.DrawText(inner.X, inner.Y, "✓ SAFE  No path overlap detected", Theme.Green, bold: true,
                maxWidth: inner.Width);
            var worktrees = snapshot.Agents
                .Where(agent => !string.IsNullOrWhiteSpace(agent.Worktree))
                .Select(agent => $"{agent.AgentId}: {Path.GetFileName(agent.Worktree)}")
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(Math.Max(0, inner.Height - 1));
            var row = inner.Y + 1;
            foreach (var worktree in worktrees)
            {
                canvas.DrawText(inner.X, row++, Text.Truncate(worktree, inner.Width), Theme.Muted,
                    maxWidth: inner.Width);
            }

            return;
        }

        var y = inner.Y;
        foreach (var conflict in snapshot.Conflicts.Take(inner.Height / 2 + 1))
        {
            canvas.DrawText(inner.X, y++,
                Text.Truncate($"{(conflict.Kind == ConflictKind.Collision ? "✖" : "△")} {conflict.RiskScore} {conflict.Summary}", inner.Width),
                conflict.Kind == ConflictKind.Collision ? Theme.Red : Theme.Amber,
                bold: true,
                maxWidth: inner.Width);
            if (y < inner.Bottom)
            {
                canvas.DrawText(inner.X + 2, y++, Text.Truncate(string.Join(", ", conflict.Paths), inner.Width - 2),
                    Theme.Foreground, maxWidth: inner.Width - 2);
            }
        }
    }

    private static void RenderEvents(Canvas canvas, DashboardSnapshot snapshot, Rect rect)
    {
        canvas.DrawBox(rect, "EVENTS", Theme.Cyan);
        var inner = rect.Inset();
        var row = inner.Y;
        foreach (var item in snapshot.Events.Take(inner.Height))
        {
            var line = $"{item.Timestamp:HH:mm:ss} {EventIcon(item.Severity)} {item.Message}";
            canvas.DrawText(inner.X, row++, Text.Truncate(line, inner.Width), EventColor(item.Severity),
                maxWidth: inner.Width);
        }

        if (row == inner.Y)
        {
            canvas.DrawText(inner.X, row, "Waiting for the first event.", Theme.Muted, maxWidth: inner.Width);
        }
    }

    private void RenderLogs(Canvas canvas, DashboardSnapshot snapshot, Rect rect)
    {
        canvas.DrawBox(rect, "TASK LOG", Theme.Cyan);
        var inner = rect.Inset();
        var task = SelectedTask(snapshot);
        if (task is null)
        {
            canvas.DrawText(inner.X, inner.Y, "No task selected.", Theme.Muted, maxWidth: inner.Width);
            return;
        }

        var path = _store.TaskLogPath(task.Id, task.CurrentStage, task.RepairRound);
        if (!File.Exists(path))
        {
            var directory = Path.GetDirectoryName(path);
            path = directory is not null && Directory.Exists(directory)
                ? Directory.EnumerateFiles(directory, "*.log")
                    .OrderByDescending(File.GetLastWriteTimeUtc)
                    .FirstOrDefault() ?? path
                : path;
        }

        canvas.DrawText(inner.X, inner.Y, Text.Truncate(path, inner.Width), Theme.Muted, maxWidth: inner.Width);
        if (!File.Exists(path))
        {
            canvas.DrawText(inner.X, inner.Y + 2, "No log exists for the current stage.", Theme.Muted,
                maxWidth: inner.Width);
            return;
        }

        var lines = File.ReadLines(path).TakeLast(Math.Max(0, inner.Height - 2)).ToArray();
        var row = inner.Y + 1;
        foreach (var line in lines)
        {
            canvas.DrawText(inner.X, row++, Text.Truncate(line, inner.Width), Theme.Foreground, maxWidth: inner.Width);
        }
    }

    private void RenderFooter(Canvas canvas, Rect rect)
    {
        canvas.DrawText(rect.X, rect.Y,
            Text.Truncate("[↑↓] Select  [Enter] Run  [N] New  [C] Cancel  [G] Land  [M] Message  [L] Logs  [U] Usage  [Tab] View  [Q] Quit", rect.Width),
            Theme.Blue,
            bold: true,
            maxWidth: rect.Width);
        canvas.DrawText(rect.X, rect.Y + 1, Text.Truncate($"{_view} | {_toast}", rect.Width), Theme.Muted,
            maxWidth: rect.Width);
    }

    private async Task HandleKeyAsync(ConsoleKeyInfo key, DashboardSnapshot snapshot, CancellationToken cancellationToken)
    {
        switch (key.Key)
        {
            case ConsoleKey.Q:
            case ConsoleKey.Escape:
                _quit = true;
                break;
            case ConsoleKey.UpArrow:
                _selectedTask = Math.Max(0, _selectedTask - 1);
                break;
            case ConsoleKey.DownArrow:
                _selectedTask = Math.Min(Math.Max(0, snapshot.Tasks.Count - 1), _selectedTask + 1);
                break;
            case ConsoleKey.Enter:
                StartSelected(snapshot);
                break;
            case ConsoleKey.C:
                if (SelectedTask(snapshot) is { } cancelTask)
                {
                    await _tasks.RequestCancellationAsync(cancelTask.Id, cancellationToken).ConfigureAwait(false);
                    _toast = $"Cancellation requested for {cancelTask.Id}";
                }
                break;
            case ConsoleKey.G:
                await LandSelectedAsync(snapshot, cancellationToken).ConfigureAwait(false);
                break;
            case ConsoleKey.N:
                await CreateTaskInteractivelyAsync(snapshot, cancellationToken).ConfigureAwait(false);
                break;
            case ConsoleKey.M:
                await SendMessageInteractivelyAsync(cancellationToken).ConfigureAwait(false);
                break;
            case ConsoleKey.L:
                _view = _view == DashboardView.Logs ? DashboardView.Overview : DashboardView.Logs;
                break;
            case ConsoleKey.U:
                await RefreshUsageAsync(cancellationToken).ConfigureAwait(false);
                break;
            case ConsoleKey.Tab:
                _view = _view switch
                {
                    DashboardView.Overview => DashboardView.Logs,
                    _ => DashboardView.Overview
                };
                break;
        }
    }

    private void StartSelected(DashboardSnapshot snapshot)
    {
        var task = SelectedTask(snapshot);
        if (task is null)
        {
            _toast = "No task selected";
            return;
        }

        if (_running.ContainsKey(task.Id) || task.Status == WorkflowStatus.Running)
        {
            _toast = $"{task.Id} is already running";
            return;
        }

        if (_running.Count >= snapshot.Config.MaxParallelAgents)
        {
            _toast = $"Parallel limit reached ({snapshot.Config.MaxParallelAgents})";
            return;
        }

        var cancellationToken = _sessionCancellation?.Token ?? CancellationToken.None;
        _running[task.Id] = Task.Run(async () =>
        {
            try
            {
                await _orchestrator.RunAsync(task.Id, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not StackOverflowException &&
                                              exception is not OutOfMemoryException)
            {
                _toast = $"{task.Id}: {exception.Message}";
            }
        }, cancellationToken);
        _toast = $"Started {task.Id}";
    }

    private async Task LandSelectedAsync(DashboardSnapshot snapshot, CancellationToken cancellationToken)
    {
        var task = SelectedTask(snapshot);
        if (task is null)
        {
            _toast = "No task selected";
            return;
        }

        var answer = Prompt($"Merge {task.BranchName} into {task.BaseRef}? Type LAND to confirm: ");
        if (!answer.Equals("LAND", StringComparison.Ordinal))
        {
            _toast = "Landing cancelled";
            return;
        }

        try
        {
            await _orchestrator.LandAsync(task.Id, cancellationToken).ConfigureAwait(false);
            _toast = $"Landed {task.Id}";
        }
        catch (Exception exception)
        {
            _toast = exception.Message;
        }
    }

    private async Task CreateTaskInteractivelyAsync(DashboardSnapshot snapshot, CancellationToken cancellationToken)
    {
        var title = Prompt("Task title: ");
        var brief = Prompt("Task brief: ");
        var lead = Prompt($"Lead agent [{snapshot.Config.Agents.FirstOrDefault()?.Id ?? "claude"}]: ");
        var implementer = Prompt($"Implementer [{snapshot.Config.Agents.Skip(1).FirstOrDefault()?.Id ?? "codex"}]: ");
        var auditor = Prompt($"Auditor [{snapshot.Config.Agents.FirstOrDefault()?.Id ?? "claude"}]: ");
        var verification = Prompt("Verification command (blank for none): ");
        lead = string.IsNullOrWhiteSpace(lead) ? snapshot.Config.Agents.FirstOrDefault()?.Id ?? "claude" : lead;
        implementer = string.IsNullOrWhiteSpace(implementer)
            ? snapshot.Config.Agents.Skip(1).FirstOrDefault()?.Id ?? lead
            : implementer;
        auditor = string.IsNullOrWhiteSpace(auditor) ? lead : auditor;
        try
        {
            var task = await _tasks.CreateAsync(
                    title,
                    brief,
                    lead,
                    implementer,
                    auditor,
                    string.IsNullOrWhiteSpace(verification) ? [] : [verification],
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            _selectedTask = 0;
            _toast = $"Created {task.Id}";
        }
        catch (Exception exception)
        {
            _toast = exception.Message;
        }
    }

    private async Task SendMessageInteractivelyAsync(CancellationToken cancellationToken)
    {
        var from = Prompt("From agent: ");
        var to = Prompt("To agent: ");
        var text = Prompt("Message: ");
        try
        {
            await _messages.SendAsync(from, to, text, cancellationToken: cancellationToken).ConfigureAwait(false);
            _toast = $"Message sent from {from} to {to}";
        }
        catch (Exception exception)
        {
            _toast = exception.Message;
        }
    }

    private async Task RefreshUsageAsync(CancellationToken cancellationToken)
    {
        _toast = "Refreshing Codex usage";
        try
        {
            var snapshot = await _usage.RefreshCodexAsync(cancellationToken).ConfigureAwait(false);
            _toast = $"Codex usage refreshed: 5h {Percent(snapshot.FiveHourRemainingPercent)}, 7d {Percent(snapshot.WeeklyRemainingPercent)}";
        }
        catch (Exception exception)
        {
            _toast = "Usage refresh failed: " + exception.Message;
        }
    }

    private static string Prompt(string label)
    {
        ExitScreen();
        Console.Write(label);
        var value = Console.ReadLine() ?? string.Empty;
        EnterScreen();
        return value.Trim();
    }

    private WorkflowTask? SelectedTask(DashboardSnapshot snapshot) =>
        snapshot.Tasks.Count == 0 ? null : snapshot.Tasks[Math.Clamp(_selectedTask, 0, snapshot.Tasks.Count - 1)];

    private void ClampSelection(DashboardSnapshot snapshot) =>
        _selectedTask = snapshot.Tasks.Count == 0 ? 0 : Math.Clamp(_selectedTask, 0, snapshot.Tasks.Count - 1);

    private void RemoveCompletedRuns()
    {
        foreach (var completed in _running.Where(pair => pair.Value.IsCompleted).Select(pair => pair.Key).ToArray())
        {
            _running.Remove(completed);
        }
    }

    private static void EnterScreen()
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        Console.Write("\u001b[?1049h\u001b[?25l\u001b[2J\u001b[H");
    }

    private static void ExitScreen()
    {
        Console.Write("\u001b[0m\u001b[?25h\u001b[?1049l");
    }

    private static int GetWidth(int fallback)
    {
        try
        {
            return Math.Max(60, Console.WindowWidth);
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or
                                          PlatformNotSupportedException)
        {
            return fallback;
        }
    }

    private static int GetHeight(int fallback)
    {
        try
        {
            return Math.Max(20, Console.WindowHeight);
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or
                                          PlatformNotSupportedException)
        {
            return fallback;
        }
    }

    private static string Percent(double? value) => value is null ? "N/A" : $"{value:0}%";

    private static Rgb UsageColor(double? remaining) => remaining switch
    {
        null => Theme.Muted,
        >= 60 => Theme.Green,
        >= 40 => Theme.Amber,
        >= 20 => Theme.Orange,
        _ => Theme.Red
    };

    private static string StateIcon(AgentActivityState state) => state switch
    {
        AgentActivityState.Planning => "◇",
        AgentActivityState.Running => "▶",
        AgentActivityState.Reviewing => "◆",
        AgentActivityState.Waiting => "⏸",
        AgentActivityState.Blocked => "■",
        AgentActivityState.Failed => "✖",
        AgentActivityState.Completed => "✓",
        AgentActivityState.Idle => "○",
        AgentActivityState.Offline => "○",
        _ => "?"
    };

    private static string StatusIcon(WorkflowStatus status) => status switch
    {
        WorkflowStatus.Running => "▶",
        WorkflowStatus.ReadyToLand => "◆",
        WorkflowStatus.Landed => "✓",
        WorkflowStatus.Failed => "✖",
        WorkflowStatus.Cancelled => "⏸",
        WorkflowStatus.Waiting => "⏸",
        _ => "○"
    };

    private static string StageIcon(StageState state) => state switch
    {
        StageState.Running => "▶",
        StageState.Passed => "✓",
        StageState.Failed => "✖",
        StageState.Skipped => "◇",
        _ => "○"
    };

    private static string StageAbbreviation(WorkflowStage stage) => stage switch
    {
        WorkflowStage.Brief => "b",
        WorkflowStage.Worktree => "w",
        WorkflowStage.Plan => "p",
        WorkflowStage.Implement => "i",
        WorkflowStage.Verify => "v",
        WorkflowStage.Audit => "a",
        WorkflowStage.ReadyToLand => "r",
        WorkflowStage.Land => "l",
        _ => "?"
    };

    private static string EventIcon(EventSeverity severity) => severity switch
    {
        EventSeverity.Success => "✓",
        EventSeverity.Warning => "△",
        EventSeverity.Error => "✖",
        EventSeverity.Critical => "✖",
        _ => "·"
    };

    private static Rgb EventColor(EventSeverity severity) => severity switch
    {
        EventSeverity.Success => Theme.Green,
        EventSeverity.Warning => Theme.Amber,
        EventSeverity.Error => Theme.Red,
        EventSeverity.Critical => Theme.Red,
        _ => Theme.Muted
    };

    private enum DashboardView
    {
        Overview,
        Logs
    }
}
