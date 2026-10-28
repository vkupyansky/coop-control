#!/bin/sh
set -eu
server_dir=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
cd "$server_dir"
for file in RageCoop.Server.dll Bootstrap/CoopAdmin.Startup.dll Bootstrap/0Harmony.dll; do
    if [ ! -f "$file" ]; then
        printf '%s\n' "Missing $file. Extract the complete server folder again." >&2
        exit 1
    fi
done
if [ -f server.pid ] && pid=$(cat server.pid) && ps -p "$pid" -o command= 2>/dev/null | grep -q RageCoop.Server.dll; then
    printf '%s\n' 'The server is already running. To stop it, run stop-server.sh.'
    exit 0
fi
# The RageCoop server loads x64-only libraries. On Apple silicon the usual dotnet
# is arm64, so the x64 runtime (it runs under Rosetta) is looked for first.
dotnet_command=
if [ "$(uname -s)" = Darwin ] && [ "$(uname -m)" = arm64 ]; then
    for candidate in "${DOTNET_ROOT_X64:-}/dotnet" /usr/local/share/dotnet/x64/dotnet "$(command -v dotnet 2>/dev/null || true)"; do
        if [ -x "$candidate" ] && file -b "$candidate" | grep -q x86_64; then
            dotnet_command=$candidate
            break
        fi
    done
    if [ -z "$dotnet_command" ]; then
        printf '%s\n' 'Install the x64 .NET 10 runtime (the macOS x64 installer; it needs Rosetta). It goes to /usr/local/share/dotnet/x64.' >&2
        exit 1
    fi
else
    dotnet_command=$(command -v dotnet 2>/dev/null || true)
    if [ -z "$dotnet_command" ]; then
        printf '%s\n' 'Install the x64 .NET 10 runtime and put dotnet on PATH.' >&2
        exit 1
    fi
fi
DOTNET_STARTUP_HOOKS="$server_dir/Bootstrap/CoopAdmin.Startup.dll"
DOTNET_ROLL_FORWARD=Major
DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_STARTUP_HOOKS DOTNET_ROLL_FORWARD DOTNET_CLI_TELEMETRY_OPTOUT
exec "$dotnet_command" RageCoop.Server.dll
