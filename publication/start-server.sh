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
command -v dotnet >/dev/null 2>&1 || {
    printf '%s\n' 'Install the x64 .NET 10 runtime and put dotnet on PATH.' >&2
    exit 1
}
DOTNET_STARTUP_HOOKS="$server_dir/Bootstrap/CoopAdmin.Startup.dll"
DOTNET_ROLL_FORWARD=Major
DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_STARTUP_HOOKS DOTNET_ROLL_FORWARD DOTNET_CLI_TELEMETRY_OPTOUT
exec dotnet RageCoop.Server.dll
