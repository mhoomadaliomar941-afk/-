package com.waterphysics.net;

import com.waterphysics.WaterPhysics;
import com.waterphysics.config.WPConfig;
import com.waterphysics.sim.Sec;
import com.waterphysics.sim.WaterSim;
import io.netty.buffer.ByteBuf;
import it.unimi.dsi.fastutil.ints.Int2ObjectOpenHashMap;
import it.unimi.dsi.fastutil.longs.LongIterator;
import it.unimi.dsi.fastutil.longs.LongOpenHashSet;
import net.minecraft.entity.player.EntityPlayer;
import net.minecraft.entity.player.EntityPlayerMP;
import net.minecraft.util.math.BlockPos;
import net.minecraftforge.fml.common.network.NetworkRegistry;
import net.minecraftforge.fml.common.network.simpleimpl.IMessage;
import net.minecraftforge.fml.common.network.simpleimpl.IMessageHandler;
import net.minecraftforge.fml.common.network.simpleimpl.MessageContext;
import net.minecraftforge.fml.common.network.simpleimpl.SimpleNetworkWrapper;
import net.minecraftforge.fml.relauncher.Side;

import java.util.ArrayList;
import java.util.List;

/**
 * Multiplayer synchronisation. Block states (visible water height) are synced by vanilla; this channel
 * adds the physical detail used for effects: exact volume, velocity and impact/stream/vortex flags of the
 * cells of sections that are currently moving, for players within the sync radius.
 */
public final class WaterNet {
    public static final SimpleNetworkWrapper CHANNEL = NetworkRegistry.INSTANCE.newSimpleChannel(WaterPhysics.MODID);
    private static final Int2ObjectOpenHashMap<LongOpenHashSet> DIRTY = new Int2ObjectOpenHashMap<>();
    public static long bytesSent, messagesSent;

    private WaterNet() {}

    public static void init() {
        CHANNEL.registerMessage(SyncHandler.class, MsgSectionSync.class, 0, Side.CLIENT);
        CHANNEL.registerMessage(EffectHandler.class, MsgEffect.class, 1, Side.CLIENT);
        CHANNEL.registerMessage(GaugeRequestHandler.class, MsgGaugeRequest.class, 2, Side.SERVER);
        CHANNEL.registerMessage(GaugeReplyHandler.class, MsgGaugeReply.class, 3, Side.CLIENT);
    }

    public static void afterSimTick(WaterSim sim) {
        int dim = sim.world.provider.getDimension();
        LongOpenHashSet dirty = DIRTY.get(dim);
        if (dirty == null) DIRTY.put(dim, dirty = new LongOpenHashSet());
        dirty.addAll(sim.changedThisTick);
        int interval = Math.max(1, WPConfig.simulation.syncInterval);
        if (sim.world.getTotalWorldTime() % interval != 0 || dirty.isEmpty()) return;
        int radius = WPConfig.simulation.syncRadius;
        for (EntityPlayer p : sim.world.playerEntities) {
            if (!(p instanceof EntityPlayerMP)) continue;
            int pcx = ((int) Math.floor(p.posX)) >> 4, pcy = ((int) Math.floor(p.posY)) >> 4, pcz = ((int) Math.floor(p.posZ)) >> 4;
            int r = (radius + 15) >> 4;
            List<MsgSectionSync.SecData> list = new ArrayList<>();
            int cells = 0;
            LongIterator it = dirty.iterator();
            while (it.hasNext()) {
                long k = it.nextLong();
                int sx = WaterSim.keyX(k), sy = WaterSim.keyY(k), sz = WaterSim.keyZ(k);
                if (Math.abs(sx - pcx) > r || Math.abs(sz - pcz) > r || Math.abs(sy - pcy) > r + 1) continue;
                Sec s = sim.secByKey(k);
                MsgSectionSync.SecData d = MsgSectionSync.SecData.of(sim, k, s);
                cells += d.count;
                list.add(d);
                if (cells > 6000) {
                    send(new MsgSectionSync(list), (EntityPlayerMP) p);
                    list = new ArrayList<>();
                    cells = 0;
                }
            }
            if (!list.isEmpty()) send(new MsgSectionSync(list), (EntityPlayerMP) p);
        }
        dirty.clear();
    }

    private static void send(IMessage m, EntityPlayerMP p) {
        CHANNEL.sendTo(m, p);
        messagesSent++;
    }

    public static void effect(net.minecraft.world.World w, int type, BlockPos pos, float strength) {
        CHANNEL.sendToAllAround(new MsgEffect(type, pos, strength),
                new NetworkRegistry.TargetPoint(w.provider.getDimension(), pos.getX() + 0.5, pos.getY() + 0.5, pos.getZ() + 0.5, 64));
    }

    // ---------------------------------------------------------------------------------------------
    public static final class SyncHandler implements IMessageHandler<MsgSectionSync, IMessage> {
        @Override
        public IMessage onMessage(MsgSectionSync m, MessageContext ctx) {
            WaterPhysics.proxy.handleSync(m);
            return null;
        }
    }

    public static final class EffectHandler implements IMessageHandler<MsgEffect, IMessage> {
        @Override
        public IMessage onMessage(MsgEffect m, MessageContext ctx) {
            WaterPhysics.proxy.handleEffect(m);
            return null;
        }
    }

    public static final class GaugeRequestHandler implements IMessageHandler<MsgGaugeRequest, IMessage> {
        @Override
        public IMessage onMessage(MsgGaugeRequest m, MessageContext ctx) {
            EntityPlayerMP p = ctx.getServerHandler().player;
            p.getServerWorld().addScheduledTask(() -> {
                if (p.getDistanceSq(m.pos) > 64 * 64) return;
                MsgGaugeReply r = GaugeReader.read(p.world, m.pos);
                if (r != null) CHANNEL.sendTo(r, p);
            });
            return null;
        }
    }

    public static final class GaugeReplyHandler implements IMessageHandler<MsgGaugeReply, IMessage> {
        @Override
        public IMessage onMessage(MsgGaugeReply m, MessageContext ctx) {
            WaterPhysics.proxy.handleGauge(m);
            return null;
        }
    }

    static void writePos(ByteBuf b, BlockPos p) {
        b.writeLong(p.toLong());
    }

    static BlockPos readPos(ByteBuf b) {
        return BlockPos.fromLong(b.readLong());
    }
}
