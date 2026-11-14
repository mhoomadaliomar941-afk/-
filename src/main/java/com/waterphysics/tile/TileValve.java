package com.waterphysics.tile;

import com.waterphysics.block.BlockValve;
import net.minecraft.block.state.IBlockState;
import net.minecraft.nbt.NBTTagCompound;
import net.minecraft.util.EnumFacing;

/** Inline valve: opening 0..100 % (redstone signal closes it). */
public class TileValve extends TileFluidNode {
    public int openingPercent = 100;

    public EnumFacing.Axis axis() {
        IBlockState s = world.getBlockState(pos);
        return s.getBlock() instanceof BlockValve ? s.getValue(BlockValve.AXIS) : EnumFacing.Axis.X;
    }

    public float opening() {
        if (world != null && world.isBlockPowered(pos)) return 0f;
        return openingPercent / 100f;
    }

    @Override
    public float diameter() {
        return 0.25f;
    }

    @Override
    public boolean connectsTo(EnumFacing side) {
        return side.getAxis() == axis();
    }

    public void cycle() {
        openingPercent = openingPercent <= 0 ? 100 : openingPercent - 25;
        markDirty();
        syncIfChanged(true);
    }

    @Override
    protected void readExtra(NBTTagCompound t) {
        if (t.hasKey("open")) openingPercent = t.getInteger("open");
    }

    @Override
    protected void writeExtra(NBTTagCompound t) {
        t.setInteger("open", openingPercent);
    }
}
