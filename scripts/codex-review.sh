#!/usr/bin/env bash
# Dispatch the queued read-only reviews to the Codex seat, one file at a time.
#
# Three fact-checks by this seat found 53 errors and two product defects, and the fourth returned
# nothing because it was scoped as three files and five questions and ran out of budget before it
# reached an answer. The lesson is in docs/collab/LOG.md and is built into this script: one file,
# one question, a short prompt. A dispatch that is too broad to finish is worth less than no
# dispatch, because it costs the same budget and returns nothing to act on.
#
#     scripts/codex-review.sh              # everything still queued
#     scripts/codex-review.sh reference    # one of them
#     scripts/codex-review.sh --list       # what is queued, without dispatching
#
# Output lands in docs/collab/reviews/. Nothing here writes to the repository except that
# directory: every dispatch is --sandbox read-only, which is the only shape that has ever worked.

set -uo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
OUT="$ROOT/docs/collab/reviews"

# name|file to read|what to check it against|the one question
QUEUE=$(cat <<'ENTRIES'
reference|src/FKNRTD.Cli/Dashboard/Reference.cs|src/FKNRTD.Core/Services/|Report ONLY statements about what this product does that the services do not do.
dashboard|src/FKNRTD.Cli/Dashboard/DashboardApp.cs|src/FKNRTD.Core/Services/|Report ONLY statements about what this product does that the services do not do.
roster|src/FKNRTD.Cli/Dashboard/AgentManager.cs|src/FKNRTD.Core/Domain/Configuration.cs|Report ONLY defects: an index that can go out of range, a panel that can draw outside its rectangle or clip its own text, a key that does nothing or the wrong thing, or code that contradicts its own comment.
settings|src/FKNRTD.Cli/Dashboard/SettingsBrowser.cs|src/FKNRTD.Core/Domain/Configuration.cs|Report ONLY defects: a value written back that could be invalid or lose data, a panel that can clip its own text, or code that contradicts its own comment.
infopanel|src/FKNRTD.Cli/Dashboard/InfoPanel.cs|src/FKNRTD.Cli/Dashboard/Canvas.cs|Report ONLY defects: an index or size that can go out of range, or content drawn outside the panel's rectangle.
options|src/FKNRTD.Cli/Commands/CliArguments.cs|src/FKNRTD.Cli/Help/CommandCatalog.cs|Report ONLY command lines that this parser would read differently from how the catalog documents them, or option spellings a user could reasonably write that it would mis-parse.
wizard|src/FKNRTD.Cli/Dashboard/Wizard.cs|src/FKNRTD.Cli/Dashboard/TaskWizard.cs|Report ONLY defects: an index that can go out of range, a step that can be skipped or repeated wrongly, an answer that can be lost, or code that contradicts its own comment.
logformat|src/FKNRTD.Cli/Dashboard/LogLine.cs|src/FKNRTD.Core/Services/AgentOutputObserver.cs|Report ONLY input that would make this throw, return something misleading, or lose the agent's message.
ENTRIES
)

if [ "${1:-}" = "--list" ]; then
    printf '%s\n' "$QUEUE" | while IFS='|' read -r name file _ _; do
        status="queued"
        [ -s "$OUT/$name.md" ] && status="done ($(wc -l < "$OUT/$name.md") lines)"
        printf '  %-10s %-52s %s\n' "$name" "$file" "$status"
    done
    exit 0
fi

if ! command -v codex >/dev/null 2>&1; then
    echo "codex is not on PATH." >&2
    exit 2
fi

mkdir -p "$OUT"
wanted="${1:-}"
dispatched=0

printf '%s\n' "$QUEUE" | while IFS='|' read -r name file against question; do
    [ -n "$wanted" ] && [ "$wanted" != "$name" ] && continue
    if [ -s "$OUT/$name.md" ]; then
        echo "skipping $name; $OUT/$name.md already has a review in it"
        continue
    fi

    echo "dispatching $name  ($file)"
    # Short, one file, one question. Long prompts stalled; this shape returned 53 findings.
    prompt="Read $file. Check it against $against. $question Give the line, the claim or code, and what is actually true. If you find nothing, say so."

    if codex exec --sandbox read-only --skip-git-repo-check "$prompt" > "$OUT/$name.md" 2>&1; then
        echo "  wrote $OUT/$name.md"
        dispatched=$((dispatched + 1))
    else
        # Keep the log under a different name. Leaving a failure at $name.md would make the next run
        # treat this review as done, which is how a queue quietly empties itself without doing
        # anything - the exact failure mode this script exists to avoid.
        mv "$OUT/$name.md" "$OUT/$name.failed.log" 2>/dev/null
        echo "  FAILED; the log is in $OUT/$name.failed.log, and $name stays queued" >&2
        if grep -q "usage limit" "$OUT/$name.failed.log" 2>/dev/null; then
            echo "  the budget is exhausted. The message says when it resets; run this again then." >&2
            exit 3
        fi
    fi
done

echo
echo "Triage every finding against the code before changing anything. Three rounds of this have"
echo "been accurate on every claim that was checked, and one round produced a correction that was"
echo "itself wrong - reviewing is cheaper than writing, and checking a review is cheaper than"
echo "trusting it."
