package com.waterphysics.client;

import com.waterphysics.net.MsgSectionSync;
import com.waterphysics.sim.WaterSim;
import it.unimi.dsi.fastutil.longs.Long2ObjectOpenHashMap;
import net.minecraft.client.Minecraft;

import java.util.Iterator;

/** Client mirror of the physical detail of moving water near the player (for effects). */
public final class ClientWaterData {
    public static final class CSec {
        public long key;
        public int cx, sy, cz;
        public boolean active;
        public long updated;
        public long prevUpdated;
        public final float[] liters = new float[4096];
        public final float[] prevLiters = new float[4096];
        public final byte[] vx = new byte[4096], vy = new byte[4096], vz = new byte[4096], fx = new byte[4096];
        public short[] cells = new short[0];
        public int count;
    }

    public static final Long2ObjectOpenHashMap<CSec> SECS = new Long2ObjectOpenHashMap<>();
    public static long packets, lastPacketTime;

    private ClientWaterData() {}

    public static void apply(MsgSectionSync m) {
        if (Minecraft.getMinecraft().world == null) return;
        long now = Minecraft.getMinecraft().world.getTotalWorldTime();
        packets++;
        lastPacketTime = now;
        for (MsgSectionSync.SecData d : m.secs) {
            CSec s = SECS.get(d.key);
            if (d.count == 0 && !d.active) {
                SECS.remove(d.key);
                continue;
            }
            if (s == null) {
                s = new CSec();
                s.key = d.key;
                s.cx = WaterSim.keyX(d.key);
                s.sy = WaterSim.keyY(d.key);
                s.cz = WaterSim.keyZ(d.key);
                SECS.put(d.key, s);
            }
            System.arraycopy(s.liters, 0, s.prevLiters, 0, 4096);
            java.util.Arrays.fill(s.liters, 0f);
            java.util.Arrays.fill(s.fx, (byte) 0);
            java.util.Arrays.fill(s.vx, (byte) 0);
            java.util.Arrays.fill(s.vy, (byte) 0);
            java.util.Arrays.fill(s.vz, (byte) 0);
            s.cells = new short[d.count];
            for (int k = 0; k < d.count; k++) {
                int i = d.idx[k] & 0xFFFF;
                s.cells[k] = (short) i;
                s.liters[i] = d.liters[k] & 0xFFFF;
                s.vx[i] = d.vx[k];
                s.vy[i] = d.vy[k];
                s.vz[i] = d.vz[k];
                s.fx[i] = d.fx[k];
            }
            s.count = d.count;
            s.active = d.active;
            s.prevUpdated = s.updated;
            s.updated = now;
        }
    }

    public static void cleanup() {
        if (Minecraft.getMinecraft().world == null) {
            SECS.clear();
            return;
        }
        long now = Minecraft.getMinecraft().world.getTotalWorldTime();
        Iterator<CSec> it = SECS.values().iterator();
        while (it.hasNext()) {
            CSec s = it.next();
            if (now - s.updated > (s.active ? 200 : 40)) it.remove();
        }
    }

    public static CSec at(int x, int y, int z) {
        if (y < 0 || y > 255) return null;
        return SECS.get(WaterSim.secKey(x >> 4, y >> 4, z >> 4));
    }

    /** interpolated liters at a cell or -1 when unknown */
    public static float liters(int x, int y, int z, float partial) {
        CSec s = at(x, y, z);
        if (s == null) return -1f;
        int i = ((y & 15) << 8) | ((z & 15) << 4) | (x & 15);
        float a = s.prevLiters[i], b = s.liters[i];
        long span = Math.max(1, s.updated - s.prevUpdated);
        long now = Minecraft.getMinecraft().world.getTotalWorldTime();
        float t = Math.min(1f, ((now - s.updated) + partial) / span);
        return a + (b - a) * t;
    }
}
