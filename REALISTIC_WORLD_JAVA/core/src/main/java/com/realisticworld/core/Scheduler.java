package com.realisticworld.core;

import com.realisticworld.util.MathUtil;

import java.util.ArrayDeque;
import java.util.ArrayList;
import java.util.Collection;
import java.util.Deque;
import java.util.HashSet;
import java.util.Iterator;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;
import java.util.Set;
import java.util.function.DoubleSupplier;
import java.util.function.LongSupplier;

/**
 * Central engine loop. Each server tick it runs the systems that are due, most
 * overdue first (ties: least recently run), until the profile's time budget is
 * used; the rest wait for the next tick. A bounded queue holds deferred work.
 * A system that keeps throwing is switched off ("faulted") instead of breaking
 * the others.
 */
public final class Scheduler {
    public static final int MAX_CONSECUTIVE_ERRORS = 5;

    /** What a system gets each time it runs. */
    public static final class Context {
        public final long tick;
        public final long deltaTicks;
        private final DoubleSupplier timeLeftMs;

        public Context(long tick, long deltaTicks, DoubleSupplier timeLeftMs) {
            this.tick = tick;
            this.deltaTicks = deltaTicks;
            this.timeLeftMs = timeLeftMs;
        }

        public double timeLeftMs() {
            return timeLeftMs.getAsDouble();
        }
    }

    /** A simulation system. update returns the number of operations done. */
    public interface SimSystem {
        String id();

        String name();

        int intervalTicks();

        default boolean scalable() {
            return false;
        }

        /** Core systems cannot be switched off from the UI. */
        default boolean core() {
            return false;
        }

        default void start() {}

        default void stop() {}

        int update(Context ctx);

        default Map<String, Object> describe() {
            return new LinkedHashMap<>();
        }
    }

    /** Deferred work: returns true when finished, false to be called again later. */
    public interface Task {
        boolean run(DoubleSupplier timeLeftMs);
    }

    /** Read-only view of a system for the UI and tests. */
    public static final class Info {
        public final String id;
        public final String name;
        public final boolean enabled;
        public final boolean faulted;
        public final boolean core;
        public final String state;
        public final int intervalTicks;
        public final long runs;
        public final long errors;
        public final String lastError;
        public final int lastOps;
        public final double avgMs;
        public final Map<String, Object> details;

        Info(Entry e, int interval) {
            id = e.system.id();
            name = e.system.name();
            enabled = e.enabled;
            faulted = e.faulted;
            core = e.system.core();
            state = e.faulted ? "FAULTED" : e.enabled ? "RUNNING" : "OFF";
            intervalTicks = interval;
            runs = e.runs;
            errors = e.errors;
            lastError = e.lastError;
            lastOps = e.lastOps;
            avgMs = MathUtil.round(Double.isNaN(e.avgMs) ? 0 : e.avgMs, 3);
            Map<String, Object> d;
            try {
                d = e.system.describe();
            } catch (RuntimeException ex) {
                d = new LinkedHashMap<>();
            }
            details = d;
        }
    }

    private static final class Entry {
        final SimSystem system;
        boolean enabled;
        boolean faulted;
        long nextDue;
        long lastRun = -1;
        long runSeq;
        long runs;
        long errors;
        int consecutiveErrors;
        int lastOps;
        double avgMs = Double.NaN;
        String lastError = "";

        Entry(SimSystem s) {
            system = s;
        }
    }

    private static final class Queued {
        final String key;
        final String owner;
        final Task task;

        Queued(String key, String owner, Task task) {
            this.key = key;
            this.owner = owner;
            this.task = task;
        }
    }

    private final Performance perf;
    private final RwLog log;
    private final LongSupplier nanoClock;
    private final Map<String, Entry> systems = new LinkedHashMap<>();
    private final Deque<Queued> queue = new ArrayDeque<>();
    private final Set<String> queueKeys = new HashSet<>();
    private long ticks;
    private long runCounter;
    private boolean paused;
    private long deferredRuns;
    private long queueRejected;
    private long queueDone;
    private double lastTickMs;

    public Scheduler(Performance perf, RwLog log, LongSupplier nanoClock, long startTick) {
        this.perf = perf;
        this.log = log;
        this.nanoClock = nanoClock;
        this.ticks = Math.max(0, startTick);
    }

    public long ticks() {
        return ticks;
    }

    public boolean paused() {
        return paused;
    }

    public void pause() {
        paused = true;
    }

    public void resume() {
        paused = false;
    }

    public long deferredRuns() {
        return deferredRuns;
    }

    public long queueRejected() {
        return queueRejected;
    }

    public long queueDone() {
        return queueDone;
    }

    public int queueSize() {
        return queue.size();
    }

    public double lastTickMs() {
        return lastTickMs;
    }

    public void register(SimSystem s, boolean enabled) {
        if (systems.containsKey(s.id())) throw new IllegalStateException("system \"" + s.id() + "\" registered twice");
        if (s.intervalTicks() < 1) throw new IllegalArgumentException("system \"" + s.id() + "\" needs intervalTicks >= 1");
        systems.put(s.id(), new Entry(s));
        if (enabled) setEnabled(s.id(), true);
    }

    /** Switch a system on/off. Switching on also clears a fault. */
    public boolean setEnabled(String id, boolean enabled) {
        Entry e = systems.get(id);
        if (e == null) return false;
        if (enabled && (!e.enabled || e.faulted)) {
            e.faulted = false;
            e.consecutiveErrors = 0;
            try {
                e.system.start();
                e.enabled = true;
                e.nextDue = ticks;
            } catch (RuntimeException ex) {
                e.enabled = false;
                e.faulted = true;
                e.lastError = String.valueOf(ex.getMessage());
                log.error("system." + id, "failed to start", ex);
                return false;
            }
        } else if (!enabled && e.enabled) {
            e.enabled = false;
            try {
                e.system.stop();
            } catch (RuntimeException ex) {
                log.error("system." + id, "failed to stop cleanly", ex);
            }
            dropQueued(id);
        }
        return true;
    }

    /** Queue deferred work; false if the queue is full or the key is already queued. */
    public boolean enqueue(String key, String owner, Task task) {
        if (queueKeys.contains(key)) return false;
        if (queue.size() >= perf.profile().maxQueuedTasks) {
            queueRejected++;
            return false;
        }
        queue.addLast(new Queued(key, owner, task));
        queueKeys.add(key);
        return true;
    }

    private void dropQueued(String owner) {
        Iterator<Queued> it = queue.iterator();
        while (it.hasNext()) {
            Queued q = it.next();
            if (q.owner.equals(owner)) {
                it.remove();
                queueKeys.remove(q.key);
            }
        }
    }

    /** Run one engine tick. Call once per server tick. */
    public void tick() {
        long tick = ticks++;
        if (paused) return;
        final long start = nanoClock.getAsLong();
        final double budget = perf.profile().tickBudgetMs;
        DoubleSupplier timeLeft = () -> budget - (nanoClock.getAsLong() - start) / 1e6;

        List<Entry> due = new ArrayList<>();
        for (Entry e : systems.values()) if (e.enabled && !e.faulted && tick >= e.nextDue) due.add(e);
        due.sort((a, b) -> a.nextDue != b.nextDue ? Long.compare(a.nextDue, b.nextDue) : Long.compare(a.runSeq, b.runSeq));

        int ran = 0;
        for (Entry e : due) {
            // Always run at least one due system per tick so nothing starves.
            if (ran > 0 && timeLeft.getAsDouble() <= 0) {
                deferredRuns += due.size() - ran;
                break;
            }
            ran++;
            runSystem(e, tick, timeLeft);
        }

        while (!queue.isEmpty() && timeLeft.getAsDouble() > 0) {
            Queued q = queue.pollFirst();
            boolean done = true;
            try {
                done = q.task.run(timeLeft);
            } catch (RuntimeException ex) {
                log.error("queue." + q.owner, "task " + q.key + " failed", ex);
            }
            if (done) {
                queueKeys.remove(q.key);
                queueDone++;
            } else {
                queue.addLast(q);
            }
        }

        lastTickMs = (nanoClock.getAsLong() - start) / 1e6;
        perf.recordTick(lastTickMs);
    }

    private void runSystem(Entry e, long tick, DoubleSupplier timeLeft) {
        SimSystem s = e.system;
        long t0 = nanoClock.getAsLong();
        try {
            e.lastOps = s.update(new Context(tick, e.lastRun < 0 ? 0 : tick - e.lastRun, timeLeft));
            e.consecutiveErrors = 0;
        } catch (RuntimeException ex) {
            e.errors++;
            e.consecutiveErrors++;
            e.lastError = String.valueOf(ex);
            log.error("system." + s.id(), "update failed", ex);
            if (e.consecutiveErrors >= MAX_CONSECUTIVE_ERRORS) {
                e.faulted = true;
                log.error("system." + s.id(), "disabled after " + MAX_CONSECUTIVE_ERRORS + " errors in a row; re-enable it from the control center");
            }
        }
        double dt = (nanoClock.getAsLong() - t0) / 1e6;
        e.avgMs = MathUtil.ema(e.avgMs, dt, 0.1);
        e.runs++;
        e.runSeq = ++runCounter;
        e.lastRun = tick;
        e.nextDue = tick + (s.scalable() ? perf.scaledInterval(s.intervalTicks()) : s.intervalTicks());
    }

    public List<Info> list() {
        List<Info> out = new ArrayList<>();
        for (Entry e : systems.values()) {
            int interval = e.system.scalable() ? perf.scaledInterval(e.system.intervalTicks()) : e.system.intervalTicks();
            out.add(new Info(e, interval));
        }
        return out;
    }

    public Info info(String id) {
        for (Info i : list()) if (i.id.equals(id)) return i;
        return null;
    }

    public Collection<String> ids() {
        return systems.keySet();
    }
}
