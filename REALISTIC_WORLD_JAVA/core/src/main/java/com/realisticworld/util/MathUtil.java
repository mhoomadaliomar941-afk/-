package com.realisticworld.util;

/** Small numeric helpers shared by every system. */
public final class MathUtil {
    private MathUtil() {}

    public static double clamp(double v, double min, double max) {
        return v < min ? min : v > max ? max : v;
    }

    public static int clamp(int v, int min, int max) {
        return v < min ? min : v > max ? max : v;
    }

    /** Round to a number of decimals (for display and stable saved values). */
    public static double round(double v, int decimals) {
        double f = Math.pow(10, decimals);
        return Math.round(v * f) / f;
    }

    /** Block coordinate to chunk coordinate (16x16 columns). */
    public static int toChunk(double blockCoord) {
        return (int) Math.floor(blockCoord / 16.0);
    }

    /** Exponential moving average step; NaN previous value means "no value yet". */
    public static double ema(double prev, double sample, double alpha) {
        return Double.isNaN(prev) ? sample : prev + alpha * (sample - prev);
    }

    /** Relaxation toward a target with time constant tau over h (same units). */
    public static double relax(double cur, double target, double h, double tau) {
        return cur + (target - cur) * (1 - Math.exp(-h / tau));
    }

    /** Numeric key for a column (exact for |x|,|z| below 2^25, past the world border). */
    public static long columnKey(int x, int z) {
        return ((long) (x + 33554432)) * 67108864L + (z + 33554432);
    }
}
