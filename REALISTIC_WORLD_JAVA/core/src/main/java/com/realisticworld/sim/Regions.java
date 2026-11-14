package com.realisticworld.sim;

import com.realisticworld.core.DataStore;
import com.realisticworld.core.Performance;
import com.realisticworld.core.RwLog;
import com.realisticworld.core.Scheduler;
import com.realisticworld.util.MathUtil;

import java.util.ArrayList;
import java.util.HashMap;
import java.util.HashSet;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;
import java.util.Set;
import java.util.function.Consumer;
import java.util.function.Supplier;

/**
 * Simulation regions and level of detail. A region is one chunk (16x16 block
 * columns). Around each player, regions get a tier by distance in chunks:
 * NEAR (full detail), MEDIUM (reduced), FAR (cheap stored-state updates).
 * Every region seen gets a small persistent record, grouped in 8x8-region
 * sectors (one store key each), loaded on demand and evicted from memory when
 * least recently used (saved first if changed). A region returning after a
 * gap tells listeners how many ticks it was away, for cheap catch-up.
 */
public final class Regions {
    public enum Tier { NEAR, MEDIUM, FAR }

    public static final int SECTOR_SIZE = 8;
    /** Full recompute at least this often (tracker updates; 1 update = 1 s). */
    public static final int FULL_REFRESH_UPDATES = 10;
    private static final int MAX_FORCED_SAVES = 8;
    private static final int RECORD_VERSION = 2;

    /** Persistent record of one region; `data` holds other systems' values. */
    public static final class Record {
        public final int dim;
        public final int cx;
        public final int cz;
        public long created;
        public long lastActive;
        public int visits;
        public Map<String, Object> data;

        Record(int dim, int cx, int cz, long created, long lastActive, int visits) {
            this.dim = dim;
            this.cx = cx;
            this.cz = cz;
            this.created = created;
            this.lastActive = lastActive;
            this.visits = visits;
        }
    }

    /** A region currently simulated. Same object while it stays active. */
    public static final class Active {
        public final int dim;
        public final int cx;
        public final int cz;
        public Tier tier;
        public Record record;
        /** Last simulation tick of systems that keep their own pace (climate, water). */
        public long lastSim = Long.MIN_VALUE;

        Active(int dim, int cx, int cz, Tier tier) {
            this.dim = dim;
            this.cx = cx;
            this.cz = cz;
            this.tier = tier;
        }
    }

    /** A player position: dimension id and block x/z. */
    public static final class PlayerPos {
        public final int dim;
        public final double x;
        public final double z;

        public PlayerPos(int dim, double x, double z) {
            this.dim = dim;
            this.x = x;
            this.z = z;
        }
    }

    /** Is the chunk around this block column loaded? */
    public interface LoadedCheck {
        boolean isLoaded(int dim, int blockX, int blockZ);
    }

    public interface ResumeListener {
        void resumed(Active region, long awayTicks);
    }

    private static final class Sector {
        final String key;
        final Map<Long, Record> regions = new HashMap<>();
        boolean dirty;
        long lastUsed;

        Sector(String key) {
            this.key = key;
        }
    }

    static long regionKey(int dim, int cx, int cz) {
        return MathUtil.columnKey(cx, cz) * 8 + (dim + 4); // dims -1, 0, 1 and a few modded ones
    }

    static String sectorKey(int dim, int cx, int cz) {
        return "reg/" + dim + "/" + Math.floorDiv(cx, SECTOR_SIZE) + "/" + Math.floorDiv(cz, SECTOR_SIZE);
    }

    // ------------------------------------------------------------ store

    private final DataStore store;
    private final RwLog log;
    private final Performance perf;
    private final Supplier<List<PlayerPos>> players;
    private final LoadedCheck loaded;
    private final Map<String, Sector> sectors = new HashMap<>();
    private int maxSectors;
    private long useCounter;
    public long sectorLoads;
    public long sectorSaves;
    public long evictions;
    public long forcedSaves;
    public long corruptSectors;

    // ------------------------------------------------------------ tracker

    private Map<Long, Active> active = new HashMap<>();
    private final List<ResumeListener> resumeListeners = new ArrayList<>();
    private final List<Consumer<Active>> enterListeners = new ArrayList<>();
    private final int[] counts = new int[3];
    private String signature = "";
    private int sinceFull;
    private long lastTick;
    public long resumedTotal;
    public long lastResumeAway;
    public long recomputes;
    public long skipped;

    public Regions(DataStore store, RwLog log, Performance perf, Supplier<List<PlayerPos>> players, LoadedCheck loaded) {
        this.store = store;
        this.log = log;
        this.perf = perf;
        this.players = players;
        this.loaded = loaded;
        setCapacity(perf.profile().maxCachedRegions);
    }

    public void setCapacity(int maxCachedRegions) {
        maxSectors = Math.max(4, (int) Math.ceil(maxCachedRegions / (double) (SECTOR_SIZE * SECTOR_SIZE)));
    }

    private Sector sector(int dim, int cx, int cz) {
        String key = sectorKey(dim, cx, cz);
        Sector s = sectors.get(key);
        if (s == null) {
            s = new Sector(key);
            Map<String, Object> saved = store.load(key);
            sectorLoads++;
            if (saved != null) {
                int v = DataStore.getInt(saved, "v", 0);
                if (v == 1 || v == RECORD_VERSION) {
                    for (Object rowObj : DataStore.getList(saved, "r")) {
                        if (!(rowObj instanceof List) || ((List<?>) rowObj).size() < 5) continue;
                        List<?> row = (List<?>) rowObj;
                        boolean ints = true;
                        for (int i = 0; i < 5; i++) ints &= row.get(i) instanceof Number;
                        if (!ints) continue;
                        Record r = new Record(dim, ((Number) row.get(0)).intValue(), ((Number) row.get(1)).intValue(),
                            ((Number) row.get(2)).longValue(), ((Number) row.get(3)).longValue(), ((Number) row.get(4)).intValue());
                        if (row.size() > 5 && row.get(5) instanceof Map) {
                            @SuppressWarnings("unchecked")
                            Map<String, Object> d = (Map<String, Object>) row.get(5);
                            r.data = d;
                        }
                        s.regions.put(regionKey(dim, r.cx, r.cz), r);
                    }
                } else {
                    corruptSectors++;
                    log.warn("regions", "sector " + key + " has an unknown format; starting it fresh");
                }
            }
            sectors.put(key, s);
        }
        s.lastUsed = ++useCounter;
        return s;
    }

    public Record get(int dim, int cx, int cz) {
        return sector(dim, cx, cz).regions.get(regionKey(dim, cx, cz));
    }

    /** Mark a region active now; returns ticks away (-1 if never seen before). */
    long activate(Active a, long tick) {
        Sector s = sector(a.dim, a.cx, a.cz);
        long key = regionKey(a.dim, a.cx, a.cz);
        Record r = s.regions.get(key);
        long away = -1;
        if (r != null) {
            away = Math.max(0, tick - r.lastActive);
            r.visits++;
            r.lastActive = tick;
        } else {
            r = new Record(a.dim, a.cx, a.cz, tick, tick, 1);
            s.regions.put(key, r);
        }
        a.record = r;
        s.dirty = true;
        return away;
    }

    void touch(int dim, int cx, int cz, long tick) {
        Sector s = sector(dim, cx, cz);
        Record r = s.regions.get(regionKey(dim, cx, cz));
        if (r != null && r.lastActive != tick) {
            r.lastActive = tick;
            s.dirty = true;
        }
    }

    /** After a system edits record.data. */
    public void markDirty(int dim, int cx, int cz) {
        Sector s = sectors.get(sectorKey(dim, cx, cz));
        if (s != null) s.dirty = true;
    }

    private void saveSector(Sector s) {
        List<Object> rows = new ArrayList<>();
        for (Record r : s.regions.values()) {
            List<Object> row = new ArrayList<>();
            row.add(r.cx);
            row.add(r.cz);
            row.add(r.created);
            row.add(r.lastActive);
            row.add(r.visits);
            row.add(r.data == null ? 0 : r.data);
            rows.add(row);
        }
        Map<String, Object> m = new LinkedHashMap<>();
        m.put("v", RECORD_VERSION);
        m.put("r", rows);
        store.save(s.key, m);
        s.dirty = false;
        sectorSaves++;
    }

    /** Save up to max dirty sectors, least recently used first. */
    public int flush(int max) {
        List<Sector> dirty = new ArrayList<>();
        for (Sector s : sectors.values()) if (s.dirty) dirty.add(s);
        dirty.sort((a, b) -> Long.compare(a.lastUsed, b.lastUsed));
        int saved = 0;
        for (Sector s : dirty) {
            if (saved >= max) break;
            saveSector(s);
            saved++;
        }
        return saved;
    }

    /** Keep memory bounded: drop LRU sectors not in use, saving unsaved ones first. */
    int evict(Set<String> inUse) {
        if (sectors.size() <= maxSectors) return 0;
        List<Sector> candidates = new ArrayList<>();
        for (Sector s : sectors.values()) if (!inUse.contains(s.key)) candidates.add(s);
        candidates.sort((a, b) -> Long.compare(a.lastUsed, b.lastUsed));
        int removed = 0;
        int forced = 0;
        for (Sector s : candidates) {
            if (sectors.size() <= maxSectors) break;
            if (s.dirty) {
                if (forced >= MAX_FORCED_SAVES) continue;
                saveSector(s);
                forced++;
            }
            sectors.remove(s.key);
            removed++;
        }
        evictions += removed;
        forcedSaves += forced;
        return removed;
    }

    public int cachedSectors() {
        return sectors.size();
    }

    public int maxSectors() {
        return maxSectors;
    }

    public int dirtyCount() {
        int n = 0;
        for (Sector s : sectors.values()) if (s.dirty) n++;
        return n;
    }

    // ------------------------------------------------------------ tracker API

    public void onResume(ResumeListener l) {
        resumeListeners.add(l);
    }

    public void onEnter(Consumer<Active> l) {
        enterListeners.add(l);
    }

    public Map<Long, Active> active() {
        return active;
    }

    public Active regionAt(int dim, double blockX, double blockZ) {
        return active.get(regionKey(dim, MathUtil.toChunk(blockX), MathUtil.toChunk(blockZ)));
    }

    public Tier tierAt(int dim, double blockX, double blockZ) {
        Active a = regionAt(dim, blockX, blockZ);
        return a == null ? null : a.tier;
    }

    public int count(Tier t) {
        return counts[t.ordinal()];
    }

    /** Record the current time in every active region (before saving). */
    public void checkpoint(long tick) {
        for (Active a : active.values()) touch(a.dim, a.cx, a.cz, tick);
    }

    /** The tracker as an engine system (every 20 ticks). */
    public Scheduler.SimSystem tracker() {
        return new Scheduler.SimSystem() {
            public String id() { return "regions"; }
            public String name() { return "Region tracker (LOD)"; }
            public int intervalTicks() { return 20; }
            public void stop() {
                checkpoint(lastTick);
                active = new HashMap<>();
                java.util.Arrays.fill(counts, 0);
                signature = "";
            }
            public int update(Scheduler.Context ctx) { return Regions.this.update(ctx.tick); }
            public Map<String, Object> describe() { return Regions.this.describe(); }
        };
    }

    /** Recompute the active set (the engine calls this through tracker()). */
    public int update(long tick) {
        lastTick = tick;
        Performance.Profile p = perf.profile();
        List<PlayerPos> list = players.get();
        StringBuilder sig = new StringBuilder().append(p.nearRadius).append('/').append(p.mediumRadius).append('/').append(p.farRadius);
        for (PlayerPos pp : list) sig.append(';').append(pp.dim).append('|').append(MathUtil.toChunk(pp.x)).append('|').append(MathUtil.toChunk(pp.z));
        String s = sig.toString();
        sinceFull++;
        if (s.equals(signature) && sinceFull < FULL_REFRESH_UPDATES) {
            skipped++;
            return 0;
        }
        signature = s;
        sinceFull = 0;
        recomputes++;

        Map<Long, Active> next = new HashMap<>();
        Set<Long> unloaded = new HashSet<>();
        int ops = 0;
        for (PlayerPos pp : list) {
            int pcx = MathUtil.toChunk(pp.x);
            int pcz = MathUtil.toChunk(pp.z);
            for (int dx = -p.farRadius; dx <= p.farRadius; dx++) {
                for (int dz = -p.farRadius; dz <= p.farRadius; dz++) {
                    int d = Math.max(Math.abs(dx), Math.abs(dz));
                    Tier tier = d <= p.nearRadius ? Tier.NEAR : d <= p.mediumRadius ? Tier.MEDIUM : Tier.FAR;
                    int cx = pcx + dx;
                    int cz = pcz + dz;
                    long key = regionKey(pp.dim, cx, cz);
                    Active existing = next.get(key);
                    if (existing != null) {
                        if (tier.ordinal() < existing.tier.ordinal()) existing.tier = tier;
                        continue;
                    }
                    if (unloaded.contains(key)) continue;
                    Active prev = active.get(key);
                    if (prev == null) {
                        ops++;
                        if (!loaded.isLoaded(pp.dim, cx * 16 + 8, cz * 16 + 8)) {
                            unloaded.add(key);
                            continue;
                        }
                        next.put(key, new Active(pp.dim, cx, cz, tier));
                    } else {
                        prev.tier = tier; // same object: other systems hold references
                        next.put(key, prev);
                    }
                }
            }
        }
        for (Map.Entry<Long, Active> e : next.entrySet()) {
            if (active.containsKey(e.getKey())) continue;
            ops++;
            Active a = e.getValue();
            long away = activate(a, tick);
            for (Consumer<Active> l : enterListeners) l.accept(a);
            if (away > 0) {
                resumedTotal++;
                lastResumeAway = away;
                for (ResumeListener l : resumeListeners) l.resumed(a, away);
            }
        }
        for (Map.Entry<Long, Active> e : active.entrySet()) {
            if (next.containsKey(e.getKey())) continue;
            ops++;
            Active a = e.getValue();
            touch(a.dim, a.cx, a.cz, tick); // left the simulated area now
        }
        active = next;
        java.util.Arrays.fill(counts, 0);
        Set<String> inUse = new HashSet<>();
        for (Active a : next.values()) {
            counts[a.tier.ordinal()]++;
            inUse.add(sectorKey(a.dim, a.cx, a.cz));
        }
        evict(inUse);
        return ops;
    }

    public Map<String, Object> describe() {
        Map<String, Object> m = new LinkedHashMap<>();
        m.put("near", counts[0]);
        m.put("medium", counts[1]);
        m.put("far", counts[2]);
        m.put("cachedSectors", sectors.size());
        m.put("dirtySectors", dirtyCount());
        m.put("resumed", resumedTotal);
        m.put("lastResumeAwayTicks", lastResumeAway);
        return m;
    }
}
