package com.waterphysics.client;

import com.waterphysics.ModRegistry;
import com.waterphysics.config.WPConfig;
import com.waterphysics.net.GaugeReader;
import com.waterphysics.net.MsgGaugeReply;
import com.waterphysics.net.MsgGaugeRequest;
import com.waterphysics.net.WaterNet;
import net.minecraft.client.Minecraft;
import net.minecraft.client.gui.FontRenderer;
import net.minecraft.client.gui.Gui;
import net.minecraft.client.resources.I18n;
import net.minecraft.entity.player.EntityPlayer;
import net.minecraft.util.math.BlockPos;
import net.minecraft.util.math.RayTraceResult;
import net.minecraft.util.math.Vec3d;
import net.minecraftforge.client.event.RenderGameOverlayEvent;
import net.minecraftforge.fml.common.eventhandler.SubscribeEvent;
import net.minecraftforge.fml.common.gameevent.TickEvent;

import java.util.ArrayList;
import java.util.List;

/** HUD readings of the targeted water / tank / pipe while the hydrometer is held. */
public final class HydrometerHud {
    private static MsgGaugeReply last;
    private static long lastTime;
    private static int timer;

    private HydrometerHud() {}

    public static void update(MsgGaugeReply m) {
        last = m;
        lastTime = Minecraft.getMinecraft().world != null ? Minecraft.getMinecraft().world.getTotalWorldTime() : 0;
    }

    private static boolean holding(EntityPlayer p) {
        return p.getHeldItemMainhand().getItem() == ModRegistry.HYDROMETER || p.getHeldItemOffhand().getItem() == ModRegistry.HYDROMETER;
    }

    @SubscribeEvent
    public static void onTick(TickEvent.ClientTickEvent e) {
        if (e.phase != TickEvent.Phase.END) return;
        Minecraft mc = Minecraft.getMinecraft();
        if (mc.player == null || mc.world == null || !WPConfig.visuals.hydrometerHud || !holding(mc.player)) return;
        if (++timer % 8 != 0) return;
        Vec3d eye = mc.player.getPositionEyes(1f);
        Vec3d end = eye.add(mc.player.getLook(1f).scale(8));
        RayTraceResult r = mc.world.rayTraceBlocks(eye, end, true, false, false);
        if (r != null && r.typeOfHit == RayTraceResult.Type.BLOCK) {
            WaterNet.CHANNEL.sendToServer(new MsgGaugeRequest(r.getBlockPos()));
        }
    }

    @SubscribeEvent
    public static void onOverlay(RenderGameOverlayEvent.Post e) {
        if (e.getType() != RenderGameOverlayEvent.ElementType.ALL) return;
        Minecraft mc = Minecraft.getMinecraft();
        if (mc.player == null || last == null || !WPConfig.visuals.hydrometerHud || !holding(mc.player)) return;
        if (mc.world.getTotalWorldTime() - lastTime > 40) return;
        FontRenderer fr = mc.fontRenderer;
        List<String> lines = new ArrayList<>();
        lines.add("§b" + I18n.format("waterphysics.hud.title") + " §7(" + last.pos.getX() + ", " + last.pos.getY() + ", " + last.pos.getZ() + ")");
        for (String l : last.lines) lines.add(format(l));
        int w = 0;
        for (String s : lines) w = Math.max(w, fr.getStringWidth(s));
        int x = 6, y = 6;
        Gui.drawRect(x - 3, y - 3, x + w + 3, y + lines.size() * 10 + 1, 0x90000000);
        for (String s : lines) {
            fr.drawStringWithShadow(s, x, y, 0xFFFFFF);
            y += 10;
        }
    }

    static String format(String l) {
        String[] p = l.split(String.valueOf(GaugeReader.SEP));
        Object[] args = new Object[p.length - 1];
        for (int i = 1; i < p.length; i++) args[i - 1] = p[i].startsWith("waterphysics.") ? I18n.format(p[i]) : p[i];
        return I18n.format(p[0], args);
    }

    static BlockPos lastPos() {
        return last == null ? null : last.pos;
    }
}
