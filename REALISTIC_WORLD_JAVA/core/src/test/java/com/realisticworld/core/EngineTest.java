package com.realisticworld.core;

import com.realisticworld.util.Rng;
import org.junit.Test;

import java.util.ArrayList;
import java.util.HashMap;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;
import java.util.concurrent.atomic.AtomicInteger;
import java.util.concurrent.atomic.AtomicLong;

import static org.junit.Assert.*;

/** Ported from tests/scheduler.test.mjs and tests/config.test.mjs. */
public class EngineTest {
    private static final RwLog.Sink QUIET = (l, s) -> {};

    /** Fake nanosecond clock: systems advance it to simulate work. */
    private final AtomicLong clock = new AtomicLong();

    private Scheduler scheduler(String profile, RwLog log) {
        Performance perf = new Performance();
        perf.setProfile(profile);
        return new Scheduler(perf, log, clock::get, 0);
    }

    private static Scheduler.SimSystem sys(String id, int interval, java.util.function.ToIntFunction<Scheduler.Context> fn) {
        return new Scheduler.SimSystem() {
            public String id() { return id; }
            public String name() { return id; }
            public int intervalTicks() { return interval; }
            public int update(Scheduler.Context ctx) { return fn.applyAsInt(ctx); }
        };
    }

    @Test
    public void systemsRunOnTheirInterval() {
        Scheduler s = scheduler("MEDIUM", new RwLog(QUIET));
        List<long[]> runs = new ArrayList<>();
        s.register(sys("a", 5, ctx -> { runs.add(new long[] {ctx.tick, ctx.deltaTicks}); return 0; }), true);
        for (int i = 0; i < 16; i++) s.tick();
        assertEquals(4, runs.size());
        assertArrayEquals(new long[] {0, 0}, runs.get(0));
        assertArrayEquals(new long[] {15, 5}, runs.get(3));
    }

    @Test
    public void budgetDefersWorkFairly() {
        Scheduler s = scheduler("LOW", new RwLog(QUIET)); // 2 ms budget
        Map<String, Integer> counts = new HashMap<>();
        for (String id : new String[] {"a", "b", "c"}) {
            counts.put(id, 0);
            s.register(sys(id, 1, ctx -> { counts.merge(id, 1, Integer::sum); clock.addAndGet(1_500_000); return 0; }), true);
        }
        for (int i = 0; i < 30; i++) s.tick();
        assertTrue(s.deferredRuns() > 0);
        int min = Integer.MAX_VALUE;
        int max = 0;
        for (int c : counts.values()) {
            min = Math.min(min, c);
            max = Math.max(max, c);
        }
        assertTrue("every system ran at least 15 times: " + counts, min >= 15);
        assertTrue("fair rotation: " + counts, max - min <= 1);
    }

    @Test
    public void failingSystemIsFaultedOthersContinue() {
        RwLog log = new RwLog(QUIET);
        Scheduler s = scheduler("MEDIUM", log);
        AtomicInteger good = new AtomicInteger();
        s.register(sys("bad", 1, ctx -> { throw new IllegalStateException("boom"); }), true);
        s.register(sys("good", 1, ctx -> good.incrementAndGet()), true);
        for (int i = 0; i < 10; i++) s.tick();
        assertEquals("FAULTED", s.info("bad").state);
        assertEquals(Scheduler.MAX_CONSECUTIVE_ERRORS, s.info("bad").errors);
        assertEquals(10, good.get());
        s.setEnabled("bad", true);
        assertEquals("RUNNING", s.info("bad").state);
    }

    @Test
    public void queueIsBoundedDedupedAndResumable() {
        Scheduler s = scheduler("LOW", new RwLog(QUIET));
        AtomicInteger steps = new AtomicInteger();
        assertTrue(s.enqueue("job", "t", left -> steps.incrementAndGet() >= 3));
        assertFalse("duplicate key", s.enqueue("job", "t", left -> true));
        for (int i = 0; i < Performance.LOW.maxQueuedTasks + 5; i++) s.enqueue("k" + i, "t", left -> true);
        assertEquals(Performance.LOW.maxQueuedTasks, s.queueSize());
        for (int i = 0; i < 5; i++) s.tick();
        assertEquals(3, steps.get());
        assertEquals(0, s.queueSize());
    }

    @Test
    public void pausedEngineAdvancesClockOnly() {
        Scheduler s = scheduler("MEDIUM", new RwLog(QUIET));
        AtomicInteger n = new AtomicInteger();
        s.register(sys("a", 1, ctx -> n.incrementAndGet()), true);
        s.pause();
        s.tick();
        s.tick();
        assertEquals(0, n.get());
        assertEquals(2, s.ticks());
        s.resume();
        s.tick();
        assertEquals(1, n.get());
    }

    @Test
    public void adaptiveThrottleRisesAndFalls() {
        Performance p = new Performance();
        for (int i = 0; i < 40; i++) p.recordTick(8);
        assertEquals(2, p.throttle());
        for (int i = 0; i < 400; i++) p.recordTick(8);
        assertEquals(Performance.MAX_THROTTLE, p.throttle());
        for (int i = 0; i < 120; i++) p.recordTick(0.2);
        assertEquals(1, p.throttle());
        p.setAdaptive(false);
        for (int i = 0; i < 80; i++) p.recordTick(8);
        assertEquals(1, p.throttle());
    }

    @Test
    public void configValidatesAndRoundTrips() {
        RwLog log = new RwLog(QUIET);
        Config c = new Config(log);
        c.defineSystem("regions", "Regions", true);
        assertFalse(c.set(Config.PROFILE, "TURBO").ok);
        assertTrue(c.set(Config.PROFILE, "LOW").ok);
        c.setSystemEnabled("regions", false);
        Map<String, Object> saved = c.save();
        Config c2 = new Config(log);
        c2.defineSystem("regions", "Regions", true);
        assertTrue(c2.load(saved).isEmpty());
        assertEquals("LOW", c2.get(Config.PROFILE));
        assertFalse(c2.isSystemEnabled("regions"));

        Map<String, Object> bad = new LinkedHashMap<>();
        Map<String, Object> values = new LinkedHashMap<>();
        values.put(Config.PROFILE, "TURBO");
        values.put(Config.ENABLED, "yes");
        values.put("bogus", 1);
        bad.put("schema", 1);
        bad.put("values", values);
        Config c3 = new Config(log);
        assertEquals(3, c3.load(bad).size());
        assertEquals("MEDIUM", c3.get(Config.PROFILE));
        assertTrue(c3.isDirty());
    }

    @Test
    public void rngMatchesTheBedrockSequence() {
        // mulberry32 from state 12345: exact values printed by the JavaScript version.
        Rng r = new Rng(12345);
        assertEquals(0.9797282677609473, r.nextDouble(), 0);
        assertEquals(0.3067522644996643, r.nextDouble(), 0);
    }
}
