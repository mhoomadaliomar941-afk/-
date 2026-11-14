package com.realisticworld.water;

import com.realisticworld.util.MathUtil;

import java.util.ArrayDeque;
import java.util.ArrayList;
import java.util.Deque;
import java.util.HashMap;
import java.util.HashSet;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;
import java.util.Set;

/**
 * Finds the shape of a body of still water by flood-filling its source
 * blocks, a few at a time, and records every opening in its walls and floor
 * (holes, open valves, pipes, drains) plus the closed space inside the walls
 * above the water. Bodies larger than the cap are natural water and refused.
 */
public final class TankScan {
    public static final int MAX_TANK_CELLS = 4096;

    public enum Cell { SOURCE, FLOW, AIR, SOLID, VALVE_OPEN, VALVE_CLOSED, PIPE, DRAIN, UNLOADED }

    public enum Status { RUNNING, DONE, TOO_BIG, UNLOADED, NOT_WATER }

    public interface CellFn {
        Cell at(int x, int y, int z);
    }

    /** An opening. bottom: side = y of the opening block; floor = top of the floor. */
    public static final class Opening {
        public final String kind; // hole, floor, valve, drain
        public final int x;
        public final int y;
        public final int z;
        public final int bottom;
        public final int dx;
        public final int dz;
        public boolean open = true;

        public Opening(String kind, int x, int y, int z, int bottom, int dx, int dz) {
            this.kind = kind;
            this.x = x;
            this.y = y;
            this.z = z;
            this.bottom = bottom;
            this.dx = dx;
            this.dz = dz;
        }

        public boolean side() {
            return y == bottom;
        }
    }

    public static final class PipeStart {
        public final int x;
        public final int y;
        public final int z;
        public final int fromY;

        PipeStart(int x, int y, int z, int fromY) {
            this.x = x;
            this.y = y;
            this.z = z;
            this.fromY = fromY;
        }
    }

    /** Result: layers include the closed space above the water. */
    public static final class Shape {
        public int cells;
        public int waterTop;
        public int baseY;
        public int[] layers;
        public List<int[]> columns; // [x, z, minY, maxY]
        public int openTop;
        public List<Opening> openings;
        public List<int[]> valves;
        public List<PipeStart> pipes;
    }

    private static final int[][] SIDES = {{1, 0}, {-1, 0}, {0, 1}, {0, -1}};

    private final CellFn cellAt;
    private final int maxCells;
    private Status status = Status.RUNNING;
    private final Deque<int[]> stack = new ArrayDeque<>();
    private final Set<Long> seen = new HashSet<>();
    private final List<int[]> cells = new ArrayList<>();
    private final Map<Long, Opening> openings = new LinkedHashMap<>();
    private final Map<Long, int[]> valves = new LinkedHashMap<>();
    private final Map<Long, PipeStart> pipes = new LinkedHashMap<>();
    private int openTop;
    public long blockReads;

    public TankScan(CellFn cellAt, int sx, int sy, int sz) {
        this(cellAt, sx, sy, sz, MAX_TANK_CELLS);
    }

    public TankScan(CellFn cellAt, int sx, int sy, int sz, int maxCells) {
        this.cellAt = cellAt;
        this.maxCells = maxCells;
        stack.push(new int[] {sx, sy, sz});
        seen.add(WaterPhysics.pack(sx, sy, sz));
        if (cellAt.at(sx, sy, sz) != Cell.SOURCE) status = Status.NOT_WATER;
    }

    public Status status() {
        return status;
    }

    public boolean contains(int x, int y, int z) {
        return seen.contains(WaterPhysics.pack(x, y, z));
    }

    public List<int[]> cells() {
        return cells;
    }

    private Cell read(int x, int y, int z) {
        blockReads++;
        return cellAt.at(x, y, z);
    }

    private void visit(int x, int y, int z) {
        if (seen.add(WaterPhysics.pack(x, y, z))) stack.push(new int[] {x, y, z});
    }

    private void neighbour(int x, int y, int z, Cell kind, int fromY, boolean side, int dx, int dz) {
        long k = WaterPhysics.pack(x, y, z);
        switch (kind) {
            case SOURCE:
                visit(x, y, z);
                break;
            case AIR:
            case FLOW:
                if (!openings.containsKey(k)) openings.put(k, side ? new Opening("hole", x, y, z, y, dx, dz) : new Opening("floor", x, y, z, fromY, 0, 0));
                break;
            case VALVE_OPEN:
                valves.put(k, new int[] {x, y, z});
                if (!openings.containsKey(k)) openings.put(k, side ? new Opening("valve", x, y, z, y, dx, dz) : new Opening("valve", x, y, z, fromY, 0, 0));
                break;
            case VALVE_CLOSED:
                valves.put(k, new int[] {x, y, z});
                break;
            case DRAIN:
                if (!openings.containsKey(k)) openings.put(k, new Opening("drain", x, y, z, side ? y : fromY, 0, 0));
                break;
            case PIPE:
                if (!pipes.containsKey(k)) pipes.put(k, new PipeStart(x, y, z, fromY));
                break;
            case UNLOADED:
                status = Status.UNLOADED;
                break;
            default:
                break; // wall or floor
        }
    }

    /** Process up to maxSteps cells; true when finished (see status()). */
    public boolean step(int maxSteps) {
        int n = 0;
        while (status == Status.RUNNING && !stack.isEmpty() && n < maxSteps) {
            int[] c = stack.pop();
            n++;
            cells.add(c);
            if (cells.size() > maxCells) {
                status = Status.TOO_BIG;
                break;
            }
            for (int[] d : SIDES) neighbour(c[0] + d[0], c[1], c[2] + d[1], read(c[0] + d[0], c[1], c[2] + d[1]), c[1], true, d[0], d[1]);
            neighbour(c[0], c[1] - 1, c[2], read(c[0], c[1] - 1, c[2]), c[1], false, 0, 0);
            Cell up = read(c[0], c[1] + 1, c[2]);
            if (up == Cell.SOURCE) visit(c[0], c[1] + 1, c[2]);
            else if (up == Cell.AIR) openTop++;
            else if (up == Cell.UNLOADED) status = Status.UNLOADED;
        }
        if (status == Status.RUNNING && stack.isEmpty()) status = Status.DONE;
        return status != Status.RUNNING;
    }

    /** Closed space inside the walls above the water, layer by layer up to the rim. */
    public List<int[]> headroom(int maxLayers, int maxColumns) {
        Map<Long, Integer> tops = new HashMap<>();
        for (int[] c : cells) tops.merge(MathUtil.columnKey(c[0], c[2]), c[1], Math::max);
        List<int[]> extra = new ArrayList<>();
        if (tops.size() > maxColumns || cells.isEmpty()) return extra;
        int surfaceY = Integer.MIN_VALUE;
        for (int y : tops.values()) surfaceY = Math.max(surfaceY, y);
        List<int[]> layer = new ArrayList<>();
        for (int[] c : cells) if (c[1] == surfaceY) layer.add(new int[] {c[0], c[2]});
        for (int y = surfaceY + 1; y <= surfaceY + maxLayers && !layer.isEmpty(); y++) {
            Set<Long> set = new HashSet<>();
            for (int[] p : layer) set.add(MathUtil.columnKey(p[0], p[1]));
            boolean closed = true;
            outer:
            for (int[] p : layer) {
                if (read(p[0], y, p[1]) != Cell.AIR) {
                    closed = false;
                    break;
                }
                for (int[] d : SIDES) {
                    if (set.contains(MathUtil.columnKey(p[0] + d[0], p[1] + d[1]))) continue;
                    if (read(p[0] + d[0], y, p[1] + d[1]) != Cell.SOLID) {
                        closed = false;
                        break outer;
                    }
                }
            }
            if (!closed) break;
            for (int[] p : layer) extra.add(new int[] {p[0], y, p[1]});
        }
        return extra;
    }

    public Shape result() {
        List<int[]> space = new ArrayList<>(cells);
        space.addAll(headroom(8, 1024));
        int waterTop = Integer.MIN_VALUE;
        for (int[] c : cells) waterTop = Math.max(waterTop, c[1]);
        int minY = Integer.MAX_VALUE;
        int maxY = Integer.MIN_VALUE;
        Map<Long, int[]> cols = new LinkedHashMap<>();
        for (int[] c : space) {
            minY = Math.min(minY, c[1]);
            maxY = Math.max(maxY, c[1]);
            long k = MathUtil.columnKey(c[0], c[2]);
            int[] col = cols.get(k);
            if (col == null) cols.put(k, new int[] {c[0], c[2], c[1], c[1]});
            else {
                col[2] = Math.min(col[2], c[1]);
                col[3] = Math.max(col[3], c[1]);
            }
        }
        int[] layers = new int[Math.max(0, maxY - minY + 1)];
        for (int[] c : space) layers[c[1] - minY]++;
        Shape s = new Shape();
        s.cells = cells.size();
        s.waterTop = waterTop;
        s.baseY = minY;
        s.layers = layers;
        s.columns = new ArrayList<>(cols.values());
        s.openTop = openTop;
        s.openings = new ArrayList<>(openings.values());
        s.valves = new ArrayList<>(valves.values());
        s.pipes = new ArrayList<>(pipes.values());
        return s;
    }

    /** End of a pipe chain: the last pipe block and what is beyond it. */
    public static final class PipeEnd {
        public final int[] end;
        public final String outKind; // water, air, drain or null
        public final int[] out;
        public final int length;

        PipeEnd(int[] end, String outKind, int[] out, int length) {
            this.end = end;
            this.outKind = outKind;
            this.out = out;
            this.length = length;
        }
    }

    public interface OwnWater {
        boolean is(int x, int y, int z);
    }

    /** Follow a chain of pipe blocks from `start` to its far end (null if too long or a loop). */
    public static PipeEnd tracePipe(CellFn cellAt, int[] start, OwnWater own, int maxLength) {
        int[][] dirs = {{1, 0, 0}, {-1, 0, 0}, {0, 0, 1}, {0, 0, -1}, {0, -1, 0}, {0, 1, 0}};
        Set<Long> seenP = new HashSet<>();
        seenP.add(WaterPhysics.pack(start[0], start[1], start[2]));
        int[] cur = start;
        for (int length = 1; length <= maxLength; length++) {
            int[] next = null;
            String outKind = null;
            int[] out = null;
            for (int[] d : dirs) {
                int x = cur[0] + d[0];
                int y = cur[1] + d[1];
                int z = cur[2] + d[2];
                if (seenP.contains(WaterPhysics.pack(x, y, z))) continue;
                Cell k = cellAt.at(x, y, z);
                if (k == Cell.PIPE) {
                    if (next == null) next = new int[] {x, y, z};
                } else if (k == Cell.SOURCE && !own.is(x, y, z)) {
                    if (outKind == null || "air".equals(outKind)) {
                        outKind = "water";
                        out = new int[] {x, y, z};
                    }
                } else if (k == Cell.DRAIN) {
                    if (outKind == null || "air".equals(outKind)) {
                        outKind = "drain";
                        out = new int[] {x, y, z};
                    }
                } else if ((k == Cell.AIR || k == Cell.FLOW) && outKind == null) {
                    outKind = "air";
                    out = new int[] {x, y, z};
                }
            }
            if (next != null) {
                seenP.add(WaterPhysics.pack(next[0], next[1], next[2]));
                cur = next;
                continue;
            }
            return new PipeEnd(cur, outKind, out, length);
        }
        return null;
    }
}
