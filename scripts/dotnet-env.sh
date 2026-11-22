# shellcheck shell=bash
# Source this file before running dotnet in sandboxes where $HOME (and ~/.dotnet)
# is read-only:   source scripts/dotnet-env.sh
# It only redirects HOME when the current HOME is not writable.

_gob_dn_home="/tmp/dnhome"

if [ -z "${HOME:-}" ] || [ ! -w "${HOME}" ] || { [ -e "${HOME}/.dotnet" ] && [ ! -w "${HOME}/.dotnet" ]; }; then
  export HOME="${_gob_dn_home}"
fi

export DOTNET_CLI_HOME="${HOME}"
export NUGET_PACKAGES="${_gob_dn_home}/nuget"
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_NOLOGO=1
export DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1

mkdir -p "${_gob_dn_home}" "${NUGET_PACKAGES}" "${DOTNET_CLI_HOME}"

unset _gob_dn_home
