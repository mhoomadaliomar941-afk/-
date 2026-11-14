#!/bin/bash
# Send console commands to the test server and print the log lines they made.
# Usage: SERVER=<server dir> [WAIT=seconds] console.sh "cmd 1" "cmd 2" ...
S=${SERVER:?set SERVER to the server directory}
n=$(wc -l < "$S/console.log")
for c in "$@"; do echo "$c" > "$S/in"; sleep 0.3; done
sleep "${WAIT:-2}"
tail -n +$((n+1)) "$S/console.log" | sed -E 's/^\[[0-9:]+\] \[Server thread\/INFO\] (\[minecraft\/DedicatedServer\]|\[RealisticWorld\]): //'
