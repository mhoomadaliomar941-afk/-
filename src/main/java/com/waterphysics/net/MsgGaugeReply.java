package com.waterphysics.net;

import io.netty.buffer.ByteBuf;
import net.minecraft.util.math.BlockPos;
import net.minecraftforge.fml.common.network.ByteBufUtils;
import net.minecraftforge.fml.common.network.simpleimpl.IMessage;

import java.util.ArrayList;
import java.util.List;

/** Hydrometer readings (server -> client): ready-to-display lines (translation key + args). */
public final class MsgGaugeReply implements IMessage {
    public BlockPos pos;
    public List<String> lines = new ArrayList<>();

    public MsgGaugeReply() {}

    public MsgGaugeReply(BlockPos pos) {
        this.pos = pos;
    }

    @Override
    public void toBytes(ByteBuf b) {
        WaterNet.writePos(b, pos);
        b.writeByte(lines.size());
        for (String l : lines) ByteBufUtils.writeUTF8String(b, l);
    }

    @Override
    public void fromBytes(ByteBuf b) {
        pos = WaterNet.readPos(b);
        int n = b.readByte();
        for (int i = 0; i < n; i++) lines.add(ByteBufUtils.readUTF8String(b));
    }
}
