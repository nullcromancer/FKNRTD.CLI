# Queued pairing briefs

These are specs for `scripts/pairctl.py pair --spec`, not prose briefs. Each one is the whole of
what an implementing seat is told: goal, scope, constraints, mechanically checkable acceptance,
the files worth reading, and what is deliberately out of scope.

They live here rather than in a temporary directory because a session that ends with a brief
written and undispatched has produced nothing, and the next session should not have to derive it
again from a list of findings.

Dispatch one with:

```
python <ghostrider>/scripts/pairctl.py pair "<title>" \
    --spec docs/collab/briefs/<file>.json --repo . --implementer codex
```

Delete a file once its brief has landed. A brief left here is one still owed.

## Two things a dispatch will otherwise rediscover the hard way

- **The implementing seat cannot commit on this machine.** A linked worktree's index lives under
  the main repository's `.git/worktrees/<name>/`, outside a `workspace-write` sandbox, and
  `--add-dir` does not lift it. Every brief here tells the seat to leave its work uncommitted and
  say so; the auditing seat verifies and commits.
- **Kill MSBuild's worker nodes before believing a build failure** in the main checkout after a
  dispatch. `AGENTS.md` explains why under Verification.
