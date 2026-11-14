package com.realisticworld;

import com.realisticworld.core.DataStore;
import com.realisticworld.core.Engine;
import com.realisticworld.core.SelfTest;
import com.realisticworld.sim.Regions;
import com.realisticworld.water.Bodies;
import com.realisticworld.water.WaterSystem;
import org.junit.Test;

import java.util.ArrayList;
import java.util.Arrays;
import java.util.HashSet;
import java.util.List;
import java.util.Map;
import java.util.Set;

import static com.realisticworld.FakeWorld.*;
import static org.junit.Assert.*;

/**
 * The mandatory water scenario through the whole Engine in a voxel world
 * (Java counterpart of tests/waterWorld.test.mjs): build a tank, fill it,
 * break a wall block, watch the level fall and stop at the hole, water
 * collect in a pit, save and reload, and check no player block was replaced.
 */
public class WaterWorldTest {
    static void run(Engine e, int ticks) {
        for (int i = 0; i < ticks; i++) e.tick();
    }

    static FakeWorld build() {
        FakeWorld w = new FakeWorld();
        w.players.add(new Regions.PlayerPos(0, 2, 12));
        for (int y = 71; y <= 75; y++) {
            for (int x = -1; x <= 5; x++) for (int z = -1; z <= 5; z++) {
                boolean wall = x == -1 || x == 5 || z == -1 || z == 5;
                if (wall) w.set(x, y, z, STONE);
                else if (y <= 74) w.set(x, y, z, WATER);
            }
        }
        for (int x = -9; x <= -5; x++) for (int z = 0; z <= 4; z++) for (int y : new int[] {69, 70}) w.set(x, y, z, AIR);
        w.set(-7, 69, 2, "minecraft:chest");
        for (int x = 100; x < 170; x++) for (int z = 100; z < 170; z++) w.set(x, 70, z, WATER);
        for (int y = 71; y <= 74; y++) {
            for (int x = 9; x <= 13; x++) for (int z = -1; z <= 3; z++) {
                boolean wall = x == 9 || x == 13 || z == -1 || z == 3;
                if (wall) w.set(x, y, z, STONE);
                else if (y <= 73) w.set(x, y, z, WATER);
            }
        }
        w.set(13, 71, 1, "rw:valve_closed");
        return w;
    }

    @Test
    public void mandatoryTankLeakScenario() {
        FakeWorld w = build();
        DataStore store = new DataStore();
        Engine e = new Engine(w, store);
        e.weather.force("CLEAR", 1_000_000, 0); // keep rain out of the measurements
        run(e, 60);
        w.recording = true;

        // 1. Breaking a wall block next to the water finds the tank.
        w.set(-1, 72, 2, AIR);
        e.water.onBlockRemoved(0, -1, 72, 2);
        run(e, 10);
        Bodies.Tank tank = e.water.tankAt(0, 0, 72, 0);
        assertNotNull("tank found", tank);
        Map<String, Object> d = e.water.describeBody(tank);
        assertEquals(125_000L, d.get("capacityLiters"));
        assertTrue(((Number) d.get("outflowLps")).doubleValue() > 0);

        // 2. The level falls layer by layer and stops at the hole.
        run(e, 20 * 40);
        int mid = w.count(0, 4, 71, 74, 0, 4, WATER);
        assertTrue("after 40 s: " + mid, mid < 100 && mid >= 25);
        run(e, 20 * 120);
        assertEquals("no water above the hole", 0, w.count(0, 4, 72, 74, 0, 4, WATER));
        assertEquals("the layer below the hole stays", 25, w.count(0, 4, 71, 71, 0, 4, WATER));
        long left = (Long) e.water.describeBody(tank).get("liters");
        assertTrue("left " + left, left > 24000 && left <= 25000);

        // 3. The escaped water collects in the pit; the chest is untouched.
        assertTrue("pit water " + w.count(-9, -5, 69, 70, 0, 4, WATER), w.count(-9, -5, 69, 70, 0, 4, WATER) >= 40);
        assertEquals("minecraft:chest", w.typeAt(-7, 69, 2));
        Bodies.Pool pool = e.water.poolAt(0, -7, 1);
        assertNotNull(pool);
        double total = pool.volume + pool.statAbsorbed + pool.statEvaporated;
        assertTrue("mass balance " + total, total > 74 && total < 76);

        // 4. Closed valve, then opened by hand.
        assertTrue(e.water.requestScan(0, 11, 72, 1, "gauge"));
        run(e, 10);
        Bodies.Tank vt = e.water.tankAt(0, 11, 72, 1);
        Map<String, Object> vd = e.water.describeBody(vt);
        assertEquals(27_000L, vd.get("liters"));
        assertEquals(36_000L, vd.get("capacityLiters"));
        assertEquals(0, vd.get("openings"));
        assertEquals(1, vd.get("closedValves"));
        w.set(13, 71, 1, "rw:valve_open");
        e.water.onValve(0, 13, 71, 1);
        run(e, 20 * 120);
        assertTrue("left behind an open valve: " + e.water.describeBody(vt).get("liters"),
            e.water.bodies().get(vt.id) == null || (Long) e.water.describeBody(e.water.bodies().get(vt.id)).get("liters") < 2000);
        assertEquals(0, w.count(10, 12, 71, 73, 0, 2, WATER));

        // 5. Natural water is never drained.
        int lake = w.count(100, 169, 70, 70, 100, 169, WATER);
        w.set(99, 70, 120, AIR);
        e.water.onBlockRemoved(0, 99, 70, 120);
        run(e, 20 * 30);
        assertEquals(lake, w.count(100, 169, 70, 70, 100, 169, WATER));
        assertNull(e.water.tankAt(0, 100, 70, 120));
        assertTrue(e.water.statNatural > 0);

        // 6. Only air, water, ice or puddles were ever replaced by the mod.
        Set<String> allowed = new HashSet<>(Arrays.asList(AIR, WATER, FLOWING, "minecraft:ice", "rw:puddle", "minecraft:tallgrass"));
        int byMod = 0;
        for (String[] c : w.changes) {
            boolean byTest = (c[0].equals("-1") && c[1].equals("72") && c[2].equals("2")) || (c[0].equals("13") && c[1].equals("71")) || (c[0].equals("99") && c[1].equals("70"));
            if (byTest) continue;
            byMod++;
            assertTrue("replaced " + c[3] + " at " + c[0] + "," + c[1] + "," + c[2], allowed.contains(c[3]));
        }
        assertTrue(byMod > 50);
        assertEquals("tank walls stand", 7 * 7 * 5 - 25 * 5 - 1, w.count(-1, 5, 71, 75, -1, 5, STONE));

        // 7. Whole layers: every water removal inside the first tank took 25 blocks at one height.
        List<Integer> removedYs = new ArrayList<>();
        for (FakeWorld.Fill f : w.fills) {
            if (!f.to.equals(AIR) || !f.from.contains(WaterSystem.Block.WATER) || f.changed.isEmpty()) continue;
            int[] c0 = f.changed.get(0);
            if (c0[0] < 0 || c0[0] > 4 || c0[2] < 0 || c0[2] > 4) continue;
            assertEquals(25, f.changed.size());
            for (int[] c : f.changed) assertEquals(c0[1], c[1]);
            removedYs.add(c0[1]);
        }
        assertEquals(Arrays.asList(74, 73, 72), removedYs);

        // 8. Save and reopen: tank and pool come back, nothing doubled.
        int blocksBefore = w.count(-12, 5, 66, 75, -3, 8, WATER);
        e.saveNow(true);
        DataStore reopened = new DataStore();
        reopened.restore(store.snapshot());
        Engine e2 = new Engine(w, reopened);
        run(e2, 60);
        Bodies.Tank tank2 = e2.water.tankAt(0, 0, 71, 0);
        assertNotNull(tank2);
        long left2 = (Long) e2.water.describeBody(tank2).get("liters");
        assertTrue("after reload " + left2, left2 > 24000 && left2 <= 25000);
        assertNotNull(e2.water.poolAt(0, -7, 1));
        assertEquals(blocksBefore, w.count(-12, 5, 66, 75, -3, 8, WATER), 2);
        assertEquals(e.scheduler.ticks(), e2.scheduler.ticks() - 60);

        // 9. Patching the hole: rescan finds no opening.
        w.set(-1, 72, 2, STONE);
        e2.water.onBlockPlaced(0, -1, 72, 2);
        run(e2, 20);
        assertEquals(0, e2.water.describeBody(e2.water.tankAt(0, 0, 71, 0)).get("openings"));

        // 10. Refilling never replaces a block the player put inside the tank.
        w.set(2, 72, 2, "minecraft:glass");
        Bodies.Tank sealed = e2.water.tankAt(0, 0, 71, 0);
        sealed.volume = 75; // e.g. filled through a pipe
        run(e2, 20);
        assertEquals("minecraft:glass", w.typeAt(2, 72, 2));
        assertEquals("layers 72 and 73 refilled around the glass", 24 + 25, w.count(0, 4, 72, 73, 0, 4, WATER));
    }

    @Test
    public void pipeJoinedTanksLevelOutWithExactMassBalance() {
        FakeWorld w = new FakeWorld();
        w.surface = (x, z) -> 64;
        w.players.add(new Regions.PlayerPos(0, 8, 8));
        tank3(w, 0, 68);
        tank3(w, 6, 65);
        for (int x : new int[] {3, 4, 5}) w.set(x, 65, 1, "rw:pipe");
        Engine e = new Engine(w, new DataStore());
        e.weather.force("CLEAR", 1_000_000, 0);
        e.water.requestScan(0, 1, 66, 1, "gauge");
        run(e, 40);
        Bodies.Tank a = e.water.tankAt(0, 1, 65, 1);
        Bodies.Tank b = e.water.tankAt(0, 7, 65, 1);
        assertNotNull("pipe led to the second tank", b);
        run(e, 20 * 60);
        assertEquals(a.level(), b.level(), 0.05);
        assertEquals(45, a.volume + b.volume + a.statEvaporated + b.statEvaporated, 1e-6);
        assertTrue(w.count(6, 8, 65, 70, 0, 2, WATER) >= 18);
    }

    static void tank3(FakeWorld w, int x0, int waterTop) {
        for (int y = 65; y <= 70; y++) {
            for (int x = x0 - 1; x <= x0 + 3; x++) for (int z = -1; z <= 3; z++) {
                boolean wall = x == x0 - 1 || x == x0 + 3 || z == -1 || z == 3;
                if (wall) w.set(x, y, z, STONE);
                else if (y <= waterTop) w.set(x, y, z, WATER);
            }
        }
    }

    @Test
    public void rainOnSoakedGroundFormsPuddlesUnlessSwitchedOff() {
        for (boolean on : new boolean[] {true, false}) {
            FakeWorld w = new FakeWorld();
            w.surface = (x, z) -> Math.abs(x - 8) <= 2 && Math.abs(z - 8) <= 2 ? 62 : 64;
            w.players.add(new Regions.PlayerPos(0, 8, 8));
            Engine e = new Engine(w, new DataStore());
            e.config.set(WaterSystem.RAIN_PUDDLES, on);
            run(e, 200);
            for (Regions.Active r : e.regions.active().values()) {
                List<Object> c = com.realisticworld.weather.ClimateSystem.data(r);
                if (c != null) c.set(0, 950);
            }
            e.weather.force("HEAVY_RAIN", 1_000_000, e.scheduler.ticks());
            run(e, 20 * 60);
            int pools = ((Number) e.water.describe().get("pools")).intValue();
            if (on) assertTrue("pools " + pools + ", runoff " + e.water.statRunoff, pools > 0 && e.water.statRunoff > 0);
            else assertEquals(0, pools);
        }
    }

    @Test
    public void selfTestPassesTwiceInARunningEngine() {
        FakeWorld w = new FakeWorld();
        w.players.add(new Regions.PlayerPos(0, 8, 8));
        Engine e = new Engine(w, new DataStore());
        run(e, 40);
        e.selfTest();
        run(e, 5);
        for (SelfTest.Result r : e.selfTest()) assertTrue(r.name + ": " + r.detail, r.ok);
        assertEquals(5, e.selfTest().size());
    }

    @Test
    public void settingsAndClockSurviveReloadAndPauseWorks() {
        FakeWorld w = new FakeWorld();
        w.players.add(new Regions.PlayerPos(0, 8, 8));
        DataStore store = new DataStore();
        Engine e = new Engine(w, store);
        e.config.set("profile", "LOW");
        e.config.set("enabled", false);
        run(e, 100);
        e.saveNow(true);
        Engine e2 = new Engine(w, store);
        assertEquals("LOW", e2.perf.profile().name);
        assertTrue(e2.scheduler.paused());
        assertEquals(100, e2.scheduler.ticks());
        assertTrue(e2.loadWarnings.isEmpty());
    }
}
