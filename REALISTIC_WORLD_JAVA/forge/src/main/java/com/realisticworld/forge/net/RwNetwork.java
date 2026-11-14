package com.realisticworld.forge.net;

import com.realisticworld.forge.Nbt;
import com.realisticworld.forge.RealisticWorldMod;
import com.realisticworld.ui.Form;
import io.netty.buffer.ByteBuf;
import net.minecraft.entity.player.EntityPlayerMP;
import net.minecraft.nbt.NBTTagCompound;
import net.minecraftforge.fml.common.network.ByteBufUtils;
import net.minecraftforge.fml.common.network.NetworkRegistry;
import net.minecraftforge.fml.common.network.simpleimpl.IMessage;
import net.minecraftforge.fml.common.network.simpleimpl.IMessageHandler;
import net.minecraftforge.fml.common.network.simpleimpl.MessageContext;
import net.minecraftforge.fml.common.network.simpleimpl.SimpleNetworkWrapper;
import net.minecraftforge.fml.relauncher.Side;

import java.util.ArrayList;
import java.util.List;

/**
 * Server and client talk through four small messages: a control-center form,
 * the player's reply, HUD lines, and the weather fog to draw.
 */
public final class RwNetwork {
    public static final SimpleNetworkWrapper CHANNEL = NetworkRegistry.INSTANCE.newSimpleChannel(RealisticWorldMod.MODID);

    private RwNetwork() {}

    public static void register() {
        CHANNEL.registerMessage(FormMsg.Handler.class, FormMsg.class, 0, Side.CLIENT);
        CHANNEL.registerMessage(ReplyMsg.Handler.class, ReplyMsg.class, 1, Side.SERVER);
        CHANNEL.registerMessage(HudMsg.Handler.class, HudMsg.class, 2, Side.CLIENT);
        CHANNEL.registerMessage(FogMsg.Handler.class, FogMsg.class, 3, Side.CLIENT);
    }

    public static void sendForm(EntityPlayerMP p, Form form) {
        CHANNEL.sendTo(new FormMsg(Nbt.fromMap(form.toMap())), p);
    }

    public static void sendHud(EntityPlayerMP p, List<String> lines) {
        CHANNEL.sendTo(new HudMsg(lines), p);
    }

    public static void sendFog(EntityPlayerMP p, String kind) {
        CHANNEL.sendTo(new FogMsg(kind == null ? "" : kind), p);
    }

    public static void sendReply(Form.Reply reply) {
        CHANNEL.sendToServer(new ReplyMsg(Nbt.fromMap(reply.toMap())));
    }

    /** Server -> client: a form to show. */
    public static class FormMsg implements IMessage {
        NBTTagCompound tag;

        public FormMsg() {}

        FormMsg(NBTTagCompound tag) {
            this.tag = tag;
        }

        @Override public void fromBytes(ByteBuf buf) { tag = ByteBufUtils.readTag(buf); }
        @Override public void toBytes(ByteBuf buf) { ByteBufUtils.writeTag(buf, tag); }

        public static class Handler implements IMessageHandler<FormMsg, IMessage> {
            @Override
            public IMessage onMessage(FormMsg msg, MessageContext ctx) {
                Form form = Form.fromMap(Nbt.toMap(msg.tag));
                RealisticWorldMod.proxy.runOnClient(() -> RealisticWorldMod.proxy.showForm(form));
                return null;
            }
        }
    }

    /** Client -> server: the player's answer to a form. */
    public static class ReplyMsg implements IMessage {
        NBTTagCompound tag;

        public ReplyMsg() {}

        ReplyMsg(NBTTagCompound tag) {
            this.tag = tag;
        }

        @Override public void fromBytes(ByteBuf buf) { tag = ByteBufUtils.readTag(buf); }
        @Override public void toBytes(ByteBuf buf) { ByteBufUtils.writeTag(buf, tag); }

        public static class Handler implements IMessageHandler<ReplyMsg, IMessage> {
            @Override
            public IMessage onMessage(ReplyMsg msg, MessageContext ctx) {
                EntityPlayerMP player = ctx.getServerHandler().player;
                Form.Reply reply = Form.Reply.fromMap(Nbt.toMap(msg.tag));
                player.getServerWorld().addScheduledTask(() -> RealisticWorldMod.onReply(player, reply));
                return null;
            }
        }
    }

    /** Server -> client: HUD lines (performance and/or weather). */
    public static class HudMsg implements IMessage {
        List<String> lines = new ArrayList<>();

        public HudMsg() {}

        HudMsg(List<String> lines) {
            this.lines = lines;
        }

        @Override
        public void fromBytes(ByteBuf buf) {
            int n = Math.min(4, buf.readByte());
            lines = new ArrayList<>();
            for (int i = 0; i < n; i++) lines.add(ByteBufUtils.readUTF8String(buf));
        }

        @Override
        public void toBytes(ByteBuf buf) {
            buf.writeByte(Math.min(4, lines.size()));
            for (int i = 0; i < Math.min(4, lines.size()); i++) ByteBufUtils.writeUTF8String(buf, lines.get(i));
        }

        public static class Handler implements IMessageHandler<HudMsg, IMessage> {
            @Override
            public IMessage onMessage(HudMsg msg, MessageContext ctx) {
                RealisticWorldMod.proxy.runOnClient(() -> RealisticWorldMod.proxy.setHud(msg.lines));
                return null;
            }
        }
    }

    /** Server -> client: which weather fog to draw ("" = none). */
    public static class FogMsg implements IMessage {
        String kind = "";

        public FogMsg() {}

        FogMsg(String kind) {
            this.kind = kind;
        }

        @Override public void fromBytes(ByteBuf buf) { kind = ByteBufUtils.readUTF8String(buf); }
        @Override public void toBytes(ByteBuf buf) { ByteBufUtils.writeUTF8String(buf, kind); }

        public static class Handler implements IMessageHandler<FogMsg, IMessage> {
            @Override
            public IMessage onMessage(FogMsg msg, MessageContext ctx) {
                String k = msg.kind.isEmpty() ? null : msg.kind;
                RealisticWorldMod.proxy.runOnClient(() -> RealisticWorldMod.proxy.setFog(k));
                return null;
            }
        }
    }
}
