#!/bin/bash
# Start a Forge 1.12.2 server (Java 8) in the background, reading console
# commands from the named pipe <server>/in. Usage: start-server.sh <server dir>
S=$(cd "$1" && pwd) || exit 1
JAVA=${JAVA:-/usr/lib/jvm/java-8-openjdk-amd64/bin/java}
cd "$S"
pkill -f "sleep 100000 > $S/in" 2>/dev/null
rm -f in; mkfifo in
(sleep 100000 > "$S/in" &)
[ -f console.log ] && mv -f console.log console.prev.log
(nohup "$JAVA" -Xmx2G -jar forge-1.12.2-14.23.5.2860.jar nogui < in > console.log 2>&1 &)
for i in $(seq 1 180); do
  grep -qE 'Realistic World .* started|Exception' console.log 2>/dev/null && break
  sleep 1
done
grep -E 'Done \(|RealisticWorld|ERROR|Exception' console.log | head -20
