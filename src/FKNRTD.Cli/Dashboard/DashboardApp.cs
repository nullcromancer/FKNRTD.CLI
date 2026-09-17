using System.Text;
using FKNRTD.Commands;
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
    private readonly GitService _git;
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
    /// How many lines back from the end of the log the view is scrolled. Zero follows the live tail.
    /// A failure is often explained a hundred lines above the last one, so the screen an operator
    /// opens when something has gone wrong has to be able to look upwards.
    /// </summary>
    private int _logScroll;

    /// <summary>
    /// The most recent snapshot, kept so an open overlay can read live state rather than the state
    /// that existed when it was opened.
    /// </summary>
    private DashboardSnapshot? _live;

    /// <summary>
    /// The modal layer. Every question the dashboard asks is an overlay drawn over the frame, so the
    /// operator never drops out of the alternate screen to answer an unexplained prompt on a blank
    /// terminal — and can still see the task they are acting on while they answer.
    /// </summary>
    private IOverlay? _overlay;

    /// <summary>
    /// What to do with a completed overlay. It is handed the snapshot current at the moment of
    /// completion rather than the one captured when the overlay opened, because a modal can be left
    /// open across several refreshes and the task it acts on may have moved on meanwhile.
    /// </summary>
    private Func<IOverlay, DashboardSnapshot, CancellationToken, Task>? _overlayCompleted;

    public DashboardApp(
        DashboardSnapshotService snapshots,
        Orchestrator orchestrator,
        TaskService tasks,
        MessageService messages,
        UsageService usage,
        StateStore store,
        WorktreeService worktrees,
        DoctorService doctor,
        GitService git)
    {
        _snapshots = snapshots;
        _orchestrator = orchestrator;
        _tasks = tasks;
        _messages = messages;
        _usage = usage;
        _store = store;
        _worktrees = worktrees;
        _doctor = doctor;
        _git = git;
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
            Console.WriteLine(Render(snapshot, widthOverride ?? Screen.Width(140), heightOverride ?? Screen.Height(40), useColor));
            return;
        }

        using var sessionCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _sessionCancellation = sessionCancellation;
        Screen.Enter();
        var previousWidth = -1;
        var previousHeight = -1;
        try
        {
            while (!_quit && !cancellationToken.IsCancellationRequested)
            {
                RemoveCompletedRuns();
                var snapshot = await _snapshots.CaptureAsync(cancellationToken).ConfigureAwait(false);
                ClampSelection(snapshot);
                _live = snapshot;

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

                var width = widthOverride ?? Screen.Width(120);
                var height = heightOverride ?? Screen.Height(32);
                Console.Write(width == previousWidth && height == previousHeight ? "\u001b[H" : "\u001b[2J\u001b[H");
                Console.Write(RenderCurrent(snapshot, width, height, useColor));
                previousWidth = width;
                previousHeight = height;

                // Each refresh re-reads the workspace's state files and shells out to Git. With a
                // modal open the operator is reading rather than watching, and most of the frame is
                // covered anyway, so polling the repository once a second buys nothing. Keystrokes
                // are still noticed immediately; only the unattended redraw slows down.
                var refresh = TimeSpan.FromMilliseconds(
                    snapshot.Config.DashboardRefreshMilliseconds * (_overlay is null ? 1 : 4));
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
            Screen.Exit();
        }
    }

    internal string Render(DashboardSnapshot snapshot, int width, int height, bool useColor) =>
        RenderFrame(snapshot, width, height, useColor, 0, DashboardView.Overview, "Ready", null);

    internal string Render(DashboardSnapshot snapshot, int width, int height, bool useColor, int selectedTaskIndex) =>
        RenderFrame(snapshot, width, height, useColor, selectedTaskIndex, DashboardView.Overview, "Ready", null);

    internal string RenderLog(
        DashboardSnapshot snapshot,
        int width,
        int height,
        int selectedTaskIndex,
        bool useColor = false) =>
        RenderFrame(snapshot, width, height, useColor, selectedTaskIndex, DashboardView.Logs, "Ready", null);

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
        canvas.DrawBox(rect, "FKNRTD COMMAND CENTER", Theme.Cyan);
        var inner = rect.Inset();
        var risk = snapshot.Conflicts.FirstOrDefault();
        var riskText = risk is null
            ? "√ SAFE"
            : risk.Kind == ConflictKind.Collision
                ? "× COLLISION"
                : "∆ " + risk.Kind.ToString().ToUpperInvariant();
        var riskColor = risk is null ? Theme.Green : risk.Kind == ConflictKind.Collision ? Theme.Red : Theme.Amber;

        // A standalone workspace has no branch, cleanliness or divergence to report.
        var located = snapshot.Git.IsRepository
            ? $"on {snapshot.Git.Branch}"
            : "○ standalone";
        canvas.DrawText(inner.X, inner.Y,
            $"{snapshot.Git.RepositoryName}  {located}", Theme.Blue, bold: true,
            maxWidth: Math.Max(10, inner.Width - 32));
        var gitText = snapshot.Git.IsClean ? "√ clean" : $"∆ {snapshot.Git.ChangedFiles} changed";
        var right = snapshot.Git.IsRepository
            ? $"{gitText}  ↑{snapshot.Git.Ahead}↓{snapshot.Git.Behind}  {riskText}"
            : riskText;
        var rightWidth = Math.Min(inner.Width, Text.DisplayWidth(right));
        canvas.DrawText(Math.Max(inner.X, rect.Right - rightWidth - 2), inner.Y, right, riskColor,
            bold: risk is not null, maxWidth: rightWidth);

        var claude = snapshot.Usage.FirstOrDefault(item => item.AgentId.Equals("claude", StringComparison.OrdinalIgnoreCase));
        var codex = snapshot.Usage.FirstOrDefault(item => item.AgentId.Equals("codex", StringComparison.OrdinalIgnoreCase));
        // A row of N/A is what a first-time operator sees, and it reads as broken rather than as
        // "nothing has reported yet". Saying which key fetches it costs the same line.
        var known = new[]
        {
            claude?.ContextRemainingPercent, claude?.FiveHourRemainingPercent,
            claude?.WeeklyRemainingPercent, codex?.FiveHourRemainingPercent, codex?.WeeklyRemainingPercent
        }.Any(value => value is not null);
        var line = known
            ? $"CTX {Percent(claude?.ContextRemainingPercent)} left  |  Claude 5h {Percent(claude?.FiveHourRemainingPercent)} 7d {Percent(claude?.WeeklyRemainingPercent)}  |  Codex 5h {Percent(codex?.FiveHourRemainingPercent)} 7d {Percent(codex?.WeeklyRemainingPercent)}"
            : "Rate-limit budget unknown — press U to ask Codex, or install the Claude statusline with " +
              "'fknrtd integration install-claude-statusline'";
        canvas.DrawText(inner.X, inner.Y + 1, Text.Truncate(line, inner.Width),
            known ? Theme.Foreground : Theme.Muted, maxWidth: inner.Width);
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
        // The panel shows a handful of rows with a small arrow in the margin when there are more.
        // Naming the total is what tells a reader the arrow means three tasks rather than one.
        canvas.DrawBox(
            rect,
            snapshot.Tasks.Count > 1 ? $"PIPELINE · {snapshot.Tasks.Count} tasks · F to find" : "PIPELINE",
            Theme.Blue);
        var inner = rect.Inset();
        if (snapshot.Tasks.Count == 0)
        {
            canvas.DrawWrapped(inner.X, inner.Y, inner.Width, Math.Max(1, inner.Height),
                "Nothing to do yet. Press N to describe a piece of work; every field explains itself.",
                Theme.Muted);
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

        // Eight initials and eight markers mean nothing on their own. The label is what turns the
        // strip from a cipher into a pipeline, and it is dropped only when there is genuinely no
        // room for it. 'fknrtd explain stage-strip' spells the letters out.
        var label = "Stages ";
        var labelled = Text.DisplayWidth(stages) + label.Length <= inner.Width;
        if (labelled)
        {
            canvas.DrawText(inner.X, row, label, Theme.Muted, maxWidth: label.Length);
        }

        canvas.DrawText(inner.X + (labelled ? label.Length : 0), row++, Text.Truncate(stages, inner.Width),
            Theme.Foreground, maxWidth: inner.Width - (labelled ? label.Length : 0));
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
        canvas.DrawBox(rect, "CHECKS + BUDGET", Theme.Green);
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
            canvas.DrawWrapped(inner.X, inner.Y, inner.Width, Math.Max(1, inner.Height),
                "No messages. Agents record hand-offs here — press M to add one.", Theme.Muted);
            return;
        }

        var row = inner.Y;
        foreach (var message in snapshot.Messages.Take(inner.Height))
        {
            var delivery = message.Delivery == MessageDelivery.Acknowledged ? "√" : "→";
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
            canvas.DrawText(inner.X, inner.Y, "√ SAFE  No path overlap detected", Theme.Green, bold: true,
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
                Text.Truncate($"{(conflict.Kind == ConflictKind.Collision ? "×" : "∆")} {conflict.RiskScore} {conflict.Summary}", inner.Width),
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
            canvas.DrawWrapped(inner.X, row, inner.Width, Math.Max(1, inner.Bottom - row),
                "Nothing has happened yet. Every stage, conflict and agent check-in is recorded here. " +
                "Press E for the full history.", Theme.Muted);
        }
    }

    /// <summary>
    /// The log view. This is the screen an operator stares at when something has gone wrong, so it
    /// leads with what is being run and why, rather than with a file path and a wall of output. When
    /// there is no log it says which of the several possible reasons applies, and what to do about it.
    /// </summary>
    private void RenderLogs(Canvas canvas, DashboardSnapshot snapshot, Rect rect, int selectedTaskIndex)
    {
        var task = SelectedTask(snapshot, selectedTaskIndex);
        if (task is null)
        {
            canvas.DrawBox(rect, "TASK LOG", Theme.Cyan);
            var empty = rect.Inset();
            canvas.DrawWrapped(empty.X, empty.Y, empty.Width, 3,
                "No task is selected, so there is no log to show. Press N to create a task, or Tab " +
                "to go back to the overview.", Theme.Muted);
            return;
        }

        var stageEntry = Glossary.Find("stage." + task.CurrentStage.ToString().ToLowerInvariant());
        var stageState = task.Stage(task.CurrentStage).State;
        canvas.DrawBox(rect, $"TASK LOG  ·  {task.CurrentStage}  {StageIcon(stageState)}", StageColour(stageState));
        var inner = rect.Inset();
        var row = inner.Y;

        canvas.DrawText(inner.X, row++, Text.Truncate(task.Title, inner.Width), Theme.Foreground, bold: true,
            maxWidth: inner.Width);
        if (stageEntry is not null && row < inner.Bottom)
        {
            canvas.DrawText(inner.X, row++, Text.Truncate(stageEntry.Summary, inner.Width), Theme.Muted,
                maxWidth: inner.Width);
        }

        var path = ResolveLogPath(task);
        if (path is null || !File.Exists(path))
        {
            if (row < inner.Bottom)
            {
                row++;
            }

            canvas.DrawWrapped(inner.X, row, inner.Width, Math.Max(1, inner.Bottom - row),
                WhyThereIsNoLog(task), Theme.Amber);
            return;
        }

        if (row < inner.Bottom)
        {
            canvas.DrawText(inner.X, row++, Text.Truncate(path, inner.Width), Theme.Muted, maxWidth: inner.Width);
        }

        string[] lines;
        var total = 0;
        try
        {
            using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            (lines, total) = ReadWindow(reader, _logScroll, Math.Max(0, inner.Bottom - row - 1));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            canvas.DrawWrapped(inner.X, row, inner.Width, 3,
                "The log could not be read: " + exception.Message +
                " It is still on disk; another process may be holding it open.", Theme.Amber);
            return;
        }

        if (lines.Length == 0)
        {
            canvas.DrawWrapped(inner.X, row, inner.Width, 2,
                "The log exists but is still empty. The agent has been launched and has not written " +
                "anything yet.", Theme.Muted);
            return;
        }

        var shown = Math.Min(lines.Length, Math.Max(0, inner.Bottom - row - 1));
        var firstShown = Math.Max(1, total - _logScroll - shown + 1);
        canvas.DrawText(inner.X, row++,
            Text.Truncate(
                _logScroll == 0
                    ? $"lines {firstShown}-{total} of {total}  ·  following"
                    : $"lines {firstShown}-{firstShown + shown - 1} of {total}  ·  End to follow again",
                inner.Width),
            _logScroll == 0 ? Theme.Muted : Theme.Amber,
            maxWidth: inner.Width);

        foreach (var line in lines)
        {
            if (row >= inner.Bottom)
            {
                break;
            }

            canvas.DrawText(inner.X, row++, Text.Truncate(line, inner.Width), LogLineColour(line),
                maxWidth: inner.Width);
        }
    }

    /// <summary>
    /// The log for the stage the task is on, falling back to the most recent log it has. A task that
    /// has moved on should still show the output of what it just did rather than nothing at all.
    /// </summary>
    private string? ResolveLogPath(WorkflowTask task)
    {
        var path = _store.TaskLogPath(task.Id, task.CurrentStage, task.RepairRound);
        if (File.Exists(path))
        {
            return path;
        }

        var directory = Path.GetDirectoryName(path);
        if (directory is null || !Directory.Exists(directory))
        {
            return null;
        }

        return Directory.EnumerateFiles(directory, "*.log")
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
    }

    /// <summary>
    /// Why there is nothing to read. "No log exists" is true and useless; which of the reasons
    /// applies determines whether the operator should press a key, wait, or go and look at Git.
    /// </summary>
    private static string WhyThereIsNoLog(WorkflowTask task) => task.Status switch
    {
        WorkflowStatus.Queued =>
            "This task has not run yet, so nothing has been written. Press Enter to start it and the " +
            "output will appear here as it arrives.",
        WorkflowStatus.Running =>
            "The stage has started but has not produced output yet. Agents often think for a while " +
            "before writing anything.",
        WorkflowStatus.Cancelled =>
            "This task was cancelled before the current stage wrote anything. Press R to reset it and " +
            "Enter to run it again.",
        WorkflowStatus.Landed =>
            "This task has landed and its logs may already have been cleaned up. The work itself is " +
            "in your base branch.",
        _ =>
            "No log has been written for this stage. Press I to see the full record of the task, " +
            "which records what each stage did."
    };

    /// <summary>
    /// Colours the lines that matter. Agent output is long and uniform; a failure buried in the
    /// middle of it is missed on a monochrome wall of text.
    /// </summary>
    private static Rgb LogLineColour(string line)
    {
        if (line.Contains("FKNRTD_VERDICT: PASS", StringComparison.Ordinal))
        {
            return Theme.Green;
        }

        if (line.Contains("FKNRTD_VERDICT: FAIL", StringComparison.Ordinal) ||
            line.Contains("error", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("exception", StringComparison.OrdinalIgnoreCase) ||
            line.Contains(" failed", StringComparison.OrdinalIgnoreCase))
        {
            return Theme.Red;
        }

        return line.Contains("warn", StringComparison.OrdinalIgnoreCase) ? Theme.Amber : Theme.Foreground;
    }

    private static Rgb StageColour(StageState state) => state switch
    {
        StageState.Running => Theme.Blue,
        StageState.Passed => Theme.Green,
        StageState.Failed => Theme.Red,
        StageState.Skipped => Theme.Muted,
        _ => Theme.Cyan
    };

    /// <summary>
    /// How far back the current stage log can usefully be scrolled. The frame clamps its own copy
    /// of the scroll position, which keeps the picture right while leaving the stored position
    /// anywhere at all - so Home, which used to jump to a sentinel a billion lines past the end,
    /// left PgDn needing about a hundred million presses to come back. Rendering has to stay a pure
    /// function, so the bound is applied here instead, where the keystroke is.
    /// </summary>
    private int LogCeiling(DashboardSnapshot snapshot)
    {
        if (_view != DashboardView.Logs || SelectedTask(snapshot) is not { } task ||
            ResolveLogPath(task) is not { } path)
        {
            return 0;
        }

        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            var lines = 0;
            while (reader.ReadLine() is not null)
            {
                lines++;
            }

            return Math.Max(0, lines - 1);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A log being written to, or gone. Leaving the position alone is better than throwing
            // away where the operator had scrolled to.
            return _logScroll;
        }
    }

    /// <summary>
    /// Reads <paramref name="count"/> lines ending <paramref name="skipFromEnd"/> lines before the
    /// end of the stream, and reports the total line count so the view can say where it is. The
    /// whole file is walked rather than seeked because a log is being appended to while it is read,
    /// and a byte offset into a growing UTF-8 stream is not a line boundary.
    /// </summary>
    internal static (string[] Lines, int Total) ReadWindow(TextReader reader, int skipFromEnd, int count)
    {
        var all = new List<string>();
        while (reader.ReadLine() is { } line)
        {
            all.Add(line);
        }

        if (count <= 0 || all.Count == 0)
        {
            return ([], all.Count);
        }

        var skip = Math.Clamp(skipFromEnd, 0, Math.Max(0, all.Count - count));
        var end = all.Count - skip;
        var start = Math.Max(0, end - count);
        return (all.GetRange(start, end - start).ToArray(), all.Count);
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
        // The whole strip when it fits. When it does not, help and quit are pinned to the right and
        // the middle is dropped instead — those two are what an operator needs most at the moment
        // they cannot find anything, and truncating from the end took exactly them.
        static int Span((string Key, string Meaning)[] entries) => entries
            .Sum(entry => Text.DisplayWidth(entry.Key) + Text.DisplayWidth(entry.Meaning) + 3);

        var essentialX = Span(Keymap.Footer) <= rect.Width
            ? rect.Right
            : Math.Max(rect.X, rect.Right - Span(Keymap.EssentialFooter));

        var x = rect.X;
        foreach (var (key, meaning) in Keymap.OptionalFooter)
        {
            var keyWidth = Text.DisplayWidth(key);
            var meaningWidth = Text.DisplayWidth(meaning);
            if (x + keyWidth + meaningWidth + 3 > essentialX)
            {
                break;
            }

            canvas.DrawText(x, rect.Y, key, Theme.Blue, bold: true, maxWidth: keyWidth);
            x += keyWidth + 1;
            canvas.DrawText(x, rect.Y, meaning, Theme.Muted, maxWidth: meaningWidth);
            x += meaningWidth + 2;
        }

        var pinned = essentialX == rect.Right
            ? x
            : Math.Max(x, rect.Right - Span(Keymap.EssentialFooter));
        foreach (var (key, meaning) in Keymap.EssentialFooter)
        {
            var keyWidth = Text.DisplayWidth(key);
            var meaningWidth = Text.DisplayWidth(meaning);
            if (pinned + keyWidth + meaningWidth + 3 > rect.Right)
            {
                break;
            }

            canvas.DrawText(pinned, rect.Y, key, Theme.Blue, bold: true, maxWidth: keyWidth);
            pinned += keyWidth + 1;
            canvas.DrawText(pinned, rect.Y, meaning, Theme.Muted, maxWidth: meaningWidth);
            pinned += meaningWidth + 2;
        }

        var mode = overlay?.Mode ?? view.ToString();
        var hint = overlay is null ? NextStepHint(snapshot, selectedTaskIndex, view) : null;
        var line = hint is null ? $"{mode} · {toast}" : $"{mode} · {toast} · {hint}";
        canvas.DrawText(rect.X, rect.Y + 1, Text.Truncate(line, rect.Width),
            hint is null ? Theme.Muted : Theme.Cyan, maxWidth: rect.Width);
    }

    /// <summary>
    /// The single most useful thing the operator could do next, given what is on screen. A command
    /// center that shows state without saying what to do with it leaves a first-time user stuck on
    /// a screen full of correct information.
    /// </summary>
    private static string? NextStepHint(DashboardSnapshot snapshot, int selectedTaskIndex, DashboardView view)
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

        // Pointing at the log view from inside the log view is the kind of detail that makes an
        // operator stop trusting the hints altogether.
        if (view == DashboardView.Logs)
        {
            return task?.Status switch
            {
                WorkflowStatus.Queued => "Press Enter to run this task and the output will appear here",
                WorkflowStatus.Running => "Following the live output. Press C to stop it, Tab for the overview",
                WorkflowStatus.Failed => "Press R to reset the failed stages, then Enter to run it again",
                _ => "Press Tab to go back to the overview"
            };
        }

        return task?.Status switch
        {
            WorkflowStatus.Queued => "This task has never run — press Enter to start it",
            WorkflowStatus.Running => "Press L to watch the live log for this task",
            WorkflowStatus.Failed => "Press L to read why it failed, then R to retry from the failed stage",
            WorkflowStatus.ReadyToLand => "Verified and audited — press V to read the change, then G to " +
                                          "merge it into " +
                                          (string.IsNullOrWhiteSpace(task.BaseRef) ? "the workspace" : task.BaseRef),
            WorkflowStatus.Cancelled => "Cancelled — press R to retry it",
            WorkflowStatus.Landed => "Landed — press X to remove its worktree and reclaim the disk space",
            _ => null
        };
    }

    /// <summary>The most recent footer message. The test seam for the action guard.</summary>
    internal string Toast => _toast;

    /// <summary>How far back through the stage log the operator has scrolled. A test seam.</summary>
    internal int LogScroll => _logScroll;

    internal async Task HandleKeyAsync(ConsoleKeyInfo key, DashboardSnapshot snapshot, CancellationToken cancellationToken)
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
                        try
                        {
                            await completed(overlay, snapshot, cancellationToken).ConfigureAwait(false);
                        }
                        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                        {
                            throw;
                        }
                        catch (Exception exception) when (exception is not StackOverflowException &&
                                                          exception is not OutOfMemoryException)
                        {
                            _toast = "That did not work: " + exception.Message;
                        }
                    }

                    break;
            }

            return;
        }

        // Keys are translated to the same action identifiers the command palette uses, and both go
        // through one router. A key and its palette entry therefore cannot drift apart, and every
        // action has exactly one implementation.
        var action = key.Key switch
        {
            ConsoleKey.Q or ConsoleKey.Escape => "Q",
            ConsoleKey.UpArrow => "up",
            ConsoleKey.DownArrow => "down",
            ConsoleKey.Enter => "Enter",
            ConsoleKey.C => "C",
            ConsoleKey.G => "G",
            ConsoleKey.X => "X",
            ConsoleKey.R => "R",
            ConsoleKey.N => "N",
            ConsoleKey.M => "M",
            ConsoleKey.L => "L",
            ConsoleKey.U => "U",
            ConsoleKey.I => "I",
            ConsoleKey.A => "A",
            ConsoleKey.D => "D",
            ConsoleKey.E => "E",
            ConsoleKey.K => "K",
            ConsoleKey.S => "S",
            ConsoleKey.F => "F",
            ConsoleKey.V => "V",
            ConsoleKey.P => "P",
            ConsoleKey.Tab => "Tab",
            ConsoleKey.PageUp => "log-up",
            ConsoleKey.PageDown => "log-down",
            ConsoleKey.Home => "log-top",
            ConsoleKey.End => "log-follow",
            // '?' and '/' have no ConsoleKey of their own and arrive differently on different
            // keyboard layouts, so they are matched on the character instead. ':' is accepted for the
            // palette because it is the same key as '/' on several layouts; nothing else is aliased,
            // because a key that does something without appearing in the reference is a key nobody
            // can discover and nobody can look up.
            _ => key.KeyChar switch
            {
                '?' => "?",
                '/' or ':' => "/",
                _ => null
            }
        };

        if (action is null)
        {
            return;
        }

        // One guard for every action. Several of them read files, query Git or launch a process, and
        // any of those can fail for reasons that have nothing to do with the operator — a log held
        // open, a repository mid-rebase, an agent that will not start. A dashboard that exits to a
        // stack trace because a file was momentarily locked is a dashboard nobody leaves running.
        try
        {
            await RunActionAsync(action, snapshot, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is not StackOverflowException &&
                                          exception is not OutOfMemoryException)
        {
            _toast = "That did not work: " + exception.Message;
        }
    }

    /// <summary>
    /// Performs one dashboard action, named by the key it is bound to. Both the keyboard and the
    /// command palette call this, which is what lets the palette explain an action it is about to
    /// run in the same words the help reference uses.
    /// </summary>
    private async Task RunActionAsync(string action, DashboardSnapshot snapshot, CancellationToken cancellationToken)
    {
        switch (action)
        {
            case "Q":
                _quit = true;
                break;
            case "up":
                _selectedTask = Math.Max(0, _selectedTask - 1);
                _logScroll = 0;
                RememberSelection(snapshot);
                break;
            case "down":
                _selectedTask = Math.Min(Math.Max(0, snapshot.Tasks.Count - 1), _selectedTask + 1);
                _logScroll = 0;
                RememberSelection(snapshot);
                break;
            case "Enter":
                StartSelected(snapshot);
                break;
            case "C":
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
            case "G":
                OpenLandConfirmation(snapshot);
                break;
            case "X":
                OpenCleanupConfirmation(snapshot);
                break;
            case "R":
                await RetrySelectedAsync(snapshot, cancellationToken).ConfigureAwait(false);
                break;
            case "N":
                OpenNewTaskWizard(snapshot);
                break;
            case "M":
                OpenMessageWizard(snapshot);
                break;
            case "L":
            case "Tab":
                _view = _view == DashboardView.Logs ? DashboardView.Overview : DashboardView.Logs;
                _logScroll = 0;
                break;
            case "log-up":
                _logScroll = Math.Min(_logScroll + 10, LogCeiling(snapshot));
                break;
            case "log-down":
                // The ceiling is applied before stepping down, not only after: a scroll position
                // past the end of the file takes just as many presses to walk back as it took to
                // reach, and Home used to put it a billion lines past the end.
                _logScroll = Math.Max(0, Math.Min(_logScroll, LogCeiling(snapshot)) - 10);
                break;
            case "log-top":
                _logScroll = LogCeiling(snapshot);
                break;
            case "log-follow":
                _logScroll = 0;
                break;
            case "U":
                await ShowUsageAsync(snapshot, cancellationToken).ConfigureAwait(false);
                break;
            case "I":
                _overlay = SelectedTask(snapshot) is { } inspected
                    ? Reference.Task(inspected, snapshot.Config)
                    : Reference.Welcome(snapshot.Config);
                break;
            case "A":
                OpenAgentManager(snapshot);
                break;
            case "D":
                _toast = "Running the pre-flight checks...";
                _overlay = Reference.Doctor(await _doctor.RunAsync(cancellationToken).ConfigureAwait(false));
                _toast = "Ready";
                break;
            case "E":
                _overlay = Reference.Events(
                    await _store.LoadEventsAsync(500, cancellationToken).ConfigureAwait(false),
                    snapshot.CapturedAt);
                break;
            case "K":
                OpenCoordination(snapshot);
                break;
            case "S":
                OpenSettings(snapshot);
                break;
            case "F":
                OpenTaskPicker(snapshot);
                break;
            case "V":
                await ShowDiffAsync(snapshot, cancellationToken).ConfigureAwait(false);
                break;
            case "P":
                await ShowPromptsAsync(snapshot, cancellationToken).ConfigureAwait(false);
                break;
            case "?":
                OpenHelp();
                break;
            case "/":
                OpenPalette(snapshot);
                break;
        }
    }

    /// <summary>
    /// Shows what each agent on the highlighted task will be told. The lead's plan is read from disk
    /// when it exists so the implementer's prompt is the real one rather than a template.
    /// </summary>
    private async Task ShowPromptsAsync(DashboardSnapshot snapshot, CancellationToken cancellationToken)
    {
        var task = SelectedTask(snapshot);
        if (task is null)
        {
            _toast = "No task is selected.";
            return;
        }

        string? plan = null;
        var planPath = _store.TaskArtifactPath(task.Id, "plan.md");
        if (File.Exists(planPath))
        {
            try
            {
                plan = await File.ReadAllTextAsync(planPath, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // The template placeholder is shown instead; an unreadable plan is not worth
                // refusing to show the other two prompts over.
            }
        }

        _overlay = Reference.Prompts(task, snapshot.Config, plan);
    }

    /// <summary>
    /// Reads the highlighted task's finished change and shows it. Every other surface tells the
    /// operator to read the diff before landing; this is the one that actually produces it.
    /// </summary>
    private async Task ShowDiffAsync(DashboardSnapshot snapshot, CancellationToken cancellationToken)
    {
        var task = SelectedTask(snapshot);
        if (task is null)
        {
            _toast = "No task is selected.";
            return;
        }

        if (snapshot.Config.Mode == WorkspaceMode.Standalone)
        {
            _overlay = Reference.Diff(task, snapshot.Config, [], truncated: false);
            return;
        }

        if (string.IsNullOrWhiteSpace(task.WorktreePath) || !Directory.Exists(task.WorktreePath))
        {
            // A task that never reached its worktree stage and one whose worktree was cleaned up
            // after landing look identical here, and the operator needs quite different advice.
            _toast = task.Status == WorkflowStatus.Landed
                ? $"{task.Id} landed and its worktree has been removed. Its change is in " +
                  $"{(string.IsNullOrWhiteSpace(task.BaseRef) ? "this workspace" : task.BaseRef)}; " +
                  "read it there with Git."
                : $"{task.Id} has no worktree yet — it has not reached its worktree stage.";
            return;
        }

        _toast = "Reading the change…";
        try
        {
            var (lines, truncated) = await _git
                .GetDiffAsync(task.WorktreePath, task.BaseRef, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            _overlay = Reference.Diff(task, snapshot.Config, lines, truncated);
            _toast = "Ready";
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _toast = "Could not read the change: " + exception.Message;
        }
    }

    /// <summary>
    /// Opens the searchable task list. Selecting one moves the highlight to it, so every other key
    /// then acts on the task that was found rather than on whatever happened to be selected.
    /// </summary>
    private void OpenTaskPicker(DashboardSnapshot snapshot)
    {
        _overlay = Picker.Tasks(snapshot);
        _overlayCompleted = (completed, current, _) =>
        {
            if (((Picker)completed).Chosen is { } id)
            {
                _selectedTaskId = id;
                var index = current.Tasks
                    .Select((task, position) => (task, position))
                    .FirstOrDefault(item => item.task.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
                if (index.task is not null)
                {
                    _selectedTask = index.position;
                    // The scroll offset belonged to the task that was showing before.
                    _logScroll = 0;
                    _toast = $"Selected {index.task.Title}";
                }
            }

            return Task.CompletedTask;
        };
    }

    /// <summary>
    /// Opens the command palette. Keyboard shortcuts only help someone who already knows them, so
    /// every action is also reachable by typing part of its name.
    /// </summary>
    private void OpenPalette(DashboardSnapshot snapshot)
    {
        _overlay = new Palette(() =>
        {
            var current = _live ?? snapshot;
            return Palette.Build(current, SelectedTask(current), _running.Count);
        });
        _overlayCompleted = async (completed, current, token) =>
        {
            if (((Palette)completed).Chosen is { } chosen)
            {
                await RunActionAsync(chosen.Id, current, token).ConfigureAwait(false);
            }
        };
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
                ? "Press Esc and then V to read the finished change before you decide; it is also in " +
                  $"{task.WorktreePath} if you would rather use your own tools."
                : null);
        _overlayCompleted = async (_, _, token) =>
        {
            try
            {
                var landed = await _orchestrator.LandAsync(task.Id, token).ConfigureAwait(false);

                // A refused merge is recorded on the task and returned, not thrown. Announcing a
                // landing without reading that back said the work was on the base branch when Git
                // had declined to put it there.
                _toast = landed.Status == WorkflowStatus.Landed
                    ? $"Landed {task.Id}. Press X to remove its worktree when you are done with it."
                    : $"{task.Id} was NOT landed. Nothing was merged into {landed.BaseRef}. " +
                      "Press I to read the land stage's output.";
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
        _overlayCompleted = async (_, _, token) =>
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
        _overlayCompleted = async (completed, _, token) =>
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

    /// <summary>
    /// Opens the key reference and the glossary behind ?, and writes the whole thing out as a page
    /// on F2. Everything the dashboard can explain was explained only while the dashboard was open;
    /// this is the same reference in a form you can keep, or hand to whoever asks you next.
    /// </summary>
    private void OpenHelp()
    {
        _overlay = Reference.Help();
        _overlayCompleted = (completed, _, _) =>
        {
            if (completed is not InfoPanel { ActionRequested: true })
            {
                return Task.CompletedTask;
            }

            // Beside the workspace rather than in the current directory, so it lands somewhere the
            // operator can find again from the path the header is already showing them.
            var destination = Path.Combine(
                Path.GetDirectoryName(_store.Paths.Config) ?? _store.Paths.Root,
                PortalCommand.DefaultFileName);
            try
            {
                File.WriteAllText(destination, PortalCommand.Render(DateTimeOffset.UtcNow),
                    new UTF8Encoding(false));
                _toast = $"Wrote the whole reference to {destination}. Open it in a browser; it " +
                         "needs no network.";
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                _toast = "Could not write the page: " + exception.Message;
            }

            return Task.CompletedTask;
        };
    }

    /// <summary>
    /// Opens the coordination screen behind K. It is the one reference panel that lists something
    /// the operator can fix rather than only read: an expired reservation is reported as stale
    /// forever, because nothing deletes the record, so R releases them.
    /// </summary>
    private void OpenCoordination(DashboardSnapshot snapshot)
    {
        _overlay = Reference.Coordination(snapshot);
        _overlayCompleted = (completed, current, _) =>
        {
            if (completed is not InfoPanel { ActionRequested: true })
            {
                return Task.CompletedTask;
            }

            var expired = current.Claims
                .Where(claim => claim.ExpiresAt <= current.CapturedAt)
                .ToArray();
            // The store is asked directly rather than through ClaimService, whose Release throws
            // when a claim has already gone. Here that race is the wanted outcome, not an error.
            var released = expired.Count(claim => _store.DeleteClaim(claim.Id));

            _toast = released == 0
                ? "Those reservations were already gone."
                : $"Released {released} expired reservation{(released == 1 ? "" : "s")}.";
            return Task.CompletedTask;
        };
    }

    /// <summary>
    /// Opens the settings screen behind S, and applies whatever it asks to change. The explanation
    /// was already here; what was missing was any way to act on it without leaving for a JSON file.
    /// </summary>
    private void OpenSettings(DashboardSnapshot snapshot)
    {
        _overlay = new SettingsBrowser(snapshot.Config, _store.Paths.Config);
        _overlayCompleted = (completed, current, _) =>
        {
            var browser = (SettingsBrowser)completed;
            if (browser.WantsReference)
            {
                _overlay = Reference.Settings(current.Config, _store.Paths.Config);
                return Task.CompletedTask;
            }

            if (SettingsBrowser.ReadOnly.TryGetValue(browser.ChosenKey, out var reason))
            {
                _toast = reason;
                return Task.CompletedTask;
            }

            OpenSettingEditor(current.Config, browser.ChosenKey);
            return Task.CompletedTask;
        };
    }

    /// <summary>
    /// Asks for one setting's new value through the same guided form everything else uses, then
    /// writes the whole configuration back.
    /// </summary>
    private void OpenSettingEditor(FknrtdConfig config, string key)
    {
        if (SettingsBrowser.Form(config, key) is not { } form ||
            SettingsBrowser.Editor(key) is not { } setting)
        {
            _toast = $"'{key}' cannot be changed from here.";
            return;
        }

        _overlay = form;
        _overlayCompleted = async (completed, current, token) =>
        {
            var answered = ((Wizard)completed).Value(key);
            // The configuration is re-read on every frame, so the write is built from the snapshot
            // taken now rather than the one the form was opened against.
            var updated = setting.Write(current.Config, answered);
            await _store.SaveConfigAsync(updated, token).ConfigureAwait(false);
            _toast = $"{key} is now {Summarise(setting, updated)}. Press S to see the rest.";
        };
    }

    /// <summary>The saved value, said back the way the operator would recognise it.</summary>
    private static string Summarise(EditableSetting setting, FknrtdConfig config)
    {
        var value = setting.Read(config);
        return value.Length == 0
            ? "empty"
            : value.Contains((char)10)
                ? value.Split((char)10).Length + " commands"
                : value;
    }

    /// <summary>
    /// Opens the agent roster behind A, and applies whatever it asks for. Enabling and disabling
    /// are applied straight away because they are reversible with the same keystroke; adding opens
    /// the builder, and removing has to be confirmed by name.
    /// </summary>
    private void OpenAgentManager(DashboardSnapshot snapshot)
    {
        _overlay = AgentManager.Create(snapshot);
        _overlayCompleted = async (completed, current, token) =>
        {
            var manager = (AgentManager)completed;
            switch (manager.Action)
            {
                case AgentAction.Add:
                    OpenAgentWizard(current.Config);
                    return;
                case AgentAction.Toggle:
                    await ToggleAgentAsync(current.Config, manager.AgentId, token).ConfigureAwait(false);
                    return;
                case AgentAction.Remove:
                    ConfirmAgentRemoval(current, manager.AgentId);
                    return;
            }
        };
    }

    /// <summary>Flips one agent's enabled flag and writes the configuration back.</summary>
    private async Task ToggleAgentAsync(FknrtdConfig config, string agentId, CancellationToken cancellationToken)
    {
        var agent = config.Agents.FirstOrDefault(item =>
            item.Id.Equals(agentId, StringComparison.OrdinalIgnoreCase));
        if (agent is null)
        {
            _toast = $"'{agentId}' is no longer configured.";
            return;
        }

        var enabled = !agent.Enabled;
        var updated = config with
        {
            Agents = config.Agents
                .Select(item => item.Id.Equals(agentId, StringComparison.OrdinalIgnoreCase)
                    ? item with { Enabled = enabled }
                    : item)
                .ToList()
        };
        await _store.SaveConfigAsync(updated, cancellationToken).ConfigureAwait(false);
        _toast = enabled
            ? $"{agentId} is enabled. New tasks can now be assigned to it."
            : $"{agentId} is disabled. It stays out of the task builder until you turn it back on.";
    }

    /// <summary>
    /// Asks before deleting an agent's configuration. The shell command demands -confirm REMOVE for
    /// the same reason: the profile, arguments and environment are not recoverable from anywhere.
    /// </summary>
    private void ConfirmAgentRemoval(DashboardSnapshot snapshot, string agentId)
    {
        var agent = snapshot.Config.Agents.FirstOrDefault(item =>
            item.Id.Equals(agentId, StringComparison.OrdinalIgnoreCase));
        if (agent is null)
        {
            _toast = $"'{agentId}' is no longer configured.";
            return;
        }

        var used = snapshot.Tasks.Count(task =>
            task.LeadAgentId.Equals(agentId, StringComparison.OrdinalIgnoreCase) ||
            task.ImplementerAgentId.Equals(agentId, StringComparison.OrdinalIgnoreCase) ||
            task.AuditorAgentId.Equals(agentId, StringComparison.OrdinalIgnoreCase));

        _overlay = new Confirmation(
            "REMOVE THIS AGENT",
            Theme.Red,
            $"{agent.Id} - {agent.DisplayName}",
            "This deletes its command profiles, its arguments and its environment from this " +
            "workspace's configuration. Nothing else stores them, so they cannot be restored " +
            "except by configuring the agent again." +
            (used == 0
                ? string.Empty
                : $" {(used == 1 ? "One task already names it" : $"{used} tasks already name it")}, " +
                  "and would fail on the stage that needed it."),
            "REMOVE",
            "agent",
            "If you only want it out of the task builder, press Esc and disable it with Space " +
            "instead. That is reversible.");
        _overlayCompleted = async (_, current, token) =>
        {
            var remaining = current.Config.Agents
                .Where(item => !item.Id.Equals(agentId, StringComparison.OrdinalIgnoreCase))
                .ToList();
            await _store.SaveConfigAsync(current.Config with { Agents = remaining }, token)
                .ConfigureAwait(false);
            _toast = $"Removed {agentId}. Press A to see what is left.";
        };
    }

    /// <summary>Opens the agent builder, and adds whatever it describes to the configuration.</summary>
    private void OpenAgentWizard(FknrtdConfig config)
    {
        _overlay = AgentWizard.Create(config);
        _overlayCompleted = async (completed, current, token) =>
        {
            var agent = AgentWizard.Build((Wizard)completed);
            if (current.Config.Agents.Any(item => item.Id.Equals(agent.Id, StringComparison.OrdinalIgnoreCase)))
            {
                _toast = $"'{agent.Id}' is already configured. Press A and use Space to enable it.";
                return;
            }

            await _store
                .SaveConfigAsync(
                    current.Config with { Agents = current.Config.Agents.Append(agent).ToList() }, token)
                .ConfigureAwait(false);
            _toast = ExecutableLocator.Find(agent.Executable) is null
                ? $"Added {agent.Id}, but {agent.Executable} is not on PATH yet. Press D to recheck."
                : $"Added {agent.Id}. It is now offered by the task builder.";
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
        _overlayCompleted = async (completed, _, token) =>
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

    /// <summary>
    /// Asks Codex for its current figures, then shows every agent's budget explained. The header has
    /// room for five numbers and no room to say what any of them mean or why one is blank.
    /// </summary>
    private async Task ShowUsageAsync(DashboardSnapshot snapshot, CancellationToken cancellationToken)
    {
        string? error = null;
        try
        {
            await _usage.RefreshCodexAsync(cancellationToken).ConfigureAwait(false);
            _toast = "Budget refreshed.";
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            error = exception.Message;
            _toast = "Could not refresh the budget.";
        }

        // Re-read so the panel shows what the refresh just wrote rather than the frame's snapshot.
        var refreshed = await _snapshots.CaptureAsync(cancellationToken).ConfigureAwait(false);
        _overlay = Reference.Usage(refreshed, error);
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
        AgentActivityState.Planning => "◊",
        AgentActivityState.Running => "►",
        AgentActivityState.Reviewing => "♦",
        AgentActivityState.Waiting => "▌",
        AgentActivityState.Blocked => "■",
        AgentActivityState.Failed => "×",
        AgentActivityState.Completed => "√",
        AgentActivityState.Idle => "○",
        AgentActivityState.Offline => "○",
        _ => "?"
    };

    private static string StatusIcon(WorkflowStatus status) => status switch
    {
        WorkflowStatus.Running => "►",
        WorkflowStatus.ReadyToLand => "♦",
        WorkflowStatus.Landed => "√",
        WorkflowStatus.Failed => "×",
        WorkflowStatus.Cancelled => "▌",
        WorkflowStatus.Waiting => "▌",
        _ => "○"
    };

    private static string StageIcon(StageState state) => state switch
    {
        StageState.Running => "►",
        StageState.Passed => "√",
        StageState.Failed => "×",
        StageState.Skipped => "◊",
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
        EventSeverity.Success => "√",
        EventSeverity.Warning => "∆",
        EventSeverity.Error => "×",
        EventSeverity.Critical => "×",
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
