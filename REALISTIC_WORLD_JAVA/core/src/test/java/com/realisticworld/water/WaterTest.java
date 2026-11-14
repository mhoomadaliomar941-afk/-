package com.realisticworld.water;

import org.junit.Test;

import java.util.ArrayList;
import java.util.HashMap;
import java.util.List;
import java.util.Map;

import static org.junit.Assert.*;

/** Ported from tests/water.test.mjs, including the mandatory analytical leak test. */
public class WaterTest {
    static final Bodies.Env NO_LOSS = new Bodies.Env(0, 1, 0, false, 0, 1); // no evaporation, not frozen
    static final Bodies.Env CALM = new Bodies.Env(15, 0.6, 2, false, 0, 0.5);

    /** Tiny voxel world for the scanner: solid below y=64, air above unless set. */
    static final class Voxels {
        final Map<Long, TankScan.Cell> m = new HashMap<>();

        void set(int x, int y, int z, TankScan.Cell c) {
            m.put(WaterPhysics.pack(x, y, z), c);
        }

        TankScan.Cell get(int x, int y, int z) {
            TankScan.Cell c = m.get(WaterPhysics.pack(x, y, z));
            return c != null ? c : y < 64 ? TankScan.Cell.SOLID : TankScan.Cell.AIR;
        }
    }

    /** Walled 5x5 tank (inner x,z 0..4), floor y=64, water y=65..68, walls to 69. */
    static void buildTank(Voxels w) {
        for (int x = -1; x <= 5; x++) for (int z = -1; z <= 5; z++) w.set(x, 64, z, TankScan.Cell.SOLID);
        for (int y = 65; y <= 69; y++) {
            for (int x = -1; x <= 5; x++) for (int z = -1; z <= 5; z++) {
                boolean wall = x == -1 || x == 5 || z == -1 || z == 5;
                if (wall) w.set(x, y, z, TankScan.Cell.SOLID);
                else if (y <= 68) w.set(x, y, z, TankScan.Cell.SOURCE);
            }
        }
    }

    static TankScan scan(Voxels w) {
        TankScan s = new TankScan(w::get, 2, 66, 2);
        while (!s.step(16)) { }
        return s;
    }

    static Bodies.Tank tank(TankScan.Shape s) {
        Bodies.Tank t = new Bodies.Tank();
        t.id = "t1";
        t.baseY = s.baseY;
        t.layers = s.layers;
        t.columns = s.columns;
        t.openTop = s.openTop;
        t.openings = s.openings;
        t.valves = s.valves;
        t.volume = s.cells;
        return t;
    }

    @Test
    public void pressureAndTorricelli() {
        assertEquals(98.1, WaterPhysics.pressureKPa(10), 1e-9);
        assertEquals(0.6 * Math.sqrt(2 * 9.81 * 2.5), WaterPhysics.sideFlow(69, 66, 1), 1e-12);
        assertEquals(0, WaterPhysics.sideFlow(66, 66, 1), 0);
        assertTrue(WaterPhysics.sideFlow(66.5, 66, 1) < WaterPhysics.sideFlow(67, 66, 1));
        assertTrue(WaterPhysics.pipeFlow(70, 65, 66, 65, 0.25) > 0 && WaterPhysics.pipeFlow(66, 65, 70, 65, 0.25) < 0);
        assertEquals(0, WaterPhysics.pipeFlow(64, 65, Double.NEGATIVE_INFINITY, 60, 0.25), 0);
    }

    @Test
    public void levelAndVolumeAreInverses() {
        int[] layers = {4, 9, 9, 16};
        for (double v : new double[] {0, 2, 4, 7.5, 13, 22, 38}) {
            assertEquals(v, WaterPhysics.volumeBelow(layers, 10, WaterPhysics.levelFromVolume(layers, 10, v)), 1e-9);
        }
    }

    @Test
    public void packedPositionsRoundTrip() {
        for (int[] p : new int[][] {{0, 0, 0}, {-1, 64, -1}, {29999999, 255, -29999999}, {-12345, 3, 678}}) {
            long k = WaterPhysics.pack(p[0], p[1], p[2]);
            assertArrayEquals(p, new int[] {WaterPhysics.unpackX(k), WaterPhysics.unpackY(k), WaterPhysics.unpackZ(k)});
        }
    }

    @Test
    public void scanFindsTankOpeningsValvesPipesAndHeadroom() {
        Voxels w = new Voxels();
        buildTank(w);
        TankScan.Shape s = scan(w).result();
        assertEquals(100, s.cells);
        assertArrayEquals("4 water layers + 1 empty inside the walls", new int[] {25, 25, 25, 25, 25}, s.layers);
        assertEquals(68, s.waterTop);
        assertTrue(s.openings.isEmpty());
        w.set(-1, 66, 2, TankScan.Cell.AIR);
        w.set(3, 64, 3, TankScan.Cell.AIR);
        w.set(5, 67, 1, TankScan.Cell.VALVE_CLOSED);
        w.set(2, 65, 5, TankScan.Cell.PIPE);
        w.set(1, 64, 1, TankScan.Cell.DRAIN);
        TankScan.Shape s2 = scan(w).result();
        List<String> kinds = new ArrayList<>();
        for (TankScan.Opening o : s2.openings) kinds.add(o.kind + "@" + o.x + "," + o.y + "," + o.z + "/" + o.bottom);
        java.util.Collections.sort(kinds);
        assertEquals("[drain@1,64,1/65, floor@3,64,3/65, hole@-1,66,2/66]", kinds.toString());
        for (TankScan.Opening o : s2.openings) if (o.kind.equals("hole")) assertArrayEquals(new int[] {-1, 0}, new int[] {o.dx, o.dz});
        assertEquals(1, s2.valves.size());
        assertEquals(1, s2.pipes.size());
    }

    @Test
    public void naturalWaterIsRefusedAndUnloadedAborts() {
        Voxels w = new Voxels();
        for (int x = 0; x < 30; x++) for (int z = 0; z < 30; z++) w.set(x, 64, z, TankScan.Cell.SOURCE);
        TankScan big = new TankScan(w::get, 5, 64, 5, 500);
        while (!big.step(100)) { }
        assertEquals(TankScan.Status.TOO_BIG, big.status());
        Voxels w2 = new Voxels();
        buildTank(w2);
        w2.set(5, 66, 2, TankScan.Cell.UNLOADED);
        assertEquals(TankScan.Status.UNLOADED, scan(w2).status());
    }

    @Test
    public void mandatoryLeakMatchesAnalyticalSolution() {
        Voxels w = new Voxels();
        buildTank(w);
        w.set(-1, 66, 2, TankScan.Cell.AIR);
        Bodies.Tank t = tank(scan(w).result());
        assertEquals(69, t.level(), 1e-9);
        assertEquals("initial outflow", 0.6 * Math.sqrt(2 * 9.81 * 2.5), t.outflow(), 1e-9);
        List<Double> levels = new ArrayList<>();
        double poured = 0;
        for (int s = 0; s < 120; s++) {
            Bodies.Result r = Bodies.stepTank(t, 1, NO_LOSS);
            for (Map.Entry<Long, Double> e : r.pours.entrySet()) {
                assertEquals("lands just outside the hole", com.realisticworld.util.MathUtil.columnKey(-2, 2), (long) e.getKey());
                poured += e.getValue();
            }
            levels.add(t.level());
        }
        for (int i = 1; i < levels.size(); i++) assertTrue(levels.get(i) <= levels.get(i - 1) + 1e-12);
        assertEquals("stops at the hole", 66, t.level(), 1e-6);
        assertEquals(25, t.volume, 1e-6);
        assertEquals("mass conserved", 75, poured, 1e-6);
        double a = 25;
        double t1 = 2 * a / (0.6 * Math.sqrt(2 * 9.81)) * (Math.sqrt(2.5) - Math.sqrt(0.5));
        double t2 = 2 * a / (0.6 * Math.sqrt(9.81)) * (1 / Math.sqrt(Bodies.RUNOFF_FILM) - 1);
        int tEmpty = 0;
        while (levels.get(tEmpty) >= 66 + 1e-6) tEmpty++;
        int t67 = 0;
        while (levels.get(t67) > 67) t67++;
        assertEquals("drained to the hole at the analytical time", t1 + t2, tEmpty + 1, 0.05 * (t1 + t2));
        assertEquals("hole uncovered at the analytical time", t1, t67 + 1, 1.5);
        assertTrue(Bodies.stepTank(t, 10, NO_LOSS).pours.isEmpty());
    }

    @Test
    public void drainsValvesRainAndFreezing() {
        Voxels w = new Voxels();
        buildTank(w);
        w.set(1, 64, 1, TankScan.Cell.DRAIN);
        Bodies.Tank t = tank(scan(w).result());
        Bodies.Result r = Bodies.stepTank(t, 5, NO_LOSS);
        assertTrue(r.pours.isEmpty() && r.lost > 0);
        t.openings.get(0).open = false;
        double v = t.volume;
        Bodies.stepTank(t, 5, NO_LOSS);
        assertEquals(v, t.volume, 0);
        t.openings.get(0).open = true;
        Bodies.stepTank(t, 600, NO_LOSS);
        assertTrue(t.volume < 1e-6);

        Voxels w2 = new Voxels();
        buildTank(w2);
        Bodies.Tank t2 = tank(scan(w2).result());
        t2.volume = 50;
        Bodies.stepTank(t2, 100, new Bodies.Env(15, 0.6, 2, true, 0.5, 0.5));
        assertTrue(t2.volume > 50);
        t2.volume = 50;
        Bodies.stepTank(t2, 1000, new Bodies.Env(-10, 0.2, 5, false, 0, 0.5));
        assertTrue(t2.frozen);
        assertEquals(50, t2.volume, 0);
        t2.volume = 125;
        Bodies.Result over = Bodies.stepTank(t2, 10, new Bodies.Env(15, 0.6, 2, true, 1, 0.5));
        assertTrue(over.overflow > 0);
        assertEquals(125, t2.volume, 0);
    }

    @Test
    public void communicatingVessels() {
        Voxels w = new Voxels();
        buildTank(w);
        Bodies.Tank a = tank(scan(w).result());
        Bodies.Tank b = tank(scan(w).result());
        a.volume = 100;
        b.volume = 20;
        Bodies.Pipe p = new Bodies.Pipe();
        p.startY = 65;
        p.endY = 65;
        double moved = 0;
        for (int s = 0; s < 300; s++) moved += Bodies.stepPipeLink(a, p, b, 65, 1);
        assertEquals(120, a.volume + b.volume, 1e-9);
        assertEquals(a.level(), b.level(), 0.02);
        assertEquals(40, moved, 0.5);
    }

    @Test
    public void pipeTracing() {
        Voxels w = new Voxels();
        for (int x = 0; x < 6; x++) w.set(x, 65, 0, TankScan.Cell.PIPE);
        w.set(6, 65, 0, TankScan.Cell.DRAIN);
        TankScan.PipeEnd e = TankScan.tracePipe(w::get, new int[] {0, 65, 0}, (x, y, z) -> false, 64);
        assertArrayEquals(new int[] {5, 65, 0}, e.end);
        assertEquals("drain", e.outKind);
        assertEquals(6, e.length);
    }

    // ---- terrain ----

    static Basin.HeightFn bowl(Map<Long, Basin.Column> extra) {
        return (x, z) -> {
            Basin.Column c = extra.get(com.realisticworld.util.MathUtil.columnKey(x, z));
            if (c != null) return c;
            int d = Math.max(Math.abs(x), Math.abs(z));
            return new Basin.Column(d <= 4 ? 60 + d : 65, false, "minecraft:grass");
        };
    }

    @Test
    public void basinDescendsFillsSpillsAndJoinsWater() {
        Basin.Point m = Basin.descend(3, 2, bowl(new HashMap<>()));
        assertArrayEquals(new int[] {0, 0}, new int[] {m.x, m.z});
        Basin.Fill f = Basin.fill(0, 0, 20, bowl(new HashMap<>()), 512, 32);
        double stored = 0;
        for (Basin.PoolCol c : f.cols) stored += Math.max(0, f.level - c.base);
        assertEquals(20, stored, 1e-9);
        assertNull(f.spill);
        Map<Long, Basin.Column> notch = new HashMap<>();
        notch.put(com.realisticworld.util.MathUtil.columnKey(5, 0), new Basin.Column(62, false, "minecraft:stone"));
        Basin.Fill over = Basin.fill(0, 0, 500, bowl(notch), 512, 32);
        assertArrayEquals(new int[] {5, 0}, over.spill);
        assertEquals(64, over.level, 1e-9);
        assertEquals(500, over.used + over.leftover, 1e-9);
        Map<Long, Basin.Column> lake = new HashMap<>();
        lake.put(com.realisticworld.util.MathUtil.columnKey(1, 0), new Basin.Column(60, true, "minecraft:water"));
        assertTrue(Basin.fill(0, 0, 50, bowl(lake), 512, 32).joinedWater);
    }

    @Test
    public void flatGroundSheetAndSizeLimit() {
        Basin.HeightFn flat = (x, z) -> new Basin.Column(64, false, "minecraft:grass");
        Basin.Fill f = Basin.fill(0, 0, 10, flat, 512, 32);
        assertFalse(f.hitLimit);
        double stored = 0;
        for (Basin.PoolCol c : f.cols) stored += f.level - c.base;
        assertEquals(10, stored, 1e-9);
        assertTrue(f.level - 64 >= Basin.MIN_FILM - 1e-9 && f.cols.size() <= 100);
        assertTrue(Basin.fill(0, 0, 1e6, flat, 100, 32).hitLimit);
    }

    @Test
    public void poolSoaksEvaporatesFreezesAndRegrows() {
        Basin.Fill f = Basin.fill(0, 0, 10, bowl(new HashMap<>()), 512, 32);
        java.util.function.Supplier<Bodies.Pool> mk = () -> {
            Bodies.Pool p = new Bodies.Pool();
            p.id = "p";
            p.setCols(f.cols);
            p.volume = 10;
            p.limit = f.nextBase;
            return p;
        };
        Bodies.Pool dry = mk.get();
        Bodies.Result r = Bodies.stepPool(dry, 60, new Bodies.Env(15, 0.6, 2, false, 0, 0.2));
        assertTrue(dry.volume < 10 && r.absorbed > 0);
        Bodies.Pool wet = mk.get();
        Bodies.stepPool(wet, 60, new Bodies.Env(15, 0.6, 2, false, 0, 1));
        assertTrue(wet.volume > dry.volume);
        Bodies.Pool frozen = mk.get();
        Bodies.stepPool(frozen, 60, new Bodies.Env(-10, 0.6, 2, false, 0, 0.2));
        assertEquals(10, frozen.volume, 0);
        Bodies.Pool grow = mk.get();
        assertTrue(Bodies.stepPool(grow, 600, new Bodies.Env(15, 0.6, 2, true, 1, 1)).regrow);
    }

    @Test
    public void absentLeakingTankSettlesInOneStep() {
        Voxels w = new Voxels();
        buildTank(w);
        w.set(-1, 67, 2, TankScan.Cell.AIR);
        Bodies.Tank t = tank(scan(w).result());
        Bodies.Result r = Bodies.catchUp(t, 3600, NO_LOSS);
        assertEquals(67, t.level(), 1e-6);
        double poured = 0;
        for (double v : r.pours.values()) poured += v;
        assertEquals(100, poured + t.volume, 1e-6);
    }

    @Test
    public void bodiesRoundTripThroughSavedData() {
        Voxels w = new Voxels();
        buildTank(w);
        w.set(-1, 66, 2, TankScan.Cell.AIR);
        w.set(2, 65, 5, TankScan.Cell.PIPE);
        Bodies.Tank t = tank(scan(w).result());
        Bodies.Pipe p = new Bodies.Pipe();
        p.x = 2; p.y = 65; p.z = 5; p.startY = 65; p.endY = 65; p.outKind = "air"; p.out = new int[] {2, 65, 6};
        t.pipes.add(p);
        t.shownTop = 68;
        Bodies.stepTank(t, 3, NO_LOSS);
        Bodies.Tank back = Bodies.Tank.fromMap(com.realisticworld.core.DataStore.deepCopy(t.toMap()));
        assertEquals(t.toMap(), back.toMap());
        Bodies.Pool pool = new Bodies.Pool();
        pool.id = "p1";
        pool.setCols(Basin.fill(0, 0, 10, bowl(new HashMap<>()), 512, 32).cols);
        pool.volume = 10;
        pool.shownWater.add(WaterPhysics.pack(1, 60, 1));
        Bodies.Pool pb = Bodies.Pool.fromMap(com.realisticworld.core.DataStore.deepCopy(pool.toMap()));
        assertEquals(pool.toMap(), pb.toMap());
    }
}
