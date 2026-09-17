using FKNRTD.Domain;
using FKNRTD.Help;
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
    private readonly WorktreeService _worktrees;
    private readonly DoctorService _doctor;
    private readonly Dictionary<string, Task> _running = new(StringComparer.OrdinalIgnoreCase);
    private int _selectedTask;
    private string? _selectedTaskId;
    private bool _quit;
    private string _toast = "Ready";
    private DashboardView _view = DashboardView.Overview;
    private CancellationTokenSource? _sessionCancellation;
    private bool _welcomed;

    /// <summary>
    /// The modal layer. Every question the dashboard asks is an overlay drawn over the frame, so the
    /// operator never drops out of the alternate screen to answer an unexplained prompt on a blank
    /// terminal — and can still see the task they are acting on while they answer.
    /// </summary>
    private IOverlay? _overlay;

    private Func<IOverlay, CancellationToken, Task>? _overlayCompleted;

    public DashboardApp(
        DashboardSnapshotService snapshots,
        Orchestrator orchestrator,
        TaskService tasks,
        MessageService messages,
        UsageService usage,
        StateStore store,
        WorktreeService worktrees,
        DoctorService doctor)
    {
        _snapshots = snapshots;
        _orchestrator = orchestrator;
        _tasks = tasks;
        _messages = messages;
        _usage = usage;
        _store = store;
        _worktrees = worktrees;
        _doctor = doctor;
    }

    public async Task RunAsync(
        bool once,
        bool useColor,
        int? widthOverride,
        int? heightOverride,
        CancellationToken cancellationToken)
    {
        if (once || Console.IsOutputRedirected || Console.IsInputRedirected)
        {
            var snapshot = await _snapshots.CaptureAsync(cancellationToken).ConfigureAwait(false);
            Console.WriteLine(Render(snapshot, widthOverride ?? GetWidth(140), heightOverride ?? GetHeight(40), useColor));
            return;
        }

        using var sessionCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _sessionCancellation = sessionCancellation;
        EnterScreen();
        var previousWidth = -1;
        var previousHeight = -1;
        try
        {
            while (!_quit && !cancellationToken.IsCancellationRequested)
            {
                RemoveCompletedRuns();
                var snapshot = await _snapshots.CaptureAsync(cancellationToken).ConfigureAwait(false);
                ClampSelection(snapshot);

                // A workspace with nothing in it opens on the introduction rather than on an empty
                // grid, because a command center that shows no state teaches nothing about itself.
                if (!_welcomed)
                {
                    _welcomed = true;
                    if (snapshot.Tasks.Count == 0)
                    {
                        _overlay = Reference.Welcome(snapshot.Config);
                        _toast = "Press Esc to close this. ? reopens it any time.";
                    }
                }

                var width = widthOverride ?? GetWidth(120);
                var height = heightOverride ?? GetHeight(32);
                Console.Write(width == previousWidth && height == previousHeight ? "\u001b[H" : "\u001b[2J\u001b[H");
                Console.Write(RenderCurrent(snapshot, width, height, useColor));
                previousWidth = width;
                previousHeight = height;

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

    internal string Render(DashboardSnapshot snapshot, int width, int height, bool useColor) =>
        RenderFrame(snapshot, width, height, useColor, 0, DashboardView.Overview, "Ready", null);

    internal string Render(DashboardSnapshot snapshot, int width, int height, bool useColor, int selectedTaskIndex) =>
        RenderFrame(snapshot, width, height, useColor, selectedTaskIndex, DashboardView.Overview, "Ready", null);

    internal string RenderLog(DashboardSnapshot snapshot, int width, int height, int selectedTaskIndex) =>
        RenderFrame(snapshot, width, height, useColor: false, selectedTaskIndex, DashboardView.Logs, "Ready", null);

    /// <summary>Renders a frame with a modal layer over it. The renderer's test seam for overlays.</summary>
    internal string Render(DashboardSnapshot snapshot, int width, int height, bool useColor, IOverlay overlay) =>
        RenderFrame(snapshot, width, height, useColor, 0, DashboardView.Overview, "Ready", overlay);

    private string RenderCurrent(DashboardSnapshot snapshot, int width, int height, bool useColor) =>
        RenderFrame(snapshot, width, height, useColor, _selectedTask, _view, _toast, _overlay);

    private string RenderFrame(
        DashboardSnapshot snapshot,
        int width,
        int height,
        bool useColor,
        int selectedTaskIndex,
        DashboardView view,
        string toast,
        IOverlay? overlay)
    {
        width = Math.Max(60, width);
        height = Math.Max(20, height);
        var canvas = new Canvas(width, height);
        RenderHeader(canvas, snapshot, new Rect(0, 0, width, 4));
        var footer = new Rect(0, height - 2, width, 2);
        var body = new Rect(0, 4, width, height - 6);

        if (view == DashboardView.Logs)
        {
            RenderLogs(canvas, snapshot, body, selectedTaskIndex);
        }
        else if (width >= 120)
        {
            RenderWide(canvas, snapshot, body, selectedTaskIndex);
        }
        else if (width >= 84)
        {
            RenderMedium(canvas, snapshot, body, selectedTaskIndex);
        }
        else
        {
            RenderNarrow(canvas, snapshot, body, selectedTaskIndex);
        }

        RenderFooter(canvas, footer, view, toast, overlay, snapshot, selectedTaskIndex);
        overlay?.Draw(canvas, new Rect(0, 0, width, height));
        return canvas.Render(useColor);
    }

    private void RenderWide(Canvas canvas, DashboardSnapshot snapshot, Rect body, int selectedTaskIndex)
    {
        var topHeight = Math.Max(10, body.Height / 2);
        var bottomHeight = body.Height - topHeight + 1;
        var joinedWidth = body.Width + 2;
        var firstWidth = joinedWidth * 30 / 100;
        var secondWidth = joinedWidth * 38 / 100;
        var thirdWidth = joinedWidth - firstWidth - secondWidth;
        var secondX = body.X + firstWidth - 1;
        var thirdX = secondX + secondWidth - 1;
        var bottomY = body.Y + topHeight - 1;

        RenderAgents(canvas, snapshot, new Rect(body.X, body.Y, firstWidth, topHeight));
        RenderPipeline(canvas, snapshot, new Rect(secondX, body.Y, secondWidth, topHeight), selectedTaskIndex);
        RenderQuality(canvas, snapshot, new Rect(thirdX, body.Y, thirdWidth, topHeight), selectedTaskIndex);

        RenderMessages(canvas, snapshot, new Rect(body.X, bottomY, firstWidth, bottomHeight));
        RenderConflicts(canvas, snapshot, new Rect(secondX, bottomY, secondWidth, bottomHeight));
        RenderEvents(canvas, snapshot, new Rect(thirdX, bottomY, thirdWidth, bottomHeight));
    }

    private void RenderMedium(Canvas canvas, DashboardSnapshot snapshot, Rect body, int selectedTaskIndex)
    {
        var leftWidth = (body.Width + 1) / 2;
        var rightWidth = body.Width - leftWidth + 1;
        var topHeight = body.Height / 2;
        var bottomHeight = body.Height - topHeight + 1;
        var rightX = body.X + leftWidth - 1;
        var bottomY = body.Y + topHeight - 1;
        RenderPipeline(canvas, snapshot, new Rect(body.X, body.Y, leftWidth, topHeight), selectedTaskIndex);
        RenderAgents(canvas, snapshot, new Rect(rightX, body.Y, rightWidth, topHeight));
        RenderConflicts(canvas, snapshot, new Rect(body.X, bottomY, leftWidth, bottomHeight));
        RenderEvents(canvas, snapshot,
            new Rect(rightX, bottomY, rightWidth, bottomHeight));
    }

    private void RenderNarrow(Canvas canvas, DashboardSnapshot snapshot, Rect body, int selectedTaskIndex)
    {
        var pipelineHeight = Math.Max(8, body.Height / 2);
        RenderPipeline(canvas, snapshot, new Rect(body.X, body.Y, body.Width, pipelineHeight), selectedTaskIndex);
        RenderAgents(canvas, snapshot,
            new Rect(body.X, body.Y + pipelineHeight - 1, body.Width, body.Height - pipelineHeight + 1));
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

        // A standalone workspace has no branch, cleanliness or divergence to report.
        var located = snapshot.Git.IsRepository
            ? $"⎇ {snapshot.Git.Branch}"
            : "○ standalone";
        canvas.DrawText(inner.X, inner.Y,
            $"▣ {snapshot.Git.RepositoryName}  {located}", Theme.Blue, bold: true,
            maxWidth: Math.Max(10, inner.Width - 32));
        var gitText = snapshot.Git.IsClean ? "✓ clean" : $"△ {snapshot.Git.ChangedFiles} changed";
        var right = snapshot.Git.IsRepository
            ? $"{gitText}  ↑{snapshot.Git.Ahead}↓{snapshot.Git.Behind}  {riskText}"
            : riskText;
        var rightWidth = Math.Min(inner.Width, Text.DisplayWidth(right));
        canvas.DrawText(Math.Max(inner.X, rect.Right - rightWidth - 2), inner.Y, right, riskColor,
            bold: risk is not null, maxWidth: rightWidth);

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
            var state = runtime?.State ?? AgentActivityState.Offline;
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

    private static void RenderPipeline(
        Canvas canvas,
        DashboardSnapshot snapshot,
        Rect rect,
        int selectedTaskIndex)
    {
        canvas.DrawBox(rect, "PIPELINE", Theme.Blue);
        var inner = rect.Inset();
        if (snapshot.Tasks.Count == 0)
        {
            canvas.DrawText(inner.X, inner.Y, "No tasks. Press N to create one.", Theme.Muted, maxWidth: inner.Width);
            return;
        }

        selectedTaskIndex = Math.Clamp(selectedTaskIndex, 0, snapshot.Tasks.Count - 1);
        var selected = SelectedTask(snapshot, selectedTaskIndex);
        var row = inner.Y;
        var maxTaskRows = Math.Max(1, Math.Min(5, inner.Height - 4));
        var visibleTasks = Math.Min(snapshot.Tasks.Count, maxTaskRows);
        var firstTask = Math.Clamp(selectedTaskIndex - visibleTasks / 2, 0, snapshot.Tasks.Count - visibleTasks);
        for (var index = firstTask; index < firstTask + visibleTasks; index++)
        {
            var task = snapshot.Tasks[index];
            var selectedMarker = index == selectedTaskIndex ? "›" : " ";
            var overflowMarker = index == firstTask && firstTask > 0
                ? "↑"
                : index == firstTask + visibleTasks - 1 && firstTask + visibleTasks < snapshot.Tasks.Count
                    ? "↓"
                    : " ";
            if (visibleTasks == 1 && firstTask > 0 && firstTask + visibleTasks < snapshot.Tasks.Count)
            {
                overflowMarker = "↕";
            }

            var line = $"{selectedMarker}{overflowMarker} {StatusIcon(task.Status)} {task.Id} {task.Title}";
            canvas.DrawText(inner.X, row++, Text.Truncate(line, inner.Width),
                index == selectedTaskIndex ? Theme.Foreground : Theme.Muted,
                bold: index == selectedTaskIndex,
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

    private static void RenderQuality(
        Canvas canvas,
        DashboardSnapshot snapshot,
        Rect rect,
        int selectedTaskIndex)
    {
        canvas.DrawBox(rect, "CI + USAGE", Theme.Green);
        var inner = rect.Inset();
        var task = SelectedTask(snapshot, selectedTaskIndex);
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

    private void RenderLogs(Canvas canvas, DashboardSnapshot snapshot, Rect rect, int selectedTaskIndex)
    {
        canvas.DrawBox(rect, "TASK LOG", Theme.Cyan);
        var inner = rect.Inset();
        var task = SelectedTask(snapshot, selectedTaskIndex);
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

        string[] lines;
        try
        {
            using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            lines = ReadTail(reader, Math.Max(0, inner.Height - 2));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            canvas.DrawText(inner.X, inner.Y + 2, Text.Truncate("Log unavailable: " + exception.Message, inner.Width),
                Theme.Amber, maxWidth: inner.Width);
            return;
        }

        var row = inner.Y + 1;
        foreach (var line in lines)
        {
            canvas.DrawText(inner.X, row++, Text.Truncate(line, inner.Width), Theme.Foreground, maxWidth: inner.Width);
        }
    }

    private static string[] ReadTail(TextReader reader, int count)
    {
        if (count <= 0)
        {
            return [];
        }

        var tail = new Queue<string>(count);
        while (reader.ReadLine() is { } line)
        {
            if (tail.Count == count)
            {
                tail.Dequeue();
            }

            tail.Enqueue(line);
        }

        return tail.ToArray();
    }

    private static void RenderFooter(
        Canvas canvas,
        Rect rect,
        DashboardView view,
        string toast,
        IOverlay? overlay,
        DashboardSnapshot snapshot,
        int selectedTaskIndex)
    {
        // The legend is drawn key-by-key so the key itself reads brighter than its meaning. On a
        // narrow terminal the trailing entries are dropped rather than the whole line truncated
        // mid-word, which keeps the first and most useful keys visible at every width.
        var x = rect.X;
        foreach (var (key, meaning) in Keymap.Footer)
        {
            var keyWidth = Text.DisplayWidth(key);
            var meaningWidth = Text.DisplayWidth(meaning);
            if (x + keyWidth + meaningWidth + 3 > rect.Right)
            {
                break;
            }

            canvas.DrawText(x, rect.Y, key, Theme.Blue, bold: true, maxWidth: keyWidth);
            x += keyWidth + 1;
            canvas.DrawText(x, rect.Y, meaning, Theme.Muted, maxWidth: meaningWidth);
            x += meaningWidth + 2;
        }

        var mode = overlay?.Mode ?? view.ToString();
        var hint = overlay is null ? NextStepHint(snapshot, selectedTaskIndex) : null;
        var line = hint is null ? $"{mode} · {toast}" : $"{mode} · {toast} · {hint}";
        canvas.DrawText(rect.X, rect.Y + 1, Text.Truncate(line, rect.Width),
            hint is null ? Theme.Muted : Theme.Cyan, maxWidth: rect.Width);
    }

    /// <summary>
    /// The single most useful thing the operator could do next, given what is on screen. A command
    /// center that shows state without saying what to do with it leaves a first-time user stuck on
    /// a screen full of correct information.
    /// </summary>
    private static string? NextStepHint(DashboardSnapshot snapshot, int selectedTaskIndex)
    {
        if (snapshot.Conflicts.Any(conflict => conflict.Kind == ConflictKind.Collision))
        {
            return "Two agents are writing the same file — press C to cancel one of them";
        }

        if (snapshot.Config.Agents.Count == 0)
        {
            return "No agents are configured — quit and run: fknrtd agent add";
        }

        if (snapshot.Tasks.Count == 0)
        {
            return "Press N to describe the first piece of work. Every field is explained as you go";
        }

        var task = SelectedTask(snapshot, selectedTaskIndex);
        return task?.Status switch
        {
            WorkflowStatus.Queued => "This task has never run — press Enter to start it",
            WorkflowStatus.Running => "Press L to watch the live log for this task",
            WorkflowStatus.Failed => "Press L to read why it failed, then R to retry from the failed stage",
            WorkflowStatus.ReadyToLand => "Verified and audited — press G to merge it into " +
                                          (string.IsNullOrWhiteSpace(task.BaseRef) ? "the workspace" : task.BaseRef),
            WorkflowStatus.Cancelled => "Cancelled — press R to retry it",
            WorkflowStatus.Landed => "Landed — press X to remove its worktree and reclaim the disk space",
            _ => null
        };
    }

    private async Task HandleKeyAsync(ConsoleKeyInfo key, DashboardSnapshot snapshot, CancellationToken cancellationToken)
    {
        // An open overlay owns every keystroke. Nothing behind it can be triggered by accident while
        // the operator is part-way through answering a question.
        if (_overlay is { } overlay)
        {
            switch (overlay.HandleKey(key))
            {
                case OverlayResult.Cancel:
                    _overlay = null;
                    _overlayCompleted = null;
                    _toast = "Cancelled. Nothing was changed.";
                    break;
                case OverlayResult.Submit:
                    var completed = _overlayCompleted;
                    _overlay = null;
                    _overlayCompleted = null;
                    if (completed is not null)
                    {
                        await completed(overlay, cancellationToken).ConfigureAwait(false);
                    }

                    break;
            }

            return;
        }

        switch (key.Key)
        {
            case ConsoleKey.Q:
            case ConsoleKey.Escape:
                _quit = true;
                break;
            case ConsoleKey.UpArrow:
                _selectedTask = Math.Max(0, _selectedTask - 1);
                RememberSelection(snapshot);
                break;
            case ConsoleKey.DownArrow:
                _selectedTask = Math.Min(Math.Max(0, snapshot.Tasks.Count - 1), _selectedTask + 1);
                RememberSelection(snapshot);
                break;
            case ConsoleKey.Enter:
                StartSelected(snapshot);
                break;
            case ConsoleKey.C:
                if (SelectedTask(snapshot) is { } cancelTask)
                {
                    await _tasks.RequestCancellationAsync(cancelTask.Id, cancellationToken).ConfigureAwait(false);
                    _toast = $"Cancellation requested for {cancelTask.Id}. The current stage finishes first.";
                }
                else
                {
                    _toast = "No task is selected.";
                }

                break;
            case ConsoleKey.G:
                OpenLandConfirmation(snapshot);
                break;
            case ConsoleKey.X:
                OpenCleanupConfirmation(snapshot);
                break;
            case ConsoleKey.R:
                await RetrySelectedAsync(snapshot, cancellationToken).ConfigureAwait(false);
                break;
            case ConsoleKey.N:
                OpenNewTaskWizard(snapshot);
                break;
            case ConsoleKey.M:
                OpenMessageWizard(snapshot);
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
            case ConsoleKey.I:
                if (SelectedTask(snapshot) is { } inspected)
                {
                    _overlay = Reference.Task(inspected, snapshot.Config);
                }
                else
                {
                    _overlay = Reference.Welcome(snapshot.Config);
                }

                break;
            case ConsoleKey.A:
                _overlay = Reference.Agents(snapshot.Config);
                break;
            case ConsoleKey.D:
                _toast = "Running the pre-flight checks…";
                _overlay = Reference.Doctor(await _doctor.RunAsync(cancellationToken).ConfigureAwait(false));
                _toast = "Ready";
                break;
            default:
                // '?' has no ConsoleKey of its own and arrives differently on different keyboards,
                // so it is matched on the character rather than on the key.
                if (key.KeyChar is '?' or 'h' or 'H')
                {
                    _overlay = Reference.Help();
                }

                break;
        }
    }

    private void StartSelected(DashboardSnapshot snapshot)
    {
        var task = SelectedTask(snapshot);
        if (task is null)
        {
            _toast = "No task is selected. Press N to create one.";
            return;
        }

        StartTask(task, snapshot.Config.MaxParallelAgents);
    }

    private void StartTask(WorkflowTask task, int maxParallel)
    {
        if (_running.ContainsKey(task.Id) || task.Status == WorkflowStatus.Running)
        {
            _toast = $"{task.Id} is already running. Press L to watch its log.";
            return;
        }

        if (task.Status == WorkflowStatus.Landed)
        {
            _toast = $"{task.Id} has already landed. Create a new task for further work.";
            return;
        }

        if (_running.Count >= maxParallel)
        {
            _toast = $"Already running {maxParallel} tasks, which is this workspace's limit. " +
                     "Wait for one to finish, or raise maxParallelAgents in .fknrtd/config.json.";
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
        _toast = $"Started {task.Id}. Press L to watch it work.";
    }

    private void OpenLandConfirmation(DashboardSnapshot snapshot)
    {
        var task = SelectedTask(snapshot);
        if (task is null)
        {
            _toast = "No task is selected.";
            return;
        }

        if (task.Status != WorkflowStatus.ReadyToLand)
        {
            _toast = $"{task.Id} is {task.Status}. Only a verified and audited task can be landed.";
            return;
        }

        var git = snapshot.Config.Mode == WorkspaceMode.Git;
        _overlay = new Confirmation(
            "LAND THIS TASK",
            Theme.Green,
            $"{task.Id} — {task.Title}",
            git
                ? $"This merges the branch {task.BranchName} into {task.BaseRef}. It is the only action " +
                  "that changes your base branch, and the dashboard cannot undo it afterwards."
                : "This records that the verified work already present in this folder is final. There is " +
                  "no branch and no merge in a standalone workspace.",
            "LAND",
            "land",
            git
                ? $"The finished diff is in {task.WorktreePath} if you want to read it before you decide."
                : null);
        _overlayCompleted = async (_, token) =>
        {
            try
            {
                await _orchestrator.LandAsync(task.Id, token).ConfigureAwait(false);
                _toast = $"Landed {task.Id}. Press X to remove its worktree when you are done with it.";
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                _toast = "Could not land: " + exception.Message;
            }
        };
    }

    private void OpenCleanupConfirmation(DashboardSnapshot snapshot)
    {
        var task = SelectedTask(snapshot);
        if (task is null)
        {
            _toast = "No task is selected.";
            return;
        }

        if (task.Status == WorkflowStatus.Running)
        {
            _toast = $"{task.Id} is still running. Cancel it with C first.";
            return;
        }

        if (snapshot.Config.Mode == WorkspaceMode.Standalone)
        {
            _toast = "Nothing to remove. A standalone workspace has no worktree or branch.";
            return;
        }

        _overlay = new Confirmation(
            "REMOVE THE WORKTREE",
            Theme.Amber,
            $"{task.Id} — {task.Title}",
            $"This deletes the directory {task.WorktreePath} and nothing else. The task record, its " +
            "stage logs and its Git branch are all kept, so you can still read what happened.",
            "REMOVE",
            "cleanup");
        _overlayCompleted = async (_, token) =>
        {
            try
            {
                await _worktrees.RemoveAsync(task, force: false, snapshot.Config.Mode, token).ConfigureAwait(false);
                _toast = $"Removed the worktree for {task.Id}. Its branch and log were kept.";
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                _toast = "Could not remove the worktree: " + exception.Message;
            }
        };
    }

    private async Task RetrySelectedAsync(DashboardSnapshot snapshot, CancellationToken cancellationToken)
    {
        var task = SelectedTask(snapshot);
        if (task is null)
        {
            _toast = "No task is selected.";
            return;
        }

        try
        {
            await _tasks.ResetFailedStagesAsync(task.Id, cancellationToken).ConfigureAwait(false);
            _toast = $"Reset the failed stages of {task.Id}. Press Enter to run it again.";
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _toast = "Could not retry: " + exception.Message;
        }
    }

    /// <summary>
    /// Opens the guided task builder behind N. The form itself is defined in
    /// <see cref="TaskWizard"/>; what belongs here is only what to do with the answers.
    /// </summary>
    private void OpenNewTaskWizard(DashboardSnapshot snapshot)
    {
        var config = snapshot.Config;
        if (!config.Agents.Any(agent => agent.Enabled))
        {
            _toast = "No agents are enabled. Quit and run: fknrtd agent list";
            return;
        }

        var git = config.Mode == WorkspaceMode.Git;
        _overlay = TaskWizard.Create(config);
        _overlayCompleted = async (completed, token) =>
        {
            var wizard = (Wizard)completed;
            try
            {
                var task = await _tasks.CreateAsync(
                        wizard.Value("title"),
                        wizard.Value("brief"),
                        wizard.Value("lead"),
                        wizard.Value("implementer"),
                        wizard.Value("auditor"),
                        wizard.Lines("verify"),
                        git ? wizard.Value("base") : null,
                        int.TryParse(wizard.Value("repairs"), out var rounds) ? rounds : null,
                        token)
                    .ConfigureAwait(false);
                _selectedTask = 0;
                _selectedTaskId = task.Id;
                if (wizard.Value("then") == "run")
                {
                    StartTask(task, config.MaxParallelAgents);
                    return;
                }

                _toast = $"Created {task.Id}. Press Enter to run it, or I to read it back.";
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                _toast = "Could not create the task: " + exception.Message;
            }
        };
    }

    private void OpenMessageWizard(DashboardSnapshot snapshot)
    {
        if (snapshot.Config.Agents.Count == 0)
        {
            _toast = "No agents are configured, so there is nobody to send a message between.";
            return;
        }

        _overlay = TaskWizard.Message(snapshot.Config);
        _overlayCompleted = async (completed, token) =>
        {
            var wizard = (Wizard)completed;
            try
            {
                await _messages
                    .SendAsync(wizard.Value("from"), wizard.Value("to"), wizard.Value("text"),
                        cancellationToken: token)
                    .ConfigureAwait(false);
                _toast = $"Recorded a message from {wizard.Value("from")} to {wizard.Value("to")}.";
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                _toast = "Could not send the message: " + exception.Message;
            }
        };
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

    private WorkflowTask? SelectedTask(DashboardSnapshot snapshot) => SelectedTask(snapshot, _selectedTask);

    private static WorkflowTask? SelectedTask(DashboardSnapshot snapshot, int selectedTaskIndex) =>
        snapshot.Tasks.Count == 0
            ? null
            : snapshot.Tasks[Math.Clamp(selectedTaskIndex, 0, snapshot.Tasks.Count - 1)];

    private void ClampSelection(DashboardSnapshot snapshot)
    {
        if (snapshot.Tasks.Count == 0)
        {
            _selectedTask = 0;
            _selectedTaskId = null;
            return;
        }

        if (_selectedTaskId is not null)
        {
            var remembered = snapshot.Tasks
                .Select((task, index) => (task, index))
                .FirstOrDefault(item => item.task.Id.Equals(_selectedTaskId, StringComparison.OrdinalIgnoreCase));
            if (remembered.task is not null)
            {
                _selectedTask = remembered.index;
            }
        }

        _selectedTask = Math.Clamp(_selectedTask, 0, snapshot.Tasks.Count - 1);
        _selectedTaskId = snapshot.Tasks[_selectedTask].Id;
    }

    private void RememberSelection(DashboardSnapshot snapshot) =>
        _selectedTaskId = SelectedTask(snapshot)?.Id;

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
