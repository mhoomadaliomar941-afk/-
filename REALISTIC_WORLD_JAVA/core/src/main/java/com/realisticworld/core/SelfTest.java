package com.realisticworld.core;

import com.realisticworld.util.Rng;
import com.realisticworld.water.Bodies;
import com.realisticworld.water.TankScan;
import com.realisticworld.water.WaterPhysics;
import com.realisticworld.weather.Climate;
import com.realisticworld.weather.WeatherModel;

import java.util.ArrayList;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;

/**
 * In-game self-test (/rw selftest and the control center): checks on the actual
 * machine that saving, settings validation, the weather and water math, and
 * the engine loop work. The same code runs in the unit tests.
 */
public final class SelfTest {
    public static final class Result {
        public final String name;
        public final boolean ok;
        public final String detail;

        Result(String name, boolean ok, String detail) {
            this.name = name;
            this.ok = ok;
            this.detail = detail;
        }
    }

    private interface Check {
        String run();
    }

    private SelfTest() {}

    private static void check(List<Result> out, String name, Check c) {
        try {
            out.add(new Result(name, true, c.run()));
        } catch (RuntimeException | AssertionError e) {
            out.add(new Result(name, false, String.valueOf(e.getMessage())));
        }
    }

    private static void ensure(boolean cond, String msg) {
        if (!cond) throw new IllegalStateException(msg);
    }

    public static List<Result> run(Scheduler scheduler, Long tickAtLastCheck) {
        List<Result> out = new ArrayList<>();

        check(out, "Saved data round trip", () -> {
            DataStore s = new DataStore();
            Map<String, Object> m = new LinkedHashMap<>();
            StringBuilder big = new StringBuilder();
            for (int i = 0; i < 25000; i++) big.append('x');
            m.put("text", big.toString());
            m.put("n", 42);
            s.save("k", m);
            Map<String, Object> back = s.load("k");
            ensure(back.get("text").toString().length() == 25000 && ((Number) back.get("n")).intValue() == 42, "data changed after loading");
            DataStore copy = new DataStore();
            copy.restore(s.snapshot());
            ensure(copy.load("k").equals(back), "snapshot/restore changed the data");
            return "25,000 characters saved and read back";
        });

        check(out, "Settings validation", () -> {
            Config c = new Config(new RwLog((l, x) -> {}));
            ensure(!c.set(Config.PROFILE, "TURBO").ok, "accepted an invalid profile");
            ensure(c.set(Config.PROFILE, "LOW").ok, "rejected a valid profile");
            ensure(!c.set(Config.ENABLED, "yes").ok, "accepted a non-boolean");
            return "invalid values rejected, valid values kept";
        });

        check(out, "Weather model and climate math", () -> {
            WeatherModel.State st = WeatherModel.create(new Rng(12345));
            WeatherModel.step(st, 24000, "NORMAL");
            ensure(!Double.isNaN(st.P + st.H + st.I + st.A + st.wind), "model produced an invalid number");
            WeatherModel.force(st, "THUNDERSTORM", 2000);
            WeatherModel.step(st, 500, "NORMAL");
            ensure("THUNDERSTORM".equals(WeatherModel.classify(st)) && "THUNDER".equals(WeatherModel.vanillaWeather(st)), "forced storm not applied");
            WeatherModel.State calm = WeatherModel.create(new Rng(1));
            double desert = Climate.temperatureAt(new Climate.BiomeInfo("minecraft:desert", 2.0, 0.0, false), 64, 8000, calm);
            double ice = Climate.temperatureAt(new Climate.BiomeInfo("minecraft:ice_flats", 0.0, 0.5, false), 64, 8000, calm);
            ensure(desert > 30 && ice < 0, "unexpected temperatures: desert " + desert + ", ice plains " + ice);
            return String.format(java.util.Locale.ROOT, "desert %.1f°C, ice plains %.1f°C at mid-afternoon", desert, ice);
        });

        check(out, "Water physics (virtual 3x3 tank with a hole)", () -> {
            TankScan.CellFn cell = (x, y, z) -> {
                if (y <= 0) return TankScan.Cell.SOLID;
                boolean inside = x >= 0 && x <= 2 && z >= 0 && z <= 2;
                if (inside) return y <= 3 ? TankScan.Cell.SOURCE : TankScan.Cell.AIR;
                if (x == -1 && y == 2 && z == 1) return TankScan.Cell.AIR;
                return y <= 4 && x >= -1 && x <= 3 && z >= -1 && z <= 3 ? TankScan.Cell.SOLID : TankScan.Cell.AIR;
            };
            TankScan scan = new TankScan(cell, 1, 2, 1);
            while (!scan.step(8)) { }
            TankScan.Shape s = scan.result();
            ensure(scan.status() == TankScan.Status.DONE && s.cells == 27 && s.openings.size() == 1, "scan found " + s.cells + " cells, " + s.openings.size() + " openings");
            Bodies.Tank t = new Bodies.Tank();
            t.id = "test";
            t.baseY = s.baseY;
            t.layers = s.layers;
            t.columns = s.columns;
            t.openings = s.openings;
            t.volume = 27;
            Bodies.Env env = new Bodies.Env(0, 1, 0, false, 0, 1);
            double out2 = 0;
            for (int i = 0; i < 120; i++) for (double v : Bodies.stepTank(t, 1, env).pours.values()) out2 += v;
            ensure(Math.abs(t.level() - 2) < 1e-6, "level " + t.level() + ", expected 2 (the hole)");
            ensure(Math.abs(out2 - 18) < 1e-6 && Math.abs(t.volume - 9) < 1e-6, "leaked " + out2 + " m³, kept " + t.volume + " m³");
            ensure(WaterPhysics.capacity(t.layers) == 36, "capacity should include the space inside the walls");
            return "18 m³ leaked, level stopped at the hole, 9 m³ kept";
        });

        check(out, "Engine loop is ticking", () -> {
            long now = scheduler.ticks();
            if (tickAtLastCheck != null) ensure(now > tickAtLastCheck, "engine tick did not advance (" + now + ")");
            List<String> faulted = new ArrayList<>();
            int running = 0;
            for (Scheduler.Info i : scheduler.list()) {
                if (i.faulted) faulted.add(i.id);
                if (i.enabled) running++;
            }
            ensure(faulted.isEmpty(), "faulted systems: " + faulted);
            return "tick " + now + ", " + running + " systems running";
        });
        return out;
    }
}
