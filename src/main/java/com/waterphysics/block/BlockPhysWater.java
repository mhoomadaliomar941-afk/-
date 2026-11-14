package com.waterphysics.block;

import com.waterphysics.WaterPhysics;
import com.waterphysics.sim.WaterManager;
import com.waterphysics.sim.WaterSim;
import net.minecraft.block.Block;
import net.minecraft.block.material.Material;
import net.minecraft.block.properties.IProperty;
import net.minecraft.block.state.BlockStateContainer;
import net.minecraft.block.state.IBlockState;
import net.minecraft.init.SoundEvents;
import net.minecraft.item.ItemStack;
import net.minecraft.util.EnumFacing;
import net.minecraft.util.EnumParticleTypes;
import net.minecraft.util.SoundCategory;
import net.minecraft.util.math.BlockPos;
import net.minecraft.util.math.RayTraceResult;
import net.minecraft.entity.player.EntityPlayer;
import net.minecraft.world.IBlockAccess;
import net.minecraft.world.World;
import net.minecraft.world.WorldServer;
import net.minecraftforge.common.property.ExtendedBlockState;
import net.minecraftforge.common.property.IExtendedBlockState;
import net.minecraftforge.common.property.IUnlistedProperty;
import net.minecraftforge.common.property.PropertyFloat;
import net.minecraftforge.fluids.BlockFluidBase;
import net.minecraftforge.fluids.Fluid;
import net.minecraftforge.fluids.FluidRegistry;
import net.minecraftforge.fluids.FluidStack;
import net.minecraftforge.fml.relauncher.Side;
import net.minecraftforge.fml.relauncher.SideOnly;

import javax.annotation.Nonnull;
import javax.annotation.Nullable;
import java.util.Random;

/**
 * Physical water block. The block state LEVEL (0..15) only encodes the visible height; the exact volume,
 * velocity and pressure live in the simulation ({@link WaterSim}). The block never flows on its own.
 */
public class BlockPhysWater extends BlockFluidBase {
    /** width of a falling stream (0 = not a stream, 1 = full block) */
    public static final PropertyFloat STREAM = new PropertyFloat("stream", 0f, 1f);
    /** a full block without water above renders slightly below the block top, like vanilla water */
    public static final float SURFACE_SCALE = 0.9f;

    public BlockPhysWater(Fluid fluid) {
        super(fluid, Material.WATER);
        setQuantaPerBlock(16);
        setHardness(100.0F);
        setLightOpacity(3);
        disableStats();
        setRegistryName(WaterPhysics.MODID, "water");
        setUnlocalizedName(WaterPhysics.MODID + ".water");
        setDefaultState(blockState.getBaseState().withProperty(LEVEL, 15));
        setTickRandomly(false);
    }

    @Override
    protected BlockStateContainer createBlockState() {
        IUnlistedProperty<?>[] props = new IUnlistedProperty<?>[FLUID_RENDER_PROPS.size() + 1];
        for (int i = 0; i < FLUID_RENDER_PROPS.size(); i++) props[i] = FLUID_RENDER_PROPS.get(i);
        props[props.length - 1] = STREAM;
        return new ExtendedBlockState(this, new IProperty[]{LEVEL}, props);
    }

    @Override
    public int getMetaFromState(IBlockState state) {
        return state.getValue(LEVEL);
    }

    @Override
    public IBlockState getStateFromMeta(int meta) {
        return getDefaultState().withProperty(LEVEL, meta & 15);
    }

    /** visible fill fraction 0..1 of a cell (0 when not our water) */
    public static float fill(IBlockAccess w, BlockPos p) {
        IBlockState s = w.getBlockState(p);
        if (s.getBlock() instanceof BlockPhysWater) return (s.getValue(LEVEL) + 1) / 16f;
        return 0f;
    }

    // ---------------------------------------------------------------------------------------------
    // no vanilla/forge flowing: everything is done by the simulation
    // ---------------------------------------------------------------------------------------------
    @Override
    public void updateTick(World world, BlockPos pos, IBlockState state, Random rand) {
    }

    @Override
    public boolean requiresUpdates() {
        return false;
    }

    @Override
    public void onBlockAdded(World world, BlockPos pos, IBlockState state) {
        if (!world.isRemote) {
            WaterSim sim = WaterManager.get(world);
            if (sim != null) sim.onWaterBlockAdded(pos, state);
        }
    }

    @Override
    public void neighborChanged(IBlockState state, World world, BlockPos pos, Block blockIn, BlockPos fromPos) {
        if (!world.isRemote) {
            WaterSim sim = WaterManager.get(world);
            if (sim != null && !sim.internalUpdate) sim.blockChanged(fromPos);
        }
    }

    @Override
    public void breakBlock(World world, BlockPos pos, IBlockState state) {
        if (!world.isRemote) {
            WaterSim sim = WaterManager.get(world);
            if (sim != null) sim.onWaterBlockReplaced(pos, world.getBlockState(pos));
        }
        super.breakBlock(world, pos, state);
    }

    @Override
    public boolean canDisplace(IBlockAccess world, BlockPos pos) {
        return false;
    }

    @Override
    public boolean displaceIfPossible(World world, BlockPos pos) {
        return false;
    }

    @Override
    public int getQuantaValue(IBlockAccess world, BlockPos pos) {
        IBlockState s = world.getBlockState(pos);
        if (s.getBlock() == this) return s.getValue(LEVEL) + 1;
        return s.getBlock().isAir(s, world, pos) ? 0 : -1;
    }

    @Override
    public boolean canCollideCheck(IBlockState state, boolean fullHit) {
        return fullHit; // buckets can target any amount of water
    }

    @Override
    public int getMaxRenderHeightMeta() {
        return 15;
    }

    @Override
    public ItemStack getPickBlock(IBlockState state, RayTraceResult target, World world, BlockPos pos, EntityPlayer player) {
        return ItemStack.EMPTY;
    }

    // ---------------------------------------------------------------------------------------------
    // IFluidBlock (interop with other mods' pumps): exposes regular water
    // ---------------------------------------------------------------------------------------------
    @Override
    public int place(World world, BlockPos pos, @Nonnull FluidStack fluidStack, boolean doPlace) {
        if (fluidStack.getFluid() != FluidRegistry.WATER && fluidStack.getFluid() != getFluid()) return 0;
        if (doPlace && !world.isRemote) {
            WaterSim sim = WaterManager.get(world);
            if (sim == null) return 0;
            float added = sim.addWater(pos, fluidStack.amount / 1000f);
            sim.stats.commandIn += added;
            return Math.round(added * 1000f);
        }
        return fluidStack.amount;
    }

    @Nullable
    @Override
    public FluidStack drain(World world, BlockPos pos, boolean doDrain) {
        WaterSim sim = world.isRemote ? null : WaterManager.get(world);
        float v = sim != null ? sim.getVolume(pos) : fill(world, pos);
        int mb = Math.round(Math.min(v, 1f) * 1000f);
        if (mb <= 0) return null;
        if (doDrain && sim != null) {
            float t = sim.takeWater(pos, mb / 1000f);
            sim.stats.otherOut += t;
            mb = Math.round(t * 1000f);
        }
        return new FluidStack(FluidRegistry.WATER, mb);
    }

    @Override
    public boolean canDrain(World world, BlockPos pos) {
        return true;
    }

    @Override
    public float getFilledPercentage(World world, BlockPos pos) {
        if (!world.isRemote) {
            WaterSim sim = WaterManager.get(world);
            if (sim != null) return Math.min(1f, sim.getVolume(pos));
        }
        return fill(world, pos);
    }

    // ---------------------------------------------------------------------------------------------
    // rendering data (client): corner heights of the surface, flow direction and stream width
    // ---------------------------------------------------------------------------------------------
    @Override
    public IBlockState getExtendedState(IBlockState oldState, IBlockAccess world, BlockPos pos) {
        IExtendedBlockState state = (IExtendedBlockState) oldState;
        boolean waterAbove = isWaterOrVanilla(world, pos.up());
        float here = waterAbove ? 1f : (oldState.getValue(LEVEL) + 1) / 16f * SURFACE_SCALE;
        float[][] h = new float[3][3];
        for (int dx = -1; dx <= 1; dx++) {
            for (int dz = -1; dz <= 1; dz++) {
                h[dx + 1][dz + 1] = (dx == 0 && dz == 0) ? here : neighbourHeight(world, pos.add(dx, 0, dz));
            }
        }
        float c00 = corner(h, 0, 0), c01 = corner(h, 0, 1), c11 = corner(h, 1, 1), c10 = corner(h, 1, 0);
        // order used by forge: (x0,z0) (x0,z1) (x1,z1) (x1,z0)
        state = state.withProperty(LEVEL_CORNERS[0], c00).withProperty(LEVEL_CORNERS[1], c01)
                .withProperty(LEVEL_CORNERS[2], c11).withProperty(LEVEL_CORNERS[3], c10);
        // flow direction: down the surface gradient
        float gx = (h[2][1] < 0 ? here : h[2][1]) - (h[0][1] < 0 ? here : h[0][1]);
        float gz = (h[1][2] < 0 ? here : h[1][2]) - (h[1][0] < 0 ? here : h[1][0]);
        float dir = -1000f;
        if (Math.abs(gx) > 0.01f || Math.abs(gz) > 0.01f) dir = (float) Math.atan2(-gz, -gx) - (float) (Math.PI / 2);
        state = state.withProperty(FLOW_DIRECTION, dir);
        // falling stream: nothing solid to rest on and it is not a full block of a column
        float stream = 0f;
        IBlockState below = world.getBlockState(pos.down());
        boolean belowFluid = below.getBlock() == this;
        boolean belowOpen = belowFluid ? below.getValue(LEVEL) < 15 : !below.getMaterial().blocksMovement() && !below.getMaterial().isLiquid();
        if (belowOpen && com.waterphysics.config.WPConfig.visuals.thinStreams) {
            float lvl = (oldState.getValue(LEVEL) + 1) / 16f;
            stream = (float) Math.max(0.12, Math.min(1.0, Math.sqrt(lvl) * 1.05));
        }
        state = state.withProperty(STREAM, stream);
        for (int i = 0; i < 4; i++) state = state.withProperty(SIDE_OVERLAYS[i], false);
        return state;
    }

    private boolean isWaterOrVanilla(IBlockAccess w, BlockPos p) {
        IBlockState s = w.getBlockState(p);
        return s.getBlock() == this || s.getMaterial() == Material.WATER;
    }

    /** -1 = solid (does not participate in the average), 0 = air, else surface height */
    private float neighbourHeight(IBlockAccess w, BlockPos p) {
        IBlockState s = w.getBlockState(p);
        if (s.getBlock() == this) {
            if (isWaterOrVanilla(w, p.up())) return 1f;
            return (s.getValue(LEVEL) + 1) / 16f * SURFACE_SCALE;
        }
        if (s.getMaterial() == Material.WATER) return 0.89f;
        if (s.getMaterial().isSolid()) return -1f;
        return 0f;
    }

    private static float corner(float[][] h, int cx, int cz) {
        // corner (cx,cz) of the centre cell touches cells [cx..cx+1][cz..cz+1] in the 3x3 grid;
        // air pulls the corner down (sloped water edges), solid blocks do not take part
        float sum = 0f;
        int n = 0;
        for (int a = 0; a <= 1; a++) {
            for (int b = 0; b <= 1; b++) {
                float v = h[cx + a][cz + b];
                if (v >= 1f) return 1f;
                if (v < 0f) continue;
                sum += v;
                n++;
            }
        }
        return n == 0 ? h[1][1] : Math.max(0.01f, sum / n);
    }

    @Override
    public boolean shouldSideBeRendered(IBlockState state, IBlockAccess world, BlockPos pos, EnumFacing side) {
        IBlockState n = world.getBlockState(pos.offset(side));
        if (n.getBlock() == this) {
            if (side == EnumFacing.UP || side == EnumFacing.DOWN) return false;
            // side between a stream and a pool / two water cells: hide unless the neighbour is lower
            return false;
        }
        if (n.getMaterial() == Material.WATER) return false;
        if (side == EnumFacing.UP) return true;
        return !n.doesSideBlockRendering(world, pos.offset(side), side.getOpposite());
    }

    @SideOnly(Side.CLIENT)
    @Override
    public void randomDisplayTick(IBlockState state, World world, BlockPos pos, Random rand) {
        int lvl = state.getValue(LEVEL);
        IBlockState below = world.getBlockState(pos.down());
        boolean falling = below.getBlock() != this && !below.getMaterial().blocksMovement();
        if ((lvl < 15 || falling) && rand.nextInt(48) == 0) {
            world.playSound(pos.getX() + 0.5, pos.getY() + 0.5, pos.getZ() + 0.5, SoundEvents.BLOCK_WATER_AMBIENT,
                    SoundCategory.BLOCKS, rand.nextFloat() * 0.25F + 0.75F, rand.nextFloat() + 0.5F, false);
        }
        if (falling && rand.nextInt(4) == 0) {
            world.spawnParticle(EnumParticleTypes.DRIP_WATER, pos.getX() + rand.nextFloat(), pos.getY() - 0.05, pos.getZ() + rand.nextFloat(), 0, 0, 0);
        }
    }

    @Override
    public boolean isNormalCube(IBlockState state, IBlockAccess world, BlockPos pos) {
        return false;
    }

    public static boolean isServerWorld(World w) {
        return w instanceof WorldServer;
    }
}
