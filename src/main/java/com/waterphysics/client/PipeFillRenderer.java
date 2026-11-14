package com.waterphysics.client;

import com.waterphysics.block.BlockPipe;
import com.waterphysics.tile.TileFluidNode;
import net.minecraft.block.Block;
import net.minecraft.client.Minecraft;
import net.minecraft.client.renderer.BufferBuilder;
import net.minecraft.client.renderer.GlStateManager;
import net.minecraft.client.renderer.RenderHelper;
import net.minecraft.client.renderer.Tessellator;
import net.minecraft.client.renderer.texture.TextureAtlasSprite;
import net.minecraft.client.renderer.texture.TextureMap;
import net.minecraft.client.renderer.tileentity.TileEntitySpecialRenderer;
import net.minecraft.client.renderer.vertex.DefaultVertexFormats;
import org.lwjgl.opengl.GL11;

/** Shows the water level inside glass pipes. */
public class PipeFillRenderer extends TileEntitySpecialRenderer<TileFluidNode> {
    @Override
    public void render(TileFluidNode te, double x, double y, double z, float partialTicks, int destroyStage, float alpha) {
        Block b = te.getWorld().getBlockState(te.getPos()).getBlock();
        if (!(b instanceof BlockPipe) || !((BlockPipe) b).glass) return;
        float fill = Math.min(1f, te.clientFill);
        if (fill <= 0.01f) return;
        float r = Math.min(0.35f, ((BlockPipe) b).diameter * 0.75f) - 0.02f;
        TextureAtlasSprite tex = Minecraft.getMinecraft().getTextureMapBlocks().getAtlasSprite("minecraft:blocks/water_still");
        bindTexture(TextureMap.LOCATION_BLOCKS_TEXTURE);
        GlStateManager.pushMatrix();
        GlStateManager.translate(x, y, z);
        GlStateManager.enableBlend();
        GlStateManager.blendFunc(GlStateManager.SourceFactor.SRC_ALPHA, GlStateManager.DestFactor.ONE_MINUS_SRC_ALPHA);
        GlStateManager.disableLighting();
        RenderHelper.disableStandardItemLighting();
        float x0 = 0.5f - r, x1 = 0.5f + r, y0 = 0.5f - r, y1 = y0 + 2 * r * fill, z0 = 0.5f - r, z1 = 0.5f + r;
        Tessellator t = Tessellator.getInstance();
        BufferBuilder buf = t.getBuffer();
        buf.begin(GL11.GL_QUADS, DefaultVertexFormats.POSITION_TEX_COLOR);
        float u0 = tex.getMinU(), u1 = tex.getMaxU(), v0 = tex.getMinV(), v1 = tex.getMaxV();
        float cr = 0.25f, cg = 0.46f, cb = 0.9f, ca = 0.75f;
        // top
        buf.pos(x0, y1, z0).tex(u0, v0).color(cr, cg, cb, ca).endVertex();
        buf.pos(x0, y1, z1).tex(u0, v1).color(cr, cg, cb, ca).endVertex();
        buf.pos(x1, y1, z1).tex(u1, v1).color(cr, cg, cb, ca).endVertex();
        buf.pos(x1, y1, z0).tex(u1, v0).color(cr, cg, cb, ca).endVertex();
        // sides
        float[][] sides = {{x0, z0, x1, z0}, {x1, z1, x0, z1}, {x0, z1, x0, z0}, {x1, z0, x1, z1}};
        for (float[] s : sides) {
            buf.pos(s[0], y1, s[1]).tex(u0, v0).color(cr, cg, cb, ca).endVertex();
            buf.pos(s[2], y1, s[3]).tex(u1, v0).color(cr, cg, cb, ca).endVertex();
            buf.pos(s[2], y0, s[3]).tex(u1, v1).color(cr, cg, cb, ca).endVertex();
            buf.pos(s[0], y0, s[1]).tex(u0, v1).color(cr, cg, cb, ca).endVertex();
        }
        t.draw();
        GlStateManager.disableBlend();
        GlStateManager.enableLighting();
        GlStateManager.popMatrix();
    }
}
