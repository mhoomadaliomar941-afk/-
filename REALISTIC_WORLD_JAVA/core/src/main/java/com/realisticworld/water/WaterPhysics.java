package com.realisticworld.water;

import com.realisticworld.util.MathUtil;

import java.util.HashMap;
import java.util.Map;

/**
 * Water physics formulas (SI units; 1 block = 1 m³):
 * hydrostatic pressure P = ρgh; leak through an opening Q = Cd·A·sqrt(2gh)
 * (Torricelli), with h from the surface to the middle of the wetted part;
 * infiltration and evaporation as gameplay-scaled rates.
 */
public final class WaterPhysics {
    public static final double G = 9.81;
    public static final double RHO = 1000;
    public static final double CD = 0.6;

    private WaterPhysics() {}

    /** Area in m² of each kind of opening. */
    public static double openingArea(String kind) {
        switch (kind) {
            case "valve":
            case "pipe":
            case "drain":
                return 0.25;
            default:
                return 1;
        }
    }

    public static double pressureKPa(double h) {
        return RHO * G * Math.max(0, h) / 1000;
    }

    /** Side opening whose bottom edge is at block y `bottom`, one block tall. */
    public static double sideFlow(double level, double bottom, double area) {
        double wetted = MathUtil.clamp(level - bottom, 0, 1);
        if (wetted <= 0) return 0;
        double head = level - (bottom + wetted / 2);
        return CD * area * wetted * Math.sqrt(2 * G * Math.max(0, head));
    }

    /** Opening in the floor: the whole column above presses. */
    public static double floorFlow(double level, double floorTop, double area) {
        double head = level - floorTop;
        return head > 0 ? CD * area * Math.sqrt(2 * G * head) : 0;
    }

    /** Pipe between two surfaces (positive: A to B); a side below its pipe end cannot push. */
    public static double pipeFlow(double levelA, double endA, double levelB, double endB, double area) {
        double pushA = Math.max(levelA, endA + 0.5);
        double pushB = Math.max(levelB, endB + 0.5);
        double dh = pushA - pushB;
        if (dh > 0 && levelA <= endA) return 0;
        if (dh < 0 && levelB <= endB) return 0;
        return Math.signum(dh) * CD * area * Math.sqrt(2 * G * Math.abs(dh));
    }

    /** Surface height of `volume` in layers (layers[i] cells at y = baseY + i). */
    public static double levelFromVolume(int[] layers, int baseY, double volume) {
        double v = Math.max(0, volume);
        for (int i = 0; i < layers.length; i++) {
            int a = layers[i];
            if (a <= 0) continue;
            if (v <= a) return baseY + i + v / a;
            v -= a;
        }
        return baseY + layers.length;
    }

    public static double volumeBelow(int[] layers, int baseY, double level) {
        double v = 0;
        for (int i = 0; i < layers.length; i++) {
            int y = baseY + i;
            if (level <= y) break;
            v += layers[i] * Math.min(1, level - y);
        }
        return v;
    }

    public static int capacity(int[] layers) {
        int c = 0;
        for (int a : layers) c += a;
        return c;
    }

    private static final Map<String, Double> INFILTRATION = new HashMap<>();

    /** m/s into the ground by the block under the water (gameplay-scaled). */
    public static double infiltrationRate(String groundBlock) {
        Double r = INFILTRATION.get(groundBlock);
        if (r == null) {
            String id = groundBlock.startsWith("minecraft:") ? groundBlock.substring(10) : groundBlock;
            if (id.contains("sand") || id.contains("gravel")) r = 0.008;
            else if (id.matches(".*(grass|dirt|podzol|mycelium|farmland|moss).*")) r = 0.002;
            else if (id.equals("mud") || id.contains("clay") || id.contains("terracotta") || id.contains("hardened_clay")) r = 0.0003;
            else if (id.contains("leaves") || id.contains("snow")) r = 0.004;
            else r = 0.0;
            INFILTRATION.put(groundBlock, r);
        }
        return r;
    }

    /** m/s from an open surface: warm, dry, windy is faster (gameplay-scaled). */
    public static double evaporationRate(double tempC, double humidity, double wind) {
        if (tempC <= 0) return 0;
        return 1e-4 * (tempC / 20) * MathUtil.clamp(1.1 - humidity, 0.05, 1.1) * (1 + wind / 10);
    }

    /** m/s of rain into an open surface from intensity 0..1. */
    public static double rainRate(double intensity) {
        return 4e-4 * intensity;
    }

    /** Freezes below -1°C, thaws above +1°C. */
    public static boolean nextFrozen(boolean frozen, double tempC) {
        if (tempC <= -1) return true;
        if (tempC >= 1) return false;
        return frozen;
    }

    // ---- packed block positions (same layout as Minecraft's BlockPos.toLong) ----

    public static long pack(int x, int y, int z) {
        return ((long) (x & 0x3FFFFFF) << 38) | ((long) (y & 0xFFF) << 26) | (z & 0x3FFFFFFL);
    }

    public static int unpackX(long p) {
        return (int) (p >> 38);
    }

    public static int unpackY(long p) {
        return (int) (p << 26 >> 52);
    }

    public static int unpackZ(long p) {
        return (int) (p << 38 >> 38);
    }
}
