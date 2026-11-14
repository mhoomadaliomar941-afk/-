package com.waterphysics.block;

import com.waterphysics.WaterPhysics;
import com.waterphysics.pipe.PipeManager;
import com.waterphysics.tile.TilePump;
import net.minecraft.block.Block;
import net.minecraft.block.ITileEntityProvider;
import net.minecraft.block.SoundType;
import net.minecraft.block.material.Material;
import net.minecraft.block.properties.PropertyDirection;
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

/** Pump: intake on the back, outlet on the front (FACING). Right click cycles ON / REDSTONE / OFF. */
public class BlockPump extends Block implements ITileEntityProvider, IWaterBarrier {
    public static final PropertyDirection FACING = PropertyDirection.create("facing");

    public BlockPump() {
        super(Material.IRON);
        setRegistryName(WaterPhysics.MODID, "pump");
        setUnlocalizedName(WaterPhysics.MODID + ".pump");
        setHardness(3.5f);
        setSoundType(SoundType.METAL);
        setDefaultState(blockState.getBaseState().withProperty(FACING, EnumFacing.UP));
    }

    @Override
    protected BlockStateContainer createBlockState() {
        return new BlockStateContainer(this, FACING);
    }

    @Override
    public int getMetaFromState(IBlockState state) {
        return state.getValue(FACING).getIndex();
    }

    @Override
    public IBlockState getStateFromMeta(int meta) {
        return getDefaultState().withProperty(FACING, EnumFacing.getFront(meta));
    }

    @Override
    public IBlockState getStateForPlacement(World w, BlockPos pos, EnumFacing side, float hx, float hy, float hz, int meta, EntityLivingBase placer) {
        return getDefaultState().withProperty(FACING, side);
    }

    @Override
    public TileEntity createNewTileEntity(World worldIn, int meta) {
        return new TilePump();
    }

    @Override
    public boolean onBlockActivated(World w, BlockPos pos, IBlockState state, EntityPlayer player, EnumHand hand, EnumFacing side, float hx, float hy, float hz) {
        if (!player.getHeldItem(hand).isEmpty() && !(player.getHeldItem(hand).getItem() instanceof com.waterphysics.item.ItemPipeWrench)) return false;
        if (!w.isRemote) {
            TileEntity te = w.getTileEntity(pos);
            if (te instanceof TilePump) {
                if (player.isSneaking()) {
                    EnumFacing f = state.getValue(FACING);
                    w.setBlockState(pos, state.withProperty(FACING, EnumFacing.getFront((f.getIndex() + 1) % 6)), 3);
                    PipeManager.blockChangedNear(w, pos);
                } else {
                    ((TilePump) te).cycleMode();
                    player.sendStatusMessage(new TextComponentTranslation("waterphysics.msg.pump_mode." + ((TilePump) te).mode), true);
                }
            }
        }
        return true;
    }

    @Override
    public void neighborChanged(IBlockState state, World w, BlockPos pos, Block b, BlockPos from) {
        PipeManager.blockChangedNear(w, pos);
    }
}
