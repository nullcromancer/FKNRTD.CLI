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
