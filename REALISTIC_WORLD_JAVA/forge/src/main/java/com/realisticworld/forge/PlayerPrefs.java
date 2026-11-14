package com.realisticworld.forge;

import net.minecraft.entity.player.EntityPlayer;
import net.minecraft.entity.player.EntityPlayerMP;
import net.minecraft.nbt.NBTTagCompound;
import net.minecraft.server.MinecraftServer;

/** Per-player HUD choices (kept across deaths and logins) and the operator check. */
public final class PlayerPrefs {
    private static final String KEY = "realisticworld";

    private PlayerPrefs() {}

    private static NBTTagCompound tag(EntityPlayer p) {
        NBTTagCompound persisted = p.getEntityData().getCompoundTag(EntityPlayer.PERSISTED_NBT_TAG);
        p.getEntityData().setTag(EntityPlayer.PERSISTED_NBT_TAG, persisted);
        NBTTagCompound mine = persisted.getCompoundTag(KEY);
        persisted.setTag(KEY, mine);
        return mine;
    }

    public static boolean perfHud(EntityPlayer p) {
        return tag(p).getBoolean("hud");
    }

    public static void setPerfHud(EntityPlayer p, boolean on) {
        tag(p).setBoolean("hud", on);
    }

    public static boolean weatherHud(EntityPlayer p) {
        return tag(p).getBoolean("hudw");
    }

    public static void setWeatherHud(EntityPlayer p, boolean on) {
        tag(p).setBoolean("hudw", on);
    }

    /** Operators, and the owner of a single-player world (like the Bedrock version). */
    public static boolean isOperator(EntityPlayerMP p) {
        MinecraftServer server = p.getServer();
        if (server == null) return false;
        if (server.isSinglePlayer() && p.getName().equals(server.getServerOwner())) return true;
        return server.getPlayerList().canSendCommands(p.getGameProfile());
    }
}
