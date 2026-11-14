package com.realisticworld;

import com.realisticworld.core.Engine;
import com.realisticworld.core.Hud;
import com.realisticworld.core.RwLog;
import com.realisticworld.sim.Regions;
import com.realisticworld.water.Basin;
import com.realisticworld.water.TankScan;
import com.realisticworld.water.WaterPhysics;
import com.realisticworld.water.WaterSystem;
import com.realisticworld.weather.Climate;
import com.realisticworld.weather.ClimateSystem;
import com.realisticworld.weather.FogSystem;

import java.util.ArrayList;
import java.util.EnumSet;
import java.util.HashMap;
import java.util.List;
import java.util.Map;
import java.util.function.IntBinaryOperator;

/**
 * A small voxel world implementing Engine.Host for tests: grass at the surface,
 * dirt below, air above, plus edits. Records every block change and every fill
 * call so tests can check what the mod did to the world.
 */
public final class FakeWorld implements Engine.Host {
    public static final String AIR = "minecraft:air";
    public static final String WATER = "minecraft:water";
    public static final String FLOWING = "minecraft:flowing_water";
    public static final String STONE = "minecraft:stone";

    public final Map<Long, String> blocks = new HashMap<>();
    public IntBinaryOperator surface = (x, z) -> 70;
    public final List<Regions.PlayerPos> players = new ArrayList<>();
    public final List<String> vanillaWeather = new ArrayList<>();
    public final List<String[]> changes = new ArrayList<>(); // {x,y,z,from,to}
    public final List<Fill> fills = new ArrayList<>();
    public boolean recording = false;
    public long time = 6000;

    public static final class Fill {
        public final String to;
        public final EnumSet<WaterSystem.Block> from;
        public final List<int[]> changed;

        Fill(String to, EnumSet<WaterSystem.Block> from, List<int[]> changed) {
            this.to = to;
            this.from = from;
            this.changed = changed;
        }
    }

    public String typeAt(int x, int y, int z) {
        String t = blocks.get(WaterPhysics.pack(x, y, z));
        if (t != null) return t;
        int s = surface.applyAsInt(x, z);
        return y > s ? AIR : y == s ? "minecraft:grass" : "minecraft:dirt";
    }

    public void set(int x, int y, int z, String type) {
        String from = typeAt(x, y, z);
        blocks.put(WaterPhysics.pack(x, y, z), type);
        if (recording) changes.add(new String[] {String.valueOf(x), String.valueOf(y), String.valueOf(z), from, type});
    }

    public int count(int x0, int x1, int y0, int y1, int z0, int z1, String type) {
        int n = 0;
        for (int x = x0; x <= x1; x++) for (int y = y0; y <= y1; y++) for (int z = z0; z <= z1; z++) if (typeAt(x, y, z).equals(type)) n++;
        return n;
    }

    static WaterSystem.Block kind(String t) {
        switch (t) {
            case AIR: return WaterSystem.Block.AIR;
            case WATER: return WaterSystem.Block.WATER;
            case FLOWING: return WaterSystem.Block.FLOWING;
            case "minecraft:ice": return WaterSystem.Block.ICE;
            case "rw:puddle": return WaterSystem.Block.PUDDLE;
            case "minecraft:tallgrass": return WaterSystem.Block.PLANT;
            default: return null;
        }
    }

    static String typeOf(WaterSystem.Block b) {
        switch (b) {
            case AIR: return AIR;
            case WATER: return WATER;
            case FLOWING: return FLOWING;
            case ICE: return "minecraft:ice";
            case PUDDLE: return "rw:puddle";
            default: return "minecraft:tallgrass";
        }
    }

    private final WaterSystem.World water = new WaterSystem.World() {
        public TankScan.Cell cellAt(int dim, int x, int y, int z) {
            String t = typeAt(x, y, z);
            switch (t) {
                case WATER: return TankScan.Cell.SOURCE;
                case FLOWING: return TankScan.Cell.FLOW;
                case AIR: case "rw:puddle": case "minecraft:tallgrass": return TankScan.Cell.AIR;
                case "rw:valve_open": return TankScan.Cell.VALVE_OPEN;
                case "rw:valve_closed": return TankScan.Cell.VALVE_CLOSED;
                case "rw:pipe": return TankScan.Cell.PIPE;
                case "rw:drain": return TankScan.Cell.DRAIN;
                default: return TankScan.Cell.SOLID;
            }
        }

        public Basin.Column columnAt(int dim, int x, int z) {
            for (int y = 200; y >= 0; y--) {
                String t = typeAt(x, y, z);
                if (t.equals(AIR) || t.equals("rw:puddle") || t.equals("minecraft:tallgrass")) continue;
                return new Basin.Column(y + 1, t.equals(WATER) || t.equals(FLOWING), t);
            }
            return null;
        }

        public int fill(int dim, List<Long> cells, WaterSystem.Block to, EnumSet<WaterSystem.Block> from) {
            List<int[]> changed = new ArrayList<>();
            for (long k : cells) {
                int x = WaterPhysics.unpackX(k), y = WaterPhysics.unpackY(k), z = WaterPhysics.unpackZ(k);
                WaterSystem.Block cur = kind(typeAt(x, y, z));
                if (cur == null || !from.contains(cur)) continue;
                set(x, y, z, typeOf(to));
                changed.add(new int[] {x, y, z});
            }
            fills.add(new Fill(typeOf(to), from, changed));
            return changed.size();
        }

        public boolean isLoaded(int dim, int x, int z) {
            return true;
        }
    };

    private final ClimateSystem.Host climate = new ClimateSystem.Host() {
        public String biomeId(int dim, int x, int y, int z) { return "minecraft:plains"; }
        public Climate.BiomeInfo info(String id) { return new Climate.BiomeInfo(id, 0.8, 0.4, false); }
        public Integer surfaceY(int dim, int x, int z) { return surface.applyAsInt(x, z) + 1; }
        public long timeOfDay() { return time; }
    };

    @Override public long maxHeapBytes() { return 4L << 30; }
    @Override public String worldSeed() { return "123456789"; }
    @Override public List<Regions.PlayerPos> players() { return players; }
    @Override public boolean isLoaded(int dim, int x, int z) { return true; }
    @Override public void setVanillaWeather(String type, int duration) { vanillaWeather.add(type); }
    @Override public ClimateSystem.Host climate() { return climate; }
    @Override public List<FogSystem.FogPlayer> fogPlayers() { return new ArrayList<>(); }
    @Override public List<Hud.HudPlayer> hudPlayers() { return new ArrayList<>(); }
    @Override public WaterSystem.World water() { return water; }
    @Override public RwLog.Sink logSink() { return (l, s) -> {}; }
    @Override public long nanoTime() { return System.nanoTime(); }
}
