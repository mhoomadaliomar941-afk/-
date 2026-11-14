package com.waterphysics.tile;

import com.waterphysics.pipe.PipeManager;
import net.minecraft.block.state.IBlockState;
import net.minecraft.nbt.NBTTagCompound;
import net.minecraft.network.NetworkManager;
import net.minecraft.network.play.server.SPacketUpdateTileEntity;
import net.minecraft.tileentity.TileEntity;
import net.minecraft.util.EnumFacing;
import net.minecraft.util.math.BlockPos;
import net.minecraft.world.World;

/** A node of the pipe network: holds water (pipe segment / valve body / pump chamber of 1 m length). */
public abstract class TileFluidNode extends TileEntity {
    /** stored water, m^3 */
    public float volume;
    /** relaxed pressure head (absolute m) when the node is full */
    public float pressureHead;
    /** last computed hydraulic head (absolute m) */
    public float head;
    /** throughput (m^3/s), for display */
    public float throughput;
    /** fill fraction last sent to clients */
    private float syncedFill = -1f;
    private int syncCooldown;
    /** client: fill fraction for rendering */
    public float clientFill;

    /** inner diameter (m) */
    public abstract float diameter();

    /** pressure rating in kPa (gauge) */
    public float ratedPressure() {
        return 1000f;
    }

    /** whether a link to another node may exist on this side */
    public abstract boolean connectsTo(EnumFacing side);

    /** whether this side may exchange water with the world when it is a free end */
    public boolean canPort(EnumFacing side) {
        return connectsTo(side);
    }

    public float area() {
        float d = diameter();
        return (float) (Math.PI * d * d / 4.0);
    }

    /** capacity of the 1 m long node (m^3) */
    public float capacity() {
        return area();
    }

    public float gaugePressureKPa() {
        return Math.max(0f, head - (pos.getY() + 0.5f)) * 9.81f;
    }

    @Override
    public void onLoad() {
        if (world != null && !world.isRemote) PipeManager.get(world).add(this);
    }

    private boolean unloading;

    @Override
    public void invalidate() {
        super.invalidate();
        if (world != null && !world.isRemote) {
            PipeManager m = PipeManager.get(world);
            m.remove(this);
            // the block was broken or replaced: its water goes back into the world (mass conservation)
            if (!unloading && volume > 0f) m.release(pos, volume);
            volume = 0f;
        }
    }

    @Override
    public void onChunkUnload() {
        unloading = true;
        if (world != null && !world.isRemote) PipeManager.get(world).remove(this);
    }

    @Override
    public boolean shouldRefresh(World world, BlockPos pos, IBlockState oldState, IBlockState newState) {
        return oldState.getBlock() != newState.getBlock();
    }

    @Override
    public void readFromNBT(NBTTagCompound t) {
        super.readFromNBT(t);
        volume = t.getFloat("vol");
        pressureHead = t.getFloat("ph");
        readExtra(t);
    }

    @Override
    public NBTTagCompound writeToNBT(NBTTagCompound t) {
        super.writeToNBT(t);
        t.setFloat("vol", volume);
        t.setFloat("ph", pressureHead);
        writeExtra(t);
        return t;
    }

    protected void readExtra(NBTTagCompound t) {}

    protected void writeExtra(NBTTagCompound t) {}

    // ---- client sync (fill level for rendering, state for models) ----
    @Override
    public NBTTagCompound getUpdateTag() {
        NBTTagCompound t = super.getUpdateTag();
        t.setFloat("fill", Math.min(1f, volume / capacity()));
        writeExtra(t);
        return t;
    }

    @Override
    public void handleUpdateTag(NBTTagCompound t) {
        super.handleUpdateTag(t);
        clientFill = t.getFloat("fill");
        readExtra(t);
    }

    @Override
    public SPacketUpdateTileEntity getUpdatePacket() {
        return new SPacketUpdateTileEntity(pos, 0, getUpdateTag());
    }

    @Override
    public void onDataPacket(NetworkManager net, SPacketUpdateTileEntity pkt) {
        handleUpdateTag(pkt.getNbtCompound());
        if (world != null) world.markBlockRangeForRenderUpdate(pos, pos);
    }

    /** server: send state to clients when the fill level changed noticeably */
    public void syncIfChanged(boolean force) {
        if (syncCooldown > 0) syncCooldown--;
        float f = Math.min(1f, volume / capacity());
        if (force || (syncCooldown == 0 && Math.abs(f - syncedFill) > 0.04f)) {
            syncedFill = f;
            syncCooldown = 10;
            IBlockState s = world.getBlockState(pos);
            world.notifyBlockUpdate(pos, s, s, 2);
            markDirty();
        }
    }
}
