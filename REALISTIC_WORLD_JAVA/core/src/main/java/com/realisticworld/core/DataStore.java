package com.realisticworld.core;

import java.util.ArrayList;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;
import java.util.TreeMap;

/**
 * Key/value store for everything the mod saves. Values are trees of
 * Map / List / String / Number / Boolean. The Forge layer turns the whole store
 * into NBT inside a WorldSavedData (written to a temporary file and renamed, so
 * a crash during saving keeps the previous file). Values are deep-copied in and
 * out so a system can never change saved data by accident.
 */
public final class DataStore {
    private final TreeMap<String, Map<String, Object>> data = new TreeMap<>();
    private boolean dirty;
    private long saves;

    public synchronized Map<String, Object> load(String key) {
        Map<String, Object> v = data.get(key);
        return v == null ? null : deepCopy(v);
    }

    public synchronized void save(String key, Map<String, Object> value) {
        data.put(key, deepCopy(value));
        dirty = true;
        saves++;
    }

    public synchronized void delete(String key) {
        if (data.remove(key) != null) dirty = true;
    }

    public synchronized List<String> keys(String prefix) {
        List<String> out = new ArrayList<>();
        for (String k : data.tailMap(prefix, true).keySet()) {
            if (!k.startsWith(prefix)) break;
            out.add(k);
        }
        return out;
    }

    public synchronized int size() {
        return data.size();
    }

    public synchronized long saves() {
        return saves;
    }

    /** True if something changed since the Forge layer last wrote the store. */
    public synchronized boolean isDirty() {
        return dirty;
    }

    public synchronized void markClean() {
        dirty = false;
    }

    /** Whole store, for the Forge layer to write. */
    public synchronized Map<String, Map<String, Object>> snapshot() {
        Map<String, Map<String, Object>> out = new LinkedHashMap<>();
        for (Map.Entry<String, Map<String, Object>> e : data.entrySet()) out.put(e.getKey(), deepCopy(e.getValue()));
        return out;
    }

    /** Replace everything with data read from the world (no dirty flag). */
    public synchronized void restore(Map<String, Map<String, Object>> all) {
        data.clear();
        for (Map.Entry<String, Map<String, Object>> e : all.entrySet()) data.put(e.getKey(), deepCopy(e.getValue()));
        dirty = false;
    }

    @SuppressWarnings("unchecked")
    public static <T> T deepCopy(T v) {
        if (v instanceof Map) {
            Map<String, Object> out = new LinkedHashMap<>();
            for (Map.Entry<?, ?> e : ((Map<?, ?>) v).entrySet()) out.put(String.valueOf(e.getKey()), deepCopy(e.getValue()));
            return (T) out;
        }
        if (v instanceof List) {
            List<Object> out = new ArrayList<>();
            for (Object o : (List<?>) v) out.add(deepCopy(o));
            return (T) out;
        }
        if (v == null || v instanceof String || v instanceof Number || v instanceof Boolean) return v;
        throw new IllegalArgumentException("cannot store " + v.getClass().getName());
    }

    // ---- typed readers that tolerate damaged data ----

    public static int getInt(Map<String, Object> m, String key, int def) {
        Object v = m == null ? null : m.get(key);
        return v instanceof Number ? ((Number) v).intValue() : def;
    }

    public static long getLong(Map<String, Object> m, String key, long def) {
        Object v = m == null ? null : m.get(key);
        return v instanceof Number ? ((Number) v).longValue() : def;
    }

    public static double getDouble(Map<String, Object> m, String key, double def) {
        Object v = m == null ? null : m.get(key);
        return v instanceof Number && !Double.isNaN(((Number) v).doubleValue()) ? ((Number) v).doubleValue() : def;
    }

    public static boolean getBool(Map<String, Object> m, String key, boolean def) {
        Object v = m == null ? null : m.get(key);
        return v instanceof Boolean ? (Boolean) v : def;
    }

    public static String getString(Map<String, Object> m, String key, String def) {
        Object v = m == null ? null : m.get(key);
        return v instanceof String ? (String) v : def;
    }

    @SuppressWarnings("unchecked")
    public static List<Object> getList(Map<String, Object> m, String key) {
        Object v = m == null ? null : m.get(key);
        return v instanceof List ? (List<Object>) v : new ArrayList<>();
    }

    @SuppressWarnings("unchecked")
    public static Map<String, Object> getMap(Map<String, Object> m, String key) {
        Object v = m == null ? null : m.get(key);
        return v instanceof Map ? (Map<String, Object>) v : null;
    }
}
