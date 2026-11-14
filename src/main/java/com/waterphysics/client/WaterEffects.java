package com.waterphysics.client;

import com.waterphysics.WaterPhysics;
import com.waterphysics.block.BlockPhysWater;
import com.waterphysics.config.WPConfig;
import com.waterphysics.net.MsgEffect;
import com.waterphysics.sim.WaterSim;
import net.minecraft.block.state.IBlockState;
import net.minecraft.client.Minecraft;
import net.minecraft.client.particle.ParticleManager;
import net.minecraft.client.renderer.BufferBuilder;
import net.minecraft.client.renderer.GlStateManager;
import net.minecraft.client.renderer.Tessellator;
import net.minecraft.client.renderer.vertex.DefaultVertexFormats;
import net.minecraft.entity.Entity;
import net.minecraft.init.SoundEvents;
import net.minecraft.util.EnumParticleTypes;
import net.minecraft.util.SoundCategory;
import net.minecraft.util.math.BlockPos;
import net.minecraft.util.math.MathHelper;
import net.minecraft.world.World;
import net.minecraftforge.client.event.RenderWorldLastEvent;
import net.minecraftforge.fml.common.eventhandler.SubscribeEvent;
import net.minecraftforge.fml.common.gameevent.TickEvent;
import org.lwjgl.opengl.GL11;

import java.util.ArrayList;
import java.util.Iterator;
import java.util.List;
import java.util.Random;

/**
 * Client effects driven by the physical state sent by the server:
 * <ul>
 *   <li>particles: splashes at impacts, spray from fast flow and jets, foam, mist at waterfall feet,
 *       bubbles spiralling into whirlpools above drains;</li>
 *   <li>an animated surface overlay on moving water: small travelling waves along the flow, ripples
 *       spreading from impacts, a whirlpool dip over drains, foam patches, and the exact (smoothly
 *       interpolated) water level so filling and draining tanks move continuously.</li>
 * </ul>
 */
public final class WaterEffects {
    private static final Random RAND = new Random();
    private static final List<Ripple> RIPPLES = new ArrayList<>();
    private static final List<float[]> VORTICES = new ArrayList<>();
    public static int lastOverlayCells, lastParticles;

    private static final class Ripple {
        final float x, y, z, amp;
        final long born;
        Ripple(float x, float y, float z, float amp, long born) {
            this.x = x; this.y = y; this.z = z; this.amp = amp; this.born = born;
        }
    }

    private WaterEffects() {}

    private static int quality() {
        return WPConfig.visuals.quality;
    }

    // ---------------------------------------------------------------------------------------------
    // particles
    // ---------------------------------------------------------------------------------------------
    @SubscribeEvent
    public static void onClientTick(TickEvent.ClientTickEvent e) {
        if (e.phase != TickEvent.Phase.END) return;
        Minecraft mc = Minecraft.getMinecraft();
        if (mc.world == null || mc.player == null) {
            ClientWaterData.SECS.clear();
            RIPPLES.clear();
            return;
        }
        if (mc.isGamePaused()) return;
        ClientWaterData.cleanup();
        long now = mc.world.getTotalWorldTime();
        RIPPLES.removeIf(r -> now - r.born > 60);
        if (!WPConfig.visuals.particles) return;
        double density = WPConfig.visuals.particleDensity * (0.4 + 0.3 * quality());
        int radius = WPConfig.visuals.effectsRadius;
        double px = mc.player.posX, py = mc.player.posY, pz = mc.player.posZ;
        ParticleManager pm = mc.effectRenderer;
        World w = mc.world;
        int spawned = 0;
        int budget = 60 + 60 * quality();
        for (ClientWaterData.CSec s : ClientWaterData.SECS.values()) {
            if (!s.active) continue;
            int bx = s.cx << 4, by = s.sy << 4, bz = s.cz << 4;
            if (Math.abs(bx + 8 - px) > radius + 8 || Math.abs(bz + 8 - pz) > radius + 8 || Math.abs(by + 8 - py) > radius + 8) continue;
            for (short c : s.cells) {
                if (spawned > budget) break;
                int i = c & 0xFFFF;
                byte f = s.fx[i];
                if (f == 0) continue;
                double x = bx + (i & 15), y = by + (i >> 8), z = bz + ((i >> 4) & 15);
                double dist2 = (x - px) * (x - px) + (y - py) * (y - py) + (z - pz) * (z - pz);
                if (dist2 > radius * radius) continue;
                float lit = s.liters[i] / 1000f;
                float vx = s.vx[i] / 16f, vy = s.vy[i] / 16f, vz = s.vz[i] / 16f;
                float speed = MathHelper.sqrt(vx * vx + vy * vy + vz * vz);
                double surf = y + Math.min(1f, lit) * BlockPhysWater.SURFACE_SCALE;
                if ((f & WaterSim.FX_IMPACT) != 0 && RAND.nextDouble() < 0.35 * density) {
                    // splash where falling or fast water hits a surface
                    for (int k = 0; k < 2; k++) {
                        w.spawnParticle(EnumParticleTypes.WATER_SPLASH, x + RAND.nextDouble(), surf + 0.05, z + RAND.nextDouble(),
                                (RAND.nextDouble() - 0.5) * 0.3, 0.2 + RAND.nextDouble() * 0.2, (RAND.nextDouble() - 0.5) * 0.3);
                    }
                    if (WPConfig.visuals.foam) {
                        pm.addEffect(new WaterParticles.Foam(w, x + RAND.nextDouble(), surf + 0.02, z + RAND.nextDouble(),
                                vx * 0.02 + (RAND.nextDouble() - 0.5) * 0.04, vz * 0.02 + (RAND.nextDouble() - 0.5) * 0.04, 0.8f + RAND.nextFloat()));
                    }
                    if (RAND.nextInt(4) == 0) addRipple((float) x + 0.5f, (float) surf, (float) z + 0.5f, 0.03f + Math.min(0.05f, speed * 0.01f), mc.world.getTotalWorldTime());
                    spawned += 3;
                }
                if ((f & WaterSim.FX_FALLING) != 0 && RAND.nextDouble() < 0.12 * density) {
                    // waterfall: droplets peeling off and mist at the foot
                    w.spawnParticle(EnumParticleTypes.DRIP_WATER, x + 0.2 + RAND.nextDouble() * 0.6, y + RAND.nextDouble(), z + 0.2 + RAND.nextDouble() * 0.6, 0, 0, 0);
                    IBlockState below = w.getBlockState(new BlockPos(x, y - 1, z));
                    if (below.getMaterial().isSolid() || below.getBlock() == WaterPhysics.WATER_BLOCK && below.getValue(BlockPhysWater.LEVEL) >= 14) {
                        pm.addEffect(new WaterParticles.Mist(w, x + 0.5 + (RAND.nextDouble() - 0.5), y + 0.1, z + 0.5 + (RAND.nextDouble() - 0.5),
                                (RAND.nextDouble() - 0.5) * 0.05, 0.02, (RAND.nextDouble() - 0.5) * 0.05, 2.5f + RAND.nextFloat() * 2f));
                    }
                    spawned++;
                }
                if ((f & (WaterSim.FX_FAST | WaterSim.FX_JET)) != 0 && RAND.nextDouble() < (0.15 + 0.05 * speed) * density) {
                    // spray from fast flow / jets out of holes
                    double sx = vx * 0.05, sz = vz * 0.05;
                    for (int k = 0; k < 2; k++) {
                        pm.addEffect(new WaterParticles.Droplet(w, x + 0.5 + (RAND.nextDouble() - 0.5) * 0.8, y + Math.min(1f, lit) * 0.5 + RAND.nextDouble() * 0.4,
                                z + 0.5 + (RAND.nextDouble() - 0.5) * 0.8, sx + (RAND.nextDouble() - 0.5) * 0.08, 0.05 + RAND.nextDouble() * 0.12,
                                sz + (RAND.nextDouble() - 0.5) * 0.08, 0.4f + RAND.nextFloat() * 0.5f));
                    }
                    if (WPConfig.visuals.foam && (f & WaterSim.FX_SURFACE) != 0 && RAND.nextInt(3) == 0) {
                        pm.addEffect(new WaterParticles.Foam(w, x + RAND.nextDouble(), surf + 0.02, z + RAND.nextDouble(), vx * 0.03, vz * 0.03, 0.6f + RAND.nextFloat()));
                    }
                    spawned += 2;
                }
                if ((f & WaterSim.FX_VORTEX) != 0 && WPConfig.visuals.vortex) {
                    if (RAND.nextDouble() < 0.5 * density) {
                        pm.addEffect(new WaterParticles.VortexBubble(w, x + 0.5, surf, z + 0.5, 0.35 + RAND.nextDouble() * 0.3, 0.5f + RAND.nextFloat() * 0.4f));
                        spawned++;
                    }
                }
            }
        }
        lastParticles = spawned;
    }

    public static void addRipple(float x, float y, float z, float amp, long t) {
        if (RIPPLES.size() > 160) RIPPLES.remove(0);
        RIPPLES.add(new Ripple(x, y, z, amp, t));
    }

    /** one-shot effects sent by the server */
    public static void oneShot(MsgEffect m) {
        Minecraft mc = Minecraft.getMinecraft();
        World w = mc.world;
        if (w == null) return;
        double x = m.pos.getX() + 0.5, y = m.pos.getY() + 0.5, z = m.pos.getZ() + 0.5;
        ParticleManager pm = mc.effectRenderer;
        switch (m.type) {
            case MsgEffect.BURST: {
                for (int k = 0; k < 60; k++) {
                    double a = RAND.nextDouble() * Math.PI * 2, b = RAND.nextDouble() * Math.PI - Math.PI / 2;
                    double sp = 0.2 + RAND.nextDouble() * 0.5;
                    pm.addEffect(new WaterParticles.Droplet(w, x, y, z, Math.cos(a) * Math.cos(b) * sp, Math.sin(b) * sp + 0.2, Math.sin(a) * Math.cos(b) * sp, 0.6f + RAND.nextFloat()));
                }
                for (int k = 0; k < 12; k++) {
                    pm.addEffect(new WaterParticles.Mist(w, x, y, z, (RAND.nextDouble() - 0.5) * 0.2, RAND.nextDouble() * 0.1, (RAND.nextDouble() - 0.5) * 0.2, 3f + RAND.nextFloat() * 3f));
                }
                w.playSound(x, y, z, SoundEvents.ENTITY_GENERIC_SPLASH, SoundCategory.BLOCKS, 1.0f, 0.8f, false);
                break;
            }
            case MsgEffect.CRACK: {
                for (int k = 0; k < 15; k++) {
                    pm.addEffect(new WaterParticles.Droplet(w, x, y, z, (RAND.nextDouble() - 0.5) * 0.3, 0.15, (RAND.nextDouble() - 0.5) * 0.3, 0.4f));
                }
                break;
            }
            case MsgEffect.LEAK:
            case MsgEffect.SPLASH: {
                for (int k = 0; k < 8; k++) {
                    w.spawnParticle(EnumParticleTypes.WATER_SPLASH, x + RAND.nextDouble() - 0.5, y, z + RAND.nextDouble() - 0.5, 0, 0.2, 0);
                }
                addRipple((float) x, (float) y, (float) z, 0.05f, w.getTotalWorldTime());
                break;
            }
            default:
                break;
        }
    }

    // ---------------------------------------------------------------------------------------------
    // wave overlay
    // ---------------------------------------------------------------------------------------------
    @SubscribeEvent
    public static void onRenderLast(RenderWorldLastEvent e) {
        if (!WPConfig.visuals.waves) return;
        Minecraft mc = Minecraft.getMinecraft();
        if (mc.world == null || ClientWaterData.SECS.isEmpty()) return;
        Entity cam = mc.getRenderViewEntity();
        if (cam == null) return;
        float pt = e.getPartialTicks();
        double cx = cam.lastTickPosX + (cam.posX - cam.lastTickPosX) * pt;
        double cy = cam.lastTickPosY + (cam.posY - cam.lastTickPosY) * pt;
        double cz = cam.lastTickPosZ + (cam.posZ - cam.lastTickPosZ) * pt;
        float time = (mc.world.getTotalWorldTime() + pt) / 20f;
        long now = mc.world.getTotalWorldTime();
        int res = Math.max(2, Math.min(8, WPConfig.visuals.waveResolution));
        if (quality() == 0) res = 2;
        int radius = WPConfig.visuals.effectsRadius;
        int maxCells = 400 + 700 * quality();

        VORTICES.clear();
        for (ClientWaterData.CSec s : ClientWaterData.SECS.values()) {
            if (!s.active) continue;
            for (short c : s.cells) {
                int i = c & 0xFFFF;
                if ((s.fx[i] & WaterSim.FX_VORTEX) != 0) {
                    VORTICES.add(new float[]{(s.cx << 4) + (i & 15) + 0.5f, (s.cz << 4) + ((i >> 4) & 15) + 0.5f});
                }
            }
        }

        GlStateManager.pushMatrix();
        GlStateManager.translate(-cx, -cy, -cz);
        GlStateManager.enableBlend();
        GlStateManager.tryBlendFuncSeparate(GlStateManager.SourceFactor.SRC_ALPHA, GlStateManager.DestFactor.ONE_MINUS_SRC_ALPHA,
                GlStateManager.SourceFactor.ONE, GlStateManager.DestFactor.ZERO);
        GlStateManager.disableTexture2D();
        GlStateManager.disableLighting();
        GlStateManager.disableCull();
        GlStateManager.depthMask(false);
        GlStateManager.enableDepth();
        GlStateManager.enablePolygonOffset();
        GlStateManager.doPolygonOffset(-1f, -3f);
        GlStateManager.shadeModel(GL11.GL_SMOOTH);
        Tessellator tes = Tessellator.getInstance();
        BufferBuilder b = tes.getBuffer();
        b.begin(GL11.GL_QUADS, DefaultVertexFormats.POSITION_COLOR);
        int drawn = 0;
        World w = mc.world;
        outer:
        for (ClientWaterData.CSec s : ClientWaterData.SECS.values()) {
            if (!s.active) continue;
            int bx = s.cx << 4, by = s.sy << 4, bz = s.cz << 4;
            if (Math.abs(bx + 8 - cx) > radius + 8 || Math.abs(bz + 8 - cz) > radius + 8 || Math.abs(by + 8 - cy) > radius + 16) continue;
            for (short c : s.cells) {
                int i = c & 0xFFFF;
                byte f = s.fx[i];
                if ((f & WaterSim.FX_SURFACE) == 0) continue;
                int x = bx + (i & 15), y = by + (i >> 8), z = bz + ((i >> 4) & 15);
                if ((x - cx) * (x - cx) + (z - cz) * (z - cz) > radius * radius) continue;
                IBlockState here = w.getBlockState(new BlockPos(x, y, z));
                if (here.getBlock() != WaterPhysics.WATER_BLOCK) continue;
                if (drawn++ > maxCells) break outer;
                float vx = s.vx[i] / 16f, vz = s.vz[i] / 16f;
                float speed = MathHelper.sqrt(vx * vx + vz * vz);
                float h00 = cornerHeight(w, x, y, z, 0, 0, pt), h01 = cornerHeight(w, x, y, z, 0, 1, pt);
                float h11 = cornerHeight(w, x, y, z, 1, 1, pt), h10 = cornerHeight(w, x, y, z, 1, 0, pt);
                float foam = ((f & WaterSim.FX_IMPACT) != 0 ? 0.35f : 0f) + ((f & WaterSim.FX_FAST) != 0 ? 0.25f : 0f);
                // only ripples / vortices close to this cell influence it
                NEAR_R.clear();
                for (Ripple r : RIPPLES) if (Math.abs(r.x - x - 0.5f) < 7f && Math.abs(r.z - z - 0.5f) < 7f && Math.abs(r.y - y) < 3f) NEAR_R.add(r);
                NEAR_V.clear();
                for (float[] vv : VORTICES) if (Math.abs(vv[0] - x - 0.5f) < 2.5f && Math.abs(vv[1] - z - 0.5f) < 2.5f) NEAR_V.add(vv);
                int n1 = res + 1;
                float step = 1f / res;
                for (int a = 0; a <= res; a++) {
                    for (int bb = 0; bb <= res; bb++) {
                        int k = a * n1 + bb;
                        float u = a * step, v = bb * step;
                        float base = h00 * (1 - u) * (1 - v) + h10 * u * (1 - v) + h01 * (1 - u) * v + h11 * u * v;
                        float wx = x + u, wz = z + v;
                        float hh = waveHeight(wx, wz, vx, vz, speed, time, now, pt);
                        float ex = 0.06f;
                        float sx = (waveHeight(wx + ex, wz, vx, vz, speed, time, now, pt) - hh) / ex;
                        float sz = (waveHeight(wx, wz + ex, vx, vz, speed, time, now, pt) - hh) / ex;
                        float lightv = MathHelper.clamp(0.5f + (-sx * 0.6f - sz * 0.4f) * 3f, 0f, 1f);
                        float spec = (float) Math.pow(lightv, 6.0) * 0.55f;
                        float swirl = 0f;
                        for (float[] vv : NEAR_V) {
                            float dx = wx - vv[0], dz = wz - vv[1];
                            swirl = Math.max(swirl, (float) Math.exp(-(dx * dx + dz * dz) * 3f));
                        }
                        GY[k] = y + base + hh + 0.004f;
                        GR[k] = MathHelper.clamp(0.55f + spec + foam * 0.4f - swirl * 0.3f, 0f, 1f);
                        GG[k] = MathHelper.clamp(0.72f + spec + foam * 0.3f - swirl * 0.3f, 0f, 1f);
                        GB[k] = MathHelper.clamp(0.95f + spec, 0f, 1f);
                        GA[k] = MathHelper.clamp(0.16f + spec * 0.5f + foam * 0.35f + swirl * 0.25f, 0f, 0.85f);
                    }
                }
                for (int a = 0; a < res; a++) {
                    for (int bb = 0; bb < res; bb++) {
                        emit(b, x, z, a, bb, n1, step);
                        emit(b, x, z, a, bb + 1, n1, step);
                        emit(b, x, z, a + 1, bb + 1, n1, step);
                        emit(b, x, z, a + 1, bb, n1, step);
                    }
                }
            }
        }
        tes.draw();
        lastOverlayCells = drawn;
        GlStateManager.doPolygonOffset(0f, 0f);
        GlStateManager.disablePolygonOffset();
        GlStateManager.shadeModel(GL11.GL_FLAT);
        GlStateManager.depthMask(true);
        GlStateManager.enableCull();
        GlStateManager.enableTexture2D();
        GlStateManager.disableBlend();
        GlStateManager.popMatrix();
    }

    /** surface height at a corner from the exact (interpolated) volumes of the 4 cells sharing it */
    private static float cornerHeight(World w, int x, int y, int z, int cxo, int czo, float pt) {
        float sum = 0f;
        int n = 0;
        for (int a = cxo - 1; a <= cxo; a++) {
            for (int b = czo - 1; b <= czo; b++) {
                int nx = x + a, nz = z + b;
                BlockPos p = new BlockPos(nx, y, nz);
                IBlockState st = w.getBlockState(p);
                if (st.getBlock() == WaterPhysics.WATER_BLOCK) {
                    IBlockState up = w.getBlockState(p.up());
                    if (up.getBlock() == WaterPhysics.WATER_BLOCK) return 1f;
                    float l = ClientWaterData.liters(nx, y, nz, pt);
                    float h = l >= 0f ? Math.min(1f, l / 1000f) : (st.getValue(BlockPhysWater.LEVEL) + 1) / 16f;
                    sum += h * BlockPhysWater.SURFACE_SCALE;
                    n++;
                } else if (!st.getMaterial().isSolid()) {
                    n++;
                }
            }
        }
        return n == 0 ? 0.5f : Math.max(0.02f, sum / n);
    }

    private static float waveHeight(float wx, float wz, float vx, float vz, float speed, float time, long now, float pt) {
        float h = 0f;
        // travelling waves along the current + small wind ripples
        float dirx = speed > 0.05f ? vx / speed : 0.7f, dirz = speed > 0.05f ? vz / speed : 0.7f;
        float amp = 0.008f + Math.min(0.03f, speed * 0.012f);
        float phase = (wx * dirx + wz * dirz) * 4.5f - time * (2.5f + speed * 2.5f);
        h += amp * MathHelper.sin(phase);
        h += 0.005f * MathHelper.sin(wx * 7.1f + time * 3.3f) * MathHelper.cos(wz * 6.3f - time * 2.7f);
        // ripples from impacts
        for (Ripple r : NEAR_R) {
            float age = (now - r.born + pt) / 20f;
            float dx = wx - r.x, dz = wz - r.z;
            float d = MathHelper.sqrt(dx * dx + dz * dz);
            float front = age * 1.6f;
            if (d > front + 0.5f || d > 6f) continue;
            float decay = (float) Math.exp(-age * 1.5f) / (1f + d * 1.5f);
            h += r.amp * decay * MathHelper.sin((d - front) * 9f);
        }
        // whirlpool dip
        for (float[] v : NEAR_V) {
            float dx = wx - v[0], dz = wz - v[1];
            float d2 = dx * dx + dz * dz;
            if (d2 < 2.25f) h -= 0.18f * (float) Math.exp(-d2 * 3f);
        }
        return h;
    }

    private static final float[] GY = new float[81], GR = new float[81], GG = new float[81], GB = new float[81], GA = new float[81];
    private static final List<Ripple> NEAR_R = new ArrayList<>();
    private static final List<float[]> NEAR_V = new ArrayList<>();

    private static void emit(BufferBuilder b, int x, int z, int a, int bb, int n1, float step) {
        int k = a * n1 + bb;
        b.pos(x + a * step, GY[k], z + bb * step).color(GR[k], GG[k], GB[k], GA[k]).endVertex();
    }

    static Iterator<Ripple> ripples() {
        return RIPPLES.iterator();
    }
}
