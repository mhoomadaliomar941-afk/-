package com.realisticworld.weather;

import com.realisticworld.core.Config;
import com.realisticworld.core.DataStore;
import com.realisticworld.core.Performance;
import com.realisticworld.core.RwLog;
import com.realisticworld.core.Scheduler;
import com.realisticworld.sim.Regions;
import com.realisticworld.util.Rng;
import org.junit.Test;

import java.util.ArrayList;
import java.util.HashSet;
import java.util.List;
import java.util.Set;

import static org.junit.Assert.*;

/** Ported from weatherModel, climate and weatherSystems tests. */
public class WeatherTest {
    private static final RwLog LOG = new RwLog((l, s) -> {});

    private static Scheduler.Context ctx(long tick, long delta) {
        return new Scheduler.Context(tick, delta, () -> 10);
    }

    // ---------------------------------------------------------------- model

    static final class Stats {
        int samples, rain, storm, light, heavy;
        List<Double> events = new ArrayList<>();
        List<Double> dry = new ArrayList<>();
        Set<String> conditions = new HashSet<>();
    }

    static Stats simulate(int seed, int days, String freq) {
        WeatherModel.State st = WeatherModel.create(new Rng(seed));
        Stats out = new Stats();
        double run = 0, dry = 0;
        boolean prev = false;
        for (long t = 0; t < (long) days * WeatherModel.TICKS_PER_DAY; t += WeatherModel.MAX_STEP_TICKS) {
            WeatherModel.step(st, WeatherModel.MAX_STEP_TICKS, freq);
            out.samples++;
            out.conditions.add(WeatherModel.classify(st));
            assertFalse(Double.isNaN(st.P + st.A + st.H + st.I + st.wind + st.windDir + st.fogG + st.dryDays + st.rainEma));
            if (st.raining) {
                out.rain++;
                if (st.storm) out.storm++;
                String c = WeatherModel.rainClass(st.I);
                if (c.equals("LIGHT_RAIN")) out.light++;
                if (c.equals("HEAVY_RAIN")) out.heavy++;
                run += 0.25;
                if (!prev && dry > 0) {
                    out.dry.add(dry);
                    dry = 0;
                }
            } else {
                dry += 0.25;
                if (prev) {
                    out.events.add(run);
                    run = 0;
                }
            }
            prev = st.raining;
        }
        return out;
    }

    static double mean(List<Double> l) {
        double s = 0;
        for (double d : l) s += d;
        return s / l.size();
    }

    @Test
    public void sameSeedSameWeatherAndSaveRestoreContinues() {
        WeatherModel.State a = WeatherModel.create(new Rng(7));
        WeatherModel.step(a, 30000, "NORMAL");
        WeatherModel.State restored = WeatherModel.State.fromMap(DataStore.deepCopy(a.toMap()));
        WeatherModel.step(a, 30000, "NORMAL");
        WeatherModel.step(restored, 30000, "NORMAL");
        assertEquals(a.toMap(), restored.toMap());
    }

    @Test
    public void oneBigStepEqualsManySmallOnes() {
        WeatherModel.State a = WeatherModel.create(new Rng(9));
        WeatherModel.State b = WeatherModel.create(new Rng(9));
        WeatherModel.step(a, 5000, "NORMAL");
        for (int i = 0; i < 20; i++) WeatherModel.step(b, 250, "NORMAL");
        assertEquals(a.toMap(), b.toMap());
    }

    @Test
    public void normalFrequencyHasRealisticRain() {
        List<Stats> all = new ArrayList<>();
        for (int s = 1; s <= 5; s++) all.add(simulate(s * 7919, 60, "NORMAL"));
        double rain = 0, storm = 0;
        List<Double> events = new ArrayList<>(), dry = new ArrayList<>();
        int rainy = 0, light = 0, heavy = 0;
        for (Stats st : all) {
            rain += (double) st.rain / st.samples / all.size();
            storm += (double) st.storm / st.samples / all.size();
            events.addAll(st.events);
            dry.addAll(st.dry);
            rainy += st.rain;
            light += st.light;
            heavy += st.heavy;
        }
        assertTrue("rain " + rain, rain > 0.18 && rain < 0.35);
        assertTrue("storms " + storm, storm > 0.005 && storm < 0.06);
        assertTrue("events " + mean(events), mean(events) > 3 && mean(events) < 12);
        assertTrue("dry spells " + mean(dry), mean(dry) > 8);
        assertTrue("light share", (double) light / rainy > 0.3 && (double) light / rainy < 0.7);
        assertTrue("heavy share", (double) heavy / rainy > 0.08 && (double) heavy / rainy < 0.4);
    }

    @Test
    public void frequencyOrderingAndDroughts() {
        double[] f = new double[3];
        String[] names = {"LOW", "NORMAL", "HIGH"};
        for (int i = 0; i < 3; i++) {
            for (int s : new int[] {11, 22, 33}) {
                Stats st = simulate(s, 40, names[i]);
                f[i] += (double) st.rain / st.samples / 3;
            }
        }
        assertTrue(f[0] < f[1] && f[1] < f[2]);
        Set<String> c = new HashSet<>();
        for (int s : new int[] {11, 22, 33, 44, 55}) c.addAll(simulate(s, 60, "LOW").conditions);
        assertTrue(c.contains("DROUGHT"));
        assertTrue(c.contains("HEATWAVE"));
    }

    @Test
    public void everyForceableConditionAppliesAndEnds() {
        for (String type : WeatherModel.FORCEABLE.keySet()) {
            WeatherModel.State st = WeatherModel.create(new Rng(5));
            WeatherModel.step(st, 2000, "NORMAL");
            WeatherModel.force(st, type, 6000);
            WeatherModel.step(st, 3000, "NORMAL");
            assertEquals(type, WeatherModel.classify(st));
            WeatherModel.step(st, 3500, "NORMAL");
            assertNull(st.forceType);
        }
        WeatherModel.State st = WeatherModel.create(new Rng(1));
        WeatherModel.force(st, "THUNDERSTORM", 100);
        assertEquals("forced storm applies at once", "THUNDER", WeatherModel.vanillaWeather(st));
    }

    @Test
    public void forecastDoesNotChangeStateAndCoversRealFuture() {
        WeatherModel.State st = WeatherModel.create(new Rng(2024));
        WeatherModel.step(st, 40000, "NORMAL");
        Object before = st.toMap();
        List<WeatherModel.Period> fc = WeatherModel.forecast(st, 72, 6, 6, "NORMAL");
        assertEquals(before, st.toMap());
        assertEquals(12, fc.size());
        for (WeatherModel.Period p : fc) {
            boolean rained = false;
            for (int k = 0; k < 24; k++) {
                WeatherModel.step(st, 250, "NORMAL");
                rained |= st.raining;
            }
            if (rained) assertTrue("rain at +" + p.fromHour + "h had 0%", p.rainChance > 0);
        }
    }

    @Test
    public void sanitizeRejectsBrokenData() {
        assertNull(WeatherModel.State.fromMap(null));
        java.util.Map<String, Object> m = WeatherModel.create(new Rng(3)).toMap();
        m.put("H", "wet");
        assertNull(WeatherModel.State.fromMap(m));
        java.util.Map<String, Object> odd = WeatherModel.create(new Rng(3)).toMap();
        odd.put("H", 5.0);
        odd.put("forceType", "NOPE");
        WeatherModel.State s = WeatherModel.State.fromMap(odd);
        assertEquals(1.0, s.H, 0);
        assertNull(s.forceType);
    }

    // ---------------------------------------------------------------- climate

    private static WeatherModel.State calm() {
        WeatherModel.State w = WeatherModel.create(new Rng(1));
        w.H = 0.5;
        w.wind = 3;
        return w;
    }

    @Test
    public void biomesMountainsDayAndNight() {
        // Java 1.12 biome values: desert 2.0/0.0, jungle 0.95/0.9, plains 0.8/0.4, taiga 0.25/0.8, ice flats 0.0/0.5
        Climate.BiomeInfo desert = new Climate.BiomeInfo("minecraft:desert", 2.0, 0.0, false);
        Climate.BiomeInfo jungle = new Climate.BiomeInfo("minecraft:jungle", 0.95, 0.9, false);
        Climate.BiomeInfo plains = new Climate.BiomeInfo("minecraft:plains", 0.8, 0.4, false);
        Climate.BiomeInfo taiga = new Climate.BiomeInfo("minecraft:taiga", 0.25, 0.8, false);
        Climate.BiomeInfo ice = new Climate.BiomeInfo("minecraft:ice_flats", 0.0, 0.5, false);
        Climate.BiomeInfo ocean = new Climate.BiomeInfo("minecraft:ocean", 0.5, 0.5, true);
        WeatherModel.State w = calm();
        assertTrue(Climate.meanTemperature(desert, 64, w) > Climate.meanTemperature(jungle, 64, w));
        assertTrue(Climate.meanTemperature(jungle, 64, w) > Climate.meanTemperature(plains, 64, w));
        assertTrue(Climate.meanTemperature(plains, 64, w) > Climate.meanTemperature(taiga, 64, w));
        assertTrue(Climate.meanTemperature(ice, 64, w) < 0);
        double swingDesert = Climate.temperatureAt(desert, 64, 8000, w) - Climate.temperatureAt(desert, 64, 20000, w);
        double swingOcean = Climate.temperatureAt(ocean, 64, 8000, w) - Climate.temperatureAt(ocean, 64, 20000, w);
        assertTrue(swingDesert > 15 && swingOcean < 5);
        assertEquals(136 * Climate.LAPSE_PER_BLOCK,
            Climate.temperatureAt(plains, 64, 6000, w) - Climate.temperatureAt(plains, 200, 6000, w), 1e-9);
    }

    @Test
    public void soilEquationIsExactAndBehaves() {
        Climate.BiomeInfo p = new Climate.BiomeInfo("minecraft:plains", 0.8, 0.4, false);
        Climate.SoilRates r = Climate.soilRates(p, 18, true, 0.3, 0.7);
        assertEquals(Climate.stepSoil(0.2, r, 10), Climate.stepSoil(Climate.stepSoil(0.2, r, 3.5), r, 6.5), 1e-12);
        assertTrue(Climate.stepSoil(0.3, Climate.soilRates(p, 15, true, 0.3, 0.8), 6) > 0.6);
        assertTrue(Climate.stepSoil(0.6, Climate.soilRates(p, 32, false, 0, 0.3), 48) < 0.5);
        Climate.SoilRates frozen = Climate.soilRates(p, -5, true, 0.5, 0.9);
        assertEquals(0, frozen.gain, 0);
        assertEquals(0, frozen.loss, 0);
    }

    @Test
    public void localFogCases() {
        Climate.BiomeInfo river = new Climate.BiomeInfo("minecraft:river", 0.5, 0.5, true);
        Climate.BiomeInfo plains = new Climate.BiomeInfo("minecraft:plains", 0.8, 0.4, false);
        Climate.BiomeInfo desert = new Climate.BiomeInfo("minecraft:desert", 2.0, 0.0, false);
        WeatherModel.State w = calm();
        w.H = 0.8;
        assertEquals("mist", Climate.localFog(river, 23500, 10, w));
        w.fogG = 0.3;
        assertEquals("dense", Climate.localFog(river, 23500, 10, w));
        WeatherModel.State windy = calm();
        windy.wind = 12;
        assertNull(Climate.localFog(plains, 6000, 15, windy));
        WeatherModel.State heavy = calm();
        heavy.raining = true;
        heavy.I = 0.5;
        assertEquals("rain", Climate.localFog(plains, 6000, 15, heavy));
        heavy.wind = 14;
        assertEquals("blizzard", Climate.localFog(plains, 6000, -8, heavy));
        WeatherModel.State hot = calm();
        hot.A = 6;
        assertEquals("haze", Climate.localFog(desert, 6000, 38, hot));
    }

    // ---------------------------------------------------------------- systems

    static final class Rig {
        final DataStore store = new DataStore();
        final Config config = new Config(LOG);
        final Performance perf = new Performance();
        final List<Regions.PlayerPos> players = new ArrayList<>();
        final List<String> vanilla = new ArrayList<>();
        final Regions regions;
        final WeatherSystem weather;
        final ClimateSystem climate;
        long time = 6000;
        int biomeLookups;

        Rig(DataStore saved, java.util.function.IntFunction<String> biomeByX) {
            if (saved != null) store.restore(saved.snapshot());
            WeatherSystem.defineSettings(config);
            players.add(new Regions.PlayerPos(0, 8, 8));
            regions = new Regions(store, LOG, perf, () -> players, (d, x, z) -> true);
            weather = new WeatherSystem(store, config, LOG, (t, d) -> vanilla.add(t), "seed");
            climate = new ClimateSystem(regions, weather, new ClimateSystem.Host() {
                public String biomeId(int dim, int x, int y, int z) { biomeLookups++; return biomeByX.apply(x); }
                public Climate.BiomeInfo info(String id) {
                    if (id.contains("desert")) return new Climate.BiomeInfo(id, 2.0, 0.0, false);
                    if (id.contains("ice")) return new Climate.BiomeInfo(id, 0.0, 0.5, false);
                    return new Climate.BiomeInfo(id, 0.8, 0.4, false);
                }
                public Integer surfaceY(int dim, int x, int z) { return 70; }
                public long timeOfDay() { return time; }
            }, LOG);
        }

        Rig() {
            this(null, x -> "minecraft:plains");
        }
    }

    @Test
    public void vanillaFollowsEngineAndPlayersWeatherCommandIsFollowed() {
        Rig r = new Rig();
        r.weather.force("THUNDERSTORM", 5000, 0);
        assertEquals("THUNDER", r.vanilla.get(r.vanilla.size() - 1));
        int n = r.vanilla.size();
        r.weather.update(ctx(100, 100));
        assertEquals("no resend when nothing changed", n, r.vanilla.size());
        r.weather.update(ctx(100 + WeatherSystem.VANILLA_REFRESH, 100));
        assertEquals("refreshed", n + 1, r.vanilla.size());
        r.weather.onVanillaChange("THUNDER", 100 + WeatherSystem.VANILLA_REFRESH + 1);
        assertEquals("own change ignored", 0, r.weather.externalChanges());
        r.weather.onVanillaChange("CLEAR", 5000);
        assertEquals(1, r.weather.externalChanges());
        assertEquals("CLEAR", r.weather.state().forceType);
    }

    @Test
    public void weatherStateIsSavedAndRestored() {
        Rig a = new Rig();
        for (int t = 0; t < 30; t++) a.weather.update(ctx(t * 100L, 100));
        a.weather.force("FOG", 9000, 3000);
        assertTrue(a.weather.save());
        assertFalse(a.weather.save());
        Rig b = new Rig(a.store, x -> "minecraft:plains");
        assertTrue(b.weather.restored());
        assertEquals(a.weather.state().toMap(), b.weather.state().toMap());
    }

    @Test
    public void climateInitialisesOnceAndRainWetsNearMoreOftenThanFar() {
        Rig r = new Rig();
        r.regions.update(0);
        for (int i = 0; i < 60; i++) r.climate.update(ctx(i * 20L, 20));
        assertEquals(r.regions.active().size(), ((Number) r.climate.describe().get("regionsWithClimate")).intValue());
        assertEquals(r.regions.active().size(), r.biomeLookups);
        Regions.Active near = r.regions.regionAt(0, 8, 8);
        Regions.Active far = r.regions.regionAt(0, 8 + 96, 8);
        int soil0 = ClimateSystem.soilPermille(ClimateSystem.data(near));
        r.weather.force("HEAVY_RAIN", 100000, 0);
        int nearUpdates = 0, farUpdates = 0;
        long ln = near.lastSim, lf = far.lastSim;
        for (long t = 0; t < 4000; t += 20) {
            r.weather.update(ctx(t, 20));
            r.climate.update(ctx(t, 20));
            if (near.lastSim != ln) { nearUpdates++; ln = near.lastSim; }
            if (far.lastSim != lf) { farUpdates++; lf = far.lastSim; }
        }
        assertTrue(ClimateSystem.soilPermille(ClimateSystem.data(near)) > soil0 + 100);
        assertTrue(nearUpdates >= 3 * farUpdates);
    }

    @Test
    public void climateCatchUpAfterAbsence() {
        Rig r = new Rig();
        r.regions.update(0);
        for (int i = 0; i < 60; i++) r.climate.update(ctx(i * 20L, 20));
        List<Object> c = ClimateSystem.data(r.regions.regionAt(0, 8, 8));
        c.set(0, 50);
        r.players.set(0, new Regions.PlayerPos(0, 3200, 0));
        r.regions.update(2000);
        r.weather.state().rainEma = 0.6;
        r.players.set(0, new Regions.PlayerPos(0, 8, 8));
        r.regions.update(2000 + 240000);
        assertTrue("soil " + c.get(0), ((Number) c.get(0)).intValue() > 300);
        assertTrue(r.climate.caughtUp > 0);
    }

    @Test
    public void sampleReadsLocalConditions() {
        Rig r = new Rig(null, x -> x < 64 ? "minecraft:desert" : "minecraft:ice_flats");
        r.regions.update(0);
        for (int i = 0; i < 60; i++) r.climate.update(ctx(i * 20L, 20));
        r.time = 8000;
        ClimateSystem.Sample hot = r.climate.sample(0, 8, 70, 8);
        ClimateSystem.Sample cold = r.climate.sample(0, 80, 70, 8);
        assertEquals("desert", hot.biome);
        assertTrue(hot.tempC > 30 && cold.tempC < 0);
        assertEquals(45, r.climate.sample(-1, 0, 0, 0).tempC, 0);
    }

    @Test
    public void fogOnlyOutdoorsInOverworldAndRefreshed() {
        Rig r = new Rig(null, x -> "minecraft:river");
        r.regions.update(0);
        for (int i = 0; i < 60; i++) r.climate.update(ctx(i * 20L, 20));
        r.weather.force("FOG", 100000, 0);
        r.weather.update(ctx(0, 500));
        List<String> a = new ArrayList<>(), b = new ArrayList<>();
        List<FogSystem.FogPlayer> players = new ArrayList<>();
        players.add(player("a", 0, true, a));
        players.add(player("b", 0, false, b));
        FogSystem fog = new FogSystem(() -> players, r.climate, LOG);
        fog.update(ctx(0, 0));
        assertEquals("dense", a.get(a.size() - 1));
        assertNull(b.get(b.size() - 1));
        fog.update(ctx(40, 40));
        assertEquals(1, a.size());
        fog.update(ctx(FogSystem.FOG_REFRESH_TICKS + 1, 40));
        assertEquals(2, a.size());
        fog.stop();
        assertNull(a.get(a.size() - 1));
    }

    private static FogSystem.FogPlayer player(String id, int dim, boolean outdoors, List<String> sink) {
        return new FogSystem.FogPlayer() {
            public String id() { return id; }
            public int dim() { return dim; }
            public double x() { return 8; }
            public double y() { return 70; }
            public double z() { return 8; }
            public boolean outdoors() { return outdoors; }
            public void setFog(String kind) { sink.add(kind); }
        };
    }
}
