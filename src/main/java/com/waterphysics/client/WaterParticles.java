package com.waterphysics.client;

import net.minecraft.client.particle.Particle;
import net.minecraft.client.renderer.BufferBuilder;
import net.minecraft.client.renderer.texture.TextureAtlasSprite;
import net.minecraft.entity.Entity;
import net.minecraft.util.math.MathHelper;
import net.minecraft.world.World;

/** Custom particles: foam, mist, spray droplets and whirlpool bubbles (textures on the block atlas). */
public final class WaterParticles {
    private WaterParticles() {}

    private static final TextureAtlasSprite FOAM = null, MIST = null, DROP = null, BUBBLE = null;

    abstract static class Base extends Particle {
        float baseAlpha;

        Base(World w, double x, double y, double z, double mx, double my, double mz, TextureAtlasSprite tex) {
            super(w, x, y, z, 0, 0, 0);
            motionX = mx;
            motionY = my;
            motionZ = mz;
        }

        /** vanilla particle sheet (layer 0): 0..7 = puff, 19..22 = splash, 32 = bubble, 112/113 = drip */
        @Override
        public int getFXLayer() {
            return 0;
        }

        float life() {
            return particleMaxAge <= 0 ? 1f : (float) particleAge / particleMaxAge;
        }
    }

    /** floating foam patch that drifts with the current and dissolves */
    public static final class Foam extends Base {
        private final double surfaceY;

        public Foam(World w, double x, double y, double z, double mx, double mz, float size) {
            super(w, x, y, z, mx, 0, mz, FOAM);
            surfaceY = y;
            particleScale = size;
            particleMaxAge = 30 + rand.nextInt(40);
            particleGravity = 0f;
            canCollide = false;
            baseAlpha = 0.85f;
            setRBGColorF(0.95f, 0.98f, 1f);
            setParticleTextureIndex(2 + rand.nextInt(3));
        }

        @Override
        public void onUpdate() {
            prevPosX = posX;
            prevPosY = posY;
            prevPosZ = posZ;
            if (particleAge++ >= particleMaxAge) setExpired();
            motionX *= 0.92;
            motionZ *= 0.92;
            posX += motionX;
            posZ += motionZ;
            posY = surfaceY + 0.01 * MathHelper.sin(particleAge * 0.4f);
            setAlphaF(baseAlpha * (1f - life()));
            particleScale *= 1.004f;
        }
    }

    /** soft mist at the foot of waterfalls and around bursts */
    public static final class Mist extends Base {
        public Mist(World w, double x, double y, double z, double mx, double my, double mz, float size) {
            super(w, x, y, z, mx, my, mz, MIST);
            particleScale = size;
            particleMaxAge = 25 + rand.nextInt(30);
            particleGravity = -0.002f;
            canCollide = false;
            baseAlpha = 0.35f;
            setRBGColorF(0.92f, 0.96f, 1f);
            setParticleTextureIndex(7);
        }

        @Override
        public void onUpdate() {
            super.onUpdate();
            motionX *= 0.95;
            motionY *= 0.95;
            motionZ *= 0.95;
            particleScale *= 1.02f;
            setParticleTextureIndex(7 - Math.min(7, (int) (life() * 8)));
            setAlphaF(baseAlpha * (1f - life()));
        }
    }

    /** water droplet of spray / jets */
    public static final class Droplet extends Base {
        public Droplet(World w, double x, double y, double z, double mx, double my, double mz, float size) {
            super(w, x, y, z, mx, my, mz, DROP);
            particleScale = size;
            particleMaxAge = 20 + rand.nextInt(20);
            particleGravity = 0.9f;
            canCollide = true;
            baseAlpha = 0.9f;
            setRBGColorF(0.55f, 0.72f, 1f);
            setParticleTextureIndex(19 + rand.nextInt(4));
        }

        @Override
        public void onUpdate() {
            super.onUpdate();
            if (onGround) setExpired();
            setAlphaF(baseAlpha * (1f - life() * 0.5f));
        }
    }

    /** bubble circling down a whirlpool */
    public static final class VortexBubble extends Base {
        private final double cx, cz;
        private double radius, angle, speed;

        public VortexBubble(World w, double cx, double y, double cz, double radius, float size) {
            super(w, cx + radius, y, cz, 0, 0, 0, BUBBLE);
            this.cx = cx;
            this.cz = cz;
            this.radius = radius;
            this.angle = rand.nextDouble() * Math.PI * 2;
            this.speed = 0.25 + rand.nextDouble() * 0.2;
            particleScale = size;
            particleMaxAge = 40 + rand.nextInt(20);
            canCollide = false;
            baseAlpha = 0.8f;
            setRBGColorF(0.85f, 0.95f, 1f);
            setParticleTextureIndex(32);
        }

        @Override
        public void onUpdate() {
            prevPosX = posX;
            prevPosY = posY;
            prevPosZ = posZ;
            if (particleAge++ >= particleMaxAge || radius < 0.02) setExpired();
            angle += speed / Math.max(0.15, radius);
            radius *= 0.965;
            posX = cx + Math.cos(angle) * radius;
            posZ = cz + Math.sin(angle) * radius;
            posY -= 0.012 + (0.5 - radius) * 0.01;
            setAlphaF(baseAlpha * (1f - life()));
        }
    }

    static void renderNoop(BufferBuilder b, Entity e) {}
}
