package com.realisticworld.core;

import java.util.ArrayList;
import java.util.Arrays;
import java.util.Collections;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;
import java.util.function.BiConsumer;

/**
 * World settings: typed schema, validation of every saved and entered value,
 * per-system switches, change listeners and a dirty flag for saving.
 */
public final class Config {
    /** Built-in setting keys. */
    public static final String ENABLED = "enabled";
    public static final String PROFILE = "profile";
    public static final String ADAPTIVE = "adaptive";
    public static final String LOG_LEVEL = "logLevel";
    public static final int SCHEMA_VERSION = 1;

    /** A setting: bool, enum (one of values) or int (min..max). */
    public static final class Def {
        public final String type;
        public final Object def;
        public final List<String> values;
        public final int min;
        public final int max;
        public final String label;
        private Object defaultValue;

        private Def(String type, Object def, List<String> values, int min, int max, String label) {
            this.type = type;
            this.def = def;
            this.values = values;
            this.min = min;
            this.max = max;
            this.label = label;
            this.defaultValue = def;
        }

        public static Def bool(boolean def, String label) {
            return new Def("bool", def, null, 0, 0, label);
        }

        public static Def choice(String def, String label, String... values) {
            return new Def("enum", def, Collections.unmodifiableList(Arrays.asList(values)), 0, 0, label);
        }

        public static Def integer(int def, int min, int max, String label) {
            return new Def("int", def, null, min, max, label);
        }

        public Object defaultValue() {
            return defaultValue;
        }
    }

    /** Result of validating one value. */
    public static final class Check {
        public final boolean ok;
        public final Object value;
        public final String error;

        Check(boolean ok, Object value, String error) {
            this.ok = ok;
            this.value = value;
            this.error = error;
        }
    }

    private final Map<String, Def> schema = new LinkedHashMap<>();
    private final Map<String, Object> values = new LinkedHashMap<>();
    private final List<BiConsumer<String, Object>> listeners = new ArrayList<>();
    private final RwLog log;
    private boolean dirty;

    public Config(RwLog log) {
        this.log = log;
        define(ENABLED, Def.bool(true, "Simulation enabled"));
        define(PROFILE, Def.choice("MEDIUM", "Performance profile", "LOW", "MEDIUM", "ULTRA"));
        define(ADAPTIVE, Def.bool(true, "Adaptive throttling"));
        define(LOG_LEVEL, Def.choice("INFO", "Log level", "DEBUG", "INFO", "WARN", "ERROR"));
    }

    public static String systemKey(String id) {
        return "system." + id;
    }

    public static Check validate(Def d, Object v) {
        switch (d.type) {
            case "bool":
                return v instanceof Boolean ? new Check(true, v, null) : new Check(false, d.defaultValue, "expected true/false, got " + v);
            case "enum":
                return v instanceof String && d.values.contains(v)
                    ? new Check(true, v, null)
                    : new Check(false, d.defaultValue, "expected one of " + String.join("/", d.values) + ", got " + v);
            case "int": {
                if (!(v instanceof Number) || Double.isNaN(((Number) v).doubleValue())) {
                    return new Check(false, d.defaultValue, "expected a number, got " + v);
                }
                double raw = ((Number) v).doubleValue();
                int c = (int) Math.max(d.min, Math.min(d.max, Math.round(raw)));
                return c == raw ? new Check(true, c, null) : new Check(false, c, raw + " adjusted to " + c);
            }
            default:
                return new Check(false, null, "unknown setting type");
        }
    }

    /** Declare a setting (before load). */
    public void define(String key, Def def) {
        schema.put(key, def);
        if (!values.containsKey(key)) values.put(key, def.defaultValue);
    }

    public void defineSystem(String id, String label, boolean enabled) {
        define(systemKey(id), Def.bool(enabled, label));
    }

    /** Change a default (e.g. the profile picked from device memory). */
    public void setDefault(String key, Object value) {
        Def d = schema.get(key);
        if (d == null) return;
        Check c = validate(d, value);
        if (c.ok) {
            d.defaultValue = c.value;
            values.put(key, c.value);
        }
    }

    public Map<String, Def> schema() {
        return Collections.unmodifiableMap(schema);
    }

    /**
     * Load saved values. Invalid or unknown entries are dropped with a warning;
     * missing ones keep their defaults. Returns the warnings.
     */
    public List<String> load(Map<String, Object> saved) {
        List<String> warnings = new ArrayList<>();
        if (saved == null) return warnings;
        Object schemaVersion = saved.get("schema");
        if (schemaVersion instanceof Number && ((Number) schemaVersion).intValue() > SCHEMA_VERSION) {
            warnings.add("settings were saved by a newer version (schema " + schemaVersion + "); unknown entries ignored");
        }
        Object raw = saved.get("values");
        if (!(raw instanceof Map)) {
            warnings.add("saved settings have an unexpected shape; using defaults");
            dirty = true;
            return warnings;
        }
        for (Map.Entry<?, ?> e : ((Map<?, ?>) raw).entrySet()) {
            String key = String.valueOf(e.getKey());
            Def d = schema.get(key);
            if (d == null) {
                warnings.add("unknown setting \"" + key + "\" ignored");
                continue;
            }
            Check c = validate(d, e.getValue());
            if (!c.ok) {
                warnings.add("setting \"" + key + "\": " + c.error);
                dirty = true;
            }
            values.put(key, c.value);
        }
        for (String w : warnings) log.warn("config", w);
        return warnings;
    }

    public Map<String, Object> save() {
        Map<String, Object> out = new LinkedHashMap<>();
        out.put("schema", SCHEMA_VERSION);
        out.put("values", new LinkedHashMap<>(values));
        dirty = false;
        return out;
    }

    public boolean isDirty() {
        return dirty;
    }

    public Object get(String key) {
        return values.get(key);
    }

    public boolean bool(String key) {
        return Boolean.TRUE.equals(values.get(key));
    }

    public String string(String key) {
        Object v = values.get(key);
        return v == null ? null : v.toString();
    }

    public boolean isSystemEnabled(String id) {
        return bool(systemKey(id));
    }

    /** Set a value; invalid values are refused (ints are clamped). */
    public Check set(String key, Object value) {
        Def d = schema.get(key);
        if (d == null) return new Check(false, null, "unknown setting \"" + key + "\"");
        Check c = validate(d, value);
        if (!c.ok && !"int".equals(d.type)) return c;
        if (c.value.equals(values.get(key))) return new Check(true, c.value, null);
        values.put(key, c.value);
        dirty = true;
        for (BiConsumer<String, Object> l : listeners) {
            try {
                l.accept(key, c.value);
            } catch (RuntimeException ex) {
                log.error("config", "listener failed for \"" + key + "\"", ex);
            }
        }
        return new Check(true, c.value, null);
    }

    public Check setSystemEnabled(String id, boolean on) {
        return set(systemKey(id), on);
    }

    public void onChange(BiConsumer<String, Object> listener) {
        listeners.add(listener);
    }

    /** Every setting back to its default (listeners are notified). */
    public void reset() {
        for (Map.Entry<String, Def> e : schema.entrySet()) set(e.getKey(), e.getValue().defaultValue);
        dirty = true;
    }
}
