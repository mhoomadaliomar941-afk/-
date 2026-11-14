package com.realisticworld.core;

import com.realisticworld.sim.Regions;
import com.realisticworld.water.WaterSystem;
import com.realisticworld.weather.ClimateSystem;
import com.realisticworld.weather.FogSystem;
import com.realisticworld.weather.WeatherSystem;

import java.util.ArrayList;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;

/**
 * Builds the whole mod from its parts (the Java counterpart of the Bedrock
 * createRuntime). Nothing here touches Minecraft: the game is reached only
 * through Host, so the full engine runs in unit tests and in a benchmark.
 */
public final class Engine {
    public static final String VERSION = "0.3.0";
    public static final int SAVE_INTERVAL_TICKS = 200;
    public static final int CHECKPOINT_EVERY_SAVES = 6;

    /** Everything the engine needs from the game. */
    public interface Host {
        long maxHeapBytes();

        String worldSeed();

        List<Regions.PlayerPos> players();

        boolean isLoaded(int dim, int blockX, int blockZ);

        void setVanillaWeather(String type, int durationTicks);

        ClimateSystem.Host climate();

        List<FogSystem.FogPlayer> fogPlayers();

        List<Hud.HudPlayer> hudPlayers();

        WaterSystem.World water();

        RwLog.Sink logSink();

        long nanoTime();
    }

    public final RwLog log;
    public final DataStore store;
    public final Config config;
    public final Performance perf;
    public final Scheduler scheduler;
    public final Regions regions;
    public final WeatherSystem weather;
    public final ClimateSystem climate;
    public final FogSystem fog;
    public final WaterSystem water;
    public final Hud hud;
    public final List<String> loadWarnings;
    private boolean errorsDirty;
    private long saves;
    private double lastSaveMs;
    private Long tickAtLastSelfTest;

    public Engine(Host host, DataStore store) {
        this.store = store;
        this.log = new RwLog(host.logSink());
        this.config = new Config(log);

        Map<String, Object> savedErrors = store.load("errlog");
        if (savedErrors != null) {
            List<RwLog.Entry> list = new ArrayList<>();
            for (Object o : DataStore.getList(savedErrors, "errors")) {
                if (!(o instanceof Map)) continue;
                @SuppressWarnings("unchecked")
                Map<String, Object> e = (Map<String, Object>) o;
                list.add(new RwLog.Entry(DataStore.getLong(e, "tick", 0), RwLog.Level.ERROR, DataStore.getString(e, "source", "?"), DataStore.getString(e, "message", "")));
            }
            log.restoreErrors(list);
        }
        log.onError(() -> errorsDirty = true);

        config.defineSystem("regions", "Region tracker (LOD)", true);
        config.defineSystem("weather", "Weather engine", true);
        config.defineSystem("climate", "Regional climate (temperature, soil moisture)", true);
        config.defineSystem("fog", "Weather fog", true);
        config.defineSystem("water", "Water physics", true);
        config.defineSystem("hud", "Status HUD", true);
        WeatherSystem.defineSettings(config);
        WaterSystem.defineSettings(config);
        config.setDefault(Config.PROFILE, Performance.suggest(host.maxHeapBytes()));
        loadWarnings = config.load(store.load("settings"));
        log.setLevel(RwLog.Level.valueOf(config.string(Config.LOG_LEVEL)));

        perf = new Performance();
        perf.setProfile(config.string(Config.PROFILE));
        perf.setAdaptive(config.bool(Config.ADAPTIVE));

        long startTick = DataStore.getLong(store.load("clock"), "tick", 0);
        scheduler = new Scheduler(perf, log, host::nanoTime, startTick);
        log.setTickSource(scheduler::ticks);

        regions = new Regions(store, log, perf, host::players, host::isLoaded);
        weather = new WeatherSystem(store, config, log, host::setVanillaWeather, host.worldSeed());
        climate = new ClimateSystem(regions, weather, host.climate(), log);
        fog = new FogSystem(host::fogPlayers, climate, log);
        water = new WaterSystem(store, config, log, regions, climate, weather, perf, scheduler, host.water());
        hud = new Hud(host::hudPlayers,
            () -> Hud.formatPerf(scheduler.paused(), perf.avgTickMs(), perf.profile().tickBudgetMs, perf.throttle(),
                regions.count(Regions.Tier.NEAR), regions.count(Regions.Tier.MEDIUM), regions.count(Regions.Tier.FAR), scheduler.queueSize()),
            p -> {
                ClimateSystem.Sample s = climate.sample(p.dim(), p.x(), p.y(), p.z());
                return s == null ? null : Hud.formatWeather(weather.summary(), s.tempC, s.humidity, s.wind, s.soil, s.precip, s.fog);
            });

        Scheduler.SimSystem persistence = new Scheduler.SimSystem() {
            public String id() { return "persistence"; }
            public String name() { return "Save cycle"; }
            public int intervalTicks() { return SAVE_INTERVAL_TICKS; }
            public boolean core() { return true; }
            public int update(Scheduler.Context ctx) { return saveNow(false); }
            public Map<String, Object> describe() {
                Map<String, Object> m = new LinkedHashMap<>();
                m.put("saves", saves);
                m.put("lastSaveMs", lastSaveMs);
                m.put("dirtySectors", regions.dirtyCount());
                return m;
            }
        };
        scheduler.register(persistence, true);
        scheduler.register(regions.tracker(), config.isSystemEnabled("regions"));
        scheduler.register(weather, config.isSystemEnabled("weather"));
        scheduler.register(climate, config.isSystemEnabled("climate"));
        scheduler.register(fog, config.isSystemEnabled("fog"));
        scheduler.register(water, config.isSystemEnabled("water"));
        scheduler.register(hud, config.isSystemEnabled("hud"));
        if (!config.bool(Config.ENABLED)) scheduler.pause();

        config.onChange((key, value) -> {
            switch (key) {
                case Config.PROFILE:
                    perf.setProfile((String) value);
                    regions.setCapacity(perf.profile().maxCachedRegions);
                    break;
                case Config.ADAPTIVE:
                    perf.setAdaptive((Boolean) value);
                    break;
                case Config.LOG_LEVEL:
                    log.setLevel(RwLog.Level.valueOf((String) value));
                    break;
                case Config.ENABLED:
                    if ((Boolean) value) scheduler.resume();
                    else scheduler.pause();
                    break;
                case WeatherSystem.SYNC_VANILLA:
                    if ((Boolean) value) weather.syncVanilla(scheduler.ticks(), true);
                    break;
                case WeatherSystem.RAIN_FREQUENCY:
                    weather.invalidateForecast();
                    break;
                default:
                    if (key.startsWith("system.")) scheduler.setEnabled(key.substring(7), (Boolean) value);
            }
        });

        log.info("core", "Realistic World " + VERSION + " started (settings: " + (store.load("settings") == null ? "defaults" : "saved")
            + ", profile " + perf.profile().name + ", sim tick " + startTick + ", weather " + (weather.restored() ? "restored" : "new") + ")");
    }

    public void tick() {
        scheduler.tick();
    }

    /**
     * Write everything that changed into the store. Region sectors are capped
     * per call by the profile; full = true writes everything (world saving,
     * player leaving, "Save now").
     */
    public int saveNow(boolean full) {
        long t0 = System.nanoTime();
        if (full || saves % CHECKPOINT_EVERY_SAVES == 0) regions.checkpoint(scheduler.ticks());
        int writes = 0;
        Map<String, Object> clock = new LinkedHashMap<>();
        clock.put("tick", scheduler.ticks());
        store.save("clock", clock);
        writes++;
        if (config.isDirty()) {
            store.save("settings", config.save());
            writes++;
        }
        if (weather.save()) writes++;
        writes += water.save(full ? Integer.MAX_VALUE : 8);
        writes += regions.flush(full ? Integer.MAX_VALUE : perf.profile().sectorWritesPerSave);
        if (errorsDirty) {
            List<Object> list = new ArrayList<>();
            for (RwLog.Entry e : log.errors()) {
                Map<String, Object> m = new LinkedHashMap<>();
                m.put("tick", e.tick);
                m.put("source", e.source);
                m.put("message", e.message);
                list.add(m);
            }
            Map<String, Object> errs = new LinkedHashMap<>();
            errs.put("errors", list);
            store.save("errlog", errs);
            errorsDirty = false;
            writes++;
        }
        saves++;
        lastSaveMs = (System.nanoTime() - t0) / 1e6;
        return writes;
    }

    public List<SelfTest.Result> selfTest() {
        List<SelfTest.Result> results = SelfTest.run(scheduler, tickAtLastSelfTest);
        tickAtLastSelfTest = scheduler.ticks();
        int failed = 0;
        for (SelfTest.Result r : results) if (!r.ok) failed++;
        if (failed > 0) log.warn("selftest", failed + " check(s) failed");
        else log.info("selftest", "all checks passed");
        return results;
    }

    /** Systems the player may switch on/off. */
    public List<Scheduler.Info> toggleableSystems() {
        List<Scheduler.Info> out = new ArrayList<>();
        for (Scheduler.Info i : scheduler.list()) if (!i.core) out.add(i);
        return out;
    }
}
