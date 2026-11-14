package com.realisticworld.water;

import com.realisticworld.core.Config;
import com.realisticworld.core.DataStore;
import com.realisticworld.core.Performance;
import com.realisticworld.core.RwLog;
import com.realisticworld.core.Scheduler;
import com.realisticworld.sim.Regions;
import com.realisticworld.util.MathUtil;
import com.realisticworld.weather.ClimateSystem;
import com.realisticworld.weather.WeatherModel;
import com.realisticworld.weather.WeatherSystem;

import java.util.ArrayList;
import java.util.EnumSet;
import java.util.HashMap;
import java.util.HashSet;
import java.util.Iterator;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;
import java.util.Random;
import java.util.Set;
import java.util.TreeMap;
import java.util.function.Consumer;

/**
 * Water physics system: finds tanks, simulates every body, pours escaping water
 * onto the terrain, makes puddles and floods from rain, and shows the result
 * with real blocks.
 * World safety: natural water (over MAX_TANK_CELLS) is never touched; blocks
 * change only through type filters (water only into air, flowing water or
 * plants; water/ice/puddles removed only where this system placed them or
 * inside a detected tank), so a player's block is never replaced; a whole layer
 * changes in one tick, so the infinite-source rule cannot bring water back.
 */
public final class WaterSystem implements Scheduler.SimSystem {
    public static final String AUTO_DETECT = "water.autoDetect";
    public static final String RAIN_PUDDLES = "water.rainPuddles";
    public static final int MAX_TANKS = 64;
    public static final int MAX_POOLS = 128;
    private static final int MAX_POURS_PER_UPDATE = 4;
    private static final int HEIGHT_CACHE_MAX = 8192;
    private static final int NATURAL_IGNORE_TICKS = 6000;
    private static final double RUNOFF_POUR = 0.3;
    /** Region soil holds 256 m² x 0.5 m = 128 m³ at saturation. */
    public static final double REGION_SOIL_M3 = 128;
    private static final double CATCHUP_AFTER = 30;

    /** Kinds of block the water system reads and writes. */
    public enum Block { AIR, WATER, FLOWING, PLANT, ICE, PUDDLE }

    public static final EnumSet<Block> FILLABLE = EnumSet.of(Block.AIR, Block.FLOWING, Block.PLANT);

    /** Game access for water. */
    public interface World {
        TankScan.Cell cellAt(int dim, int x, int y, int z);

        /** Terrain column (water stands from base up), null if unloaded. */
        Basin.Column columnAt(int dim, int x, int z);

        /** Set every cell whose current block is one of `from` to `to`, all in this tick; returns how many changed. */
        int fill(int dim, List<Long> cells, Block to, EnumSet<Block> from);

        boolean isLoaded(int dim, int x, int z);
    }

    /** Reported when a scan finishes (status: tank, natural, sealed, limit, unloaded, not_water). */
    public static final class ScanResult {
        public final int dim, x, y, z;
        public final String tankId;
        public final String status;

        ScanResult(int dim, int x, int y, int z, String tankId, String status) {
            this.dim = dim;
            this.x = x;
            this.y = y;
            this.z = z;
            this.tankId = tankId;
            this.status = status;
        }
    }

    public static int interval(Regions.Tier t) {
        return t == Regions.Tier.NEAR ? 10 : t == Regions.Tier.MEDIUM ? 20 : 60;
    }

    private final DataStore store;
    private final Config config;
    private final RwLog log;
    private final Regions regions;
    private final ClimateSystem climate;
    private final WeatherSystem weather;
    private final Performance perf;
    private final Scheduler scheduler;
    private final World world;
    private final Random random = new Random();

    private final Map<String, Bodies.Body> bodies = new LinkedHashMap<>();
    private int nextId = 1;
    private final Set<String> scans = new HashSet<>();
    private final Map<Long, Long> ignored = new HashMap<>();
    private final Map<Integer, Map<Long, Basin.Column>> heights = new HashMap<>();
    private int heightCount;
    private final Map<Integer, Map<Long, Double>> pending = new HashMap<>();
    private final Set<String> dirty = new HashSet<>();
    private boolean indexDirty;
    private final Map<Integer, Map<Long, String>> colIndex = new HashMap<>();
    private final Map<Regions.Active, Double> runoff = new HashMap<>();
    private final Map<Regions.Active, long[]> runoffTarget = new HashMap<>();
    private int runoffCursor;
    private final List<Consumer<ScanResult>> scanListeners = new ArrayList<>();

    public long statScans, statTanksFound, statNatural, statPours, statBlockOps, statLayerOps;
    public double statLost, statAbsorbed, statRunoff;

    public WaterSystem(DataStore store, Config config, RwLog log, Regions regions, ClimateSystem climate, WeatherSystem weather,
                       Performance perf, Scheduler scheduler, World world) {
        this.store = store;
        this.config = config;
        this.log = log;
        this.regions = regions;
        this.climate = climate;
        this.weather = weather;
        this.perf = perf;
        this.scheduler = scheduler;
        this.world = world;
        load();
    }

    public static void defineSettings(Config c) {
        c.define(AUTO_DETECT, Config.Def.bool(true, "Detect tanks when a block next to still water is broken"));
        c.define(RAIN_PUDDLES, Config.Def.bool(true, "Rain on soaked ground forms puddles and floods"));
    }

    @Override public String id() { return "water"; }
    @Override public String name() { return "Water physics"; }
    @Override public int intervalTicks() { return 10; }
    @Override public boolean scalable() { return true; } // sub-steps keep the physics right when throttled

    public void onScan(Consumer<ScanResult> l) {
        scanListeners.add(l);
    }

    public Map<String, Bodies.Body> bodies() {
        return bodies;
    }

    // ---------------------------------------------------------------- storage

    private void load() {
        Map<String, Object> idx = store.load("water/index");
        if (idx == null) return;
        nextId = DataStore.getInt(idx, "next", 1);
        for (Object o : DataStore.getList(idx, "ids")) {
            String id = String.valueOf(o);
            Map<String, Object> m = store.load("water/b/" + id);
            Bodies.Body b = null;
            try {
                if (m != null) b = "tank".equals(m.get("type")) ? Bodies.Tank.fromMap(m) : Bodies.Pool.fromMap(m);
            } catch (RuntimeException e) {
                b = null;
            }
            if (b == null) {
                log.warn("water", "water body " + id + " could not be loaded; dropped");
                indexDirty = true;
            } else {
                bodies.put(b.id, b);
                index(b);
            }
        }
    }

    /** Save the index and up to `max` changed bodies; returns writes. */
    public int save(int max) {
        int writes = 0;
        for (Iterator<String> it = dirty.iterator(); it.hasNext() && writes < max; ) {
            String id = it.next();
            Bodies.Body b = bodies.get(id);
            if (b != null) store.save("water/b/" + id, b.toMap());
            else store.delete("water/b/" + id);
            it.remove();
            writes++;
        }
        if (indexDirty) {
            Map<String, Object> idx = new LinkedHashMap<>();
            idx.put("next", nextId);
            idx.put("ids", new ArrayList<Object>(bodies.keySet()));
            store.save("water/index", idx);
            indexDirty = false;
            writes++;
        }
        return writes;
    }

    private void add(Bodies.Body b) {
        Bodies.Body old = bodies.get(b.id);
        if (old != null) unindex(old);
        bodies.put(b.id, b);
        index(b);
        dirty.add(b.id);
        indexDirty = true;
    }

    private void remove(String id) {
        Bodies.Body b = bodies.remove(id);
        if (b != null) unindex(b);
        dirty.add(id);
        indexDirty = true;
    }

    private Map<Long, String> dimIndex(int dim) {
        return colIndex.computeIfAbsent(dim, d -> new HashMap<>());
    }

    private void index(Bodies.Body b) {
        Map<Long, String> m = dimIndex(b.dim);
        if (b.isTank()) for (int[] c : ((Bodies.Tank) b).columns) m.put(MathUtil.columnKey(c[0], c[1]), b.id);
        else for (Basin.PoolCol c : ((Bodies.Pool) b).cols) m.put(MathUtil.columnKey(c.x, c.z), b.id);
    }

    private void unindex(Bodies.Body b) {
        Map<Long, String> m = dimIndex(b.dim);
        if (b.isTank()) {
            for (int[] c : ((Bodies.Tank) b).columns) m.remove(MathUtil.columnKey(c[0], c[1]), b.id);
        } else {
            for (Basin.PoolCol c : ((Bodies.Pool) b).cols) m.remove(MathUtil.columnKey(c.x, c.z), b.id);
        }
    }

    // ---------------------------------------------------------------- lookups

    private Bodies.Body bodyAt(int dim, int x, int z) {
        String id = dimIndex(dim).get(MathUtil.columnKey(x, z));
        return id == null ? null : bodies.get(id);
    }

    public Bodies.Tank tankAt(int dim, int x, int y, int z) {
        Bodies.Body b = bodyAt(dim, x, z);
        return b instanceof Bodies.Tank && ((Bodies.Tank) b).contains(x, y, z) ? (Bodies.Tank) b : null;
    }

    public Bodies.Pool poolAt(int dim, int x, int z) {
        Bodies.Body b = bodyAt(dim, x, z);
        return b instanceof Bodies.Pool ? (Bodies.Pool) b : null;
    }

    /** Terrain column, cached; our own pool water reads as the ground below it. */
    public Basin.Column heightAt(int dim, int x, int z) {
        Map<Long, Basin.Column> m = heights.computeIfAbsent(dim, d -> new HashMap<>());
        long key = MathUtil.columnKey(x, z);
        Basin.Column cached = m.get(key);
        if (cached != null) return cached;
        Basin.Column col = world.columnAt(dim, x, z);
        if (col != null && col.water) {
            Bodies.Pool pool = poolAt(dim, x, z);
            if (pool != null) {
                for (Basin.PoolCol pc : pool.cols) {
                    if (pc.x == x && pc.z == z) {
                        col = new Basin.Column(pc.base, false, pc.ground);
                        break;
                    }
                }
            }
        }
        if (col == null) return null;
        if (heightCount >= HEIGHT_CACHE_MAX) {
            heights.clear();
            heightCount = 0;
            m = heights.computeIfAbsent(dim, d -> new HashMap<>());
        }
        m.put(key, col);
        heightCount++;
        return col;
    }

    public void invalidateColumn(int dim, int x, int z) {
        Map<Long, Basin.Column> m = heights.get(dim);
        if (m != null && m.remove(MathUtil.columnKey(x, z)) != null) heightCount--;
        runoffTarget.values().removeIf(t -> t[0] == dim && t[1] == x && t[2] == z);
    }

    // ---------------------------------------------------------------- detection

    /** Start (or repeat) a scan of the still water containing (x, y, z). */
    public boolean requestScan(int dim, int x, int y, int z, String reason) {
        long chunk = MathUtil.columnKey(MathUtil.toChunk(x), MathUtil.toChunk(z)) * 8 + dim + 4;
        Long until = ignored.get(chunk);
        if (until != null && until > scheduler.ticks()) return false;
        Bodies.Tank existing = tankAt(dim, x, y, z);
        String key = existing != null ? "tank:" + existing.id : "seed:" + dim + "|" + x + "," + y + "," + z;
        if (scans.contains(key)) return false;
        TankScan scan = new TankScan((sx, sy, sz) -> world.cellAt(dim, sx, sy, sz), x, y, z);
        if (scan.status() == TankScan.Status.NOT_WATER) return false;
        scans.add(key);
        statScans++;
        String existingId = existing == null ? null : existing.id;
        boolean ok = scheduler.enqueue("water-scan:" + key, id(), timeLeft -> {
            do {
                if (scan.step(32)) {
                    scans.remove(key);
                    scanDone(scan, dim, x, y, z, reason, existingId);
                    return true;
                }
            } while (timeLeft.getAsDouble() > 0);
            return false;
        });
        if (!ok) scans.remove(key);
        return ok;
    }

    private void notifyScan(int dim, int x, int y, int z, String tankId, String status) {
        ScanResult r = new ScanResult(dim, x, y, z, tankId, status);
        for (Consumer<ScanResult> l : scanListeners) l.accept(r);
    }

    private void scanDone(TankScan scan, int dim, int x, int y, int z, String reason, String tankId) {
        if (scan.status() == TankScan.Status.TOO_BIG) {
            statNatural++;
            int n = 0;
            for (int[] c : scan.cells()) {
                if (n++ >= 64) break;
                ignored.put(MathUtil.columnKey(MathUtil.toChunk(c[0]), MathUtil.toChunk(c[2])) * 8 + dim + 4, scheduler.ticks() + NATURAL_IGNORE_TICKS);
            }
            notifyScan(dim, x, y, z, null, "natural");
            return;
        }
        if (scan.status() != TankScan.Status.DONE) {
            notifyScan(dim, x, y, z, null, scan.status().name().toLowerCase());
            return;
        }
        TankScan.Shape shape = scan.result();
        Bodies.Tank old = tankId == null ? null : (Bodies.Tank) bodies.get(tankId);
        boolean interesting = !shape.openings.isEmpty() || !shape.pipes.isEmpty() || !shape.valves.isEmpty() || "gauge".equals(reason) || old != null;
        if (!interesting) {
            notifyScan(dim, x, y, z, null, "sealed");
            return;
        }
        if (old == null) {
            int tanks = 0;
            for (Bodies.Body b : bodies.values()) if (b.isTank()) tanks++;
            if (tanks >= MAX_TANKS) {
                log.warn("water", "tank limit (" + MAX_TANKS + ") reached; new tank ignored");
                notifyScan(dim, x, y, z, null, "limit");
                return;
            }
        }
        Bodies.Tank t = new Bodies.Tank();
        t.id = old != null ? old.id : "t" + nextId++;
        t.dim = dim;
        t.baseY = shape.baseY;
        t.layers = shape.layers;
        if (old != null && old.baseY == shape.baseY && old.layers.length > shape.layers.length) {
            int[] merged = old.layers.clone();
            System.arraycopy(shape.layers, 0, merged, 0, shape.layers.length);
            t.layers = merged; // keep capacity seen before the water drained
        }
        t.columns = mergeColumns(old == null ? null : old.columns, shape.columns);
        t.openTop = shape.openTop;
        t.openings = shape.openings;
        t.valves = shape.valves;
        for (TankScan.PipeStart ps : shape.pipes) {
            TankScan.PipeEnd end = TankScan.tracePipe((px, py, pz) -> world.cellAt(dim, px, py, pz), new int[] {ps.x, ps.y, ps.z}, scan::contains, 64);
            Bodies.Pipe p = new Bodies.Pipe();
            p.x = ps.x;
            p.y = ps.y;
            p.z = ps.z;
            p.startY = ps.fromY;
            p.endY = end == null ? ps.y : end.end[1];
            p.outKind = end == null ? null : end.outKind;
            p.out = end == null ? null : end.out;
            t.pipes.add(p);
        }
        t.volume = shape.cells;
        t.frozen = old != null && old.frozen;
        t.lastSim = scheduler.ticks();
        if (old != null) {
            t.statOut = old.statOut;
            t.statRained = old.statRained;
            t.statEvaporated = old.statEvaporated;
        } else {
            statTanksFound++;
        }
        t.shownTop = shape.waterTop;
        add(t);
        log.info("water", (old != null ? "rescanned" : "found") + " tank " + t.id + ": " + shape.cells + " m³, "
            + shape.openings.size() + " openings, " + t.pipes.size() + " pipes");
        for (Bodies.Pipe p : t.pipes) {
            if ("water".equals(p.outKind) && tankAt(dim, p.out[0], p.out[1], p.out[2]) == null) requestScan(dim, p.out[0], p.out[1], p.out[2], "pipe");
        }
        notifyScan(dim, x, y, z, t.id, "tank");
    }

    private static List<int[]> mergeColumns(List<int[]> oldCols, List<int[]> newCols) {
        if (oldCols == null) return newCols;
        Map<Long, int[]> m = new LinkedHashMap<>();
        for (int[] c : oldCols) m.put(MathUtil.columnKey(c[0], c[1]), c.clone());
        for (int[] c : newCols) {
            int[] o = m.get(MathUtil.columnKey(c[0], c[1]));
            if (o == null) m.put(MathUtil.columnKey(c[0], c[1]), c.clone());
            else {
                o[2] = Math.min(o[2], c[2]);
                o[3] = Math.max(o[3], c[3]);
            }
        }
        return new ArrayList<>(m.values());
    }

    private int[] seed(Bodies.Tank t) {
        for (int[] c : t.columns) if (t.shownTop >= c[2]) return new int[] {c[0], Math.min(c[3], t.shownTop), c[1]};
        return null;
    }

    /** Measure a known tank again (picks up changes made by commands, pistons or other mods). */
    public void rescanTank(Bodies.Tank t, String reason) {
        rescan(t, reason);
    }

    private void rescan(Bodies.Tank t, String reason) {
        int[] s = seed(t);
        if (s != null) requestScan(t.dim, s[0], s[1], s[2], reason);
    }

    // ---------------------------------------------------------------- events

    private static final int[][] SIX = {{1, 0, 0}, {-1, 0, 0}, {0, 0, 1}, {0, 0, -1}, {0, 1, 0}, {0, -1, 0}};

    /** A block at (x,y,z) was broken or blown up. */
    public void onBlockRemoved(int dim, int x, int y, int z) {
        invalidateColumn(dim, x, z);
        if (!config.bool(AUTO_DETECT)) return;
        for (int[] d : SIX) {
            if (world.cellAt(dim, x + d[0], y + d[1], z + d[2]) == TankScan.Cell.SOURCE) {
                requestScan(dim, x + d[0], y + d[1], z + d[2], "break");
                return;
            }
        }
    }

    /** A block was placed: it may close a hole or change a pool's ground. */
    public void onBlockPlaced(int dim, int x, int y, int z) {
        invalidateColumn(dim, x, z);
        for (Bodies.Body b : new ArrayList<>(bodies.values())) {
            if (b.dim != dim || !b.isTank()) continue;
            Bodies.Tank t = (Bodies.Tank) b;
            boolean hit = false;
            for (TankScan.Opening o : t.openings) hit |= o.x == x && o.y == y && o.z == z;
            for (Bodies.Pipe p : t.pipes) hit |= p.x == x && p.y == y && p.z == z;
            if (hit) rescan(t, "change");
        }
    }

    /** A valve was opened or closed. */
    public void onValve(int dim, int x, int y, int z) {
        for (Bodies.Body b : new ArrayList<>(bodies.values())) {
            if (b.dim != dim || !b.isTank()) continue;
            Bodies.Tank t = (Bodies.Tank) b;
            for (int[] v : t.valves) {
                if (v[0] == x && v[1] == y && v[2] == z) {
                    rescan(t, "valve");
                    break;
                }
            }
        }
    }

    // ---------------------------------------------------------------- simulation

    private int[] ref(Bodies.Body b) {
        if (b.isTank()) {
            Bodies.Tank t = (Bodies.Tank) b;
            return new int[] {t.columns.get(0)[0], (int) Math.floor(t.level()), t.columns.get(0)[1]};
        }
        Bodies.Pool p = (Bodies.Pool) b;
        return new int[] {p.minX, p.cols.get(0).base, p.minZ};
    }

    private Bodies.Env env(int dim, int x, int y, int z) {
        WeatherModel.State w = weather.state();
        ClimateSystem.Sample c = climate.sample(dim, x, y, z);
        return new Bodies.Env(c == null ? 15 : c.tempC, w.H, w.wind, w.raining && (c == null || !"none".equals(c.precip)), w.I,
            c == null || c.soil == null ? 0.5 : c.soil);
    }

    @Override
    public int update(Scheduler.Context ctx) {
        long now = ctx.tick;
        int ops = 0;
        List<Bodies.Tank> stepped = new ArrayList<>();
        for (Bodies.Body b : new ArrayList<>(bodies.values())) {
            int[] r = ref(b);
            Regions.Active region = regions.regionAt(b.dim, r[0], r[2]);
            if (region == null) continue; // not simulated now; caught up when it comes back
            if (now - b.lastSim < interval(region.tier)) continue;
            double seconds = (now - b.lastSim) / 20.0;
            Bodies.Env env = env(b.dim, r[0], r[1], r[2]);
            Bodies.Result res = seconds > CATCHUP_AFTER ? Bodies.catchUp(b, seconds, env)
                : b.isTank() ? Bodies.stepTank((Bodies.Tank) b, seconds, env) : Bodies.stepPool((Bodies.Pool) b, seconds, env);
            b.lastSim = now;
            ops++;
            for (Map.Entry<Long, Double> e : res.pours.entrySet()) pending.computeIfAbsent(b.dim, d -> new LinkedHashMap<>()).merge(e.getKey(), e.getValue(), Double::sum);
            statLost += res.lost + res.overflow;
            if (res.absorbed > 0) {
                statAbsorbed += res.absorbed;
                climate.addSoilWater(region, res.absorbed, REGION_SOIL_M3);
            }
            if (b.isTank()) stepped.add((Bodies.Tank) b);
            else if (res.regrow) refill((Bodies.Pool) b, 0);
            dirty.add(b.id);
        }
        for (Bodies.Tank a : stepped) {
            for (Bodies.Pipe p : a.pipes) {
                if (!"water".equals(p.outKind)) continue;
                Bodies.Tank b = tankAt(a.dim, p.out[0], p.out[1], p.out[2]);
                if (b == null || b.id.compareTo(a.id) <= 0) continue;
                Bodies.stepPipeLink(a, p, b, p.out[1], interval(Regions.Tier.NEAR) / 20.0);
                dirty.add(b.id);
            }
        }
        ops += pourPending();
        ops += rainRunoff();
        ops += render();
        cleanup();
        return ops;
    }

    private int pourPending() {
        int n = 0;
        for (Map.Entry<Integer, Map<Long, Double>> d : pending.entrySet()) {
            Iterator<Map.Entry<Long, Double>> it = d.getValue().entrySet().iterator();
            while (it.hasNext() && n < MAX_POURS_PER_UPDATE) {
                Map.Entry<Long, Double> e = it.next();
                int x = (int) (e.getKey() / 67108864L) - 33554432;
                int z = (int) (e.getKey() % 67108864L) - 33554432;
                if (!world.isLoaded(d.getKey(), x, z)) continue;
                it.remove();
                n++;
                pourAt(d.getKey(), x, z, e.getValue(), 0);
            }
        }
        return n;
    }

    /** Pour `volume` m³ onto the terrain at (x, z). */
    public void pourAt(int dim, int x, int z, double volume, int depth) {
        if (volume <= 0 || depth > 6) {
            statLost += Math.max(0, volume);
            return;
        }
        statPours++;
        Bodies.Pool here = poolAt(dim, x, z);
        if (here != null) {
            addToPool(here, volume, depth);
            return;
        }
        Basin.HeightFn h = (px, pz) -> heightAt(dim, px, pz);
        Basin.Point low = Basin.descend(x, z, h);
        if (low == null) {
            pending.computeIfAbsent(dim, d -> new LinkedHashMap<>()).merge(MathUtil.columnKey(x, z), volume, Double::sum);
            return;
        }
        if (low.col.water) {
            Bodies.Tank tank = tankAt(dim, low.x, low.col.base - 1, low.z);
            if (tank != null) {
                tank.volume = Math.min(WaterPhysics.capacity(tank.layers), tank.volume + volume); // falls into a tank
                dirty.add(tank.id);
            } else {
                statLost += volume; // joins a lake, river or the sea
            }
            return;
        }
        Bodies.Pool existing = poolAt(dim, low.x, low.z);
        if (existing != null) {
            addToPool(existing, volume, depth);
            return;
        }
        int pools = 0;
        for (Bodies.Body b : bodies.values()) if (!b.isTank()) pools++;
        if (pools >= MAX_POOLS) {
            statLost += volume;
            return;
        }
        Basin.Fill r = Basin.fill(low.x, low.z, volume, h, 512, 32);
        if (r.cols.isEmpty()) {
            statLost += volume;
            return;
        }
        for (Basin.PoolCol c : r.cols) {
            Bodies.Pool other = poolAt(dim, c.x, c.z);
            if (other != null) {
                addToPool(other, volume, depth);
                return;
            }
        }
        Bodies.Pool pool = new Bodies.Pool();
        pool.id = "p" + nextId++;
        pool.dim = dim;
        pool.setCols(r.cols);
        pool.minX = low.x;
        pool.minZ = low.z;
        pool.volume = r.used;
        pool.limit = r.nextBase;
        pool.spill = r.spill;
        pool.lastSim = scheduler.ticks();
        add(pool);
        if (r.spill != null && r.leftover > 0) pourAt(dim, r.spill[0], r.spill[1], r.leftover, depth + 1);
        else statLost += r.leftover;
    }

    private void addToPool(Bodies.Pool p, double volume, int depth) {
        p.volume += volume;
        // Recompute the shape only when the water rises past what it describes.
        if (p.level() > p.limit + 1e-6) refill(p, depth);
        else dirty.add(p.id);
    }

    private void refill(Bodies.Pool p, int depth) {
        Basin.Fill r = Basin.fill(p.minX, p.minZ, p.volume, (px, pz) -> heightAt(p.dim, px, pz), 512, 32);
        if (r.cols.isEmpty()) return;
        unindex(p);
        p.setCols(r.cols);
        index(p);
        p.volume = r.used;
        p.limit = r.nextBase;
        p.spill = r.spill;
        dirty.add(p.id);
        if (r.spill != null && r.leftover > 0) pourAt(p.dim, r.spill[0], r.spill[1], r.leftover, depth + 1);
        else statLost += r.leftover;
    }

    /** Rain on saturated ground runs off to each region's low point (puddles, ponds, floods). */
    private int rainRunoff() {
        WeatherModel.State w = weather.state();
        if (!w.raining || !config.bool(RAIN_PUDDLES)) return 0;
        List<Regions.Active> near = new ArrayList<>();
        for (Regions.Active a : regions.active().values()) if (a.tier == Regions.Tier.NEAR && a.dim == ClimateSystem.OVERWORLD) near.add(a);
        if (near.isEmpty()) return 0;
        near.sort((a, b) -> a.cx != b.cx ? Integer.compare(a.cx, b.cx) : Integer.compare(a.cz, b.cz));
        int ops = 0;
        for (int i = 0; i < 2; i++) {
            Regions.Active r = near.get(runoffCursor++ % near.size());
            List<Object> c = ClimateSystem.data(r);
            if (c == null || ClimateSystem.soilPermille(c) < 850) continue; // ground still soaks it up
            ClimateSystem.Sample s = climate.sample(r.dim, r.cx * 16 + 8, ((Number) c.get(2)).intValue(), r.cz * 16 + 8);
            if (s == null || !"rain".equals(s.precip)) continue;
            double seconds = Math.max(1, near.size() / 2.0) * intervalTicks() / 20.0;
            double acc = runoff.getOrDefault(r, 0.0) + WaterPhysics.rainRate(w.I) * 256 * 0.5 * seconds;
            if (acc < RUNOFF_POUR) {
                runoff.put(r, acc);
                continue;
            }
            runoff.remove(r);
            long[] target = runoffTarget.get(r);
            if (target == null) {
                Basin.Point low = Basin.descend(r.cx * 16 + random.nextInt(16), r.cz * 16 + random.nextInt(16), (px, pz) -> heightAt(r.dim, px, pz));
                if (low == null) continue;
                target = new long[] {r.dim, low.x, low.z};
                runoffTarget.put(r, target);
            }
            statRunoff += acc;
            pourAt(r.dim, (int) target[1], (int) target[2], acc, 0);
            ops++;
        }
        runoff.keySet().retainAll(regions.active().values());
        runoffTarget.keySet().retainAll(regions.active().values());
        return ops;
    }

    // ---------------------------------------------------------------- rendering

    private int render() {
        int budget = perf.profile().waterBlocksPerUpdate;
        int ops = 0;
        for (Bodies.Body b : bodies.values()) {
            if (budget <= 0) break;
            int[] r = ref(b);
            Regions.Active region = regions.regionAt(b.dim, r[0], r[2]);
            if (region == null || region.tier == Regions.Tier.FAR) continue;
            if (!world.isLoaded(b.dim, r[0], r[2])) continue;
            int used = b.isTank() ? renderTank((Bodies.Tank) b, budget) : renderPool((Bodies.Pool) b, budget);
            budget -= used;
            if (used > 0) ops++;
        }
        return ops;
    }

    private List<Long> tankLayer(Bodies.Tank t, int y) {
        List<Long> cells = new ArrayList<>();
        for (int[] c : t.columns) if (y >= c[2] && y <= c[3]) cells.add(WaterPhysics.pack(c[0], y, c[1]));
        return cells;
    }

    private int fill(int dim, List<Long> cells, Block to, EnumSet<Block> from) {
        if (cells.isEmpty()) return 0;
        world.fill(dim, cells, to, from);
        statLayerOps++;
        statBlockOps += cells.size();
        return cells.size();
    }

    private int renderTank(Bodies.Tank t, int budget) {
        int want = (int) Math.min(t.baseY + t.layers.length, Math.floor(t.level() + 0.5)) - 1;
        int used = 0;
        if (t.frozen != t.iced && t.shownTop >= t.baseY) {
            used += t.frozen ? fill(t.dim, tankLayer(t, t.shownTop), Block.ICE, EnumSet.of(Block.WATER))
                : fill(t.dim, tankLayer(t, t.shownTop), Block.WATER, EnumSet.of(Block.ICE));
            t.iced = t.frozen;
            dirty.add(t.id);
        }
        if (t.iced) return used;
        while (t.shownTop > want && (used == 0 || used < budget)) {
            used += fill(t.dim, tankLayer(t, t.shownTop), Block.AIR, EnumSet.of(Block.WATER, Block.FLOWING));
            t.shownTop--;
            dirty.add(t.id);
        }
        while (t.shownTop < want && (used == 0 || used < budget)) {
            t.shownTop++;
            used += fill(t.dim, tankLayer(t, t.shownTop), Block.WATER, FILLABLE);
            dirty.add(t.id);
        }
        return used;
    }

    private int renderPool(Bodies.Pool p, int budget) {
        double level = p.level();
        String sig = p.cols.size() + "|" + Math.round(level * 20) + "|" + p.frozen + "|" + (p.volume > 0);
        if (sig.equals(p.renderSig)) return 0;
        TreeMap<Integer, List<Long>> want = new TreeMap<>();
        Set<Long> wantSet = new HashSet<>();
        Set<Long> puddles = new HashSet<>();
        if (p.volume > 0) {
            for (Basin.PoolCol c : p.cols) {
                double d = level - c.base;
                if (d >= 0.5) {
                    int n = (int) Math.round(d);
                    for (int y = c.base; y < c.base + n; y++) {
                        long k = WaterPhysics.pack(c.x, y, c.z);
                        want.computeIfAbsent(y, yy -> new ArrayList<>()).add(k);
                        wantSet.add(k);
                    }
                } else if (d >= 0.05 && !p.frozen) {
                    puddles.add(WaterPhysics.pack(c.x, c.base, c.z));
                }
            }
        }
        int used = 0;
        if (!p.icedCells.isEmpty() && !p.frozen) {
            used += fill(p.dim, new ArrayList<>(p.icedCells), Block.WATER, EnumSet.of(Block.ICE));
            p.icedCells.clear();
            dirty.add(p.id);
        }
        if (p.frozen) {
            if (p.icedCells.isEmpty() && !p.shownWater.isEmpty()) {
                int top = Integer.MIN_VALUE;
                for (long k : p.shownWater) top = Math.max(top, WaterPhysics.unpackY(k));
                List<Long> topCells = new ArrayList<>();
                for (long k : p.shownWater) if (WaterPhysics.unpackY(k) == top) topCells.add(k);
                used += fill(p.dim, topCells, Block.ICE, EnumSet.of(Block.WATER));
                p.icedCells.addAll(topCells);
                dirty.add(p.id);
            }
            p.renderSig = sig;
            return used;
        }
        // Remove extra water, highest layer first, one whole layer per step.
        TreeMap<Integer, List<Long>> extra = new TreeMap<>();
        for (long k : p.shownWater) if (!wantSet.contains(k)) extra.computeIfAbsent(WaterPhysics.unpackY(k), y -> new ArrayList<>()).add(k);
        while (!extra.isEmpty() && (used == 0 || used < budget)) {
            Map.Entry<Integer, List<Long>> top = extra.pollLastEntry();
            used += fill(p.dim, top.getValue(), Block.AIR, EnumSet.of(Block.WATER, Block.FLOWING));
            p.shownWater.removeAll(top.getValue());
        }
        // Add missing water, lowest layer first.
        boolean complete = extra.isEmpty();
        for (Map.Entry<Integer, List<Long>> e : want.entrySet()) {
            List<Long> missing = new ArrayList<>();
            for (long k : e.getValue()) if (!p.shownWater.contains(k)) missing.add(k);
            if (missing.isEmpty()) continue;
            if (used > 0 && used >= budget) {
                complete = false;
                break;
            }
            EnumSet<Block> from = EnumSet.copyOf(FILLABLE);
            from.add(Block.PUDDLE);
            used += fill(p.dim, missing, Block.WATER, from);
            p.shownWater.addAll(missing);
        }
        // Puddles (thin custom block, no vanilla flow): cell by cell.
        List<Long> gone = new ArrayList<>();
        for (long k : p.shownPuddles) if (!puddles.contains(k) && !p.shownWater.contains(k)) gone.add(k);
        List<Long> fresh = new ArrayList<>();
        for (long k : puddles) if (!p.shownPuddles.contains(k) && !p.shownWater.contains(k)) fresh.add(k);
        if (!gone.isEmpty()) used += fill(p.dim, gone, Block.AIR, EnumSet.of(Block.PUDDLE));
        p.shownPuddles.removeIf(k -> !puddles.contains(k));
        if (!fresh.isEmpty()) {
            if (used == 0 || used < budget) {
                used += fill(p.dim, fresh, Block.PUDDLE, EnumSet.of(Block.AIR));
                p.shownPuddles.addAll(fresh);
            } else {
                complete = false;
            }
        }
        if (used > 0) dirty.add(p.id);
        p.renderSig = complete ? sig : null;
        return used;
    }

    private void cleanup() {
        for (Bodies.Body b : new ArrayList<>(bodies.values())) {
            if (!b.isTank()) {
                Bodies.Pool p = (Bodies.Pool) b;
                if (p.volume <= Bodies.MIN_POOL_VOLUME && p.shownWater.isEmpty() && p.shownPuddles.isEmpty() && p.icedCells.isEmpty()) remove(p.id);
            } else {
                Bodies.Tank t = (Bodies.Tank) b;
                if (t.volume < 0.01 && t.shownTop < t.baseY) remove(t.id);
            }
        }
    }

    // ---------------------------------------------------------------- info

    /** Readings for the gauge and the UI. */
    public Map<String, Object> describeBody(Bodies.Body b) {
        Map<String, Object> m = new LinkedHashMap<>();
        m.put("id", b.id);
        if (b.isTank()) {
            Bodies.Tank t = (Bodies.Tank) b;
            double level = t.level();
            int openCount = 0;
            int openValves = 0;
            for (TankScan.Opening o : t.openings) {
                if (!o.open) continue;
                openCount++;
                if ("valve".equals(o.kind)) openValves++;
            }
            m.put("type", "tank");
            m.put("liters", Math.round(t.volume * 1000));
            m.put("capacityLiters", WaterPhysics.capacity(t.layers) * 1000L);
            m.put("level", MathUtil.round(level, 2));
            m.put("depth", MathUtil.round(level - t.baseY, 2));
            m.put("bottomPressureKPa", MathUtil.round(WaterPhysics.pressureKPa(level - t.baseY), 1));
            m.put("openings", openCount);
            m.put("closedValves", t.valves.size() - openValves);
            m.put("pipes", t.pipes.size());
            m.put("outflowLps", MathUtil.round(t.outflow() * 1000, 1));
            m.put("leakedLiters", Math.round(t.statOut * 1000));
            m.put("frozen", t.frozen);
        } else {
            Bodies.Pool p = (Bodies.Pool) b;
            double level = p.level();
            int wet = 0;
            double maxDepth = 0;
            for (Basin.PoolCol c : p.cols) {
                if (level - c.base > 0) wet++;
                maxDepth = Math.max(maxDepth, level - c.base);
            }
            m.put("type", "pool");
            m.put("liters", Math.round(p.volume * 1000));
            m.put("areaM2", wet);
            m.put("maxDepth", MathUtil.round(maxDepth, 2));
            m.put("absorbedLiters", Math.round(p.statAbsorbed * 1000));
            m.put("evaporatedLiters", Math.round(p.statEvaporated * 1000));
            m.put("frozen", p.frozen);
            m.put("flood", wet >= 64);
        }
        return m;
    }

    /** Bodies within `radius` blocks, nearest first. */
    public List<Bodies.Body> near(int dim, double x, double z, double radius) {
        List<Bodies.Body> out = new ArrayList<>();
        for (Bodies.Body b : bodies.values()) {
            if (b.dim != dim) continue;
            int[] r = ref(b);
            if (Math.hypot(r[0] - x, r[2] - z) <= radius) out.add(b);
        }
        out.sort((a, b) -> {
            int[] ra = ref(a);
            int[] rb = ref(b);
            return Double.compare(Math.hypot(ra[0] - x, ra[2] - z), Math.hypot(rb[0] - x, rb[2] - z));
        });
        return out;
    }

    @Override
    public Map<String, Object> describe() {
        int tanks = 0;
        int pools = 0;
        double m3 = 0;
        for (Bodies.Body b : bodies.values()) {
            if (b.isTank()) tanks++;
            else pools++;
            m3 += b.volume;
        }
        Map<String, Object> m = new LinkedHashMap<>();
        m.put("tanks", tanks);
        m.put("pools", pools);
        m.put("liters", Math.round(m3 * 1000));
        m.put("scans", scans.size());
        m.put("blockOps", statBlockOps);
        return m;
    }
}
