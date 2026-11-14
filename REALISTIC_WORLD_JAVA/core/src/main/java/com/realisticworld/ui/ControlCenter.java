package com.realisticworld.ui;

import com.realisticworld.core.Config;
import com.realisticworld.core.Engine;
import com.realisticworld.core.Performance;
import com.realisticworld.core.RwLog;
import com.realisticworld.core.Scheduler;
import com.realisticworld.core.SelfTest;
import com.realisticworld.sim.Regions;
import com.realisticworld.water.Bodies;
import com.realisticworld.water.WaterSystem;
import com.realisticworld.weather.ClimateSystem;
import com.realisticworld.weather.WeatherModel;
import com.realisticworld.weather.WeatherSystem;

import java.util.ArrayList;
import java.util.Arrays;
import java.util.List;
import java.util.Locale;
import java.util.Map;
import java.util.function.Consumer;

/**
 * REALISTIC WORLD CONTROL CENTER (same pages as the Bedrock version). Every
 * button does something; systems not built yet are listed as text. Operators
 * change settings; other players get read-only pages and their own HUD toggles.
 */
public final class ControlCenter {
    public static final String TITLE = "REALISTIC WORLD CONTROL CENTER";
    static final List<String> PLANNED = Arrays.asList("Vegetation & Seasons (phase 4)", "Ecosystem & Wildlife (phase 5)",
        "Fire, Disasters & Geology (phase 6)", "Survival & Atmosphere (phase 7)");
    static final List<String> PROFILES = Arrays.asList("LOW", "MEDIUM", "ULTRA");
    static final List<String> LOG_LEVELS = Arrays.asList("DEBUG", "INFO", "WARN", "ERROR");
    static final List<String> FREQUENCIES = Arrays.asList("LOW", "NORMAL", "HIGH");
    static final List<String> FORCE_TYPES = new ArrayList<>(WeatherModel.FORCEABLE.keySet());
    static final int MINUTE = 1200;

    /** The player looking at the control center. */
    public interface Viewer {
        String name();

        boolean isOperator();

        boolean perfHud();

        void setPerfHud(boolean on);

        boolean weatherHud();

        void setWeatherHud(boolean on);

        int dim();

        double x();

        double y();

        double z();

        void message(String text);

        /** Show a form; onReply runs with the answer (or a cancel). */
        void show(Form form, Consumer<Form.Reply> onReply);
    }

    private final Engine e;

    public ControlCenter(Engine engine) {
        this.e = engine;
    }

    private static String pretty(String s) {
        return s.replace('_', ' ').toLowerCase(Locale.ROOT);
    }

    private static String onOff(boolean v) {
        return v ? "§aON§r" : "§cOFF§r";
    }

    // ---------------------------------------------------------------- main

    public void open(Viewer v) {
        Form f = Form.action(TITLE);
        int errors = e.log.errors().size();
        f.body("Simulation: " + (e.scheduler.paused() ? "§ePAUSED§r" : "§aRUNNING§r") + "  |  Profile: §b" + e.perf.profile().name + "§r\n"
            + "Errors logged: " + (errors > 0 ? "§c" + errors + "§r" : "0")
            + (v.isOperator() ? "" : "\n§7Read-only: only operators can change settings.§r"));
        List<Runnable> actions = new ArrayList<>();
        addButton(f, actions, "Dashboard", () -> dashboard(v));
        addButton(f, actions, "Weather", () -> weather(v));
        addButton(f, actions, "Water Physics", () -> water(v));
        addButton(f, actions, "Systems", () -> systems(v));
        addButton(f, actions, "Performance", () -> performance(v));
        addButton(f, actions, "General Settings", () -> general(v));
        addButton(f, actions, "Error Log (" + errors + ")", () -> errorLog(v));
        addButton(f, actions, "Run Self-Test", () -> selfTest(v));
        StringBuilder planned = new StringBuilder("§7Coming later:§r");
        for (String p : PLANNED) planned.append("\n§7- ").append(p).append("§r");
        f.label(planned.toString());
        v.show(f, r -> run(actions, r));
    }

    private static void addButton(Form f, List<Runnable> actions, String text, Runnable action) {
        f.button(text);
        actions.add(action);
    }

    private static void run(List<Runnable> actions, Form.Reply r) {
        if (r.canceled || r.selection < 0 || r.selection >= actions.size()) return;
        actions.get(r.selection).run();
    }

    // ---------------------------------------------------------------- dashboard

    public void dashboard(Viewer v) {
        Performance p = e.perf;
        Map<String, Object> reg = e.regions.describe();
        Map<String, Object> w = e.weather.summary();
        Map<String, Object> cl = e.climate.describe();
        Map<String, Object> wa = e.water.describe();
        List<String> lines = new ArrayList<>();
        lines.add("§lEngine§r");
        lines.add("State: " + (e.scheduler.paused() ? "§ePAUSED" : "§aRUNNING") + "§r   Sim tick: " + e.scheduler.ticks());
        lines.add("");
        lines.add("§lPerformance (mod time per tick, not FPS)§r");
        lines.add(String.format(Locale.ROOT, "Profile: %s   Budget: %s ms/tick", p.profile().name, fmt(p.profile().tickBudgetMs)));
        lines.add(String.format(Locale.ROOT, "Average: %.3f ms   Peak: %.3f ms", p.avgTickMs(), p.peakTickMs()));
        lines.add("Ticks over budget: " + p.overBudgetTicks() + " / " + p.ticksMeasured());
        lines.add("Adaptive: " + onOff(p.adaptive()) + "   Throttle: x" + p.throttle());
        lines.add("Queue: " + e.scheduler.queueSize() + " (rejected " + e.scheduler.queueRejected() + ")   Deferred runs: " + e.scheduler.deferredRuns());
        lines.add("");
        lines.add("§lSimulation regions§r");
        lines.add("NEAR " + reg.get("near") + "   MEDIUM " + reg.get("medium") + "   FAR " + reg.get("far"));
        lines.add("Sectors in memory: " + reg.get("cachedSectors") + " (unsaved " + reg.get("dirtySectors") + ")");
        lines.add("Regions resumed: " + reg.get("resumed") + " (last away " + reg.get("lastResumeAwayTicks") + " ticks)");
        lines.add("");
        lines.add("§lWeather§r");
        lines.add(pretty(String.valueOf(w.get("condition"))) + (w.get("forced") != null ? " (forced)" : "") + "   wind " + w.get("wind") + " m/s " + w.get("windFrom") + "   humidity " + w.get("humidity") + "%");
        lines.add("Regions with climate data: " + cl.get("regionsWithClimate") + "   average soil moisture " + cl.get("avgSoilPct") + "%");
        lines.add("");
        lines.add("§lWater§r");
        lines.add("Tanks " + wa.get("tanks") + "   pools " + wa.get("pools") + "   " + wa.get("liters") + " L   scans running " + wa.get("scans") + "   blocks changed " + wa.get("blockOps"));
        lines.add("");
        lines.add("§lSaved data§r");
        lines.add("Entries: " + e.store.size() + "   Writes: " + e.store.saves());
        lines.add("");
        lines.add("§lSystems§r");
        for (Scheduler.Info i : e.scheduler.list()) {
            lines.add(i.name + ": " + i.state + "  avg " + i.avgMs + " ms, every " + i.intervalTicks + " t, runs " + i.runs);
        }
        Form f = Form.action("Dashboard").body(String.join("\n", lines)).button("Refresh").button("Back");
        v.show(f, r -> {
            if (r.canceled) return;
            if (r.selection == 0) dashboard(v);
            else open(v);
        });
    }

    private static String fmt(double d) {
        return d == Math.rint(d) ? String.valueOf((int) d) : String.valueOf(d);
    }

    // ---------------------------------------------------------------- weather

    public void weather(Viewer v) {
        Map<String, Object> w = e.weather.summary();
        ClimateSystem.Sample s = e.climate.sample(v.dim(), v.x(), v.y(), v.z());
        List<String> lines = new ArrayList<>();
        if (s != null) {
            lines.add("§lHere§r (" + pretty(s.biome) + (s.tier != null ? ", " + s.tier : "") + ")");
            lines.add(String.format(Locale.ROOT, "Temperature: §f%.1f°C§r%s", s.tempC, s.frost ? " §b(frost)§r" : ""));
            lines.add("Precipitation: " + precipText(s.precip));
            lines.add("Air humidity: " + s.humidity + "%   Soil moisture: " + (s.soil == null ? "-" : Math.round(s.soil * 100) + "%"));
            lines.add("Wind: " + s.wind + " m/s from " + w.get("windFrom") + "   Fog: " + (s.fog == null ? "none" : s.fog));
            if (s.drought) lines.add("§6Drought: the ground here is very dry§r");
            lines.add("");
        }
        double pressure = ((Number) w.get("pressure")).doubleValue();
        double anomaly = ((Number) w.get("anomaly")).doubleValue();
        lines.add("§lOverall§r: " + pretty(String.valueOf(w.get("condition"))));
        lines.add("Pressure: " + pressure + " (" + (pressure < 0.4 ? "low" : pressure > 0.6 ? "high" : "normal") + ")   Temperature anomaly: " + (anomaly >= 0 ? "+" : "") + anomaly + "°C");
        lines.add("Days without rain: " + w.get("dryDays"));
        if (w.get("forced") != null) lines.add("§eForced: " + pretty((String) w.get("forced")) + " (" + duration(((Number) w.get("forcedTicksLeft")).longValue()) + " left)§r");
        if (!e.config.isSystemEnabled("weather")) lines.add("§cThe weather engine is switched off (Systems).§r");

        Form f = Form.action("Weather").body(String.join("\n", lines));
        List<Runnable> actions = new ArrayList<>();
        addButton(f, actions, "Forecast (3 days)", () -> forecast(v));
        boolean hud = v.weatherHud();
        addButton(f, actions, "Weather on my HUD: " + (hud ? "ON" : "OFF"), () -> {
            v.setWeatherHud(!hud);
            weather(v);
        });
        if (v.isOperator()) {
            addButton(f, actions, "Force weather...", () -> force(v));
            if (w.get("forced") != null) {
                addButton(f, actions, "Release forced weather", () -> {
                    e.weather.release(e.scheduler.ticks());
                    v.message("§a[Realistic World] Weather returns to natural behaviour.");
                    weather(v);
                });
            }
            addButton(f, actions, "Weather settings", () -> weatherSettings(v));
        }
        addButton(f, actions, "Back", () -> open(v));
        v.show(f, r -> run(actions, r));
    }

    static String precipText(String p) {
        switch (p) {
            case "sleet": return "sleet (rain and snow)";
            default: return p;
        }
    }

    static String duration(long ticks) {
        long sec = Math.round(ticks / 20.0);
        return sec >= 60 ? (sec / 60) + "m " + (sec % 60) + "s" : sec + "s";
    }

    public void forecast(Viewer v) {
        List<String> lines = new ArrayList<>();
        lines.add("Chance of rain and storms in each 6 in-game hours (1 in-game hour = 50 s).");
        lines.add("Worked out by replaying the weather model forward 6 times.");
        lines.add("");
        for (WeatherModel.Period p : e.weather.forecast()) {
            String rain = p.rainChance >= 50 ? "§9" + p.rainChance + "%§r" : p.rainChance + "%";
            String storm = p.stormChance > 0 ? "  storm §e" + p.stormChance + "%§r" : "";
            lines.add("Day " + (p.fromHour / 24 + 1) + " +" + p.fromHour + "-" + p.toHour + "h: rain " + rain + storm + "  wind ≤" + p.maxWind
                + "  temp " + signed(p.anomalyMin) + ".." + signed(p.anomalyMax) + "°C");
        }
        if (e.weather.state().forceType != null) lines.add("\n§eA forced condition is active; the forecast includes it until it ends.§r");
        v.show(Form.action("Forecast").body(String.join("\n", lines)).button("Back"), r -> {
            if (!r.canceled) weather(v);
        });
    }

    private static String signed(double d) {
        return (d >= 0 ? "+" : "") + d;
    }

    public void force(Viewer v) {
        List<String> names = new ArrayList<>();
        for (String t : FORCE_TYPES) names.add(pretty(t));
        Form f = Form.modal("Force weather")
            .label("Holds a condition, then the weather continues naturally from there. The world's rain and thunder follow it.")
            .dropdown("type", "Condition", names, 0)
            .slider("minutes", "Duration (real minutes)", 1, 60, 1, 5)
            .submit("Force");
        v.show(f, r -> {
            if (r.canceled) {
                weather(v);
                return;
            }
            if (!v.isOperator()) return;
            String type = FORCE_TYPES.get(Math.max(0, r.index("type")));
            int minutes = (int) Math.round(r.number("minutes"));
            e.weather.force(type, (long) minutes * MINUTE, e.scheduler.ticks());
            v.message("§a[Realistic World] " + pretty(type) + " for " + minutes + " min.");
            weather(v);
        });
    }

    public void weatherSettings(Viewer v) {
        Form f = Form.modal("Weather settings")
            .dropdown("freq", "Rain frequency (LOW: ~20% of the time, NORMAL: ~26%, HIGH: ~33%)", FREQUENCIES,
                Math.max(0, FREQUENCIES.indexOf(e.config.string(WeatherSystem.RAIN_FREQUENCY))))
            .toggle("sync", "Show the engine's weather in the world (rain, thunder, snow)", e.config.bool(WeatherSystem.SYNC_VANILLA))
            .submit("Apply");
        v.show(f, r -> {
            if (r.canceled) {
                weather(v);
                return;
            }
            if (!v.isOperator()) return;
            e.config.set(WeatherSystem.RAIN_FREQUENCY, FREQUENCIES.get(Math.max(0, r.index("freq"))));
            e.config.set(WeatherSystem.SYNC_VANILLA, r.bool("sync"));
            e.saveNow(false);
            v.message("§a[Realistic World] Weather settings applied.");
            weather(v);
        });
    }

    // ---------------------------------------------------------------- water

    /** Gauge/UI text for a water body. */
    public static List<String> bodyLines(Map<String, Object> i) {
        List<String> out = new ArrayList<>();
        if ("tank".equals(i.get("type"))) {
            long liters = ((Number) i.get("liters")).longValue();
            long cap = ((Number) i.get("capacityLiters")).longValue();
            long pct = cap == 0 ? 0 : Math.round(liters * 100.0 / cap);
            out.add(String.format(Locale.ROOT, "§bTank %s§r: %,d L of %,d L (%d%%)", i.get("id"), liters, cap, pct));
            out.add("Depth " + i.get("depth") + " m, pressure at the bottom " + i.get("bottomPressureKPa") + " kPa");
            double out2 = ((Number) i.get("outflowLps")).doubleValue();
            int openings = ((Number) i.get("openings")).intValue();
            if (out2 > 0) out.add(String.format(Locale.ROOT, "§cLeaking %s L/s§r through %d opening(s); %,d L lost so far", i.get("outflowLps"), openings, ((Number) i.get("leakedLiters")).longValue()));
            else if (openings > 0) out.add("Openings: " + openings + " (water is at or below them)");
            else out.add("Sealed: no openings");
            if (((Number) i.get("closedValves")).intValue() > 0) out.add("Closed valves: " + i.get("closedValves"));
            if (((Number) i.get("pipes")).intValue() > 0) out.add("Pipes: " + i.get("pipes"));
            if (Boolean.TRUE.equals(i.get("frozen"))) out.add("§bSurface frozen§r");
            return out;
        }
        out.add(String.format(Locale.ROOT, "§9%s %s§r: %,d L over %d m², deepest %s m", Boolean.TRUE.equals(i.get("flood")) ? "Flood" : "Pool",
            i.get("id"), ((Number) i.get("liters")).longValue(), ((Number) i.get("areaM2")).intValue(), i.get("maxDepth")));
        out.add(String.format(Locale.ROOT, "Soaked into the ground: %,d L, evaporated: %,d L", ((Number) i.get("absorbedLiters")).longValue(), ((Number) i.get("evaporatedLiters")).longValue()));
        if (Boolean.TRUE.equals(i.get("frozen"))) out.add("§bFrozen§r");
        return out;
    }

    public void water(Viewer v) {
        Map<String, Object> d = e.water.describe();
        List<String> lines = new ArrayList<>();
        lines.add(String.format(Locale.ROOT, "Simulated: %s tank(s), %s pool(s), %,d L in total", d.get("tanks"), d.get("pools"), ((Number) d.get("liters")).longValue()));
        if (!e.config.isSystemEnabled("water")) lines.add("§cWater physics is switched off (Systems).§r");
        lines.add("");
        List<Bodies.Body> near = e.water.near(v.dim(), v.x(), v.z(), 64);
        lines.add(near.isEmpty() ? "Nothing simulated near you." : "§lNear you§r");
        for (Bodies.Body b : near.subList(0, Math.min(6, near.size()))) {
            lines.addAll(bodyLines(e.water.describeBody(b)));
            lines.add("");
        }
        lines.add("§7Tanks are found when you break a block next to still water, or with the Water Gauge. Lakes and the sea are never drained.§r");
        Form f = Form.action("Water Physics").body(String.join("\n", lines));
        List<Runnable> actions = new ArrayList<>();
        addButton(f, actions, "Refresh", () -> water(v));
        if (v.isOperator()) addButton(f, actions, "Water settings", () -> waterSettings(v));
        addButton(f, actions, "Back", () -> open(v));
        v.show(f, r -> run(actions, r));
    }

    public void waterSettings(Viewer v) {
        Form f = Form.modal("Water settings")
            .toggle("auto", "Detect tanks when a block next to still water is broken", e.config.bool(WaterSystem.AUTO_DETECT))
            .toggle("puddles", "Rain on soaked ground forms puddles, ponds and floods in low places", e.config.bool(WaterSystem.RAIN_PUDDLES))
            .submit("Apply");
        v.show(f, r -> {
            if (r.canceled) {
                water(v);
                return;
            }
            if (!v.isOperator()) return;
            e.config.set(WaterSystem.AUTO_DETECT, r.bool("auto"));
            e.config.set(WaterSystem.RAIN_PUDDLES, r.bool("puddles"));
            e.saveNow(false);
            v.message("§a[Realistic World] Water settings applied.");
            water(v);
        });
    }

    // ---------------------------------------------------------------- systems / performance / general

    public void systems(Viewer v) {
        List<Scheduler.Info> list = e.toggleableSystems();
        if (!v.isOperator()) {
            List<String> lines = new ArrayList<>();
            for (Scheduler.Info i : list) lines.add(i.name + ": " + i.state + (i.lastError.isEmpty() ? "" : "\n  §7last error: " + i.lastError + "§r"));
            v.show(Form.action("Systems").body(String.join("\n", lines)).button("Back"), r -> {
                if (!r.canceled) open(v);
            });
            return;
        }
        Form f = Form.modal("Systems").label("Switch each system on or off. A FAULTED system stopped itself after repeated errors; switching it on again restarts it.");
        for (Scheduler.Info i : list) f.toggle(i.id, i.name + (i.faulted ? " §c(FAULTED)§r" : ""), i.enabled && !i.faulted);
        f.submit("Apply");
        v.show(f, r -> {
            if (r.canceled) return;
            for (Scheduler.Info i : list) {
                boolean want = r.bool(i.id);
                if (want != (i.enabled && !i.faulted)) {
                    if (want && i.faulted) e.scheduler.setEnabled(i.id, true); // clears the fault
                    e.config.setSystemEnabled(i.id, want);
                }
            }
            e.saveNow(false);
            v.message("§a[Realistic World] Systems updated.");
            open(v);
        });
    }

    public void performance(Viewer v) {
        boolean op = v.isOperator();
        Performance p = e.perf;
        Form f = Form.modal("Performance").label(String.format(Locale.ROOT,
            "Average mod time: %.3f ms of %s ms budget, throttle x%d.\nLOW: 2 ms, slower updates, small radius. MEDIUM: 4 ms. ULTRA: 6 ms, biggest radius.",
            p.avgTickMs(), fmt(p.profile().tickBudgetMs), p.throttle()));
        if (op) {
            f.dropdown("profile", "Performance profile", PROFILES, Math.max(0, PROFILES.indexOf(p.profile().name)));
            f.toggle("adaptive", "Adaptive throttling (slow updates down when over budget)", p.adaptive());
        }
        f.toggle("hud", "Show status HUD for me", v.perfHud()).submit("Apply");
        v.show(f, r -> {
            if (r.canceled) return;
            if (op) {
                e.config.set(Config.PROFILE, PROFILES.get(Math.max(0, r.index("profile"))));
                e.config.set(Config.ADAPTIVE, r.bool("adaptive"));
                p.resetPeak();
                e.saveNow(false);
            }
            v.setPerfHud(r.bool("hud"));
            v.message("§a[Realistic World] Performance settings applied.");
            open(v);
        });
    }

    public void general(Viewer v) {
        if (!v.isOperator()) {
            v.message("§e[Realistic World] Only operators can change general settings.");
            open(v);
            return;
        }
        Form f = Form.action("General Settings")
            .body("Simulation: " + onOff(e.config.bool(Config.ENABLED)) + "   Log level: " + e.config.string(Config.LOG_LEVEL))
            .button("Edit settings").button("Save now").button("Reset all settings").button("Back");
        v.show(f, r -> {
            if (r.canceled) return;
            switch (r.selection) {
                case 0: editGeneral(v); break;
                case 1:
                    int writes = e.saveNow(true);
                    v.message("§a[Realistic World] Saved (" + writes + " writes).");
                    general(v);
                    break;
                case 2: confirmReset(v); break;
                default: open(v);
            }
        });
    }

    private void editGeneral(Viewer v) {
        Form f = Form.modal("General Settings")
            .toggle("enabled", "Simulation enabled (off pauses every system)", e.config.bool(Config.ENABLED))
            .dropdown("log", "Log level (game log)", LOG_LEVELS, Math.max(0, LOG_LEVELS.indexOf(e.config.string(Config.LOG_LEVEL))))
            .submit("Apply");
        v.show(f, r -> {
            if (r.canceled) {
                general(v);
                return;
            }
            e.config.set(Config.ENABLED, r.bool("enabled"));
            e.config.set(Config.LOG_LEVEL, LOG_LEVELS.get(Math.max(0, r.index("log"))));
            e.saveNow(false);
            v.message("§a[Realistic World] Settings applied.");
            general(v);
        });
    }

    private void confirmReset(Viewer v) {
        v.show(Form.message("Reset all settings?", "Every setting goes back to its default. Saved simulation data (regions, clock, water) is kept.", "Reset", "Cancel"), r -> {
            if (r.canceled) return;
            if (r.selection == 0) {
                e.config.reset();
                e.saveNow(false);
                v.message("§a[Realistic World] Settings reset to defaults.");
            }
            general(v);
        });
    }

    public void errorLog(Viewer v) {
        List<RwLog.Entry> errors = e.log.errors();
        List<String> lines = new ArrayList<>();
        for (int i = errors.size() - 1; i >= Math.max(0, errors.size() - 10); i--) lines.add("§c" + errors.get(i) + "§r");
        Form f = Form.action("Error Log (" + errors.size() + ")").body(errors.isEmpty() ? "No errors recorded." : String.join("\n\n", lines));
        boolean canClear = v.isOperator() && !errors.isEmpty();
        if (canClear) f.button("Clear log");
        f.button("Back");
        v.show(f, r -> {
            if (r.canceled) return;
            if (canClear && r.selection == 0) {
                e.log.clearErrors();
                e.saveNow(false);
                errorLog(v);
            } else {
                open(v);
            }
        });
    }

    public void selfTest(Viewer v) {
        List<SelfTest.Result> results = e.selfTest();
        int passed = 0;
        StringBuilder sb = new StringBuilder();
        for (SelfTest.Result r : results) {
            if (r.ok) passed++;
            sb.append(r.ok ? "§aPASS" : "§cFAIL").append("§r ").append(r.name).append("\n  §7").append(r.detail).append("§r\n");
        }
        String body = passed + "/" + results.size() + " checks passed.\n§7Run it twice: the engine-loop check compares with the previous run.§r\n\n" + sb;
        v.show(Form.action("Self-Test").body(body).button("Run again").button("Back"), r -> {
            if (r.canceled) return;
            if (r.selection == 0) selfTest(v);
            else open(v);
        });
    }

    /** One-line status for /rw status. */
    public String statusLine() {
        return String.format(Locale.ROOT, "Realistic World %s | %s | tick %d\nprofile %s | avg %.3f ms / %s ms | throttle x%d\nregions NEAR %d MEDIUM %d FAR %d | errors %d",
            Engine.VERSION, e.scheduler.paused() ? "PAUSED" : "RUNNING", e.scheduler.ticks(), e.perf.profile().name, e.perf.avgTickMs(),
            fmt(e.perf.profile().tickBudgetMs), e.perf.throttle(), e.regions.count(Regions.Tier.NEAR), e.regions.count(Regions.Tier.MEDIUM),
            e.regions.count(Regions.Tier.FAR), e.log.errors().size());
    }
}
