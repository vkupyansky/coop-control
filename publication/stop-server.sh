#!/bin/sh
set -u
# Same as Ctrl+C in the server window: RageCoop saves and shuts down.
server_dir=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
pid_file="$server_dir/server.pid"
if [ ! -f "$pid_file" ]; then
    printf '%s\n' 'The server is not running.'
    exit 0
fi
pid=$(cat "$pid_file")
case "$pid" in ''|*[!0-9]*) pid='' ;; esac
if [ -z "$pid" ] || ! ps -p "$pid" -o command= 2>/dev/null | grep -q RageCoop.Server.dll; then
    rm -f "$pid_file"
    printf '%s\n' 'The server is not running.'
    exit 0
fi
kill -INT "$pid"
tries=0
while kill -0 "$pid" 2>/dev/null; do
    tries=$((tries + 1))
    if [ "$tries" -gt 40 ]; then
        kill -KILL "$pid" 2>/dev/null
        rm -f "$pid_file"
        printf '%s\n' 'The server did not answer in 20 seconds and was stopped. Garage and favourites are saved on every change.'
        exit 0
    fi
    sleep 0.5
done
printf '%s\n' 'Server stopped.'
