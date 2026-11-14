package com.realisticworld.water;

import com.realisticworld.util.MathUtil;

import java.util.ArrayList;
import java.util.Arrays;
import java.util.HashSet;
import java.util.List;
import java.util.PriorityQueue;
import java.util.Set;

/**
 * Where poured water ends up on the terrain ("fill and spill"): it runs
 * downhill to a low point, fills it with a flat surface up to the lowest rim,
 * and anything more runs on from the spill point. On flat ground it spreads
 * only while the sheet stays at least MIN_FILM thick.
 */
public final class Basin {
    public static final double MIN_FILM = 0.1;
    private static final int[][] N4 = {{1, 0}, {-1, 0}, {0, 1}, {0, -1}};

    private Basin() {}

    /** A terrain column: water stands from `base` up; `water` means natural water is there. */
    public static final class Column {
        public final int base;
        public final boolean water;
        public final String ground;

        public Column(int base, boolean water, String ground) {
            this.base = base;
            this.water = water;
            this.ground = ground;
        }
    }

    /** Terrain lookup; null = unknown/unloaded (treated as a wall). */
    public interface HeightFn {
        Column at(int x, int z);
    }

    public static final class Point {
        public final int x;
        public final int z;
        public final Column col;

        Point(int x, int z, Column col) {
            this.x = x;
            this.z = z;
            this.col = col;
        }
    }

    /** One column of a pool. */
    public static final class PoolCol {
        public final int x;
        public final int z;
        public final int base;
        public final String ground;

        public PoolCol(int x, int z, int base, String ground) {
            this.x = x;
            this.z = z;
            this.base = base;
            this.ground = ground;
        }
    }

    public static final class Fill {
        public final List<PoolCol> cols;
        public final double level;
        public final double used;
        public final int[] spill;
        public final double leftover;
        public final boolean joinedWater;
        public final boolean hitLimit;
        public final double nextBase;

        Fill(List<PoolCol> cols, double level, double used, int[] spill, double leftover, boolean joinedWater, boolean hitLimit, double nextBase) {
            this.cols = cols;
            this.level = level;
            this.used = used;
            this.spill = spill;
            this.leftover = leftover;
            this.joinedWater = joinedWater;
            this.hitLimit = hitLimit;
            this.nextBase = nextBase;
        }
    }

    /** Follow the way down to a low point, searching flat areas for a way lower. */
    public static Point descend(int x, int z, HeightFn h) {
        return descend(x, z, h, 64, 48);
    }

    public static Point descend(int x, int z, HeightFn h, int maxSteps, int maxFlat) {
        Column col = h.at(x, z);
        if (col == null) return null;
        for (int i = 0; i < maxSteps && !col.water; i++) {
            Point best = null;
            for (int[] d : N4) {
                Column c = h.at(x + d[0], z + d[1]);
                if (c != null && (c.base < col.base || (c.water && c.base <= col.base)) && (best == null || c.base < best.col.base)) {
                    best = new Point(x + d[0], z + d[1], c);
                }
            }
            if (best == null) best = flatExit(x, z, col.base, h, maxFlat);
            if (best == null) break;
            x = best.x;
            z = best.z;
            col = best.col;
        }
        return new Point(x, z, col);
    }

    private static Point flatExit(int x0, int z0, int base, HeightFn h, int maxFlat) {
        Set<Long> seen = new HashSet<>();
        seen.add(MathUtil.columnKey(x0, z0));
        List<int[]> queue = new ArrayList<>();
        queue.add(new int[] {x0, z0});
        for (int qi = 0; qi < queue.size() && seen.size() <= maxFlat; qi++) {
            int[] p = queue.get(qi);
            for (int[] d : N4) {
                int nx = p[0] + d[0];
                int nz = p[1] + d[1];
                if (!seen.add(MathUtil.columnKey(nx, nz))) continue;
                Column c = h.at(nx, nz);
                if (c == null) continue;
                if (c.base < base || c.water) return new Point(nx, nz, c);
                if (c.base == base) queue.add(new int[] {nx, nz});
            }
        }
        return null;
    }

    private static final class Cand {
        final int x;
        final int z;
        final Column c;
        final long order;

        Cand(int x, int z, Column c, long order) {
            this.x = x;
            this.z = z;
            this.c = c;
            this.order = order;
        }
    }

    /** Fill the depression around a low point with `volume` m³. */
    public static Fill fill(int sx, int sz, double volume, HeightFn h, int maxCols, int maxRadius) {
        Column first = h.at(sx, sz);
        List<PoolCol> cols = new ArrayList<>();
        if (first == null) return new Fill(cols, 0, 0, null, volume, false, false, Double.POSITIVE_INFINITY);
        if (first.water) return new Fill(cols, 0, 0, null, 0, true, false, Double.POSITIVE_INFINITY);
        Set<Long> seen = new HashSet<>();
        seen.add(MathUtil.columnKey(sx, sz));
        PriorityQueue<Cand> heap = new PriorityQueue<>((a, b) -> a.c.base != b.c.base ? Integer.compare(a.c.base, b.c.base) : Long.compare(a.order, b.order));
        long order = 0;
        heap.add(new Cand(sx, sz, first, order++));
        double level = first.base;
        double v = Math.max(0, volume);
        while (!heap.isEmpty()) {
            Cand cd = heap.poll();
            if (cd.c.water) return new Fill(cols, level, volume - v, null, 0, true, false, level);
            if (cd.c.base < level - 1e-9) return new Fill(cols, level, volume - v, new int[] {cd.x, cd.z}, v, false, false, level);
            if (!cols.isEmpty()) {
                double need = (cd.c.base - level) * cols.size();
                if (v < need + MIN_FILM * (cols.size() + 1)) {
                    level += v / cols.size();
                    return new Fill(cols, level, volume, null, 0, false, false, cd.c.base + MIN_FILM);
                }
                v -= need;
            }
            level = cd.c.base;
            cols.add(new PoolCol(cd.x, cd.z, cd.c.base, cd.c.ground));
            if (cols.size() >= maxCols) return new Fill(cols, level, volume - v, null, v, false, true, level);
            for (int[] d : N4) {
                int nx = cd.x + d[0];
                int nz = cd.z + d[1];
                if (!seen.add(MathUtil.columnKey(nx, nz))) continue;
                if (Math.abs(nx - sx) > maxRadius || Math.abs(nz - sz) > maxRadius) continue;
                Column nc = h.at(nx, nz);
                if (nc != null) heap.add(new Cand(nx, nz, nc, order++));
            }
        }
        if (v > 0 && !cols.isEmpty()) level += v / cols.size();
        return new Fill(cols, level, volume, null, 0, false, false, Double.POSITIVE_INFINITY);
    }

    /** Surface of `volume` spread over columns whose bases are sorted ascending. */
    public static double levelOverSorted(int[] sorted, double volume) {
        if (sorted.length == 0) return Double.NEGATIVE_INFINITY;
        double v = Math.max(0, volume);
        double level = sorted[0];
        for (int i = 0; i < sorted.length; i++) {
            double next = i + 1 < sorted.length ? sorted[i + 1] : Double.POSITIVE_INFINITY;
            double need = (next - level) * (i + 1);
            if (v <= need) return level + v / (i + 1);
            v -= need;
            level = next;
        }
        return level;
    }

    public static double levelOverColumns(int[] bases, double volume) {
        int[] s = bases.clone();
        Arrays.sort(s);
        return levelOverSorted(s, volume);
    }
}
