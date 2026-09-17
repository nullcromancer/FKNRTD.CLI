using System.Globalization;
using System.Net;
using System.Text;
using FKNRTD.Help;

namespace FKNRTD.Portal;

/// <summary>All variable input to the portal. The caller owns collection snapshots and the clock.</summary>
public sealed record PortalModel(
    IReadOnlyList<GlossaryEntry> Glossary,
    IReadOnlyList<CommandEntry> Commands,
    IReadOnlyList<KeyBinding> Keymap,
    string ProductVersion,
    DateTimeOffset GeneratedAt,
    IReadOnlyList<Milestone>? Milestones = null,
    IReadOnlyList<SettingEntry>? Settings = null);

/// <summary>Renders an offline guide without reading files, launching processes or consulting the clock.</summary>
public static class PortalWriter
{
    public static string Render(PortalModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(model.Glossary);
        ArgumentNullException.ThrowIfNull(model.Commands);
        ArgumentNullException.ThrowIfNull(model.Keymap);

        var html = new StringBuilder(64 * 1024);
        html.Append(Head);
        html.Append("<p class=edition>Version ").Append(H(model.ProductVersion))
            .Append(" &middot; Generated <time datetime=\"")
            .Append(H(model.GeneratedAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)))
            .Append("\">").Append(H(model.GeneratedAt.ToUniversalTime().ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture)))
            .Append("</time></p>");
        html.Append(Introduction);
        RenderPipeline(html);
        RenderCommands(html, model);
        RenderKeymap(html, model.Keymap);
        RenderGlossary(html, model.Glossary);
        RenderSettings(html, model.Settings ?? []);
        RenderMilestones(html, model.Milestones ?? []);
        html.Append(StateAndExitCodes);
        html.Append(Script);
        html.Append("</body></html>");
        // A fixed newline convention makes output identical across operating systems too.
        return html.ToString().Replace("\r\n", "\n", StringComparison.Ordinal);
    }

    private static void RenderCommands(StringBuilder html, PortalModel model)
    {
        html.Append("<section id=commands aria-labelledby=commands-title><h2 id=commands-title>Commands</h2>")
            .Append("<p>Start with initialization and a doctor check. Each command below explains its disk changes and next step.</p>");
        foreach (var group in model.Commands.GroupBy(entry => entry.Group, StringComparer.Ordinal))
        {
            html.Append("<div data-filter-group><h3>").Append(H(group.Key)).Append("</h3>");
            foreach (var entry in group)
            {
                html.Append("<article class=entry data-filter-entry><h4><code>").Append(H(entry.Name))
                    .Append("</code></h4><p class=summary>").Append(H(entry.Summary))
                    .Append("</p><pre><code>").Append(H(entry.Invocation)).Append("</code></pre><p>")
                    .Append(H(entry.Detail)).Append("</p>");
                if (entry.Options.Count > 0)
                {
                    html.Append("<h5>Arguments and options</h5><dl class=options>");
                    foreach (var option in entry.Options)
                    {
                        html.Append("<dt><code>").Append(H(option.Name));
                        if (!string.IsNullOrEmpty(option.ValueHint))
                        {
                            html.Append(' ').Append(H(option.ValueHint));
                        }
                        html.Append("</code>");
                        if (option.Required)
                        {
                            html.Append(" <span class=required>required</span>");
                        }
                        html.Append("</dt><dd>").Append(H(option.Meaning)).Append("</dd>");
                    }
                    html.Append("</dl>");
                }
                if (entry.Examples.Count > 0)
                {
                    html.Append("<h5>Try it</h5>");
                    foreach (var example in entry.Examples)
                    {
                        html.Append("<pre><code>").Append(H(example)).Append("</code></pre>");
                    }
                }
                html.Append("<p class=next><strong>What happens next:</strong> ")
                    .Append(H(entry.WhatHappensNext)).Append("</p>");
                if (entry.GlossaryTerms.Count > 0)
                {
                    html.Append("<p class=related>Related concepts: ");
                    for (var index = 0; index < entry.GlossaryTerms.Count; index++)
                    {
                        if (index > 0) html.Append(" &middot; ");
                        var term = entry.GlossaryTerms[index];
                        var target = model.Glossary.FirstOrDefault(item => item.Term.Equals(term, StringComparison.OrdinalIgnoreCase));
                        if (target is null)
                        {
                            html.Append(H(term));
                        }
                        else
                        {
                            html.Append("<a href=\"#").Append(H(TermId(target.Term))).Append("\">")
                                .Append(H(target.Title)).Append("</a>");
                        }
                    }
                    html.Append("</p>");
                }
                html.Append("</article>");
            }
            html.Append("</div>");
        }
        html.Append("</section>");
    }

    private static void RenderKeymap(StringBuilder html, IReadOnlyList<KeyBinding> keymap)
    {
        html.Append("<section id=keymap aria-labelledby=keymap-title><h2 id=keymap-title>Dashboard keymap</h2>");
        if (keymap.Count == 0)
        {
            html.Append("<p>No dashboard shortcuts were supplied for this edition.</p>");
        }
        else
        {
            html.Append("<dl class=keymap>");
            foreach (var binding in keymap)
            {
                html.Append("<dt><kbd>").Append(H(binding.Key)).Append("</kbd></dt><dd>")
                    .Append("<strong>").Append(H(binding.Action)).Append("</strong> — ")
                    .Append(H(binding.Detail)).Append("</dd>");
            }
            html.Append("</dl>");
        }
        html.Append("</section>");
    }

    private static void RenderGlossary(StringBuilder html, IReadOnlyList<GlossaryEntry> glossary)
    {
        html.Append("<section id=glossary aria-labelledby=glossary-title><h2 id=glossary-title>Glossary</h2>");
        foreach (var group in glossary.GroupBy(entry => entry.Category, StringComparer.Ordinal))
        {
            html.Append("<div data-filter-group><h3>").Append(H(group.Key)).Append("</h3>");
            foreach (var entry in group)
            {
                html.Append("<article class=entry data-filter-entry id=\"").Append(H(TermId(entry.Term)))
                    .Append("\"><h4>").Append(H(entry.Title)).Append("</h4><p class=term><code>")
                    .Append(H(entry.Term)).Append("</code></p><p class=summary>").Append(H(entry.Summary))
                    .Append("</p><p>").Append(H(entry.Detail)).Append("</p>");
                if (!string.IsNullOrEmpty(entry.Example))
                {
                    html.Append("<pre><code>").Append(H(entry.Example)).Append("</code></pre>");
                }
                html.Append("</article>");
            }
            html.Append("</div>");
        }
        html.Append("</section>");
    }

    private static void RenderPipeline(StringBuilder html)
    {
        html.Append("""
            <section id=pipeline aria-labelledby=pipeline-title>
            <h2 id=pipeline-title>Eight stages. One deliberate hand-off.</h2>
            <p>Follow the arrows from your brief to your decision. Roles are assignments: the configured agent and its stage profile determine how each runs.</p>
            <div class=pipeline-layout><figure>
            <svg class=pipeline viewBox="0 0 400 1056" role=img aria-labelledby="flow-title flow-description" xmlns="http://www.w3.org/2000/svg">
            <title id=flow-title>The eight-stage FKNRTD.CLI pipeline</title>
            <desc id=flow-description>Brief, Worktree, Plan by the read-only lead, Implement by the implementer with write access, Verify using your commands, Audit by the read-only auditor, Ready to land awaiting human confirmation, then Land. Type LAND before stage eight. Only the Implement agent stage is granted write access.</desc>
            <defs><marker id=arrow viewBox="0 0 10 10" refX=9 refY=5 markerWidth=6 markerHeight=6 orient=auto-start-reverse><path d="M 0 0 L 10 5 L 0 10 z"/></marker></defs>
            """);
        (string Title, string Owner, string Note, string Style)[] stages =
        [
            ("1 / Brief", "You define the task", "Goal, roles and verification", ""),
            ("2 / Worktree", "FKNRTD.CLI prepares isolation", "Git checkout; skipped in standalone", ""),
            ("3 / Plan", "LEAD / read-only", "Reads the brief and proposes a plan", "read"),
            ("4 / Implement", "IMPLEMENTER / write access", "The only agent stage allowed to edit", "write"),
            ("5 / Verify", "Your build and test commands", "Every command must exit 0", ""),
            ("6 / Audit", "AUDITOR / read-only", "An explicit PASS is required", "read"),
            ("7 / Ready to land", "YOU / inspect the finished diff", "Nothing merges while you review", "human"),
            ("8 / Land", "FKNRTD.CLI follows your confirmation", "Merge, or record standalone completion", "human")
        ];
        for (var index = 0; index < stages.Length; index++)
        {
            var stage = stages[index];
            var y = 8 + index * 132;
            if (index > 0)
            {
                html.Append("<path class=connector d=\"M 200 ").Append(H((y - 34).ToString(CultureInfo.InvariantCulture)))
                    .Append(" V ").Append(H((y - 4).ToString(CultureInfo.InvariantCulture))).Append("\" marker-end=\"url(#arrow)\"/>");
            }
            html.Append("<g class=\"").Append(H(stage.Style)).Append("\" transform=\"translate(8 ")
                .Append(H(y.ToString(CultureInfo.InvariantCulture))).Append(")\"><rect width=384 height=98 rx=\"12\"/>")
                .Append("<text x=16 y=27 class=stage-title>").Append(H(stage.Title)).Append("</text>")
                .Append("<text x=16 y=53>").Append(H(stage.Owner)).Append("</text>")
                .Append("<text x=16 y=78 class=stage-note>").Append(H(stage.Note)).Append("</text></g>");
            if (index == 6)
            {
                html.Append("<text x=217 y=922 class=confirmation>Type LAND</text>");
            }
        }
        html.Append("""
            </svg><figcaption>Only Implement gives an agent write access. FKNRTD.CLI also writes task records and Git state; verification commands may create build outputs.</figcaption>
            </figure><div class=pipeline-notes>
            <h3>Three jobs, separate permissions</h3>
            <p><strong>Lead:</strong> understand the brief and plan the work. The plan profile should be read-only.</p>
            <p><strong>Implementer:</strong> make the changes. In Git mode, this happens in a task worktree.</p>
            <p><strong>Auditor:</strong> review the result against the brief with a read-only audit profile. Missing or failing verdicts block landing.</p>
            <h3>Failure has a next step</h3>
            <p>Failed verification or audit can send work back for repair within the task's repair budget. Once that budget is exhausted, read the stage log before retrying.</p>
            <p>With no verification commands configured, Verify is skipped. Supply a real build and test command so that a ready task carries useful evidence.</p>
            <h3>The human gate</h3>
            <p>Ready to land is a stopping point. Inspect the diff, then explicitly confirm <code>LAND</code>. Passing tests and a PASS verdict do not trigger a merge on their own.</p>
            <p class=callout><strong>Standalone mode:</strong> agents edit the workspace directly. Landing records completion; it does not delay or undo those edits.</p>
            </div></div></section>
            """);
    }

    /// <summary>
    /// The configuration file, explained. It is plain JSON meant to be edited by hand, so the
    /// consequence of moving each value matters more than the value itself.
    /// </summary>
    private static void RenderSettings(StringBuilder html, IReadOnlyList<SettingEntry> settings)
    {
        if (settings.Count == 0)
        {
            return;
        }

        html.Append("<section id=settings aria-labelledby=settings-title>")
            .Append("<h2 id=settings-title>Configuration</h2>")
            .Append("<p>Everything about a workspace lives in <code>.fknrtd/config.json</code>. ")
            .Append("Validate it after editing with <code>fknrtd config validate</code>.</p>");
        foreach (var group in settings.GroupBy(entry => entry.Section, StringComparer.Ordinal))
        {
            html.Append("<div data-filter-group><h3>").Append(H(group.Key)).Append("</h3>");
            foreach (var entry in group)
            {
                html.Append("<article class=entry data-filter-entry><h4><code>").Append(H(entry.Key))
                    .Append("</code></h4><p class=summary>").Append(H(entry.Summary))
                    .Append("</p><p class=term>Default: <code>").Append(H(entry.Default))
                    .Append("</code></p><p>").Append(H(entry.Detail))
                    .Append("</p><p class=next><strong>If you change it:</strong> ")
                    .Append(H(entry.IfYouChangeIt)).Append("</p>");
                if (entry.GlossaryTerm is not null)
                {
                    html.Append("<p class=related><a href=\"#").Append(H(TermId(entry.GlossaryTerm)))
                        .Append("\">").Append(H(entry.GlossaryTerm)).Append("</a></p>");
                }

                html.Append("</article>");
            }

            html.Append("</div>");
        }

        html.Append("</section>");
    }

    /// <summary>
    /// How the product came to work this way. It is here rather than in a changelog because the
    /// parts only make sense together, and a record of what each piece was for shows that where a
    /// list of features would not.
    /// </summary>
    private static void RenderMilestones(StringBuilder html, IReadOnlyList<Milestone> milestones)
    {
        if (milestones.Count == 0)
        {
            return;
        }

        html.Append("<section id=built aria-labelledby=built-title><h2 id=built-title>How this was built</h2>")
            .Append("<p>Each step below names the thing that was wrong, what was done about it, and ")
            .Append("why — including what was deliberately left alone.</p>");
        foreach (var milestone in milestones)
        {
            html.Append("<article class=entry><h3>").Append(H(milestone.Title))
                .Append("</h3><p class=term><time datetime=\"").Append(H(milestone.Date)).Append("\">")
                .Append(H(milestone.Date)).Append("</time></p>")
                .Append("<h5>The problem</h5><p>").Append(H(milestone.Problem))
                .Append("</p><h5>What changed</h5><p>").Append(H(milestone.Change))
                .Append("</p><h5>Why</h5><p class=next>").Append(H(milestone.Why)).Append("</p></article>");
        }

        html.Append("</section>");
    }

    private static string H(string? value) => WebUtility.HtmlEncode(value) ?? string.Empty;

    // UTF-8 hex gives arbitrary caller-provided terms unique, attribute-safe fragment identifiers.
    private static string TermId(string term) => "term-" + Convert.ToHexString(Encoding.UTF8.GetBytes(term));

    private const string Head = """
        <!doctype html>
        <html lang=en><head><meta charset=utf-8><meta name=viewport content="width=device-width, initial-scale=1">
        <meta name=color-scheme content="dark light"><title>FKNRTD.CLI — Operator guide</title>
        <style>
        :root{color-scheme:dark;--bg:#10151d;--panel:#18212c;--text:#edf2f7;--muted:#b3c0d0;--border:#405063;--accent:#81dfcb;--code:#0c1118;--write:#ffc580;--human:#cbb5ff;--focus:#ffe49a}
        @media(prefers-color-scheme:light){:root{color-scheme:light;--bg:#f5f7fa;--panel:#fff;--text:#182331;--muted:#48596c;--border:#b4c1cf;--accent:#006c5c;--code:#eaf0f5;--write:#814000;--human:#6740a5;--focus:#784800}}
        *{box-sizing:border-box}html{scroll-padding-top:2rem}body{margin:0;background:var(--bg);color:var(--text);font:1rem/1.65 system-ui,sans-serif;overflow-wrap:anywhere}
        a{color:var(--accent);text-underline-offset:.2em}a:hover{text-decoration-thickness:2px}:focus-visible{outline:3px solid var(--focus);outline-offset:4px}
        .shell{display:grid;grid-template-columns:15rem minmax(0,1fr);max-width:1440px;margin:auto;gap:3rem;padding:2rem}
        aside{position:sticky;top:1.5rem;align-self:start;max-height:calc(100vh - 3rem);overflow-y:auto;padding:.25rem}nav a{display:block;padding:.3rem 0}nav{margin:1rem 0}
        main,section,article,figure{min-width:0}main{max-width:68rem}section{margin:0 0 4rem;scroll-margin-top:1rem}h1,h2,h3,h4,h5{line-height:1.2;text-wrap:balance}h1{font-size:clamp(2.1rem,5vw,4.2rem);letter-spacing:-.04em;margin:.3rem 0 1rem}h2{font-size:1.85rem}h3{font-size:1.3rem;margin-top:2rem}h4{font-size:1.15rem;margin:0 0 .6rem}h5{font-size:1rem;margin:1.4rem 0 .5rem}
        .brand{font-weight:800;letter-spacing:.04em}.eyebrow,.edition,.term,.related,figcaption{color:var(--muted);font-size:.88rem}.eyebrow{text-transform:uppercase;letter-spacing:.12em}.intro{font-size:1.2rem;max-width:50rem}
        .entry{padding:1.5rem;margin:1rem 0;border:1px solid var(--border);border-radius:12px;background:var(--panel)}.summary{font-weight:600}.next,.callout{border-left:3px solid var(--accent);padding:.7rem 1rem;background:var(--code)}
        pre,code,kbd{font-family:ui-monospace,Consolas,monospace;font-size:.92em}pre{white-space:pre-wrap;overflow-wrap:anywhere;word-break:break-word;background:var(--code);border:1px solid var(--border);padding:1rem;border-radius:8px}kbd{padding:.2rem .45rem;border:1px solid var(--border);border-radius:4px;background:var(--code)}
        dl{margin:1rem 0}dt{font-weight:650}dd{margin:.3rem 0 1rem;color:var(--muted)}.required{color:var(--write);font-size:.8rem}.keymap{display:grid;grid-template-columns:minmax(4rem,1fr) minmax(0,3fr);gap:.8rem 1rem}.keymap dd{margin:0}
        label{display:block;font-weight:600;margin-top:1.5rem}input{width:100%;min-width:0;padding:.7rem;background:var(--code);color:var(--text);border:1px solid var(--border);border-radius:6px;font:inherit}button{margin-top:.6rem;padding:.35rem .65rem;background:var(--panel);color:var(--text);border:1px solid var(--border);border-radius:5px;font:inherit;cursor:pointer}.filter-status{font-size:.85rem;color:var(--muted)}[hidden]{display:none!important}
        .pipeline-layout{display:grid;grid-template-columns:minmax(0,1fr) minmax(0,1fr);gap:2rem}.pipeline-layout figure{margin:0}.pipeline{display:block;width:100%;max-width:400px;height:auto;margin:auto}.pipeline rect{fill:var(--panel);stroke:var(--border)}.pipeline text{fill:var(--text);font:15px system-ui,sans-serif}.pipeline .stage-title{font-size:21px;font-weight:700}.pipeline .stage-note{font-size:14px;fill:var(--muted)}.pipeline .read rect{stroke:var(--accent)}.pipeline .write rect{stroke:var(--write);stroke-width:2}.pipeline .human rect{stroke:var(--human)}.pipeline .confirmation{font-size:14px;font-weight:700;fill:var(--human)}.connector{stroke:var(--muted);stroke-width:2;fill:none}marker path{fill:var(--muted)}
        .skip{position:absolute;left:1rem;top:-5rem;z-index:5;background:var(--panel);padding:.5rem}.skip:focus{top:1rem}footer{color:var(--muted);border-top:1px solid var(--border);padding-top:1rem}
        @media(max-width:850px){.shell{display:block;padding:1rem}aside{position:sticky;top:0;z-index:2;max-height:40vh;background:var(--bg);padding:.6rem 0;border-bottom:1px solid var(--border)}nav{display:flex;flex-wrap:wrap;gap:.15rem 1rem;margin:.4rem 0}nav a{font-size:.85rem;padding:0}aside label{margin-top:.4rem}.filter-status{margin:.3rem 0}main{padding-top:1.5rem}html{scroll-padding-top:42vh}section,article{scroll-margin-top:42vh}.entry{padding:1rem}}
        @media(max-width:550px){.pipeline-layout{grid-template-columns:minmax(0,1fr)}h2{font-size:1.55rem}.pipeline-notes h3:first-child{margin-top:0}}
        @media print{aside,.skip{display:none}.shell{display:block;padding:0}body{background:white;color:black}.entry{break-inside:avoid}section{margin-bottom:2rem}}
        </style></head><body><a class=skip href=#main>Skip to guide</a><div class=shell>
        <aside aria-label="Guide navigation"><a class=brand href=#overview>FKNRTD.CLI</a>
        <nav aria-label=Sections><a href=#overview>Start here</a><a href=#pipeline>Eight-stage pipeline</a><a href=#commands>Commands</a><a href=#keymap>Dashboard keys</a><a href=#glossary>Glossary</a><a href=#settings>Configuration</a><a href=#built>How this was built</a><a href=#state>Files on disk</a><a href=#exit-codes>Exit codes</a></nav>
        <div id=filter-controls hidden><label for=filter>Find a command or concept</label><input id=filter type=search placeholder="Try brief, audit, task…" autocomplete=off aria-controls="commands glossary"><button id=clear-filter type=button>Clear filter</button><p id=filter-status class=filter-status role=status aria-live=polite></p></div>
        <noscript><p>All entries are shown. Use your browser's Find command to search.</p></noscript></aside>
        <main id=main><section id=overview aria-labelledby=overview-title><p class=eyebrow>Operator guide / offline edition</p><h1 id=overview-title>Your agents.<br>Your final say.</h1>
        """;

    private const string Introduction = """
        <p class=intro>FKNRTD.CLI coordinates the coding CLIs you already use. You write a brief; a lead plans, an implementer changes the code, your commands verify it, and an auditor reviews it. You decide when it lands.</p>
        <h2>Know what you are authorizing</h2>
        <p>In Git mode, each task works on its own branch in a separate worktree. The default lead and auditor profiles are read-only; the implementer profile permits edits. Configured verification commands must pass, and the auditor must give an explicit PASS verdict, before the task can become ready to land. Merging still needs your confirmation.</p>
        <p>These controls depend on the agent profiles and verification commands you configure. Review them before running an unfamiliar CLI. A worktree isolates a checkout, not the whole machine, and your build commands run with your permissions.</p>
        <p class=callout>Without Git, standalone mode edits the folder in place. Use a recoverable copy before delegating edits. The final landing step records completion; it cannot provide branch isolation.</p>
        <p>First visit? Run <code>fknrtd init</code>, then <code>fknrtd doctor</code>. Write a small, specific task with a verification command you trust.</p></section>
        """;

    private const string StateAndExitCodes = """
        <section id=state aria-labelledby=state-title><h2 id=state-title>Where state lives</h2>
        <p>Workspace records live under <code>&lt;root&gt;/.fknrtd/</code>. Git and the agent CLIs remain the source of truth for their own state.</p>
        <dl><dt><code>config.json</code></dt><dd>Workspace mode, agent executables and profiles, defaults and timeouts.</dd>
        <dt><code>tasks/</code></dt><dd>Durable task records, stage results and errors.</dd>
        <dt><code>logs/&lt;task-id&gt;/</code></dt><dd>Stage output, including repair attempts. Start here when a run fails.</dd>
        <dt><code>artifacts/&lt;task-id&gt;/</code></dt><dd>Task artifacts such as the plan.</dd>
        <dt><code>worktrees/</code></dt><dd>Task checkouts in Git mode. Cleanup removes a checkout while retaining its branch and task history.</dd>
        <dt><code>runtime/agents/</code> and <code>runtime/usage/</code></dt><dd>Agent reports and usage snapshots.</dd>
        <dt><code>runtime/claims/</code>, <code>runtime/locks/</code>, <code>runtime/cancels/</code></dt><dd>File claims, execution leases and cancellation requests.</dd>
        <dt><code>runtime/events.jsonl</code> and <code>runtime/messages.jsonl</code></dt><dd>Event history and agent hand-off messages.</dd></dl>
        <p>Commit <code>config.json</code> and <code>.gitignore</code>: the configuration is a project decision worth sharing. Everything else is this machine&rsquo;s own state and is already excluded by that ignore file, which is why Git shows <code>.fknrtd</code> as untracked but offers you only those two.</p><p>Deleting this directory is not an undo operation: it can discard logs and unlanded worktrees. Standalone edits, landed Git changes and settings written by integrations also survive removal of workspace records.</p></section>
        <section id=exit-codes aria-labelledby=exit-title><h2 id=exit-title>Exit codes</h2><p>When scripting commands, check the exit code as well as the output.</p>
        <dl><dt><code>0</code> — Success</dt><dd>The requested operation completed. A successful run may be ready to land; it has not merged itself.</dd>
        <dt><code>1</code> — Command or configuration error</dt><dd>An operation raised an error. Read the diagnostic; invalid configuration, missing files or rejected confirmation can cause this result.</dd>
        <dt><code>2</code> — Command or required-check error</dt><dd>An unknown command or failed required doctor check. Read the diagnostic and correct the invocation or configuration.</dd>
        <dt><code>3</code> — Failed or collided outcome</dt><dd>A task outcome failed or a collision was detected. Read task logs or conflict details before proceeding.</dd>
        <dt><code>130</code> — Cancelled</dt><dd>Execution was cancelled. Inspect the task and its log; completed edits are not automatically rolled back.</dd></dl></section>
        <footer>FKNRTD.CLI &middot; A guide you can keep beside your terminal. No network required.</footer></main></div>
        """;

    private const string Script = """
        <script>
        (() => {
          const input = document.getElementById('filter');
          const entries = Array.from(document.querySelectorAll('[data-filter-entry]'));
          const groups = Array.from(document.querySelectorAll('[data-filter-group]'));
          const searchable = entries.map(entry => ({ entry, text: (entry.textContent + ' ' + entry.parentElement.querySelector('h3').textContent).toLowerCase() }));
          const status = document.getElementById('filter-status');
          function filter() {
            const words = input.value.trim().toLowerCase().split(/\s+/).filter(Boolean);
            let visible = 0;
            searchable.forEach(({ entry, text }) => {
              entry.hidden = !words.every(word => text.includes(word));
              if (!entry.hidden) visible++;
            });
            groups.forEach(group => { group.hidden = !Array.from(group.querySelectorAll('[data-filter-entry]')).some(entry => !entry.hidden); });
            status.textContent = visible === 0 ? 'No matches. Try another word or clear the filter.' : `${visible} of ${entries.length} entries shown`;
          }
          function revealFragment() {
            const target = document.getElementById(location.hash.slice(1));
            if (target && target.matches('[data-filter-entry]') && (target.hidden || target.parentElement.hidden)) {
              input.value = '';
              filter();
              target.scrollIntoView();
            }
          }
          document.getElementById('filter-controls').hidden = false;
          input.addEventListener('input', filter);
          document.getElementById('clear-filter').addEventListener('click', () => { input.value = ''; filter(); input.focus(); });
          document.addEventListener('click', event => {
            const link = event.target.closest('a[href^="#term-"]');
            if (link) { input.value = ''; filter(); }
          });
          window.addEventListener('hashchange', revealFragment);
          filter();
          revealFragment();
        })();
        </script>
        """;
}
