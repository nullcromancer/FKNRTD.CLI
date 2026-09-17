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
