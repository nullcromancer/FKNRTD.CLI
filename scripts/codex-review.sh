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
#     scripts/codex-review.sh --wait       # sleep until the budget resets, then dispatch
#
# Do not test whether the other seat is available with a small prompt first. A one-line dispatch
# costs about nineteen thousand tokens and fits under headroom that a real review does not, so it
# answers "is the seat reachable" and not "can the seat do a unit of work" - which is the only
# question worth asking. Running the queue is the probe: it fails safely, leaves every entry
# queued, and prints when to try again.
#
# This is the second round. The first eight reviews were dispatched on 2026-09-17 and their
# answers are still in docs/collab/reviews/ under their own names; what came of them is in
# docs/collab/LOG.md. This round asks about the code that changed as a result, which is where a
# new defect would be - every fault the first round found was in the gap between a surface and the
# service behind it, and this round was written by the seat that closed those gaps.
#
# Output lands in docs/collab/reviews/. Nothing here writes to the repository except that
# directory: every dispatch is --sandbox read-only, which is the only shape that has ever worked.

set -uo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
OUT="$ROOT/docs/collab/reviews"

# name|file to read|what to check it against|the one question
QUEUE=$(cat <<'ENTRIES'
identity|src/FKNRTD.Core/Services/DoctorService.cs|src/FKNRTD.Core/Services/Orchestrator.cs|Report ONLY prerequisites the orchestrator enforces during a run that doctor does not check, or checks differently.
newfiles|src/FKNRTD.Core/Services/GitService.cs|src/FKNRTD.Core/Services/Orchestrator.cs|Report ONLY ways GetDiffAsync could miss a change a task made, report one that is not there, or write to the repository it is reading.
logread|src/FKNRTD.Cli/Dashboard/LogLine.cs|src/FKNRTD.Core/Services/AgentOutputObserver.cs|Report ONLY input that would make this throw, return something misleading, or lose the agent's message.
parser|src/FKNRTD.Cli/Commands/CliArguments.cs|src/FKNRTD.Content/CommandCatalog.cs|Report ONLY command lines that this parser would read differently from how the catalog documents them, or option spellings a user could reasonably write that it would mis-parse.
window|src/FKNRTD.Cli/Dashboard/AgentManager.cs|src/FKNRTD.Cli/Dashboard/Canvas.cs|Report ONLY defects: an index that can go out of range, a panel that can draw outside its rectangle or clip its own text, a key that does nothing or the wrong thing, or code that contradicts its own comment.
wizardempty|src/FKNRTD.Cli/Dashboard/Wizard.cs|src/FKNRTD.Cli/Dashboard/TaskWizard.cs|Report ONLY defects: an answer that can be lost or replaced by one the user did not give, a step that can be skipped or repeated wrongly, or code that contradicts its own comment.
ENTRIES
)

# Sleep until the reset time in the last failure, then carry on. The goal this queue serves says
# that a seat which reaches its window waits and then continues; doing that by hand means somebody
# has to be awake at the right moment, and the reset is often many hours out.
if [ "${1:-}" = "--wait" ]; then
    shift
    latest=""
    for log in "$OUT"/*.failed.log; do
        [ -e "$log" ] || continue
        found=$(grep -oE "try again at [A-Z][a-z]+ [0-9]+[a-z]{2}, [0-9]{4} [0-9]+:[0-9]+ [AP]M" "$log" | tail -1)
        [ -n "$found" ] && latest="$found"
    done

    if [ -z "$latest" ]; then
        echo "No reset time is recorded in $OUT/*.failed.log; dispatching now." >&2
    elif ! seconds=$(printf '%s' "$latest" | python "$ROOT/scripts/reset-seconds.py"); then
        echo "Could not read a reset time out of '$latest'; dispatching now." >&2
    else
        echo "The budget resets at ${latest#try again at }, which is ${seconds}s away."
        echo "Sleeping until then, and dispatching as soon as it passes."
        sleep "$seconds"
        # A minute of slack. The reset is stated to the minute, and refusing on the boundary
        # would spend an entry to learn nothing.
        sleep 60
    fi
fi

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

    # </dev/null matters: the queue is being read by `while read` from a pipe, and codex exec
    # reads stdin. Without it the first dispatch swallows every remaining entry and the loop
    # ends after one review, having reported success. That is how seven of eight stayed queued.
    if codex exec --sandbox read-only --skip-git-repo-check "$prompt" > "$OUT/$name.md" 2>&1 </dev/null; then
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
