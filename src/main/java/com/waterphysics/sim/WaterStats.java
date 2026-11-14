package com.waterphysics.sim;

import net.minecraft.nbt.NBTTagCompound;
import net.minecraft.world.storage.WorldSavedData;

/**
 * Mass accounting for one dimension (all values in m^3 = 1000 L). Every way water can enter or leave
 * the physical system is counted, so conservation can be audited:
 * <pre>stored now = sum(in) - sum(out)</pre>
 * where "stored" = water in blocks + water inside pipes/pumps.
 */
public final class WaterStats extends WorldSavedData {
    public static final String NAME = "waterphysics_stats";

    // sources
    public double bucketsIn, convertedIn, reservoirIn, commandIn, pumpedIn;
    // sinks
    public double bucketsOut, reservoirOut, commandOut, displacedOut, evaporated, destroyedOut, otherOut;
    /** float rounding residue (values below 1e-7 m^3 cleared) */
    public double rounding;

    public WaterStats() { super(NAME); }
    public WaterStats(String name) { super(name); }

    public double totalIn() { return bucketsIn + convertedIn + reservoirIn + commandIn + pumpedIn; }
    public double totalOut() { return bucketsOut + reservoirOut + commandOut + displacedOut + evaporated + destroyedOut + otherOut; }
    public double expectedStored() { return totalIn() - totalOut() - rounding; }

    @Override
    public void readFromNBT(NBTTagCompound t) {
        bucketsIn = t.getDouble("bucketsIn");
        convertedIn = t.getDouble("convertedIn");
        reservoirIn = t.getDouble("reservoirIn");
        commandIn = t.getDouble("commandIn");
        pumpedIn = t.getDouble("pumpedIn");
        bucketsOut = t.getDouble("bucketsOut");
        reservoirOut = t.getDouble("reservoirOut");
        commandOut = t.getDouble("commandOut");
        displacedOut = t.getDouble("displacedOut");
        evaporated = t.getDouble("evaporated");
        destroyedOut = t.getDouble("destroyedOut");
        otherOut = t.getDouble("otherOut");
        rounding = t.getDouble("rounding");
    }

    @Override
    public NBTTagCompound writeToNBT(NBTTagCompound t) {
        t.setDouble("bucketsIn", bucketsIn);
        t.setDouble("convertedIn", convertedIn);
        t.setDouble("reservoirIn", reservoirIn);
        t.setDouble("commandIn", commandIn);
        t.setDouble("pumpedIn", pumpedIn);
        t.setDouble("bucketsOut", bucketsOut);
        t.setDouble("reservoirOut", reservoirOut);
        t.setDouble("commandOut", commandOut);
        t.setDouble("displacedOut", displacedOut);
        t.setDouble("evaporated", evaporated);
        t.setDouble("destroyedOut", destroyedOut);
        t.setDouble("otherOut", otherOut);
        t.setDouble("rounding", rounding);
        return t;
    }
}
