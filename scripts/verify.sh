#!/usr/bin/env bash
# Run every command the validation record claims a result for, against a throwaway workspace,
# and print what actually happened.
#
# The record in VALIDATION.md is a statement that these commands were run and produced these
# results. Re-typing that table by hand after each change is how such a record quietly stops
# being true. This runs it instead. Output is a markdown table, ready to paste.
#
# Usage: scripts/verify.sh [--keep]
#   --keep   leave the temporary workspace behind for inspection
set -u

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
FKNRTD="$ROOT/src/FKNRTD.Cli/bin/Release/net10.0/fknrtd.exe"
KEEP=0
[ "${1:-}" = "--keep" ] && KEEP=1

if [ ! -x "$FKNRTD" ]; then
  echo "Build first: dotnet build FKNRTD.CLI.sln -c Release" >&2
  exit 1
fi

WORK="$(mktemp -d)"
cleanup() { [ "$KEEP" = "1" ] || rm -rf "$WORK"; }
trap cleanup EXIT
[ "$KEEP" = "1" ] && echo "workspace: $WORK" >&2

git -C "$WORK" init -q -b main
git -C "$WORK" commit -q --allow-empty -m "root"

FAILURES=0
TASK=""

# Run one command, check its exit code against what is expected, print the row.
# row <label> <expected-exit> <command...>
row() {
  local label="$1" expect="$2"; shift 2
  local out code
  out="$("$@" 2>&1)"; code=$?
  local note
  note="$(printf '%s' "$out" | grep -v '^$' | head -1 | cut -c1-90)"
  if [ "$code" = "$expect" ]; then
    printf '| `%s` | Exit %s. %s |\n' "$label" "$code" "$note"
  else
    printf '| `%s` | **EXPECTED EXIT %s, GOT %s.** %s |\n' "$label" "$expect" "$code" "$note"
    FAILURES=$((FAILURES + 1))
  fi
}

# In the workspace, so relative paths behave as a user's would.
cd "$WORK" || exit 1

echo "| Command | Result |"
echo "| --- | --- |"

row "fknrtd init -yes"        0 "$FKNRTD" init -yes
row "fknrtd doctor"           0 "$FKNRTD" doctor
row "fknrtd agent list"       0 "$FKNRTD" agent list
row "fknrtd agent list -json" 0 "$FKNRTD" agent list -json
row "fknrtd config validate"  0 "$FKNRTD" config validate

# The title is positional here. `task create` has no -json: it prints the identifier in the
# text it writes, which is what this reads.
# Everything below needs a task to point at.
TASK="$("$FKNRTD" task create "Add rate limiting to the login endpoint" \
  -brief "Requests are unbounded." 2>/dev/null \
  | grep -o 'FKN-[0-9A-Za-z-]*' | head -1)"
if [ -z "$TASK" ]; then
  echo "| \`fknrtd task create\` | **NO TASK IDENTIFIER RETURNED.** |"
  FAILURES=$((FAILURES + 1))
else
  echo "| \`fknrtd task create\` | Exit 0; created \`$TASK\`. |"
fi

row "fknrtd task list"          0 "$FKNRTD" task list
row "fknrtd task list -json"    0 "$FKNRTD" task list -json
row "fknrtd task show <id>"     0 "$FKNRTD" task show "$TASK"
row "fknrtd task show -json"    0 "$FKNRTD" task show "$TASK" -json
row "fknrtd task prompts <id>"  0 "$FKNRTD" task prompts "$TASK"
row "fknrtd task diff <id>"     0 "$FKNRTD" task diff "$TASK"
row "fknrtd task land <queued>" 1 "$FKNRTD" task land "$TASK" -confirm LAND
row "fknrtd task cancel <id>"   0 "$FKNRTD" task cancel "$TASK"
row "fknrtd task cancel <none>" 1 "$FKNRTD" task cancel FKN-00000000-000000-zzzz

row "fknrtd claim add"        0 "$FKNRTD" claim add -agent claude -path src/auth.cs -ttl 300
row "fknrtd claim list"       0 "$FKNRTD" claim list
row "fknrtd claim list -json" 0 "$FKNRTD" claim list -json
row "fknrtd message list"     0 "$FKNRTD" message list
row "fknrtd usage list"       0 "$FKNRTD" usage list
row "fknrtd events"           0 "$FKNRTD" events
row "fknrtd events -json"     0 "$FKNRTD" events -json
row "fknrtd status -json"     0 "$FKNRTD" status -json
row "fknrtd dashboard -once"  0 "$FKNRTD" dashboard -once -no-color -width 100 -height 30
row "fknrtd explain brief"    0 "$FKNRTD" explain brief
row "fknrtd help task diff"   0 "$FKNRTD" help task diff
row "fknrtd version"          0 "$FKNRTD" version
row "fknrtd portal -out"      0 "$FKNRTD" portal -out "$WORK/portal.html"
row "fknrtd taks"             2 "$FKNRTD" taks

# A mistyped option is refused rather than ignored. Exit 2, the same as a mistyped command,
# because a script that asked for JSON and got a table must not be told it succeeded.
row "fknrtd task list -jsno"  2 "$FKNRTD" task list -jsno
row "fknrtd agent list -verbose" 2 "$FKNRTD" agent list -verbose

# A workspace that is not a Git repository has to work too, and is the case most easily
# forgotten, because the developer's own checkout always is one. It has to sit outside the
# repository created above: a subdirectory of a Git repository is still in a Git repository,
# which is how this check silently tested nothing the first time it was written.
PLAIN="$(mktemp -d)"
trap 'cleanup; [ "$KEEP" = "1" ] || rm -rf "$PLAIN"' EXIT
mkdir -p "$PLAIN" && cd "$PLAIN" || exit 1
row "fknrtd init -yes (no Git)"      0 "$FKNRTD" init -yes
row "fknrtd doctor (no Git)"         0 "$FKNRTD" doctor
row "fknrtd dashboard -once (no Git)" 0 "$FKNRTD" dashboard -once -no-color -width 100 -height 30

# A configuration edited by hand can hold values the settings screen would refuse. doctor and
# config validate have to agree about that: they used to disagree, and doctor - the one people
# are told to run - was the one saying everything was fine.
BROKEN="$(mktemp -d)"
trap 'cleanup; [ "$KEEP" = "1" ] || rm -rf "$PLAIN" "$BROKEN"' EXIT
cd "$BROKEN" || exit 1
"$FKNRTD" init -yes >/dev/null 2>&1
python -c "
import json, io
p = '.fknrtd/config.json'
c = json.load(io.open(p, encoding='utf-8'))
c['dashboardRefreshMilliseconds'] = 0
c['maxParallelAgents'] = 0
json.dump(c, io.open(p, 'w', encoding='utf-8'), indent=2)
" 2>/dev/null

row "fknrtd config validate (bad values)" 1 "$FKNRTD" config validate
row "fknrtd doctor (bad values)"          2 "$FKNRTD" doctor

echo
if [ "$FAILURES" = "0" ]; then
  echo "every command behaved as the record says"
else
  echo "$FAILURES command(s) did not behave as the record says"
fi
exit "$FAILURES"
