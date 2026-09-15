#!/usr/bin/env sh
# FKNRTD.CLI installation manager.
# Installs, updates, removes and diagnoses the global 'fknrtd' tool built from this checkout.
set -eu

package_id="FKNRTD.CLI"
command_name="fknrtd"

script_dir=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
root=$(CDPATH= cd -- "$script_dir/.." && pwd)
project="$root/src/FKNRTD.Cli/FKNRTD.Cli.csproj"
solution="$root/FKNRTD.CLI.sln"
self_test="$root/tests/FKNRTD.SelfTest/FKNRTD.SelfTest.csproj"
artifacts="$root/artifacts"

usage() {
  cat <<'USAGE'
FKNRTD.CLI installation manager

  manage.sh install      Pack this checkout and install the global tool
  manage.sh update       Pack this checkout and move the installed tool to it
  manage.sh uninstall    Remove the global tool; project .fknrtd state is kept
  manage.sh doctor       Diagnose the installation and report what to do next
  manage.sh help         Show this text

Options for install and update
  -verify      Build and run the local self-test suite before installing
  -no-pack     Install from artifacts/ without packing again

'fknrtd doctor' is a different command: it diagnoses a workspace, not the installation.
USAGE
}

fail() {
  printf 'fknrtd manage: %s\n' "$*" >&2
  exit 1
}

report() {
  printf '%-6s %-24s %s\n' "$1" "$2" "$3"
}

require_dotnet() {
  command -v dotnet >/dev/null 2>&1 || fail "the .NET 10 SDK was not found on PATH."
}

# <Version> from the tool project: the exact package this checkout produces.
source_version() {
  sed -n 's@.*<Version>\([^<]*\)</Version>.*@\1@p' "$project" | head -n 1
}

# Empty when the global tool is not installed.
installed_version() {
  dotnet tool list --global 2>/dev/null |
    awk 'tolower($1) == "fknrtd.cli" { print $2; exit }'
}

verify() {
  printf 'Verifying this checkout before installing.\n'
  dotnet build "$solution" -c Release
  dotnet run --project "$self_test" -c Release --no-build
}

pack() {
  dotnet pack "$project" -c Release -o "$artifacts"
}

# Shared flag parsing for install and update.
do_verify=0
do_pack=1
parse_options() {
  for option in "$@"; do
    case "$option" in
      -verify|--verify) do_verify=1 ;;
      -no-pack|--no-pack) do_pack=0 ;;
      *) fail "unknown option '$option'. Run 'manage.sh help'." ;;
    esac
  done
}

install_tool() {
  require_dotnet
  parse_options "$@"
  current=$(installed_version)
  if [ -n "$current" ]; then
    fail "$package_id $current is already installed. Run 'manage.sh update' instead."
  fi

  if [ "$do_verify" -eq 1 ]; then verify; fi
  if [ "$do_pack" -eq 1 ]; then pack; fi
  version=$(source_version)
  dotnet tool install --global --add-source "$artifacts" --version "$version" "$package_id"
  printf '\nInstalled %s %s. Run: %s help\n' "$package_id" "$version" "$command_name"
  warn_about_path
}

update_tool() {
  require_dotnet
  parse_options "$@"
  current=$(installed_version)
  if [ -z "$current" ]; then
    printf '%s is not installed yet; installing it instead.\n\n' "$package_id"
    if [ "$do_verify" -eq 1 ]; then verify; fi
    if [ "$do_pack" -eq 1 ]; then pack; fi
    version=$(source_version)
    dotnet tool install --global --add-source "$artifacts" --version "$version" "$package_id"
    printf '\nInstalled %s %s. Run: %s help\n' "$package_id" "$version" "$command_name"
    warn_about_path
    return
  fi

  if [ "$do_verify" -eq 1 ]; then verify; fi
  if [ "$do_pack" -eq 1 ]; then pack; fi
  version=$(source_version)
  if [ "$current" = "$version" ]; then
    # Same version number, changed content: 'update' is a no-op, so reinstall over it.
    printf 'Reinstalling %s %s over the same version.\n' "$package_id" "$version"
    dotnet tool uninstall --global "$package_id"
    dotnet tool install --global --add-source "$artifacts" --version "$version" "$package_id"
  else
    dotnet tool update --global --add-source "$artifacts" --version "$version" "$package_id"
  fi

  printf '\nUpdated %s %s -> %s. Run: %s help\n' "$package_id" "$current" "$version" "$command_name"
  warn_about_path
}

uninstall_tool() {
  require_dotnet
  current=$(installed_version)
  if [ -z "$current" ]; then
    printf '%s is not installed. Nothing to remove.\n' "$package_id"
    return
  fi

  dotnet tool uninstall --global "$package_id"
  printf '\nRemoved %s %s.\n' "$package_id" "$current"
  printf 'Project state in .fknrtd/ directories was left untouched.\n'
}

warn_about_path() {
  if command -v "$command_name" >/dev/null 2>&1; then
    return
  fi

  printf '\n%s is not on PATH yet. Add this directory and restart the terminal:\n  %s\n' \
    "$command_name" "$HOME/.dotnet/tools"
}

doctor() {
  status=0
  printf 'FKNRTD.CLI installation diagnostics\n'

  if command -v dotnet >/dev/null 2>&1; then
    report "OK"   "dotnet SDK" "$(dotnet --version 2>/dev/null) at $(command -v dotnet)"
  else
    report "FAIL" "dotnet SDK" "Not found on PATH. Install the .NET 10 SDK."
    # Every remaining check needs the SDK, so stop with a single actionable failure.
    printf '\nInstall the .NET 10 SDK and run this again.\n'
    return 1
  fi

  if [ -f "$project" ]; then
    version=$(source_version)
    report "OK"   "Source version" "$version from $project"
  else
    report "FAIL" "Source version" "$project is missing."
    return 1
  fi

  current=$(installed_version)
  if [ -n "$current" ]; then
    report "OK"   "Installed tool" "$package_id $current"
  else
    report "WARN" "Installed tool" "Not installed. Run 'manage.sh install'."
    status=1
  fi

  if [ -n "$current" ] && [ "$current" != "$version" ]; then
    report "WARN" "Up to date" "Installed $current, this checkout builds $version. Run 'manage.sh update'."
    status=1
  elif [ -n "$current" ]; then
    report "OK"   "Up to date" "Matches this checkout."
  fi

  resolved=$(command -v "$command_name" 2>/dev/null || true)
  if [ -n "$resolved" ]; then
    report "OK"   "Command on PATH" "$resolved"
  else
    report "WARN" "Command on PATH" "'$command_name' does not resolve. Add $HOME/.dotnet/tools to PATH."
    status=1
  fi

  if [ -n "$resolved" ]; then
    if reported=$("$command_name" --version 2>&1); then
      report "OK"   "Command runs" "$reported"
    else
      report "FAIL" "Command runs" "$command_name --version failed: $reported"
      status=1
    fi
  fi

  if ls "$artifacts"/*.nupkg >/dev/null 2>&1; then
    report "OK"   "Local package" "$artifacts"
  else
    report "WARN" "Local package" "No .nupkg in artifacts/. Packing happens on install or update."
  fi

  printf '\n'
  if [ "$status" -eq 0 ]; then
    printf 'The installation is healthy. For workspace diagnostics run: %s doctor\n' "$command_name"
  else
    printf 'Act on the warnings above, then run this again.\n'
  fi
  return "$status"
}

action=${1:-help}
if [ "$#" -gt 0 ]; then shift; fi

case "$action" in
  install) install_tool "$@" ;;
  update|upgrade) update_tool "$@" ;;
  uninstall|remove) uninstall_tool "$@" ;;
  doctor|status) doctor ;;
  help|-h|--help) usage ;;
  *) printf 'Unknown action: %s\n\n' "$action" >&2; usage >&2; exit 2 ;;
esac
