# Validation record

## 2026-09-17 — the usability programme

Revision: 1.0.0
Commit verified: `cda7e8309023b19c87d766ba01fcf094663e949d`
(branch `feature/standalone-workspaces-and-tool-management`)

Environment: .NET SDK 10.0.401, Git 2.55.0.windows.4, Windows 11 (10.0.26200).

Every result below was produced by running the command on this machine against the commit
named above, in a freshly created temporary Git repository. Nothing here is inferred, and
nothing is reported that was not run.

| Command | Result |
| --- | --- |
| `dotnet build FKNRTD.CLI.sln -c Release` | Build succeeded. 0 warnings, 0 errors. |
| `dotnet run --project tests/FKNRTD.SelfTest/FKNRTD.SelfTest.csproj -c Release --no-build` | 49/49 self-tests passed, exit 0. |
| `fknrtd init -yes` | Workspace created, pre-flight checks run, exit 0. |
| `fknrtd doctor` | Exit 0; both configured agent executables resolved and reported versions. |
| `fknrtd agent list` / `-json` | Exit 0 for both. |
| `fknrtd config validate` | Exit 0. |
| `fknrtd task create` | Exit 0; task created with an `FKN-` identifier. |
| `fknrtd task list` / `-json` | Exit 0 for both. |
| `fknrtd task show <id>` / `-json` | Exit 0; brief, roles, verification, every stage and the next step. |
| `fknrtd task prompts <id>` | Exit 0; all three prompts, with the placeholders named as placeholders. |
| `fknrtd task diff <id>` | Exit 0; reported that the task has no worktree yet rather than printing nothing. |
| `fknrtd task land <id> -confirm LAND` on a queued task | Exit 1; named the status and what it would need to be. |
| `fknrtd task cancel <id>` | Exit 0; recorded the request. |
| `fknrtd task cancel <missing id>` | Exit 1; said no such task exists in this workspace. |
| `fknrtd claim add` | Exit 0; claim registered with its expiry. |
| `fknrtd claim list` / `-json`, no conflict | Exit 0 for both. |
| `fknrtd message list`, `fknrtd usage list` | Exit 0. |
| `fknrtd events` / `-json` | Exit 0 for both. |
| `fknrtd status -json` | Exit 0; complete normalised snapshot. |
| `fknrtd dashboard -once -no-color -width 100 -height 30` | Exit 0; one frame, no ANSI. |
| `fknrtd explain brief`, `fknrtd help task diff`, `fknrtd version` | Exit 0. |
| `fknrtd portal -out <file>` | Exit 0; 71 terms, 44 commands, 26 keys, 25 settings, 21 log entries; 170 KB. |
| `fknrtd taks` | Exit 2; reported the typo and named the commands meant. |

The exit codes documented but never observed were checked directly, because writing one down
is not the same as having seen it:

| Situation | Documented | Observed |
| --- | --- | --- |
| `claim list` with two agents claiming one path in one worktree | 3 | 3, and the collision named both agents and the path |
| `fknrtd task show <id>` on a failed task | 3 | 3 |
| `fknrtd task show <id> -json` on a failed task | 0 | 0 |

Also verified directly, outside the suite:

- **Glyph coverage.** Every non-ASCII character the dashboard can draw was measured against
  Cascadia Mono, Consolas and Lucida Console by rendering each one and comparing it to the
  font's own missing-glyph box. Four had been missing from all three, including the failure
  marker; all are replaced. Only Lucida Console still lacks anything, and only the heavy
  borders used for modal panels.
- **The diff plumbing.** Both Git invocations behind `task diff` and `V` were run against a
  repository with one committed change and one uncommitted line on top, and returned exactly
  the two halves the implementation combines.
- **Colour output.** Every rendered scene was checked to emit only reset, bold and 24-bit
  colour sequences, to hold its exact width once escapes are stripped, and to reset at the end
  of every row so a panel background cannot bleed past the frame.
- **The generated guide.** `fknrtd-portal.html` was checked to contain no external references,
  no broken internal links, and balanced structural tags.
- **A renderer sweep.** `dotnet run --project tests/FKNRTD.SelfTest -c Release -- fuzz` renders
  every scene at twenty widths from 1 to 400 and eleven heights from 1 to 80 — 8,140 frames
  across 37 scenes — and checks each for the right number of rows, the right display width on
  every row, and no exception. All 8,140 passed. The suite itself samples five widths and three
  heights; this is the wider net.
- **A landing Git refuses.** A task was run to ready-to-land, a conflicting version of the same
  file was committed to `main`, and the landing was attempted. The task came back Failed with
  its land stage failed, the base branch was exactly where it had been, and no conflict markers
  were left in the working copy. This is a regression test now; it was written because the
  command used to report that landing as a success and exit 0.

Independent review: three read-only audits by an OpenAI Codex seat, recorded in
`docs/collab/LOG.md`. The first found a non-terminating text wrap and eight other real defects
in the overlay layer. The second found twenty-six factual errors in the glossary. The third
found twenty-one in the command catalog, one of which was the landing bug above rather than a
documentation error. All are fixed, with regressions.

## 2026-09-15 — baseline

This section is the record as it stood before the usability programme above. It is kept
as written; the numbers in it were true of the commit it names.

Date: 2026-09-15
Revision: 1.0.0
Commit verified: `8809a88b87920137852761361f2d6f44999221be` (branch `main`)

## What was actually executed

Every result below was produced by running the command on this machine against the
commit named above. Nothing here is inferred, and nothing is reported that was not run.

Environment: .NET SDK 10.0.302, .NET runtime 10.0.10, Git 2.55.0, Windows 11 (10.0.26200).

| Command | Result |
| --- | --- |
| `dotnet build FKNRTD.CLI.sln -c Release` | Build succeeded. 0 warnings, 0 errors. |
| `dotnet run --project tests/FKNRTD.SelfTest/FKNRTD.SelfTest.csproj -c Release --no-build` | 19/19 self-tests passed, exit code 0. |
| `fknrtd init` in a fresh Git repository | Created `.fknrtd/` state and configuration, exit 0. |
| `fknrtd doctor` | All checks passed, exit 0; both configured agent executables resolved and reported versions. |
| `fknrtd task create` | Task created with an `FKN-` identifier. |
| `fknrtd status -json` | Emitted a complete normalised snapshot. |
| `fknrtd dashboard -once` | Rendered a single frame and exited. |
| `fknrtd telemetry claude-statusline` | Rendered a statusline from the sample payload in `examples/`. |

## Self-test coverage

The suite in `tests/FKNRTD.SelfTest/Program.cs` performs 19 checks:

1. Atomic snapshot and compact JSON Lines event storage
2. Process timeout kills the process tree and reports the timeout
3. Post-exit output drain is bounded when a grandchild inherits the pipe handles
4. An unlaunchable executable is reported as a start failure, not thrown
5. File lease acquisition is bounded and names the holding process
6. JSON Lines tail reads tolerate a concurrent appender
7. JSON Lines rotation preserves the most recent entries
8. Windows PATHEXT resolution beats an extensionless shim of the same name
9. A configuration file without the timeout keys still loads and receives defaults
10. Doctor renders an unlaunchable agent instead of crashing
11. Claude context and allowance conversion from used to remaining
12. Codex duration-based rate-limit parsing and missing-bucket behaviour
13. Same-worktree collision, separate-worktree merge risk, and stale-claim classification
14. The audit verdict ignores an echoed prompt containing both verdict markers
15. Git path lists round-trip verbatim, including non-ASCII and spaces
16. Dashboard frames preserve display width and border topology
17. Dashboard command-line dimension overrides take precedence over detection
18. Colour forcing beats redirection but not explicit suppression
19. A complete fake-agent workflow: worktree, lead plan, implementation, deterministic
    verification, exact audit verdict, commit, primary-state resolution from the linked
    worktree, explicit landing, and cleanup

## Independent renderer verification

The renderer was checked from outside its own code, not only by the self-tests that use
its own width implementation.

Frames were produced from the built binary with
`fknrtd dashboard -once -no-color -width W -height 32` at widths 60, 72, 84, 100, 119,
120, 140 and 200, then measured with an independent Unicode width implementation
(`unicodedata.east_asian_width` plus combining-mark detection).

- Every line measured exactly the requested display width at every width.
- No ESC (U+001B) character appeared in any frame under `-no-color`.
- Repeated with task titles containing Japanese text and an emoji: wide characters were
  confirmed present in the output and every line still measured exactly the requested
  width at 84, 120 and 160.
- A rendered 120-column frame contains no doubled corner seams; adjacent panels share
  joined border junctions.

## Documentation frames

The screenshots in `fknrtd-cli.html` are the program's own output, not mock-ups. Frames were
captured with `fknrtd dashboard -once -color -width W -height H` against the live test
repository after a real task had been planned, implemented, verified, audited and landed by
Claude and Codex. The captured ANSI was converted to HTML without alteration and photographed
with headless Chromium via Playwright.

## Limits of this record

- Display width is terminal-dependent. The implementation follows East Asian
  Wide/Fullwidth plus emoji presentation, which matches Windows Terminal and most modern
  emulators. A terminal that renders ambiguous-width characters as double width will
  still disagree.
- The end-to-end workflow self-test drives a fake agent, not Claude Code or Codex CLI. It
  proves the orchestration, worktree, verification, verdict and landing logic. It does not
  prove behaviour against any particular assistant's real output.
- Measured code coverage was not collected and is therefore not claimed.
- Only Windows was exercised. The POSIX paths in `ProcessRunner.RunShellAsync` are unverified on
  Linux and macOS; `scripts/manage.sh` was exercised under Git Bash on Windows only.

## Reproducing this record

From the repository root on a machine with the .NET 10 SDK:

```sh
dotnet --info
dotnet restore FKNRTD.CLI.sln
dotnet build FKNRTD.CLI.sln -c Release --no-restore
dotnet run --project tests/FKNRTD.SelfTest/FKNRTD.SelfTest.csproj -c Release --no-build
```

The self-test prints one line per check and a final `N/N self-tests passed` count,
exiting 0 only when every check passes.
