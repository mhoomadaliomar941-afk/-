package com.waterphysics.tile;

import com.waterphysics.block.BlockPump;
import net.minecraft.block.state.IBlockState;
import net.minecraft.nbt.NBTTagCompound;
import net.minecraft.util.EnumFacing;

/**
 * Centrifugal pump: takes water on the back side and pushes it out of the front (FACING) side, adding up to
 * {@code pumpMaxHead} meters of head along the curve H(Q) = Hmax * (1 - (Q/Qmax)^2).
 */
public class TilePump extends TileFluidNode {
    public static final int MODE_ON = 0, MODE_REDSTONE = 1, MODE_OFF = 2;
    public int mode = MODE_ON;
    public float lastFlow;
    public float lastBoost;

    public EnumFacing facing() {
        IBlockState s = world.getBlockState(pos);
        return s.getBlock() instanceof BlockPump ? s.getValue(BlockPump.FACING) : EnumFacing.NORTH;
    }

    public boolean running() {
        switch (mode) {
            case MODE_ON: return true;
            case MODE_REDSTONE: return world.isBlockPowered(pos);
            default: return false;
        }
    }

    @Override
    public float diameter() {
        return 0.25f;
    }

    @Override
    public boolean connectsTo(EnumFacing side) {
        EnumFacing f = facing();
        return side == f || side == f.getOpposite();
    }

    public void cycleMode() {
        mode = (mode + 1) % 3;
        markDirty();
        syncIfChanged(true);
    }

    @Override
    protected void readExtra(NBTTagCompound t) {
        mode = t.getInteger("mode");
        lastFlow = t.getFloat("flow");
    }

    @Override
    protected void writeExtra(NBTTagCompound t) {
        t.setInteger("mode", mode);
        t.setFloat("flow", lastFlow);
    }
}
