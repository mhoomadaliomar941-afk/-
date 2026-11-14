package com.realisticworld.weather;

import com.realisticworld.core.RwLog;
import com.realisticworld.core.Scheduler;
import com.realisticworld.sim.Regions;
import com.realisticworld.util.MathUtil;

import java.util.ArrayList;
import java.util.Arrays;
import java.util.HashMap;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;

/**
 * Regional climate system: keeps soil moisture current for every active
 * overworld region and knows its biome and surface height, so temperature,
 * precipitation type and fog can be read anywhere near a player. Work is
 * spread: a cursor walks the active regions within the tick budget, NEAR every
 * 100 ticks, MEDIUM 200, FAR 400; biome lookups are capped per update; a region
 * coming back after a gap is caught up in one step.
 * Stored per region in record.data "c": [soil in 1/1000, biome id, surface y].
 */
public final class ClimateSystem implements Scheduler.SimSystem {
    public static final int OVERWORLD = 0;
    private static final int MAX_INITS_PER_UPDATE = 6;

    /** Game access needed for climate. */
    public interface Host {
        /** Registry name of the biome at a block, e.g. "minecraft:plains"; throws if not loaded. */
        String biomeId(int dim, int x, int y, int z);

        /** Climate values of a biome by registry name. */
        Climate.BiomeInfo info(String biomeId);

        /** y of the first open block above the ground, or null if not loaded. */
        Integer surfaceY(int dim, int x, int z);

        long timeOfDay();
    }

    /** Local conditions at a place. */
    public static final class Sample {
        public final String biome;
        public final double tempC;
        public final Double soil;
        public final int humidity;
        public final String precip;
        public final String fog;
        public final double wind;
        public final boolean frost;
        public final boolean drought;
        public final Regions.Tier tier;

        Sample(String biome, double tempC, Double soil, int humidity, String precip, String fog, double wind, boolean frost, boolean drought, Regions.Tier tier) {
            this.biome = biome;
            this.tempC = tempC;
            this.soil = soil;
            this.humidity = humidity;
            this.precip = precip;
            this.fog = fog;
            this.wind = wind;
            this.frost = frost;
            this.drought = drought;
            this.tier = tier;
        }
    }

    public static int interval(Regions.Tier t) {
        return t == Regions.Tier.NEAR ? 100 : t == Regions.Tier.MEDIUM ? 200 : 400;
    }

    private final Regions regions;
    private final WeatherSystem weather;
    private final Host host;
    private final RwLog log;
    private final Map<String, Climate.BiomeInfo> infoCache = new HashMap<>();
    private List<Regions.Active> queue = new ArrayList<>();
    private int cursor;
    public long updated;
    public long initialized;
    public long caughtUp;
    public long initFailures;

    public ClimateSystem(Regions regions, WeatherSystem weather, Host host, RwLog log) {
        this.regions = regions;
        this.weather = weather;
        this.host = host;
        this.log = log;
        regions.onResume(this::catchUp);
    }

    @Override public String id() { return "climate"; }
    @Override public String name() { return "Regional climate"; }
    @Override public int intervalTicks() { return 20; }
    @Override public boolean scalable() { return true; }

    public Climate.BiomeInfo info(String id) {
        Climate.BiomeInfo i = infoCache.get(id);
        if (i == null) {
            try {
                i = host.info(id);
            } catch (RuntimeException e) {
                i = null;
            }
            if (i == null) i = Climate.BiomeInfo.fallback(id);
            infoCache.put(id, i);
        }
        return i;
    }

    /** [soil 1/1000, biome id, surface y] or null. */
    public static List<Object> data(Regions.Active r) {
        if (r.record == null || r.record.data == null) return null;
        Object c = r.record.data.get("c");
        if (!(c instanceof List) || ((List<?>) c).size() != 3) return null;
        @SuppressWarnings("unchecked")
        List<Object> list = (List<Object>) c;
        return list;
    }

    public static int soilPermille(List<Object> c) {
        return ((Number) c.get(0)).intValue();
    }

    private boolean init(Regions.Active r) {
        int x = r.cx * 16 + 8;
        int z = r.cz * 16 + 8;
        String biome;
        Integer y;
        try {
            y = host.surfaceY(r.dim, x, z);
            if (y == null) return false;
            biome = host.biomeId(r.dim, x, y, z);
        } catch (RuntimeException e) {
            initFailures++;
            return false; // chunk not ready: retried on a later pass
        }
        double sm = info(biome).groundWater;
        if (r.record.data == null) r.record.data = new LinkedHashMap<>();
        r.record.data.put("c", new ArrayList<Object>(Arrays.asList((int) Math.round(sm * 1000), biome, y)));
        regions.markDirty(r.dim, r.cx, r.cz);
        r.lastSim = weather.state().tick;
        initialized++;
        return true;
    }

    private void catchUp(Regions.Active r, long away) {
        if (r.dim != OVERWORLD) return;
        List<Object> c = data(r);
        if (c == null) return;
        double sm = Climate.catchUpSoil(soilPermille(c) / 1000.0, info((String) c.get(1)), ((Number) c.get(2)).intValue(),
            weather.state(), away / (double) WeatherModel.TICKS_PER_HOUR);
        setSoil(r, c, sm);
        r.lastSim = weather.state().tick;
        caughtUp++;
    }

    private void setSoil(Regions.Active r, List<Object> c, double sm) {
        int q = (int) Math.round(sm * 1000);
        if (q != soilPermille(c)) {
            c.set(0, q);
            regions.markDirty(r.dim, r.cx, r.cz);
        }
    }

    /** Add water soaked into the ground of a region (from pools), in m³. */
    public void addSoilWater(Regions.Active r, double m3, double regionSoilM3) {
        List<Object> c = data(r);
        if (c == null) return;
        c.set(0, Math.min(1000, soilPermille(c) + (int) Math.round(m3 / regionSoilM3 * 1000)));
        regions.markDirty(r.dim, r.cx, r.cz);
    }

    @Override
    public int update(Scheduler.Context ctx) {
        WeatherModel.State w = weather.state();
        long now = w.tick;
        if (cursor >= queue.size()) {
            List<Regions.Active> q = new ArrayList<>();
            for (Regions.Active a : regions.active().values()) if (a.dim == OVERWORLD) q.add(a);
            q.sort((a, b) -> a.tier.ordinal() - b.tier.ordinal()); // near players first
            queue = q;
            cursor = 0;
        }
        long time = host.timeOfDay();
        int inits = 0;
        int ops = 0;
        while (cursor < queue.size()) {
            if (ops > 0 && ctx.timeLeftMs() <= 0) break;
            Regions.Active r = queue.get(cursor++);
            if (r.record == null) continue;
            List<Object> c = data(r);
            if (c == null) {
                if (inits >= MAX_INITS_PER_UPDATE) continue;
                inits++;
                ops++;
                if (!init(r)) continue;
                c = data(r);
            }
            if (r.lastSim == Long.MIN_VALUE) {
                r.lastSim = now; // known region without a gap: start timing now
                continue;
            }
            if (now - r.lastSim < interval(r.tier)) continue;
            ops++;
            Climate.BiomeInfo b = info((String) c.get(1));
            double temp = Climate.temperatureAt(b, ((Number) c.get(2)).intValue(), time, w);
            double sm = Climate.stepSoil(soilPermille(c) / 1000.0, Climate.soilRates(b, temp, w.raining, w.I, w.H),
                (now - r.lastSim) / (double) WeatherModel.TICKS_PER_HOUR);
            setSoil(r, c, sm);
            r.lastSim = now;
            updated++;
        }
        return ops;
    }

    /** Local conditions at a position (uses the region's data, else asks the game). */
    public Sample sample(int dim, double x, double y, double z) {
        WeatherModel.State w = weather.state();
        if (dim == -1) return new Sample("hell", 45, null, 5, "none", null, 0, false, false, null);
        if (dim == 1) return new Sample("sky", -5, null, 10, "none", null, 0, true, false, null);
        if (dim != OVERWORLD) return null;
        Regions.Active r = regions.regionAt(dim, x, z);
        List<Object> c = r == null ? null : data(r);
        String biome = c == null ? null : (String) c.get(1);
        if (biome == null) {
            try {
                biome = host.biomeId(dim, (int) Math.floor(x), (int) Math.floor(y), (int) Math.floor(z));
            } catch (RuntimeException e) {
                return null;
            }
        }
        Climate.BiomeInfo b = info(biome);
        long time = host.timeOfDay();
        double temp = Climate.temperatureAt(b, (int) Math.floor(y), time, w);
        Double soil = c == null ? null : soilPermille(c) / 1000.0;
        String shortId = biome.startsWith("minecraft:") ? biome.substring(10) : biome;
        return new Sample(shortId, MathUtil.round(temp, 1), soil, (int) Math.round(w.H * 100),
            Climate.precipitation(temp, w), Climate.localFog(b, time, temp, w), MathUtil.round(w.wind, 1),
            temp <= 0, soil != null && soil < 0.15 && w.dryDays >= 4, r == null ? null : r.tier);
    }

    @Override
    public Map<String, Object> describe() {
        int withData = 0;
        long soilSum = 0;
        for (Regions.Active r : regions.active().values()) {
            List<Object> c = data(r);
            if (c != null) {
                withData++;
                soilSum += soilPermille(c);
            }
        }
        Map<String, Object> m = new LinkedHashMap<>();
        m.put("regionsWithClimate", withData);
        m.put("avgSoilPct", withData == 0 ? 0 : (int) Math.round(soilSum / (double) withData / 10));
        m.put("updated", updated);
        m.put("initialized", initialized);
        m.put("caughtUp", caughtUp);
        return m;
    }
}
