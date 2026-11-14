package com.waterphysics.block;

import com.waterphysics.WaterPhysics;
import net.minecraft.block.Block;
import net.minecraft.block.SoundType;
import net.minecraft.block.material.Material;
import net.minecraft.block.state.IBlockState;
import net.minecraft.util.BlockRenderLayer;
import net.minecraft.util.EnumFacing;
import net.minecraft.util.math.BlockPos;
import net.minecraft.world.IBlockAccess;
import net.minecraftforge.fml.relauncher.Side;
import net.minecraftforge.fml.relauncher.SideOnly;

/** Reinforced tank wall (steel) and tank glass. Water-tight like any solid block; glass shows the water. */
public class BlockTankWall extends Block implements IWaterBarrier {
    private final boolean glass;

    public BlockTankWall(String name, boolean glass) {
        super(glass ? Material.GLASS : Material.IRON);
        this.glass = glass;
        setRegistryName(WaterPhysics.MODID, name);
        setUnlocalizedName(WaterPhysics.MODID + "." + name);
        setHardness(glass ? 1.5f : 4.0f);
        setResistance(glass ? 30f : 60f);
        setSoundType(glass ? SoundType.GLASS : SoundType.METAL);
        setHarvestLevel("pickaxe", glass ? 0 : 1);
        // Block's constructor computed opacity before 'glass' was set: glass must let light through
        this.fullBlock = !glass;
        this.translucent = glass;
        setLightOpacity(glass ? 0 : 255);
    }

    @Override
    public boolean isOpaqueCube(IBlockState state) {
        return !glass;
    }

    @Override
    public boolean isFullCube(IBlockState state) {
        return !glass;
    }

    @SideOnly(Side.CLIENT)
    @Override
    public BlockRenderLayer getBlockLayer() {
        return glass ? BlockRenderLayer.CUTOUT : BlockRenderLayer.SOLID;
    }

    @SideOnly(Side.CLIENT)
    @Override
    public boolean shouldSideBeRendered(IBlockState state, IBlockAccess world, BlockPos pos, EnumFacing side) {
        if (glass && world.getBlockState(pos.offset(side)).getBlock() == this) return false;
        return super.shouldSideBeRendered(state, world, pos, side);
    }
}
