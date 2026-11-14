package com.realisticworld.forge.client;

import com.realisticworld.forge.CommonProxy;
import com.realisticworld.forge.RwBlocks;
import com.realisticworld.ui.Form;
import net.minecraft.block.Block;
import net.minecraft.client.Minecraft;
import net.minecraft.client.renderer.GlStateManager;
import net.minecraft.client.renderer.block.model.ModelResourceLocation;
import net.minecraft.block.material.Material;
import net.minecraft.item.Item;
import net.minecraftforge.client.event.EntityViewRenderEvent;
import net.minecraftforge.client.event.ModelRegistryEvent;
import net.minecraftforge.client.event.RenderGameOverlayEvent;
import net.minecraftforge.client.model.ModelLoader;
import net.minecraftforge.common.MinecraftForge;
import net.minecraftforge.fml.common.eventhandler.SubscribeEvent;

import java.util.ArrayList;
import java.util.List;

/** Client side: item models, the form screen, HUD lines and weather fog. */
public class ClientProxy extends CommonProxy {
    private List<String> hud = new ArrayList<>();
    private long hudAt;
    private String fog;
    private float fogStart = -1;
    private float fogEnd = -1;

    @Override
    public void preInit() {
        MinecraftForge.EVENT_BUS.register(this);
    }

    @Override
    public void runOnClient(Runnable r) {
        Minecraft.getMinecraft().addScheduledTask(r);
    }

    @Override
    public void showForm(Form form) {
        Minecraft.getMinecraft().displayGuiScreen(new GuiForm(form));
    }

    @Override
    public void setHud(List<String> lines) {
        hud = new ArrayList<>(lines);
        hudAt = System.currentTimeMillis();
    }

    @Override
    public void setFog(String kind) {
        fog = kind;
    }

    @SubscribeEvent
    public void registerModels(ModelRegistryEvent event) {
        for (Block b : new Block[] {RwBlocks.VALVE, RwBlocks.PIPE, RwBlocks.DRAIN}) {
            Item item = Item.getItemFromBlock(b);
            ModelLoader.setCustomModelResourceLocation(item, 0, new ModelResourceLocation(b.getRegistryName(), "inventory"));
        }
        for (Item i : new Item[] {RwBlocks.CONTROL_CENTER, RwBlocks.WATER_GAUGE}) {
            ModelLoader.setCustomModelResourceLocation(i, 0, new ModelResourceLocation(i.getRegistryName(), "inventory"));
        }
    }

    @SubscribeEvent
    public void onOverlayText(RenderGameOverlayEvent.Text event) {
        if (hud.isEmpty() || System.currentTimeMillis() - hudAt > 3000) return; // stale: the server stopped sending
        event.getLeft().addAll(hud);
    }

    /** Fog distances in blocks (same as the Bedrock fog files), and tint colour. */
    private static float[] fogSpec(String kind) {
        switch (kind) {
            case "mist": return new float[] {24, 110, 0xC9 / 255f, 0xD1 / 255f, 0xD9 / 255f};
            case "dense": return new float[] {2, 36, 0xBF / 255f, 0xC6 / 255f, 0xCC / 255f};
            case "rain": return new float[] {8, 64, 0x8C / 255f, 0x94 / 255f, 0x9C / 255f};
            case "blizzard": return new float[] {1, 20, 0xE8 / 255f, 0xEE / 255f, 0xF2 / 255f};
            case "haze": return new float[] {30, 140, 0xD8 / 255f, 0xC9 / 255f, 0xA8 / 255f};
            default: return null;
        }
    }

    @SubscribeEvent
    public void onRenderFog(EntityViewRenderEvent.RenderFogEvent event) {
        if (event.getState().getMaterial() == Material.WATER || event.getState().getMaterial() == Material.LAVA) return;
        float far = event.getFarPlaneDistance();
        float[] spec = fog == null ? null : fogSpec(fog);
        float targetStart = spec == null ? far * 0.75f : Math.min(spec[0], far * 0.75f);
        float targetEnd = spec == null ? far : Math.min(spec[1], far);
        if (fogStart < 0) {
            fogStart = targetStart;
            fogEnd = targetEnd;
        }
        // Ease toward the target so fog rolls in and out instead of popping.
        fogStart += (targetStart - fogStart) * 0.02f;
        fogEnd += (targetEnd - fogEnd) * 0.02f;
        if (spec == null && Math.abs(fogEnd - far) < 0.5f) return; // fully clear: leave vanilla fog alone
        GlStateManager.setFog(GlStateManager.FogMode.LINEAR);
        GlStateManager.setFogStart(fogStart);
        GlStateManager.setFogEnd(fogEnd);
    }

    @SubscribeEvent
    public void onFogColors(EntityViewRenderEvent.FogColors event) {
        float[] spec = fog == null ? null : fogSpec(fog);
        if (spec == null || event.getState().getMaterial() == Material.WATER) return;
        float k = 0.6f;
        event.setRed(event.getRed() * (1 - k) + spec[2] * k);
        event.setGreen(event.getGreen() * (1 - k) + spec[3] * k);
        event.setBlue(event.getBlue() * (1 - k) + spec[4] * k);
    }
}
