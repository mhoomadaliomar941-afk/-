package com.realisticworld.weather;

import com.realisticworld.util.MathUtil;

/**
 * Regional climate (pure). Temperature = biome base (from the biome's own
 * temperature value) - elevation cooling + day/night swing (larger in dry
 * biomes, smaller over water and under clouds) + global anomaly - rain cooling.
 * Soil moisture follows dSM/dt = gain(1-SM) - loss*SM + k(SM0-SM), whose exact
 * solution is used both for normal steps and to catch up a region that was
 * away, so a long gap costs one calculation.
 */
public final class Climate {
    public static final int SEA_LEVEL = 64;
    /** °C lost per block above sea level (exaggerated so mountains matter). */
    public static final double LAPSE_PER_BLOCK = 0.06;

    private static final double[][] TEMP_POINTS = {
        {-0.7, -20}, {0, -8}, {0.15, 0}, {0.5, 9}, {0.8, 15}, {1.2, 24}, {2.0, 32},
    };

    private Climate() {}

    /** Biome temperature value (vanilla scale) to daily mean °C at sea level. */
    public static double vanillaToCelsius(double t) {
        if (t <= TEMP_POINTS[0][0]) return TEMP_POINTS[0][1];
        for (int i = 1; i < TEMP_POINTS.length; i++) {
            if (t <= TEMP_POINTS[i][0]) {
                double t0 = TEMP_POINTS[i - 1][0];
                double c0 = TEMP_POINTS[i - 1][1];
                return c0 + (t - t0) / (TEMP_POINTS[i][0] - t0) * (TEMP_POINTS[i][1] - c0);
            }
        }
        return TEMP_POINTS[TEMP_POINTS.length - 1][1];
    }

    /** Climate parameters of a biome, built from the game's biome values. */
    public static final class BiomeInfo {
        public final String id;
        public final double temp;
        public final double downfall;
        public final boolean water;
        public final double baseC;
        public final double amplitude;
        public final double drain;
        public final double groundWater;

        public BiomeInfo(String id, double temp, double downfall, boolean water) {
            this.id = id;
            this.temp = temp;
            this.downfall = downfall;
            this.water = water;
            boolean sandy = id.contains("desert") || id.contains("mesa") || id.contains("badlands") || id.contains("beach") || (downfall == 0 && temp >= 1.2);
            boolean swampy = id.contains("swamp") || id.contains("mangrove");
            boolean tropical = temp >= 0.9 && downfall >= 0.8; // jungles are hotter than their value suggests
            this.baseC = vanillaToCelsius(temp) + (tropical ? 7 : 0);
            this.amplitude = water ? 2 : 3 + 5 * (1 - downfall);
            this.drain = sandy ? 2.5 : swampy || water ? 0.5 : 1;
            this.groundWater = MathUtil.clamp(0.15 + 0.6 * downfall, 0, 1);
        }

        /** Used when a biome is unknown: plains values. */
        public static BiomeInfo fallback(String id) {
            return new BiomeInfo(id, 0.8, 0.4, false);
        }
    }

    public static double meanTemperature(BiomeInfo b, int elevY, WeatherModel.State w) {
        return b.baseC - Math.max(0, elevY - SEA_LEVEL) * LAPSE_PER_BLOCK + w.A - (w.raining ? 4 * w.I : 0);
    }

    /** Air temperature at a time of day (0..24000, 6000 = noon, warmest ~8000). */
    public static double temperatureAt(BiomeInfo b, int elevY, long timeOfDay, WeatherModel.State w) {
        double clouds = 1 - 0.5 * MathUtil.clamp((w.H - 0.6) / 0.4, 0, 1);
        double amp = b.amplitude * (w.raining ? 1 - 0.6 * w.I : 1) * clouds;
        double diurnal = amp * Math.cos(2 * Math.PI * ((timeOfDay % 24000) - 8000) / 24000.0);
        return meanTemperature(b, elevY, w) + diurnal;
    }

    /** "none", "rain", "sleet", "snow" or "blizzard". */
    public static String precipitation(double tempC, WeatherModel.State w) {
        if (!w.raining) return "none";
        if (tempC <= 0) return w.I >= WeatherModel.HEAVY_INTENSITY && w.wind >= 10 ? "blizzard" : "snow";
        if (tempC < 2) return "sleet";
        return "rain";
    }

    /** Soil coefficients per in-game hour. */
    public static final class SoilRates {
        public final double gain;
        public final double loss;
        public final double k;
        public final double sm0;

        public SoilRates(double gain, double loss, double k, double sm0) {
            this.gain = gain;
            this.loss = loss;
            this.k = k;
            this.sm0 = sm0;
        }
    }

    public static SoilRates soilRates(BiomeInfo b, double tempC, boolean raining, double intensity, double humidity) {
        boolean frozen = tempC <= 0;
        return new SoilRates(
            raining && !frozen ? 0.5 * intensity : 0,
            frozen ? 0 : 0.02 * b.drain * (Math.max(0, tempC) / 20) * (1.1 - humidity),
            0.004,
            b.groundWater);
    }

    /** Exact solution of the soil equation over `hours` with constant rates. */
    public static double stepSoil(double sm, SoilRates r, double hours) {
        double a = r.gain + r.k * r.sm0;
        double b = r.gain + r.loss + r.k;
        if (b <= 0 || hours <= 0) return sm;
        double eq = a / b;
        return MathUtil.clamp(eq + (sm - eq) * Math.exp(-b * hours), 0, 1);
    }

    /** Cheap catch-up after a gap: rain for the recent share of time at a typical intensity. */
    public static double catchUpSoil(double sm, BiomeInfo b, int elevY, WeatherModel.State w, double hours) {
        boolean raining = w.raining;
        w.raining = false;
        double tMean = meanTemperature(b, elevY, w);
        w.raining = raining;
        return stepSoil(sm, soilRates(b, tMean, true, 0.25 * w.rainEma, w.H), hours);
    }

    /** Fog to show at a place: null, "mist", "dense", "rain", "blizzard" or "haze". */
    public static String localFog(BiomeInfo b, long timeOfDay, double tempC, WeatherModel.State w) {
        String precip = precipitation(tempC, w);
        if ("blizzard".equals(precip)) return "blizzard";
        if (w.raining && w.I >= WeatherModel.HEAVY_INTENSITY) return "rain";
        long t = timeOfDay % 24000;
        boolean dawn = t >= 22500 || t <= 1500;
        boolean calm = w.wind < 4;
        double score = w.fogG + (b.water ? 0.25 : 0) + (dawn && calm ? 0.35 : 0) + (w.H > 0.75 ? 0.15 : 0) - Math.max(0, (w.wind - 6) / 10);
        if (w.raining) score -= 0.2;
        score = MathUtil.clamp(score, 0, 1);
        if (score >= 0.85) return "dense";
        if (score >= 0.5) return "mist";
        boolean day = t >= 2000 && t <= 11000;
        if (day && tempC >= 30 && !w.raining && (w.A >= 4.5 || w.dryDays >= 4)) return "haze";
        return null;
    }
}
