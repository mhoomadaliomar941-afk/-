package com.realisticworld.sim;

import com.realisticworld.core.DataStore;
import com.realisticworld.core.Performance;
import com.realisticworld.core.RwLog;
import org.junit.Test;

import java.util.ArrayList;
import java.util.Collections;
import java.util.List;

import static org.junit.Assert.*;

/** Ported from tests/regions.test.mjs. */
public class RegionsTest {
    private static final RwLog LOG = new RwLog((l, s) -> {});

    static final class World {
        final DataStore store;
        final Performance perf = new Performance();
        final List<Regions.PlayerPos> players = new ArrayList<>();
        Regions.LoadedCheck loaded = (d, x, z) -> true;
        final Regions regions;

        World(DataStore store, String profile) {
            this.store = store;
            perf.setProfile(profile);
            players.add(new Regions.PlayerPos(0, 8, 8));
            regions = new Regions(store, LOG, perf, () -> players, (d, x, z) -> loaded.isLoaded(d, x, z));
        }

        World() {
            this(new DataStore(), "MEDIUM");
        }
    }

    @Test
    public void lodRingsAroundOnePlayer() {
        World w = new World();
        w.regions.update(100);
        assertEquals(25, w.regions.count(Regions.Tier.NEAR));
        assertEquals(81 - 25, w.regions.count(Regions.Tier.MEDIUM));
        assertEquals(169 - 81, w.regions.count(Regions.Tier.FAR));
        assertEquals(Regions.Tier.NEAR, w.regions.tierAt(0, 8, 8));
        assertEquals(Regions.Tier.MEDIUM, w.regions.tierAt(0, 48, 0));
        assertEquals(Regions.Tier.FAR, w.regions.tierAt(0, 96, 0));
        assertNull(w.regions.tierAt(0, 112, 0));
    }

    @Test
    public void unloadedChunksAreSkippedAndPickedUpLater() {
        World w = new World();
        boolean[] ready = {false};
        w.loaded = (d, x, z) -> x < 0 || ready[0];
        w.regions.update(0);
        for (Regions.Active a : w.regions.active().values()) assertTrue(a.cx < 0);
        ready[0] = true;
        for (int i = 1; i <= Regions.FULL_REFRESH_UPDATES; i++) w.regions.update(i * 20);
        assertEquals(169, w.regions.active().size());
    }

    @Test
    public void overlappingPlayersTakeTheMostDetailedTier() {
        World w = new World();
        w.players.clear();
        w.players.add(new Regions.PlayerPos(0, 0, 0));
        w.players.add(new Regions.PlayerPos(0, 80, 0));
        w.regions.update(1);
        assertEquals(Regions.Tier.NEAR, w.regions.tierAt(0, 80, 0));
        assertEquals(Regions.Tier.NEAR, w.regions.tierAt(0, 0, 0));
    }

    @Test
    public void returningRegionReportsAwayTime() {
        World w = new World();
        List<long[]> resumed = new ArrayList<>();
        w.regions.onResume((a, away) -> resumed.add(new long[] {a.cx, a.cz, away}));
        w.regions.update(100);
        w.players.set(0, new Regions.PlayerPos(0, 1600, 0));
        w.regions.update(200);
        w.players.set(0, new Regions.PlayerPos(0, 8, 8));
        w.regions.update(1300);
        long[] home = null;
        for (long[] r : resumed) if (r[0] == 0 && r[1] == 0) home = r;
        assertNotNull(home);
        assertEquals(1100, home[2]);
    }

    @Test
    public void recordsSurviveSaveAndReload() {
        World first = new World();
        first.regions.update(500);
        first.regions.tracker().stop();
        assertTrue(first.regions.flush(Integer.MAX_VALUE) > 0);
        World second = new World(first.store, "MEDIUM");
        List<Long> away = new ArrayList<>();
        second.regions.onResume((a, t) -> away.add(t));
        second.regions.update(900);
        assertEquals(169, away.size());
        for (long t : away) assertEquals(400, t);
        assertEquals(2, second.regions.get(0, 0, 0).visits);
    }

    @Test
    public void systemDataIsSavedWithTheRecord() {
        World first = new World();
        first.regions.update(1);
        Regions.Active a = first.regions.regionAt(0, 8, 8);
        a.record.data = new java.util.LinkedHashMap<>();
        a.record.data.put("c", new ArrayList<Object>(java.util.Arrays.asList(420, "plains", 70)));
        first.regions.markDirty(0, 0, 0);
        first.regions.flush(Integer.MAX_VALUE);
        World second = new World(first.store, "MEDIUM");
        assertEquals("[420, plains, 70]", String.valueOf(second.regions.get(0, 0, 0).data.get("c")));
    }

    @Test
    public void memoryStaysBoundedWhileTravelling() {
        World w = new World(new DataStore(), "LOW");
        List<Integer> visited = new ArrayList<>();
        for (int step = 0; step < 60; step++) {
            w.players.set(0, new Regions.PlayerPos(0, step * 160, 0));
            visited.add(step * 10);
            w.regions.update(step * 20);
            assertTrue("sectors " + w.regions.cachedSectors(), w.regions.cachedSectors() <= w.regions.maxSectors() + 2);
        }
        assertTrue(w.regions.evictions > 0 && w.regions.forcedSaves > 0);
        World fresh = new World(w.store, "LOW");
        for (int cx : visited.subList(0, 20)) assertNotNull("region " + cx, fresh.regions.get(0, cx, 0));
    }

    @Test
    public void idleUpdatesSkipTheRecompute() {
        World w = new World();
        w.regions.update(0);
        w.players.set(0, new Regions.PlayerPos(0, 15, 3));
        for (int i = 1; i < 5; i++) assertEquals(0, w.regions.update(i * 20));
        assertEquals(1, w.regions.recomputes);
        w.players.set(0, new Regions.PlayerPos(0, 17, 3));
        w.regions.update(200);
        assertEquals(2, w.regions.recomputes);
    }

    @Test
    public void corruptSectorStartsFresh() {
        World first = new World();
        first.regions.update(1);
        first.regions.flush(Integer.MAX_VALUE);
        for (String k : first.store.keys("reg/")) first.store.save(k, Collections.singletonMap("v", (Object) 99));
        World again = new World(first.store, "MEDIUM");
        again.regions.update(2);
        assertTrue(again.regions.corruptSectors > 0);
        assertEquals(25, again.regions.count(Regions.Tier.NEAR));
    }
}
