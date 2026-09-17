# Brief — Codex seat 01: the command catalog and the HTML portal

## Why this exists

FKNRTD.CLI is powerful and completely unexplained. An operator who presses `N` in the
dashboard is asked for a "brief", a "lead", an "implementer", an "auditor" and a
"verification command" with no indication of what any of those are. The two seats are
fixing that together over the next stretch.

`src/FKNRTD.Cli/Help/Glossary.cs` already landed (commit `c26baea`). It is the single
table of *concepts* — brief, lead, auditor, worktree, verdict, claim, every stage, every
status marker — each with a one-line summary, a full detail paragraph and an example.
Read it first. It is the pattern this brief asks you to repeat for *commands*.

## File ownership — do not cross this line

Two seats are editing this repository at the same time.

| Path | Owner |
|---|---|
| `src/FKNRTD.Cli/Help/CommandCatalog.cs` | **you** (new file) |
| `src/FKNRTD.Cli/Portal/PortalWriter.cs` | **you** (new file) |
| `src/FKNRTD.Cli/Dashboard/**` | Claude — do not open |
| `src/FKNRTD.Cli/Commands/**` | Claude — do not open |
| `src/FKNRTD.Cli/Help/Glossary.cs` | Claude — **read it, never edit it** |
| `tests/FKNRTD.SelfTest/Program.cs` | Claude — do not open |

Create only the two files above. Claude wires them into the dispatcher afterwards. If you
believe you need to change a file you do not own, stop and write the reason into
`docs/collab/LOG.md` instead.

## Task 1 — `src/FKNRTD.Cli/Help/CommandCatalog.cs`

Namespace `FKNRTD.Help`. Same shape as `Glossary`: a static class holding one immutable
table, plus lookup helpers. No third-party packages; BCL only; nullable enabled;
`TreatWarningsAsErrors` is on.

Model every command and subcommand the dispatcher actually implements. Read
`src/FKNRTD.Cli/Commands/CommandDispatcher.cs` to enumerate them — do not invent any, and
do not omit any. Suggested records:

```csharp
public sealed record CommandOption(string Name, string ValueHint, string Meaning, bool Required = false);

public sealed record CommandEntry(
    string Invocation,       // "fknrtd task create \"Title\" -brief \"...\""
    string Name,             // "task create"
    string Group,            // "Getting started" | "Tasks" | "Agents" | "Coordination" | "Budget" | "Configuration"
    string Summary,          // one line
    string Detail,           // what it does and why you would reach for it
    string WhatHappensNext,  // the single most useful follow-up, in prose
    IReadOnlyList<CommandOption> Options,
    IReadOnlyList<string> Examples,
    IReadOnlyList<string> GlossaryTerms); // Glossary.Find keys this command's ideas map to
```

Expose `All`, `Groups`, `InGroup(string)`, `Find(string name)` (tolerant: `"task create"`,
`"task-create"` and `"create"` should all resolve), and `Search(string query)` ranked like
`Glossary.Search`.

Quality bar for the prose: write for someone who has never used the tool and is slightly
wary of handing a robot write access to their repository. Say what the command does, what
it changes on disk, and what it will *not* do. `WhatHappensNext` is the sentence that stops
a first-time user from getting stuck — after `init` it is `doctor`; after `task create` it
is running the task; after a failed run it is reading the log.

Exit codes matter and are currently undocumented anywhere: `0` success, `2` unknown command
or a failed required doctor check, `3` a failed/collided outcome, `130` cancelled. Fold
those into the relevant entries' `Detail`.

## Task 2 — `src/FKNRTD.Cli/Portal/PortalWriter.cs`

Namespace `FKNRTD.Portal`. One public entry point:

```csharp
public static class PortalWriter
{
    public static string Render(PortalModel model);   // returns a complete standalone HTML document
}
```

`PortalModel` is yours to define, but it must be built **only** from data passed in —
`Glossary.All`, `CommandCatalog.All`, a keymap list, a product version string, and a
generation timestamp. No file reads, no network, no `DateTime.Now` inside `Render` (take
the timestamp as input so the output is deterministic and testable).

Requirements for the document:

- One self-contained `.html` file. No external CSS, JS, fonts or images — it must open
  correctly from `file://` with no network at all.
- Dark by default, with a light mode under `@media (prefers-color-scheme: light)`. Define
  colors as CSS custom properties on `:root`.
- Readable at phone width and at 1600px. No horizontal scrolling.
- A sticky sidebar or top nav linking to each section, and a client-side filter box that
  hides non-matching glossary and command entries as you type. Vanilla JS, inline.
- Sections, in order: what FKNRTD.CLI is and the safety argument for it; the eight-stage
  pipeline as an inline SVG diagram (not ASCII); every command grouped, with options,
  examples and "what happens next"; the dashboard keymap; the full glossary grouped by
  category; where state lives on disk; exit codes.
- HTML-escape every interpolated string. Some glossary examples contain `<`, `>` and `&`.

Aim for something a person would actually read, not generated-doc wallpaper. The pipeline
SVG is the centrepiece — show the three roles, which stage each one owns, which single
stage can write files, and where the human confirmation sits.

## Verification — the only evidence that counts

```
dotnet build FKNRTD.CLI.sln -c Release
dotnet run --project tests/FKNRTD.SelfTest/FKNRTD.SelfTest.csproj -c Release --no-build
```

Must build with zero warnings and print `N/N self-tests passed`. Claude adds the self-tests
for these two files; you are not writing tests in `tests/`.

GitHub Actions results are not evidence. Do not wait on or report CI.

## Reporting back

Append to `docs/collab/LOG.md` when you finish, or if you get blocked:

```
## <UTC timestamp> — codex
<what you did, what you decided, anything Claude needs to know to wire it up>
```

Do not commit. Claude reviews and commits.
