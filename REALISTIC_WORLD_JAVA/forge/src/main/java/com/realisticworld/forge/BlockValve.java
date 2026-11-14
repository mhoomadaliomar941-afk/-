package com.realisticworld.forge;

import net.minecraft.block.Block;
import net.minecraft.block.SoundType;
import net.minecraft.block.material.Material;
import net.minecraft.block.properties.PropertyBool;
import net.minecraft.block.state.BlockStateContainer;
import net.minecraft.block.state.IBlockState;
import net.minecraft.creativetab.CreativeTabs;
import net.minecraft.entity.player.EntityPlayer;
import net.minecraft.entity.player.EntityPlayerMP;
import net.minecraft.util.EnumFacing;
import net.minecraft.util.EnumHand;
import net.minecraft.util.math.BlockPos;
import net.minecraft.util.text.TextComponentString;
import net.minecraft.world.World;

/** Water valve: put it in a tank wall; right-click with an empty hand (or anything but the gauge) to open/close. */
public class BlockValve extends Block {
    public static final PropertyBool OPEN = PropertyBool.create("open");

    public BlockValve() {
        super(Material.IRON);
        setHardness(1.5F);
        setResistance(6F);
        setSoundType(SoundType.METAL);
        setCreativeTab(CreativeTabs.REDSTONE);
        setDefaultState(blockState.getBaseState().withProperty(OPEN, false));
    }

    @Override
    protected BlockStateContainer createBlockState() {
        return new BlockStateContainer(this, OPEN);
    }

    @Override
    public IBlockState getStateFromMeta(int meta) {
        return getDefaultState().withProperty(OPEN, (meta & 1) == 1);
    }

    @Override
    public int getMetaFromState(IBlockState state) {
        return state.getValue(OPEN) ? 1 : 0;
    }

    @Override
    public int damageDropped(IBlockState state) {
        return 0;
    }

    @Override
    public boolean onBlockActivated(World world, BlockPos pos, IBlockState state, EntityPlayer player, EnumHand hand, EnumFacing facing, float hx, float hy, float hz) {
        if (player.getHeldItem(hand).getItem() == RwBlocks.WATER_GAUGE) return false; // let the gauge measure
        if (hand != EnumHand.MAIN_HAND) return true;
        if (world.isRemote) return true;
        boolean open = !state.getValue(OPEN);
        world.setBlockState(pos, state.withProperty(OPEN, open), 3);
        player.sendStatusMessage(new TextComponentString(open ? "Valve opened" : "Valve closed"), true);
        RealisticWorldMod.onValve(world.provider.getDimension(), pos);
        if (player instanceof EntityPlayerMP) world.playEvent(null, open ? 1011 : 1005, pos, 0); // iron trapdoor/door sounds
        return true;
    }
}
