package com.realisticworld.forge;

import net.minecraft.nbt.NBTBase;
import net.minecraft.nbt.NBTTagByte;
import net.minecraft.nbt.NBTTagCompound;
import net.minecraft.nbt.NBTTagDouble;
import net.minecraft.nbt.NBTTagFloat;
import net.minecraft.nbt.NBTTagInt;
import net.minecraft.nbt.NBTTagLong;
import net.minecraft.nbt.NBTTagShort;
import net.minecraft.nbt.NBTTagString;

import java.util.ArrayList;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;

/**
 * Converts the core's data trees (Map / List / String / Number / Boolean) to
 * NBT and back, keeping number types. Lists can mix types (NBT lists cannot),
 * so a list is stored as a compound with keys "0".."n-1" and a size marker.
 */
public final class Nbt {
    private static final String LIST_MARK = "\u0000list";

    private Nbt() {}

    public static NBTTagCompound fromMap(Map<String, Object> m) {
        NBTTagCompound tag = new NBTTagCompound();
        for (Map.Entry<String, Object> e : m.entrySet()) {
            NBTBase v = toTag(e.getValue());
            if (v != null) tag.setTag(e.getKey(), v);
        }
        return tag;
    }

    @SuppressWarnings("unchecked")
    private static NBTBase toTag(Object v) {
        if (v instanceof Map) return fromMap((Map<String, Object>) v);
        if (v instanceof List) {
            List<?> l = (List<?>) v;
            NBTTagCompound tag = new NBTTagCompound();
            tag.setInteger(LIST_MARK, l.size());
            for (int i = 0; i < l.size(); i++) {
                NBTBase t = toTag(l.get(i));
                if (t != null) tag.setTag(String.valueOf(i), t);
            }
            return tag;
        }
        if (v instanceof String) return new NBTTagString((String) v);
        if (v instanceof Boolean) return new NBTTagByte((byte) ((Boolean) v ? 1 : 0));
        if (v instanceof Integer) return new NBTTagInt((Integer) v);
        if (v instanceof Long) return new NBTTagLong((Long) v);
        if (v instanceof Double) return new NBTTagDouble((Double) v);
        if (v instanceof Float) return new NBTTagDouble((Float) v);
        if (v instanceof Number) return new NBTTagLong(((Number) v).longValue());
        return null;
    }

    public static Map<String, Object> toMap(NBTTagCompound tag) {
        Map<String, Object> m = new LinkedHashMap<>();
        for (String k : tag.getKeySet()) m.put(k, fromTag(tag.getTag(k)));
        return m;
    }

    private static Object fromTag(NBTBase t) {
        if (t instanceof NBTTagCompound) {
            NBTTagCompound c = (NBTTagCompound) t;
            if (c.hasKey(LIST_MARK)) {
                int n = c.getInteger(LIST_MARK);
                List<Object> l = new ArrayList<>(n);
                for (int i = 0; i < n; i++) l.add(c.hasKey(String.valueOf(i)) ? fromTag(c.getTag(String.valueOf(i))) : null);
                return l;
            }
            return toMap(c);
        }
        if (t instanceof NBTTagString) return ((NBTTagString) t).getString();
        if (t instanceof NBTTagByte) return ((NBTTagByte) t).getByte() != 0;
        if (t instanceof NBTTagInt) return ((NBTTagInt) t).getInt();
        if (t instanceof NBTTagLong) return ((NBTTagLong) t).getLong();
        if (t instanceof NBTTagDouble) return ((NBTTagDouble) t).getDouble();
        if (t instanceof NBTTagFloat) return (double) ((NBTTagFloat) t).getFloat();
        if (t instanceof NBTTagShort) return (int) ((NBTTagShort) t).getShort();
        return null;
    }
}
