package com.realisticworld.forge;

import com.realisticworld.core.DataStore;
import net.minecraft.nbt.NBTTagCompound;
import net.minecraft.world.storage.WorldSavedData;

import java.util.LinkedHashMap;
import java.util.Map;

/**
 * Everything the mod saves, stored with the world (data/realisticworld.dat).
 * Minecraft writes it with the world; the core's DataStore lives inside.
 */
public class RwSavedData extends WorldSavedData {
    public static final String NAME = "realisticworld";

    public final DataStore store = new DataStore();

    public RwSavedData(String name) {
        super(name);
    }

    public RwSavedData() {
        this(NAME);
    }

    @Override
    public void readFromNBT(NBTTagCompound tag) {
        Map<String, Map<String, Object>> all = new LinkedHashMap<>();
        NBTTagCompound entries = tag.getCompoundTag("entries");
        for (String k : entries.getKeySet()) all.put(k, Nbt.toMap(entries.getCompoundTag(k)));
        store.restore(all);
    }

    @Override
    public NBTTagCompound writeToNBT(NBTTagCompound tag) {
        NBTTagCompound entries = new NBTTagCompound();
        for (Map.Entry<String, Map<String, Object>> e : store.snapshot().entrySet()) entries.setTag(e.getKey(), Nbt.fromMap(e.getValue()));
        tag.setTag("entries", entries);
        tag.setInteger("format", 1);
        return tag;
    }

    /** Called every tick: if the core changed anything, let Minecraft save it with the world. */
    public void syncDirty() {
        if (store.isDirty()) {
            store.markClean();
            markDirty();
        }
    }
}
