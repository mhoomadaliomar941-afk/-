package com.waterphysics.block;

import com.waterphysics.WaterPhysics;
import com.waterphysics.pipe.PipeManager;
import com.waterphysics.tile.TileValve;
import net.minecraft.block.Block;
import net.minecraft.block.ITileEntityProvider;
import net.minecraft.block.SoundType;
import net.minecraft.block.material.Material;
import net.minecraft.block.properties.PropertyEnum;
import net.minecraft.block.state.BlockStateContainer;
import net.minecraft.block.state.IBlockState;
import net.minecraft.entity.EntityLivingBase;
import net.minecraft.entity.player.EntityPlayer;
import net.minecraft.tileentity.TileEntity;
import net.minecraft.util.EnumFacing;
import net.minecraft.util.EnumHand;
import net.minecraft.util.math.BlockPos;
import net.minecraft.util.text.TextComponentTranslation;
import net.minecraft.world.World;

/** Inline valve along an axis. Right click: 100 / 75 / 50 / 25 / 0 % open. Redstone closes it. */
public class BlockValve extends Block implements ITileEntityProvider, IWaterBarrier {
    public static final PropertyEnum<EnumFacing.Axis> AXIS = PropertyEnum.create("axis", EnumFacing.Axis.class);

    public BlockValve() {
        super(Material.IRON);
        setRegistryName(WaterPhysics.MODID, "valve");
        setUnlocalizedName(WaterPhysics.MODID + ".valve");
        setHardness(3f);
        setSoundType(SoundType.METAL);
        setDefaultState(blockState.getBaseState().withProperty(AXIS, EnumFacing.Axis.X));
    }

    @Override
    protected BlockStateContainer createBlockState() {
        return new BlockStateContainer(this, AXIS);
    }

    @Override
    public int getMetaFromState(IBlockState state) {
        return state.getValue(AXIS).ordinal();
    }

    @Override
    public IBlockState getStateFromMeta(int meta) {
        return getDefaultState().withProperty(AXIS, EnumFacing.Axis.values()[meta % 3]);
    }

    @Override
    public IBlockState getStateForPlacement(World w, BlockPos pos, EnumFacing side, float hx, float hy, float hz, int meta, EntityLivingBase placer) {
        return getDefaultState().withProperty(AXIS, side.getAxis());
    }

    @Override
    public TileEntity createNewTileEntity(World worldIn, int meta) {
        return new TileValve();
    }

    @Override
    public boolean onBlockActivated(World w, BlockPos pos, IBlockState state, EntityPlayer player, EnumHand hand, EnumFacing side, float hx, float hy, float hz) {
        if (!player.getHeldItem(hand).isEmpty() && !(player.getHeldItem(hand).getItem() instanceof com.waterphysics.item.ItemPipeWrench)) return false;
        if (!w.isRemote) {
            TileEntity te = w.getTileEntity(pos);
            if (te instanceof TileValve) {
                ((TileValve) te).cycle();
                player.sendStatusMessage(new TextComponentTranslation("waterphysics.msg.valve", ((TileValve) te).openingPercent), true);
            }
        }
        return true;
    }

    @Override
    public void neighborChanged(IBlockState state, World w, BlockPos pos, Block b, BlockPos from) {
        PipeManager.blockChangedNear(w, pos);
    }

    @Override
    public boolean isOpaqueCube(IBlockState state) {
        return false;
    }

    @Override
    public boolean isFullCube(IBlockState state) {
        return false;
    }
}
