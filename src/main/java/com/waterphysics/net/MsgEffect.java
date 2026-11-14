package com.waterphysics.net;

import io.netty.buffer.ByteBuf;
import net.minecraft.util.math.BlockPos;
import net.minecraftforge.fml.common.network.simpleimpl.IMessage;

/** One-shot visual/sound effect (pipe burst, big splash, leak start). */
public final class MsgEffect implements IMessage {
    public static final int BURST = 0, CRACK = 1, SPLASH = 2, LEAK = 3;

    public int type;
    public BlockPos pos;
    public float strength;

    public MsgEffect() {}

    public MsgEffect(int type, BlockPos pos, float strength) {
        this.type = type;
        this.pos = pos;
        this.strength = strength;
    }

    @Override
    public void toBytes(ByteBuf b) {
        b.writeByte(type);
        WaterNet.writePos(b, pos);
        b.writeFloat(strength);
    }

    @Override
    public void fromBytes(ByteBuf b) {
        type = b.readByte();
        pos = WaterNet.readPos(b);
        strength = b.readFloat();
    }
}
