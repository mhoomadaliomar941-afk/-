package com.waterphysics.sim;

import com.waterphysics.block.BlockPhysWater;
import com.waterphysics.block.IWaterBarrier;
import net.minecraft.block.*;
import net.minecraft.block.material.Material;
import net.minecraft.block.state.IBlockState;
import net.minecraft.init.Blocks;

/** Classification of world blocks for the water grid. */
public final class CellType {
    private CellType() {}

    /** air, physical water, or a block water can wash away (plants, torches...) */
    public static final byte OPEN = 0;
    /** blocks water */
    public static final byte SOLID = 1;
    /** vanilla water: a reservoir boundary at its own level */
    public static final byte RESERVOIR = 2;
    /** chunk not loaded or outside the world: treated as a wall */
    public static final byte UNLOADED = 3;
    /** lava: blocks flow (vanilla handles water/lava mixing) */
    public static final byte LAVA = 4;

    public static byte of(IBlockState state) {
        Block b = state.getBlock();
        if (b instanceof BlockPhysWater) return OPEN;
        Material m = state.getMaterial();
        if (m == Material.AIR) return OPEN;
        if (b instanceof IWaterBarrier) return SOLID;
        if (m == Material.WATER) {
            return (b == Blocks.WATER || b == Blocks.FLOWING_WATER) ? RESERVOIR : SOLID;
        }
        if (m == Material.LAVA) return LAVA;
        if (b instanceof BlockDoor || b == Blocks.STANDING_SIGN || b == Blocks.WALL_SIGN || b == Blocks.LADDER
                || b == Blocks.REEDS || m == Material.PORTAL || m == Material.STRUCTURE_VOID) {
            return SOLID;
        }
        if (m.isReplaceable() || !m.blocksMovement()) return OPEN;
        return SOLID;
    }

    /** true when water entering this (OPEN) cell must first destroy the block (dropping it like vanilla water). */
    public static boolean needsWashAway(IBlockState state) {
        Block b = state.getBlock();
        return !(b instanceof BlockPhysWater) && state.getMaterial() != Material.AIR;
    }
}
