# Client test results

Real Forge 1.12.2-14.23.5.2860 client (Java 8, Xvfb + Mesa llvmpipe OpenGL). 6/6 checks passed.

| Test | Check | Result | Measured |
|---|---|---|---|
| multiplayer (dedicated server + client) | server side: the tank leaks while a client is connected | PASS | tank volume now 22563 L |
| multiplayer (dedicated server + client) | client connects to the dedicated server (mod handshake ok) | PASS | joined=True |
| multiplayer (dedicated server + client) | client receives physics sync packets over the network | PASS | client stats packets=46 sections=5 activeSections=5 overlayCells=264 particles=8 fps=27 entityParticles=185; client stats packets=59 sections=5 activeSections=5 overlayCells=302 particles=5 fps=28 entityParticles=154; client stats packets=69 sections=6 activeSections=6 overlayCells=327 particles=3 fps=29 entityParticles=157 |
| multiplayer (dedicated server + client) | client renders effects from synced data (overlay cells > 0) | PASS | overlay cells: [264, 302, 327] |
| multiplayer (dedicated server + client) | screenshots captured | PASS | mp_01_leak.png, mp_02_leak_later.png |
| multiplayer (dedicated server + client) | no exceptions from the mod (client and server) | PASS | 0 lines |
