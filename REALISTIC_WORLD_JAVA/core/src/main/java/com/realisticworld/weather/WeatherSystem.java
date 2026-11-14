package com.realisticworld.weather;

import com.realisticworld.core.Config;
import com.realisticworld.core.DataStore;
import com.realisticworld.core.RwLog;
import com.realisticworld.core.Scheduler;
import com.realisticworld.util.MathUtil;
import com.realisticworld.util.Rng;

import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;

/**
 * Weather engine system: advances the global model, keeps the vanilla weather
 * (rain/thunder visuals and mechanics) in step with it, follows /weather
 * commands typed by players, and saves its state with the world. Minecraft has
 * one weather per dimension, so the vanilla weather shows the global state;
 * local differences (snow vs rain, fog, soil) come from the climate system.
 */
public final class WeatherSystem implements Scheduler.SimSystem {
    public static final int VANILLA_DURATION = 6000;
    public static final int VANILLA_REFRESH = 1200;
    public static final int EXTERNAL_FORCE_TICKS = 6000;
    public static final String KEY = "weather";
    public static final String RAIN_FREQUENCY = "weather.rainFrequency";
    public static final String SYNC_VANILLA = "weather.syncVanilla";

    /** Sets the vanilla weather: "CLEAR", "RAIN" or "THUNDER" for a number of ticks. */
    public interface VanillaWeather {
        void set(String type, int durationTicks);
    }

    private final DataStore store;
    private final Config config;
    private final RwLog log;
    private final VanillaWeather vanilla;
    private WeatherModel.State state;
    private final boolean restored;
    private boolean dirty;
    private String expectType;
    private long expectTick = Long.MIN_VALUE;
    private String lastSent = "";
    private long lastSentTick = Long.MIN_VALUE / 2;
    private int externalChanges;
    private long forecastTick = Long.MIN_VALUE;
    private List<WeatherModel.Period> forecastCache;

    public WeatherSystem(DataStore store, Config config, RwLog log, VanillaWeather vanilla, String worldSeed) {
        this.store = store;
        this.config = config;
        this.log = log;
        this.vanilla = vanilla;
        Map<String, Object> saved = store.load(KEY);
        WeatherModel.State s = WeatherModel.State.fromMap(saved);
        if (saved != null && s == null) log.warn("weather", "saved weather state invalid; starting fresh");
        restored = s != null;
        state = s != null ? s : WeatherModel.create(Rng.fromSeed(worldSeed));
    }

    public static void defineSettings(Config c) {
        c.define(RAIN_FREQUENCY, Config.Def.choice("NORMAL", "Rain frequency", "LOW", "NORMAL", "HIGH"));
        c.define(SYNC_VANILLA, Config.Def.bool(true, "Show the engine's weather in the world (rain, thunder)"));
    }

    public WeatherModel.State state() {
        return state;
    }

    public boolean restored() {
        return restored;
    }

    public int externalChanges() {
        return externalChanges;
    }

    public String lastSent() {
        return lastSent;
    }

    @Override public String id() { return "weather"; }
    @Override public String name() { return "Weather engine"; }
    @Override public int intervalTicks() { return 100; }
    @Override public boolean scalable() { return true; }

    @Override
    public int update(Scheduler.Context ctx) {
        long dt = ctx.deltaTicks > 0 ? ctx.deltaTicks : intervalTicks();
        WeatherModel.step(state, dt, config.string(RAIN_FREQUENCY));
        dirty = true;
        syncVanilla(ctx.tick, false);
        return (int) Math.ceil(dt / 250.0);
    }

    /** Send the vanilla weather if it changed or is about to run out. */
    public void syncVanilla(long tick, boolean force) {
        if (!config.bool(SYNC_VANILLA)) return;
        String want = WeatherModel.vanillaWeather(state);
        if (!force && want.equals(lastSent) && tick - lastSentTick < VANILLA_REFRESH) return;
        try {
            expectType = want;
            expectTick = tick;
            vanilla.set(want, VANILLA_DURATION);
            lastSent = want;
            lastSentTick = tick;
        } catch (RuntimeException e) {
            log.error("weather", "could not set the vanilla weather", e);
        }
    }

    /**
     * The vanilla weather changed (seen by polling the world each tick). Our own
     * changes are ignored; anything else (a player typing /weather) becomes a
     * forced condition for EXTERNAL_FORCE_TICKS.
     */
    public void onVanillaChange(String newWeather, long tick) {
        if (newWeather.equals(expectType) && tick - expectTick <= 40) return;
        if (!config.bool(SYNC_VANILLA)) return;
        String type = "CLEAR".equals(newWeather) ? "CLEAR" : "RAIN".equals(newWeather) ? "MODERATE_RAIN" : "THUNDER".equals(newWeather) ? "THUNDERSTORM" : null;
        if (type == null) return;
        WeatherModel.force(state, type, EXTERNAL_FORCE_TICKS);
        externalChanges++;
        lastSent = newWeather;
        lastSentTick = tick;
        dirty = true;
        forecastCache = null;
        log.info("weather", "vanilla weather changed to " + newWeather + " (e.g. /weather); following it for 5 minutes");
    }

    public boolean force(String type, long durationTicks, long tick) {
        if (!WeatherModel.FORCEABLE.containsKey(type)) return false;
        WeatherModel.force(state, type, durationTicks);
        dirty = true;
        forecastCache = null;
        syncVanilla(tick, true);
        return true;
    }

    public void release(long tick) {
        WeatherModel.release(state);
        dirty = true;
        forecastCache = null;
        syncVanilla(tick, true);
    }

    public void invalidateForecast() {
        forecastCache = null;
    }

    /** 3-day forecast, recomputed at most every 500 ticks. */
    public List<WeatherModel.Period> forecast() {
        if (forecastCache == null || Math.abs(state.tick - forecastTick) >= 500) {
            forecastCache = WeatherModel.forecast(state, 72, 6, 6, config.string(RAIN_FREQUENCY));
            forecastTick = state.tick;
        }
        return forecastCache;
    }

    /** @return true if written. */
    public boolean save() {
        if (!dirty) return false;
        store.save(KEY, state.toMap());
        dirty = false;
        return true;
    }

    public Map<String, Object> summary() {
        WeatherModel.State s = state;
        Map<String, Object> m = new LinkedHashMap<>();
        m.put("condition", WeatherModel.classify(s));
        m.put("raining", s.raining);
        m.put("storm", s.storm);
        m.put("intensity", MathUtil.round(s.I, 2));
        m.put("humidity", (int) Math.round(s.H * 100));
        m.put("pressure", MathUtil.round(s.P, 2));
        m.put("anomaly", MathUtil.round(s.A, 1));
        m.put("wind", MathUtil.round(s.wind, 1));
        m.put("windFrom", WeatherModel.compass(s.windDir));
        m.put("dryDays", MathUtil.round(s.dryDays, 1));
        m.put("forced", s.forceType);
        m.put("forcedTicksLeft", s.forceType == null ? 0 : Math.max(0, s.forceUntil - s.tick));
        return m;
    }

    @Override
    public Map<String, Object> describe() {
        Map<String, Object> m = new LinkedHashMap<>();
        m.put("condition", WeatherModel.classify(state));
        m.put("forced", state.forceType == null ? "no" : state.forceType);
        m.put("vanilla", lastSent.isEmpty() ? "-" : lastSent);
        m.put("external", externalChanges);
        return m;
    }
}
