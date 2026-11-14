package com.realisticworld.water;

import com.realisticworld.core.DataStore;

import java.util.ArrayList;
import java.util.Arrays;
import java.util.HashSet;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;
import java.util.Set;

/**
 * Water bodies and their time step.
 * Tank: still water enclosed by blocks (found by TankScan). One flat level set
 * by its volume; leaves through openings (Torricelli), open pipe ends and
 * drains; gains rain on its open surface; evaporates; never drains below its
 * lowest open opening by itself.
 * Pool: water on the terrain (made by pouring). Soaks into the ground (feeding
 * soil moisture), evaporates, drains, gains rain, and asks to be re-poured when
 * it grows past the shape it was computed for.
 */
public final class Bodies {
    public static final double SUBSTEP = 0.25;
    public static final double MAX_DETAILED_CATCHUP = 120;
    public static final double MIN_POOL_VOLUME = 0.02;
    /** A film thinner than this over an opening's edge runs off at once (flow ~ depth^1.5). */
    public static final double RUNOFF_FILM = 0.05;

    private Bodies() {}

    /** Weather and ground conditions at a body. */
    public static final class Env {
        public final double tempC;
        public final double humidity;
        public final double wind;
        public final boolean raining;
        public final double intensity;
        public final double soil;

        public Env(double tempC, double humidity, double wind, boolean raining, double intensity, double soil) {
            this.tempC = tempC;
            this.humidity = humidity;
            this.wind = wind;
            this.raining = raining;
            this.intensity = intensity;
            this.soil = soil;
        }
    }

    /** What a step produced. pours: m³ to pour on the terrain keyed by column. */
    public static final class Result {
        public final Map<Long, Double> pours = new LinkedHashMap<>();
        public double lost;
        public double absorbed;
        public double overflow;
        public boolean regrow;

        void pour(int x, int z, double v) {
            if (v <= 0) return;
            pours.merge(com.realisticworld.util.MathUtil.columnKey(x, z), v, Double::sum);
        }
    }

    /** A pipe as seen from a tank. */
    public static final class Pipe {
        public int x, y, z, startY, endY;
        public String outKind; // water, air, drain or null
        public int[] out;
    }

    public abstract static class Body {
        public String id;
        public int dim;
        public double volume;
        public boolean frozen;
        public long lastSim;

        public abstract boolean isTank();

        public abstract Map<String, Object> toMap();
    }

    // ================================================================ tank

    public static final class Tank extends Body {
        public int baseY;
        public int[] layers;
        public List<int[]> columns = new ArrayList<>();
        public int openTop;
        public List<TankScan.Opening> openings = new ArrayList<>();
        public List<int[]> valves = new ArrayList<>();
        public List<Pipe> pipes = new ArrayList<>();
        public double statOut, statRained, statEvaporated, statOutflow;
        /** Render state: highest y shown as water, and whether the top is iced. */
        public int shownTop;
        public boolean iced;

        @Override
        public boolean isTank() {
            return true;
        }

        public double level() {
            return WaterPhysics.levelFromVolume(layers, baseY, volume);
        }

        public double drainFloor() {
            double floor = Double.POSITIVE_INFINITY;
            for (TankScan.Opening o : openings) if (o.open) floor = Math.min(floor, o.bottom);
            for (Pipe p : pipes) if (p.outKind != null && !"water".equals(p.outKind)) floor = Math.min(floor, p.startY);
            return floor;
        }

        public double outflow() {
            double level = level();
            double q = 0;
            for (TankScan.Opening o : openings) {
                if (!o.open) continue;
                double a = WaterPhysics.openingArea(o.kind);
                q += o.side() ? WaterPhysics.sideFlow(level, o.bottom, a) : WaterPhysics.floorFlow(level, o.bottom, a);
            }
            for (Pipe p : pipes) {
                if (p.outKind != null && !"water".equals(p.outKind)) q += Math.max(0, WaterPhysics.pipeFlow(level, p.startY, Double.NEGATIVE_INFINITY, p.endY, 0.25));
            }
            return q;
        }

        public boolean containsColumn(int x, int z) {
            for (int[] c : columns) if (c[0] == x && c[1] == z) return true;
            return false;
        }

        public boolean contains(int x, int y, int z) {
            for (int[] c : columns) if (c[0] == x && c[1] == z) return y >= c[2] && y <= c[3];
            return false;
        }

        @Override
        public Map<String, Object> toMap() {
            Map<String, Object> m = new LinkedHashMap<>();
            m.put("type", "tank");
            m.put("id", id);
            m.put("dim", dim);
            m.put("baseY", baseY);
            m.put("layers", ints(layers));
            List<Object> cols = new ArrayList<>();
            for (int[] c : columns) cols.add(ints(c));
            m.put("columns", cols);
            m.put("openTop", openTop);
            List<Object> ops = new ArrayList<>();
            for (TankScan.Opening o : openings) {
                Map<String, Object> om = new LinkedHashMap<>();
                om.put("kind", o.kind);
                om.put("p", ints(new int[] {o.x, o.y, o.z, o.bottom, o.dx, o.dz}));
                om.put("open", o.open);
                ops.add(om);
            }
            m.put("openings", ops);
            List<Object> vs = new ArrayList<>();
            for (int[] v : valves) vs.add(ints(v));
            m.put("valves", vs);
            List<Object> ps = new ArrayList<>();
            for (Pipe p : pipes) {
                Map<String, Object> pm = new LinkedHashMap<>();
                pm.put("p", ints(new int[] {p.x, p.y, p.z, p.startY, p.endY}));
                if (p.outKind != null) {
                    pm.put("outKind", p.outKind);
                    pm.put("out", ints(p.out));
                }
                ps.add(pm);
            }
            m.put("pipes", ps);
            m.put("volume", volume);
            m.put("frozen", frozen);
            m.put("lastSim", lastSim);
            m.put("stats", new ArrayList<Object>(Arrays.asList(statOut, statRained, statEvaporated, statOutflow)));
            m.put("shownTop", shownTop);
            m.put("iced", iced);
            return m;
        }

        public static Tank fromMap(Map<String, Object> m) {
            Tank t = new Tank();
            t.id = DataStore.getString(m, "id", null);
            if (t.id == null) return null;
            t.dim = DataStore.getInt(m, "dim", 0);
            t.baseY = DataStore.getInt(m, "baseY", 0);
            t.layers = toInts(DataStore.getList(m, "layers"));
            for (Object c : DataStore.getList(m, "columns")) t.columns.add(toInts((List<?>) c));
            t.openTop = DataStore.getInt(m, "openTop", 0);
            for (Object o : DataStore.getList(m, "openings")) {
                @SuppressWarnings("unchecked")
                Map<String, Object> om = (Map<String, Object>) o;
                int[] p = toInts(DataStore.getList(om, "p"));
                TankScan.Opening op = new TankScan.Opening(DataStore.getString(om, "kind", "hole"), p[0], p[1], p[2], p[3], p[4], p[5]);
                op.open = DataStore.getBool(om, "open", true);
                t.openings.add(op);
            }
            for (Object v : DataStore.getList(m, "valves")) t.valves.add(toInts((List<?>) v));
            for (Object o : DataStore.getList(m, "pipes")) {
                @SuppressWarnings("unchecked")
                Map<String, Object> pm = (Map<String, Object>) o;
                int[] p = toInts(DataStore.getList(pm, "p"));
                Pipe pipe = new Pipe();
                pipe.x = p[0];
                pipe.y = p[1];
                pipe.z = p[2];
                pipe.startY = p[3];
                pipe.endY = p[4];
                pipe.outKind = DataStore.getString(pm, "outKind", null);
                if (pipe.outKind != null) pipe.out = toInts(DataStore.getList(pm, "out"));
                t.pipes.add(pipe);
            }
            t.volume = DataStore.getDouble(m, "volume", 0);
            t.frozen = DataStore.getBool(m, "frozen", false);
            t.lastSim = DataStore.getLong(m, "lastSim", 0);
            List<Object> st = DataStore.getList(m, "stats");
            if (st.size() == 4) {
                t.statOut = ((Number) st.get(0)).doubleValue();
                t.statRained = ((Number) st.get(1)).doubleValue();
                t.statEvaporated = ((Number) st.get(2)).doubleValue();
                t.statOutflow = ((Number) st.get(3)).doubleValue();
            }
            t.shownTop = DataStore.getInt(m, "shownTop", t.baseY + t.layers.length - 1);
            t.iced = DataStore.getBool(m, "iced", false);
            return t.layers.length > 0 && !t.columns.isEmpty() ? t : null;
        }
    }

    /** Advance a tank by `seconds`. */
    public static Result stepTank(Tank t, double seconds, Env env) {
        Result res = new Result();
        t.frozen = WaterPhysics.nextFrozen(t.frozen, env.tempC);
        int cap = WaterPhysics.capacity(t.layers);
        double remaining = Math.max(0, seconds);
        double flowSum = 0;
        while (remaining > 1e-9) {
            double h = Math.min(SUBSTEP, remaining);
            remaining -= h;
            double level = t.level();
            for (TankScan.Opening o : t.openings) {
                if (!o.open) continue;
                double area = WaterPhysics.openingArea(o.kind);
                double q = o.side() ? WaterPhysics.sideFlow(level, o.bottom, area) : WaterPhysics.floorFlow(level, o.bottom, area);
                double avail = Math.max(0, t.volume - WaterPhysics.volumeBelow(t.layers, t.baseY, o.bottom));
                double dv = level - o.bottom < RUNOFF_FILM ? avail : Math.min(q * h, avail);
                if (dv <= 0) continue;
                t.volume -= dv;
                flowSum += dv;
                // Drains and floor holes take the water into the ground; side openings let it out outside the wall.
                if ("drain".equals(o.kind) || !o.side()) res.lost += dv;
                else res.pour(o.x + o.dx, o.z + o.dz, dv);
            }
            for (Pipe p : t.pipes) {
                if (p.outKind == null || "water".equals(p.outKind)) continue; // tank links are done in pairs
                double q = Math.max(0, WaterPhysics.pipeFlow(t.level(), p.startY, Double.NEGATIVE_INFINITY, p.endY, 0.25));
                double avail = Math.max(0, t.volume - WaterPhysics.volumeBelow(t.layers, t.baseY, p.startY));
                double dv = Math.min(q * h, avail);
                if (dv <= 0) continue;
                t.volume -= dv;
                flowSum += dv;
                if ("drain".equals(p.outKind)) res.lost += dv;
                else res.pour(p.out[0], p.out[2], dv);
            }
            if (t.openTop > 0) {
                if (env.raining) {
                    double add = WaterPhysics.rainRate(env.intensity) * t.openTop * h;
                    t.volume += add;
                    t.statRained += add;
                }
                if (!t.frozen) {
                    double ev = Math.min(t.volume, WaterPhysics.evaporationRate(env.tempC, env.humidity, env.wind) * t.openTop * h);
                    t.volume -= ev;
                    t.statEvaporated += ev;
                }
            }
            if (t.volume > cap) {
                res.overflow += t.volume - cap;
                t.volume = cap;
            }
        }
        t.statOut += flowSum;
        t.statOutflow = seconds > 0 ? flowSum / seconds : 0;
        return res;
    }

    private static double topArea(Tank t) {
        int i = (int) Math.min(t.layers.length - 1, Math.max(0, Math.floor(t.level() - t.baseY)));
        return Math.max(1, t.layers[i]);
    }

    /** Exchange water between two tanks joined by a pipe; returns m³ moved a to b. */
    public static double stepPipeLink(Tank a, Pipe pa, Tank b, int endBY, double seconds) {
        double moved = 0;
        double remaining = Math.max(0, seconds);
        while (remaining > 1e-9) {
            double h = Math.min(SUBSTEP, remaining);
            remaining -= h;
            double la = a.level();
            double lb = b.level();
            double dv = WaterPhysics.pipeFlow(la, pa.startY, lb, endBY, 0.25) * h;
            double share = 1 / topArea(a) + 1 / topArea(b);
            if (dv > 0) {
                dv = Math.min(dv, Math.min(Math.max(0, a.volume - WaterPhysics.volumeBelow(a.layers, a.baseY, pa.startY)), WaterPhysics.capacity(b.layers) - b.volume));
                dv = Math.min(dv, Math.max(0, (la - lb) / share));
            } else if (dv < 0) {
                double up = Math.min(-dv, Math.min(Math.max(0, b.volume - WaterPhysics.volumeBelow(b.layers, b.baseY, endBY)), WaterPhysics.capacity(a.layers) - a.volume));
                dv = -Math.min(up, Math.max(0, (lb - la) / share));
            }
            if (dv == 0) continue;
            a.volume -= dv;
            b.volume += dv;
            moved += dv;
        }
        return moved;
    }

    // ================================================================ pool

    public static final class Pool extends Body {
        public List<Basin.PoolCol> cols = new ArrayList<>();
        public int minX, minZ;
        public double limit = Double.POSITIVE_INFINITY;
        public int[] spill;
        public double statAbsorbed, statRained, statEvaporated, statDrained;
        /** Render state: water cells, puddle cells, iced cells (packed positions). */
        public Set<Long> shownWater = new HashSet<>();
        public Set<Long> shownPuddles = new HashSet<>();
        public Set<Long> icedCells = new HashSet<>();
        /** Not saved: last rendered signature, to skip unchanged pools. */
        public transient String renderSig;
        private transient int[] sortedBases;

        @Override
        public boolean isTank() {
            return false;
        }

        public int[] sortedBases() {
            if (sortedBases == null || sortedBases.length != cols.size()) {
                int[] b = new int[cols.size()];
                for (int i = 0; i < b.length; i++) b[i] = cols.get(i).base;
                Arrays.sort(b);
                sortedBases = b;
            }
            return sortedBases;
        }

        public void setCols(List<Basin.PoolCol> c) {
            cols = c;
            sortedBases = null;
        }

        public double level() {
            return Basin.levelOverSorted(sortedBases(), volume);
        }

        @Override
        public Map<String, Object> toMap() {
            Map<String, Object> m = new LinkedHashMap<>();
            m.put("type", "pool");
            m.put("id", id);
            m.put("dim", dim);
            List<Object> cs = new ArrayList<>();
            for (Basin.PoolCol c : cols) {
                List<Object> row = new ArrayList<>(Arrays.asList(c.x, c.z, c.base, c.ground));
                cs.add(row);
            }
            m.put("cols", cs);
            m.put("min", ints(new int[] {minX, minZ}));
            m.put("volume", volume);
            m.put("limit", Double.isInfinite(limit) ? -1.0 : limit);
            if (spill != null) m.put("spill", ints(spill));
            m.put("frozen", frozen);
            m.put("lastSim", lastSim);
            m.put("stats", new ArrayList<Object>(Arrays.asList(statAbsorbed, statRained, statEvaporated, statDrained)));
            m.put("w", new ArrayList<Object>(shownWater));
            m.put("p", new ArrayList<Object>(shownPuddles));
            m.put("ice", new ArrayList<Object>(icedCells));
            return m;
        }

        public static Pool fromMap(Map<String, Object> m) {
            Pool p = new Pool();
            p.id = DataStore.getString(m, "id", null);
            if (p.id == null) return null;
            p.dim = DataStore.getInt(m, "dim", 0);
            List<Basin.PoolCol> cs = new ArrayList<>();
            for (Object o : DataStore.getList(m, "cols")) {
                List<?> r = (List<?>) o;
                cs.add(new Basin.PoolCol(((Number) r.get(0)).intValue(), ((Number) r.get(1)).intValue(), ((Number) r.get(2)).intValue(), String.valueOf(r.get(3))));
            }
            p.setCols(cs);
            int[] min = toInts(DataStore.getList(m, "min"));
            if (min.length == 2) {
                p.minX = min[0];
                p.minZ = min[1];
            }
            p.volume = DataStore.getDouble(m, "volume", 0);
            double lim = DataStore.getDouble(m, "limit", -1);
            p.limit = lim < 0 ? Double.POSITIVE_INFINITY : lim;
            List<Object> sp = DataStore.getList(m, "spill");
            p.spill = sp.size() == 2 ? toInts(sp) : null;
            p.frozen = DataStore.getBool(m, "frozen", false);
            p.lastSim = DataStore.getLong(m, "lastSim", 0);
            List<Object> st = DataStore.getList(m, "stats");
            if (st.size() == 4) {
                p.statAbsorbed = ((Number) st.get(0)).doubleValue();
                p.statRained = ((Number) st.get(1)).doubleValue();
                p.statEvaporated = ((Number) st.get(2)).doubleValue();
                p.statDrained = ((Number) st.get(3)).doubleValue();
            }
            for (Object o : DataStore.getList(m, "w")) p.shownWater.add(((Number) o).longValue());
            for (Object o : DataStore.getList(m, "p")) p.shownPuddles.add(((Number) o).longValue());
            for (Object o : DataStore.getList(m, "ice")) p.icedCells.add(((Number) o).longValue());
            return p.cols.isEmpty() ? null : p;
        }
    }

    /** Advance a pool by `seconds`. */
    public static Result stepPool(Pool p, double seconds, Env env) {
        Result res = new Result();
        p.frozen = WaterPhysics.nextFrozen(p.frozen, env.tempC);
        int[] sorted = p.sortedBases();
        double remaining = Math.max(0, seconds);
        double soak = Math.max(0, 1 - env.soil);
        while (remaining > 1e-9 && p.volume > 0) {
            double h = Math.min(SUBSTEP * 8, remaining); // pools change slowly: 2 s steps
            remaining -= h;
            double level = Basin.levelOverSorted(sorted, p.volume);
            int wet = 0;
            double absorb = 0;
            double drain = 0;
            for (Basin.PoolCol c : p.cols) {
                double d = level - c.base;
                if (d <= 0) continue;
                wet++;
                if ("rw:drain".equals(c.ground)) drain += WaterPhysics.floorFlow(level, c.base, 0.25) * h;
                else if (!p.frozen) absorb += Math.min(d, WaterPhysics.infiltrationRate(c.ground) * soak * h);
            }
            double evap = p.frozen ? 0 : WaterPhysics.evaporationRate(env.tempC, env.humidity, env.wind) * wet * h;
            double rain = env.raining ? WaterPhysics.rainRate(env.intensity) * p.cols.size() * h : 0;
            double total = absorb + evap + drain;
            double out = Math.min(p.volume, total);
            double scale = total > 0 ? Math.min(1, p.volume / total) : 0;
            p.volume = p.volume - out + rain;
            p.statAbsorbed += absorb * scale;
            p.statEvaporated += evap * scale;
            p.statDrained += drain * scale;
            p.statRained += rain;
            res.absorbed += absorb * scale;
            res.lost += drain * scale;
        }
        if (p.volume < MIN_POOL_VOLUME) {
            res.absorbed += p.volume;
            p.statAbsorbed += p.volume;
            p.volume = 0;
        }
        if (Basin.levelOverSorted(sorted, p.volume) > p.limit + 1e-6) res.regrow = true;
        return res;
    }

    /** Bring a body up to date after a long gap: detailed first, then a jump to its settled state. */
    public static Result catchUp(Body body, double seconds, Env env) {
        double detailed = Math.min(seconds, MAX_DETAILED_CATCHUP);
        Result res;
        if (body.isTank()) {
            Tank t = (Tank) body;
            res = stepTank(t, detailed, env);
            if (seconds - detailed > 0) {
                double floor = t.drainFloor();
                if (!Double.isInfinite(floor)) {
                    double settled = Math.min(t.volume, WaterPhysics.volumeBelow(t.layers, t.baseY, floor));
                    double out = t.volume - settled;
                    if (out > 0) {
                        TankScan.Opening open = null;
                        for (TankScan.Opening o : t.openings) if (o.open && o.bottom == floor) open = o;
                        if (open != null && !"drain".equals(open.kind) && open.side()) res.pour(open.x + open.dx, open.z + open.dz, out);
                        else res.lost += out;
                        t.volume = settled;
                        t.statOut += out;
                    }
                }
            }
        } else {
            Pool p = (Pool) body;
            double before = p.statAbsorbed + p.statEvaporated + p.statDrained;
            res = stepPool(p, detailed, env);
            double rest = seconds - detailed;
            if (rest > 0 && p.volume > 0) {
                double rate = (p.statAbsorbed + p.statEvaporated + p.statDrained - before) / Math.max(1, detailed);
                double loss = Math.min(p.volume, rate * rest);
                p.volume -= loss;
                res.absorbed += loss;
                if (p.volume < MIN_POOL_VOLUME) p.volume = 0;
            }
        }
        return res;
    }

    // ---- small list helpers ----

    static List<Object> ints(int[] a) {
        List<Object> l = new ArrayList<>(a.length);
        for (int v : a) l.add(v);
        return l;
    }

    static int[] toInts(List<?> l) {
        int[] a = new int[l.size()];
        for (int i = 0; i < a.length; i++) a[i] = ((Number) l.get(i)).intValue();
        return a;
    }
}
