# Validation record

Date: 2026-09-14

## Checks completed in the authoring environment

- All 28 C# files passed a lexical delimiter, comment, character-literal, regular-string, verbatim-string, and raw-string structure scan.
- All project XML files parsed successfully.
- All JSON examples parsed successfully.
- The GitHub Actions workflow parsed successfully as YAML.
- The Unix installer passed `sh -n` syntax validation.
- Every relative Markdown link resolves to a packaged file.
- No zero-byte source or documentation files were found.
- A real Git smoke test created and removed a linked worktree under an ignored `.fknrtd/worktrees` directory.
- The repository contains no third-party runtime package reference.

## Build limitation

The authoring container did not contain `dotnet`, `csc`, Mono, MSBuild, or a cached .NET SDK, and its network policy did not permit downloading the SDK. A local compilation was therefore not possible in that environment and is not falsely reported as completed.

## Reproducible build verification

Run from the repository root on a machine with the .NET 10 SDK:

```sh
dotnet --info
dotnet restore FKNRTD.CLI.sln
dotnet build FKNRTD.CLI.sln -c Release --no-restore
dotnet run --project tests/FKNRTD.SelfTest/FKNRTD.SelfTest.csproj -c Release --no-build
```

The self-test performs five checks:

1. Atomic snapshot and compact JSONL event storage
2. Claude context and allowance conversion from used to remaining
3. Codex duration-based rate-limit parsing and missing-bucket behavior
4. Same-worktree collision, separate-worktree merge risk, and stale-claim classification
5. A complete fake-agent workflow: Git worktree, lead plan, implementation, deterministic verification, exact audit verdict, commit, primary-state resolution from the linked worktree, explicit landing, and cleanup

The included GitHub Actions workflow executes the build and self-test on current Windows and Ubuntu runners with .NET 10.
