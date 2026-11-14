# Server test results

Real Forge 1.12.2-14.23.5.2860 dedicated server, Java 8. 43/45 checks passed.

| Test | Check | Result | Measured |
|---|---|---|---|
| 1 bucket on the ground | vanilla bucket item used through the bucket event | PASS | bucket mode=empty result=SUCCESS item=minecraft:bucket |
| 1 bucket on the ground | exactly 1000 L placed in the world | PASS | volume=999.993 L in 50 blocks |
| 1 bucket on the ground | water spread over the ground (not a single block) | PASS | 50 blocks wet, surface y=4.032 |
| 1 bucket on the ground | empty bucket picks up exactly 1000 L again (mass conserved) | PASS | pickup: bucket mode=fill result=SUCCESS item=minecraft:water_bucket; remaining=0.000 L |
| 1 bucket on the ground | no water can be taken from nothing (no infinite water) | PASS | bucket mode=fill result=FAIL item=minecraft:bucket |
| 2 fill a 10000 L tank with buckets | tank capacity detected | PASS | tank pos=-563,5,717 capacity_l=10000 closed=false spill=10 floor=5 volume_l=0.000 |
| 2 fill a 10000 L tank with buckets | 9 buckets = exactly 9000 L in the tank (nothing lost) | PASS | volume=8999.996 L, surface y=9.512 |
| 2 fill a 10000 L tank with buckets | 10th bucket fills the 10000 L tank to the brim (>= 99.5 %) | PASS | inside=9999.99 L, splashed over the rim=0.00 L, surface y=10.000 |
| 2 fill a 10000 L tank with buckets | all 10000 L accounted for (inside + spilled) | PASS | total=9999.994 L |
| 2 fill a 10000 L tank with buckets | hydrostatic pressure at the bottom ~ rho*g*h (5 m -> ~49 kPa) | PASS | bottom pressure=49.05 kPa |
| 2 fill a 10000 L tank with buckets | an 11th bucket overflows: the tank stays full, the excess spills out | PASS | inside=9888.4 L, outside=1111.4 L, total=10999.79 L |
| 3-5,7 hole in a tank wall | tank filled | PASS | volume=80000.0 L surface=10.000 |
| 3-5,7 hole in a tank wall | 3) breaking a wall block creates a hole: water starts leaving | PASS | V: 80000 -> 55447 L |
| 3-5,7 hole in a tank wall | 4) water flows out through the hole onto the ground | PASS | outside=21935 L in 216 blocks |
| 3-5,7 hole in a tank wall | 5) the level inside decreases gradually (monotonic) | PASS | surface 10.000 -> 8.576; samples=11 |
| 3-5,7 hole in a tank wall | outflow follows Torricelli (Q/sqrt(h) roughly constant) | PASS | Q/sqrt(h): mean=1635 L/s/m^0.5, spread=2%, ideal free orifice=2746; samples (h m, Q L/s): (4.50, 4014), (4.50, 3417), (4.36, 3396), (4.13, 3332), (3.94, 3238), (3.77, 3174), (3.61, 3108), (3.44, 3063), (3.29, 2983), (3.15, 2914) |
| 3-5,7 hole in a tank wall | 7) closing the hole stops the leak | PASS | after closing: 53664.27 L -> 53664.26 L in 5 s |
| 6 leak speed depends on hole size | bigger hole drains faster (2 m^2 > 1 m^2 > 100 cm^2 > 20 cm^2) | PASS | 1 block hole (1 m^2): 3345.3 L/s, 2 block hole (2 m^2): 6159.8 L/s, spigot 100 cm^2: 58.2 L/s, spigot 20 cm^2: 11.6 L/s |
| 6 leak speed depends on hole size | 2 m^2 hole drains about twice as fast as 1 m^2 (ratio 1.4..2.6) | PASS | ratio=1.84 |
| 6 leak speed depends on hole size | small orifices follow area ratio 100/20 = 5 (ratio 4..6) | PASS | ratio=4.99 |
| 6 leak speed depends on hole size | spigot flow matches Torricelli Q = Cd*A*sqrt(2gh) (within 25%) | PASS | measured 58.17 L/s, theory 58.26 L/s |
| 6b several holes share the outflow by depth | the deeper hole leaks faster (higher pressure) | PASS | low hole 64.25 L/s, high hole 43.19 L/s |
| 6b several holes share the outflow by depth | flow ratio matches Torricelli sqrt(h_low/h_high) within 10% | PASS | ratio=1.488 expected=1.486 (h_low=5.48 m, h_high=2.48 m) |
| tank gauge + comparator | comparator reads the fill level from the gauge (50 %% -> signal 7..8) | **FAIL** | tank 18000.000 / 36000 L, comparator signal=None |
| bucket poured into a pipe | water poured at the open end of a pipe flows into it | **FAIL** | bucket mode=empty result=SUCCESS item=minecraft:bucket; pipe contents bottom..top = 0.0, 0.0, 0.0 L |
| 8 two tanks connected by a pipe | initial: tank A full of water, tank B empty | PASS | A=35095 L (surface 9.00) B=207 L |
| 8 two tanks connected by a pipe | water flows through the pipe from A to B | PASS | B received 17324 L; initial flow 491 L/s |
| 8 two tanks connected by a pipe | levels equalise (communicating vessels) | PASS | surface A=6.966 B=6.925 after 84 s |
| 8 two tanks connected by a pipe | total water conserved (A + B + pipe contents = poured amount) | PASS | A+B+pipes=36000.77 L vs poured 36000.00 L (pipes hold 988.1 L) |
| valve controls the flow | closed valve: no flow between the tanks | PASS | B: 17317 -> 17318 L while A=35672 L |
| valve controls the flow | opened valve: flow resumes | PASS | B: 17318 -> 18741 L |
| pump lifts water to a higher tank | water is pumped up 7 m into the upper tank | PASS | upper tank=809 L (surface 12.11), pump flow=47.365 L/s |
| pump lifts water to a higher tank | source tank loses what the upper tank gains (+ pipe contents) | PASS | source -957 L, upper +809 L |
| over-pressure: pipes crack and fragile pipes burst | pump against a closed valve builds pressure above the glass rating (60 kPa) | PASS | max pressure seen=212.5 kPa |
| over-pressure: pipes crack and fragile pipes burst | the fragile glass pipe bursts and releases its water | PASS | glass pipe position now: waterphysics:water |
| over-pressure: pipes crack and fragile pipes burst | copper pipe (rated 800 kPa) survives the same pressure | PASS | copper damage=0.000 at 0.713 kPa |
| over-pressure: pipes crack and fragile pipes burst | a cracked pipe leaks water into the world | PASS | water in the basin around the pipe: 474.2 -> 678.8 L (24 blocks); pipe at 60.522 kPa |
| mass conservation | stored water = sum(in) - sum(out) over the whole session | PASS | drift=-5.719 L of 497073 L total (rounding -78.4060 L) |
| performance | server tick cost of the simulation | PASS | last tick 16.448 ms, 39 active sections, 23 pipe nodes |
| 9 restart persistence | water volume of a draining tank is preserved across restart | PASS | before stop=44946.4 L, after restart=44930.1 L |
| 9 restart persistence | the leak continues after restart (simulation state restored) | PASS | 44930.1 -> 44859.7 L in 6 s |
| 9 restart persistence | connected tanks keep their water (A+B) | PASS | A+B 52964 -> 52966 L (A 28495->28047, B 24469->24919) |
| 9 restart persistence | pipe contents are saved | PASS | pipe 197.05 -> 195.936 L |
| 9 restart persistence | mass accounting survives restart | PASS | drift=-7.150 L of 497071 L |
| stability | no exceptions from the mod in the server logs | PASS | 0 error lines |
