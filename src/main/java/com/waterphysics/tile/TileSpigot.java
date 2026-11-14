package com.waterphysics.tile;

import com.waterphysics.block.BlockSpigot;
import com.waterphysics.config.WPConfig;
import com.waterphysics.net.MsgEffect;
import com.waterphysics.net.WaterNet;
import com.waterphysics.sim.CellType;
import com.waterphysics.sim.WaterManager;
import com.waterphysics.sim.WaterSim;
import net.minecraft.block.state.IBlockState;
import net.minecraft.nbt.NBTTagCompound;
import net.minecraft.network.NetworkManager;
import net.minecraft.network.play.server.SPacketUpdateTileEntity;
import net.minecraft.tileentity.TileEntity;
import net.minecraft.util.EnumFacing;
import net.minecraft.util.math.BlockPos;
import net.minecraft.world.World;

/**
 * Orifice built into a tank wall (adjustable hole). Water flows from the back cell to the front cell with
 * Torricelli's law Q = Cd * A * sqrt(2 g dh), where A is the selected opening area and dh the head
 * difference across the wall. A redstone signal closes it.
 */
public class TileSpigot extends TileEntity {
    /** opening areas in cm^2 */
    public static final int[] APERTURES_CM2 = {0, 5, 20, 100, 500, 2000};
    public int aperture = 3;
    public float lastFlow; // m^3/s
    private boolean wasFlowing;

    public EnumFacing facing() {
        IBlockState s = world.getBlockState(pos);
        return s.getBlock() instanceof BlockSpigot ? s.getValue(BlockSpigot.FACING) : EnumFacing.NORTH;
    }

    public float areaM2() {
        if (world.isBlockPowered(pos)) return 0f;
        return APERTURES_CM2[Math.max(0, Math.min(APERTURES_CM2.length - 1, aperture))] / 10000f;
    }

    public void cycle() {
        aperture = (aperture + 1) % APERTURES_CM2.length;
        markDirty();
        IBlockState s = world.getBlockState(pos);
        world.notifyBlockUpdate(pos, s, s, 3);
    }

    @Override
    public void onLoad() {
        if (world != null && !world.isRemote) com.waterphysics.pipe.PipeManager.get(world).addSpigot(this);
    }

    @Override
    public void invalidate() {
        super.invalidate();
        if (world != null && !world.isRemote) com.waterphysics.pipe.PipeManager.get(world).removeSpigot(this);
    }

    @Override
    public void onChunkUnload() {
        if (world != null && !world.isRemote) com.waterphysics.pipe.PipeManager.get(world).removeSpigot(this);
    }

    /** called every tick by the hydraulics manager (independent of vanilla tile entity ticking) */
    public void update() {
        if (world.isRemote || isInvalid()) return;
        WaterSim sim = WaterManager.get(world);
        if (sim == null || !WPConfig.simulation.enabled) return;
        EnumFacing f = facing();
        BlockPos back = pos.offset(f.getOpposite()), front = pos.offset(f);
        float a = areaM2();
        lastFlow = 0f;
        if (a <= 0f) return;
        IBlockState fs = world.getBlockState(front), bs = world.getBlockState(back);
        if (CellType.of(fs) != CellType.OPEN || CellType.of(bs) != CellType.OPEN) return;
        float vb = sim.getVolume(back), vf = sim.getVolume(front);
        float orifice = pos.getY() + 0.5f;
        float hb = vb > 0f ? sim.headAt(back) : back.getY();
        float hf = vf > 0f ? sim.headAt(front) : front.getY();
        // water discharges at the orifice height unless the outlet is submerged
        float hOut = Math.max(orifice, hf);
        float hIn = Math.max(orifice, hb);
        float dh = hb - hOut;
        BlockPos from = back, to = front;
        if (dh <= 0f) {
            dh = hf - hIn;
            from = front;
            to = back;
            if (dh <= 0f) {
                wasFlowing = false;
                return;
            }
        }
        float g = (float) WPConfig.physics.gravity;
        float dt = (float) (0.05 * WPConfig.simulation.timeScale);
        float q = (float) (WPConfig.physics.dischargeCoefficient * a * Math.sqrt(2.0 * g * dh));
        float amt = Math.min(q * dt, sim.getVolume(from));
        if (amt <= 0f) return;
        float taken = sim.takeWater(from, amt);
        sim.addWater(to, taken);
        lastFlow = taken / dt;
        if (!wasFlowing && taken > 0f) WaterNet.effect(world, MsgEffect.LEAK, pos, q);
        wasFlowing = true;
    }

    @Override
    public boolean shouldRefresh(World world, BlockPos pos, IBlockState oldState, IBlockState newState) {
        return oldState.getBlock() != newState.getBlock();
    }

    @Override
    public void readFromNBT(NBTTagCompound t) {
        super.readFromNBT(t);
        aperture = t.hasKey("ap") ? t.getInteger("ap") : 3;
    }

    @Override
    public NBTTagCompound writeToNBT(NBTTagCompound t) {
        super.writeToNBT(t);
        t.setInteger("ap", aperture);
        return t;
    }

    @Override
    public NBTTagCompound getUpdateTag() {
        return writeToNBT(new NBTTagCompound());
    }

    @Override
    public SPacketUpdateTileEntity getUpdatePacket() {
        return new SPacketUpdateTileEntity(pos, 0, getUpdateTag());
    }

    @Override
    public void onDataPacket(NetworkManager net, SPacketUpdateTileEntity pkt) {
        readFromNBT(pkt.getNbtCompound());
    }
}
