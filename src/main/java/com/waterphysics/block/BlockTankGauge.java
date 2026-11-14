package com.waterphysics.block;

import com.waterphysics.WaterPhysics;
import com.waterphysics.net.GaugeReader;
import net.minecraft.block.Block;
import net.minecraft.block.SoundType;
import net.minecraft.block.material.Material;
import net.minecraft.block.properties.PropertyDirection;
import net.minecraft.block.state.BlockStateContainer;
import net.minecraft.block.state.IBlockState;
import net.minecraft.entity.EntityLivingBase;
import net.minecraft.entity.player.EntityPlayer;
import net.minecraft.util.EnumFacing;
import net.minecraft.util.EnumHand;
import net.minecraft.util.math.BlockPos;
import net.minecraft.world.World;

/**
 * Tank gauge: part of a tank wall. Right click shows volume, capacity, level and pressure of the tank
 * behind it; a comparator reads the fill level (0..15).
 */
public class BlockTankGauge extends Block implements IWaterBarrier {
    public static final PropertyDirection FACING = PropertyDirection.create("facing", EnumFacing.Plane.HORIZONTAL);

    public BlockTankGauge() {
        super(Material.IRON);
        setRegistryName(WaterPhysics.MODID, "tank_gauge");
        setUnlocalizedName(WaterPhysics.MODID + ".tank_gauge");
        setHardness(3f);
        setSoundType(SoundType.METAL);
        setDefaultState(blockState.getBaseState().withProperty(FACING, EnumFacing.NORTH));
    }

    @Override
    protected BlockStateContainer createBlockState() {
        return new BlockStateContainer(this, FACING);
    }

    @Override
    public int getMetaFromState(IBlockState state) {
        return state.getValue(FACING).getHorizontalIndex();
    }

    @Override
    public IBlockState getStateFromMeta(int meta) {
        return getDefaultState().withProperty(FACING, EnumFacing.getHorizontal(meta));
    }

    /** the dial faces the player who places it; the tank is behind */
    @Override
    public IBlockState getStateForPlacement(World w, BlockPos pos, EnumFacing side, float hx, float hy, float hz, int meta, EntityLivingBase placer) {
        return getDefaultState().withProperty(FACING, placer.getHorizontalFacing().getOpposite());
    }

    public static BlockPos tankCell(IBlockState state, BlockPos pos) {
        return pos.offset(state.getValue(FACING).getOpposite());
    }

    @Override
    public boolean onBlockActivated(World w, BlockPos pos, IBlockState state, EntityPlayer player, EnumHand hand, EnumFacing side, float hx, float hy, float hz) {
        if (!w.isRemote) GaugeReader.toChat(w, tankCell(state, pos), player, true);
        return true;
    }

    /** comparators only re-read their input when notified: refresh them every second */
    @Override
    public void onBlockAdded(World w, BlockPos pos, IBlockState state) {
        if (!w.isRemote) w.scheduleUpdate(pos, this, 20);
    }

    @Override
    public void updateTick(World w, BlockPos pos, IBlockState state, java.util.Random rand) {
        if (w.isRemote) return;
        w.updateComparatorOutputLevel(pos, this);
        w.scheduleUpdate(pos, this, 20);
    }

    @Override
    public boolean hasComparatorInputOverride(IBlockState state) {
        return true;
    }

    @Override
    public int getComparatorInputOverride(IBlockState state, World w, BlockPos pos) {
        return GaugeReader.comparator(w, tankCell(state, pos));
    }
}
