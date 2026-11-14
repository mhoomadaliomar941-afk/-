package com.realisticworld.forge;

import com.realisticworld.core.Engine;
import com.realisticworld.core.Hud;
import com.realisticworld.core.RwLog;
import com.realisticworld.forge.net.RwNetwork;
import com.realisticworld.sim.Regions;
import com.realisticworld.water.Basin;
import com.realisticworld.water.TankScan;
import com.realisticworld.water.WaterPhysics;
import com.realisticworld.water.WaterSystem;
import com.realisticworld.weather.Climate;
import com.realisticworld.weather.ClimateSystem;
import com.realisticworld.weather.FogSystem;
import net.minecraft.block.Block;
import net.minecraft.block.BlockLiquid;
import net.minecraft.block.state.IBlockState;
import net.minecraft.entity.player.EntityPlayerMP;
import net.minecraft.init.Blocks;
import net.minecraft.server.MinecraftServer;
import net.minecraft.util.ResourceLocation;
import net.minecraft.util.math.BlockPos;
import net.minecraft.world.World;
import net.minecraft.world.WorldServer;
import net.minecraft.world.biome.Biome;
import net.minecraft.world.storage.WorldInfo;
import net.minecraftforge.common.BiomeDictionary;
import net.minecraftforge.common.DimensionManager;
import org.apache.logging.log4j.Logger;

import java.util.ArrayList;
import java.util.EnumSet;
import java.util.List;

/**
 * The game side of the engine: players, chunks, vanilla weather, biomes and
 * blocks of a running Minecraft 1.12.2 server.
 */
public final class ForgeHost implements Engine.Host {
    private final MinecraftServer server;
    private final Logger logger;

    public ForgeHost(MinecraftServer server, Logger logger) {
        this.server = server;
        this.logger = logger;
    }

    static WorldServer world(int dim) {
        return DimensionManager.getWorld(dim);
    }

    static WorldServer overworld() {
        return DimensionManager.getWorld(0);
    }

    List<EntityPlayerMP> onlinePlayers() {
        return server.getPlayerList().getPlayers();
    }

    @Override
    public long maxHeapBytes() {
        return Runtime.getRuntime().maxMemory();
    }

    @Override
    public String worldSeed() {
        return String.valueOf(overworld().getSeed());
    }

    /** Areas kept simulated without a player (/rw anchor), as [dim, x, z]. */
    static final List<int[]> ANCHORS = new ArrayList<>();

    @Override
    public List<Regions.PlayerPos> players() {
        List<Regions.PlayerPos> out = new ArrayList<>();
        for (EntityPlayerMP p : onlinePlayers()) out.add(new Regions.PlayerPos(p.dimension, p.posX, p.posZ));
        for (int[] a : ANCHORS) out.add(new Regions.PlayerPos(a[0], a[1] + 0.5, a[2] + 0.5));
        return out;
    }

    @Override
    public boolean isLoaded(int dim, int x, int z) {
        World w = world(dim);
        return w != null && w.isBlockLoaded(new BlockPos(x, 64, z));
    }

    /** Same effect as the /weather command, for a number of ticks. */
    @Override
    public void setVanillaWeather(String type, int duration) {
        WorldInfo info = overworld().getWorldInfo();
        switch (type) {
            case "THUNDER":
                info.setCleanWeatherTime(0);
                info.setRainTime(duration);
                info.setThunderTime(duration);
                info.setRaining(true);
                info.setThundering(true);
                break;
            case "RAIN":
                info.setCleanWeatherTime(0);
                info.setRainTime(duration);
                info.setThunderTime(duration);
                info.setRaining(true);
                info.setThundering(false);
                break;
            default:
                info.setCleanWeatherTime(duration);
                info.setRainTime(0);
                info.setThunderTime(0);
                info.setRaining(false);
                info.setThundering(false);
        }
    }

    /** "CLEAR", "RAIN" or "THUNDER" as the world shows it now. */
    static String vanillaWeatherNow() {
        WorldInfo info = overworld().getWorldInfo();
        return info.isThundering() && info.isRaining() ? "THUNDER" : info.isRaining() ? "RAIN" : "CLEAR";
    }

    // ---------------------------------------------------------------- climate

    private final ClimateSystem.Host climate = new ClimateSystem.Host() {
        @Override
        public String biomeId(int dim, int x, int y, int z) {
            World w = world(dim);
            BlockPos pos = new BlockPos(x, y, z);
            if (w == null || !w.isBlockLoaded(pos)) throw new IllegalStateException("not loaded");
            ResourceLocation id = w.getBiome(pos).getRegistryName();
            return id == null ? "minecraft:plains" : id.toString();
        }

        @Override
        public Climate.BiomeInfo info(String biomeId) {
            Biome b = Biome.REGISTRY.getObject(new ResourceLocation(biomeId));
            if (b == null) return null;
            boolean water = BiomeDictionary.hasType(b, BiomeDictionary.Type.OCEAN) || BiomeDictionary.hasType(b, BiomeDictionary.Type.RIVER)
                || BiomeDictionary.hasType(b, BiomeDictionary.Type.SWAMP) || BiomeDictionary.hasType(b, BiomeDictionary.Type.BEACH);
            return new Climate.BiomeInfo(biomeId, b.getDefaultTemperature(), b.getRainfall(), water);
        }

        @Override
        public Integer surfaceY(int dim, int x, int z) {
            World w = world(dim);
            BlockPos probe = new BlockPos(x, 64, z);
            if (w == null || !w.isBlockLoaded(probe)) return null;
            return w.getTopSolidOrLiquidBlock(probe).getY();
        }

        @Override
        public long timeOfDay() {
            return overworld().getWorldTime() % 24000L;
        }
    };

    @Override
    public ClimateSystem.Host climate() {
        return climate;
    }

    @Override
    public List<FogSystem.FogPlayer> fogPlayers() {
        List<FogSystem.FogPlayer> out = new ArrayList<>();
        for (EntityPlayerMP p : onlinePlayers()) {
            out.add(new FogSystem.FogPlayer() {
                public String id() { return p.getUniqueID().toString(); }
                public int dim() { return p.dimension; }
                public double x() { return p.posX; }
                public double y() { return p.posY; }
                public double z() { return p.posZ; }
                public boolean outdoors() { return p.world.canSeeSky(new BlockPos(p.posX, p.posY + p.getEyeHeight(), p.posZ)); }
                public void setFog(String kind) { RwNetwork.sendFog(p, kind); }
            });
        }
        return out;
    }

    @Override
    public List<Hud.HudPlayer> hudPlayers() {
        List<Hud.HudPlayer> out = new ArrayList<>();
        for (EntityPlayerMP p : onlinePlayers()) {
            out.add(new Hud.HudPlayer() {
                public boolean perfHud() { return PlayerPrefs.perfHud(p); }
                public boolean weatherHud() { return PlayerPrefs.weatherHud(p); }
                public int dim() { return p.dimension; }
                public double x() { return p.posX; }
                public double y() { return p.posY; }
                public double z() { return p.posZ; }
                public void show(List<String> lines) { RwNetwork.sendHud(p, lines); }
            });
        }
        return out;
    }

    // ---------------------------------------------------------------- water

    static WaterSystem.Block kind(IBlockState s) {
        Block b = s.getBlock();
        if (b == Blocks.AIR) return WaterSystem.Block.AIR;
        if (b == Blocks.WATER || b == Blocks.FLOWING_WATER) return s.getValue(BlockLiquid.LEVEL) == 0 ? WaterSystem.Block.WATER : WaterSystem.Block.FLOWING;
        if (b == Blocks.ICE) return WaterSystem.Block.ICE;
        if (b == RwBlocks.PUDDLE) return WaterSystem.Block.PUDDLE;
        if (b == Blocks.TALLGRASS || b == Blocks.DEADBUSH) return WaterSystem.Block.PLANT;
        return null;
    }

    static IBlockState stateOf(WaterSystem.Block k) {
        switch (k) {
            case WATER: return Blocks.WATER.getDefaultState();
            case ICE: return Blocks.ICE.getDefaultState();
            case PUDDLE: return RwBlocks.PUDDLE.getDefaultState();
            default: return Blocks.AIR.getDefaultState();
        }
    }

    private final WaterSystem.World water = new WaterSystem.World() {
        @Override
        public TankScan.Cell cellAt(int dim, int x, int y, int z) {
            if (y < 0 || y > 255) return TankScan.Cell.SOLID;
            World w = world(dim);
            BlockPos pos = new BlockPos(x, y, z);
            if (w == null || !w.isBlockLoaded(pos)) return TankScan.Cell.UNLOADED;
            IBlockState s = w.getBlockState(pos);
            Block b = s.getBlock();
            if (b == Blocks.WATER || b == Blocks.FLOWING_WATER) return s.getValue(BlockLiquid.LEVEL) == 0 ? TankScan.Cell.SOURCE : TankScan.Cell.FLOW;
            if (b == Blocks.AIR || b == Blocks.TALLGRASS || b == Blocks.DEADBUSH || b == Blocks.SNOW_LAYER || b == RwBlocks.PUDDLE) return TankScan.Cell.AIR;
            if (b == RwBlocks.VALVE) return s.getValue(BlockValve.OPEN) ? TankScan.Cell.VALVE_OPEN : TankScan.Cell.VALVE_CLOSED;
            if (b == RwBlocks.PIPE) return TankScan.Cell.PIPE;
            if (b == RwBlocks.DRAIN) return TankScan.Cell.DRAIN;
            return TankScan.Cell.SOLID;
        }

        @Override
        public Basin.Column columnAt(int dim, int x, int z) {
            World w = world(dim);
            BlockPos probe = new BlockPos(x, 64, z);
            if (w == null || !w.isBlockLoaded(probe)) return null;
            BlockPos top = w.getTopSolidOrLiquidBlock(probe); // first open block above the ground or liquid
            IBlockState below = w.getBlockState(top.down());
            Block b = below.getBlock();
            boolean liquid = b == Blocks.WATER || b == Blocks.FLOWING_WATER;
            ResourceLocation id = b.getRegistryName();
            return new Basin.Column(top.getY(), liquid, id == null ? "minecraft:stone" : id.toString());
        }

        @Override
        public int fill(int dim, List<Long> cells, WaterSystem.Block to, EnumSet<WaterSystem.Block> from) {
            World w = world(dim);
            if (w == null) return 0;
            IBlockState target = stateOf(to);
            int n = 0;
            for (long k : cells) {
                BlockPos pos = new BlockPos(WaterPhysics.unpackX(k), WaterPhysics.unpackY(k), WaterPhysics.unpackZ(k));
                if (!w.isBlockLoaded(pos)) continue;
                WaterSystem.Block cur = kind(w.getBlockState(pos));
                if (cur == null || !from.contains(cur)) continue; // never replace anything else
                if (w.setBlockState(pos, target, 3)) n++;
            }
            return n;
        }

        @Override
        public boolean isLoaded(int dim, int x, int z) {
            return ForgeHost.this.isLoaded(dim, x, z);
        }
    };

    @Override
    public WaterSystem.World water() {
        return water;
    }

    @Override
    public RwLog.Sink logSink() {
        return (level, line) -> {
            switch (level) {
                case ERROR: logger.error(line); break;
                case WARN: logger.warn(line); break;
                case DEBUG: logger.debug(line); break;
                default: logger.info(line);
            }
        };
    }

    @Override
    public long nanoTime() {
        return System.nanoTime();
    }
}
