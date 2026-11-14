package com.waterphysics.sim;

import com.waterphysics.WaterPhysics;
import com.waterphysics.config.WPConfig;
import com.waterphysics.net.WaterNet;
import it.unimi.dsi.fastutil.ints.Int2ObjectOpenHashMap;
import net.minecraft.block.Block;
import net.minecraft.block.state.IBlockState;
import net.minecraft.entity.player.EntityPlayer;
import net.minecraft.init.Blocks;
import net.minecraft.init.Items;
import net.minecraft.init.SoundEvents;
import net.minecraft.item.ItemStack;
import net.minecraft.nbt.NBTTagCompound;
import net.minecraft.util.EnumFacing;
import net.minecraft.util.SoundCategory;
import net.minecraft.util.math.BlockPos;
import net.minecraft.util.math.RayTraceResult;
import net.minecraft.util.text.TextComponentTranslation;
import net.minecraft.world.World;
import net.minecraft.world.WorldServer;
import net.minecraftforge.event.entity.player.FillBucketEvent;
import net.minecraftforge.event.world.BlockEvent;
import net.minecraftforge.event.world.ChunkDataEvent;
import net.minecraftforge.event.world.ChunkEvent;
import net.minecraftforge.event.world.WorldEvent;
import net.minecraftforge.fml.common.Mod;
import net.minecraftforge.fml.common.eventhandler.Event;
import net.minecraftforge.fml.common.eventhandler.EventPriority;
import net.minecraftforge.fml.common.eventhandler.SubscribeEvent;
import net.minecraftforge.fml.common.gameevent.TickEvent;

/** Owns one {@link WaterSim} per loaded server dimension and connects it to Forge events. */
@Mod.EventBusSubscriber(modid = WaterPhysics.MODID)
public final class WaterManager {
    private static final Int2ObjectOpenHashMap<WaterSim> SIMS = new Int2ObjectOpenHashMap<>();

    private WaterManager() {}

    /** @return the simulation of a server world, null on the client */
    public static WaterSim get(World w) {
        if (w == null || w.isRemote || !(w instanceof WorldServer)) return null;
        int dim = w.provider.getDimension();
        WaterSim s = SIMS.get(dim);
        if (s == null || s.world != w) {
            s = new WaterSim((WorldServer) w);
            SIMS.put(dim, s);
        }
        return s;
    }

    public static Iterable<WaterSim> all() {
        return SIMS.values();
    }

    @SubscribeEvent
    public static void onWorldUnload(WorldEvent.Unload e) {
        if (!e.getWorld().isRemote) {
            WaterSim s = SIMS.get(e.getWorld().provider.getDimension());
            if (s != null && s.world == e.getWorld()) SIMS.remove(e.getWorld().provider.getDimension());
        }
    }

    @SubscribeEvent
    public static void onWorldTick(TickEvent.WorldTickEvent e) {
        if (e.phase != TickEvent.Phase.END || e.world.isRemote) return;
        WaterSim s = get(e.world);
        if (s == null) return;
        s.tick();
        WaterNet.afterSimTick(s);
    }

    // ---------------------------------------------------------------------------------------------
    // persistence
    // ---------------------------------------------------------------------------------------------
    @SubscribeEvent
    public static void onChunkSave(ChunkDataEvent.Save e) {
        World w = e.getWorld();
        if (w == null || w.isRemote) return;
        WaterSim s = get(w);
        if (s == null) return;
        NBTTagCompound t = s.writeChunk(e.getChunk().x, e.getChunk().z);
        if (t != null) e.getData().setTag(WaterSim.NBT_KEY, t);
        s.recordSavedTotal(e.getChunk().x, e.getChunk().z);
    }

    @SubscribeEvent
    public static void onChunkDataLoad(ChunkDataEvent.Load e) {
        World w = e.getWorld();
        if (w == null || w.isRemote) return;
        if (!e.getData().hasKey(WaterSim.NBT_KEY)) return;
        WaterSim s = get(w);
        if (s != null) s.queueChunkLoad(e.getChunk(), e.getData().getCompoundTag(WaterSim.NBT_KEY));
    }

    @SubscribeEvent
    public static void onChunkLoad(ChunkEvent.Load e) {
        if (e.getWorld().isRemote) return;
        WaterSim s = get(e.getWorld());
        if (s != null) s.onChunkLoaded(e.getChunk());
    }

    @SubscribeEvent
    public static void onChunkUnload(ChunkEvent.Unload e) {
        if (e.getWorld().isRemote) return;
        WaterSim s = get(e.getWorld());
        if (s != null) s.onChunkUnloaded(e.getChunk());
    }

    // ---------------------------------------------------------------------------------------------
    // block changes: holes, closed holes, disturbed vanilla water
    // ---------------------------------------------------------------------------------------------
    @SubscribeEvent
    public static void onNeighborNotify(BlockEvent.NeighborNotifyEvent e) {
        World w = e.getWorld();
        if (w.isRemote) return;
        WaterSim s = get(w);
        if (s == null) return;
        BlockPos p = e.getPos();
        if (WPConfig.gameplay.convertVanillaWater) {
            for (EnumFacing f : e.getNotifiedSides()) {
                BlockPos n = p.offset(f);
                IBlockState ns = w.getBlockState(n);
                Block b = ns.getBlock();
                if ((b == Blocks.WATER || b == Blocks.FLOWING_WATER) && shouldConvert(w, n, ns, s)) {
                    s.convertVanilla(n, ns);
                }
            }
        }
        s.blockChanged(p);
    }

    /**
     * A disturbed vanilla water block is converted when it could start to flow, i.e. it touches an open
     * cell (air / washable block / physical water that is not full).
     */
    private static boolean shouldConvert(World w, BlockPos n, IBlockState ns, WaterSim s) {
        for (EnumFacing f : EnumFacing.VALUES) {
            if (f == EnumFacing.UP) continue;
            BlockPos q = n.offset(f);
            IBlockState qs = w.getBlockState(q);
            byte t = CellType.of(qs);
            if (t == CellType.OPEN) {
                if (qs.getBlock() == WaterPhysics.WATER_BLOCK && s.getVolume(q) >= 0.98f) continue;
                return true;
            }
        }
        return false;
    }

    @SubscribeEvent
    public static void onCreateSource(BlockEvent.CreateFluidSourceEvent e) {
        if (WPConfig.gameplay.disableInfiniteSources && e.getState().getMaterial() == net.minecraft.block.material.Material.WATER) {
            e.setResult(Event.Result.DENY);
        }
    }

    // ---------------------------------------------------------------------------------------------
    // buckets: exact 1000 L transfers
    // ---------------------------------------------------------------------------------------------
    @SubscribeEvent(priority = EventPriority.HIGH)
    public static void onBucket(FillBucketEvent e) {
        if (!WPConfig.gameplay.physicalBuckets) return;
        RayTraceResult t = e.getTarget();
        if (t == null || t.typeOfHit != RayTraceResult.Type.BLOCK) return;
        ItemStack stack = e.getEmptyBucket();
        World w = e.getWorld();
        EntityPlayer player = e.getEntityPlayer();
        if (stack.getItem() == Items.BUCKET) {
            BlockPos p = t.getBlockPos();
            if (w.getBlockState(p).getBlock() != WaterPhysics.WATER_BLOCK) return; // vanilla handles other fluids
            if (!w.isBlockModifiable(player, p)) return;
            if (!w.isRemote) {
                WaterSim s = get(w);
                if (s == null || !s.drainExact(p, 1.0f, 512)) {
                    float have = s == null ? 0f : (float) s.scanBody(p, 512).volume;
                    player.sendStatusMessage(new TextComponentTranslation("waterphysics.msg.not_enough", Math.round(have * 1000)), true);
                    e.setCanceled(true);
                    return;
                }
                s.stats.bucketsOut += 1.0;
                s.stats.markDirty();
            }
            player.playSound(SoundEvents.ITEM_BUCKET_FILL, 1.0F, 1.0F);
            e.setFilledBucket(new ItemStack(Items.WATER_BUCKET));
            e.setResult(Event.Result.ALLOW);
        } else if (stack.getItem() == Items.WATER_BUCKET) {
            if (w.provider.doesWaterVaporize()) return;
            BlockPos hit = t.getBlockPos();
            IBlockState hs = w.getBlockState(hit);
            boolean replaceable = hs.getBlock().isReplaceable(w, hit);
            BlockPos target = (replaceable && t.sideHit == EnumFacing.UP) || hs.getBlock() == WaterPhysics.WATER_BLOCK ? hit : hit.offset(t.sideHit);
            if (!w.isBlockModifiable(player, target) || !player.canPlayerEdit(target, t.sideHit, stack)) return;
            if (!w.isRemote) {
                if (!pour(w, target, 1.0f)) {
                    e.setCanceled(true);
                    return;
                }
            }
            w.playSound(player, target, SoundEvents.ITEM_BUCKET_EMPTY, SoundCategory.BLOCKS, 1.0F, 1.0F);
            e.setFilledBucket(new ItemStack(Items.BUCKET));
            e.setResult(Event.Result.ALLOW);
        }
    }

    /** Pours an amount of water at a position (server). */
    public static boolean pour(World w, BlockPos target, float amount) {
        WaterSim s = get(w);
        if (s == null) return false;
        IBlockState st = w.getBlockState(target);
        byte type = CellType.of(st);
        if (type != CellType.OPEN && type != CellType.RESERVOIR) return false;
        float added = s.pour(target, amount);
        if (added <= 0f) return false;
        s.stats.bucketsIn += added;
        s.stats.markDirty();
        return true;
    }
}
