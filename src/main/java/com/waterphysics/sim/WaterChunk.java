package com.waterphysics.sim;

import net.minecraft.world.chunk.Chunk;

/** Physical water data of one chunk column (16 sections). */
public final class WaterChunk {
    public final int cx, cz;
    public final Sec[] secs = new Sec[16];
    /** data was loaded from the chunk NBT */
    public boolean fromSave;
    /** the Minecraft chunk while loaded */
    public Chunk chunk;

    public WaterChunk(int cx, int cz) {
        this.cx = cx;
        this.cz = cz;
    }

    public Sec getOrCreate(int sy) {
        Sec s = secs[sy];
        if (s == null) {
            s = new Sec(this, sy);
            secs[sy] = s;
        }
        return s;
    }

    public boolean isEmpty() {
        for (Sec s : secs) if (s != null && s.waterCells > 0) return false;
        return true;
    }
}
