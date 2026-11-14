package com.realisticworld.weather;

import com.realisticworld.core.DataStore;
import com.realisticworld.util.MathUtil;
import com.realisticworld.util.Rng;

import java.util.ArrayList;
import java.util.Arrays;
import java.util.Collections;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;

/**
 * Global weather model of the overworld (same equations and calibration as the
 * Bedrock version). Variables evolve in steps of at most 250 ticks:
 * P pressure 0..1, A temperature anomaly °C, H air humidity 0..1, I rain
 * intensity 0..1 (rain starts above RAIN_ON, stops below RAIN_OFF), storm,
 * wind m/s and direction, fog tendency, days without rain, recent rain share.
 * Rates are per in-game hour (1000 ticks).
 */
public final class WeatherModel {
    public static final int TICKS_PER_HOUR = 1000;
    public static final int TICKS_PER_DAY = 24000;
    public static final int MAX_STEP_TICKS = 250;
    public static final double RAIN_ON = 0.2;
    public static final double RAIN_OFF = 0.05;
    public static final double MODERATE_INTENSITY = 0.22;
    public static final double HEAVY_INTENSITY = 0.34;
    public static final double STORM_INTENSITY = 0.28;
    public static final double DROUGHT_DAYS = 4;

    /** Humidity at which rain becomes likely, per rain-frequency setting. */
    public static double rainThreshold(String frequency) {
        if ("LOW".equals(frequency)) return 0.72;
        if ("HIGH".equals(frequency)) return 0.6;
        return 0.66;
    }

    /** A condition an operator can hold. */
    public static final class Forceable {
        public final double intensity;
        public final boolean storm;
        public final boolean fog;
        public final Double anomaly;
        public final boolean drought;

        Forceable(double intensity, boolean storm, boolean fog, Double anomaly, boolean drought) {
            this.intensity = intensity;
            this.storm = storm;
            this.fog = fog;
            this.anomaly = anomaly;
            this.drought = drought;
        }
    }

    public static final Map<String, Forceable> FORCEABLE;

    static {
        Map<String, Forceable> m = new LinkedHashMap<>();
        m.put("CLEAR", new Forceable(0, false, false, null, false));
        m.put("LIGHT_RAIN", new Forceable(0.2, false, false, null, false));
        m.put("MODERATE_RAIN", new Forceable(0.28, false, false, null, false));
        m.put("HEAVY_RAIN", new Forceable(0.45, false, false, null, false));
        m.put("THUNDERSTORM", new Forceable(0.5, true, false, null, false));
        m.put("FOG", new Forceable(0, false, true, null, false));
        m.put("HEATWAVE", new Forceable(0, false, false, 8.0, false));
        m.put("DROUGHT", new Forceable(0, false, false, 3.0, true));
        FORCEABLE = Collections.unmodifiableMap(m);
    }

    /** Weather state; saved with the world. */
    public static final class State {
        public long tick;
        public Rng rng;
        public double P = 0.5;
        public double A = 0;
        public double H = 0.55;
        public double I = 0;
        public boolean raining;
        public boolean storm;
        public double wind = 4;
        public double windDir = 270;
        public double fogG = 0;
        public double dryDays = 0;
        public double rainEma = 0.25;
        public String forceType;
        public long forceUntil;

        public State(Rng rng) {
            this.rng = rng;
        }

        public State copy() {
            State s = new State(rng.copy());
            s.tick = tick;
            s.P = P;
            s.A = A;
            s.H = H;
            s.I = I;
            s.raining = raining;
            s.storm = storm;
            s.wind = wind;
            s.windDir = windDir;
            s.fogG = fogG;
            s.dryDays = dryDays;
            s.rainEma = rainEma;
            s.forceType = forceType;
            s.forceUntil = forceUntil;
            return s;
        }

        public Map<String, Object> toMap() {
            Map<String, Object> m = new LinkedHashMap<>();
            m.put("v", 1);
            m.put("tick", tick);
            m.put("rng", rng.state());
            m.put("P", P);
            m.put("A", A);
            m.put("H", H);
            m.put("I", I);
            m.put("raining", raining);
            m.put("storm", storm);
            m.put("wind", wind);
            m.put("windDir", windDir);
            m.put("fogG", fogG);
            m.put("dryDays", dryDays);
            m.put("rainEma", rainEma);
            if (forceType != null) {
                m.put("forceType", forceType);
                m.put("forceUntil", forceUntil);
            }
            return m;
        }

        /** Read a saved state; null if it cannot be used. Out-of-range values are clamped. */
        public static State fromMap(Map<String, Object> m) {
            if (m == null || DataStore.getInt(m, "v", 0) != 1) return null;
            for (String k : Arrays.asList("tick", "rng", "P", "A", "H", "I", "wind", "windDir", "fogG", "dryDays", "rainEma")) {
                Object v = m.get(k);
                if (!(v instanceof Number) || Double.isNaN(((Number) v).doubleValue()) || Double.isInfinite(((Number) v).doubleValue())) return null;
            }
            State s = new State(new Rng(DataStore.getInt(m, "rng", 0)));
            s.tick = DataStore.getLong(m, "tick", 0);
            s.P = MathUtil.clamp(DataStore.getDouble(m, "P", 0.5), 0, 1);
            s.A = MathUtil.clamp(DataStore.getDouble(m, "A", 0), -15, 15);
            s.H = MathUtil.clamp(DataStore.getDouble(m, "H", 0.55), 0.05, 1);
            s.I = MathUtil.clamp(DataStore.getDouble(m, "I", 0), 0, 1);
            s.raining = DataStore.getBool(m, "raining", false);
            s.storm = DataStore.getBool(m, "storm", false);
            s.wind = DataStore.getDouble(m, "wind", 4);
            s.windDir = DataStore.getDouble(m, "windDir", 270);
            s.fogG = DataStore.getDouble(m, "fogG", 0);
            s.dryDays = DataStore.getDouble(m, "dryDays", 0);
            s.rainEma = DataStore.getDouble(m, "rainEma", 0.25);
            String ft = DataStore.getString(m, "forceType", null);
            if (ft != null && FORCEABLE.containsKey(ft)) {
                s.forceType = ft;
                s.forceUntil = DataStore.getLong(m, "forceUntil", 0);
            }
            return s;
        }
    }

    private WeatherModel() {}

    public static State create(Rng rng) {
        return new State(rng);
    }

    /** Advance the model by dtTicks. */
    public static void step(State st, long dtTicks, String rainFrequency) {
        double h0 = rainThreshold(rainFrequency);
        long remaining = Math.max(0, dtTicks);
        while (remaining > 0) {
            long dt = Math.min(MAX_STEP_TICKS, remaining);
            remaining -= dt;
            st.tick += dt;
            double h = dt / (double) TICKS_PER_HOUR;
            double sq = Math.sqrt(h);
            Rng rng = st.rng;

            if (st.forceType != null && st.tick >= st.forceUntil) st.forceType = null;
            Forceable force = st.forceType == null ? null : FORCEABLE.get(st.forceType);

            st.P = MathUtil.clamp(st.P + 0.03 * (0.5 - st.P) * h + 0.05 * sq * rng.nextGaussian(), 0, 1);
            double aTarget = force != null && force.anomaly != null ? force.anomaly : 0;
            double aRate = force != null && force.anomaly != null ? 0.5 : 0.02;
            st.A = MathUtil.clamp(st.A + aRate * (aTarget - st.A) * h + 0.55 * sq * rng.nextGaussian(), -15, 15);

            double evap = 0.012 * (1 + Math.max(0, st.A) / 12);
            st.H = MathUtil.clamp(st.H + (evap * (1 - st.H) - 0.09 * st.I * st.H) * h + 0.015 * sq * rng.nextGaussian(), 0.05, 1);

            double iTarget = MathUtil.clamp((st.H - h0) / 0.1 + (0.42 - st.P) * 3, 0, 1);
            if (!st.raining && iTarget < RAIN_ON) iTarget = 0;
            if (force != null) iTarget = force.intensity;
            st.I = MathUtil.clamp(MathUtil.relax(st.I, iTarget, h, 1), 0, 1);
            if (force != null && force.intensity > 0 && st.I < RAIN_ON) st.I = RAIN_ON;

            if (!st.raining && st.I >= RAIN_ON) st.raining = true;
            else if (st.raining && st.I < RAIN_OFF) st.raining = false;
            st.storm = force != null ? force.storm : st.raining && st.I > STORM_INTENSITY && st.P < 0.32 && st.A > -2;

            double windTarget = 2.5 + 22 * Math.abs(st.P - 0.5) + 7 * st.I + (st.storm ? 7 : 0) - (force != null && force.fog ? 3 : 0);
            st.wind = MathUtil.clamp(MathUtil.relax(st.wind, windTarget, h, 0.5) + 0.6 * sq * rng.nextGaussian(), 0, 30);
            st.windDir = (((st.windDir + 25 * sq * rng.nextGaussian()) % 360) + 360) % 360;

            double fogTarget = force != null && force.fog
                ? 1
                : st.raining ? 0 : MathUtil.clamp((st.H - (h0 - 0.04)) / 0.06, 0, 1) * MathUtil.clamp((5 - st.wind) / 3, 0, 1);
            st.fogG = MathUtil.clamp(MathUtil.relax(st.fogG, fogTarget, h, force != null && force.fog ? 0.2 : 1), 0, 1);

            st.dryDays = st.raining ? Math.max(0, st.dryDays - st.I * h * 2) : st.dryDays + h / 24;
            if (force != null && force.drought) st.dryDays = Math.max(st.dryDays, DROUGHT_DAYS);
            st.rainEma = MathUtil.relax(st.rainEma, st.raining ? 1 : 0, h, 72);
        }
    }

    public static String rainClass(double i) {
        return i < MODERATE_INTENSITY ? "LIGHT_RAIN" : i < HEAVY_INTENSITY ? "MODERATE_RAIN" : "HEAVY_RAIN";
    }

    /** Overall condition for display; snow vs rain is decided per region. */
    public static String classify(State st) {
        if (st.storm) return "THUNDERSTORM";
        if (st.raining) return rainClass(st.I);
        if (st.fogG > 0.6) return "FOG";
        if (st.A >= 4.5) return "HEATWAVE";
        if (st.dryDays >= DROUGHT_DAYS) return "DROUGHT";
        if (st.wind >= 12) return "WINDY";
        if (st.H >= 0.75) return "CLOUDY";
        return "CLEAR";
    }

    /** "CLEAR", "RAIN" or "THUNDER": what the vanilla world should show. */
    public static String vanillaWeather(State st) {
        return st.storm ? "THUNDER" : st.raining ? "RAIN" : "CLEAR";
    }

    /** Hold a condition for durationTicks; applied at once. */
    public static void force(State st, String type, long durationTicks) {
        Forceable f = FORCEABLE.get(type);
        if (f == null) throw new IllegalArgumentException("unknown weather type " + type);
        st.forceType = type;
        st.forceUntil = st.tick + Math.max(1, durationTicks);
        if (f.intensity == 0) {
            st.I = 0;
            st.raining = false;
            st.storm = false;
        } else {
            st.I = Math.max(st.I, RAIN_ON);
            st.raining = true;
            st.storm = f.storm;
        }
        if (f.fog) st.fogG = Math.max(st.fogG, 0.9);
    }

    public static void release(State st) {
        st.forceType = null;
    }

    private static final String[] DIRS = {"N", "NE", "E", "SE", "S", "SW", "W", "NW"};

    /** Compass name of the direction the wind comes from. */
    public static String compass(double deg) {
        return DIRS[(int) (Math.round((((deg % 360) + 360) % 360) / 45.0) % 8)];
    }

    /** One forecast period. */
    public static final class Period {
        public final int fromHour;
        public final int toHour;
        public final int rainChance;
        public final int stormChance;
        public final double maxIntensity;
        public final double anomalyMin;
        public final double anomalyMax;
        public final double maxWind;

        Period(int from, int to, int rain, int storm, double maxI, double aMin, double aMax, double wind) {
            fromHour = from;
            toHour = to;
            rainChance = rain;
            stormChance = storm;
            maxIntensity = maxI;
            anomalyMin = aMin;
            anomalyMax = aMax;
            maxWind = wind;
        }
    }

    /**
     * Ensemble forecast: replay the model from copies of the current state.
     * Member 0 keeps the real random sequence (the future as it will happen if
     * nothing intervenes); the others use perturbed sequences for uncertainty.
     */
    public static List<Period> forecast(State st, int hours, int periodHours, int members, String rainFrequency) {
        int periods = (int) Math.ceil(hours / (double) periodHours);
        int[] rain = new int[periods];
        int[] storm = new int[periods];
        double[] maxI = new double[periods];
        double[] minA = new double[periods];
        double[] maxA = new double[periods];
        double[] maxW = new double[periods];
        Arrays.fill(minA, Double.POSITIVE_INFINITY);
        Arrays.fill(maxA, Double.NEGATIVE_INFINITY);
        int stepsPerPeriod = periodHours * TICKS_PER_HOUR / MAX_STEP_TICKS;
        for (int m = 0; m < members; m++) {
            State s = st.copy();
            if (m > 0) s.rng.setState(s.rng.state() ^ (m * 0x9e3779b1));
            for (int p = 0; p < periods; p++) {
                boolean rained = false;
                boolean stormed = false;
                for (int k = 0; k < stepsPerPeriod; k++) {
                    step(s, MAX_STEP_TICKS, rainFrequency);
                    rained |= s.raining;
                    stormed |= s.storm;
                    maxI[p] = Math.max(maxI[p], s.I);
                    minA[p] = Math.min(minA[p], s.A);
                    maxA[p] = Math.max(maxA[p], s.A);
                    maxW[p] = Math.max(maxW[p], s.wind);
                }
                if (rained) rain[p]++;
                if (stormed) storm[p]++;
            }
        }
        List<Period> out = new ArrayList<>();
        for (int p = 0; p < periods; p++) {
            out.add(new Period(p * periodHours, (p + 1) * periodHours,
                (int) Math.round(rain[p] * 100.0 / members), (int) Math.round(storm[p] * 100.0 / members),
                MathUtil.round(maxI[p], 2), MathUtil.round(minA[p], 1), MathUtil.round(maxA[p], 1), MathUtil.round(maxW[p], 1)));
        }
        return out;
    }
}
