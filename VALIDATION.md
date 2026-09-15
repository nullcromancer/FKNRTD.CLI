# Validation record

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
- Only Windows was exercised. The POSIX paths in `ProcessRunner.RunShellAsync` and
  `scripts/install.sh` are unverified on Linux and macOS.

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
