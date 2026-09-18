# FKNRTD.CLI — project invariants

FKNRTD.CLI is a dependency-free C# terminal command center that coordinates Claude Code,
OpenAI Codex CLI, and any other coding CLI registered in `.fknrtd/config.json`.

## Non-negotiable constraints

- **Zero third-party NuGet dependencies.** `FKNRTD.Core`, `FKNRTD.Cli` and `FKNRTD.SelfTest`
  use the BCL only. Do not add PackageReference entries. This is a product property, not an
  accident — the tool must install as a single `dotnet tool` with no transitive supply chain.
- **`TreatWarningsAsErrors` is on** (`Directory.Build.props`). Warnings fail the build.
  Do not suppress warnings to pass; fix the cause.
- **Nullable reference types are enabled** and must stay enabled.
- **Target framework is `net10.0`.** Do not retarget.
- Attribution in commits, copyright and licence headers is **nullcromancer**.

## Naming

The product is **FKNRTD.CLI**. The shipped binary and tool command are `fknrtd`.
Namespaces are `FKNRTD.*` (`FKNRTD.Domain`, `FKNRTD.Services`, `FKNRTD.Commands`,
`FKNRTD.Dashboard`, `FKNRTD.Telemetry`). Types use `Fknrtd*` (`FknrtdConfig`,
`FknrtdEvent`, `FknrtdPaths`, `FknrtdRuntime`). On-disk state lives in `<root>/.fknrtd/`.
Task ids are `FKN-<utc>-<rand>`. Audit verdict markers are `FKNRTD_VERDICT: PASS|FAIL`.
The statusline badge is `FKN`. There must be no reference anywhere to the former name
"Synergia", nor to "McK"/"McKenneys".

## Verification

Local suite — this is the only evidence that counts:

```
dotnet build FKNRTD.CLI.sln -c Release
dotnet run --project tests/FKNRTD.SelfTest/FKNRTD.SelfTest.csproj -c Release --no-build
```

The self-test must print `N/N self-tests passed` and exit 0. Add a self-test for every
behavior change; `tests/FKNRTD.SelfTest/Program.cs` is a hand-rolled harness, not a
framework — follow its existing `Check(...)` style.

**No self-test may start a real agent.** A workspace created by `fknrtd init` is configured
for Claude and Codex, and a developer's machine is likely to have both — so a test that runs a
task will launch them for real, with a live budget and a one-hour timeout. One did: it spent
ten minutes of real Codex time before it was killed. A test that needs a task to run either
supplies a fake agent by re-executing the self-test binary, as `TestWorkflowAsync` does, or
points every agent's executable at a name nothing resolves first. Never leave the shipped
executables in place in a test that reaches the implement stage.

**GitHub Actions results are not evidence.** Never gate on, wait for, or report CI status.

## Architecture rules

- `FKNRTD.Core` holds domain + services and must not reference `FKNRTD.Cli`.
- State writes go through `StateStore` (atomic temp-file + rename). Never write state files
  directly.
- Every external process launch goes through `ProcessRunner`. It must never be able to hang
  the CLI indefinitely or crash it on a bad executable.
- `DashboardApp.Render(snapshot, width, height, useColor)` is a **pure function** of its
  inputs. Keep it pure — it is the renderer's test seam.
- The command center is never the source of truth: Git and the agent CLIs are. Do not cache
  derived state that could go stale silently.
- A workspace is **not** guaranteed to be a Git repository. `FknrtdConfig.Mode` selects between
  `Git` and `Standalone`; every Git-dependent path must either be mode-gated or degrade without
  throwing. Never reintroduce a hard requirement on `git` outside `WorkspaceMode.Git`.

## Terminal rendering

- The dashboard targets 3 breakpoints: narrow (<84 cols), medium (84–119), wide (>=120).
- All box-drawing must stay aligned at every width. Layout math is in `Dashboard/Canvas.cs`.
- Never assume one `char` equals one terminal column: emoji, CJK and surrogate pairs break
  that assumption and shift every box border on the row.
- Colour must degrade cleanly: `-no-color` and redirected output must emit no ANSI escapes.
- **Colour is only ever an enhancement.** Stripping the escapes from a coloured frame must leave
  exactly the colourless frame, character for character — so nothing is ever distinguished by
  colour alone. A self-test asserts this for every scene; if you add a surface, it covers yours too.
- **Only draw glyphs the common terminal fonts have.** Every non-ASCII character was measured
  against Cascadia Mono (the Windows Terminal default), Consolas and Lucida Console. Four had been
  missing from all three — including the failure marker, which rendered as an empty box on a
  default Windows Terminal. Before introducing a new glyph, check it renders in at least Cascadia
  Mono and Consolas.

## The product explains itself

This is the property most easily broken by accident, so it is enforced by tests rather than by
convention.

- **Four tables in `src/FKNRTD.Cli/Help/` are the single source of every explanation.**
  `Glossary.cs` defines each concept, `CommandCatalog.cs` each command, `Keymap.cs` each dashboard
  key, `SettingsCatalog.cs` each configuration field. The dashboard's inline hints, the in-app
  reference, `fknrtd help`, `fknrtd explain` and the generated portal all read from them. Never
  write explanatory prose into a screen; add a row and point at it.
- Self-tests fail if a marker the dashboard can draw, a field a guided form asks for, a key the
  dispatcher handles, or a field on `FknrtdConfig` or `AgentDefinition` has no entry.
- **A key that does something must be in `Keymap.cs`.** No undocumented aliases.
- **An entry must be true.** Two independent reviews found twenty-six factual errors in these
  tables, every one written by the seat that also wrote the code it described. When you change
  behaviour, re-read the entry that describes it. Documentation that is confidently wrong is worse
  than documentation that is missing.
- **Every question carries its own explanation.** A `WizardStep` is required to name a glossary
  term, which is what stops a form asking for a "brief" or an "auditor" with nothing on screen
  saying what those are.
- **Say what to do next, phrased for the surface asking.** `Reference.NextStep` is shared between
  the dashboard and `fknrtd task show` so the two cannot disagree, and takes `onDashboard` so that
  a shell is never told to press a key.
- **A refusal states its reason.** An action that cannot be taken is listed with why, not hidden
  and not silently inert. An error says what happened, why it matters, and the next command.
- **Every event type belongs in `EventTypes.cs`.** A literal is how an event becomes silently
  unsearchable.

## Generated artifacts

Regenerate these after changing anything they describe; both are committed.

- `fknrtd-portal.html` — `fknrtd portal`. Built from the four tables plus `Milestones.cs`, so it
  cannot describe a command that was removed. Deterministic: the same version produces the same
  bytes, so the diff is reviewable.
- `docs/screenshots/*.png` — `python scripts/capture-frames.py`. Rendered from the real renderer
  and the real binary, never hand-made.
