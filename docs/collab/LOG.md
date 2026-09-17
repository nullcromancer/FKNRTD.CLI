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
