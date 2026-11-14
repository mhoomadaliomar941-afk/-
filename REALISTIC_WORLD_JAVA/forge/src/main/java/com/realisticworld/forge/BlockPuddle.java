package com.realisticworld.forge;

import net.minecraft.block.Block;
import net.minecraft.block.SoundType;
import net.minecraft.block.material.EnumPushReaction;
import net.minecraft.block.material.MapColor;
import net.minecraft.block.material.Material;
import net.minecraft.block.state.BlockFaceShape;
import net.minecraft.block.state.IBlockState;
import net.minecraft.item.Item;
import net.minecraft.util.BlockRenderLayer;
import net.minecraft.util.EnumFacing;
import net.minecraft.util.math.AxisAlignedBB;
import net.minecraft.util.math.BlockPos;
import net.minecraft.world.IBlockAccess;
import net.minecraftforge.fml.relauncher.Side;
import net.minecraftforge.fml.relauncher.SideOnly;

import javax.annotation.Nullable;
import java.util.Random;

/**
 * Thin see-through puddle (water shallower than half a block): no collision,
 * replaced by anything placed on it, drops nothing. Placed and removed only by
 * the water simulation.
 */
public class BlockPuddle extends Block {
    private static final AxisAlignedBB SHAPE = new AxisAlignedBB(0, 0, 0, 1, 1 / 16.0, 1);

    /**
     * Not Material.WATER: vanilla liquid code reads the "level" property of any
     * block made of water, and the puddle has none (that crashed the server
     * when flowing water reached a puddle). Flowing water washes puddles away.
     */
    static final Material PUDDLE = new Material(MapColor.WATER) {
        @Override public boolean isSolid() { return false; }
        @Override public boolean blocksLight() { return false; }
        @Override public boolean blocksMovement() { return false; }
        @Override public boolean isReplaceable() { return true; }
        @Override public EnumPushReaction getMobilityFlag() { return EnumPushReaction.DESTROY; }
    };

    public BlockPuddle() {
        super(PUDDLE);
        setHardness(0F);
        setLightOpacity(0);
        setSoundType(SoundType.SLIME);
    }

    @Override
    public AxisAlignedBB getBoundingBox(IBlockState state, IBlockAccess world, BlockPos pos) {
        return SHAPE;
    }

    @Nullable
    @Override
    public AxisAlignedBB getCollisionBoundingBox(IBlockState state, IBlockAccess world, BlockPos pos) {
        return NULL_AABB;
    }

    @Override
    public boolean isOpaqueCube(IBlockState state) {
        return false;
    }

    @Override
    public boolean isFullCube(IBlockState state) {
        return false;
    }

    @Override
    public boolean isReplaceable(IBlockAccess world, BlockPos pos) {
        return true;
    }

    @Override
    public BlockFaceShape getBlockFaceShape(IBlockAccess world, IBlockState state, BlockPos pos, EnumFacing face) {
        return BlockFaceShape.UNDEFINED;
    }

    @Override
    public Item getItemDropped(IBlockState state, Random rand, int fortune) {
        return net.minecraft.init.Items.AIR;
    }

    @Override
    public int quantityDropped(Random random) {
        return 0;
    }

    @SideOnly(Side.CLIENT)
    @Override
    public BlockRenderLayer getBlockLayer() {
        return BlockRenderLayer.TRANSLUCENT;
    }

    @SideOnly(Side.CLIENT)
    @Override
    public boolean shouldSideBeRendered(IBlockState state, IBlockAccess world, BlockPos pos, EnumFacing side) {
        return side == EnumFacing.UP || super.shouldSideBeRendered(state, world, pos, side);
    }
}
