package com.waterphysics.net;

import com.waterphysics.sim.Sec;
import com.waterphysics.sim.WaterSim;
import io.netty.buffer.ByteBuf;
import net.minecraftforge.fml.common.network.simpleimpl.IMessage;

import java.util.ArrayList;
import java.util.List;

/** Physical detail of changed sections (server -> client). */
public final class MsgSectionSync implements IMessage {
    public static final class SecData {
        public long key;
        public boolean active;
        public int count;
        public short[] idx;
        public short[] liters;   // 0..65535 (unsigned)
        public byte[] vx, vy, vz; // m/s * 16
        public byte[] fx;

        static SecData of(WaterSim sim, long key, Sec s) {
            SecData d = new SecData();
            d.key = key;
            if (s == null) {
                d.count = 0;
                return d;
            }
            d.active = s.active && s.out != null;
            int n = 0;
            for (float v : s.vol) if (v > 0f) n++;
            d.count = n;
            d.idx = new short[n];
            d.liters = new short[n];
            d.vx = new byte[n];
            d.vy = new byte[n];
            d.vz = new byte[n];
            d.fx = new byte[n];
            float[] vel = new float[3];
            int k = 0;
            for (int i = 0; i < Sec.SIZE; i++) {
                float v = s.vol[i];
                if (v <= 0f) continue;
                d.idx[k] = (short) i;
                d.liters[k] = (short) Math.min(65535, Math.round(v * 1000f));
                if (d.active) {
                    sim.cellVelocity(s, i, vel);
                    d.vx[k] = q(vel[0]);
                    d.vy[k] = q(vel[1]);
                    d.vz[k] = q(vel[2]);
                    d.fx[k] = s.fx[i];
                }
                k++;
            }
            return d;
        }

        private static byte q(float v) {
            return (byte) Math.max(-127, Math.min(127, Math.round(v * 16f)));
        }
    }

    public List<SecData> secs = new ArrayList<>();

    public MsgSectionSync() {}

    public MsgSectionSync(List<SecData> secs) {
        this.secs = secs;
    }

    @Override
    public void toBytes(ByteBuf b) {
        b.writeInt(secs.size());
        for (SecData d : secs) {
            b.writeLong(d.key);
            b.writeBoolean(d.active);
            b.writeShort(d.count);
            for (int k = 0; k < d.count; k++) {
                b.writeShort(d.idx[k]);
                b.writeShort(d.liters[k]);
                if (d.active) {
                    b.writeByte(d.vx[k]);
                    b.writeByte(d.vy[k]);
                    b.writeByte(d.vz[k]);
                    b.writeByte(d.fx[k]);
                }
            }
        }
    }

    @Override
    public void fromBytes(ByteBuf b) {
        int n = b.readInt();
        secs = new ArrayList<>(n);
        for (int s = 0; s < n; s++) {
            SecData d = new SecData();
            d.key = b.readLong();
            d.active = b.readBoolean();
            d.count = b.readUnsignedShort();
            d.idx = new short[d.count];
            d.liters = new short[d.count];
            d.vx = new byte[d.count];
            d.vy = new byte[d.count];
            d.vz = new byte[d.count];
            d.fx = new byte[d.count];
            for (int k = 0; k < d.count; k++) {
                d.idx[k] = b.readShort();
                d.liters[k] = b.readShort();
                if (d.active) {
                    d.vx[k] = b.readByte();
                    d.vy[k] = b.readByte();
                    d.vz[k] = b.readByte();
                    d.fx[k] = b.readByte();
                }
            }
            secs.add(d);
        }
    }
}
