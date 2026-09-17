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
