package com.waterphysics.block;

import com.waterphysics.WaterPhysics;
import com.waterphysics.tile.TileSpigot;
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

/** Adjustable orifice for tank walls: right click cycles the opening area (0 to 2000 cm^2). */
public class BlockSpigot extends Block implements ITileEntityProvider, IWaterBarrier {
    public static final PropertyDirection FACING = PropertyDirection.create("facing");

    public BlockSpigot() {
        super(Material.IRON);
        setRegistryName(WaterPhysics.MODID, "spigot");
        setUnlocalizedName(WaterPhysics.MODID + ".spigot");
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
        return state.getValue(FACING).getIndex();
    }

    @Override
    public IBlockState getStateFromMeta(int meta) {
        return getDefaultState().withProperty(FACING, EnumFacing.getFront(meta));
    }

    /** the outlet faces away from the player who places it (towards where they look from inside the tank) */
    @Override
    public IBlockState getStateForPlacement(World w, BlockPos pos, EnumFacing side, float hx, float hy, float hz, int meta, EntityLivingBase placer) {
        return getDefaultState().withProperty(FACING, side);
    }

    @Override
    public TileEntity createNewTileEntity(World worldIn, int meta) {
        return new TileSpigot();
    }

    @Override
    public boolean onBlockActivated(World w, BlockPos pos, IBlockState state, EntityPlayer player, EnumHand hand, EnumFacing side, float hx, float hy, float hz) {
        if (!player.getHeldItem(hand).isEmpty() && !(player.getHeldItem(hand).getItem() instanceof com.waterphysics.item.ItemPipeWrench)) return false;
        if (!w.isRemote) {
            TileEntity te = w.getTileEntity(pos);
            if (te instanceof TileSpigot) {
                TileSpigot s = (TileSpigot) te;
                s.cycle();
                player.sendStatusMessage(new TextComponentTranslation("waterphysics.msg.spigot", TileSpigot.APERTURES_CM2[s.aperture]), true);
            }
        }
        return true;
    }

    @Override
    public void neighborChanged(IBlockState state, World w, BlockPos pos, Block b, BlockPos from) {
        // redstone is read every tick by the tile entity
    }
}
