package com.realisticworld.core;

import com.realisticworld.util.MathUtil;

/**
 * Performance profiles and adaptive throttling. Measures the mod's own work
 * time per server tick; when too many ticks run over budget, update intervals
 * of scalable systems are stretched (x2, up to x4), and relaxed again when idle.
 */
public final class Performance {
    /** Limits of one profile. Every field is used by the engine or a system. */
    public static final class Profile {
        public final String name;
        public final double tickBudgetMs;
        public final int intervalScale;
        public final int maxQueuedTasks;
        public final int nearRadius;
        public final int mediumRadius;
        public final int farRadius;
        public final int maxCachedRegions;
        public final int sectorWritesPerSave;
        public final int waterBlocksPerUpdate;

        Profile(String name, double budget, int scale, int queue, int near, int medium, int far, int cached, int sectorWrites, int waterBlocks) {
            this.name = name;
            this.tickBudgetMs = budget;
            this.intervalScale = scale;
            this.maxQueuedTasks = queue;
            this.nearRadius = near;
            this.mediumRadius = medium;
            this.farRadius = far;
            this.maxCachedRegions = cached;
            this.sectorWritesPerSave = sectorWrites;
            this.waterBlocksPerUpdate = waterBlocks;
        }
    }

    // Java servers have more headroom than a phone, but the same shape is kept.
    public static final Profile LOW = new Profile("LOW", 2, 2, 64, 1, 2, 4, 256, 6, 64);
    public static final Profile MEDIUM = new Profile("MEDIUM", 4, 1, 128, 2, 4, 6, 512, 12, 160);
    public static final Profile ULTRA = new Profile("ULTRA", 6, 1, 256, 3, 5, 8, 1024, 20, 320);
    public static final int MAX_THROTTLE = 4;

    private Profile profile = MEDIUM;
    private boolean adaptive = true;
    private int throttle = 1;
    private double avgTickMs = Double.NaN;
    private double peakTickMs;
    private long overBudgetTicks;
    private long ticksMeasured;
    private int windowTicks;
    private int windowOver;
    private double windowSumMs;

    public static Profile byName(String name) {
        if ("LOW".equals(name)) return LOW;
        if ("ULTRA".equals(name)) return ULTRA;
        return MEDIUM;
    }

    /** Starting profile from available memory: small heaps get LOW. ULTRA is never automatic. */
    public static String suggest(long maxHeapBytes) {
        return maxHeapBytes > 0 && maxHeapBytes < 1536L * 1024 * 1024 ? "LOW" : "MEDIUM";
    }

    public Profile profile() {
        return profile;
    }

    public void setProfile(String name) {
        profile = byName(name);
    }

    public void setAdaptive(boolean on) {
        adaptive = on;
        if (!on) throttle = 1;
    }

    public boolean adaptive() {
        return adaptive;
    }

    public int throttle() {
        return throttle;
    }

    /** For tests and the benchmark. */
    public void forceThrottle(int t) {
        throttle = t;
    }

    public double avgTickMs() {
        return Double.isNaN(avgTickMs) ? 0 : avgTickMs;
    }

    public double peakTickMs() {
        return peakTickMs;
    }

    public long overBudgetTicks() {
        return overBudgetTicks;
    }

    public long ticksMeasured() {
        return ticksMeasured;
    }

    public void resetPeak() {
        peakTickMs = 0;
    }

    public int scaledInterval(int baseTicks) {
        return Math.max(1, Math.round(baseTicks * profile.intervalScale * throttle));
    }

    /** Record the mod's work time in one tick; re-evaluate throttling every 40 ticks. */
    public void recordTick(double ms) {
        ticksMeasured++;
        avgTickMs = MathUtil.ema(avgTickMs, ms, 0.05);
        if (ms > peakTickMs) peakTickMs = ms;
        boolean over = ms > profile.tickBudgetMs;
        if (over) overBudgetTicks++;
        windowTicks++;
        windowSumMs += ms;
        if (over) windowOver++;
        if (windowTicks >= 40) {
            if (adaptive) {
                double overRatio = (double) windowOver / windowTicks;
                double avg = windowSumMs / windowTicks;
                if (overRatio > 0.25 && throttle < MAX_THROTTLE) throttle *= 2;
                else if (windowOver == 0 && avg < profile.tickBudgetMs * 0.4 && throttle > 1) throttle /= 2;
            }
            windowTicks = 0;
            windowOver = 0;
            windowSumMs = 0;
        }
    }
}
