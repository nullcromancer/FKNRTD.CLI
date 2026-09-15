# Command reference

Options accept either `-name value` or `--name=value`. Repeat an option such as `-verify` or `-path` to supply multiple values. Use the equals form when the value itself starts with a hyphen.

## Project

| Command | Purpose |
|---|---|
| `fknrtd init [path]` | Create default Claude and Codex configuration in an existing Git repository |
| `fknrtd doctor` | Check .NET, Git, configuration, writable state, and configured executables |
| `fknrtd dashboard` | Open the interactive command center |
| `fknrtd dashboard -once` | Render one frame and exit |
| `fknrtd status` | Render one frame and return exit code 3 for a live collision |
| `fknrtd status -json` | Emit a complete normalized snapshot |
| `fknrtd config show` | Print the project configuration |
| `fknrtd config path` | Print the active configuration path |
| `fknrtd config validate` | Validate every configured agent definition |

## Tasks

| Command | Purpose |
|---|---|
| `task create <title>` | Create a queued task |
| `task list` | List tasks by recency |
| `task show <id>` | Show stages and evidence |
| `task run <id>` | Run or resume the pipeline |
| `task retry <id>` | Reset failed stages and run again |
| `task cancel <id>` | Request cancellation and process-tree termination |
| `task land <id> -confirm LAND` | Merge a ready task into its base branch |
| `task cleanup <id> -confirm REMOVE` | Remove its worktree while retaining task and branch |

`task create` options:

| Option | Meaning |
|---|---|
| `-brief <text>` | Inline brief |
| `-brief-file <path>` | Read the brief from a file |
| `-lead <agent>` | Planning agent |
| `-implementer <agent>` | Writing agent |
| `-auditor <agent>` | Independent auditor |
| `-verify <command>` | Repeatable deterministic check |
| `-base <branch>` | Override the configured base branch |
| `-repairs <count>` | Maximum repair cycles |
| `-run` | Start immediately after creation |

## Agents

| Command | Purpose |
|---|---|
| `agent list` | Show enabled state, executable discovery, and kind |
| `agent add -file <json>` | Add a complete serialized definition |
| `agent add -id <id> -exe <path>` | Build a generic definition from options |
| `agent enable <id>` | Enable a configured adapter |
| `agent disable <id>` | Retain but disable an adapter |
| `agent remove <id> -confirm REMOVE` | Remove an adapter from configuration |

Generic agent options include repeatable `-arg`, `-default-arg`, `-plan-arg`, `-implement-arg`, and `-audit-arg`. Add `-stdin` when the CLI reads its prompt from standard input. Supported placeholders are `{prompt}`, `{taskId}`, `{workspace}`, and `{branch}`.

## Telemetry

`telemetry report` and its `hook` alias update one agent runtime snapshot.

Options:

- `-agent`, required
- `-state`: unknown, offline, idle, planning, running, reviewing, waiting, blocked, failed, completed
- `-role`: observer, lead, implementer, auditor
- `-intent`, `-task`, `-source`
- `-cwd`, `-worktree`, `-branch`, `-pid`, `-exit-code`
- repeatable `-path` and `-planned-path`
- `-progress 0..100` with required `-basis`
- `-quiet`

## Claims and messages

| Command | Purpose |
|---|---|
| `claim add -agent <id> -path <pattern>` | Add a read or write lease |
| `claim renew <id>` | Extend a lease |
| `claim release <id>` | Release a lease |
| `claim list` | Print claims and detected risks |
| `message send -from <id> -to <id> -text <text>` | Append a delivered message |
| `message list` | Show current messages |
| `message ack <id>` | Append an acknowledgement |

## Usage and events

| Command | Purpose |
|---|---|
| `usage refresh codex` | Read structured Codex rate limits |
| `usage set <agent>` | Store an explicit external metric |
| `usage list` | Display cached values |
| `events -limit 50` | Display recent event history |

`usage set` accepts `-context`, `-five-hour`, `-weekly`, and `-source`. Values mean percentage remaining and are clamped to zero through one hundred.

## Integrations

| Command | Purpose |
|---|---|
| `integration install-claude-statusline` | Install a user-scoped command statusline |
| `integration install-claude-statusline -project` | Install in the current project |
| `integration install-claude-statusline -force` | Back up and replace an existing statusline |

## Exit codes

| Code | Meaning |
|---:|---|
| 0 | Success |
| 1 | Command or configuration error |
| 2 | Usage error or failed required doctor check |
| 3 | Failed workflow or live collision |
| 130 | Cancelled by Ctrl+C |
