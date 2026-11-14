package com.waterphysics.net;

import io.netty.buffer.ByteBuf;
import net.minecraft.util.math.BlockPos;
import net.minecraftforge.fml.common.network.simpleimpl.IMessage;

/** Client asks for hydrometer readings of a block (sent while holding the hydrometer). */
public final class MsgGaugeRequest implements IMessage {
    public BlockPos pos;

    public MsgGaugeRequest() {}

    public MsgGaugeRequest(BlockPos pos) {
        this.pos = pos;
    }

    @Override
    public void toBytes(ByteBuf b) {
        WaterNet.writePos(b, pos);
    }

    @Override
    public void fromBytes(ByteBuf b) {
        pos = WaterNet.readPos(b);
    }
}
