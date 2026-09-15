# FKNRTD.CLI Command Center

FKNRTD.CLI is a dependency-free C# terminal application that coordinates Claude Code, OpenAI Codex CLI, and any other coding CLI you register. It provides one live command center for repository state, context and rate-limit capacity, agent activity, task pipelines, verification evidence, messages, worktrees, and file-conflict warnings.

The workflow is intentionally evidence based:

```text
brief → isolated worktree → lead plan → implementation → deterministic verification
      → independent read-only audit → explicit landing
```

An implementer's report is not treated as verified work. Verification commands must pass, the auditor must return an explicit passing marker, and a human must issue the landing command.

## What the command center shows

At 120 columns and wider, the interactive dashboard uses the full FKNRTD.CLI layout:

```text
┌─ ◉ FKNRTD COMMAND CENTER ─────────────────────────────────────────────────────────────────┐
│ ▣ owner/repository  ⎇ feature/auth             ✓ clean  ↑2↓0  ✓ SAFE                    │
│ CTX 62% left | Claude 5h 78% 7d 64% | Codex 5h 82% 7d 71%                               │
├─ AGENT RADAR ─────────────┬─ PIPELINE ─────────────────────┬─ CI + USAGE ─────────────────┤
│ ◆ Claude  Lead  auditing  │ › ▶ FKN-... Authentication     │ BUILD ✓ TEST ✓ LINT ○         │
│ ▶ Codex  Impl  JWT        │ b✓ w✓ p✓ i▶ v○ a○ r○ l○       │ Claude 5h ███████░ 78%        │
│ ○ Cline  idle             │ Lead claude Implement codex    │ Codex  5h ████████░ 82%       │
├─ MESSAGE BUS ─────────────┼─ CONFLICT SENTINEL ─────────────┼─ EVENTS ──────────────────────┤
│ ↪ codex>claude ready      │ ✓ SAFE No path overlap         │ 10:23 ✓ tests passed          │
│                           │ codex: FKN-auth                 │ 10:22 · audit started         │
└───────────────────────────┴─────────────────────────────────┴───────────────────────────────┘
 [↑↓] Select [Enter] Run [N] New [C] Cancel [G] Land [M] Message [L] Logs [U] Usage [Q] Quit
```

The renderer reorganizes panels for medium and narrow terminals. A live collision is Priority 0 and replaces ordinary telemetry with a prominent red warning.

## Requirements

- .NET 10 SDK
- Git 2.28 or newer
- At least one configured coding CLI
- Claude Code and Codex CLI already authenticated if you use the built-in adapters
- A terminal with ANSI and UTF-8 support. Windows Terminal is recommended on Windows.

No third-party NuGet runtime packages are used.

## Install

### Windows

Open Command Prompt in the extracted folder:

```bat
scripts\install.cmd
```

Or install it manually:

```bat
dotnet pack src\FKNRTD.Cli\FKNRTD.Cli.csproj -c Release -o artifacts
dotnet tool install --global --add-source artifacts FKNRTD.CLI
```

If it is already installed, use `dotnet tool update` in place of `dotnet tool install`.

### macOS or Linux

```sh
./scripts/install.sh
```

Restart the terminal if `fknrtd` is not immediately on `PATH`.

## Initialize a repository

From the root of an existing Git repository:

```sh
fknrtd init
git add .fknrtd/config.json .fknrtd/.gitignore
git commit -m "Configure FKNRTD.CLI"
fknrtd doctor
```

FKNRTD.CLI keeps runtime state, task records, logs, worktrees, and artifacts ignored by Git. The small `config.json` and `.gitignore` files are intended to be committed so the coordination policy is reviewable.

Launch the command center:

```sh
fknrtd dashboard
```

## Run a real task

The built-in defaults use Claude as lead and auditor, and Codex as implementer:

```sh
fknrtd task create "Add JWT validation" \
  -brief "Validate issuer, audience, signature, and expiry. Add focused tests." \
  -verify "dotnet build" \
  -verify "dotnet test --no-build"

fknrtd task list
fknrtd task run FKN-20260914-123456-abcd
```

When the result is `ReadyToLand`, inspect it and merge explicitly:

```sh
fknrtd task show FKN-20260914-123456-abcd
fknrtd task land FKN-20260914-123456-abcd -confirm LAND
```

FKNRTD.CLI will not merge if the primary worktree is dirty, is on a different base branch, verification failed, or the audit did not pass.

## Dashboard keys

| Key | Action |
|---|---|
| Up or Down | Select a task |
| Enter | Run the selected task |
| N | Create a task |
| C | Request cancellation |
| G | Confirm and land verified work |
| M | Send an agent message |
| L or Tab | Toggle the selected task log |
| U | Refresh Codex allowance data |
| Q or Escape | Exit |

## Claude usage and statusline

Claude provides context and allowance data in its statusline JSON. Install the FKNRTD.CLI view globally with:

```sh
fknrtd integration install-claude-statusline
```

Use `-project` for `.claude/settings.json` in the current project, or `-force` to replace an existing statusline after FKNRTD.CLI backs it up. Restart Claude Code afterward.

The statusline stores Claude's real remaining context, five-hour allowance, and seven-day allowance. It also reads the cached Codex values and current FKNRTD.CLI agent activity. It never makes a remote call during rendering.

Refresh Codex explicitly:

```sh
fknrtd usage refresh codex
```

The collector uses Codex app-server's structured `account/rateLimits/read` request. It recognizes exact 300-minute and 10,080-minute windows. If an account does not expose a bucket, the dashboard shows `N/A`.

## Register another CLI

Every agent is an executable plus named argument-array profiles. Argument arrays avoid passing agent prompts through a shell.

```sh
fknrtd agent add \
  -id myagent \
  -name "My Agent" \
  -exe my-agent \
  -stdin \
  -plan-arg=plan \
  -implement-arg=implement \
  -audit-arg=audit
```

For arguments beginning with a hyphen, use the equals form, such as `-implement-arg=--json`. A complete JSON template is in `examples/generic-agent.json`.

An auditor profile must emit exactly one of these markers:

```text
FKNRTD_VERDICT: PASS
FKNRTD_VERDICT: FAIL
```

Use the CLI's own read-only or plan-mode argument for `plan` and `audit`. FKNRTD.CLI supplies read-only instructions, but only the underlying CLI can enforce its sandbox.

## External activity hooks

An agent wrapper or IDE can report observable intent without exposing hidden reasoning:

```sh
fknrtd telemetry report \
  -agent cline \
  -state running \
  -role implementer \
  -intent "Editing authentication middleware" \
  -path src/AuthMiddleware.cs \
  -planned-path tests/AuthMiddlewareTests.cs
```

Real progress is accepted only with a stated basis:

```sh
fknrtd telemetry report -agent cline -progress 60 -basis "3 of 5 plan steps complete"
```

## Claims, messages, and collisions

Register temporary ownership before an external agent writes:

```sh
fknrtd claim add -agent codex -path src/AuthService.cs -mode write -ttl 300
fknrtd claim list
fknrtd claim renew <claim-id>
fknrtd claim release <claim-id>
```

Overlapping write claims in the same physical worktree are a live collision. Overlapping paths in separate worktrees are a merge risk. Expired ownership is marked stale.

Agents and wrappers can publish coordination messages:

```sh
fknrtd message send -from codex -to claude -text "Tests pass; ready for audit"
fknrtd message list
```

## State layout

```text
.fknrtd/
  config.json               committed project policy
  tasks/                    workflow records
  runtime/
    agents/                 normalized live activity
    usage/                  real or explicitly reported allowance data
    claims/                 path ownership leases
    events.jsonl            append-only event stream
    messages.jsonl          append-only message bus
  logs/                     raw agent and verification output
  artifacts/                briefs, plans, and audit reports
  worktrees/                isolated Git worktrees
```

Writes use temporary files plus atomic replacement. Event and message history use compact JSON Lines. A per-task exclusive lock prevents two FKNRTD.CLI processes from running the same task.

## Build and test

```sh
dotnet build FKNRTD.CLI.sln -c Release
dotnet run --project tests/FKNRTD.SelfTest/FKNRTD.SelfTest.csproj -c Release
```

The self-test suite validates JSONL state, Claude usage conversion, Codex window parsing, collision classification, and a complete fake-agent workflow through worktree creation, implementation, verification, audit, commit, explicit landing, and cleanup.

Publish a single executable for Windows:

```bat
dotnet publish src\FKNRTD.Cli\FKNRTD.Cli.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish\win-x64
```

## Safety boundaries

- Agents receive prompts through `ProcessStartInfo.ArgumentList` or standard input, not through a shell.
- Each task implements in its own Git worktree and branch.
- Plan and audit profiles are configured read-only for Claude and Codex.
- Verification commands are trusted project configuration and intentionally use the platform shell.
- Failed verification or audit can trigger only the configured number of repair rounds.
- Agent completion, deterministic verification, independent audit, commit, and landing are separate states.
- Landing always requires the literal confirmation `LAND`.
- Cleanup always requires the literal confirmation `REMOVE`.
- No usage percentage or task progress is fabricated.

See [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) for the component model, [docs/COMMANDS.md](docs/COMMANDS.md) for the full command reference, and [VALIDATION.md](VALIDATION.md) for the verification record.

## Official integration references

- [Claude Code CLI reference](https://code.claude.com/docs/en/cli-reference)
- [Claude Code statusline](https://code.claude.com/docs/en/statusline)
- [Codex non-interactive mode](https://learn.chatgpt.com/docs/non-interactive-mode)
- [Codex app-server protocol](https://learn.chatgpt.com/docs/app-server)

## License

MIT. See [LICENSE](LICENSE).
