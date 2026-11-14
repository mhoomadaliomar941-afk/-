package com.waterphysics.tile;

import com.waterphysics.block.BlockPipe;
import net.minecraft.block.Block;
import net.minecraft.nbt.NBTTagCompound;
import net.minecraft.util.EnumFacing;

/** Pipe segment. Accumulates damage above its pressure rating, then cracks (leaks) and bursts. */
public class TilePipe extends TileFluidNode {
    /** 0 = intact, >= 0.5 cracked, >= 1 burst */
    public float damage;
    /** pressure low-pass filtered over ~5 ticks (damage is driven by sustained pressure) */
    public float smoothedKPa;

    public BlockPipe pipeBlock() {
        Block b = world != null ? world.getBlockState(pos).getBlock() : null;
        return b instanceof BlockPipe ? (BlockPipe) b : null;
    }

    @Override
    public float diameter() {
        BlockPipe b = pipeBlock();
        return b == null ? 0.25f : b.diameter;
    }

    @Override
    public float ratedPressure() {
        BlockPipe b = pipeBlock();
        return b == null ? 800f : b.ratedKPa;
    }

    public boolean isGlass() {
        BlockPipe b = pipeBlock();
        return b != null && b.glass;
    }

    @Override
    public boolean connectsTo(EnumFacing side) {
        return true;
    }

    public boolean cracked() {
        return damage >= 0.5f;
    }

    public boolean burst() {
        return damage >= 1f;
    }

    @Override
    protected void readExtra(NBTTagCompound t) {
        damage = t.getFloat("dmg");
    }

    @Override
    protected void writeExtra(NBTTagCompound t) {
        t.setFloat("dmg", damage);
    }
}
