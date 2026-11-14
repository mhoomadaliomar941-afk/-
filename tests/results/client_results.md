# Client test results

Real Forge 1.12.2-14.23.5.2860 client (Java 8, Xvfb + Mesa llvmpipe OpenGL). 17/17 checks passed.

| Test | Check | Result | Measured |
|---|---|---|---|
| singleplayer (integrated server) | client started, world created, scenario completed without crash | PASS | log lines=975 crashed=False |
| singleplayer (integrated server) | player right-clicks with a water bucket: bucket becomes empty | PASS | use -> SUCCESS; held item=minecraft:bucket count=1 |
| singleplayer (integrated server) | the bucket placed exactly 1000 L of physical water | PASS | region: 999.990 L |
| singleplayer (integrated server) | tank filled (60000 L + displaced puddle), leak lowers the level, closing the hole stops it | PASS | full=60187 after leak=53693 -> 51100; closed: 51602.1 -> 51602.1 |
| singleplayer (integrated server) | pump pushes water up into glass pipes | PASS | pump flow=53.051 L/s, glass pipe holds 51.101 L |
| singleplayer (integrated server) | client receives physics detail packets from the (integrated) server | PASS | client stats packets=71 sections=4 activeSections=4 overlayCells=151 particles=18 fps=12 entityParticles=379; client stats packets=118 sections=7 activeSections=7 overlayCells=289 particles=14 fps=25 entityParticles=85; client stats packets=148 sections=7 activeSections=7 overlayCells=322 particles=3 fps=27 entityParticles=185; client stats packets=161 sections=7 activeSections=7 overlayCells=330 particles=8 fps=24 entityParticles=184 |
| singleplayer (integrated server) | wave/ripple overlay rendered on moving water | PASS | overlay cells per frame: [151, 289, 322, 330] |
| singleplayer (integrated server) | mass conservation in singleplayer | PASS | drift=-0.049 L of 136033.597 L |
| singleplayer (integrated server) | no exceptions from the mod in the client log | PASS | 0 lines |
| singleplayer (integrated server) | all mod models and textures load | PASS | 0 model/texture errors |
| singleplayer (integrated server) | screenshots captured | PASS | sp_02_tanks_full.png, sp_03_leak_jet_a.png, sp_04_leak_jet_b.png, sp_05_leak_spreading.png, sp_06_vortex_a.png, sp_07_vortex_b.png, sp_08_pump_pipes.png, sp_09_hydrometer_hud.png, sp_10_settings_gui.png |
| multiplayer (dedicated server + client) | server side: the tank leaks while a client is connected | PASS | tank volume now 22520 L |
| multiplayer (dedicated server + client) | client connects to the dedicated server (mod handshake ok) | PASS | joined=True |
| multiplayer (dedicated server + client) | client receives physics sync packets over the network | PASS | client stats packets=47 sections=5 activeSections=5 overlayCells=266 particles=12 fps=25 entityParticles=169; client stats packets=60 sections=5 activeSections=5 overlayCells=300 particles=4 fps=25 entityParticles=144; client stats packets=70 sections=5 activeSections=5 overlayCells=328 particles=3 fps=28 entityParticles=95 |
| multiplayer (dedicated server + client) | client renders effects from synced data (overlay cells > 0) | PASS | overlay cells: [266, 300, 328] |
| multiplayer (dedicated server + client) | screenshots captured | PASS | mp_01_leak.png, mp_02_leak_later.png |
| multiplayer (dedicated server + client) | no exceptions from the mod (client and server) | PASS | 0 lines |
