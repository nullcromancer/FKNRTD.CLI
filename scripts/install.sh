#!/usr/bin/env sh
set -eu

if ! command -v dotnet >/dev/null 2>&1; then
  echo ".NET 10 SDK was not found on PATH." >&2
  exit 1
fi

fknrtd_root=$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)
fknrtd_artifacts="$fknrtd_root/artifacts"
dotnet pack "$fknrtd_root/src/FKNRTD.Cli/FKNRTD.Cli.csproj" -c Release -o "$fknrtd_artifacts"

if dotnet tool list --global | grep -qi '^fknrtd\.cli '; then
  dotnet tool update --global --add-source "$fknrtd_artifacts" FKNRTD.CLI
else
  dotnet tool install --global --add-source "$fknrtd_artifacts" FKNRTD.CLI
fi

echo "FKNRTD.CLI installed. Run: fknrtd help"
