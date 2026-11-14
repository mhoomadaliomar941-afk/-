package com.waterphysics.sim;

/**
 * Water state of one 16x16x16 section. Volumes are in cubic meters (1.0 = one full block = 1000 L).
 * Simulation arrays exist only while the section is active, so settled water costs no CPU and little memory.
 */
public final class Sec {
    public static final int SIZE = 4096;

    public final WaterChunk owner;
    public final int sy;
    public final long key;

    /** volume per cell (m^3); never null once created */
    public final float[] vol = new float[SIZE];
    /** number of cells with water */
    public int waterCells;

    // ---- simulation state (only while active) ----
    public boolean active;
    public boolean simulatedThisTick;
    public int quiet;
    /** outflow flux per cell and direction (m^3/s), index = cell * 6 + dir */
    public float[] out;
    /** hydraulic head (absolute height in meters) */
    public float[] head;
    /** base head (without overfill / impact terms) */
    public float[] baseHead;
    /** water depth above the cell (m) used for hydrostatic pressure */
    public float[] depth;
    /** relaxed pressure head for the top of enclosed water columns */
    public float[] confined;
    /** cached cell types (see {@link CellType}) */
    public byte[] type;
    /** per-cell effect flags for the client (see {@link WaterSim} FX_*) */
    public byte[] fx;
    /** volumes at the start of the tick */
    public float[] prev;
    public int typeAge;
    /** the section contains or borders vanilla water (reservoir exchange needed) */
    public boolean nearReservoir;

    /** visual (block state) update pending */
    public boolean visualDirty;
    /** sync revision (incremented whenever the section changed) */
    public int rev;
    /** world tick when the section last went to sleep (for a final sync) */
    public long sleptAt = -1;

    public Sec(WaterChunk owner, int sy) {
        this.owner = owner;
        this.sy = sy;
        this.key = WaterSim.secKey(owner.cx, sy, owner.cz);
    }

    public static int idx(int x, int y, int z) {
        return ((y & 15) << 8) | ((z & 15) << 4) | (x & 15);
    }

    public void ensureSimArrays() {
        if (out == null) {
            out = new float[SIZE * 6];
            head = new float[SIZE];
            baseHead = new float[SIZE];
            depth = new float[SIZE];
            confined = new float[SIZE];
            type = new byte[SIZE];
            fx = new byte[SIZE];
            prev = new float[SIZE];
            typeAge = 0;
        }
    }

    public void releaseSimArrays() {
        out = null;
        head = null;
        baseHead = null;
        depth = null;
        confined = null;
        type = null;
        fx = null;
        prev = null;
    }

    public int worldX(int idx) { return (owner.cx << 4) | (idx & 15); }
    public int worldY(int idx) { return (sy << 4) | (idx >> 8); }
    public int worldZ(int idx) { return (owner.cz << 4) | ((idx >> 4) & 15); }

    public void recount() {
        int n = 0;
        for (float v : vol) if (v > 0f) n++;
        waterCells = n;
    }

    public double total() {
        double t = 0;
        for (float v : vol) t += v;
        return t;
    }
}
