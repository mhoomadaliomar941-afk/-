package com.realisticworld.weather;

import com.realisticworld.core.RwLog;
import com.realisticworld.core.Scheduler;

import java.util.HashMap;
import java.util.HashSet;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;
import java.util.Objects;
import java.util.Set;
import java.util.function.Supplier;

/**
 * Decides the weather fog for each player who is outdoors in the overworld
 * (mist, dense, rain, blizzard, haze or none). Fog is drawn on the player's
 * client: the Forge layer sends the choice in a small packet when it changes,
 * and again every FOG_REFRESH_TICKS in case the client missed it.
 */
public final class FogSystem implements Scheduler.SimSystem {
    public static final int FOG_REFRESH_TICKS = 600;

    /** A player as the fog system sees it. */
    public interface FogPlayer {
        String id();

        int dim();

        double x();

        double y();

        double z();

        boolean outdoors();

        /** Send the fog kind (null = none) to this player's client. */
        void setFog(String kind);
    }

    private final Supplier<List<FogPlayer>> players;
    private final ClimateSystem climate;
    private final RwLog log;
    private final Map<String, String> applied = new HashMap<>();
    private final Map<String, Long> appliedTick = new HashMap<>();
    public long failures;

    public FogSystem(Supplier<List<FogPlayer>> players, ClimateSystem climate, RwLog log) {
        this.players = players;
        this.climate = climate;
        this.log = log;
    }

    @Override public String id() { return "fog"; }
    @Override public String name() { return "Weather fog"; }
    @Override public int intervalTicks() { return 40; }
    @Override public boolean scalable() { return true; }

    @Override
    public int update(Scheduler.Context ctx) {
        int ops = 0;
        Set<String> seen = new HashSet<>();
        for (FogPlayer p : players.get()) {
            seen.add(p.id());
            String want = null;
            if (p.dim() == ClimateSystem.OVERWORLD && p.outdoors()) {
                ClimateSystem.Sample s = climate.sample(p.dim(), p.x(), p.y(), p.z());
                want = s == null ? null : s.fog;
            }
            Long last = appliedTick.get(p.id());
            if (applied.containsKey(p.id()) && Objects.equals(applied.get(p.id()), want) && last != null && ctx.tick - last < FOG_REFRESH_TICKS) continue;
            try {
                p.setFog(want);
                applied.put(p.id(), want);
                appliedTick.put(p.id(), ctx.tick);
                ops++;
            } catch (RuntimeException e) {
                failures++;
                if (failures <= 3) log.error("fog", "could not send fog " + want, e);
            }
        }
        applied.keySet().retainAll(seen);
        appliedTick.keySet().retainAll(seen);
        return ops;
    }

    @Override
    public void stop() {
        for (FogPlayer p : players.get()) {
            try {
                p.setFog(null);
            } catch (RuntimeException ignored) {
                // player may be leaving
            }
        }
        applied.clear();
        appliedTick.clear();
    }

    @Override
    public Map<String, Object> describe() {
        Map<String, Object> m = new LinkedHashMap<>();
        m.put("players", applied.size());
        for (String f : applied.values()) m.merge(f == null ? "none" : f, 1, (a, b) -> (Integer) a + (Integer) b);
        return m;
    }
}
