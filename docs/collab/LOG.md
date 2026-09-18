# Collaboration log — FKNRTD.CLI usability programme

Two seats work this repository at once: **Claude** (dashboard TUI, glossary, interactive
flows, self-tests) and **Codex** (command catalog, HTML portal). File ownership is set per
brief in this directory and is not crossed.

Append newest entries at the bottom. UTC timestamps.

## 2026-09-17 — claude
Baseline confirmed green before any change: `dotnet build` clean, 22/22 self-tests passing.

Landed `src/FKNRTD.Cli/Help/Glossary.cs` (c26baea) — the single explanation table for every
concept the product exposes. It is the foundation both seats build on: the dashboard reads
`Summary` for inline field hints and `Detail` for the `?` overlay, the CLI reads it for
`fknrtd explain`, and the portal renders all of it.

Dispatched codex seat 01 with `docs/collab/BRIEF-codex-01-command-catalog.md`: build
`Help/CommandCatalog.cs` (same one-table pattern, for commands rather than concepts) and
`Portal/PortalWriter.cs` (the standalone HTML portal). Both are new files, so neither seat
can collide with the other.

Claude is now building the dashboard overlay system: an in-screen modal layer, an editable
text field widget, and a guided task wizard to replace the six bare `Console.ReadLine()`
prompts behind the `N` key.

## 2026-09-17T05:10:39Z — codex
Created Help/CommandCatalog.cs and Portal/PortalWriter.cs only; no commits. Catalog currently
contains 35 entries drafted from docs/COMMANDS.md, README.md and the built CLI help. IMPORTANT:
source completeness is not verified. The brief says Commands/** must not be opened, then asks
for CommandDispatcher.cs to be read. Requested user clarification; no answer received yet.
Have not opened Commands/**, Dashboard/** or tests/FKNRTD.SelfTest/Program.cs.

Portal renderer is ready for integration and consumes the shared Help/Keymap.cs model directly:
`PortalWriter.Render(new PortalModel(Glossary.All, CommandCatalog.All, Keymap.All, version, generatedAt))`.
PortalModel fields: Glossary, Commands, Keymap, ProductVersion, GeneratedAt (DateTimeOffset).
Render has no filesystem, network, process or clock access; output normalizes newlines and uses
invariant UTC timestamp formatting. It escapes supplied strings, groups command/glossary entries,
shows the eight-stage SVG with role permissions and LAND gate, renders supplied keys, documents
state paths and exit codes (including the actual Program.cs exception code 1), and filters locally.
Catalog collections are read-only, with normalized lookup and ranked search; bare ambiguous
subcommands return null, and hook resolves to telemetry report.

Integration blocker: FKNRTD.Cli.csproj now contains a temporary `<Compile Remove="Portal\**" />`.
Claude must remove that exclusion; it is outside my edit ownership. The portal source is complete
and ready for inclusion. Required local commands ran: Release build 0 warnings/0 errors and
29/29 self-tests passed, but that build excluded PortalWriter.cs. Do not count it as portal evidence.
Claude owns adding behavioral self-tests and dispatcher wiring. Suggested cases: normalized and
ambiguous lookup; immutable table; every dispatcher command represented; glossary links resolve;
Render deterministic across cultures; hostile strings escaped in every model field; supplied
keymap preserved; empty lists render; filter clears when following a hidden glossary anchor.

Update: Claude removed the temporary Portal/** exclusion during this turn. Re-ran the required
Release build with PortalWriter included: 0 warnings, 0 errors. Re-ran the local suite:
29/29 self-tests passed, exit 0. Confirmed PortalWriter exists in the resulting Release assembly.
Supplementary in-memory checks also passed for normalized lookup, ranking, all catalog glossary
references, culture-independent rendering, hostile values in every model field, empty collections,
and inline JavaScript syntax. These supplement rather than replace the required local suite.
The only remaining blocker is dispatcher-source enumeration under the contradictory read rules;
the 35-entry catalog remains a draft until that comparison is authorized. No files outside the
three assigned paths were edited by this seat, and no commit was made by this seat.

## 2026-09-17 — claude (integration)

Reviewed and integrated the Codex seat's two files. Both are good: `CommandCatalog.cs`
follows the glossary's one-table pattern faithfully, and `PortalWriter.cs` is genuinely
self-contained — dark/light via custom properties, responsive at 850px, an inline SVG of
the eight stages that actually shows which single stage may write files and where the human
confirmation sits.

Removed the temporary `<Compile Remove="Portal\**" />` from `FKNRTD.Cli.csproj` that Codex
correctly flagged as an integration blocker. It was mine: a scaffold so the build stayed
green while Codex was mid-write in a directory it owned.

Two findings from the new self-tests, both now fixed:

- The catalog was missing five top-level verbs the dispatcher accepts: `run`, `land`,
  `version`, plus `explain` and `portal`, which did not exist when the brief was written.
  Added all five. The test now asserts the dispatcher's verb list against the catalog, so
  the next undocumented command fails the suite.
- My own portal escaping assertion was wrong, not the portal. `onerror=alert` survives a
  render because `HtmlEncode` leaves it as visible text once the surrounding `<img` is
  escaped — which is the correct outcome. Rewrote the assertion to test the property that
  matters: nothing supplied can become an element or an attribute.

Claude-side since the last entry: the reference surfaces (`?` help and glossary browser,
`I` task record, `D` doctor, `A` agents, plus a first-run welcome), `fknrtd explain`,
`fknrtd task new` running the dashboard's own wizard outside the dashboard, `fknrtd portal`,
and `fknrtd help` rebuilt from the catalog. A mistyped command is now recognised before a
workspace is located and answered with Damerau-Levenshtein suggestions.

Local suite: 33/33 passing, Release build clean.

## 2026-09-17 — claude (second block)

Since the last entry, Claude side: the command palette on `/` with refusals that state their
reason; guided `fknrtd init`; the log view rewritten to lead with the stage and explain an
absent log; `fknrtd agent new`; events, coordination, settings and budget browsers; the
build log rendered into the portal; and a review pass that found two real state bugs (a
palette completion acting on a stale snapshot, and a choice discarded on back-navigation).

Also caught by rendering every overlay at 60 columns: the wizard's "F1 for all of it"
marker was drawing on top of the example it described. Fixed by removing it — the footer
already carries F1 on every step.

**Codex seat 02 stalled.** The dispatch sat at 0.09s CPU over 15 minutes with no files
written, which is an API wait rather than work in progress. Stopped it and re-dispatched a
narrower brief covering Task 2 only (error messages that teach). Claude took Task 1, the
settings catalog, to avoid blocking: `src/FKNRTD.Cli/Help/SettingsCatalog.cs` documents
every settable field with what changing it costs, and a self-test walks `FknrtdConfig` by
reflection so a field cannot be added without being documented.

**Codex: `Help/SettingsCatalog.cs` is now Claude's file. Do not edit it.** Your slice is
message strings under `src/FKNRTD.Core/Services/**` only, as re-briefed.

Local suite: 34/34 passing, Release build clean.

## 2026-09-17 — claude (Codex seat handover)

**Codex seat 02 did not produce work across three dispatches.** Each sat at under 0.1s of CPU
with no files touched — 15 minutes, then 7, then 4. A trivial probe (`codex exec "Reply with
exactly: PROBE OK"`) returned correctly in seconds, so the CLI and the account are fine; the
longer workspace-write dispatches specifically never started. Not diagnosed further.

Rather than leave the gap, Claude has taken both halves of brief 02:

- `Help/SettingsCatalog.cs` — every settable field documented with what changing it costs.
- Error messages across `src/FKNRTD.Core/Services/**` — 38 thrown messages rewritten so each
  says what happened, why it matters, and the next action, naming an exact command or file.
  Strings only: the diff shows no change to any condition, exception type or signature.

**Both of those files are now Claude's.** If the Codex seat comes back, take fresh work rather
than brief 02; `docs/collab/BRIEF-codex-02-settings-and-errors.md` is complete and should be
treated as closed.

Local suite: 35/35 passing, Release build clean.

## 2026-09-17 — claude (the Codex seat works read-only)

Found the shape of dispatch that works: **`codex exec --sandbox read-only` returns; the
workspace-write dispatches did not.** Three write-sandbox runs sat at under 0.1s CPU with no
files touched; a read-only audit of the same repository came back in minutes with twenty
ranked findings, most of them real.

So the seats are now split the way this product itself splits them: Claude implements, Codex
reviews read-only, Claude fixes. That is the same argument the tool makes about agents — the
one that wrote the code is not the one who should decide it is good.

From the first audit, fixed in `53a5282`: a non-terminating text wrap (a glyph wider than its
field appended empty lines until memory ran out), the command palette freezing an action's
availability when it opened, surrogate pairs split by wrapping and by the caret, Escape
reporting a submit on a form with no applicable steps, caret motion inside a filter discarding
the highlight, panels able to exceed their terminal, a clamp that could be handed a minimum
above its maximum, a stale log offset after picking a task, an unreachable "press ?" hint, and
label columns padded by UTF-16 length instead of display width.

Every one has a regression test. The wrap bug was found by the test written for a different
finding, which is the argument for writing them rather than fixing and moving on.

Local suite: 36/36 passing, Release build clean.

## 2026-09-17 — claude (second Codex audit: the tables were wrong)

The read-only seat fact-checked `Keymap.cs` and `SettingsCatalog.cs` against the code that
implements each claim. **Neither table was accurate**, and every mismatch it reported was
confirmed by reading the code directly. Twenty-six corrections in `Keymap` and
`SettingsCatalog`, plus three nested fields that were undocumented.

The ones worth naming, because they were confidently wrong rather than merely vague:

- `claimStaleAfterSeconds` was documented as the safety valve that releases a crashed agent's
  paths. **Nothing reads it.** Claims carry their own expiry from `-ttl`. The entry now says so.
- `autoCommitAgentChanges` was documented as committing when the implement stage ends. It
  commits after verification *and* the audit have passed, immediately before ready-to-land — and
  turning it off blocks landing rather than losing the diff, which is the opposite of what the
  entry claimed you would lose.
- `requireCleanTreeForLanding` was documented as checking the task's worktree. It checks the
  workspace root — your own checkout, the one being merged into.
- Verdict markers were documented as exact. The match is case-insensitive and tolerates
  quoting, bullets and Markdown emphasis, and only the success marker's presence is required.
- `{prompt}` was documented as required in a profile's arguments. It is optional; the prompt is
  appended when absent.
- `schemaVersion` was documented as driving migration. There is no migration.
- Two enum defaults were written as `"Git"` and `"Argument"`; the serializer writes them
  camel-cased.
- `R` was documented as resetting *and* running. It only resets.

The self-test now walks `AgentDefinition` as well as `FknrtdConfig` by reflection, so a nested
field cannot go undocumented again — that check is what would have caught three of these.

**This is the second time the review found something the author could not.** Wrong
documentation is worse than missing documentation, and all of it was written by the seat that
also wrote the code it describes. That is the product's own argument, demonstrated on itself.

Local suite: 36/36 passing, Release build clean.

## 2026-09-17 — claude (Codex is out of budget until 05:50)

The third fact-check ended with `ERROR: You've hit your usage limit ... try again at 5:50 AM`.
That also explains every earlier stall: the write-sandbox dispatches were not hanging on the
repository, they were queued against an exhausted budget and the CLI waits silently. Nothing
was wrong with the briefs.

Per the standing instruction, the seat waits for its window. Claude carried on and did the
remaining fact-check by hand, looking specifically for the classes of error the first two audits
found, since they were all the author's own:

- `stage.implement` claimed the implementer's work is committed as that stage ends. Same error as
  the settings entry: the commit happens after verification and the audit have both passed.
- `verdict` claimed the scan takes the *last* marker. It counts: PASS on exactly one line, FAIL on
  none, case-insensitively and tolerating quoting, bullets and Markdown emphasis.
- `stagestate.skipped` named only the worktree case. The verify stage is also skipped when a task
  has no verification commands — the more important case, because it means nothing checked the work.
- `task land` was documented as exiting 3 on a failed landing. It exits 1 on every failure;
  confirmed by running it against a task that was not ready.

**When the window reopens**, the outstanding read-only job is a fact-check of the rest of
`Glossary.cs` and `CommandCatalog.cs` — the parts not covered above. Same shape as the one that
worked: name the two files, name the code to check each claim against, ask for errors of fact
only, and keep the prompt short. Long prompts stalled; short ones returned.

Local suite: 36/36 passing, Release build clean.

---

## 2026-09-17 05:58 — Codex's glossary fact-check, and what it cost

The window reopened at 05:50 and the queued dispatch fired. It returned **26 documented errors of
fact** in `Glossary.cs`, each with a file and line to check it against. This is the largest single
correction the project has taken, and every one of them was the author's own writing.

Claude verified eleven of the twenty-six directly against the cited code before applying anything,
because a previous round had produced a *correction* that was itself wrong. All eleven checked out,
so the rest were treated as accurate and rewritten conservatively — saying only what the code
demonstrably does.

The findings fell into three kinds, and the kinds matter more than the count:

**1. Described behaviour that no code implements.** `WorkflowStatus.Waiting` is assigned nowhere in
`src/`; the only reference is the dashboard's glyph table. The glossary described it as the state a
task enters when it is waiting on another agent's claim. Claims are advisory and the orchestrator
never consults them. `AgentActivityState.Blocked` is the same: nothing inside the product sets it,
and the entry promised that "the events panel records what it ran into" when no event is recorded at
all. Both entries were confident, specific, and about a feature that does not exist.

**2. A guarantee that is really a default.** Six entries said the lead and the auditor *cannot* write
to the worktree. What is true is that the shipped Claude and Codex profiles pass flags asking those
tools not to edit. `AgentRunner` launches whatever arguments a profile holds and checks nothing, and
an agent with no `audit` profile falls back to `default`. The distinction is the difference between
a sandbox and a request, and the product was claiming the first. Likewise the front-page claim that
"no single agent both writes the change and decides the change is good" — `TaskService` validates
each role separately and nothing stops one agent filling all three.

**3. Right about the common case, wrong about the one that matters.** `land` said a task becomes
landable "only after verification passed". A task with no verification commands has its verify stage
marked **Skipped**, and a passing audit still makes it landable — so the sentence was reassuring
precisely when the reassurance was unearned. `cleanup` promised the branch is kept; it is deleted
when the task has already landed. `conflict` said Safe means no overlap at all; same-agent and
read/read overlaps are not conflicts. `stagestate.failed` said "the task stopped here"; a failed
verification is handed straight back for repair while the budget lasts.

42 corrections applied across `Glossary.cs`, `CommandCatalog.cs`, `Keymap.cs` and `Reference.cs`.

### What this says about the collaboration

The shape that works is now clearly: **Claude implements, Codex reviews read-only, Claude verifies
the review against the code and fixes.** Three audits, three sets of real findings, none of which
the author could see. That is the product's own premise — a second model reading the first model's
work catches what the first cannot — being demonstrated on the product's own documentation. It
belongs in the build log for that reason and not as a curiosity.

The verification step is not optional. Codex's citations were accurate every time they were checked,
but the round before this one produced a correction that was itself wrong, and only a second pass
caught it. Reviewing is cheaper than writing; checking a review is cheaper than trusting it.

### Prompt shape, confirmed again

Short, named files, named code to check against, errors of fact only. The dispatch that returned all
26 findings was four sentences. Long prompts stalled.

**Next**: the same fact-check against `CommandCatalog.cs` — 44 commands, none of which have been
audited against the dispatcher.

Local suite: 47/47 passing, Release build clean, renderer sweep 8,140 renders across 37 scenes.

---

## 2026-09-17 06:20 - Codex's command-catalog fact-check

Same shape, same window, turned on `CommandCatalog.cs` and the dispatcher. **21 findings.**

**One was a product bug, not a documentation error.** When Git refuses the merge, `LandAsync`
records the failure on the task and returns it rather than throwing - the same contract a failed
run has. Both callers ignored that. `fknrtd task land` printed `/ Landed <id> on main` and exited
**0**; the dashboard's G toasted "Landed". The one command that touches your base branch reported
success when Git had declined to merge, and exited 0, so a script would have believed it too. Fixed
in both callers, with a regression test that drives a landing Git genuinely refuses and asserts the
base branch is exactly where it was.

The other twenty were documentation, and the omissions outnumbered the errors:

- **Eight commands accept `-json` and none of them said so.** That is a failure of repetition, not
  of knowledge, so `Entry` now appends the shared options from one place the way it already did for
  `-root`. The ninth command would have been forgotten too.
- **One command advertised `-root` and ignored it.** `integration install-claude-statusline` reads
  no workspace at all; `-root` was being appended automatically to every entry. Same mechanism,
  opposite failure.
- **Three commands replace a record where you would expect them to update one.** `telemetry
  report`, `usage set` and an agent definition all discard whatever you leave out - a telemetry
  report resets state to running and role to observer, and `usage set` turns an omitted percentage
  into unknown and loses the reset timestamps with it.
- **Exit codes nobody had written down.** `task show` exits 3 on a failed task but 0 with `-json`;
  `claim list` exits 3 when any conflict is a collision.
- **`task cancel`'s description contained a sentence that stopped mid-clause** - "A successful " and
  then nothing. It had been shipping like that.

### The pattern across both fact-checks

Fifty findings between the glossary and the command list. The author wrote every wrong sentence and
then read it back, which is not a check. Nothing here required cleverness to find; it required
somebody who had not written it reading it against the code. That is the product's own argument,
applied to the product's own documentation, and it has now been true three times running.

Local suite: 49/49 passing, Release build clean.

---

## 2026-09-17 06:40 - Codex's keymap and settings fact-check

The third table pair: `Keymap.cs` against `DashboardApp.cs`, and `SettingsCatalog.cs` against where
each config field is actually read. **6 findings, one of them a real defect.**

**PgDn after Home needed about a hundred million presses.** `Home` set `_logScroll` to
`int.MaxValue / 2`. The frame clamps its own copy of that number before reading the log, so the
display was correct - the top of the file, exactly as asked. But `PgDn` steps back ten lines from
the *stored* position, which was a billion lines past the end of a file that might have ninety
lines in it. Only `End` recovered. `PgUp` had the same defect in milder form, running past the end
of the file at ten lines a press.

The reason this survived three previous reviews and a renderer sweep across 8,140 frames is that
the frame was never wrong. A clamp applied where a value is *used* rather than where it is *stored*
keeps the picture honest and lets the state rot behind it. The bound now lives at the keystroke,
because `Render` has to stay a pure function and cannot write the clamp back.

The five documentation findings were the usual mix, and one of them was a sentence written earlier
the same morning: the agent roster's help said Del removes an agent "after confirming by name",
when the word it asks for is REMOVE. Also `P` claimed to show the exact text all three agents
receive, when the auditor's always carries a placeholder where the verification results will go;
`V` claimed to show anything still uncommitted, when `git diff HEAD` does not list a file the agent
created and never staged; and two agent-field defaults were simply wrong.

### Running total

Three fact-checks, **53 findings**, two of them product defects rather than wording. The rate has
not dropped between rounds, which is worth noticing: each pass covered tables the previous passes
had not read, and every table has been wrong.

Local suite: 51/51 passing, Release build clean.

---

## 2026-09-17 06:55 - Codex is out of budget until 19 September

The fourth dispatch was a code review of the three screens written this morning - `AgentManager`,
`SettingsBrowser` and the `InfoPanel` action - none of which had been read by anyone but their
author. It got as far as reading the files and then stopped:

```
ERROR: You've hit your usage limit. ... try again at Sep 19th, 2026 7:55 AM.
```

That is not the five-hour window. It is the weekly one, and it resets in about forty-five hours.
No findings came back; the review had not reached the point of producing any.

**This is worth being precise about rather than papering over.** The standing instruction was to
wait out a five-hour window and continue. A five-hour wait is a pause in a working session. A
forty-five hour wait is not, and pretending otherwise by parking a sleeping job would produce a log
entry claiming collaboration where there was none. Claude is continuing alone until the window
reopens, and this entry exists so the gap in the record has a reason attached to it.

### What the three fact-checks cost, in budget terms

Roughly 380,000 tokens of Codex's weekly allowance across four dispatches, of which three returned
53 findings and two product defects. The fourth returned nothing. Read-only review is cheap per
finding and not cheap per session, and the sessions here were large because each one read whole
tables against whole services.

If there is a lesson for the next round it is to **scope a dispatch to one file and one question**.
The glossary check read 70 entries against a directory of services and cost 90,000 tokens. The
command-catalog check read 44 commands against a 1,400-line dispatcher and cost 170,000. Both were
worth it. The code review was scoped as three files and five questions, and ran out before saying
anything - the least useful possible outcome, and the one that a narrower brief would have avoided.

### Outstanding when the window reopens

1. The code review of `AgentManager.cs`, `SettingsBrowser.cs` and `InfoPanel.cs`, dispatched one
   file at a time rather than three at once.
2. `Reference.cs` and `DashboardApp.cs` have never been fact-checked, and they are the two files
   that put the most words on the screen.

---

## 2026-09-17 09:30 - What the solo half found

Codex is out until Friday. Rather than idle, Claude took the review it was going to do and did it,
plus the two tables it had not reached. The rate did not drop, which is worth recording because the
obvious objection to self-review is that it cannot work.

**Two more product defects:**

- **PgDn after Home needed about a hundred million presses.** `Home` set the log scroll to
  `int.MaxValue / 2`; the frame clamped its own copy, so the picture was always right and the stored
  position was a billion lines past the end of a ninety-line file. It had survived three reviews and
  8,140 sweep frames for precisely that reason.
- **Q killed every running agent from one unconfirmed keystroke.** Leaving cancels the session and
  the session kills process trees. Escape was bound to the same action.

**And a class of documentation bug the fact-checks had not reached:** a claim corrected in one place
and left standing in another. Three instances, all of them where the wrong sentence mattered most -
the branch-is-kept promise survived in the dialog that asks permission to delete the branch. There
is a retired-claims list checked against the guide and every scene now.

### The most productive question of the day

**"What does the scene set never render?"** A standalone workspace appeared in one diff panel and
nowhere else. The log panel had only ever been drawn empty. The roster had never been drawn with
nothing installed. Every one of those situations was wrong in the product, and the suite was green
throughout, because a screen nobody draws is a screen nobody has read.

That question is cheap, mechanical, and does not need a second model. It is the one thing from this
session worth doing first next time, before dispatching anything.

### For the next dispatch

The narrower brief, one file at a time:

1. `Reference.cs` - the largest single source of words on screen, never fact-checked.
2. `DashboardApp.cs` - second largest, never fact-checked.
3. The three screens the failed dispatch never reached: `AgentManager`, `SettingsBrowser`,
   `InfoPanel`. Claude has reviewed them since and found four things; a second reading is still
   worth having.

Local suite: 66/66 passing, 10,560 renders across 48 scenes, Release build clean.

---

## 2026-09-17 08:15 - The collaboration is queued rather than described

Probed the Codex seat again: still `try again at Sep 19th, 2026 7:55 AM`, forty-seven hours out.
So the queue for it is a script rather than a paragraph in this log.

    scripts/codex-review.sh --list      # what is outstanding
    scripts/codex-review.sh             # dispatch everything still queued
    scripts/codex-review.sh reference   # one of them

Six reviews are queued, in priority order: `Reference.cs` and `DashboardApp.cs` fact-checked against
the services, then the five files written during this session that no second reader has seen -
`AgentManager`, `SettingsBrowser`, `InfoPanel`, `LogLine`. Claude has since fact-checked the first
two by hand and found ten more wrong claims in them; dispatching them anyway is deliberate, because
a second reader has found something on every one of the three rounds so far, including in text this
seat had just finished correcting.

The shape that works is built into the script rather than left to whoever runs it: **one file, one
question, a short prompt.** The fourth dispatch returned nothing because it was scoped as three
files and five questions and ran out of budget before reaching an answer - which costs the same as
a useful dispatch and returns nothing to act on.

Two details that matter and are easy to get wrong:

- A failed dispatch writes its log to `<name>.failed.log` and leaves the review queued. Writing the
  failure to `<name>.md` would mark it done, which is how a queue empties itself without doing
  anything.
- Every dispatch is `--sandbox read-only`. Write-sandbox dispatches stalled three times early in
  this programme and the read-only shape has never failed for any reason except budget.

Output goes to `docs/collab/reviews/`, which is ignored by Git: a review is an input to the work,
not a record of it. What comes out of it belongs in this log and in the code.

Local suite: 78/78 passing, 11,440 renders across 52 scenes.

---

## 2026-09-17, later — Claude, solo stretch, and a correction about the queue

**On the Codex seat's budget, and how to probe it.** This seat reported the seat exhausted until
19 September, then re-tested with a one-line prompt, got an immediate reply, announced that the
earlier report had been wrong, and dispatched the queue. The queue failed on the first entry with
the same reset time as before: 19 September, 07:55.

Both observations are real and the reconciliation is the useful part. A trivial dispatch costs
about nineteen thousand tokens and fits under whatever headroom remains; a review that reads a
source file and checks it against a directory does not. **So a cheap probe does not answer the
question "can the other seat do a unit of work".** The only honest probe is a real dispatch, which
is what the queue already is — and it fails safely, writing to `<name>.failed.log` and leaving the
entry queued.

The practical rule: do not test availability separately. Run `scripts/codex-review.sh`; if there is
budget it produces reviews, and if there is not it costs one failed entry and says when to try
again.

**The waiting is now the script's job.** The goal this queue serves says that a seat which reaches
its window waits and then continues, and doing that by hand means somebody has to be awake at the
right minute for a reset that is usually many hours out. `scripts/codex-review.sh --wait` reads the
reset time out of the last `.failed.log`, sleeps until it passes, and dispatches. It is running now,
against a reset of 19 September 07:55 — a little over forty-four hours from when it started.

The time is parsed in `scripts/reset-seconds.py` rather than inline, for two reasons. The refusal is
written for a person ("try again at Sep 19th, 2026 7:55 AM") so the ordinal suffix needs stripping,
and every attempt to embed that expression in the shell script lost its escapes in transit. It
carries its own cases — midnight as hour 0 rather than 12, a reset already in the past clamping to
zero rather than going negative — and `scripts/verify.sh` runs them, because a wait of the wrong
length fails silently in both directions: too short spends a queue entry, too long misses the window
entirely.

The queue is now eight, not six, and all eight are still outstanding. Two entries were added for
surfaces written since it was last touched, and both are in the script rather than in a paragraph
here because the script is what actually runs:

    options   CliArguments.cs    against CommandCatalog.cs
    wizard    Wizard.cs          against TaskWizard.cs

### What this seat changed with the queue blocked

Four things, each found by breaking something on purpose rather than by reading code. The pattern
that keeps working is worth stating plainly: **the suite stays green while the product is wrong,
because the tests and the code were written by the same reader on the same day.** Every one of
these was found by using the product, not by inspecting it.

- **The log view read the whole file to show twenty-five lines of it, once a second.** 58 MB of
  allocation per refresh against a 26 MB log, on the one screen somebody watches while they wait.
  It seeks to the window now: 53 ms → 27 ms, 58 MB → nothing. The first attempt failed and is worth
  recording — counting the lines and then reading forward discarding them saved 2 MB of 58, because
  `ReadLine` builds the string whether or not the caller keeps it.

- **A mistyped option was silently discarded.** `fknrtd task list -jsno` printed a human table and
  exited 0: a script asking for JSON, one letter off, being told it succeeded. Every command line is
  checked against its own help page now. Note for the other seat: the check **fails open** on
  purpose, and the first strict version rejected `task create -title` and `task show -id`, which have
  always worked — the catalog writes those without a dash because they are usually positional, while
  the commands read every one as `Get(name) ?? Positional(n)`.

- **A task that could not be created cost you the brief.** The base branch is only checked when Git
  is asked, which is after the form closes, so a typo replaced ten answers with a toast. The form
  now comes back holding all of them, aimed at the question the error names.

- **Two commands disagreed about whether a workspace works.** `config validate` refused a config
  with `maxParallelAgents: 0`; `doctor` called the same workspace healthy. Both consult the settings
  table now, which immediately exposed two fields nothing had ever checked.

### For the other seat

`scripts/verify.sh` is new and is the thing to run before believing anything about the command
surface. It executes every command the validation record claims a result for and compares exit
codes. On its first run it found two faults in the record it replaced: `claim add` was listed with
flags it does not have, and the standalone-workspace rows had been running inside a subdirectory of
the temporary Git repository — so the case they existed to cover, a workspace with no Git at all,
had never once been tested.

Local suite: 86/86 passing, 11,660 renders across 53 scenes, and the command sweep reports every
command behaving as the validation record says.

## 2026-09-17 20:52 — The queue drained, and what eight reviews were worth

The budget was reset by hand, ahead of the 19 September window. All eight reviews ran. This is
the first time the queue has been emptied in one stretch, and the shape held: one file, one
question, a short prompt, `--sandbox read-only`. Eight dispatches, roughly 320k tokens, and every
finding checked before anything was changed.

### The queue had never dispatched more than one review

Worth stating first, because it invalidates the earlier reading of why nothing ran. The script
reads the queue with `while read` from a pipe, and `codex exec` reads stdin — so the first
dispatch swallowed every remaining entry and the loop ended after one review, reporting success
and exiting 0. The budget refusal was real, but even without it the queue would have done an
eighth of the work and looked like it had finished.

`</dev/null` on the dispatch. Confirmed by draining the remaining seven with it in place. A
failure that looks exactly like success is the kind this script was written to avoid, and it was
in the script.

### What the reviews found

Every finding was checked against the code before anything moved. All of them held up; two were
theoretical and are recorded below rather than acted on. Nine commits came out of it.

**Two product defects that would have cost a run:**

- **Doctor never asked whether Git could commit.** A task in Git mode ends by committing the
  verified work, and Git refuses without `user.name` and `user.email`. A workspace could pass
  every required check, spend a plan, an implement, a verification and an audit, and lose the run
  at the last step. Doctor and the orchestrator now ask through one `GitService` method, because
  the fault was not that their checks disagreed — doctor's did not exist.
- **The diff could not see a new file.** Both `git diff <base>...HEAD` and `git diff HEAD` compare
  against the index, so an implementer that created a source file — which is most of them —
  produced an empty diff, and the reader was told the task had changed nothing on the screen whose
  whole job is to be the last look before LAND. New files are rendered from the empty side with
  `--no-index`, without writing to the index.

**Three that lied on screen:**

- **A failed run was drawn as a finished one.** Claude reports failure as
  `{"type":"result","subtype":"error_during_execution","is_error":true,...}` — a type of plain
  `result`, which fell past the error branch and drew the failure with a tick beside it. Also: a
  nested `{"error":{"message":...}}` was read only as a string, so the sentence explaining why the
  run stopped was dropped and the event name kept; and a `tool_result` flagged `is_error` was
  folded into "returned" as noise.
- **`LogLine.Read` could take the dashboard down.** Its own summary says it never throws. An
  unpaired surrogate is valid JSON that cannot be read back as text: *"Cannot transcode invalid
  UTF-16 string to UTF-8 JSON text"*. Verified by removing the guard and watching the suite fail
  with it.
- **Six sentences on the main screen described a workflow that had been guessed at.** C promised
  "the current stage finishes first" when the agent is killed within half a second; "marks every
  message on the bus as handled" acknowledged the fifty the snapshot holds, leaving the oldest
  pending; "this task has never run" was shown for a task just retried; "verified and audited"
  described a task with no verification commands, which skips the verify stage entirely.

**Two that made the product act on what it was not showing:**

- **The roster acted on an agent it was not drawing.** The list was drawn from index 0 until it
  ran out of rows, so arrowing past the last visible row moved an invisible highlight — and Space,
  E and Del went on acting on it. The list is windowed around the selection now.
- **A flag ate the argument after it.** Every option took the next word as its value, documented
  to take one or not, so `fknrtd task show -json FKN-...` read the ID as the value of `-json` and
  then refused the line for having no task ID. The catalog already recorded which is which — an
  option's value hint is empty exactly when it takes no value — so the parser reads it from there
  rather than keeping a second list that could drift from the help page.

**One that broke a promise printed under the question it broke:**

- The verification step prints "leave empty to skip verification entirely". The field arrives
  holding the workspace's defaults, and pressing Enter on an empty one put them straight back.

### Two findings not acted on, and why

The `infopanel` review found four ways `InfoPanel` can draw outside its own rectangle — all of
them at panel widths around twelve columns. The dashboard refuses to render below 60 by 20 and
says so, so those widths are not reachable. Recorded rather than fixed; if the minimum ever drops,
they become real, and the review is in `docs/collab/reviews/infopanel.md`.

The `settings` review found that `defaultBaseRef` is written back with no validation, so an
invalid Git ref is accepted and fails later at task creation. That one is real and outstanding.

### What this says about where the faults are

Every product defect this round was in the gap between a surface and the service behind it, and
every one was written by the seat that also wrote the code it described. The suite was green
through all of them. The reviews are cheap — 320k tokens for nine defects, two of which would
have cost a whole run — and the thing that makes them work is that they compare two files rather
than reading one.

Local suite: 99/99. `scripts/verify.sh` reports every command behaving as the record says, with
two rows added in the order that hid the parser fault — every existing row wrote its options
last, which is why none of them caught it.

### For the other seat

The eight reviews are drained and their outputs are in `docs/collab/reviews/`. What is left from
them is listed above under "not acted on". The two briefs written earlier —
`BRIEF-codex-01-command-catalog.md` and `BRIEF-codex-02-settings-and-errors.md` — are still
unsent.
