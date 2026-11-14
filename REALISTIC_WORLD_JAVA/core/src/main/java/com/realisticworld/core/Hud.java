package com.realisticworld.core;

import java.util.ArrayList;
import java.util.List;
import java.util.Map;
import java.util.function.Function;
import java.util.function.Supplier;

/**
 * Optional status lines for players who switch them on: a performance line
 * and a weather line. The server builds the text; the Forge layer sends it to
 * the player's client, which draws it on screen.
 */
public final class Hud implements Scheduler.SimSystem {
    /** A player as the HUD sees it. */
    public interface HudPlayer {
        boolean perfHud();

        boolean weatherHud();

        int dim();

        double x();

        double y();

        double z();

        void show(List<String> lines);
    }

    private final Supplier<List<HudPlayer>> players;
    private final Supplier<String> perfLine;
    private final Function<HudPlayer, String> weatherLine;
    private int shownTo;

    public Hud(Supplier<List<HudPlayer>> players, Supplier<String> perfLine, Function<HudPlayer, String> weatherLine) {
        this.players = players;
        this.perfLine = perfLine;
        this.weatherLine = weatherLine;
    }

    @Override public String id() { return "hud"; }
    @Override public String name() { return "Status HUD"; }
    @Override public int intervalTicks() { return 10; }
    @Override public boolean scalable() { return true; }

    /** Performance line, e.g. "RW 0.42/4ms | N25 M56 F88 | Q0" (green/yellow/red by load). */
    public static String formatPerf(boolean paused, double avgMs, double budgetMs, int throttle, int near, int medium, int far, int queue) {
        if (paused) return "§eRW§r PAUSED";
        String load = avgMs > budgetMs ? "§c" : avgMs > budgetMs * 0.6 ? "§e" : "§a";
        String thr = throttle > 1 ? " §6x" + throttle + "§r" : "";
        return String.format(java.util.Locale.ROOT, "§bRW§r %s%.2f§r/%sms%s | N%d M%d F%d | Q%d",
            load, avgMs, budgetMs == Math.rint(budgetMs) ? String.valueOf((int) budgetMs) : String.valueOf(budgetMs), thr, near, medium, far, queue);
    }

    /** Weather line from the global summary and a local sample. */
    public static String formatWeather(Map<String, Object> w, double tempC, int humidity, double wind, Double soil, String precip, String fog) {
        String sky;
        switch (precip) {
            case "rain": sky = "Rain"; break;
            case "sleet": sky = "Sleet"; break;
            case "snow": sky = "Snow"; break;
            case "blizzard": sky = "Blizzard"; break;
            default: sky = null;
        }
        String condition = sky != null
            ? sky + " " + Math.round(((Number) w.get("intensity")).doubleValue() * 100) + "%"
            : String.valueOf(w.get("condition")).replace('_', ' ').toLowerCase(java.util.Locale.ROOT);
        String t = tempC <= 0 ? "§b" : tempC >= 30 ? "§c" : "§f";
        String soilText = soil == null ? "" : " | soil " + Math.round(soil * 100) + "%";
        return String.format(java.util.Locale.ROOT, "%s%.1f°C§r %s | hum %d%% | wind %s m/s %s%s%s",
            t, tempC, condition, humidity, wind, w.get("windFrom"), soilText, fog != null ? " | fog" : "");
    }

    @Override
    public int update(Scheduler.Context ctx) {
        int shown = 0;
        String perf = null;
        for (HudPlayer p : players.get()) {
            List<String> lines = new ArrayList<>();
            if (p.perfHud()) {
                if (perf == null) perf = perfLine.get();
                lines.add(perf);
            }
            if (p.weatherHud()) {
                String line = weatherLine.apply(p);
                if (line != null) lines.add(line);
            }
            if (lines.isEmpty()) continue;
            p.show(lines);
            shown++;
        }
        shownTo = shown;
        return shown;
    }

    @Override
    public Map<String, Object> describe() {
        Map<String, Object> m = new java.util.LinkedHashMap<>();
        m.put("viewers", shownTo);
        return m;
    }
}
