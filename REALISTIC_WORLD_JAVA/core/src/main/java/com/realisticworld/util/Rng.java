package com.realisticworld.util;

/**
 * Seeded random numbers (mulberry32) whose whole state is one int, so it can be
 * saved with the world: the weather continues the same way after a reload and
 * forecasts can replay the future from a copy. Same sequence as the Bedrock
 * version for the same state.
 */
public final class Rng {
    private int s;

    public Rng(int state) {
        this.s = state;
    }

    public int state() {
        return s;
    }

    public void setState(int state) {
        this.s = state;
    }

    public Rng copy() {
        return new Rng(s);
    }

    /** Uniform in [0, 1). */
    public double nextDouble() {
        s += 0x6d2b79f5;
        int t = s;
        t = (t ^ (t >>> 15)) * (t | 1);
        t ^= t + (t ^ (t >>> 7)) * (t | 61);
        return ((t ^ (t >>> 14)) & 0xffffffffL) / 4294967296.0;
    }

    /** Standard normal sample (Box-Muller). */
    public double nextGaussian() {
        double u = 0;
        while (u == 0) u = nextDouble();
        double v = nextDouble();
        return Math.sqrt(-2 * Math.log(u)) * Math.cos(2 * Math.PI * v);
    }

    /** Seed from a world seed or any text (FNV-1a). */
    public static Rng fromSeed(String value) {
        int h = 0x811c9dc5;
        for (int i = 0; i < value.length(); i++) {
            h ^= value.charAt(i);
            h *= 16777619;
        }
        return new Rng(h);
    }
}
