using FKNRTD.Domain;
using FKNRTD.Help;
using FKNRTD.Services;

namespace FKNRTD.Dashboard;

/// <summary>
/// The dashboard's read-only reference surfaces. Each is an <see cref="InfoPanel"/> assembled from
/// the same tables the rest of the product reads, so nothing here can describe a key that does not
/// exist or a stage the pipeline no longer runs.
/// </summary>
internal static class Reference
{
    /// <summary>
    /// The key reference and searchable glossary behind <c>?</c>. Filtering spans both, because an
    /// operator who does not know what a word means also does not know which list it is in.
    /// </summary>
    public static InfoPanel Help() => new(
        "HELP",
        Theme.Cyan,
        filter =>
        {
            var blocks = new List<InfoBlock>();
            var matchingKeys = Keymap.All
                .Where(binding => filter.Length == 0 ||
                                  binding.Key.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                                  binding.Action.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                                  binding.Detail.Contains(filter, StringComparison.OrdinalIgnoreCase))
                .ToArray();

            if (matchingKeys.Length > 0)
            {
                blocks.Add(new InfoHeading("Keys"));
                foreach (var binding in matchingKeys)
                {
                    blocks.Add(new InfoLine(binding.Key, binding.Action, Theme.Foreground, Bold: true));
                    blocks.Add(new InfoParagraph(binding.Detail, Theme.Muted, Indent: 2));
                }
            }

            var terms = Glossary.Search(filter);
            foreach (var category in Glossary.Categories)
            {
                var entries = terms.Where(entry => entry.Category == category).ToArray();
                if (entries.Length == 0)
                {
                    continue;
                }

                blocks.Add(new InfoHeading(category));
                foreach (var entry in entries)
                {
                    blocks.Add(new InfoLine(entry.Title, entry.Summary, Theme.Foreground, Bold: true));
                    blocks.Add(new InfoParagraph(entry.Detail, Theme.Muted, Indent: 2));
                    if (entry.Example.Length > 0)
                    {
                        blocks.Add(new InfoParagraph("e.g.  " + entry.Example, Theme.Cyan, Indent: 2));
                    }
                }
            }

            return blocks;
        },
        filterHint: "a key, a word, or anything you do not recognise",
        // The one thing this panel could not do was let you keep it. F2 writes the whole reference
        // out as a page you can open away from the terminal, or send to whoever asks you next.
        action: (ConsoleKey.F2, "F2", "save all of this as a web page"));

    /// <summary>
    /// The full record of one task behind <c>I</c>. Each stage is shown with what that stage is for,
    /// so a pipeline position means something without the operator having to look it up elsewhere.
    /// </summary>
    public static InfoPanel Task(WorkflowTask task, FknrtdConfig config)
    {
        var blocks = new List<InfoBlock>
        {
            new InfoHeading(task.Title),
            new InfoLine("Id", task.Id, Theme.Muted),
            new InfoLine("Status", Describe(task.Status), StatusColour(task.Status), Bold: true)
        };

        var status = Glossary.Find("status." + task.Status.ToString().ToLowerInvariant());
        if (status is not null)
        {
            blocks.Add(new InfoParagraph(status.Detail, Theme.Muted, Indent: 2));
        }

        blocks.Add(new InfoHeading("What to do next"));
        blocks.Add(new InfoParagraph(NextStep(task, config)));

        blocks.Add(new InfoHeading("What was asked for"));
        blocks.Add(new InfoParagraph(task.Brief));

        blocks.Add(new InfoHeading("Who is on it"));
        blocks.Add(new InfoLine("Lead", task.LeadAgentId + "  —  reads the code and writes the plan"));
        blocks.Add(new InfoLine("Implementer",
            task.ImplementerAgentId + "  —  the only agent that may change files"));
        blocks.Add(new InfoLine("Auditor",
            task.AuditorAgentId + "  —  judges the finished work and must return PASS"));

        blocks.Add(new InfoHeading("Where the work happens"));
        if (config.Mode == WorkspaceMode.Git)
        {
            blocks.Add(new InfoLine("Base branch", Blank(task.BaseRef)));
            blocks.Add(new InfoLine("Task branch", Blank(task.BranchName)));
            blocks.Add(new InfoLine("Worktree", Blank(task.WorktreePath)));
        }
        else
        {
            blocks.Add(new InfoParagraph(
                "This is a standalone workspace, so there is no branch and no worktree. Agents edit " +
                "this folder directly and landing records that the verified work is already in place.",
                Theme.Amber));
        }

        blocks.Add(new InfoHeading("How correctness is decided"));
        if (task.VerificationCommands.Count == 0)
        {
            blocks.Add(new InfoParagraph(
                "No verification commands are set, so nothing independent checks this work. The audit " +
                "is the only gate, and an audit is a judgement rather than a measurement.",
                Theme.Amber));
        }
        else
        {
            foreach (var command in task.VerificationCommands)
            {
                blocks.Add(new InfoLine("must exit 0", command, Theme.Foreground));
            }
        }

        blocks.Add(new InfoLine("Repair rounds", $"{task.RepairRound} used of {task.MaxRepairRounds} allowed",
            Theme.Muted));

        blocks.Add(new InfoHeading("Pipeline"));
        foreach (var stage in task.Stages)
        {
            var entry = Glossary.Find("stage." + stage.Stage.ToString().ToLowerInvariant());
            var summary = string.IsNullOrWhiteSpace(stage.Summary) ? entry?.Summary ?? string.Empty : stage.Summary;
            blocks.Add(new InfoLine(
                $"{Icon(stage.State)} {stage.Stage}",
                summary,
                StageColour(stage.State),
                Bold: stage.State == StageState.Running));
        }

        if (!string.IsNullOrWhiteSpace(task.LastError))
        {
            blocks.Add(new InfoHeading("What went wrong"));
            blocks.Add(new InfoParagraph(task.LastError, Theme.Red));
        }

        return new InfoPanel("TASK", Theme.Blue, blocks);
    }

    /// <summary>
    /// The workspace history behind <c>E</c>. The overview's events panel shows the last few lines,
    /// which is the wrong instrument for "what happened an hour ago". This is searchable and keeps
    /// the severity legible without relying on colour.
    /// </summary>
    public static InfoPanel Events(IReadOnlyList<FknrtdEvent> events, DateTimeOffset now) => new(
        "HISTORY",
        Theme.Cyan,
        filter =>
        {
            var matching = events
                .Where(item => filter.Length == 0 ||
                               item.Message.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                               item.Type.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                               item.Severity.ToString().Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                               (item.TaskId?.Contains(filter, StringComparison.OrdinalIgnoreCase) ?? false))
                .OrderByDescending(item => item.Timestamp)
                .ToArray();

            if (matching.Length == 0)
            {
                return
                [
                    new InfoParagraph(events.Count == 0
                        ? "Nothing has happened in this workspace yet. Events are recorded as tasks are " +
                          "created, stages run, and agents report in."
                        : "No event matches that. Try a task id, a severity such as error, or an event " +
                          "type such as stage.failed.", Theme.Muted)
                ];
            }

            var blocks = new List<InfoBlock>();
            foreach (var item in matching)
            {
                blocks.Add(new InfoLine(
                    $"{Severity(item.Severity)} {Text.Age(item.Timestamp, now)} ago",
                    item.Message,
                    EventColour(item.Severity),
                    Bold: item.Severity >= EventSeverity.Error));
                blocks.Add(new InfoParagraph(
                    item.TaskId is null ? item.Type : $"{item.Type}  ·  {item.TaskId}",
                    Theme.Muted, Indent: 2));
            }

            return blocks;
        },
        filterHint: "a task id, a severity, or part of a message");

    private static string Severity(EventSeverity severity) => severity switch
    {
        EventSeverity.Success => "OK   ",
        EventSeverity.Warning => "WARN ",
        EventSeverity.Error => "ERROR",
        EventSeverity.Critical => "FATAL",
        EventSeverity.Trace => "trace",
        _ => "info "
    };

    private static Rgb EventColour(EventSeverity severity) => severity switch
    {
        EventSeverity.Success => Theme.Green,
        EventSeverity.Warning => Theme.Amber,
        EventSeverity.Error or EventSeverity.Critical => Theme.Red,
        EventSeverity.Trace => Theme.Muted,
        _ => Theme.Foreground
    };

    /// <summary>
    /// What the agents have reserved and where they overlap, behind <c>K</c>. The overview shows the
    /// worst conflict in three words; this says which paths, which agents, and what to do about it.
    /// </summary>
    public static InfoPanel Coordination(DashboardSnapshot snapshot)
    {
        var expired = snapshot.Claims.Count(claim => claim.ExpiresAt <= snapshot.CapturedAt);
        var blocks = new List<InfoBlock>();

        blocks.Add(new InfoHeading("Overlaps"));
        if (snapshot.Conflicts.Count == 0)
        {
            blocks.Add(new InfoParagraph(
                "No overlap. Nothing that is running is about to write a file that something else " +
                "is also working on.", Theme.Green));
        }
        else
        {
            foreach (var conflict in snapshot.Conflicts)
            {
                var entry = Glossary.Find("conflict." + conflict.Kind.ToString().ToLowerInvariant());
                blocks.Add(new InfoLine(
                    conflict.Kind == ConflictKind.Collision ? "COLLISION" : conflict.Kind.ToString(),
                    conflict.Summary,
                    conflict.Kind == ConflictKind.Collision ? Theme.Red : Theme.Amber,
                    Bold: conflict.Kind == ConflictKind.Collision));
                blocks.Add(new InfoParagraph("Paths: " + string.Join(", ", conflict.Paths), Theme.Foreground,
                    Indent: 2));
                if (conflict.AgentIds.Count > 0)
                {
                    blocks.Add(new InfoParagraph(
                        conflict.AgentIds.Count == 1
                            ? "Held by: " + conflict.AgentIds[0]
                            : "Between: " + string.Join(" and ", conflict.AgentIds),
                        Theme.Muted, Indent: 2));
                }

                if (entry is not null)
                {
                    blocks.Add(new InfoParagraph(entry.Detail, Theme.Muted, Indent: 2));
                }
            }
        }

        blocks.Add(new InfoHeading("Reservations"));
        if (snapshot.Claims.Count == 0)
        {
            blocks.Add(new InfoParagraph(
                "No agent has reserved any path. Claims are optional: an agent that does not declare " +
                "what it is about to touch simply cannot be warned about an overlap in advance.",
                Theme.Muted));
        }
        else
        {
            foreach (var claim in snapshot.Claims.OrderBy(item => item.AgentId, StringComparer.Ordinal))
            {
                var remaining = claim.ExpiresAt - snapshot.CapturedAt;
                var expiry = remaining <= TimeSpan.Zero
                    ? "expired"
                    : $"{remaining.TotalSeconds:0}s left";
                blocks.Add(new InfoLine(
                    $"{claim.AgentId} · {claim.Mode.ToString().ToLowerInvariant()}",
                    string.Join(", ", claim.Paths),
                    remaining <= TimeSpan.Zero ? Theme.Muted : Theme.Foreground));
                blocks.Add(new InfoParagraph(expiry, Theme.Muted, Indent: 2));
            }
        }

        blocks.Add(new InfoHeading("Messages"));
        if (snapshot.Messages.Count == 0)
        {
            blocks.Add(new InfoParagraph(
                "Nothing on the message bus. Press M to record a hand-off between agents.", Theme.Muted));
        }
        else
        {
            foreach (var message in snapshot.Messages)
            {
                blocks.Add(new InfoLine(
                    $"{message.FromAgentId} to {message.ToAgentId}",
                    message.Text,
                    message.Delivery == MessageDelivery.Acknowledged ? Theme.Muted : Theme.Foreground));
                blocks.Add(new InfoParagraph(
                    $"{message.Delivery.ToString().ToLowerInvariant()} · " +
                    $"{Text.Age(message.CreatedAt, snapshot.CapturedAt)} ago",
                    Theme.Muted, Indent: 2));
            }
        }

        var claimEntry = Glossary.Find("claim");
        if (claimEntry is not null)
        {
            blocks.Add(new InfoHeading("How this works"));
            blocks.Add(new InfoParagraph(claimEntry.Detail, Theme.Muted));
        }

        if (expired > 0)
        {
            blocks.Add(new InfoGap());
            blocks.Add(new InfoParagraph(
                (expired == 1
                    ? "One reservation has passed its expiry. "
                    : expired + " reservations have passed their expiry. ") +
                "An expired reservation does not go away on its own: the record stays " +
                "where it is and keeps being reported as stale until somebody renews or releases it. " +
                "Press R to release every expired one now. Nothing that is running is affected - a " +
                "reservation is a warning to you, not a lock on anything.", Theme.Amber));
        }

        var worst = snapshot.Conflicts.FirstOrDefault();
        return new InfoPanel("COORDINATION",
            worst is null ? Theme.Green : worst.Kind == ConflictKind.Collision ? Theme.Red : Theme.Amber,
            _ => blocks,
            action: expired == 0
                ? null
                : (ConsoleKey.R, "R", expired == 1 ? "release the expired one" : $"release {expired} expired"));
    }

    /// <summary>
    /// The workspace's settings behind <c>S</c>, each shown with what this workspace currently has
    /// rather than only with its default. The configuration file is plain JSON meant to be edited by
    /// hand and carried no explanation of any kind.
    /// </summary>
    public static InfoPanel Settings(FknrtdConfig config, string configPath) => new(
        "SETTINGS",
        Theme.Blue,
        filter =>
        {
            var blocks = new List<InfoBlock>
            {
                new InfoParagraph("Edit these in " + configPath + ", then run 'fknrtd config validate'.",
                    Theme.Muted)
            };

            var matched = false;
            foreach (var section in SettingsCatalog.Sections)
            {
                var entries = SettingsCatalog.InSection(section)
                    .Where(entry => filter.Length == 0 ||
                                    entry.Key.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                                    entry.Title.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                                    entry.Summary.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                                    entry.Detail.Contains(filter, StringComparison.OrdinalIgnoreCase))
                    .ToArray();
                if (entries.Length == 0)
                {
                    continue;
                }

                matched = true;
                blocks.Add(new InfoHeading(section));
                foreach (var entry in entries)
                {
                    var live = CurrentValue(config, entry.Key);
                    blocks.Add(new InfoLine(entry.Key, live ?? entry.Summary, Theme.Foreground, Bold: true));
                    if (live is not null)
                    {
                        blocks.Add(new InfoParagraph(entry.Summary, Theme.Muted, Indent: 2));
                    }

                    blocks.Add(new InfoParagraph(entry.Detail, Theme.Muted, Indent: 2));
                    blocks.Add(new InfoParagraph("If you change it: " + entry.IfYouChangeIt, Theme.Amber,
                        Indent: 2));
                }
            }

            if (!matched)
            {
                blocks.Add(new InfoParagraph("No setting matches that.", Theme.Muted));
            }

            return blocks;
        },
        filterHint: "a setting name, or what you are trying to change");

    /// <summary>
    /// What this workspace has, for the settings whose value is a single readable scalar. A default
    /// is what the documentation says; the live value is what the operator is actually running.
    /// </summary>
    private static string? CurrentValue(FknrtdConfig config, string key) => key switch
    {
        "schemaVersion" => config.SchemaVersion.ToString(),
        "projectName" => config.ProjectName,
        "mode" => config.Mode.ToString(),
        "defaultBaseRef" => string.IsNullOrWhiteSpace(config.DefaultBaseRef) ? "none" : config.DefaultBaseRef,
        "defaultVerificationCommands" => config.DefaultVerificationCommands.Count == 0
            ? "none — nothing checks agent work in this workspace"
            : string.Join("; ", config.DefaultVerificationCommands),
        "defaultMaxRepairRounds" => config.DefaultMaxRepairRounds.ToString(),
        "maxParallelAgents" => config.MaxParallelAgents.ToString(),
        "agentTimeoutSeconds" => $"{config.AgentTimeoutSeconds}s",
        "verificationTimeoutSeconds" => $"{config.VerificationTimeoutSeconds}s",
        "agentStaleAfterSeconds" => $"{config.AgentStaleAfterSeconds}s",
        "claimStaleAfterSeconds" => $"{config.ClaimStaleAfterSeconds}s",
        "dashboardRefreshMilliseconds" => $"{config.DashboardRefreshMilliseconds}ms",
        "requireCleanTreeForLanding" => config.RequireCleanTreeForLanding ? "true" : "false",
        "autoCommitAgentChanges" => config.AutoCommitAgentChanges ? "true" : "false",
        "agents" => config.Agents.Count == 0
            ? "none configured"
            : string.Join(", ", config.Agents.Select(agent => agent.Enabled ? agent.Id : agent.Id + " (disabled)")),
        _ => null
    };

    /// <summary>
    /// Rate-limit budget behind <c>U</c>, after a refresh. The header has room for five numbers and
    /// no room to say what any of them mean, where they came from, or why one is blank.
    /// </summary>
    public static InfoPanel Usage(DashboardSnapshot snapshot, string? refreshError)
    {
        var blocks = new List<InfoBlock>();
        if (refreshError is not null)
        {
            blocks.Add(new InfoHeading("Refresh failed"));
            blocks.Add(new InfoParagraph(refreshError, Theme.Red));
        }

        foreach (var agent in snapshot.Config.Agents)
        {
            var usage = snapshot.Usage.FirstOrDefault(item =>
                item.AgentId.Equals(agent.Id, StringComparison.OrdinalIgnoreCase));
            blocks.Add(new InfoHeading(agent.DisplayName));
            if (usage is null)
            {
                blocks.Add(new InfoParagraph(HowToGetFigures(agent.Id), Theme.Amber));
                continue;
            }

            blocks.Add(new InfoLine("Context left", Percent(usage.ContextRemainingPercent),
                Colour(usage.ContextRemainingPercent)));
            blocks.Add(new InfoLine("5-hour window", Percent(usage.FiveHourRemainingPercent),
                Colour(usage.FiveHourRemainingPercent)));
            blocks.Add(new InfoLine("7-day window", Percent(usage.WeeklyRemainingPercent),
                Colour(usage.WeeklyRemainingPercent)));
            blocks.Add(new InfoLine("Reported by", usage.Source, Theme.Muted));
            blocks.Add(new InfoLine("Last updated",
                Text.Age(usage.UpdatedAt, snapshot.CapturedAt) + " ago", Theme.Muted));

            var lowest = new[]
            {
                usage.FiveHourRemainingPercent, usage.WeeklyRemainingPercent
            }.Where(value => value is not null).Select(value => value!.Value).DefaultIfEmpty(100).Min();
            if (lowest < 25)
            {
                blocks.Add(new InfoParagraph(
                    $"{agent.DisplayName} is low. A long task started now may exhaust its budget " +
                    "mid-stage, which fails in a confusing way — the agent simply stops producing " +
                    "output. Consider a smaller brief, or waiting for the window to refill.",
                    Theme.Amber));
            }
        }

        blocks.Add(new InfoHeading("What these windows are"));
        foreach (var term in new[] { "context", "five-hour", "weekly" })
        {
            if (Glossary.Find(term) is { } entry)
            {
                blocks.Add(new InfoLine(entry.Title, entry.Summary, Theme.Foreground, Bold: true));
                blocks.Add(new InfoParagraph(entry.Detail, Theme.Muted, Indent: 2));
            }
        }

        return new InfoPanel("BUDGET", Theme.Green, blocks);
    }

    /// <summary>Why an agent has no figures, and what would give it some.</summary>
    private static string HowToGetFigures(string agentId) => agentId.ToLowerInvariant() switch
    {
        "claude" =>
            "Nothing reported yet. Claude reports through its statusline: install it with " +
            "'fknrtd integration install-claude-statusline' and restart Claude Code.",
        "codex" =>
            "Nothing reported yet. Press U to ask Codex directly; it answers on demand rather than " +
            "reporting on its own.",
        _ =>
            "Nothing reported yet. This agent has no automatic reporting. Feed figures in with " +
            "'fknrtd usage set <agent> -five-hour <percent>' if you track them elsewhere."
    };

    private static string Percent(double? value) => value is null ? "not reported" : $"{value:0}% left";

    private static Rgb Colour(double? remaining) => remaining switch
    {
        null => Theme.Muted,
        >= 60 => Theme.Green,
        >= 40 => Theme.Amber,
        >= 20 => Theme.Orange,
        _ => Theme.Red
    };

    /// <summary>
    /// The finished change behind <c>V</c>. Every surface in this product tells the operator to read
    /// the diff before landing it, and until now none of them would show it: they named a directory
    /// and left them to go and look. Searchable, because the question is usually about one file.
    /// </summary>
    public static InfoPanel Diff(
        WorkflowTask task,
        FknrtdConfig config,
        IReadOnlyList<string> lines,
        bool truncated) => new(
        "THE FINISHED CHANGE",
        Theme.Blue,
        filter =>
        {
            var blocks = new List<InfoBlock>
            {
                new InfoLine("Task", $"{task.Id} — {task.Title}", Theme.Muted)
            };

            if (config.Mode == WorkspaceMode.Standalone)
            {
                blocks.Add(new InfoParagraph(
                    "This is a standalone workspace, so there is no branch to compare against and no " +
                    "diff to show. Agents edited this folder directly; whatever changed is simply " +
                    "what is here now.", Theme.Amber));
                return blocks;
            }

            if (lines.Count == 0)
            {
                blocks.Add(new InfoParagraph(
                    $"Nothing has changed against {Blank(task.BaseRef)}. Either the task has not " +
                    "reached its implement stage yet, or the implementer finished without editing " +
                    "anything — which is itself worth knowing before you land it.", Theme.Amber));
                return blocks;
            }

            blocks.Add(new InfoLine("Against", Blank(task.BaseRef), Theme.Muted));
            var matching = filter.Length == 0
                ? lines
                : Relevant(lines, filter);
            if (matching.Count == 0)
            {
                blocks.Add(new InfoParagraph("No file or line in this change matches that.", Theme.Muted));
                return blocks;
            }

            blocks.Add(new InfoGap());
            foreach (var line in matching)
            {
                blocks.Add(new InfoRaw(line, DiffColour(line)));
            }

            if (truncated && filter.Length == 0)
            {
                blocks.Add(new InfoGap());
                blocks.Add(new InfoParagraph(
                    "The change is larger than this view will hold and has been cut off. Read the " +
                    $"rest with git in {Blank(task.WorktreePath)}.", Theme.Amber));
            }

            return blocks;
        },
        filterHint: "a file name, or any text in the change");

    /// <summary>
    /// Filtering a diff by line would strip the file headers that say what you are looking at, so a
    /// match keeps the hunk it belongs to and the file it came from.
    /// </summary>
    private static IReadOnlyList<string> Relevant(IReadOnlyList<string> lines, string filter)
    {
        var kept = new List<string>();
        var file = string.Empty;
        var hunk = string.Empty;
        var hunkMatches = false;
        var shownFile = string.Empty;
        var shownHunk = string.Empty;
        foreach (var line in lines)
        {
            if (line.StartsWith("diff --git ", StringComparison.Ordinal))
            {
                file = line;
                hunk = string.Empty;
                hunkMatches = false;
                continue;
            }

            if (line.StartsWith("@@", StringComparison.Ordinal))
            {
                hunk = line;
                // Git puts the enclosing function in the hunk header, so searching for a method name
                // often matches only there. Treat that as a match for the whole hunk rather than
                // finding nothing in the one place the reader was looking.
                hunkMatches = hunk.Contains(filter, StringComparison.OrdinalIgnoreCase);
                continue;
            }

            if (!hunkMatches &&
                !line.Contains(filter, StringComparison.OrdinalIgnoreCase) &&
                !file.Contains(filter, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (file.Length > 0 && file != shownFile)
            {
                kept.Add(file);
                shownFile = file;
                shownHunk = string.Empty;
            }

            if (hunk.Length > 0 && hunk != shownHunk)
            {
                kept.Add(hunk);
                shownHunk = hunk;
            }

            kept.Add(line);
        }

        return kept;
    }

    /// <summary>
    /// Added and removed lines are the two things the eye needs to separate, and the leading + and -
    /// already do that without colour — this only makes it faster, never the only signal.
    /// </summary>
    private static Rgb DiffColour(string line)
    {
        if (line.StartsWith("+++", StringComparison.Ordinal) ||
            line.StartsWith("---", StringComparison.Ordinal) ||
            line.StartsWith("diff --git ", StringComparison.Ordinal) ||
            line.StartsWith("index ", StringComparison.Ordinal) ||
            line.StartsWith("new file", StringComparison.Ordinal) ||
            line.StartsWith("deleted file", StringComparison.Ordinal))
        {
            return Theme.Violet;
        }

        if (line.StartsWith("@@", StringComparison.Ordinal))
        {
            return Theme.Cyan;
        }

        if (line.StartsWith('+'))
        {
            return Theme.Green;
        }

        return line.StartsWith('-') ? Theme.Red : Theme.Muted;
    }

    /// <summary>
    /// What each agent on this task will actually be told, behind <c>P</c>. This product's whole
    /// argument is that you should know what you are authorising before an agent runs, and the one
    /// thing it would not show was the instruction the agent receives.
    /// </summary>
    public static InfoPanel Prompts(WorkflowTask task, FknrtdConfig config, string? plan) => new(
        "WHAT THE AGENTS ARE TOLD",
        Theme.Violet,
        filter =>
        {
            var blocks = new List<InfoBlock>
            {
                new InfoParagraph(
                    "This is the text each agent receives, composed from your brief. Nothing else is " +
                    "sent. Each agent then reads the code itself, under the sandbox its profile sets.",
                    Theme.Muted)
            };

            void Section(string heading, string role, string prompt)
            {
                if (filter.Length > 0 &&
                    !heading.Contains(filter, StringComparison.OrdinalIgnoreCase) &&
                    !prompt.Contains(filter, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                blocks.Add(new InfoHeading(heading));
                blocks.Add(new InfoParagraph(role, Theme.Muted));
                blocks.Add(new InfoGap());
                // Wrapped, not truncated. A diff needs its leading + or - kept at the start of a
                // row, which is why that view renders raw; a prompt is prose, and cutting an
                // instruction off at the panel edge defeats the entire point of showing it.
                foreach (var line in prompt.Split((char)10))
                {
                    var text = line.TrimEnd((char)13);
                    blocks.Add(text.Length == 0 ? new InfoGap() : new InfoParagraph(text));
                }
            }

            Section($"Sent to {task.LeadAgentId} — the plan stage", "Read-only. It proposes; it changes nothing.",
                AgentPrompts.Plan(task));

            Section($"Sent to {task.ImplementerAgentId} — the implement stage",
                "The only agent that may write files. This prompt carries the lead's plan.",
                AgentPrompts.Implement(
                    task,
                    config.Mode,
                    plan ?? "[the lead's plan is inserted here once the plan stage has run]",
                    task.RepairRound == 0
                        ? string.Empty
                        : "[on a repair round, the failing verification output is added here]"));

            Section($"Sent to {task.AuditorAgentId} — the audit stage",
                "Read-only. It must end with a PASS or FAIL verdict.",
                AgentPrompts.Audit(
                    task,
                    config.Mode,
                    "[the verification results are inserted here once the verify stage has run]"));

            if (blocks.Count == 1)
            {
                blocks.Add(new InfoParagraph("Nothing in these prompts matches that.", Theme.Muted));
            }

            return blocks;
        },
        filterHint: "any text you want to check is or is not being sent");

    /// <summary>The pre-flight checks behind <c>D</c>, with what a failure would actually cost.</summary>
    public static InfoPanel Doctor(IReadOnlyList<DoctorCheck> checks)
    {
        var blocks = new List<InfoBlock> { new InfoHeading("Pre-flight checks") };
        foreach (var check in checks)
        {
            var icon = check.Passed ? "√" : check.Required ? "×" : "∆";
            var colour = check.Passed ? Theme.Green : check.Required ? Theme.Red : Theme.Amber;
            blocks.Add(new InfoLine($"{icon} {check.Name}", check.Detail, colour,
                Bold: !check.Passed && check.Required));
        }

        var blocking = checks.Count(check => check.Required && !check.Passed);
        blocks.Add(new InfoHeading("What this means"));
        blocks.Add(new InfoParagraph(blocking == 0
            ? "Everything a task run depends on is in place. A ∆ is an optional capability that is " +
              "not available; it removes a feature rather than stopping work."
            : $"{blocking} required check{(blocking == 1 ? "" : "s")} failed. A task will not get " +
              "through the pipeline until that is fixed — most often an agent whose executable is " +
              "not on PATH, or Git missing from a Git-mode workspace."));
        var entry = Glossary.Find("doctor");
        if (entry is not null)
        {
            blocks.Add(new InfoParagraph(entry.Detail, Theme.Muted));
        }

        return new InfoPanel("DOCTOR", blocking == 0 ? Theme.Green : Theme.Red, blocks);
    }

    /// <summary>The agent roster behind <c>A</c>: who is configured, and what each one can actually do.</summary>
    public static InfoPanel Agents(FknrtdConfig config)
    {
        var blocks = new List<InfoBlock>();
        if (config.Agents.Count == 0)
        {
            blocks.Add(new InfoParagraph(
                "No agents are configured. Quit the dashboard and run 'fknrtd agent add -id <name> " +
                "-exe <executable>' to register a coding CLI, or 'fknrtd init -force' to restore the " +
                "built-in Claude and Codex definitions."));
            return new InfoPanel("AGENTS", Theme.Amber, blocks);
        }

        foreach (var agent in config.Agents)
        {
            var found = ExecutableLocator.Find(agent.Executable);
            var audits = TaskWizard.CanAudit(config.Agents, agent.Id);
            blocks.Add(new InfoHeading(agent.DisplayName));
            blocks.Add(new InfoLine("Id", agent.Id, Theme.Muted));
            blocks.Add(new InfoLine("Runs", agent.Executable));
            blocks.Add(new InfoLine("On PATH",
                found is not null ? "yes — " + found : "no. This agent cannot be launched from here.",
                found is not null ? Theme.Green : Theme.Red,
                Bold: found is null));
            blocks.Add(new InfoLine("Enabled",
                agent.Enabled ? "yes" : "no. It will not be offered for any role.",
                agent.Enabled ? Theme.Green : Theme.Muted));
            blocks.Add(new InfoLine("Can audit",
                audits
                    ? "yes — its audit profile returns a PASS or FAIL verdict"
                    : "no. Without successMarker and failureMarker it could never approve work.",
                audits ? Theme.Green : Theme.Amber));
            blocks.Add(new InfoLine("Profiles", string.Join(", ", agent.Profiles.Keys.Order(StringComparer.Ordinal)),
                Theme.Muted));
        }

        var entry = Glossary.Find("agent");
        if (entry is not null)
        {
            blocks.Add(new InfoHeading("What an agent is"));
            blocks.Add(new InfoParagraph(entry.Detail, Theme.Muted));
        }

        blocks.Add(new InfoHeading("Adding another"));
        blocks.Add(new InfoParagraph(
            "Quit the dashboard and run 'fknrtd agent new'. It asks what to call the agent, which " +
            "program runs it and how that program wants its prompt, explaining each as it goes."));

        return new InfoPanel("AGENTS", Theme.Violet, blocks);
    }

    /// <summary>
    /// The first thing an operator meets in a workspace with no tasks in it. A command center that
    /// opens on an empty grid teaches nothing about what it is for.
    /// </summary>
    public static InfoPanel Welcome(FknrtdConfig config)
    {
        var product = Glossary.Find("fknrtd");
        var blocks = new List<InfoBlock>
        {
            new InfoHeading($"{config.ProjectName} is ready"),
            new InfoParagraph(product?.Detail ?? string.Empty),
            new InfoHeading("How a piece of work moves through it"),
            new InfoParagraph("1. You write a brief saying what done looks like."),
            new InfoParagraph("2. The lead agent reads your code and writes a plan, with a profile that " +
                              "asks it not to edit."),
            new InfoParagraph("3. The implementer makes the change" +
                              (config.Mode == WorkspaceMode.Git
                                  ? " in its own worktree, so your checkout never moves."
                                  : " directly in this folder.")),
            new InfoParagraph("4. Your own commands run. Every one must exit 0 or the work goes back."),
            new InfoParagraph("5. A third agent audits the result and returns PASS or FAIL."),
            new InfoParagraph("6. You read the diff and type LAND. Nothing merges without that."),
            new InfoHeading("Start here"),
            new InfoLine("N", "Describe the first piece of work. Every field explains itself as you reach it."),
            new InfoLine("D", "Check that everything a run depends on is actually installed."),
            new InfoLine("?", "The key reference and a glossary of every word this product uses."),
            new InfoGap()
        };

        if (config.Mode == WorkspaceMode.Standalone)
        {
            blocks.Add(new InfoParagraph(
                "This workspace is standalone: it is not a Git repository, so agents edit this folder " +
                "in place and there is nothing to roll back to. Making it a repository first is " +
                "strictly safer.",
                Theme.Amber));
        }
        else if (Glossary.Find("what-to-commit") is { } committing)
        {
            // Setting up prints this too, but a bare `fknrtd` goes straight into the dashboard and
            // that line scrolls away into the alternate screen before anyone reads it.
            blocks.Add(new InfoHeading("About that untracked .fknrtd"));
            blocks.Add(new InfoParagraph(committing.Detail, Theme.Muted));
        }

        if (config.DefaultVerificationCommands.Count == 0)
        {
            blocks.Add(new InfoParagraph(
                "No default verification commands were detected for this project, so new tasks start " +
                "with nothing checking them. Add your build and test commands when the task builder " +
                "asks — that is the one gate an agent cannot talk its way past.",
                Theme.Amber));
        }

        return new InfoPanel("WELCOME TO FKNRTD.CLI", Theme.Cyan, blocks);
    }

    /// <summary>
    /// The most useful thing to do with this task now. Shared between the dashboard and
    /// <c>fknrtd task show</c>, because two surfaces that disagree about the next step are worse
    /// than either alone — but phrased for the surface asking, since "press Enter" is no use in a
    /// shell and "run fknrtd task run" is no use with the dashboard already open.
    /// </summary>
    public static string NextStep(WorkflowTask task, FknrtdConfig config, bool onDashboard = true)
    {
        var id = task.Id;
        return task.Status switch
        {
            WorkflowStatus.Queued => onDashboard
                ? "Press Enter to run it. The lead agent starts first and nothing is written until " +
                  "the implement stage."
                : $"Run it with 'fknrtd task run {id}'. The lead agent starts first and nothing is " +
                  "written until the implement stage.",

            WorkflowStatus.Running => onDashboard
                ? "Press L to watch the live output of the current stage. Press C if you want it to " +
                  "stop; the running agent is killed, so cancelling is quick but not instantaneous."
                : $"It is running. Watch it with 'fknrtd dashboard', or stop it with " +
                  $"'fknrtd task cancel {id}'.",

            WorkflowStatus.Failed => onDashboard
                ? "Press L to read the failing output. Fix whatever caused it — often the brief was " +
                  "ambiguous or a verification command is wrong — then press R to reset the failed " +
                  "stages and Enter to run again."
                : $"Read the failing output in the task's log, fix the cause — often an ambiguous " +
                  $"brief or a wrong verification command — then run 'fknrtd task retry {id}'.",

            WorkflowStatus.ReadyToLand => config.Mode == WorkspaceMode.Git
                ? onDashboard
                    ? $"Press V to read the finished change, then G and type LAND to merge it into " +
                      $"{Blank(task.BaseRef)}. Nothing moves until you do."
                    : $"Read the finished change with 'fknrtd task diff {id}', then land it with " +
                      $"'fknrtd task land {id} -confirm LAND'. Nothing moves until you do."
                : onDashboard
                    ? "The verified work is already in this folder. Press G and type LAND to record " +
                      "it as final."
                    : $"The verified work is already in this folder. Record it as final with " +
                      $"'fknrtd task land {id} -confirm LAND'.",

            WorkflowStatus.Landed => onDashboard
                ? "This is done and merged. Press X to remove its worktree when you no longer need " +
                  "to read it."
                : $"This is done and merged. Reclaim its worktree with " +
                  $"'fknrtd task cleanup {id} -confirm REMOVE' when you no longer need to read it.",

            WorkflowStatus.Cancelled => onDashboard
                ? "Press R to reset it, then Enter to run again. Anything the implementer had " +
                  "already written is still in the worktree."
                : $"Reset and run it again with 'fknrtd task retry {id}'. Anything the implementer " +
                  "had already written is still in the worktree.",

            _ => onDashboard
                ? "Press L to read the log for whatever this is waiting on."
                : "Read the task's log for whatever this is waiting on."
        };
    }

    private static string Describe(WorkflowStatus status) =>
        Glossary.Find("status." + status.ToString().ToLowerInvariant())?.Title ?? status.ToString();

    private static string Blank(string value) => string.IsNullOrWhiteSpace(value) ? "none" : value;

    private static string Icon(StageState state) => state switch
    {
        StageState.Running => "►",
        StageState.Passed => "√",
        StageState.Failed => "×",
        StageState.Skipped => "◊",
        _ => "○"
    };

    private static Rgb StageColour(StageState state) => state switch
    {
        StageState.Running => Theme.Blue,
        StageState.Passed => Theme.Green,
        StageState.Failed => Theme.Red,
        StageState.Skipped => Theme.Muted,
        _ => Theme.Muted
    };

    private static Rgb StatusColour(WorkflowStatus status) => status switch
    {
        WorkflowStatus.Running => Theme.Blue,
        WorkflowStatus.ReadyToLand => Theme.Green,
        WorkflowStatus.Landed => Theme.Green,
        WorkflowStatus.Failed => Theme.Red,
        WorkflowStatus.Cancelled => Theme.Amber,
        _ => Theme.Foreground
    };
}
