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
    /** water total of each chunk at its last save (for auditing chunks that are not loaded) */
    public final java.util.HashMap<Long, Double> savedTotals = new java.util.HashMap<>();

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
        savedTotals.clear();
        long[] keys = toLongs(t.getTag("ledgerK"));
        net.minecraft.nbt.NBTTagList vals = t.getTagList("ledgerV", 6);
        for (int i = 0; i < Math.min(keys.length, vals.tagCount()); i++) savedTotals.put(keys[i], vals.getDoubleAt(i));
    }

    private static long[] toLongs(net.minecraft.nbt.NBTBase b) {
        if (!(b instanceof net.minecraft.nbt.NBTTagList)) return new long[0];
        net.minecraft.nbt.NBTTagList l = (net.minecraft.nbt.NBTTagList) b;
        long[] r = new long[l.tagCount()];
        for (int i = 0; i < r.length; i++) r[i] = ((net.minecraft.nbt.NBTTagLong) l.get(i)).getLong();
        return r;
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
        net.minecraft.nbt.NBTTagList k = new net.minecraft.nbt.NBTTagList();
        net.minecraft.nbt.NBTTagList v = new net.minecraft.nbt.NBTTagList();
        for (java.util.Map.Entry<Long, Double> e : savedTotals.entrySet()) {
            k.appendTag(new net.minecraft.nbt.NBTTagLong(e.getKey()));
            v.appendTag(new net.minecraft.nbt.NBTTagDouble(e.getValue()));
        }
        t.setTag("ledgerK", k);
        t.setTag("ledgerV", v);
        return t;
    }
}
