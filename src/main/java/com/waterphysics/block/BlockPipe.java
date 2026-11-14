package com.waterphysics.block;

import com.waterphysics.WaterPhysics;
import com.waterphysics.pipe.PipeManager;
import com.waterphysics.tile.TileFluidNode;
import com.waterphysics.tile.TilePipe;
import net.minecraft.block.Block;
import net.minecraft.block.ITileEntityProvider;
import net.minecraft.block.SoundType;
import net.minecraft.block.material.Material;
import net.minecraft.block.properties.PropertyBool;
import net.minecraft.block.state.BlockFaceShape;
import net.minecraft.block.state.BlockStateContainer;
import net.minecraft.block.state.IBlockState;
import net.minecraft.client.util.ITooltipFlag;
import net.minecraft.item.ItemStack;
import net.minecraft.tileentity.TileEntity;
import net.minecraft.util.BlockRenderLayer;
import net.minecraft.util.EnumFacing;
import net.minecraft.util.math.AxisAlignedBB;
import net.minecraft.util.math.BlockPos;
import net.minecraft.util.text.translation.I18n;
import net.minecraft.world.IBlockAccess;
import net.minecraft.world.World;
import net.minecraftforge.fml.relauncher.Side;
import net.minecraftforge.fml.relauncher.SideOnly;

import javax.annotation.Nullable;
import java.util.List;

/** Pipe of a given inner diameter and pressure rating. Connects to neighbouring pipes, valves and pumps. */
public class BlockPipe extends Block implements ITileEntityProvider, IWaterBarrier {
    public static final PropertyBool[] CONN = {
            PropertyBool.create("down"), PropertyBool.create("up"), PropertyBool.create("north"),
            PropertyBool.create("south"), PropertyBool.create("west"), PropertyBool.create("east")};

    public final float diameter;
    public final float ratedKPa;
    public final boolean glass;

    public BlockPipe(String name, float diameter, float ratedKPa, boolean glass) {
        super(glass ? Material.GLASS : Material.IRON);
        this.diameter = diameter;
        this.ratedKPa = ratedKPa;
        this.glass = glass;
        setRegistryName(WaterPhysics.MODID, name);
        setUnlocalizedName(WaterPhysics.MODID + "." + name);
        setHardness(glass ? 0.8f : 2.5f);
        setSoundType(glass ? SoundType.GLASS : SoundType.METAL);
        IBlockState s = blockState.getBaseState();
        for (PropertyBool p : CONN) s = s.withProperty(p, false);
        setDefaultState(s);
    }

    @Override
    protected BlockStateContainer createBlockState() {
        return new BlockStateContainer(this, CONN);
    }

    @Override
    public int getMetaFromState(IBlockState state) {
        return 0;
    }

    @Override
    public IBlockState getActualState(IBlockState state, IBlockAccess w, BlockPos pos) {
        for (EnumFacing f : EnumFacing.VALUES) {
            TileEntity te = w.getTileEntity(pos.offset(f));
            boolean c = te instanceof TileFluidNode && connectsBack((TileFluidNode) te, f.getOpposite(), w);
            state = state.withProperty(CONN[f.getIndex()], c);
        }
        return state;
    }

    private static boolean connectsBack(TileFluidNode te, EnumFacing side, IBlockAccess w) {
        try {
            return te.getWorld() != null ? te.connectsTo(side) : true;
        } catch (Exception e) {
            return true;
        }
    }

    @Override
    public TileEntity createNewTileEntity(World worldIn, int meta) {
        return new TilePipe();
    }

    @Override
    public void neighborChanged(IBlockState state, World w, BlockPos pos, Block b, BlockPos from) {
        PipeManager.blockChangedNear(w, pos);
    }

    @Override
    public void onBlockAdded(World w, BlockPos pos, IBlockState state) {
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

    @Override
    public BlockFaceShape getBlockFaceShape(IBlockAccess w, IBlockState state, BlockPos pos, EnumFacing face) {
        return BlockFaceShape.UNDEFINED;
    }

    @SideOnly(Side.CLIENT)
    @Override
    public BlockRenderLayer getBlockLayer() {
        return glass ? BlockRenderLayer.TRANSLUCENT : BlockRenderLayer.CUTOUT;
    }

    private float half() {
        return Math.max(0.125f, Math.min(0.375f, diameter * 0.75f));
    }

    @Override
    public AxisAlignedBB getBoundingBox(IBlockState state, IBlockAccess w, BlockPos pos) {
        IBlockState a = getActualState(state, w, pos);
        float h = half();
        double x0 = 0.5 - h, y0 = 0.5 - h, z0 = 0.5 - h, x1 = 0.5 + h, y1 = 0.5 + h, z1 = 0.5 + h;
        if (a.getValue(CONN[0])) y0 = 0;
        if (a.getValue(CONN[1])) y1 = 1;
        if (a.getValue(CONN[2])) z0 = 0;
        if (a.getValue(CONN[3])) z1 = 1;
        if (a.getValue(CONN[4])) x0 = 0;
        if (a.getValue(CONN[5])) x1 = 1;
        return new AxisAlignedBB(x0, y0, z0, x1, y1, z1);
    }

    @SideOnly(Side.CLIENT)
    @Override
    public void addInformation(ItemStack stack, @Nullable World world, List<String> tooltip, ITooltipFlag flag) {
        tooltip.add(I18n.translateToLocalFormatted("waterphysics.tooltip.pipe", Math.round(diameter * 100),
                Math.round(Math.PI * diameter * diameter / 4 * 1000), Math.round(ratedKPa)));
    }
}
