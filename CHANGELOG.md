# Changelog

## Unreleased

### Added

- **Standalone workspaces.** `fknrtd init` no longer requires a Git repository. A folder that is
  not a repository — or a machine with no Git at all — is provisioned in standalone mode, where
  agents work directly in the project folder instead of an isolated worktree, nothing is committed
  or merged, and landing records that the verified work is already in place. `-standalone` forces
  the mode inside a repository; `-git` demands a repository and fails without one. The mode is
  recorded as `mode` in `.fknrtd/config.json` and defaults to `git`, so existing configurations
  are unaffected.
- **Bare invocation opens the current folder.** Running `fknrtd` with no arguments opens the
  dashboard with the default loading parameters, provisioning the workspace first when there is
  none, instead of printing help. It searches the current folder and above, so running from a
  subdirectory finds the enclosing project. When no workspace exists anywhere it provisions the
  Git repository root if one encloses the folder, and the folder itself otherwise; `fknrtd .` pins
  it to the current folder regardless.
- `fknrtd doctor` reports the workspace mode and treats the Git checks as informational in a
  standalone workspace rather than failing the diagnostic.
- **An update and uninstall route.** `scripts/install.cmd` and `scripts/install.sh` are replaced
  by `scripts/manage.cmd` and `scripts/manage.sh`, one management script per platform covering
  `install`, `update`, `uninstall`, `doctor` and `help`. `update` reinstalls in place when the
  version number has not moved, which `dotnet tool update` alone will not do, so a rebuilt
  checkout actually reaches the installed tool. `install` refuses to clobber an existing
  installation and points at `update`; `uninstall` leaves project `.fknrtd/` state alone. Both
  accept `-verify` to gate on the local self-test suite and `-no-pack` to skip packing.
- `scripts/manage.* doctor` diagnoses the installation itself: SDK, the version this checkout
  builds against the version installed, `PATH` resolution and whether the command runs.

### Fixed

- The colour-forcing self-test read `NO_COLOR` from the invoking terminal, so it failed on any
  machine that sets it. It now pins the variable for the duration and additionally covers
  `NO_COLOR` overriding an explicit `-color`.

## 1.0.0 - 2026-09-15

First shippable revision. The command center was renamed from its working title and then
hardened until it builds clean and runs reliably.

### Renamed

- Renamed the product to FKNRTD.CLI throughout: namespaces are `FKNRTD.*`, types are
  `Fknrtd*`, the shipped binary and tool command are `fknrtd`, the package id is
  `FKNRTD.CLI`, on-disk state moved to `<repo>/.fknrtd/`, task identifiers use the `FKN-`
  prefix, and audit verdict markers are `FKNRTD_VERDICT: PASS` and `FKNRTD_VERDICT: FAIL`.

### Fixed - reliability

- Agent and task file leases could loop forever: the retry guard was always true when no
  attempt cap was supplied, so a held lock hung the process with no message and no
  timeout. Acquisition is now bounded by both attempt count and wall clock, and the error
  names the process holding the lock.
- Launching an executable that the operating system refused to start threw an unhandled
  exception and aborted the command. `fknrtd doctor` crashed on any machine where a
  configured agent was an npm shim. Launch failures are now returned as a result.
- Executable resolution preferred an extensionless file on `PATH` over the PATHEXT match,
  which resolved the wrong `codex` on Windows. PATHEXT candidates now win.
- No process had a timeout. A hung agent or verification command hung the whole workflow
  indefinitely. Agent and verification timeouts are now configurable and enforced, killing
  the process tree on expiry and reporting partial output.
- Output draining after a process exited was unbounded, so a grandchild inheriting the
  pipe handles could hang a completed run forever. The drain is now bounded and marks the
  result as truncated.
- The dashboard read the entire event history into memory on every refresh. Reads are now
  tail-bounded and the history rotates once past a size cap.
- Concurrent readers and writers of the event and message logs could throw. File sharing
  is now correct on both sides.

### Fixed - workflow correctness

- The audit verdict could be corrupted by an agent echoing its own prompt, because the
  prompt contains both the pass and fail markers. Verdicts are now read from the agent's
  final text with the prompt echo removed, and an ambiguous verdict never passes.
- Verdict matching now tolerates markdown wrapping while still requiring the marker to own
  its line.
- Base refs defaulting to the literal `HEAD` made landing checks compare against a
  non-branch. Base refs now resolve to a real local branch and are repaired at run and
  land time.
- Git path lists mangled paths containing spaces, quotes or non-ASCII characters. Path
  listing is now NUL-terminated with quoting disabled.
- Committing verified changes with no Git committer identity surfaced raw git stderr. It
  now fails with a message naming `user.name` and `user.email`.
- Task cleanup left orphaned worktree administrative state and branches behind.

### Fixed - terminal rendering

- The renderer assumed one character occupied one terminal column, so a CJK or emoji
  repository name, branch or task title shifted every box border on its row. Text is now
  measured in display columns by grapheme cluster.
- Truncation could split a surrogate pair or a combining sequence.
- Adjacent dashboard panels drew doubled corner seams instead of shared joined borders.
- The pipeline panel could scroll the selected task out of view with no way to see it. The
  viewport now follows the selection and indicates more tasks above and below.
- Opening a task log while an agent was writing it threw an IOException.
- Shrinking the terminal left stale characters from the previous wider frame.

### Added

- `AgentTimeoutSeconds` and `VerificationTimeoutSeconds` configuration keys, with defaults
  applied to configuration files written before they existed.
- `OutputTruncated` on command results, distinguishing a complete capture from a truncated
  one.
- `-width` and `-height` on `dashboard -once` and `status`, so a frame can be rendered
  deterministically in a script or a test.
- `-color` to force ANSI output through a redirect. Colour is still suppressed for piped
  output by default, and `-no-color` and `NO_COLOR` still override the flag; without it a
  coloured frame could not be captured for documentation or a pager at all.
- Self-test coverage grew from 5 checks to 19, including process timeout and drain bounds,
  lease bounds, PATHEXT resolution, verdict spoofing, verbatim Git paths, and renderer
  frame geometry across a width sweep.
- `AGENTS.md` recording the project invariants that both human and AI contributors must
  hold to.

### Documentation

- Added `fknrtd-cli.html`, a self-contained single-page operator manual carrying
  screenshots of the program's own output.
- Rewrote `README.md` as an evidence-based reference in which every substantive claim
  cites the file it came from.
- Rewrote `VALIDATION.md`. The previous record claimed checks that had not been run,
  including a GitHub Actions workflow that does not exist in this repository, and reported
  a self-test count that was no longer accurate.
