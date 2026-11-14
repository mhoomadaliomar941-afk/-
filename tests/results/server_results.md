# Server test results

Real Forge 1.12.2-14.23.5.2860 dedicated server, Java 8. 36/40 checks passed.

| Test | Check | Result | Measured |
|---|---|---|---|
| 1 bucket on the ground | vanilla bucket item used through the bucket event | PASS | bucket mode=empty result=SUCCESS item=minecraft:bucket |
| 1 bucket on the ground | exactly 1000 L placed in the world | PASS | volume=999.988 L in 49 blocks |
| 1 bucket on the ground | water spread over the ground (not a single block) | PASS | 49 blocks wet, surface y=4.033 |
| 1 bucket on the ground | empty bucket picks up exactly 1000 L again (mass conserved) | PASS | pickup: bucket mode=fill result=SUCCESS item=minecraft:water_bucket; remaining=0.000 L |
| 1 bucket on the ground | no water can be taken from nothing (no infinite water) | PASS | bucket mode=fill result=FAIL item=minecraft:bucket |
| 2 fill a 10000 L tank with buckets | tank capacity detected | PASS | tank pos=-428,5,551 capacity_l=10000 closed=false spill=10 floor=5 volume_l=0.000 |
| 2 fill a 10000 L tank with buckets | 10 buckets = 10000 L inside the tank | **FAIL** | volume=9574.01 L, 10 blocks, surface y=9.822 |
| 2 fill a 10000 L tank with buckets | water level reached the rim (tank full) | **FAIL** | surface=9.822, rim top=10 |
| 2 fill a 10000 L tank with buckets | hydrostatic pressure at the bottom ~ rho*g*h (5 m -> ~49 kPa) | PASS | bottom pressure=47.00 kPa |
| 2 fill a 10000 L tank with buckets | an 11th bucket overflows: the tank keeps its capacity, the rest spills out | **FAIL** | inside=9896.8 L, spilled outside=917.2 L |
| 3-5,7 hole in a tank wall | tank filled | PASS | volume=80000.0 L surface=10.000 |
| 3-5,7 hole in a tank wall | 3) breaking a wall block creates a hole: water starts leaving | PASS | V: 80000 -> 53369 L |
| 3-5,7 hole in a tank wall | 4) water flows out through the hole onto the ground | PASS | outside=23735 L in 258 blocks |
| 3-5,7 hole in a tank wall | 5) the level inside decreases gradually (monotonic) | PASS | surface 10.000 -> 8.424; samples=11 |
| 3-5,7 hole in a tank wall | outflow follows Torricelli (Q/sqrt(h) roughly constant) | PASS | Q/sqrt(h): mean=1810 L/s/m^0.5, spread=4%, ideal free orifice=2746; samples (h m, Q L/s): (4.50, 4221), (4.50, 3760), (4.32, 3669), (4.02, 3640), (3.80, 3539), (3.62, 3451), (3.46, 3377), (3.29, 3331), (3.15, 3245), (3.00, 3173) |
| 3-5,7 hole in a tank wall | 7) closing the hole stops the leak | PASS | after closing: 51392.54 L -> 51392.54 L in 5 s |
| 6 leak speed depends on hole size | bigger hole drains faster (2 m^2 > 1 m^2 > 100 cm^2 > 20 cm^2) | PASS | 1 block hole (1 m^2): 3491.9 L/s, 2 block hole (2 m^2): 6601.4 L/s, spigot 100 cm^2: 58.2 L/s, spigot 20 cm^2: 11.6 L/s |
| 6 leak speed depends on hole size | 2 m^2 hole drains about twice as fast as 1 m^2 (ratio 1.4..2.6) | PASS | ratio=1.89 |
| 6 leak speed depends on hole size | small orifices follow area ratio 100/20 = 5 (ratio 4..6) | PASS | ratio=5.00 |
| 6 leak speed depends on hole size | spigot flow matches Torricelli Q = Cd*A*sqrt(2gh) (within 25%) | PASS | measured 58.18 L/s, theory 58.26 L/s |
| 8 two tanks connected by a pipe | initial: tank A full of water, tank B empty | PASS | A=35094 L (surface 9.00) B=207 L |
| 8 two tanks connected by a pipe | water flows through the pipe from A to B | PASS | B received 17296 L; initial flow 495 L/s |
| 8 two tanks connected by a pipe | levels equalise (communicating vessels) | PASS | surface A=6.969 B=6.922 after 81 s |
| 8 two tanks connected by a pipe | total water conserved (A + B + pipe contents = poured amount) | PASS | A+B+pipes=36000.29 L vs poured 36000.00 L (pipes hold 986.2 L) |
| valve controls the flow | closed valve: no flow between the tanks | PASS | B: 17294 -> 17292 L while A=35706 L |
| valve controls the flow | opened valve: flow resumes | PASS | B: 17292 -> 18715 L |
| pump lifts water to a higher tank | water is pumped up 7 m into the upper tank | PASS | upper tank=812 L (surface 12.11), pump flow=47.292 L/s |
| pump lifts water to a higher tank | source tank loses what the upper tank gains (+ pipe contents) | PASS | source -957 L, upper +812 L |
| over-pressure: pipes crack and fragile pipes burst | pump against a closed valve builds pressure above the glass rating (60 kPa) | PASS | max pressure seen=212.4 kPa |
| over-pressure: pipes crack and fragile pipes burst | the fragile glass pipe bursts and releases its water | PASS | glass pipe position now: waterphysics:water |
| over-pressure: pipes crack and fragile pipes burst | copper pipe (rated 800 kPa) survives the same pressure | PASS | copper damage=0.000 at 0.152 kPa |
| over-pressure: pipes crack and fragile pipes burst | a cracked pipe leaks water into the world | PASS | water in the basin around the pipe: 492.2 -> 743.6 L (26 blocks); pipe at 89.421 kPa |
| mass conservation | stored water = sum(in) - sum(out) over the whole session | PASS | drift=-0.370 L of 424140 L total (rounding -78.0980 L) |
| performance | server tick cost of the simulation | PASS | last tick 8.058 ms, 32 active sections, 20 pipe nodes |
| 9 restart persistence | water volume of a draining tank is preserved across restart | PASS | before stop=44945.8 L, after restart=44930.1 L |
| 9 restart persistence | the leak continues after restart (simulation state restored) | PASS | 44930.1 -> 44859.7 L in 6 s |
| 9 restart persistence | connected tanks keep their water (A+B) | PASS | A+B 52968 -> 52967 L (A 28491->28064, B 24477->24904) |
| 9 restart persistence | pipe contents are saved | PASS | pipe 197.81 -> 197.827 L |
| 9 restart persistence | mass accounting survives restart | **FAIL** | drift=57.894 L of 424198 L |
| stability | no exceptions from the mod in the server logs | PASS | 0 error lines |
